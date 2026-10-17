using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Quality;

public enum FormRecordStatus { Draft = 0, Submitted = 1, Approved = 2, Rejected = 3 }

/// <summary>A completed controlled form. Bound to the exact template revision it was filled against.</summary>
public class FormRecord : Entity, IAudited, ISiteScoped, IContentLock
{
    public string Number { get; set; } = "";
    public Guid TemplateId { get; set; }
    public Guid TemplateRevisionId { get; set; }
    public string TemplateNumber { get; set; } = "";
    public string TemplateRevision { get; set; } = "";
    public string Title { get; set; } = "";
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public FormRecordStatus Status { get; set; }
    public string ValuesJson { get; set; } = "{}";
    public DateTimeOffset? SubmittedAt { get; set; }
    public Guid? SubmittedById { get; set; }
    public Guid? OwnerId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid? RetentionPolicyId { get; set; }
    public string AuditLabel => $"form record {Number}";
    public string StatusProperty => nameof(Status);
    public bool IsLockedState(object? s) => s is FormRecordStatus.Submitted or FormRecordStatus.Approved;
    public IReadOnlyCollection<string> MutableWhenLocked { get; } = [nameof(Status), nameof(ApprovedAt), nameof(ClosedAt)];
}

/// <summary>
/// An electronic signature applied to a signature block inside a form record. Bound to the exact content signed (ContentHash);
/// any later change to the record's values invalidates it. Rows are never deleted: invalidated signatures stay as history.
/// </summary>
public class FormSignature : Entity, IAudited
{
    public Guid FormRecordId { get; set; }
    public string SignatureKey { get; set; } = "";
    public string Label { get; set; } = "";
    public string Role { get; set; } = "";
    public string Meaning { get; set; } = "";
    public Guid SignerId { get; set; }
    public string SignerName { get; set; } = "";
    public DateTimeOffset SignedAt { get; set; }
    /// <summary>"password" or "password+mfa".</summary>
    public string Verification { get; set; } = "password";
    public string? Comment { get; set; }
    /// <summary>SHA-256 of the record's normalized values at signing time.</summary>
    public string ContentHash { get; set; } = "";
    /// <summary>SHA-256 over record, block, signer, meaning, time and content hash; recomputed by integrity checks.</summary>
    public string SignatureHash { get; set; } = "";
    public bool IsInvalidated { get; set; }
    public DateTimeOffset? InvalidatedAt { get; set; }
    public string? InvalidatedReason { get; set; }
    public string AuditLabel => $"signature '{Label}' on form record";
}


/// <summary>
/// Typed copy of one value of a form record (TECH 2 "Completed form value"), kept in step with the record's values for reporting and
/// search. RowKey identifies table rows and repeated section items ("rows/3", "items/1"); scalar fields have none.
/// </summary>
public class FormRecordValue
{
    public long Id { get; set; }
    public Guid FormRecordId { get; set; }
    public Guid FieldId { get; set; }
    public string? RowKey { get; set; }
    /// <summary>text, number, date, boolean, json or file.</summary>
    public string ValueType { get; set; } = "text";
    public string? TextValue { get; set; }
    public decimal? NumericValue { get; set; }
    public DateTimeOffset? DateValue { get; set; }
    public bool? BooleanValue { get; set; }
    public string? JsonValue { get; set; }
    public Guid? FileId { get; set; }
}
