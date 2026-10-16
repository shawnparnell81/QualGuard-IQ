using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace QualGuardIQ.Infrastructure.Identity;

/// <summary>Adds the display name so audit entries read as a person's name rather than a sign-in address.</summary>
public sealed class AppClaimsFactory(UserManager<ApplicationUser> users, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser>(users, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var id = await base.GenerateClaimsAsync(user);
        id.AddClaim(new Claim("display_name", user.DisplayName));
        return id;
    }
}
