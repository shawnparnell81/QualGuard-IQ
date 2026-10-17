using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.MasterData;

/// <summary>
/// Shared master records used by every module (NCR, inspection, complaints, change, supplier quality).
/// Each has an owning site for access scope; organization-wide view permission sees all of them.
/// </summary>
public enum MasterStatus { Active = 0, Inactive = 1, Obsolete = 2 }

public class ProductFamily : Entity, IAudited, IArchivable
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Regulatory category the family's products fall under (e.g. medical device class, automotive safety); used by retention policies.</summary>
    public string? RegulatoryCategory { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public string AuditLabel => $"Product family {Code}";
}

public class Product : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public Guid? FamilyId { get; set; }
    public string? Category { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? OwnerId { get; set; }
    public MasterStatus Status { get; set; }
    public string AuditLabel => $"Product {Number}";
}

public enum PartType { Component = 0, Assembly = 1, Material = 2, Subassembly = 3, Consumable = 4, Packaging = 5, Other = 9 }

public class Part : Entity, IAudited, ISiteScoped
{
    public string PartNumber { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public PartType PartType { get; set; }
    public string Revision { get; set; } = "A";
    public string UnitOfMeasure { get; set; } = "EA";
    public Guid? ProductId { get; set; }
    public string? Material { get; set; }
    public bool HasSpecialCharacteristics { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public MasterStatus Status { get; set; }
    public string AuditLabel => $"Part {PartNumber} rev {Revision}";
}

/// <summary>Immutable history of part revisions: a revision change adds a row, it never overwrites one.</summary>
public class PartRevision : Entity, IAudited
{
    public Guid PartId { get; set; }
    public string Revision { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateOnly EffectiveOn { get; set; }
    public string AuditLabel => $"Part revision {Revision}";
}

public class Customer : Entity, IAudited, ISiteScoped
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ContactName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public MasterStatus Status { get; set; }
    public string AuditLabel => $"Customer {Code}";
}
