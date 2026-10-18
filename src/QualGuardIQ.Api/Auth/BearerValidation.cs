using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using QualGuardIQ.Infrastructure.Identity;

namespace QualGuardIQ.Api.Auth;

/// <summary>
/// Same session rules as <see cref="CookieValidation"/> for bearer tokens: a disabled user or a changed
/// security stamp (password reset, forced sign-out, role change) or a signed-out session invalidates the token immediately.
/// </summary>
public sealed class BearerValidation(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, UserManager<ApplicationUser> users, QualGuardIQ.Infrastructure.Security.SessionRevocation sessions)
    {
        if (ctx.User.Identity?.IsAuthenticated == true && ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var id = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var stamp = ctx.User.FindFirstValue("AspNet.Identity.SecurityStamp");
            var user = id is null ? null : await users.FindByIdAsync(id);
            if (user is null || !user.IsActive || user.SecurityStamp != stamp || await sessions.RejectReasonAsync(ctx.User) is not null)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsJsonAsync(new { title = "The access token is no longer valid." });
                return;
            }
        }
        await next(ctx);
    }
}
