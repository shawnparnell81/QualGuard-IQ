using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Documents;

public enum FormMigrationStatus { Draft = 0, InReview = 1, Approved = 2, Effective = 3, RolledBack = 4 }

/// <summary>
/// Opt-in migration of an existing form template to the current form schema. The original revision is never changed: a snapshot of it is kept,
/// the converted definition goes into a new draft revision that is approved through the normal form approval route, and a comparison
/// of every field is stored. Completed records stay bound to the revision they were filled against. Rollback restores the original definition.
/// </summary>
public class FormMigration : Entity, IAudited
{
    public string Number { get; set; } = "";
    public Guid DocumentId { get; set; }
    public Guid SiteId { get; set; }
    /// <summary>The revision that was migrated (kept unchanged).</summary>
    public Guid SourceRevisionId { get; set; }
    public string SourceRevision { get; set; } = "";
    /// <summary>The new draft revision holding the converted definition.</summary>
    public Guid TargetRevisionId { get; set; }
    public string TargetRevision { get; set; } = "";
    public int FromSchemaVersion { get; set; }
    public int ToSchemaVersion { get; set; }
    public FormMigrationStatus Status { get; set; }
    /// <summary>Migration snapshot: the original definition exactly as stored, its hash, revision and the records filled against it.</summary>
    public string Snapshot { get; set; } = "{}";
    public string SnapshotSha256 { get; set; } = "";
    /// <summary>Field-by-field comparison of the original and converted definitions.</summary>
    public string Comparison { get; set; } = "{}";
    public int FieldCount { get; set; }
    public int ChangedCount { get; set; }
    public int RecordCount { get; set; }
    public DateTimeOffset? RolledBackAt { get; set; }
    public Guid? RolledBackById { get; set; }
    public string? RollbackReason { get; set; }
    /// <summary>When the migrated revision was already approved, rollback starts a new revision restoring the original definition.</summary>
    public Guid? RollbackRevisionId { get; set; }
    public string AuditLabel => $"form migration {Number}";
}
