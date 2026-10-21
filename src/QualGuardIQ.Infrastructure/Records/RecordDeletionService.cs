using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Common;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Records;

public sealed record DeleteCheck(bool Deletable, bool CanDelete, bool Locked, string? Reason, string? DeletePermission,
    bool Draft = false, bool Critical = false, bool CanCloseOut = false);
public sealed record CloseOutDto(Guid Id, string RecordType, Guid RecordId, string Outcome, string OutcomeLabel, string Note, string? PreviousStatus, string NewStatus,
    string ClosedByName, DateTimeOffset ClosedAt);
public sealed record DeletedRecordDto(Guid Id, string RecordType, string TypeLabel, Guid RecordId, string Number, string Title, string? Status, Guid SiteId,
    string Reason, string DeletedByName, DateTimeOffset DeletedAt, bool WasLocked, DateTimeOffset? RestoredAt, string? RestoredByName, string? RestoreReason);

/// <summary>
/// Deleting and restoring any record type. A deletion hides the record everywhere (see AppDbContext.ConfigureRecordDeletion); nothing is
/// removed, so a person holding the module's delete permission can bring it back. Each delete and restore needs a reason and writes a
/// plain-language audit entry on the record. Closed, approved, effective or signed records also need the "delete locked records" permission.
/// <para>
/// Three rules decide who may delete:
/// a draft (status Draft) can be deleted by anyone who may edit or delete it;
/// a live quality-event record (see <see cref="CriticalRecords"/>) can only be deleted with <see cref="Permissions.RecordsDeleteLive"/>,
/// otherwise it is closed out with an outcome and a note;
/// any other record can be deleted with the module's delete permission and a reason.
/// </para>
/// </summary>
public sealed class RecordDeletionService(AppDbContext db, IRecordRegistry records, IAccessService access, ICurrentUser current, IClock clock, RecordTypeLabels labels, ISearchIndexer search)
{
    public const int MinReason = 5, MaxReason = 2000;

    Guid UserId => current.UserId ?? throw new ForbiddenException();

    /// <summary>True when the record type can be deleted at all (a deletable stored record with a module delete permission).</summary>
    public string? DeletePermissionFor(RecordInfo r)
    {
        var t = records.EntityTypeOf(r.Type);
        if (t is null || !typeof(ISoftDeletable).IsAssignableFrom(t) || typeof(IAppendOnly).IsAssignableFrom(t)) return null;
        return Permissions.DeleteFor(r.EditPermission);
    }

    /// <summary>Whether the record is closed, approved, effective or signed, so deleting it needs the higher permission.</summary>
    async Task<bool> IsLockedAsync(RecordInfo r, CancellationToken ct)
    {
        if (r.IsLocked) return true;
        if (records.EntityTypeOf(r.Type) == typeof(ControlledDocument)
            && await db.DocumentRevisions.AnyAsync(x => x.DocumentId == r.Id && x.Status != RevisionStatus.Draft, ct)) return true;
        return await db.ApprovalRequests.AnyAsync(a => a.RecordType == r.Type && a.RecordId == r.Id && a.Decisions.Any(), ct);
    }

    static bool IsDraft(RecordInfo r) => string.Equals(r.Status, "Draft", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for quality-event records and for any record linked to one (an 8D or containment form on a CAPA, for example).</summary>
    public async Task<bool> IsCriticalAsync(RecordInfo r, CancellationToken ct)
    {
        if (CriticalRecords.IsCritical(r.Type)) return true;
        var types = CriticalRecords.Types.ToList();
        return await db.RecordLinks.AsNoTracking().AnyAsync(l => (l.FromId == r.Id && types.Contains(l.ToType)) || (l.ToId == r.Id && types.Contains(l.FromType)), ct);
    }

    public async Task<DeleteCheck> CheckAsync(string type, Guid id, CancellationToken ct)
    {
        var r = await records.RequireViewAsync(type, id, ct);
        var perm = DeletePermissionFor(r);
        if (perm is null) return new DeleteCheck(false, false, false, "Records of this kind can't be deleted. Cancel, close or archive them instead.", null);
        var locked = await IsLockedAsync(r, ct);
        if (IsDraft(r) && !locked)
        {
            var may = await access.CanAccessAsync(UserId, perm, r.SiteId, r.DepartmentId, ct) || await access.CanAccessAsync(UserId, r.EditPermission, r.SiteId, r.DepartmentId, ct);
            return new DeleteCheck(true, may, false, may ? null : "You don't have permission to delete these records.", perm, Draft: true);
        }
        if (await IsCriticalAsync(r, ct))
        {
            var canClose = !locked && CloseOutStatusFor(r) is not null && await access.CanAccessAsync(UserId, r.EditPermission, r.SiteId, r.DepartmentId, ct)
                && !await db.RecordCloseOuts.AnyAsync(c => c.RecordId == r.Id && c.NewStatus == r.Status, ct);
            var mayLive = await access.CanAccessAsync(UserId, Permissions.RecordsDeleteLive, r.SiteId, r.DepartmentId, ct);
            return new DeleteCheck(true, mayLive, locked,
                mayLive ? null : "This record is live. Deleting it needs the \"delete live quality records\" permission. Close it out instead.",
                Permissions.RecordsDeleteLive, Critical: true, CanCloseOut: canClose);
        }
        if (!await access.CanAccessAsync(UserId, perm, r.SiteId, r.DepartmentId, ct))
            return new DeleteCheck(true, false, locked, "You don't have permission to delete these records.", perm);
        if (locked && !await access.CanAccessAsync(UserId, Permissions.RecordsDeleteLocked, r.SiteId, r.DepartmentId, ct))
            return new DeleteCheck(true, false, true, "This record is closed, approved or signed. Deleting it needs the \"delete locked records\" permission.", perm);
        return new DeleteCheck(true, true, locked, null, perm);
    }

    /// <summary>The status a close-out moves the record to (Cancelled where the record has it, else Closed), or null when it can't be closed out.</summary>
    string? CloseOutStatusFor(RecordInfo r)
    {
        var prop = records.EntityTypeOf(r.Type)?.GetProperty("Status");
        if (prop is null || !prop.PropertyType.IsEnum || !prop.CanWrite) return null;
        var names = Enum.GetNames(prop.PropertyType);
        return CloseOutTargets.FirstOrDefault(t => names.Contains(t));
    }

    static readonly string[] CloseOutTargets = ["Cancelled", "Closed", "Resolved", "Rejected"];

    public static string OutcomeLabel(CloseOutOutcome o) => o switch
    {
        CloseOutOutcome.NotRequired => "Not required",
        CloseOutOutcome.NoFaultFound => "No fault found",
        CloseOutOutcome.Duplicate => "Duplicate",
        CloseOutOutcome.RaisedInError => "Raised in error",
        _ => o.ToString(),
    };

    /// <summary>
    /// Closes a live quality-event record without deleting it: the record moves to its cancelled (or closed) status and the outcome and note
    /// are kept with it and written to the audit trail. Needs the record's edit permission.
    /// </summary>
    public async Task<CloseOutDto> CloseOutAsync(string type, Guid id, CloseOutOutcome outcome, string? note, CancellationToken ct)
    {
        if (!Enum.IsDefined(outcome)) throw new AppException("Choose why the record is being closed out.");
        var why = CleanReason(note, "close-out");
        var r = await records.RequireViewAsync(type, id, ct);
        if (!await access.CanAccessAsync(UserId, r.EditPermission, r.SiteId, r.DepartmentId, ct)) throw new ForbiddenException();
        if (!await IsCriticalAsync(r, ct)) throw new AppException("Only quality-event records are closed out. Delete this record with a note instead.");
        if (await IsLockedAsync(r, ct)) throw new ConflictException("The record is already closed, approved or signed.");
        var target = CloseOutStatusFor(r) ?? throw new AppException("Records of this kind can't be closed out.");
        var entityType = records.EntityTypeOf(r.Type)!;
        var entity = await db.FindAsync(entityType, r.Id) ?? throw new NotFoundException("Record");
        var prop = entityType.GetProperty("Status")!;
        prop.SetValue(entity, Enum.Parse(prop.PropertyType, target));
        var now = clock.UtcNow;
        var label = OutcomeLabel(outcome);
        var entry = new RecordCloseOut
        {
            RecordType = r.Type, RecordId = r.Id, SiteId = r.SiteId, Outcome = outcome, Note = why, PreviousStatus = r.Status is null ? null : Fit(r.Status, 100), NewStatus = target,
            ClosedById = UserId, ClosedByName = Fit(current.UserName, 200), ClosedAt = now,
        };
        db.RecordCloseOuts.Add(entry);
        var a = AuditLog.Add(db, current, now, "Closed out", r.Type, r.Id, r.SiteId, $"Closed out {labels.For(r.Type)} {r.Number} \"{r.Title}\" as {label}. Note: {why}");
        a.Reason = why;
        await db.SaveChangesAsync(ct);
        try { await search.IndexAsync(await records.GetAsync(r.Type, r.Id, ct), null, ct); } catch (NotFoundException) { }
        return ToDto(entry);
    }

    /// <summary>The latest close-out of the record, if it was closed out.</summary>
    public async Task<CloseOutDto?> LatestCloseOutAsync(Guid recordId, CancellationToken ct)
    {
        var e = await db.RecordCloseOuts.AsNoTracking().Where(x => x.RecordId == recordId).OrderByDescending(x => x.ClosedAt).FirstOrDefaultAsync(ct);
        return e is null ? null : ToDto(e);
    }

    static CloseOutDto ToDto(RecordCloseOut e) => new(e.Id, e.RecordType, e.RecordId, e.Outcome.ToString(), OutcomeLabel(e.Outcome), e.Note, e.PreviousStatus, e.NewStatus, e.ClosedByName, e.ClosedAt);

    static string CleanReason(string? reason, string what)
    {
        var r = (reason ?? "").Trim();
        if (r.Length < MinReason) throw new AppException($"Give a reason for the {what} (at least {MinReason} characters).");
        if (r.Length > MaxReason) throw new AppException($"The reason is limited to {MaxReason} characters.");
        return r;
    }

    public async Task<DeletedRecordDto> DeleteAsync(string type, Guid id, string? reason, CancellationToken ct)
    {
        var why = CleanReason(reason, "deletion");
        var r = await records.RequireViewAsync(type, id, ct);
        var check = await CheckAsync(type, id, ct);
        if (!check.Deletable) throw new AppException(check.Reason!);
        if (!check.CanDelete) throw new ForbiddenException(check.Reason!);
        if (await db.DeletedRecords.AnyAsync(d => d.RecordId == r.Id && d.RestoredAt == null, ct)) throw new ConflictException("The record is already deleted.");
        var now = clock.UtcNow;
        var label = labels.For(r.Type);
        var entry = new DeletedRecord
        {
            RecordType = r.Type, RecordId = r.Id, SiteId = r.SiteId, DepartmentId = r.DepartmentId, Number = Fit(r.Number, 200), Title = Fit(r.Title, 500), Status = r.Status is null ? null : Fit(r.Status, 100),
            DeletePermission = check.DeletePermission!, WasLocked = check.Locked, Reason = why, DeletedById = UserId, DeletedByName = Fit(current.UserName, 200), DeletedAt = now,
        };
        db.DeletedRecords.Add(entry);
        var a = AuditLog.Add(db, current, now, "Deleted", r.Type, r.Id, r.SiteId, $"Deleted {label} {r.Number} \"{r.Title}\". Reason: {why}");
        a.Reason = why;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw new ConflictException("The record is already deleted."); }
        return ToDto(entry);
    }

    public async Task<DeletedRecordDto> RestoreAsync(Guid entryId, string? reason, CancellationToken ct)
    {
        var why = CleanReason(reason, "restore");
        var e = await db.DeletedRecords.FirstOrDefaultAsync(x => x.Id == entryId, ct) ?? throw new NotFoundException("Deleted record");
        if (!await access.CanAccessAsync(UserId, e.DeletePermission, e.SiteId, e.DepartmentId, ct)) throw new NotFoundException("Deleted record");
        if (e.RestoredAt is not null) throw new ConflictException("The record has already been restored.");
        if (e.WasLocked && e.DeletePermission != Permissions.RecordsDeleteLive && !await access.CanAccessAsync(UserId, Permissions.RecordsDeleteLocked, e.SiteId, e.DepartmentId, ct))
            throw new ForbiddenException("This record was closed, approved or signed when it was deleted. Restoring it needs the \"delete locked records\" permission.");
        var now = clock.UtcNow;
        e.RestoredAt = now;
        e.RestoredById = UserId;
        e.RestoredByName = Fit(current.UserName, 200);
        e.RestoreReason = why;
        var a = AuditLog.Add(db, current, now, "Restored", e.RecordType, e.RecordId, e.SiteId,
            $"Restored {labels.For(e.RecordType)} {e.Number} \"{e.Title}\" (deleted by {e.DeletedByName} on {e.DeletedAt:yyyy-MM-dd}). Reason: {why}");
        a.Reason = why;
        await db.SaveChangesAsync(ct);
        return ToDto(e);
    }

    /// <summary>Deleted records the caller may restore (those in modules and sites where they hold the delete permission), newest first.</summary>
    public async Task<IReadOnlyList<DeletedRecordDto>> ListAsync(string? recordType, string? q, bool includeRestored, CancellationToken ct)
    {
        var query = db.DeletedRecords.AsNoTracking();
        if (!includeRestored) query = query.Where(x => x.RestoredAt == null);
        if (!string.IsNullOrWhiteSpace(recordType)) query = query.Where(x => x.RecordType == recordType);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = "%" + q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(x => EF.Functions.ILike(x.Number, like) || EF.Functions.ILike(x.Title, like) || EF.Functions.ILike(x.Reason, like));
        }
        var rows = await query.OrderByDescending(x => x.DeletedAt).Take(1000).ToListAsync(ct);
        var scopes = new Dictionary<string, AccessScope>();
        var result = new List<DeletedRecordDto>();
        foreach (var r in rows)
        {
            if (!scopes.TryGetValue(r.DeletePermission, out var scope)) scopes[r.DeletePermission] = scope = await access.GetScopeAsync(UserId, r.DeletePermission, ct);
            if (scope.IsEmpty) continue;
            if (!scope.AllSites && !await access.CanAccessAsync(UserId, r.DeletePermission, r.SiteId, r.DepartmentId, ct)) continue;
            result.Add(ToDto(r));
        }
        return result;
    }

    DeletedRecordDto ToDto(DeletedRecord e) => new(e.Id, e.RecordType, labels.For(e.RecordType), e.RecordId, e.Number, e.Title, e.Status, e.SiteId, e.Reason, e.DeletedByName, e.DeletedAt,
        e.WasLocked, e.RestoredAt, e.RestoredByName, e.RestoreReason);

    static string Fit(string s, int max) => s.Length > max ? s[..max] : s;
}
