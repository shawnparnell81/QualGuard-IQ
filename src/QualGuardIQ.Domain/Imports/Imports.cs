using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Imports;

/// <summary>Import job states (TECH 1). Every transition is written to the import history and the audit trail.</summary>
public enum ImportJobStatus
{
    Uploaded = 0, SecurityScanPending = 1, SecurityScanFailed = 2, Inspecting = 3, Parsing = 4, MappingRequired = 5, PreviewReady = 6,
    ValidationFailed = 7, AwaitingApproval = 8, Importing = 9, Imported = 10, PartiallyImported = 11, CompletedWithWarnings = 12,
    Failed = 13, Canceled = 14, RolledBack = 15, Archived = 16,
}

public enum ImportIssueSeverity { Info = 0, Warning = 1, Error = 2 }

/// <summary>An import of one uploaded file (TECH 2 "Import job").</summary>
public class ImportJob : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    /// <summary>Preserved original file (never modified).</summary>
    public Guid SourceFileId { get; set; }
    public string OriginalFileName { get; set; } = "";
    /// <summary>csv, xlsx, xls, xlsm, docx, doc, pdf, image.</summary>
    public string FileType { get; set; } = "";
    public long FileSize { get; set; }
    public string FileHash { get; set; } = "";
    /// <summary>records (data rows into a module), template (file becomes a form template), document (controlled document), master-list.</summary>
    public string ImportMode { get; set; } = "records";
    public string? TargetModule { get; set; }
    public Guid? TargetTemplateId { get; set; }
    public ImportJobStatus Status { get; set; }
    public int Progress { get; set; }
    public Guid SubmittedById { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }
    public string? BackgroundJobId { get; set; }
    /// <summary>Parser options chosen or detected (encoding, delimiter, header row, date format, decimal separator, empty values).</summary>
    public string Settings { get; set; } = "{}";
    /// <summary>Inspection and analysis result (columns, inferred types, samples, structure).</summary>
    public string Analysis { get; set; } = "{}";
    /// <summary>Dry run / import summary.</summary>
    public string Summary { get; set; } = "{}";
    public int TotalRows { get; set; }
    public int ImportedRows { get; set; }
    public Guid? ApprovedById { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    /// <summary>Set while a background worker owns the job, so two workers never process it at once.</summary>
    public DateTimeOffset? LeasedUntil { get; set; }
    public string AuditLabel => $"import {Number}";
}

/// <summary>Source column / cell / field to target field mapping (TECH 2 "Import mapping").</summary>
public class ImportMapping : Entity
{
    public Guid ImportJobId { get; set; }
    public string SourceLocation { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string SourceType { get; set; } = "text";
    public string? TargetField { get; set; }
    public string? TargetType { get; set; }
    /// <summary>trim, upper, lower, date:FORMAT, map:{json}, default — applied in order.</summary>
    public string? TransformationRule { get; set; }
    public Documents.MappingConfidence MappingConfidence { get; set; }
    public bool UserConfirmed { get; set; }
    public string? DefaultValue { get; set; }
    public bool RequiredValue { get; set; }
    public string ValidationStatus { get; set; } = "Pending";
    public int Sequence { get; set; }
}

/// <summary>A problem found while inspecting, mapping, validating or importing (TECH 2 "Import issue").</summary>
public class ImportIssue : Entity
{
    public Guid ImportJobId { get; set; }
    public string SourceLocation { get; set; } = "";
    public int? RowNumber { get; set; }
    public ImportIssueSeverity Severity { get; set; }
    public string IssueCode { get; set; } = "";
    public string Description { get; set; } = "";
    public string? SuggestedResolution { get; set; }
    public string? UserResolution { get; set; }
    public Guid? ResolvedById { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

/// <summary>Append-only import history (TECH 2 "Import history").</summary>
public class ImportHistoryEvent : IAppendOnly
{
    public long Id { get; set; }
    public Guid ImportJobId { get; set; }
    public string Event { get; set; } = "";
    public Guid? ActorId { get; set; }
    public string ActorName { get; set; } = "";
    public DateTimeOffset EventDate { get; set; }
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }
    public string? Message { get; set; }
    public string? CorrelationId { get; set; }
}

/// <summary>A record created by an import, kept so the import can be rolled back.</summary>
public class ImportedItem
{
    public long Id { get; set; }
    public Guid ImportJobId { get; set; }
    public int RowNumber { get; set; }
    public string TargetType { get; set; } = "";
    public Guid TargetId { get; set; }
    public bool RolledBack { get; set; }
}
