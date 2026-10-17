using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Shared;

public enum ScanStatus { NotScanned = 0, Clean = 1, Infected = 2, Failed = 3 }
public enum Classification { Internal = 0, Confidential = 1, Restricted = 2, Public = 3 }

/// <summary>File stored in object storage, owned by any record. Bytes are never served by public URL.</summary>
public class Attachment : Entity, IAudited, IArchivable, IContentLock
{
    public string StatusProperty => nameof(IsLocked);
    public bool IsLockedState(object? originalStatus) => originalStatus is true;
    public IReadOnlyCollection<string> MutableWhenLocked { get; } = [nameof(RetainUntil), nameof(SiteId), nameof(DepartmentId), nameof(ViewPermission)];

    public string OwnerType { get; set; } = "";
    public Guid OwnerId { get; set; }
    public Guid? SiteId { get; set; }
    /// <summary>Owning record's department and view permission, copied at upload so the content library can filter by scope in the database.</summary>
    public Guid? DepartmentId { get; set; }
    public string? ViewPermission { get; set; }
    public string FileName { get; set; } = "";
    /// <summary>Lower-case extension including the dot (database-generated from FileName).</summary>
    public string? Extension { get; private set; }
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public string StorageKey { get; set; } = "";
    public string? Category { get; set; }
    public string? Description { get; set; }
    public Classification Classification { get; set; }
    /// <summary>Files of the same logical document share a lineage id; Version increments.</summary>
    public Guid LineageId { get; set; }
    public int Version { get; set; } = 1;
    /// <summary>Locked files (for example the content of an approved revision) cannot be replaced or archived.</summary>
    public bool IsLocked { get; set; }
    public ScanStatus ScanStatus { get; set; }
    public DateOnly? RetainUntil { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public string AuditLabel => $"file {FileName} v{Version}";
}

public class Comment : Entity, IAudited
{
    public string OwnerType { get; set; } = "";
    public Guid OwnerId { get; set; }
    public Guid AuthorId { get; set; }
    public string AuthorName { get; set; } = "";
    public string Body { get; set; } = "";
    public string AuditLabel => $"comment on {OwnerType}";
}

/// <summary>Directed link between two records of any type, such as NCR -> CAPA.</summary>
public class RecordLink : Entity, IAudited
{
    public string FromType { get; set; } = "";
    public Guid FromId { get; set; }
    public string ToType { get; set; } = "";
    public Guid ToId { get; set; }
    public string LinkType { get; set; } = "Related";
    public string AuditLabel => $"link {FromType} -> {ToType} ({LinkType})";
}

public class Tag : Entity
{
    public string Name { get; set; } = "";
}

public class RecordTag
{
    public string OwnerType { get; set; } = "";
    public Guid OwnerId { get; set; }
    public Guid TagId { get; set; }
}
