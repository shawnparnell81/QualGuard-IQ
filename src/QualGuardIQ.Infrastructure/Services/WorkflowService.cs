using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Identity;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Security;

namespace QualGuardIQ.Infrastructure.Services;

/// <summary>
/// Shared approval engine. Modules call SubmitAsync; approvers sign each step with their password.
/// When the last step is approved (or any step is rejected) the module's IApprovalHandler runs in the same transaction.
/// </summary>
public sealed class WorkflowService(
    AppDbContext db, IRecordRegistry records, IAccessService access, ICurrentUser current, INotifier notifier,
    UserManager<ApplicationUser> users, IEnumerable<IApprovalHandler> handlers, IClock clock, ISecurityEventLogger security)
{
    public async Task<ApprovalRequest> SubmitAsync(string recordType, Guid recordId, CancellationToken ct)
    {
        var record = await records.RequireEditAsync(recordType, recordId, ct);
        var route = await db.ApprovalRoutes.Include(r => r.Steps).FirstOrDefaultAsync(r => r.RecordType == record.Type && r.IsActive, ct)
            ?? throw new AppException($"No approval route is configured for {record.Type}. An administrator must set one up.");
        if (route.Steps.Count == 0) throw new AppException("The approval route has no steps.");
        if (await db.ApprovalRequests.AnyAsync(a => a.RecordType == record.Type && a.RecordId == record.Id && a.Status == ApprovalStatus.Pending, ct))
            throw new ConflictException("This record is already waiting for approval.");

        var req = new ApprovalRequest
        {
            RecordType = record.Type, RecordId = record.Id, RecordNumber = record.Number, SiteId = record.SiteId, DepartmentId = record.DepartmentId,
            RouteId = route.Id, SubmittedById = current.UserId!.Value, Status = ApprovalStatus.Pending, CurrentStep = route.Steps.Min(s => s.Order)
        };
        db.ApprovalRequests.Add(req);
        await db.SaveChangesAsync(ct);
        await NotifyStepAsync(req, route, record, ct);
        return req;
    }

    public async Task<ApprovalRequest> DecideAsync(Guid requestId, bool approve, string password, string? comment, CancellationToken ct)
    {
        var req = await db.ApprovalRequests.Include(r => r.Decisions).FirstOrDefaultAsync(r => r.Id == requestId, ct) ?? throw new NotFoundException("Approval request");
        var record = await records.RequireViewAsync(req.RecordType, req.RecordId, ct);
        if (req.Status != ApprovalStatus.Pending) throw new ConflictException("This approval is no longer pending.");
        var route = await db.ApprovalRoutes.Include(r => r.Steps).FirstAsync(r => r.Id == req.RouteId, ct);
        var step = route.Steps.Single(s => s.Order == req.CurrentStep);
        var uid = current.UserId!.Value;

        if (!await access.CanAccessAsync(uid, step.RequiredPermission, req.SiteId, req.DepartmentId, ct))
            throw new ForbiddenException("You are not an approver for this step.");
        if (route.PreventSelfApproval && req.SubmittedById == uid)
            throw new ForbiddenException("You cannot approve a record you submitted.");
        if (req.Decisions.Any(d => d.SignerId == uid))
            throw new ForbiddenException("You have already signed this approval.");
        if (!approve && string.IsNullOrWhiteSpace(comment))
            throw new AppException("A reason is required to reject.");

        var user = await users.FindByIdAsync(uid.ToString()) ?? throw new ForbiddenException();
        if (!await users.CheckPasswordAsync(user, password ?? ""))
        {
            await users.AccessFailedAsync(user);
            await security.LogAsync("SignatureFailed", false, uid, user.Email, $"Approval {req.RecordNumber}");
            throw new AppException("Password is incorrect. The signature was not applied.", 401);
        }
        await users.ResetAccessFailedCountAsync(user);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        req.Decisions.Add(new ApprovalDecision
        {
            RequestId = req.Id, StepOrder = step.Order, StepName = step.Name, SignerId = uid, SignerName = user.DisplayName,
            Approved = approve, Meaning = approve ? step.SignatureMeaning : "Rejected", Comment = comment, SignedAt = clock.UtcNow
        });
        var handler = handlers.FirstOrDefault(h => h.RecordType == req.RecordType);
        if (!approve)
        {
            req.Status = ApprovalStatus.Rejected;
            req.CompletedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            if (handler is not null) await handler.OnRejectedAsync(req.RecordId, comment, ct);
            await notifier.NotifyAsync([req.SubmittedById], $"{req.RecordNumber} was rejected", comment, record, ct: ct);
        }
        else
        {
            var next = route.Steps.Where(s => s.Order > step.Order).OrderBy(s => s.Order).FirstOrDefault();
            if (next is null)
            {
                req.Status = ApprovalStatus.Approved;
                req.CompletedAt = clock.UtcNow;
                await db.SaveChangesAsync(ct);
                if (handler is not null) await handler.OnApprovedAsync(req.RecordId, ct);
                await notifier.NotifyAsync([req.SubmittedById], $"{req.RecordNumber} was approved", null, record, ct: ct);
            }
            else
            {
                req.CurrentStep = next.Order;
                await db.SaveChangesAsync(ct);
                await NotifyStepAsync(req, route, record, ct);
            }
        }
        db.AuditEntries.Add(new Domain.Audit.AuditEntry
        {
            OccurredAt = clock.UtcNow, UserId = uid, UserName = user.DisplayName, Action = approve ? "Signed" : "Rejected",
            EntityType = req.RecordType, EntityId = req.RecordId, SiteId = req.SiteId, Reason = comment,
            Description = $"{user.DisplayName} signed {req.RecordNumber} step '{step.Name}' meaning '{(approve ? step.SignatureMeaning : "Rejected")}'"
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return req;
    }

    public async Task CancelAsync(Guid requestId, CancellationToken ct)
    {
        var req = await db.ApprovalRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct) ?? throw new NotFoundException("Approval request");
        await records.RequireEditAsync(req.RecordType, req.RecordId, ct);
        if (req.Status != ApprovalStatus.Pending) throw new ConflictException("This approval is no longer pending.");
        req.Status = ApprovalStatus.Cancelled;
        req.CompletedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        var handler = handlers.FirstOrDefault(h => h.RecordType == req.RecordType);
        if (handler is not null) await handler.OnRejectedAsync(req.RecordId, "Submission withdrawn", ct);
    }

    /// <summary>Pending approvals where the current user is an eligible signer for the current step.</summary>
    public async Task<IReadOnlyList<(ApprovalRequest Request, string StepName)>> MyPendingAsync(CancellationToken ct)
    {
        var uid = current.UserId!.Value;
        var pending = await db.ApprovalRequests.AsNoTracking().Include(r => r.Decisions).Where(r => r.Status == ApprovalStatus.Pending).ToListAsync(ct);
        var routes = await db.ApprovalRoutes.AsNoTracking().Include(r => r.Steps).ToDictionaryAsync(r => r.Id, ct);
        var result = new List<(ApprovalRequest, string)>();
        foreach (var r in pending)
        {
            var route = routes[r.RouteId];
            var step = route.Steps.Single(s => s.Order == r.CurrentStep);
            if (route.PreventSelfApproval && r.SubmittedById == uid) continue;
            if (r.Decisions.Any(d => d.SignerId == uid)) continue;
            if (await access.CanAccessAsync(uid, step.RequiredPermission, r.SiteId, r.DepartmentId, ct)) result.Add((r, step.Name));
        }
        return result;
    }

    private async Task NotifyStepAsync(ApprovalRequest req, ApprovalRoute route, RecordInfo record, CancellationToken ct)
    {
        var step = route.Steps.Single(s => s.Order == req.CurrentStep);
        var approvers = (await access.UsersWithPermissionAsync(step.RequiredPermission, req.SiteId, req.DepartmentId, ct))
            .Where(u => !(route.PreventSelfApproval && u == req.SubmittedById));
        await notifier.NotifyAsync(approvers, $"Approval needed: {record.Number}", $"{step.Name} - {record.Title}", record, ct: ct);
    }
}
