using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Application.Rules;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Security;

namespace QualGuardIQ.Infrastructure.Services;

/// <summary>
/// Shared approval engine. Modules call SubmitAsync; approvers sign each step with their password.
/// When the last step is approved (or any step is rejected) the module's IApprovalHandler runs in the same transaction.
/// </summary>
public sealed class WorkflowService(
    AppDbContext db, IRecordRegistry records, IAccessService access, ICurrentUser current, INotifier notifier,
    IEnumerable<IApprovalHandler> handlers, IClock clock, QualGuardIQ.Infrastructure.Modules.Enterprise.QualificationService qualifications, RecordConditions conditions, QualGuardIQ.Infrastructure.Modules.Platform.RuleEngine rules, SignerVerification signer)
{
    public async Task<ApprovalRequest> SubmitAsync(string recordType, Guid recordId, CancellationToken ct)
    {
        var record = await records.RequireEditAsync(recordType, recordId, ct);
        // A site-specific route wins over the shared corporate route.
        var route = await db.ApprovalRoutes.Include(r => r.Steps).Where(r => r.RecordType == record.Type && r.IsActive && (r.SiteId == record.SiteId || r.SiteId == null))
            .OrderBy(r => r.SiteId == null).FirstOrDefaultAsync(ct)
            ?? throw new AppException($"No approval route is configured for {record.Type}. An administrator must set one up.");
        if (route.Steps.Count == 0) throw new AppException("The approval route has no steps.");
        if (await db.ApprovalRequests.AnyAsync(a => a.RecordType == record.Type && a.RecordId == record.Id && a.Status == ApprovalStatus.Pending, ct))
            throw new ConflictException("This record is already waiting for approval.");

        var due = await rules.OnSubmitAsync(record, ct); // business rules: validation blocks, routing sets the due date
        var values = await conditions.ValuesAsync(record.Type, record.Id, ct);
        var first = NextStage(route, int.MinValue, values) ?? throw new AppException("No step of the approval route applies to this record. An administrator must add an unconditional step.");
        var req = new ApprovalRequest
        {
            RecordType = record.Type, RecordId = record.Id, RecordNumber = record.Number, SiteId = record.SiteId, DepartmentId = record.DepartmentId,
            RouteId = route.Id, SubmittedById = current.UserId!.Value, Status = ApprovalStatus.Pending, CurrentStep = first, StageStartedAt = clock.UtcNow, DueAt = due
        };
        db.ApprovalRequests.Add(req);
        await db.SaveChangesAsync(ct);
        await NotifyStepAsync(req, route, record, ct);
        return req;
    }

    public Task<ApprovalRequest> DecideAsync(Guid requestId, bool approve, string password, string? comment, CancellationToken ct) => DecideAsync(requestId, approve, password, comment, null, ct);

    public async Task<ApprovalRequest> DecideAsync(Guid requestId, bool approve, string password, string? comment, string? mfaCode, CancellationToken ct)
    {
        var req = await db.ApprovalRequests.Include(r => r.Decisions).FirstOrDefaultAsync(r => r.Id == requestId, ct) ?? throw new NotFoundException("Approval request");
        var record = await records.RequireViewAsync(req.RecordType, req.RecordId, ct);
        if (await QualGuardIQ.Infrastructure.Records.ImportedRecords.IsReadOnlyAsync(db, req.RecordId, ct)) throw new AppException(QualGuardIQ.Infrastructure.Records.ImportedRecords.ReadOnlyMessage, 409);
        if (req.Status != ApprovalStatus.Pending) throw new ConflictException("This approval is no longer pending.");
        var route = await db.ApprovalRoutes.Include(r => r.Steps).FirstAsync(r => r.Id == req.RouteId, ct);
        var values = await conditions.ValuesAsync(req.RecordType, req.RecordId, ct);
        var open = OpenSteps(route, req, values);
        var uid = current.UserId!.Value;
        ApprovalRouteStep? step = null;
        foreach (var candidate in open)
            if (await access.CanAccessAsync(uid, candidate.RequiredPermission, req.SiteId, req.DepartmentId, ct)) { step = candidate; break; }
        // Delegated approval: an active delegation lets the delegate sign a step the delegator could sign.
        (Guid Id, string Name)? onBehalf = null;
        if (step is null)
            foreach (var d in await ActiveDelegationsAsync(uid, req.RecordType, ct))
            {
                foreach (var candidate in open)
                    if (await access.CanAccessAsync(d.DelegatorId, candidate.RequiredPermission, req.SiteId, req.DepartmentId, ct)) { step = candidate; break; }
                if (step is not null) { onBehalf = (d.DelegatorId, await db.Users.Where(u => u.Id == d.DelegatorId).Select(u => u.DisplayName).FirstAsync(ct)); break; }
            }
        if (step is null) throw new ForbiddenException("You are not an approver for this step.");
        if (route.PreventSelfApproval && (req.SubmittedById == uid || onBehalf?.Id == req.SubmittedById))
            throw new ForbiddenException("You cannot approve a record you submitted.");
        if (req.Decisions.Any(d => d.SignerId == uid || d.OnBehalfOfId == uid || (onBehalf != null && (d.SignerId == onBehalf.Value.Id || d.OnBehalfOfId == onBehalf.Value.Id))))
            throw new ForbiddenException("You have already signed this approval.");
        if (!approve && string.IsNullOrWhiteSpace(comment))
            throw new AppException("A reason is required to reject.");

        var (user, verification) = await signer.VerifyAsync(uid, password, mfaCode, step.RequireMfa, $"Approval {req.RecordNumber}", "This step needs an MFA-verified signature. Set up an authenticator app first.");
        if (approve && step.RequiredSkillId is { } skillId)
        {
            var q = await qualifications.CheckAsync(uid, skillId, ct);
            if (!q.Qualified) throw new ForbiddenException($"You cannot sign this step: {q.Reason}");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        req.Decisions.Add(new ApprovalDecision
        {
            RequestId = req.Id, StepOrder = step.Order, StepId = step.Id, StepName = step.Name, SignerId = uid, SignerName = user.DisplayName,
            OnBehalfOfId = onBehalf?.Id, OnBehalfOfName = onBehalf?.Name,
            Approved = approve, Meaning = approve ? step.SignatureMeaning : "Rejected", Comment = comment, SignedAt = clock.UtcNow, Verification = verification
        });
        var signed = req.Decisions[^1];
        signed.SignatureHash = SignatureHash(signed, req.RecordNumber);
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
            var stillOpen = open.Count(s => s.Id != step.Id);
            var next = stillOpen > 0 ? (int?)req.CurrentStep : NextStage(route, req.CurrentStep, values);
            if (stillOpen > 0)
            {
                await db.SaveChangesAsync(ct); // parallel stage: other signers still pending
            }
            else if (next is null)
            {
                req.Status = ApprovalStatus.Approved;
                req.CompletedAt = clock.UtcNow;
                await db.SaveChangesAsync(ct);
                if (handler is not null) await handler.OnApprovedAsync(req.RecordId, ct);
                await notifier.NotifyAsync([req.SubmittedById], $"{req.RecordNumber} was approved", null, record, ct: ct);
            }
            else
            {
                req.CurrentStep = next.Value;
                req.StageStartedAt = clock.UtcNow;
                await db.SaveChangesAsync(ct);
                await NotifyStepAsync(req, route, record, ct);
            }
        }
        db.AuditEntries.Add(new Domain.Audit.AuditEntry
        {
            OccurredAt = clock.UtcNow, UserId = uid, UserName = user.DisplayName, Action = approve ? "Signed" : "Rejected",
            EntityType = req.RecordType, EntityId = req.RecordId, SiteId = req.SiteId, Reason = comment,
            Description = $"{user.DisplayName} signed {req.RecordNumber} step '{step.Name}' meaning '{(approve ? step.SignatureMeaning : "Rejected")}'" + (onBehalf is { } ob ? $" on behalf of {ob.Name} (delegation)" : "")
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
            if (route.PreventSelfApproval && r.SubmittedById == uid) continue;
            if (r.Decisions.Any(d => d.SignerId == uid)) continue;
            var stage = route.Steps.Where(s => s.Stage == r.CurrentStep).ToList();
            if (stage.Any(s => s.ConditionField != null)) stage = OpenSteps(route, r, await conditions.ValuesAsync(r.RecordType, r.RecordId, ct));
            else stage = stage.Where(s => !Signed(r, s)).ToList();
            var found = false;
            foreach (var step in stage)
                if (await access.CanAccessAsync(uid, step.RequiredPermission, r.SiteId, r.DepartmentId, ct)) { result.Add((r, step.Name)); found = true; break; }
            if (found) continue;
            foreach (var d in await ActiveDelegationsAsync(uid, r.RecordType, ct))
            {
                if ((route.PreventSelfApproval && r.SubmittedById == d.DelegatorId) || r.Decisions.Any(x => x.SignerId == d.DelegatorId || x.OnBehalfOfId == d.DelegatorId)) continue;
                var step = await FirstAsync(stage, d.DelegatorId, r, ct);
                if (step is not null) { result.Add((r, step.Name + " (delegated)")); break; }
            }
        }
        return result;
    }

    private async Task<ApprovalRouteStep?> FirstAsync(List<ApprovalRouteStep> steps, Guid userId, ApprovalRequest r, CancellationToken ct)
    {
        foreach (var s in steps)
            if (await access.CanAccessAsync(userId, s.RequiredPermission, r.SiteId, r.DepartmentId, ct)) return s;
        return null;
    }

    /// <summary>Delegations to <paramref name="delegateId"/> in force now that cover the record type.</summary>
    private Task<List<Domain.Quality.ApprovalDelegation>> ActiveDelegationsAsync(Guid delegateId, string recordType, CancellationToken ct)
    {
        var now = clock.UtcNow;
        return db.ApprovalDelegations.AsNoTracking()
            .Where(d => d.DelegateId == delegateId && !d.Revoked && d.StartsAt <= now && d.EndsAt > now && (d.RecordType == null || d.RecordType == recordType))
            .OrderBy(d => d.StartsAt).ToListAsync(ct);
    }

    private async Task NotifyStepAsync(ApprovalRequest req, ApprovalRoute route, RecordInfo record, CancellationToken ct)
    {
        var values = await conditions.ValuesAsync(req.RecordType, req.RecordId, ct);
        foreach (var step in OpenSteps(route, req, values))
        {
            var approvers = (await access.UsersWithPermissionAsync(step.RequiredPermission, req.SiteId, req.DepartmentId, ct))
                .Where(u => !(route.PreventSelfApproval && u == req.SubmittedById));
            await notifier.NotifyAsync(approvers, $"Approval needed: {record.Number}", $"{step.Name} - {record.Title}", record, ct: ct);
        }
    }

    public static bool Applies(ApprovalRouteStep s, IReadOnlyDictionary<string, object?> values) =>
        s.ConditionField is null || RecordConditions.Matches(new Condition(s.ConditionField, s.ConditionOperator ?? "equals", s.ConditionValue), values);

    static bool Signed(ApprovalRequest r, ApprovalRouteStep s) =>
        r.Decisions.Any(d => d.Approved && (d.StepId == s.Id || (d.StepId == null && d.StepOrder == s.Order)));

    /// <summary>Applicable steps of the current stage that still need a signature.</summary>
    public static List<ApprovalRouteStep> OpenSteps(ApprovalRoute route, ApprovalRequest r, IReadOnlyDictionary<string, object?> values) =>
        route.Steps.Where(s => s.Stage == r.CurrentStep && Applies(s, values) && !Signed(r, s)).OrderBy(s => s.Order).ToList();

    /// <summary>The next stage after <paramref name="after"/> that has at least one applicable step; conditional stages that do not apply are skipped.</summary>
    public static int? NextStage(ApprovalRoute route, int after, IReadOnlyDictionary<string, object?> values) =>
        route.Steps.Where(s => s.Stage > after && Applies(s, values)).OrderBy(s => s.Stage).Select(s => (int?)s.Stage).FirstOrDefault();

    /// <summary>Which steps would apply to a record, stage by stage (designer preview). Requires view access to the record.</summary>
    public async Task<List<(int Stage, string Step, bool Applies)>> PreviewAsync(ApprovalRoute route, string recordType, Guid recordId, CancellationToken ct)
    {
        await records.RequireViewAsync(recordType, recordId, ct);
        var values = await conditions.ValuesAsync(recordType, recordId, ct);
        return route.Steps.OrderBy(s => s.Stage).ThenBy(s => s.Order).Select(s => (s.Stage, s.Name, Applies(s, values))).ToList();
    }

    /// <summary>Background: notifies the escalation group once per stage when a stage waits longer than its escalation time.</summary>
    public async Task<int> EscalateAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var pending = await db.ApprovalRequests.Include(r => r.Decisions).Where(r => r.Status == ApprovalStatus.Pending && r.StageStartedAt != null && (r.EscalatedStage == null || r.EscalatedStage != r.CurrentStep)).Take(500).ToListAsync(ct);
        if (pending.Count == 0) return 0;
        var routeIds = pending.Select(p => p.RouteId).Distinct().ToList();
        var routes = await db.ApprovalRoutes.AsNoTracking().Include(r => r.Steps).Where(r => routeIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var sent = 0;
        foreach (var r in pending)
        {
            var due = routes[r.RouteId].Steps.Where(s => s.Stage == r.CurrentStep && s.EscalateAfterHours is > 0 && r.StageStartedAt!.Value.AddHours(s.EscalateAfterHours!.Value) <= now && !Signed(r, s)).ToList();
            if (due.Count == 0) continue;
            var record = await records.GetAsync(r.RecordType, r.RecordId, ct);
            foreach (var s in due)
            {
                var who = await access.UsersWithPermissionAsync(s.EscalationPermission ?? s.RequiredPermission, r.SiteId, r.DepartmentId, ct);
                await notifier.NotifyAsync(who, $"Escalation: {r.RecordNumber} waiting on '{s.Name}'", $"Pending more than {s.EscalateAfterHours} h - {record.Title}", record, ct: ct);
            }
            r.EscalatedStage = r.CurrentStep;
            db.AuditEntries.Add(new Domain.Audit.AuditEntry
            {
                OccurredAt = now, UserName = "System", Action = "Escalated", EntityType = r.RecordType, EntityId = r.RecordId, SiteId = r.SiteId,
                Description = $"System escalated approval of {r.RecordNumber}: stage {r.CurrentStep} ({string.Join(", ", due.Select(d => d.Name))}) pending beyond its escalation time"
            });
            sent++;
        }
        await db.SaveChangesAsync(ct);
        return sent;
    }

    /// <summary>Binds the signature to its record: any later change to signer, meaning, time or record number breaks the hash.</summary>
    public static string SignatureHash(ApprovalDecision d, string recordNumber) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"{d.RequestId}|{d.StepOrder}|{d.SignerId}|{d.SignerName}|{d.Approved}|{d.Meaning}|{d.SignedAt.UtcTicks / 10}|{recordNumber}|{d.Comment}" + (d.OnBehalfOfId is { } ob ? $"|on-behalf:{ob}" : "")))).ToLowerInvariant();
}
