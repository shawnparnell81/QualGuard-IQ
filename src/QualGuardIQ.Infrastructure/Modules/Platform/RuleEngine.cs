using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Application.Rules;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Platform;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Platform;

public sealed record BusinessRuleInput(string Name, string RecordType, RuleKind Kind, string? Trigger, List<RuleCondition>? Conditions, string Message, string? TargetPermission, int? DueDays, int? AfterHours, Guid? SiteId, int Priority = 100, bool Enabled = true);
public sealed record RuleTestResult(bool Matches, List<object> Conditions, string Outcome);

/// <summary>
/// Centralized business rule engine. Rules are data (no code); conditions use the same evaluator as the workflow and form designers.
/// Hooks: approval submission (validation, routing), the approval escalation job (escalation) and the platform event bus (automation).
/// </summary>
public sealed class RuleEngine(AppDbContext db, RecordConditions conditions, IRecordRegistry records, IAccessService access, INotifier notifier, IClock clock)
{
    static readonly string[] Triggers = [PlatformEvents.RecordCreated, PlatformEvents.RecordUpdated, PlatformEvents.RecordApproved, PlatformEvents.RecordRejected, PlatformEvents.WorkflowCompleted, PlatformEvents.WorkflowStepSigned];

    public Task<List<BusinessRule>> ListAsync(CancellationToken ct) => db.BusinessRules.AsNoTracking().OrderBy(r => r.RecordType).ThenBy(r => r.Priority).ThenBy(r => r.Name).ToListAsync(ct);

    public async Task<List<RuleFiring>> FiringsAsync(Guid? ruleId, CancellationToken ct) =>
        await db.RuleFirings.AsNoTracking().Where(f => ruleId == null || f.RuleId == ruleId).OrderByDescending(f => f.Id).Take(200).ToListAsync(ct);

    void Check(BusinessRuleInput r)
    {
        Quality.ModuleSupport.Required(r.Name, "Name", 200);
        if (!records.IsKnownType(r.RecordType)) throw new AppException($"Unknown record type '{r.RecordType}'.");
        foreach (var c in r.Conditions ?? []) conditions.Validate(r.RecordType, new Condition(c.Field, c.Operator, c.Value));
        if (r.TargetPermission is { Length: > 0 } p && !Permissions.All.Contains(p)) throw new AppException($"Unknown permission '{p}'.");
        switch (r.Kind)
        {
            case RuleKind.Validation: Quality.ModuleSupport.Required(r.Message, "Message", 1000); if (r.Conditions is not { Count: > 0 }) throw new AppException("A validation rule needs at least one condition."); break;
            case RuleKind.Routing: if (string.IsNullOrWhiteSpace(r.TargetPermission)) throw new AppException("A routing rule needs a target permission group."); if (r.DueDays is < 1 or > 365) throw new AppException("Due days is 1–365."); break;
            case RuleKind.Escalation: if (string.IsNullOrWhiteSpace(r.TargetPermission) || r.AfterHours is not (>= 1 and <= 2160)) throw new AppException("An escalation rule needs a target permission and 1–2160 hours."); break;
            case RuleKind.Automation: if (r.Trigger is null || !Triggers.Contains(r.Trigger)) throw new AppException("Choose the event that triggers the automation."); if (string.IsNullOrWhiteSpace(r.TargetPermission)) throw new AppException("An automation rule needs a target permission group."); break;
        }
    }

    static void Apply(BusinessRule x, BusinessRuleInput r)
    {
        x.Name = r.Name.Trim(); x.RecordType = r.RecordType; x.Kind = r.Kind; x.Trigger = r.Kind == RuleKind.Automation ? r.Trigger : null; x.Conditions = r.Conditions ?? [];
        x.Message = r.Message?.Trim() ?? ""; x.TargetPermission = string.IsNullOrWhiteSpace(r.TargetPermission) ? null : r.TargetPermission; x.DueDays = r.DueDays; x.AfterHours = r.AfterHours;
        x.SiteId = r.SiteId; x.Priority = r.Priority; x.Enabled = r.Enabled;
    }

    public async Task<BusinessRule> CreateAsync(BusinessRuleInput r, CancellationToken ct)
    {
        Check(r);
        var x = new BusinessRule(); Apply(x, r);
        db.BusinessRules.Add(x); await db.SaveChangesAsync(ct);
        return x;
    }

    public async Task UpdateAsync(Guid id, BusinessRuleInput r, CancellationToken ct)
    {
        var x = await db.BusinessRules.FirstOrDefaultAsync(q => q.Id == id, ct) ?? throw new NotFoundException("Business rule");
        Check(r); Apply(x, r); await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var x = await db.BusinessRules.FirstOrDefaultAsync(q => q.Id == id, ct) ?? throw new NotFoundException("Business rule");
        db.BusinessRules.Remove(x); await db.SaveChangesAsync(ct);
    }

    static bool Holds(BusinessRule r, IReadOnlyDictionary<string, object?> values) => r.Conditions.All(c => Conditions.Matches(new Condition(c.Field, c.Operator, c.Value), values));

    async Task<List<BusinessRule>> ActiveAsync(string recordType, RuleKind kind, Guid siteId, CancellationToken ct) =>
        await db.BusinessRules.AsNoTracking().Where(r => r.Enabled && r.RecordType == recordType && r.Kind == kind && (r.SiteId == null || r.SiteId == siteId)).OrderBy(r => r.Priority).ToListAsync(ct);

    /// <summary>Dry run against an existing record: which conditions hold and what the rule would do.</summary>
    public async Task<RuleTestResult> TestAsync(Guid id, Guid recordId, CancellationToken ct)
    {
        var r = await db.BusinessRules.AsNoTracking().FirstOrDefaultAsync(q => q.Id == id, ct) ?? throw new NotFoundException("Business rule");
        await records.RequireViewAsync(r.RecordType, recordId, ct);
        var values = await conditions.ValuesAsync(r.RecordType, recordId, ct);
        var each = r.Conditions.Select(c => (object)new { c.Field, c.Operator, c.Value, Actual = values.GetValueOrDefault(c.Field)?.ToString(), Holds = Conditions.Matches(new Condition(c.Field, c.Operator, c.Value), values) }).ToList();
        var holds = Holds(r, values);
        return new RuleTestResult(holds, each, !holds ? "No action (conditions do not all hold)." : r.Kind switch
        {
            RuleKind.Validation => $"Blocks submission: {r.Message}",
            RuleKind.Routing => $"Routes to {r.TargetPermission} with a {r.DueDays}-day due date.",
            RuleKind.Escalation => $"Escalates to {r.TargetPermission} after {r.AfterHours} h pending.",
            _ => $"On {r.Trigger}: notifies {r.TargetPermission}."
        });
    }

    async Task LogAsync(BusinessRule r, string type, Guid id, string outcome, long? eventId, Guid? requestId, Guid siteId, CancellationToken ct)
    {
        db.RuleFirings.Add(new RuleFiring { RuleId = r.Id, RecordType = type, RecordId = id, EventId = eventId, ApprovalRequestId = requestId, FiredAt = clock.UtcNow, Outcome = outcome });
        db.AuditEntries.Add(new Domain.Audit.AuditEntry { OccurredAt = clock.UtcNow, UserName = "Rule engine", Action = "RuleFired", EntityType = type, EntityId = id, SiteId = siteId, Description = $"Rule engine applied '{r.Name}': {outcome}" });
        await db.SaveChangesAsync(ct);
    }

    async Task NotifyAsync(BusinessRule r, RecordInfo rec, string title, CancellationToken ct)
    {
        var who = await access.UsersWithPermissionAsync(r.TargetPermission!, rec.SiteId, rec.DepartmentId, ct);
        await notifier.NotifyAsync(who, title, string.IsNullOrWhiteSpace(r.Message) ? rec.Title : r.Message, rec, ct: ct);
    }

    /// <summary>Called by the approval engine before a request is created: validation rules block, routing rules notify their group and set the due date.</summary>
    public async Task<DateTimeOffset?> OnSubmitAsync(RecordInfo rec, CancellationToken ct)
    {
        var validation = await ActiveAsync(rec.Type, RuleKind.Validation, rec.SiteId, ct);
        var routing = await ActiveAsync(rec.Type, RuleKind.Routing, rec.SiteId, ct);
        if (validation.Count + routing.Count == 0) return null;
        var values = await conditions.ValuesAsync(rec.Type, rec.Id, ct);
        var blocked = validation.Where(r => Holds(r, values)).ToList();
        if (blocked.Count > 0) throw new AppException(string.Join(" ", blocked.Select(b => b.Message)));
        DateTimeOffset? due = null;
        foreach (var r in routing.Where(r => Holds(r, values)))
        {
            var d = clock.UtcNow.AddDays(r.DueDays ?? 5);
            if (due is null || d < due) due = d;
            await NotifyAsync(r, rec, $"Routed to you: {rec.Number}", ct);
            await LogAsync(r, rec.Type, rec.Id, $"routed to {r.TargetPermission}, due in {r.DueDays} days", null, null, rec.SiteId, ct);
        }
        return due;
    }

    /// <summary>Background: escalation rules over pending approvals (once per rule and request).</summary>
    public async Task<int> EscalateAsync(CancellationToken ct)
    {
        var rules = await db.BusinessRules.AsNoTracking().Where(r => r.Enabled && r.Kind == RuleKind.Escalation).ToListAsync(ct);
        var n = 0;
        foreach (var rule in rules)
        {
            var cutoff = clock.UtcNow.AddHours(-(rule.AfterHours ?? 24));
            var pending = await db.ApprovalRequests.AsNoTracking().Where(a => a.Status == ApprovalStatus.Pending && a.RecordType == rule.RecordType && a.CreatedAt <= cutoff && (rule.SiteId == null || a.SiteId == rule.SiteId)).Take(500).ToListAsync(ct);
            foreach (var a in pending)
            {
                if (await db.RuleFirings.AnyAsync(f => f.RuleId == rule.Id && f.ApprovalRequestId == a.Id, ct)) continue;
                var values = await conditions.ValuesAsync(a.RecordType, a.RecordId, ct);
                if (!Holds(rule, values)) continue;
                var rec = await records.GetAsync(a.RecordType, a.RecordId, ct);
                await NotifyAsync(rule, rec, $"Escalation: {a.RecordNumber} pending more than {rule.AfterHours} h", ct);
                await LogAsync(rule, a.RecordType, a.RecordId, $"escalated to {rule.TargetPermission}", null, a.Id, a.SiteId, ct);
                n++;
            }
        }
        return n;
    }

    /// <summary>Automation rules over new platform events (own cursor, so every event is considered once).</summary>
    public async Task<int> ProcessEventsAsync(CancellationToken ct)
    {
        var cursor = await db.PlatformCursors.FirstOrDefaultAsync(c => c.Key == "rules", ct);
        if (cursor is null) { cursor = new PlatformCursor { Key = "rules", Position = 0 }; db.PlatformCursors.Add(cursor); await db.SaveChangesAsync(ct); }
        var rules = await db.BusinessRules.AsNoTracking().Where(r => r.Enabled && r.Kind == RuleKind.Automation).ToListAsync(ct);
        var events = await db.PlatformEvents.AsNoTracking().Where(e => e.Id > cursor.Position).OrderBy(e => e.Id).Take(2000).ToListAsync(ct);
        var n = 0;
        foreach (var e in events)
        {
            foreach (var rule in rules.Where(r => r.Trigger == e.Type && r.RecordType == e.RecordType && e.RecordId != null))
            {
                RecordInfo rec;
                try { rec = await records.GetAsync(rule.RecordType, e.RecordId!.Value, ct); } catch (NotFoundException) { continue; }
                if (rule.SiteId is { } s && rec.SiteId != s) continue;
                if (!Holds(rule, await conditions.ValuesAsync(rule.RecordType, rec.Id, ct))) continue;
                await NotifyAsync(rule, rec, $"{rule.Name}: {rec.Number}", ct);
                await LogAsync(rule, rec.Type, rec.Id, $"on {e.Type} notified {rule.TargetPermission}", e.Id, null, rec.SiteId, ct);
                n++;
            }
        }
        if (events.Count > 0) { cursor.Position = events[^1].Id; await db.SaveChangesAsync(ct); }
        return n;
    }
}
