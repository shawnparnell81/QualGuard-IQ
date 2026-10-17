using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Documents;

public enum DocumentKind { Document = 0, Drawing = 1, FormTemplate = 2 }
public enum DocumentStatus { Draft = 0, Active = 1, Obsolete = 2 }
public enum RevisionStatus { Draft = 0, InReview = 1, Approved = 2, Effective = 3, Superseded = 4, Obsolete = 5 }

/// <summary>Controlled document or engineering drawing. Content lives in immutable revisions.</summary>
public class ControlledDocument : Entity, IAudited, ISiteScoped, IArchivable
{
    public DocumentKind Kind { get; set; }
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>Procedure, Work Instruction, Policy, Specification, Form, Manual, Assembly Drawing, Detail Drawing, and so on. Configurable list.</summary>
    public string DocumentType { get; set; } = "";
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid OwnerId { get; set; }
    public DocumentStatus Status { get; set; }
    public int ReviewIntervalMonths { get; set; } = 24;
    public DateOnly? NextReviewDue { get; set; }
    public string? PartNumber { get; set; }
    public string? DrawingSize { get; set; }
    public int? SheetCount { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public List<DocumentRevision> Revisions { get; set; } = [];
    public string AuditLabel => $"{Kind} {Number}";
}

public class DocumentRevision : Entity, IAudited, IContentLock
{
    public Guid DocumentId { get; set; }
    public ControlledDocument? Document { get; set; }
    public string Revision { get; set; } = "";
    public int Sequence { get; set; }
    public RevisionStatus Status { get; set; }
    public string ChangeSummary { get; set; } = "";
    public DateOnly? EffectiveDate { get; set; }
    public DateOnly? SupersededOn { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? ObsoletedAt { get; set; }
    public string? ObsoleteReason { get; set; }
    public bool TrainingRequired { get; set; }
    /// <summary>Form templates only: JSON field definitions. Frozen with the revision.</summary>
    public string? FieldSchema { get; set; }
    public string AuditLabel => $"revision {Revision} of {Document?.Number ?? DocumentId.ToString()}";

    public string StatusProperty => nameof(Status);
    public bool IsLockedState(object? originalStatus) => originalStatus is RevisionStatus s && s >= RevisionStatus.Approved;
    public IReadOnlyCollection<string> MutableWhenLocked { get; } =
        [nameof(Status), nameof(SupersededOn), nameof(ObsoletedAt), nameof(ObsoleteReason)];
}

/// <summary>
/// Governed document type library (one row per kind + name). Seeded types carry a SeedKey so re-seeding never duplicates
/// them or undoes a rename or deactivation; customer-added types have no SeedKey. Inactive types cannot be chosen for new documents.
/// </summary>
public class DocumentTypeDefinition : Entity, IAudited
{
    public DocumentKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? SeedKey { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public string AuditLabel => $"Document type {Name}";
}
