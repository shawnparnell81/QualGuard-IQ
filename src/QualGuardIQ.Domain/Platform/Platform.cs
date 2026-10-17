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
    /// <summary>Confidential reports are never attached to scheduled deliveries unless the delivery policy allows it.</summary>
    public bool Confidential { get; set; }
    public Guid OwnerId { get; set; }
    public string AuditLabel => $"saved report {Name}";
}

public enum ReportFormat { Csv = 0, Xlsx = 1, Pdf = 2 }

public enum ReportScheduleSource { SavedReport = 0, BoardReport = 1 }
/// <summary>Link (default): recipients open the report under their own permissions. Attachment modes need the delivery policy, reports.export and an audience whose access covers the content.</summary>
public enum ReportDeliveryMode { Link = 0, PdfAttachment = 1, XlsxAttachment = 2, CsvAttachment = 3 }

/// <summary>Scheduled distribution of a saved report or a board report. Runs in its own time zone; recipients are people and role groups.</summary>
public class ReportSchedule : Entity, IAudited
{
    public string? Name { get; set; }
    public ReportScheduleSource Source { get; set; }
    public Guid? SavedReportId { get; set; }
    /// <summary>executive, board or strategic-risk (board report schedules).</summary>
    public string? BoardReportKind { get; set; }
    public string Cron { get; set; } = "0 7 * * 1";
    /// <summary>IANA time zone the cron expression is read in.</summary>
    public string TimeZone { get; set; } = "UTC";
    public List<Guid> Recipients { get; set; } = [];
    /// <summary>Recipient groups: every active member of these roles, checked against the report's permissions on each run.</summary>
    public List<Guid> RecipientRoleIds { get; set; } = [];
    public ReportFormat Format { get; set; }
    public ReportDeliveryMode DeliveryMode { get; set; }
    /// <summary>Board reports: also generate a new draft edition on each run for approval (delivery always uses the latest approved edition).</summary>
    public bool GenerateDraftEdition { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public bool Enabled { get; set; } = true;
    public DateTimeOffset? PausedAt { get; set; }
    public Guid? PausedById { get; set; }
    public string? PauseReason { get; set; }
    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    public string? LastStatus { get; set; }
    /// <summary>Failed attempts of the current run; reset when a run succeeds or gives up.</summary>
    public int FailedAttempts { get; set; }
    public DateTimeOffset? RetryAt { get; set; }
    public Guid OwnerId { get; set; }
    public string AuditLabel => $"report schedule {Name ?? Cron}";
}

public enum DistributionTrigger { Scheduled = 0, Manual = 1, Retry = 2 }
public enum DistributionStatus { Succeeded = 0, PartiallyDelivered = 1, Failed = 2, Skipped = 3 }
public enum DistributionOutcome { Delivered = 0, Excluded = 1 }

/// <summary>One run of a schedule (append-only history).</summary>
public class ReportDistribution
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ScheduleId { get; set; }
    public DistributionTrigger Trigger { get; set; }
    public int Attempt { get; set; }
    public DistributionStatus Status { get; set; }
    public ReportDeliveryMode DeliveryMode { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? TriggeredById { get; set; }
    public int Recipients { get; set; }
    public int Delivered { get; set; }
    public int Excluded { get; set; }
    public string? Error { get; set; }
    public string? Note { get; set; }
    public Guid? EditionId { get; set; }
    public Guid? ArtifactId { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
}

public class ReportDistributionRecipient
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid DistributionId { get; set; }
    public Guid UserId { get; set; }
    public DistributionOutcome Outcome { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset? DownloadedAt { get; set; }
}

/// <summary>An attached report file kept for the policy's retention period, then purged (the record stays).</summary>
public class ReportArtifact
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ScheduleId { get; set; }
    public Guid DistributionId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[]? Data { get; set; }
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public bool Confidential { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? PurgedAt { get; set; }
}

public enum EditionStatus { Draft = 0, Approved = 1, Rejected = 2 }

/// <summary>A frozen board report (snapshot) that can be approved for distribution. Carries the generator's access scope so it is only sent to people who could see the same data.</summary>
public class BoardReportEdition : Entity, IAudited
{
    public string Kind { get; set; } = "";
    public int Version { get; set; }
    public string Title { get; set; } = "";
    public string SnapshotJson { get; set; } = "{}";
    public EditionStatus Status { get; set; }
    public Guid GeneratedById { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public bool ScopeAllSites { get; set; }
    public List<Guid> ScopeSiteIds { get; set; } = [];
    public Guid? ApprovedById { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? Notes { get; set; }
    /// <summary>Board reports are confidential: never attached unless the delivery policy allows confidential attachments.</summary>
    public bool Confidential { get; set; } = true;
    public Guid? ScheduleId { get; set; }
    public string AuditLabel => $"board report {Kind} edition {Version}";
}

/// <summary>Organization policy for attached report delivery (one row). Attachments are off until an administrator enables them.</summary>
public class ReportDeliveryPolicy : Entity, IAudited
{
    public bool AttachmentsEnabled { get; set; }
    public bool AllowPdf { get; set; } = true;
    public bool AllowXlsx { get; set; } = true;
    public bool AllowCsv { get; set; } = true;
    public bool AllowConfidential { get; set; }
    public long MaxAttachmentBytes { get; set; } = 5 * 1024 * 1024;
    public int RetentionDays { get; set; } = 30;
    /// <summary>When set, attachments go only to users whose email is in one of these domains.</summary>
    public List<string> AllowedEmailDomains { get; set; } = [];
    public string AuditLabel => "report delivery policy";
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
