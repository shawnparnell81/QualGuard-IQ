using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Platform;

public enum LibraryStatus { Draft = 0, Published = 1, Retired = 2 }

/// <summary>Reusable form component in the form library (for example "Lot identification" or "Dimensional check"). Versioned; templates copy a published version so later changes never alter released forms.</summary>
public class FormComponent : Entity, IAudited
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "General";
    public string? Description { get; set; }
    public int Version { get; set; } = 1;
    public LibraryStatus Status { get; set; }
    public string FieldsJson { get; set; } = "[]";
    public DateTimeOffset? PublishedAt { get; set; }
    public Guid? PublishedById { get; set; }
    public string AuditLabel => $"form component {Key} v{Version}";
}

public enum RuleKind { Validation = 0, Routing = 1, Escalation = 2, Automation = 3 }

public sealed class RuleCondition
{
    public string Field { get; set; } = "";
    public string Operator { get; set; } = "equals";
    public string? Value { get; set; }
}

/// <summary>
/// No-code business rule. Validation rules block an approval submission; routing rules direct a submission to a permission group with a due date;
/// escalation rules notify a group when an approval waits too long; automation rules react to platform events. All conditions must hold.
/// </summary>
public class BusinessRule : Entity, IAudited
{
    public string Name { get; set; } = "";
    public string RecordType { get; set; } = "";
    public RuleKind Kind { get; set; }
    /// <summary>Automation: platform event type (record.created, record.updated, record.approved, ...).</summary>
    public string? Trigger { get; set; }
    public List<RuleCondition> Conditions { get; set; } = [];
    /// <summary>Validation: the message shown when the rule blocks. Other kinds: the notification text.</summary>
    public string Message { get; set; } = "";
    /// <summary>Routing / escalation / automation: people holding this permission at the record's site are notified.</summary>
    public string? TargetPermission { get; set; }
    public int? DueDays { get; set; }
    public int? AfterHours { get; set; }
    /// <summary>Null applies to every site.</summary>
    public Guid? SiteId { get; set; }
    public int Priority { get; set; } = 100;
    public bool Enabled { get; set; } = true;
    public string AuditLabel => $"business rule {Name}";
}

/// <summary>Log of rule firings (also de-duplicates escalations and automations).</summary>
public class RuleFiring
{
    public long Id { get; set; }
    public Guid RuleId { get; set; }
    public string RecordType { get; set; } = "";
    public Guid RecordId { get; set; }
    public long? EventId { get; set; }
    public Guid? ApprovalRequestId { get; set; }
    public DateTimeOffset FiredAt { get; set; }
    public string Outcome { get; set; } = "";
}

public enum DataClassification { Public = 0, Internal = 1, Confidential = 2, Restricted = 3 }

/// <summary>Governed master-data domain (Product, Part, Supplier, Customer, Site, Equipment) with its steward, owner and classification.</summary>
public class DataDomain : Entity, IAudited
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid? StewardId { get; set; }
    public Guid? OwnerId { get; set; }
    public DataClassification Classification { get; set; } = DataClassification.Internal;
    public string? Description { get; set; }
    public string AuditLabel => $"data domain {Key}";
}

public enum DataRuleType { Required = 0, Unique = 1, Pattern = 2, AllowedValues = 3 }

/// <summary>Data quality rule on one field of a governed domain.</summary>
public class DataQualityRule : Entity, IAudited
{
    public string DomainKey { get; set; } = "";
    public string Field { get; set; } = "";
    public DataRuleType RuleType { get; set; }
    public string? Parameter { get; set; }
    public bool Blocking { get; set; }
    public bool Enabled { get; set; } = true;
    public string AuditLabel => $"data rule {DomainKey}.{Field} {RuleType}";
}

/// <summary>Compliance measurement of a domain at a point in time (trend tracking).</summary>
public class DataQualitySnapshot
{
    public long Id { get; set; }
    public string DomainKey { get; set; } = "";
    public DateTimeOffset RunAt { get; set; }
    public int Records { get; set; }
    public int FailingRecords { get; set; }
    public int Violations { get; set; }
    public decimal Compliance { get; set; }
}

public enum MasterChangeStatus { Draft = 0, PendingApproval = 1, Applied = 2, Rejected = 3 }

/// <summary>Governed change to a master record. Approved through the shared approval engine; the change is applied only on approval.</summary>
public class MasterDataChange : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public string DomainKey { get; set; } = "";
    public Guid RecordId { get; set; }
    public string RecordLabel { get; set; } = "";
    public Dictionary<string, string?> Changes { get; set; } = [];
    public Dictionary<string, string?> Previous { get; set; } = [];
    public string Reason { get; set; } = "";
    public MasterChangeStatus Status { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid RequestedById { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public string AuditLabel => $"master data change {Number}";
}

public enum BackupKind { Database = 0, FileStore = 1, Configuration = 2 }
public enum BackupState { Succeeded = 0, Failed = 1 }
public enum BackupVerification { NotVerified = 0, Passed = 1, Failed = 2 }

/// <summary>A backup taken by the backup tooling (scripts/backup.sh) and registered with the disaster recovery center.</summary>
public class BackupRecord : Entity, IAudited
{
    public BackupKind Kind { get; set; }
    public string Label { get; set; } = "";
    public string Location { get; set; } = "";
    public string Method { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public long SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public BackupState State { get; set; }
    /// <summary>Schema version and row counts captured at backup time; the verify script compares a restored copy against it.</summary>
    public string? Fingerprint { get; set; }
    public BackupVerification Verification { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public string? VerificationNotes { get; set; }
    public string AuditLabel => $"backup {Label}";
}

/// <summary>Recovery objective for a disaster scenario: target recovery time and data loss, and the controlled procedure document that describes the recovery.</summary>
public class RecoveryObjective : Entity, IAudited
{
    public string Scenario { get; set; } = "";
    public int RtoMinutes { get; set; }
    public int RpoMinutes { get; set; }
    public Guid? ProcedureDocumentId { get; set; }
    public string AuditLabel => $"recovery objective {Scenario}";
}

public enum ExtensionKind { AddOn = 0, QualityExtension = 1, IndustryPack = 2 }

/// <summary>
/// A marketplace package version: what it is, what it needs (platform modules as "module:X", other packages by key), whether it needs a license,
/// and the configuration it installs through the existing engines (business rules, data quality rules, form library components).
/// </summary>
public class ExtensionPackage : Entity, IAudited
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public ExtensionKind Kind { get; set; }
    public string Publisher { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Description { get; set; } = "";
    public List<string> DependsOn { get; set; } = [];
    public bool RequiresLicense { get; set; }
    public bool BuiltIn { get; set; }
    public string ContentJson { get; set; } = "{}";
    public string AuditLabel => $"extension package {Key} {Version}";
}

public class InstalledItem
{
    public string Type { get; set; } = "";
    public Guid Id { get; set; }
    public string Label { get; set; } = "";
}

public class InstalledItems { public List<InstalledItem> Items { get; set; } = []; }

/// <summary>An installed package on this installation, with the version installed and the configuration items it created.</summary>
public class ExtensionInstallation : Entity, IAudited
{
    public string Key { get; set; } = "";
    public string Version { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public DateTimeOffset InstalledAt { get; set; }
    public Guid InstalledById { get; set; }
    public InstalledItems Created { get; set; } = new();
    public string AuditLabel => $"extension {Key} {Version}";
}

/// <summary>A signed license activated on this installation. The key itself is not stored, only its SHA-256.</summary>
public class ExtensionLicense : Entity, IAudited
{
    public string LicenseId { get; set; } = "";
    public string Licensee { get; set; } = "";
    public List<string> Extensions { get; set; } = [];
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string KeyHash { get; set; } = "";
    public bool Revoked { get; set; }
    public string AuditLabel => $"license {LicenseId}";
}
