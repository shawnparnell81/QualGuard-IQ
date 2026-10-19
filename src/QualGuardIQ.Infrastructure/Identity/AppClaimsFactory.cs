using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace QualGuardIQ.Infrastructure.Identity;

/// <summary>
/// Adds the display name so audit entries read as a person's name rather than a sign-in address, plus a session id and the sign-in
/// time so one session can be signed out on the server and every session has a fixed maximum lifetime.
/// </summary>
public sealed class AppClaimsFactory(UserManager<ApplicationUser> users, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser>(users, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var id = await base.GenerateClaimsAsync(user);
        id.AddClaim(new Claim("display_name", user.DisplayName));
        id.AddClaim(new Claim(SessionClaims.SessionId, Guid.NewGuid().ToString("N")));
        id.AddClaim(new Claim(SessionClaims.IssuedAt, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), ClaimValueTypes.Integer64));
        return id;
    }
}

public static class SessionClaims
{
    public const string SessionId = "qms.sid";
    public const string IssuedAt = "qms.iat";
}
