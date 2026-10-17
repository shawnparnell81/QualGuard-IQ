using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Quality;

public enum ShippingDocumentKind { BillOfLading = 0, PackingList = 1, ShippingManifest = 2 }
/// <summary>Draft (editable) → PendingApproval → Approved (immutable; effective from its effective date until a later approved revision takes over) or Rejected.</summary>
public enum TemplateRevisionStatus { Draft = 0, PendingApproval = 1, Approved = 2, Rejected = 3 }

/// <summary>
/// A designed layout for a bill of lading, packing list or shipping manifest. The template holds identity and the owning site (none =
/// every site); the content, the selection rule (customer, carrier, priority) and the logo live on revisions, so every change is approved.
/// </summary>
public class ShippingDocumentTemplate : Entity, IAudited
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public ShippingDocumentKind Kind { get; set; }
    public string? Description { get; set; }
    /// <summary>Null = organization-wide (editing needs the permission at every site).</summary>
    public Guid? SiteId { get; set; }
    public bool Active { get; set; } = true;
    public string AuditLabel => $"shipping document template {Code}";
}

public class ShippingDocumentTemplateRevision : Entity, IAudited
{
    public Guid TemplateId { get; set; }
    public int Revision { get; set; }
    public TemplateRevisionStatus Status { get; set; }
    /// <summary>Layout as JSON (page, header, footer, sections). Validated on every save.</summary>
    public string LayoutJson { get; set; } = "{}";
    /// <summary>Optional JPEG logo printed in the header.</summary>
    public byte[]? Logo { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? CarrierId { get; set; }
    /// <summary>1 is tried first among templates with the same specificity.</summary>
    public int Priority { get; set; } = 100;
    public string? ChangeSummary { get; set; }
    public Guid? SubmittedById { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public Guid? ApprovedById { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovalNotes { get; set; }
    public string? RejectedReason { get; set; }
    /// <summary>Requested by the editor; on approval the revision becomes effective at the later of this and the approval time.</summary>
    public DateTimeOffset? EffectiveFrom { get; set; }
    /// <summary>SHA-256 of layout, logo and selection rule, fixed at approval; rendering refuses a revision whose content no longer matches.</summary>
    public string? ContentHash { get; set; }
    public string AuditLabel => $"shipping document template revision {Revision}";
}

/// <summary>
/// Every issued shipping document: which template revision produced it (or the built-in layout), the data snapshot it was rendered from
/// and the PDF itself, so it can be reproduced and compared later. Append-only.
/// </summary>
public class GeneratedShippingDocument : Entity
{
    public ShippingDocumentKind Kind { get; set; }
    public Guid? ShipmentId { get; set; }
    public Guid SiteId { get; set; }
    /// <summary>Shipment number, BOL number or manifest number.</summary>
    public string Reference { get; set; } = "";
    public Guid? TemplateId { get; set; }
    public Guid? TemplateRevisionId { get; set; }
    public string? TemplateCode { get; set; }
    public int? TemplateRevision { get; set; }
    public bool BuiltIn { get; set; }
    public string DataJson { get; set; } = "{}";
    public byte[] Pdf { get; set; } = [];
    public string PdfSha256 { get; set; } = "";
    public int Pages { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public Guid GeneratedById { get; set; }
}
