namespace QualGuardIQ.Domain.Common;

/// <summary>
/// Records that can be deleted from the screens. Deleting never removes the row: a <see cref="DeletedRecord"/> entry hides it from every
/// list, search and lookup until someone with permission restores it.
/// </summary>
public interface ISoftDeletable { }

/// <summary>
/// One deletion of a record: who deleted it, when and why, and (once restored) who brought it back. Only one open entry may exist per record.
/// The number, title and site are copied so the deleted-records list can show and scope it without loading the hidden record.
/// </summary>
public class DeletedRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string RecordType { get; set; } = "";
    public Guid RecordId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Status { get; set; }
    /// <summary>The permission needed to see this entry and restore the record (the module's delete permission).</summary>
    public string DeletePermission { get; set; } = "";
    /// <summary>True when the record was closed, approved or signed when it was deleted.</summary>
    public bool WasLocked { get; set; }
    public string Reason { get; set; } = "";
    public Guid DeletedById { get; set; }
    public string DeletedByName { get; set; } = "";
    public DateTimeOffset DeletedAt { get; set; }
    public DateTimeOffset? RestoredAt { get; set; }
    public Guid? RestoredById { get; set; }
    public string? RestoredByName { get; set; }
    public string? RestoreReason { get; set; }
}

/// <summary>Why a quality-event record was closed without being worked to the end.</summary>
public enum CloseOutOutcome { NotRequired = 0, NoFaultFound = 1, Duplicate = 2, RaisedInError = 3 }

/// <summary>
/// A live quality-event record (NCR, CAPA, complaint and the like) closed out instead of deleted: who closed it, when, the outcome and
/// the note saying why. The record keeps its history and stays in lists with its closed status.
/// </summary>
public class RecordCloseOut
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string RecordType { get; set; } = "";
    public Guid RecordId { get; set; }
    public Guid SiteId { get; set; }
    public CloseOutOutcome Outcome { get; set; }
    public string Note { get; set; } = "";
    public string? PreviousStatus { get; set; }
    public string NewStatus { get; set; } = "";
    public Guid ClosedById { get; set; }
    public string ClosedByName { get; set; } = "";
    public DateTimeOffset ClosedAt { get; set; }
}
