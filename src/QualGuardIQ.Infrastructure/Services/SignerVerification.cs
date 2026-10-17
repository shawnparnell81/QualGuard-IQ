using Microsoft.AspNetCore.Identity;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Infrastructure.Security;
using QualGuardIQ.Infrastructure.Identity;

namespace QualGuardIQ.Infrastructure.Services;

/// <summary>
/// Re-authentication for electronic signatures: the signer's password, plus the authenticator code when the account has one or the
/// step demands it. Failures count toward account lockout and are written to the security log. Used by approvals and form signatures.
/// </summary>
public sealed class SignerVerification(UserManager<ApplicationUser> users, ISecurityEventLogger security)
{
    public async Task<(ApplicationUser User, string Verification)> VerifyAsync(Guid userId, string? password, string? mfaCode, bool requireMfa, string context, string mfaRequiredMessage)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new ForbiddenException();
        if (!await users.CheckPasswordAsync(user, password ?? ""))
        {
            await users.AccessFailedAsync(user);
            await security.LogAsync("SignatureFailed", false, userId, user.Email, context);
            throw new AppException("Password is incorrect. The signature was not applied.", 401);
        }
        var verification = "password";
        if (user.TwoFactorEnabled || requireMfa)
        {
            if (!user.TwoFactorEnabled) throw new AppException(mfaRequiredMessage, 400);
            var code = (mfaCode ?? "").Replace(" ", "").Replace("-", "");
            if (code.Length == 0 || !await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code))
            {
                await security.LogAsync("SignatureFailed", false, userId, user.Email, $"{context}: MFA code");
                throw new AppException("The authenticator code is incorrect. The signature was not applied.", 401);
            }
            verification = "password+mfa";
        }
        await users.ResetAccessFailedCountAsync(user);
        return (user, verification);
    }
}
