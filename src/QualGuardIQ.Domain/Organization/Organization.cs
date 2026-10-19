using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Organization;

/// <summary>Single-row organization and branding configuration. Nothing in source depends on these values.</summary>
public class OrganizationSettings : Entity, IAudited
{
    public string ApplicationName { get; set; } = "";
    public string OrganizationName { get; set; } = "";
    public string? PrimaryColor { get; set; }
    public Guid? LogoAttachmentId { get; set; }
    public string TimeZone { get; set; } = "America/New_York";
    public bool LoginHistoryEnabled { get; set; }
    /// <summary>How dates are shown across the app unless a person picks their own format ("auto" follows the browser).</summary>
    public string DateFormat { get; set; } = "auto";
    /// <summary>Organization-wide switch for email copies of notifications (people can also turn their own off).</summary>
    public bool EmailNotificationsEnabled { get; set; } = true;
    /// <summary>Supplier restriction policy: what happens when a record references a supplier in this condition.</summary>
    public QualGuardIQ.Domain.MasterData.SupplierRestriction SupplierSuspendedPolicy { get; set; } = QualGuardIQ.Domain.MasterData.SupplierRestriction.Block;
    public QualGuardIQ.Domain.MasterData.SupplierRestriction SupplierDisqualifiedPolicy { get; set; } = QualGuardIQ.Domain.MasterData.SupplierRestriction.Block;
    public QualGuardIQ.Domain.MasterData.SupplierRestriction SupplierUnapprovedPolicy { get; set; } = QualGuardIQ.Domain.MasterData.SupplierRestriction.Warn;
    public QualGuardIQ.Domain.MasterData.SupplierRestriction SupplierExpiredCertPolicy { get; set; } = QualGuardIQ.Domain.MasterData.SupplierRestriction.Warn;
    public string AuditLabel => "Organization settings";
}

public class Site : Entity, IAudited, IArchivable
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    /// <summary>IANA time zone for business dates at this site (due, overdue, effective, "today").</summary>
    public string TimeZone { get; set; } = "America/New_York";
    public bool IsArchived { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public List<Department> Departments { get; set; } = [];
    public string AuditLabel => $"Site {Code}";
}

public class Department : Entity, IAudited, IArchivable
{
    public Guid SiteId { get; set; }
    public Site? Site { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsArchived { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public string AuditLabel => $"Department {Code}";
}
