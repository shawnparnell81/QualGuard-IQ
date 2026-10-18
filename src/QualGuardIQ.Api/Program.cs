using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Api.Infrastructure;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Infrastructure;
using QualGuardIQ.Infrastructure.Identity;
using QualGuardIQ.Infrastructure.Persistence;
using Serilog;

if (args.Length > 0 && args[0] == "license")
{
    // Offline license tooling; does not start the web host or touch the database.
    var cli = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: true).AddEnvironmentVariables().Build();
    return QualGuardIQ.Api.Infrastructure.LicenseCommand.Run(args[1..], cli, Console.Out, Console.Error, DateTimeOffset.UtcNow);
}

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(new Serilog.Formatting.Compact.RenderedCompactJsonFormatter()));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<QualGuardIQ.Application.Security.ICallerRestriction, HttpCallerRestriction>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

var authOptions = builder.Configuration.GetSection(QualGuardIQ.Infrastructure.Security.AuthOptions.Section).Get<QualGuardIQ.Infrastructure.Security.AuthOptions>() ?? new();
builder.Services.AddIdentityCore<ApplicationUser>(o =>
    {
        // Password and lockout rules come from configuration (Auth:Password, Auth:Lockout); PolicyPasswordValidator adds the
        // breached/common list, reuse history and email-name checks.
        o.Password.RequiredLength = Math.Max(8, authOptions.Password.MinLength);
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireLowercase = false;
        o.Password.RequireDigit = false;
        o.Lockout.MaxFailedAccessAttempts = Math.Max(3, authOptions.Lockout.MaxFailedAttempts);
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(Math.Max(1, authOptions.Lockout.Minutes));
        o.Lockout.AllowedForNewUsers = true;
        o.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddUserStore<QualGuardIQ.Infrastructure.Security.AppUserStore>()
    .AddPasswordValidator<QualGuardIQ.Infrastructure.Security.PolicyPasswordValidator>()
    .AddDefaultTokenProviders()
    .AddClaimsPrincipalFactory<AppClaimsFactory>();

// Browser sessions use the Identity cookie; API clients may instead send "Authorization: Bearer <token>"
// issued by POST /api/auth/token. A policy scheme picks one per request, so the cookie flow is unchanged.
const string AuthSelector = "qms.auth";
builder.Services.AddAuthentication(AuthSelector)
    .AddPolicyScheme(AuthSelector, "Cookie, bearer or API key", o => o.ForwardDefaultSelector = ctx =>
        ctx.Request.Headers.ContainsKey(ApiKeyHandler.Header) ? ApiKeyHandler.Scheme
        : ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? IdentityConstants.BearerScheme : IdentityConstants.ApplicationScheme)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyHandler>(ApiKeyHandler.Scheme, null)
    .AddBearerToken(IdentityConstants.BearerScheme, o =>
    {
        o.BearerTokenExpiration = TimeSpan.FromMinutes(builder.Configuration.GetValue("Auth:BearerTokenMinutes", 30));
        o.RefreshTokenExpiration = TimeSpan.FromHours(builder.Configuration.GetValue("Auth:RefreshTokenHours", 8));
    })
    .AddIdentityCookies(c => c.ApplicationCookie!.Configure(o =>
    {
        o.Cookie.Name = "qms.session";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing")
            ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
        o.Events.OnValidatePrincipal = CookieValidation.ValidateAsync;
    }));

builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF-TOKEN";
    o.Cookie.Name = "qms.csrf";
    o.Cookie.SameSite = SameSiteMode.Strict;
});

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimits:LoginPerMinute", 10), Window = TimeSpan.FromMinutes(1) }));
    // Expensive processing (upload inspection, conversion, previews, rendering, OCR, migrations): a separate, lower per-caller limit.
    o.AddPolicy("processing", ctx => RateLimitPartition.GetFixedWindowLimiter(
        "proc:" + (ctx.User.FindFirst(ApiKeyHandler.ClientClaim)?.Value ?? ctx.User.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimits:ProcessingPerMinute", 30), Window = TimeSpan.FromMinutes(1) }));
    // Public API clients get their own per-client limit (set on the client); everyone else shares the per-user limit.
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => ctx.User.FindFirst(ApiKeyHandler.ClientClaim)?.Value is { } client
        ? RateLimitPartition.GetFixedWindowLimiter("api:" + client + ":" + ctx.User.FindFirst(ApiKeyHandler.RateClaim)?.Value,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = int.TryParse(ctx.User.FindFirst(ApiKeyHandler.RateClaim)?.Value, out var n) ? n : 120, Window = TimeSpan.FromMinutes(1) })
        : RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimits:RequestsPerMinute", 600), Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ErrorHandler>();
builder.Services.AddOpenApi(o => QualGuardIQ.Api.OpenApi.OpenApiSetup.Configure(o, builder.Configuration));
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
builder.Services.Configure<ForwardedHeadersOptions>(o => o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

builder.Services.AddMemoryCache();
// sends sign-in emails (reset links, change notices) queued by SignInSecurityService
builder.Services.AddHostedService(sp => sp.GetRequiredService<QualGuardIQ.Infrastructure.Security.AuthMailQueue>());
var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<Seeder>().SeedAsync();
    await scope.ServiceProvider.GetRequiredService<QualGuardIQ.Infrastructure.Services.AttachmentService>().BackfillScopeAsync(CancellationToken.None);
}
await app.Services.GetRequiredService<QualGuardIQ.Infrastructure.Storage.IFileStorage>().EnsureReadyAsync(CancellationToken.None);

app.UseCorrelationId();
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.Use(async (ctx, next) =>
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    try { await next(); }
    finally { ctx.RequestServices.GetRequiredService<QualGuardIQ.Infrastructure.Modules.Platform.ApiMetrics>().Record(ctx.Request.Path.Value ?? "/", ctx.Response.StatusCode, sw.Elapsed.TotalMilliseconds); }
});
app.UseSerilogRequestLogging();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    // API versioning: version 1 is current. Clients may pin it with the Api-Version header; unknown versions are refused.
    if (ctx.Request.Path.StartsWithSegments("/api"))
    {
        ctx.Response.Headers["Api-Supported-Versions"] = "1";
        if (ctx.Request.Headers.TryGetValue("Api-Version", out var v) && v.ToString().Trim() is { Length: > 0 } ver && ver != "1" && ver != "1.0")
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(new { title = $"API version '{ver}' is not supported. Supported: 1." });
            return;
        }
    }
    await next();
});
app.UseAuthentication();
app.UseMiddleware<BearerValidation>();
app.UseMiddleware<PortalBoundary>();
app.UseMiddleware<ApiClientGuard>();
app.UseRateLimiter();

// CSRF: every state-changing API call must carry the antiforgery header.
app.Use(async (ctx, next) =>
{
    // Bearer requests carry no ambient credentials, so CSRF does not apply to them (or to obtaining a token).
    var bearer = ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        || ctx.Request.Headers.ContainsKey(ApiKeyHandler.Header)
        || ctx.Request.Path.StartsWithSegments("/api/auth/token") || ctx.Request.Path.StartsWithSegments("/api/oauth/token")
        // gateway delivery receipts: anonymous, authenticated by the provider's callback token, never act as a signed-in user
        || ctx.Request.Path.StartsWithSegments("/api/messaging/callbacks");
    if (!bearer && ctx.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method) && !HttpMethods.IsOptions(ctx.Request.Method))
    {
        var af = ctx.RequestServices.GetRequiredService<IAntiforgery>();
        if (!await af.IsRequestValidAsync(ctx))
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(new { title = "Missing or invalid CSRF token." });
            return;
        }
    }
    await next();
});
app.UseMiddleware<IdempotencyMiddleware>();

app.UseAuthorization();

if (!app.Environment.IsProduction())
{
    app.MapOpenApi().AllowAnonymous();                                  // /openapi/v1.json
    app.MapOpenApi("/openapi/{documentName}.yaml").AllowAnonymous();   // /openapi/v1.yaml
}
app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();
app.MapControllers();

app.Run();
return 0;

public partial class Program;
