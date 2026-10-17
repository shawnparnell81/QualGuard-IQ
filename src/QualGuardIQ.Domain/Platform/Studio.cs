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
