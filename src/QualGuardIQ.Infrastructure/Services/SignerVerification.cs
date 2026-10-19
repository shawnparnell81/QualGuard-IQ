using Microsoft.AspNetCore.Identity;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Infrastructure.Security;
using QualGuardIQ.Infrastructure.Identity;

namespace QualGuardIQ.Infrastructure.Services;

/// <summary>
/// Re-authentication for electronic signatures: the signer's password, plus the authenticator code when the account has one or the
/// step demands it. Failures count toward account lockout and are written to the security log. Used by approvals, form signatures,
/// release decisions, training sign-offs and project gate sign-offs. The authenticator code is checked the same way sign-in checks it
/// (the app's own two-step key); recovery codes are for getting into the account and never count as a signature.
/// </summary>
public sealed class SignerVerification(UserManager<ApplicationUser> users, ISecurityEventLogger security, SignInSecurityService mfa)
{
    public async Task<(ApplicationUser User, string Verification)> VerifyAsync(Guid userId, string? password, string? mfaCode, bool requireMfa, string context, string mfaRequiredMessage)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new ForbiddenException();
        if (await users.IsLockedOutAsync(user))
        {
            await security.LogAsync("SignatureFailed", false, userId, user.Email, $"{context}: account locked");
            throw new AppException("Your account is temporarily locked after repeated failed attempts. Try again later. The signature was not applied.", 423);
        }
        if (!await users.CheckPasswordAsync(user, password ?? ""))
        {
            await users.AccessFailedAsync(user);
            await security.LogAsync("SignatureFailed", false, userId, user.Email, context);
            throw new AppException("Password is incorrect. The signature was not applied.", 401);
        }
        var verification = "password";
        var enrolled = user.MfaSecret is not null;
        if (enrolled || requireMfa)
        {
            if (!enrolled) throw new AppException(mfaRequiredMessage, 400);
            var code = mfaCode ?? "";
            var digits = new string(code.Where(char.IsDigit).ToArray());
            var looksLikeCode = digits.Length == Totp.Digits && code.Trim().All(c => char.IsDigit(c) || c == ' ' || c == '-');
            if (!looksLikeCode || await mfa.VerifySecondFactorAsync(user, digits) != SecondFactor.AuthenticatorCode)
            {
                await users.AccessFailedAsync(user);
                await security.LogAsync("SignatureFailed", false, userId, user.Email, $"{context}: authenticator code");
                throw new AppException(looksLikeCode || code.Trim().Length == 0
                    ? "The authenticator code is incorrect. The signature was not applied."
                    : "Enter the 6-digit code from your authenticator app. Recovery codes can't be used to sign. The signature was not applied.", 401);
            }
            verification = "password+mfa";
        }
        await users.ResetAccessFailedCountAsync(user);
        return (user, verification);
    }
}
