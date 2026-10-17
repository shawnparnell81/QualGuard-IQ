using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Platform;

/// <summary>Platform event bus entry. Internal events are derived from the audit trail and approval engine; external events arrive through the API.</summary>
public class PlatformEvent
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Type { get; set; } = "";
    public string Source { get; set; } = "Internal";
    public string? RecordType { get; set; }
    public Guid? RecordId { get; set; }
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Data { get; set; }
}

/// <summary>Watermarks for the event pump (last audit entry id, last approval completion time).</summary>
public class PlatformCursor
{
    public string Key { get; set; } = "";
    public long Position { get; set; }
    public DateTimeOffset? At { get; set; }
}

public static class PlatformEvents
{
    public const string RecordCreated = "record.created", RecordUpdated = "record.updated", RecordApproved = "record.approved", RecordRejected = "record.rejected",
        WorkflowCompleted = "workflow.completed", WorkflowStepSigned = "workflow.step_signed", NotificationSent = "notification.sent", External = "external";
    public static readonly string[] Subscribable = [RecordCreated, RecordUpdated, RecordApproved, RecordRejected, WorkflowCompleted, WorkflowStepSigned, External];
}

/// <summary>Outbound webhook subscription. The signing secret is stored encrypted and shown once.</summary>
public class WebhookSubscription : Entity, IAudited
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string EncryptedSecret { get; set; } = "";
    public List<string> Events { get; set; } = [];
    public List<string> RecordTypes { get; set; } = [];
    public bool Enabled { get; set; } = true;
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset? LastDeliveredAt { get; set; }
    public string AuditLabel => $"webhook {Name}";
}

public enum DeliveryStatus { Pending = 0, Delivered = 1, Failed = 2, Abandoned = 3 }

public class WebhookDelivery
{
    public long Id { get; set; }
    public Guid SubscriptionId { get; set; }
    public long EventId { get; set; }
    public string EventType { get; set; } = "";
    public DeliveryStatus Status { get; set; }
    public int Attempts { get; set; }
    public int? LastStatusCode { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
}

public enum SavedReportKind { Tabular = 0, Matrix = 1, Kpi = 2, Executive = 3 }

/// <summary>A report built on a catalog report: chosen columns, filters, optional matrix (row × column) layout. Personal unless shared.</summary>
public class SavedReport : Entity, IAudited
{
    public string Name { get; set; } = "";
    public string ReportKey { get; set; } = "";
    public SavedReportKind Kind { get; set; }
    public List<string> Columns { get; set; } = [];
    public Guid? SiteId { get; set; }
    public int? RangeDays { get; set; }
    public string? FilterColumn { get; set; }
    public string? FilterValue { get; set; }
    public string? RowField { get; set; }
    public string? ColumnField { get; set; }
    public string? ValueField { get; set; }
    public bool Shared { get; set; }
    public Guid OwnerId { get; set; }
    public string AuditLabel => $"saved report {Name}";
}

public enum ReportFormat { Csv = 0, Xlsx = 1, Pdf = 2 }

/// <summary>Scheduled distribution of a saved report. Each recipient opens it under their own permissions.</summary>
public class ReportSchedule : Entity, IAudited
{
    public Guid SavedReportId { get; set; }
    public string Cron { get; set; } = "0 7 * * 1";
    public List<Guid> Recipients { get; set; } = [];
    public ReportFormat Format { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    public Guid OwnerId { get; set; }
    public string AuditLabel => $"report schedule {Cron}";
}

public enum WidgetKind { Kpi = 0, Report = 1, Count = 2, Matrix = 3 }

public sealed class DashboardWidget
{
    public string Title { get; set; } = "";
    public WidgetKind Kind { get; set; }
    /// <summary>Objective id (Kpi), saved report id (Report/Matrix) or record type (Count).</summary>
    public string Source { get; set; } = "";
    public int Width { get; set; } = 4;
    public string? Drilldown { get; set; }
}

/// <summary>User-designed dashboard. Personal by default; shared dashboards are visible to everyone, data still follows each viewer's permissions.</summary>
public class Dashboard : Entity, IAudited
{
    public string Name { get; set; } = "";
    public List<DashboardWidget> Widgets { get; set; } = [];
    public bool Shared { get; set; }
    public bool IsHome { get; set; }
    public Guid OwnerId { get; set; }
    public string AuditLabel => $"dashboard {Name}";
}

public class SavedSearch : Entity
{
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
    public string? RecordType { get; set; }
    public Guid OwnerId { get; set; }
}

public enum ResourceKind { Person = 0, Equipment = 1, Facility = 2 }

/// <summary>Planned unavailability of a person, equipment or facility, used for availability planning.</summary>
public class ResourceAbsence : Entity, IAudited
{
    public ResourceKind Kind { get; set; }
    public Guid ResourceId { get; set; }
    public string ResourceName { get; set; } = "";
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public string Reason { get; set; } = "";
    public Guid SiteId { get; set; }
    public string AuditLabel => $"absence {ResourceName} {From:yyyy-MM-dd}";
}
