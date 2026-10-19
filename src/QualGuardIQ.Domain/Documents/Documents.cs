using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Documents;

public enum DocumentKind { Document = 0, Drawing = 1, FormTemplate = 2 }
public enum DocumentStatus { Draft = 0, Active = 1, Obsolete = 2 }
public enum RevisionStatus { Draft = 0, InReview = 1, Approved = 2, Effective = 3, Superseded = 4, Obsolete = 5 }

/// <summary>Controlled document or engineering drawing. Content lives in immutable revisions.</summary>
public class ControlledDocument : Entity, IAudited, ISiteScoped, IArchivable, ISoftDeletable
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

    // ---- Enterprise template catalog (TECH 2 "Template catalog"): category, module, industry pack, description, confidentiality, retention
    public Guid? CategoryId { get; set; }
    /// <summary>QMS module the template belongs to (forms, document-control, calibration, ...).</summary>
    public string? ModuleKey { get; set; }
    /// <summary>Industry pack the template came from (automotive, aerospace, medical-device, electronics, manufacturing).</summary>
    public string? IndustryPack { get; set; }
    public string? Description { get; set; }
    /// <summary>Confidentiality marking: Public, Internal, Confidential or Restricted.</summary>
    public string Confidentiality { get; set; } = "Internal";
    public Guid? RetentionPolicyId { get; set; }
    /// <summary>How the template was created: native, converted from a file, installed from a package, or cloned.</summary>
    public TemplateSource Source { get; set; }
    public Guid? ClonedFromId { get; set; }
    /// <summary>Master document list: the date after which the document may no longer be used, when it has one.</summary>
    public DateOnly? ExpiresOn { get; set; }
    /// <summary>Master document list: who holds controlled copies (departments, locations or roles, as text).</summary>
    public string? Distribution { get; set; }
    /// <summary>Master document list: the process the document belongs to.</summary>
    public string? RelatedProcess { get; set; }
}

public enum TemplateSource { Native = 0, Excel = 1, Word = 2, Csv = 3, Pdf = 4, Package = 5, Clone = 6 }

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

    // ---- Template revision details (TECH 2 "Template revision")
    public TemplateSource SourceType { get; set; }
    /// <summary>Preserved original file (attachment) the revision was converted from.</summary>
    public Guid? SourceFileId { get; set; }
    /// <summary>Native file kept with the revision (for example the workbook a template is maintained in).</summary>
    public Guid? NativeFileId { get; set; }
    /// <summary>Derived preview (regenerated, so it may change after approval).</summary>
    public Guid? PreviewFileId { get; set; }
    /// <summary>Form schema version of <see cref="FieldSchema"/> (1 = field list, 2 = sectioned definition).</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>Version of the print renderer the revision was approved against.</summary>
    public int RenderingVersion { get; set; } = 1;
    public string? ChangeReason { get; set; }
    public DateOnly? ReviewDate { get; set; }
    public Guid? ApprovedById { get; set; }
    public string AuditLabel => $"revision {Revision} of {Document?.Number ?? DocumentId.ToString()}";

    public string StatusProperty => nameof(Status);
    public bool IsLockedState(object? originalStatus) => originalStatus is RevisionStatus s && s >= RevisionStatus.Approved;
    public IReadOnlyCollection<string> MutableWhenLocked { get; } =
        [nameof(Status), nameof(SupersededOn), nameof(ObsoletedAt), nameof(ObsoleteReason), nameof(PreviewFileId)];
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
