using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Auth;

/// <summary>
/// Keeps external portal accounts inside the portal: a signed-in customer or supplier account may call only /api/portal/* and /api/auth/*
/// (and /api/account for its own password). Portal accounts also hold no staff roles, so this is a second, independent boundary.
/// </summary>
public sealed class PortalBoundary(RequestDelegate next)
{
    static readonly string[] Allowed = ["/api/portal", "/api/auth", "/api/account", "/api/organization/branding"];

    public async Task InvokeAsync(HttpContext ctx, AppDbContext db, IMemoryCache cache)
    {
        if (ctx.User.Identity?.IsAuthenticated == true && ctx.Request.Path.StartsWithSegments("/api")
            && Guid.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            var kind = await cache.GetOrCreateAsync($"portal-kind:{id}", async e =>
            {
                e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
                return await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.PortalKind ?? "").FirstOrDefaultAsync() ?? "";
            });
            if (!string.IsNullOrEmpty(kind) && (!Allowed.Any(p => ctx.Request.Path.StartsWithSegments(p)) || ctx.Request.Path.StartsWithSegments("/api/portal/admin")))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(new { title = "Portal accounts can only use the customer or supplier portal." });
                return;
            }
        }
        await next(ctx);
    }
}
