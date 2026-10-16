using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using QualGuardIQ.Infrastructure.Identity;

namespace QualGuardIQ.Api.Auth;

/// <summary>
/// Validates the session on every request: disabled users and users whose security stamp changed
/// (password reset, forced sign-out, role change) are rejected immediately.
/// </summary>
public static class CookieValidation
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext ctx)
    {
        var users = ctx.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var id = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = ctx.Principal?.FindFirstValue("AspNet.Identity.SecurityStamp");
        var user = id is null ? null : await users.FindByIdAsync(id);
        if (user is null || !user.IsActive || user.SecurityStamp != stamp)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
    }
}
