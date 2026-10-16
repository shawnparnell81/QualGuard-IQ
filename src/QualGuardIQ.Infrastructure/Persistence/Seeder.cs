using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QualGuardIQ.Domain.Common;
using QualGuardIQ.Domain.Organization;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Identity;

namespace QualGuardIQ.Infrastructure.Persistence;

/// <summary>
/// Seeds configuration only: organization settings, the Administrator role, numbering sequences, and an
/// initial administrator account when one is configured. No sample business records are created.
/// </summary>
public sealed class Seeder(AppDbContext db, UserManager<ApplicationUser> users, IConfiguration config, ILogger<Seeder> log)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var seed = config.GetSection("Seed");
        if (!await db.OrganizationSettings.AnyAsync(ct))
        {
            db.OrganizationSettings.Add(new OrganizationSettings
            {
                ApplicationName = seed["ApplicationName"] ?? "QMS",
                OrganizationName = seed["OrganizationName"] ?? "Organization",
                TimeZone = seed["TimeZone"] ?? "UTC",
                LoginHistoryEnabled = false
            });
        }

        var admin = await db.QmsRoles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.IsSystem && r.Name == "Administrator", ct);
        if (admin is null)
        {
            admin = new Role { Name = "Administrator", Description = "Full access to every module and administration.", IsSystem = true };
            db.QmsRoles.Add(admin);
        }
        foreach (var p in Permissions.All.Where(p => admin.Permissions.All(x => x.Permission != p)))
            admin.Permissions.Add(new RolePermission { RoleId = admin.Id, Permission = p });

        foreach (var (type, prefix) in DefaultSequences)
        {
            if (!await db.NumberSequences.AnyAsync(s => s.RecordType == type && s.SiteId == null, ct))
                db.NumberSequences.Add(new NumberSequence { RecordType = type, Prefix = prefix, Padding = 4, ResetYearly = false });
        }
        foreach (var (type, name, perm) in DefaultRoutes)
        {
            if (!await db.ApprovalRoutes.AnyAsync(r => r.RecordType == type && r.IsActive, ct))
                db.ApprovalRoutes.Add(new ApprovalRoute
                {
                    RecordType = type, Name = name, PreventSelfApproval = true,
                    Steps = [new ApprovalRouteStep { Order = 1, Name = "Approval", RequiredPermission = perm, SignatureMeaning = "Approved" }]
                });
        }
        await db.SaveChangesAsync(ct);

        var email = seed["AdminEmail"];
        var password = seed["AdminPassword"];
        if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password) && await users.FindByEmailAsync(email) is null)
        {
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = seed["AdminName"] ?? "Administrator", CreatedAt = DateTimeOffset.UtcNow };
            var result = await users.CreateAsync(user, password);
            if (!result.Succeeded)
                throw new InvalidOperationException("Initial administrator could not be created: " + string.Join("; ", result.Errors.Select(e => e.Description)));
            db.UserRoleAssignments.Add(new UserRoleAssignment { UserId = user.Id, RoleId = admin.Id });
            await db.SaveChangesAsync(ct);
            log.LogInformation("Initial administrator {Email} created", email);
        }
    }

    /// <summary>Default one-step routes; administrators can add review steps.</summary>
    public static readonly (string Type, string Name, string Permission)[] DefaultRoutes =
    [
        ("DocumentRevision", "Document revision approval", Permissions.DocumentsApprove),
        ("DrawingRevision", "Drawing revision approval", Permissions.DrawingsApprove),
    ];

    public static readonly (string Type, string Prefix)[] DefaultSequences =
    [
        ("Document", "DOC-"), ("Drawing", "DWG-"), ("FormTemplate", "FT-"), ("FormRecord", "FR-"),
        ("Ncr", "NCR-"), ("Capa", "CAPA-"), ("Audit", "AUD-"), ("AuditFinding", "AF-"),
        ("Equipment", "EQ-"), ("CalibrationEvent", "CAL-"), ("Action", "ACT-"), ("TrainingCourse", "TRN-")
    ];
}
