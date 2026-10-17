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
