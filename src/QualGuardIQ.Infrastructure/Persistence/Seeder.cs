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
                TimeZone = seed["TimeZone"] ?? "America/New_York",
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
                    Steps = DefaultRouteSteps.TryGetValue(type, out var names)
                        ? names.Select((n, k) => new ApprovalRouteStep { Order = k + 1, Name = n, RequiredPermission = perm, SignatureMeaning = $"Approved ({n})" }).ToList()
                        : [new ApprovalRouteStep { Order = 1, Name = "Approval", RequiredPermission = perm, SignatureMeaning = "Approved" }]
                });
        }
        // common parcel carriers; LTL and customer-arranged carriers are added by users
        foreach (var (code, name, url) in new[] { ("UPS", "UPS", "https://www.ups.com/track?tracknum={tracking}"), ("FEDEX", "FedEx", "https://www.fedex.com/fedextrack/?trknbr={tracking}"),
            ("DHL", "DHL", "https://www.dhl.com/en/express/tracking.html?AWB={tracking}") })
            if (!await db.Carriers.AnyAsync(c => c.Code == code, ct)) db.Carriers.Add(new Domain.Quality.Carrier { Code = code, Name = name, Kind = Domain.Quality.CarrierKind.Parcel, TrackingUrl = url });
        await db.SaveChangesAsync(ct);
        await new Modules.Fai.AqlService(db, null!).SeedAsync(ct);

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
        ("FormTemplateRevision", "Form template approval", Permissions.FormsApprove),
        ("FormRecord", "Form record review", Permissions.FormsApprove),
        ("Ncr", "NCR disposition approval", Permissions.NcrApprove),
        ("Supplier", "Supplier approval", Permissions.SuppliersApprove),
        ("InspectionPlanRevision", "Inspection plan approval", Permissions.InspectionApprove),
        ("ChangeRequest", "Change approval", Permissions.ChangesApprove),
        ("Risk", "Residual risk acceptance", Permissions.RisksApprove),
        ("QualityObjective", "Quality objective approval", Permissions.ObjectivesApprove),
        ("ManagementReview", "Management review approval", Permissions.ReviewsApprove),
        ("Fai", "First article approval", Permissions.FaiApprove),
        ("ProcessFlow", "Process flow release", Permissions.ApqpApprove),
        ("Pfmea", "PFMEA release", Permissions.ApqpApprove),
        ("ControlPlan", "Control plan release", Permissions.ApqpApprove),
        ("MsaStudy", "MSA study approval", Permissions.MsaApprove),
        ("ReworkOrder", "Rework approval", Permissions.ReworkApprove),
        ("ErpChange", "ERP change approval", Permissions.IntegrationsApprove),
        ("Recall", "Recall closure", Permissions.RecallsApprove),
        ("InventoryHold", "Hold release", Permissions.InventoryApprove),
        ("MrbCase", "MRB disposition", Permissions.MrbApprove),
        ("CycleCount", "Count variance approval", Permissions.InventoryApprove),
    ];

    /// <summary>Multi-step default routes (administrators can change steps and permissions).</summary>
    public static readonly Dictionary<string, string[]> DefaultRouteSteps = new() { ["Fai"] = ["Quality", "Engineering", "Manufacturing"] };

    public static readonly (string Type, string Prefix)[] DefaultSequences =
    [
        ("Document", "DOC-"), ("Drawing", "DWG-"), ("FormTemplate", "FT-"), ("FormRecord", "FR-"),
        ("Ncr", "NCR-"), ("Capa", "CAPA-"), ("Audit", "AUD-"), ("AuditFinding", "AF-"),
        ("Equipment", "EQ-"), ("CalibrationEvent", "CAL-"), ("Action", "ACT-"), ("TrainingCourse", "TRN-"), ("ImpactAssessment", "IA-"), ("SupplierCorrectiveAction", "SCAR-"), ("Complaint", "CC-"), ("InspectionPlan", "IP-"), ("Inspection", "INS-"), ("ChangeRequest", "CHG-"), ("Risk", "RSK-"), ("MaintenanceEvent", "MNT-"), ("QualityObjective", "OBJ-"), ("ManagementReview", "MR-"), ("Fai", "FAI-"), ("AqlInspection", "AQL-"), ("ProcessFlow", "PF-"), ("Pfmea", "PFMEA-"), ("ControlPlan", "CP-"), ("SpcChart", "SPC-"), ("MsaStudy", "MSA-"), ("LpaAudit", "LPA-"), ("SupplierProgram", "SDP-"), ("ScrapEvent", "SCR-"), ("ReworkOrder", "RWK-"), ("ErpChange", "ERPC-"), ("Recall", "RCL-"), ("CustomerReturn", "RMA-"), ("WarrantyClaim", "WC-"), ("WorkOrder", "WO-"), ("Lot", "LOT-"), ("InventoryTransaction", "IT-"), ("Receipt", "RCV-"), ("InventoryHold", "HOLD-"), ("MrbCase", "MRB-"), ("SupplierReturn", "RTV-"), ("CycleCount", "CNT-"), ("StockTransfer", "TRF-"), ("Shipment", "SHP-"), ("ShipmentContainer", "PKG-")
    ];
}
