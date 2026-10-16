using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Shared;

public enum ActionStatus { Open = 0, InProgress = 1, Completed = 2, Verified = 3, Cancelled = 4 }

/// <summary>The one action system for every module: containment, corrective, preventive, audit follow-up, and so on.</summary>
public class ActionItem : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string ActionType { get; set; } = "General";
    public string SourceType { get; set; } = "";
    public Guid SourceId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid AssigneeId { get; set; }
    public DateOnly DueDate { get; set; }
    public ActionStatus Status { get; set; }
    public bool RequiresVerification { get; set; }
    public string? CompletionNotes { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? CompletedById { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public Guid? VerifiedById { get; set; }
    public string? VerificationNotes { get; set; }
    public string AuditLabel => $"action {Number}";
}

public class Notification
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Title { get; set; } = "";
    public string? Body { get; set; }
    public string? RecordType { get; set; }
    public Guid? RecordId { get; set; }
    public string? Link { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public bool EmailRequested { get; set; }
    public DateTimeOffset? EmailSentAt { get; set; }
    public int EmailAttempts { get; set; }
}

public enum CustomFieldType { Text = 0, Number = 1, Date = 2, Boolean = 3, Choice = 4 }

/// <summary>Administrator-defined extra field for a record type.</summary>
public class CustomFieldDefinition : Entity, IAudited
{
    public string RecordType { get; set; } = "";
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public CustomFieldType FieldType { get; set; }
    public bool IsRequired { get; set; }
    public List<string> Options { get; set; } = [];
    public int SortOrder { get; set; }
    public bool IsArchived { get; set; }
    public string AuditLabel => $"custom field {RecordType}.{Key}";
}

public class CustomFieldValues : Entity, IAudited
{
    public string RecordType { get; set; } = "";
    public Guid RecordId { get; set; }
    public Dictionary<string, string?> Values { get; set; } = [];
    public string AuditLabel => $"custom fields of {RecordType}";
}

/// <summary>Permission-aware search entry maintained by each module when its records change.</summary>
public class SearchEntry
{
    public string RecordType { get; set; } = "";
    public Guid RecordId { get; set; }
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Summary { get; set; }
    public string? Status { get; set; }
    public string Url { get; set; } = "";
    public string ViewPermission { get; set; } = "";
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string PermissionSiteKey { get; set; } = "";
    public string? PermissionDepartmentKey { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
