using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.QualityOps;

public sealed record DelegationInput(Guid? DelegatorId, Guid DelegateId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? RecordType, string Reason);

/// <summary>
/// Delegation and temporary responsibility. People delegate their own approvals; administrators of users may set one up for someone
/// who is away. The workflow engine accepts the delegate's signature on steps the delegator could sign and records "on behalf of".
/// </summary>
public sealed class DelegationService(AppDbContext db, ModuleSupport m, IAccessService access, IRecordRegistry registry, INotifier notifier)
{
    public async Task<ApprovalDelegation> CreateAsync(DelegationInput r, CancellationToken ct)
    {
        var delegator = r.DelegatorId ?? m.UserId;
        if (delegator != m.UserId && !(await access.GetScopeAsync(m.UserId, Permissions.AdminUsers, ct)).AllSites)
            throw new ForbiddenException("Only a user administrator can delegate on someone else's behalf.");
        if (delegator == r.DelegateId) throw new AppException("Choose someone else as the delegate.");
        await m.RequireActiveUserAsync(delegator, "delegator", ct);
        await m.RequireActiveUserAsync(r.DelegateId, "delegate", ct);
        if (r.EndsAt <= r.StartsAt) throw new AppException("The delegation must end after it starts.");
        if (r.EndsAt <= m.Now) throw new AppException("The delegation period has already ended.");
        if ((r.EndsAt - r.StartsAt).TotalDays > 366) throw new AppException("A delegation can last at most one year.");
        ModuleSupport.Required(r.Reason, "Reason", 1000);
        var type = string.IsNullOrWhiteSpace(r.RecordType) ? null : r.RecordType.Trim();
        if (type is not null && !registry.IsKnownType(type)) throw new AppException("Unknown record type.");
        if (await db.ApprovalDelegations.AnyAsync(d => d.DelegateId == delegator && !d.Revoked && d.EndsAt > r.StartsAt && d.StartsAt < r.EndsAt && d.DelegatorId == r.DelegateId, ct))
            throw new ConflictException("The delegate has delegated to this person for the same period; delegations cannot run in a circle.");
        var x = new ApprovalDelegation { DelegatorId = delegator, DelegateId = r.DelegateId, StartsAt = r.StartsAt, EndsAt = r.EndsAt, RecordType = type, Reason = r.Reason.Trim() };
        db.ApprovalDelegations.Add(x);
        await db.SaveChangesAsync(ct);
        var name = await db.Users.Where(u => u.Id == delegator).Select(u => u.DisplayName).FirstAsync(ct);
        await notifier.NotifyAsync([r.DelegateId], $"{name} delegated approvals to you", $"{(type ?? "All approvals")} from {r.StartsAt:yyyy-MM-dd HH:mm} to {r.EndsAt:yyyy-MM-dd HH:mm} UTC. {x.Reason}", null, ct: ct);
        return x;
    }

    public async Task RevokeAsync(Guid id, CancellationToken ct)
    {
        var x = await db.ApprovalDelegations.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Delegation");
        if (x.DelegatorId != m.UserId && !(await access.GetScopeAsync(m.UserId, Permissions.AdminUsers, ct)).AllSites) throw new ForbiddenException();
        if (x.Revoked) throw new ConflictException("The delegation is already revoked.");
        x.Revoked = true; x.RevokedAt = m.Now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Delegations the user gave or received (administrators of users see all).</summary>
    public async Task<object> ListAsync(bool all, CancellationToken ct)
    {
        var uid = m.UserId;
        var q = db.ApprovalDelegations.AsNoTracking();
        if (!(all && (await access.GetScopeAsync(uid, Permissions.AdminUsers, ct)).AllSites)) q = q.Where(d => d.DelegatorId == uid || d.DelegateId == uid);
        var now = m.Now;
        return await q.OrderByDescending(d => d.StartsAt).Take(500).Select(d => new
        {
            d.Id, d.DelegatorId, Delegator = db.Users.Where(u => u.Id == d.DelegatorId).Select(u => u.DisplayName).First(), d.DelegateId,
            Delegate = db.Users.Where(u => u.Id == d.DelegateId).Select(u => u.DisplayName).First(), d.StartsAt, d.EndsAt, d.RecordType, d.Reason, d.Revoked,
            Active = !d.Revoked && d.StartsAt <= now && d.EndsAt > now
        }).ToListAsync(ct);
    }
}
