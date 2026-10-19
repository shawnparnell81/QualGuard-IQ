using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Common;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Records;

public sealed record DeleteCheck(bool Deletable, bool CanDelete, bool Locked, string? Reason, string? DeletePermission);
public sealed record DeletedRecordDto(Guid Id, string RecordType, string TypeLabel, Guid RecordId, string Number, string Title, string? Status, Guid SiteId,
    string Reason, string DeletedByName, DateTimeOffset DeletedAt, bool WasLocked, DateTimeOffset? RestoredAt, string? RestoredByName, string? RestoreReason);

/// <summary>
/// Deleting and restoring any record type. A deletion hides the record everywhere (see AppDbContext.ConfigureRecordDeletion); nothing is
/// removed, so a person holding the module's delete permission can bring it back. Each delete and restore needs a reason and writes a
/// plain-language audit entry on the record. Closed, approved, effective or signed records also need the "delete locked records" permission.
/// </summary>
public sealed class RecordDeletionService(AppDbContext db, IRecordRegistry records, IAccessService access, ICurrentUser current, IClock clock, RecordTypeLabels labels)
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

    public async Task<DeleteCheck> CheckAsync(string type, Guid id, CancellationToken ct)
    {
        var r = await records.RequireViewAsync(type, id, ct);
        var perm = DeletePermissionFor(r);
        if (perm is null) return new DeleteCheck(false, false, false, "Records of this kind can't be deleted. Cancel, close or archive them instead.", null);
        var locked = await IsLockedAsync(r, ct);
        if (!await access.CanAccessAsync(UserId, perm, r.SiteId, r.DepartmentId, ct))
            return new DeleteCheck(true, false, locked, "You don't have permission to delete these records.", perm);
        if (locked && !await access.CanAccessAsync(UserId, Permissions.RecordsDeleteLocked, r.SiteId, r.DepartmentId, ct))
            return new DeleteCheck(true, false, true, "This record is closed, approved or signed. Deleting it needs the \"delete locked records\" permission.", perm);
        return new DeleteCheck(true, true, locked, null, perm);
    }

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
