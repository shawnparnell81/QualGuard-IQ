using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Security;

namespace QualGuardIQ.Api.Controllers;

/// <summary>Organization sign-in policy (who must use two-step verification, trusted browsers) plus the configured password and lockout rules (read only).</summary>
[ApiController]
[Route("api/admin/sign-in-policy")]
[RequirePermission(Permissions.AdminUsers)]
public sealed class SignInPolicyController(AppDbContext db, IOptions<AuthOptions> options, IClock clock) : ControllerBase
{
    public sealed record PolicyDto(bool MfaRequiredForStaff, bool MfaRequiredForPortal, bool TrustedDevicesEnabled, int TrustedDeviceDays, uint RowVersion);
    public sealed record ViewDto(PolicyDto Policy, object Configuration, IReadOnlyList<RoleRow> Roles, Coverage Coverage);
    public sealed record RoleRow(Guid Id, string Name, bool RequiresMfa, int People);
    public sealed record Coverage(int ActiveStaff, int StaffWithMfa, int ActivePortal, int PortalWithMfa);

    [HttpGet]
    public async Task<ViewDto> Get(CancellationToken ct)
    {
        var p = await db.SignInPolicies.AsNoTracking().FirstOrDefaultAsync(ct) ?? new SignInPolicy();
        var o = options.Value;
        var roles = await db.QmsRoles.AsNoTracking().OrderBy(r => r.Name)
            .Select(r => new RoleRow(r.Id, r.Name, r.RequiresMfa, db.UserRoleAssignments.Where(a => a.RoleId == r.Id).Select(a => a.UserId).Distinct().Count())).ToListAsync(ct);
        var people = await db.Users.AsNoTracking().Where(u => u.IsActive).GroupBy(u => u.PortalKind == null)
            .Select(g => new { Staff = g.Key, Total = g.Count(), Mfa = g.Count(u => u.MfaSecret != null) }).ToListAsync(ct);
        var staff = people.FirstOrDefault(x => x.Staff); var portal = people.FirstOrDefault(x => !x.Staff);
        return new ViewDto(new PolicyDto(p.MfaRequiredForStaff, p.MfaRequiredForPortal, p.TrustedDevicesEnabled, p.TrustedDeviceDays, p.RowVersion),
            new
            {
                o.ChallengeMinutes, o.TrustedDeviceMaxDays, mfaRequiredForAll = o.Mfa.RequiredForAll, o.Mfa.ClockSkewSteps, o.Mfa.RecoveryCodeCount,
                passwordMinLength = Math.Max(8, o.Password.MinLength), passwordMaxLength = o.Password.MaxLength, passwordHistory = o.Password.HistoryCount, blockCommonPasswords = o.Password.BlockCommon,
                commonPasswordListSize = CommonPasswords.Count, lockoutAttempts = Math.Max(3, o.Lockout.MaxFailedAttempts), lockoutMinutes = Math.Max(1, o.Lockout.Minutes),
                resetLinkMinutes = Math.Clamp(o.Reset.TokenMinutes, 10, 120), resetEmailsPerHour = o.Reset.MaxPerHour,
            },
            roles, new Coverage(staff?.Total ?? 0, staff?.Mfa ?? 0, portal?.Total ?? 0, portal?.Mfa ?? 0));
    }

    [HttpPut]
    public async Task<IActionResult> Save(PolicyDto req, CancellationToken ct)
    {
        var max = Math.Max(1, options.Value.TrustedDeviceMaxDays);
        if (req.TrustedDeviceDays < 1 || req.TrustedDeviceDays > max) throw new AppException($"Trusted browser period must be 1 to {max} days (the limit is set in configuration, Auth:TrustedDeviceMaxDays).");
        var p = await db.SignInPolicies.FirstOrDefaultAsync(ct);
        if (p is null) { p = new SignInPolicy { CreatedAt = clock.UtcNow }; db.SignInPolicies.Add(p); }
        else db.Entry(p).Property(x => x.RowVersion).OriginalValue = req.RowVersion;
        p.MfaRequiredForStaff = req.MfaRequiredForStaff; p.MfaRequiredForPortal = req.MfaRequiredForPortal;
        p.TrustedDevicesEnabled = req.TrustedDevicesEnabled; p.TrustedDeviceDays = req.TrustedDeviceDays;
        await db.SaveChangesAsync(ct); // SignInPolicy is audited (before/after values in the audit trail)
        return NoContent();
    }
}
