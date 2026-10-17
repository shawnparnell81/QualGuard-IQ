namespace QualGuardIQ.Domain.Security;

/// <summary>
/// Permission catalog. One edit permission covers a whole module; there are no per-form permissions.
/// Roles reference these codes as data.
/// </summary>
public static class Permissions
{
    public const string AdminUsers = "admin.users";
    public const string AdminRoles = "admin.roles";
    public const string AdminOrganization = "admin.organization";
    public const string AuditTrailView = "audittrail.view";
    public const string SecurityLogView = "securitylog.view";

    public const string DocumentsView = "documents.view";
    public const string DocumentsEdit = "documents.edit";
    public const string DocumentsApprove = "documents.approve";
    public const string DrawingsView = "drawings.view";
    public const string DrawingsEdit = "drawings.edit";
    public const string DrawingsApprove = "drawings.approve";
    public const string FormsView = "forms.view";
    public const string FormsEdit = "forms.edit";
    public const string FormsApprove = "forms.approve";
    public const string NcrView = "ncr.view";
    public const string NcrEdit = "ncr.edit";
    public const string NcrApprove = "ncr.approve";
    public const string CapaView = "capa.view";
    public const string CapaEdit = "capa.edit";
    public const string CapaApprove = "capa.approve";
    public const string AuditsView = "audits.view";
    public const string AuditsEdit = "audits.edit";
    public const string AuditsApprove = "audits.approve";
    public const string TrainingView = "training.view";
    public const string TrainingEdit = "training.edit";
    public const string CalibrationView = "calibration.view";
    public const string CalibrationEdit = "calibration.edit";
    public const string CalibrationApprove = "calibration.approve";
    public const string MasterDataView = "masterdata.view";
    public const string MasterDataEdit = "masterdata.edit";
    public const string SuppliersView = "suppliers.view";
    public const string SuppliersEdit = "suppliers.edit";
    public const string SuppliersApprove = "suppliers.approve";
    /// <summary>Use a restricted (suspended, disqualified, unapproved, expired) supplier with a recorded reason.</summary>
    public const string SuppliersOverride = "suppliers.override";
    public const string ComplaintsView = "complaints.view";
    public const string ComplaintsEdit = "complaints.edit";
    /// <summary>Verify complaint corrective action and reopen closed complaints.</summary>
    public const string ComplaintsApprove = "complaints.approve";
    public const string InspectionView = "inspection.view";
    public const string InspectionEdit = "inspection.edit";
    /// <summary>Approve inspection plan revisions and make release/rejection decisions.</summary>
    public const string InspectionApprove = "inspection.approve";
    public const string ChangesView = "changes.view";
    public const string ChangesEdit = "changes.edit";
    /// <summary>Approve changes, waive affected items, verify implementation and reopen.</summary>
    public const string ChangesApprove = "changes.approve";
    /// <summary>Record maintenance, repairs, breakdowns and qualification on equipment (viewing equipment needs calibration.view).</summary>
    public const string MaintenanceEdit = "maintenance.edit";
    public const string ObjectivesView = "objectives.view";
    public const string ObjectivesEdit = "objectives.edit";
    /// <summary>Approve quality objectives and their targets.</summary>
    public const string ObjectivesApprove = "objectives.approve";
    public const string ReviewsView = "reviews.view";
    public const string ReviewsEdit = "reviews.edit";
    /// <summary>Approve management review records.</summary>
    public const string ReviewsApprove = "reviews.approve";
    public const string RisksView = "risks.view";
    public const string RisksEdit = "risks.edit";
    /// <summary>Accept residual risk, reopen risks.</summary>
    public const string RisksApprove = "risks.approve";

    /// <summary>Run reports and dashboards (each report also needs view on its source module).</summary>
    public const string ReportsView = "reports.view";
    /// <summary>Export report results to CSV/PDF; every export is written to the audit trail.</summary>
    public const string ReportsExport = "reports.export";
    /// <summary>See overdue actions across all modules in scope.</summary>
    public const string ActionsView = "actions.view";

    public const string FaiView = "fai.view";
    public const string FaiEdit = "fai.edit";
    /// <summary>Sign FAI approval steps (Quality, Engineering, Manufacturing steps are configured on the FAI route).</summary>
    public const string FaiApprove = "fai.approve";

    public static readonly IReadOnlyList<string> All = typeof(Permissions)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToArray();

    /// <summary>Permissions that only make sense organization-wide (never site-scoped).</summary>
    public static readonly IReadOnlySet<string> GlobalOnly = new HashSet<string>
    {
        AdminUsers, AdminRoles, AdminOrganization, SecurityLogView
    };
}
