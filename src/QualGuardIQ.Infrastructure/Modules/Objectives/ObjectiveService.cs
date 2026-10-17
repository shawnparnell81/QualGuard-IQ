using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Objectives;

public sealed record ObjectiveInput(string Title, string? Description, Guid SiteId, Guid? DepartmentId, string? Process, Guid? OwnerId, string MetricName, string MetricDefinition, string DataSource,
    string? Unit, KpiDirection Direction, KpiFrequency Frequency, decimal? Baseline, decimal Target, decimal Warning, decimal Critical, bool TriggerActionOnCritical, string? ActionPlan,
    DateOnly StartsOn, DateOnly? EndsOn, uint RowVersion = 0, KpiLevel Level = KpiLevel.Site, Guid? OrgUnitId = null);
public sealed record MeasurementInput(DateOnly PeriodStart, decimal? Value, string? Notes);

/// <summary>
/// Quality objectives and KPIs. Draft → (approval via workflow) → Active → Achieved/Retired. Targets and thresholds
/// can only change in Draft; changing an active objective's targets means returning it to draft and re-approving.
/// Calculated KPIs use KpiCalculator; manual KPIs take a value. A critical result raises an action on the objective
/// when the trigger is enabled and no action is already open.
/// </summary>
public sealed class ObjectiveService(AppDbContext db, ModuleSupport m, INumberingService numbers, WorkflowService workflow, ActionService actions, KpiCalculator calc, INotifier notifier)
{
    public const string RecordType = "QualityObjective";

    public async Task<QualityObjective> CreateAsync(ObjectiveInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.ObjectivesEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        var owner = r.OwnerId ?? m.UserId;
        await m.RequireActiveUserAsync(owner, "owner", ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var o = new QualityObjective { SiteId = r.SiteId, DepartmentId = r.DepartmentId, OwnerId = owner, Status = ObjectiveStatus.Draft, Number = await numbers.NextAsync(RecordType, r.SiteId, ct) };
        Apply(o, r);
        db.QualityObjectives.Add(o);
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, o.Id, $"{o.MetricName} {o.Process}", ct);
        await tx.CommitAsync(ct);
        return o;
    }

    public async Task UpdateAsync(Guid id, ObjectiveInput r, CancellationToken ct)
    {
        var o = await LoadEditAsync(id, ct);
        if (r.SiteId != o.SiteId) throw new AppException("The site of an objective cannot change.");
        db.Entry(o).Property(x => x.RowVersion).OriginalValue = r.RowVersion;
        if (o.Status == ObjectiveStatus.Draft) Apply(o, r);
        else if (o.Status == ObjectiveStatus.Active)
        {
            // Only narrative fields change on an active objective; targets need re-approval.
            if (r.Target != o.Target || r.Warning != o.Warning || r.Critical != o.Critical || r.DataSource != o.DataSource || r.Direction != o.Direction || r.Frequency != o.Frequency || r.MetricDefinition.Trim() != o.MetricDefinition)
                throw new ConflictException("Targets, thresholds and the metric of an active objective cannot change. Return it to draft for re-approval.");
            o.Description = T(r.Description); o.ActionPlan = T(r.ActionPlan); o.Process = T(r.Process); o.EndsOn = r.EndsOn; o.TriggerActionOnCritical = r.TriggerActionOnCritical;
        }
        else throw new ConflictException("This objective cannot be edited in its current state.");
        if (r.OwnerId is { } ow && ow != o.OwnerId) { await m.RequireActiveUserAsync(ow, "owner", ct); o.OwnerId = ow; }
        o.DepartmentId = r.DepartmentId;
        await db.SaveChangesAsync(ct);
    }

    private static void Apply(QualityObjective o, ObjectiveInput r)
    {
        ModuleSupport.Required(r.Title, "Objective", 300);
        ModuleSupport.Required(r.MetricName, "Metric", 200);
        ModuleSupport.Required(r.MetricDefinition, "Metric definition", 4000);
        var source = string.IsNullOrWhiteSpace(r.DataSource) ? "manual" : r.DataSource.Trim();
        if (source != "manual" && KpiCalculator.Find(source) is null) throw new AppException("Choose a data source from the controlled calculations, or manual entry.");
        if (r.EndsOn is { } e && e < r.StartsOn) throw new AppException("The end date cannot be before the start date.");
        // Thresholds must be ordered in the direction of improvement: target better than warning boundary, warning better than critical.
        var ordered = r.Direction == KpiDirection.HigherIsBetter ? r.Target >= r.Warning && r.Warning >= r.Critical : r.Target <= r.Warning && r.Warning <= r.Critical;
        if (!ordered) throw new AppException(r.Direction == KpiDirection.HigherIsBetter ? "For higher-is-better metrics use target ≥ warning ≥ critical." : "For lower-is-better metrics use target ≤ warning ≤ critical.");
        o.Title = r.Title.Trim(); o.Description = T(r.Description); o.Process = T(r.Process); o.MetricName = r.MetricName.Trim(); o.Level = r.Level; o.OrgUnitId = r.Level is KpiLevel.Corporate or KpiLevel.Regional ? r.OrgUnitId : null; o.MetricDefinition = r.MetricDefinition.Trim(); o.DataSource = source;
        o.Unit = T(r.Unit) ?? KpiCalculator.Find(source)?.Unit; o.Direction = r.Direction; o.Frequency = r.Frequency; o.Baseline = r.Baseline; o.Target = r.Target; o.Warning = r.Warning; o.Critical = r.Critical;
        o.TriggerActionOnCritical = r.TriggerActionOnCritical; o.ActionPlan = T(r.ActionPlan); o.StartsOn = r.StartsOn; o.EndsOn = r.EndsOn;
    }

    public async Task SubmitAsync(Guid id, CancellationToken ct)
    {
        var o = await LoadEditAsync(id, ct);
        if (o.Status != ObjectiveStatus.Draft) throw new ConflictException("Only a draft objective can be submitted.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        o.Status = ObjectiveStatus.PendingApproval;
        await db.SaveChangesAsync(ct);
        await workflow.SubmitAsync(RecordType, id, ct);
        await tx.CommitAsync(ct);
    }

    public async Task OnApprovedAsync(Guid id, CancellationToken ct)
    {
        var o = await db.QualityObjectives.FirstAsync(x => x.Id == id, ct);
        o.Status = ObjectiveStatus.Active; o.ApprovedAt = m.Now;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([o.OwnerId], $"Objective {o.Number} approved", o.Title, await m.RecordAsync(RecordType, id, ct), ct: ct);
    }

    public async Task OnRejectedAsync(Guid id, string? comment, CancellationToken ct)
    {
        var o = await db.QualityObjectives.FirstAsync(x => x.Id == id, ct);
        o.Status = ObjectiveStatus.Draft;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([o.OwnerId], $"Objective {o.Number} returned", comment, await m.RecordAsync(RecordType, id, ct), ct: ct);
    }

    public async Task ReviseAsync(Guid id, string reason, CancellationToken ct)
    {
        var o = await LoadEditAsync(id, ct);
        if (o.Status != ObjectiveStatus.Active) throw new ConflictException("Only an active objective can be revised.");
        ModuleSupport.Required(reason, "Reason", 4000);
        o.Status = ObjectiveStatus.Draft; o.ApprovedAt = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetOutcomeAsync(Guid id, bool achieved, CancellationToken ct)
    {
        var o = await LoadEditAsync(id, ct);
        if (o.Status != ObjectiveStatus.Active) throw new ConflictException("Only an active objective can be closed out.");
        o.Status = achieved ? ObjectiveStatus.Achieved : ObjectiveStatus.Retired;
        await db.SaveChangesAsync(ct);
    }

    public static (DateOnly Start, DateOnly End) Period(KpiFrequency f, DateOnly anyDay) => f switch
    {
        KpiFrequency.Monthly => (new DateOnly(anyDay.Year, anyDay.Month, 1), new DateOnly(anyDay.Year, anyDay.Month, 1).AddMonths(1).AddDays(-1)),
        KpiFrequency.Quarterly => (new DateOnly(anyDay.Year, (anyDay.Month - 1) / 3 * 3 + 1, 1), new DateOnly(anyDay.Year, (anyDay.Month - 1) / 3 * 3 + 1, 1).AddMonths(3).AddDays(-1)),
        _ => (new DateOnly(anyDay.Year, 1, 1), new DateOnly(anyDay.Year, 12, 31)),
    };

    /// <summary>Records (manual) or calculates (controlled source) the KPI for the period containing PeriodStart. Supersedes an earlier result for the same period.</summary>
    public async Task<KpiMeasurement> MeasureAsync(Guid id, MeasurementInput r, CancellationToken ct)
    {
        var o = await LoadEditAsync(id, ct);
        if (o.Status != ObjectiveStatus.Active) throw new ConflictException("Only an approved, active objective is measured.");
        var (start, end) = Period(o.Frequency, r.PeriodStart);
        if (start > await m.TodayAsync(o.SiteId, ct)) throw new AppException("The period has not started yet.");
        if (end < o.StartsOn || (o.EndsOn is { } e && start > e)) throw new AppException("The period is outside the objective's dates.");
        if (r.Notes?.Length > 4000) throw new AppException("Notes are limited to 4000 characters.");
        decimal value; string? basis = null; var calculated = o.DataSource != "manual";
        if (calculated) { var res = await calc.CalculateAsync(o.DataSource, o.SiteId, o.DepartmentId, start, end, ct); value = res.Value; basis = res.Basis; }
        else value = r.Value ?? throw new AppException("Enter the measured value.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var meas = new KpiMeasurement { ObjectiveId = id, PeriodStart = start, PeriodEnd = end, Value = value, Rating = KpiCalculator.Rate(o.Direction, value, o.Target, o.Warning, o.Critical),
            Calculated = calculated, Basis = basis, Notes = T(r.Notes), RecordedById = m.UserId };
        db.KpiMeasurements.Add(meas);
        foreach (var old in await db.KpiMeasurements.Where(x => x.ObjectiveId == id && x.PeriodStart == start && x.SupersededById == null).ToListAsync(ct)) old.SupersededById = meas.Id;
        await db.SaveChangesAsync(ct);
        if (meas.Rating == KpiRating.Critical && o.TriggerActionOnCritical && await actions.AllClosedAsync(RecordType, id, ct))
            await actions.CreateAsync(RecordType, id, new CreateActionRequest($"{o.MetricName} critical for {start:yyyy-MM}: {value}{o.Unit switch { "%" => "%", null => "", var u => " " + u }}",
                $"Result {value} is beyond the critical threshold {o.Critical}. Investigate and act. {o.ActionPlan}".Trim(), "Corrective", o.OwnerId, (await m.TodayAsync(o.SiteId, ct)).AddDays(30), true), ct);
        await tx.CommitAsync(ct);
        return meas;
    }

    private async Task<QualityObjective> LoadEditAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(RecordType, id, ct);
        return await db.QualityObjectives.FirstAsync(x => x.Id == id, ct);
    }

    private static string? T(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
