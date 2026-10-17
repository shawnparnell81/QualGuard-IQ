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

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(new Serilog.Formatting.Compact.RenderedCompactJsonFormatter()));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.AddIdentityCore<ApplicationUser>(o =>
    {
        o.Password.RequiredLength = 12;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireLowercase = false;
        o.Password.RequireDigit = false;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        o.Lockout.AllowedForNewUsers = true;
        o.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders()
    .AddClaimsPrincipalFactory<AppClaimsFactory>();

// Browser sessions use the Identity cookie; API clients may instead send "Authorization: Bearer <token>"
// issued by POST /api/auth/token. A policy scheme picks one per request, so the cookie flow is unchanged.
const string AuthSelector = "qms.auth";
builder.Services.AddAuthentication(AuthSelector)
    .AddPolicyScheme(AuthSelector, "Cookie or bearer", o => o.ForwardDefaultSelector = ctx =>
        ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? IdentityConstants.BearerScheme : IdentityConstants.ApplicationScheme)
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
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimits:RequestsPerMinute", 600), Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ErrorHandler>();
builder.Services.AddOpenApi(o => QualGuardIQ.Api.OpenApi.OpenApiSetup.Configure(o, builder.Configuration));
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
builder.Services.Configure<ForwardedHeadersOptions>(o => o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<Seeder>().SeedAsync();
}
await app.Services.GetRequiredService<QualGuardIQ.Infrastructure.Storage.IFileStorage>().EnsureReadyAsync(CancellationToken.None);

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
app.UseAuthentication();
app.UseMiddleware<BearerValidation>();
app.UseRateLimiter();

// CSRF: every state-changing API call must carry the antiforgery header.
app.Use(async (ctx, next) =>
{
    // Bearer requests carry no ambient credentials, so CSRF does not apply to them (or to obtaining a token).
    var bearer = ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        || ctx.Request.Path.StartsWithSegments("/api/auth/token");
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

public partial class Program;
