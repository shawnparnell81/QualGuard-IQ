using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Objectives;

public sealed record KpiResult(decimal Value, string Basis);
public sealed record KpiDefinition(string Key, string Label, string Unit, KpiDirection Direction, string Formula);

/// <summary>
/// Controlled KPI calculations. Each metric is a documented formula over system records for one site (and department, when set)
/// and one period [start, end] inclusive. Dates are business dates stored on the records; completion timestamps are compared by UTC date.
/// Percentages with an empty denominator return 100 (nothing was due), and the basis says so.
/// </summary>
public sealed class KpiCalculator(AppDbContext db)
{
    public static readonly KpiDefinition[] Definitions =
    [
        new("ncr.opened", "NCRs opened", "count", KpiDirection.LowerIsBetter, "Count of NCRs with detected date in the period (cancelled excluded)."),
        new("ncr.open_over_30", "NCRs open more than 30 days", "count", KpiDirection.LowerIsBetter, "Open NCRs at period end whose detected date is more than 30 days before period end."),
        new("actions.on_time", "Actions completed on time", "%", KpiDirection.HigherIsBetter, "Actions due in the period completed on or before the due date ÷ actions due in the period (cancelled excluded) × 100."),
        new("actions.overdue", "Overdue actions", "count", KpiDirection.LowerIsBetter, "Actions not completed by period end whose due date is before period end."),
        new("complaints.received", "Customer complaints", "count", KpiDirection.LowerIsBetter, "Complaints reported in the period (cancelled excluded)."),
        new("inspection.first_pass", "Inspection first-pass yield", "%", KpiDirection.HigherIsBetter, "Inspections dated in the period that passed ÷ completed inspections dated in the period × 100."),
        new("training.on_time", "Training completed on time", "%", KpiDirection.HigherIsBetter, "Assignments due in the period completed on or before the due date ÷ assignments due in the period (cancelled/superseded excluded) × 100."),
        new("calibration.overdue", "Equipment overdue for calibration", "count", KpiDirection.LowerIsBetter, "Calibrated, non-retired equipment whose next due date is before period end and not recalibrated by then (current snapshot)."),
        new("audit.findings", "Audit nonconformities", "count", KpiDirection.LowerIsBetter, "Major and minor nonconformities raised in the period."),
        new("capa.closed", "CAPAs closed", "count", KpiDirection.HigherIsBetter, "CAPAs closed in the period."),
    ];

    public static KpiDefinition? Find(string key) => Definitions.FirstOrDefault(d => d.Key == key);

    public async Task<KpiResult> CalculateAsync(string key, Guid siteId, Guid? departmentId, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var from = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var to = new DateTimeOffset(end.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        IQueryable<T> S<T>(IQueryable<T> q) where T : Domain.Common.ISiteScoped => departmentId is { } d ? q.Where(x => x.SiteId == siteId && x.DepartmentId == d) : q.Where(x => x.SiteId == siteId);
        static KpiResult Pct(int num, int den, string what) => den == 0 ? new(100m, $"No {what} in the period") : new(Math.Round(100m * num / den, 2), $"{num} of {den} {what}");
        switch (key)
        {
            case "ncr.opened":
            {
                var n = await S(db.Ncrs).CountAsync(x => x.DetectedOn >= start && x.DetectedOn <= end && x.Status != NcrStatus.Cancelled, ct);
                return new(n, $"{n} NCRs detected {start:yyyy-MM-dd}..{end:yyyy-MM-dd}");
            }
            case "ncr.open_over_30":
            {
                var limit = end.AddDays(-30);
                var n = await S(db.Ncrs).CountAsync(x => x.DetectedOn < limit && x.Status != NcrStatus.Cancelled && (x.ClosedAt == null || x.ClosedAt >= to), ct);
                return new(n, $"{n} NCRs detected before {limit:yyyy-MM-dd} still open at {end:yyyy-MM-dd}");
            }
            case "actions.on_time":
            {
                var due = S(db.ActionItems).Where(x => x.DueDate >= start && x.DueDate <= end && x.Status != ActionStatus.Cancelled);
                var rows = await due.Select(x => new { x.DueDate, x.CompletedAt }).ToListAsync(ct);
                var ok = rows.Count(r => r.CompletedAt is { } c && DateOnly.FromDateTime(c.UtcDateTime) <= r.DueDate);
                return Pct(ok, rows.Count, "actions due");
            }
            case "actions.overdue":
            {
                var n = await S(db.ActionItems).CountAsync(x => x.DueDate < end && x.Status != ActionStatus.Cancelled && (x.CompletedAt == null || x.CompletedAt >= to), ct);
                return new(n, $"{n} actions due before {end:yyyy-MM-dd} not completed by then");
            }
            case "complaints.received":
            {
                var n = await S(db.Complaints).CountAsync(x => x.ReportedOn >= start && x.ReportedOn <= end && x.Status != ComplaintStatus.Cancelled, ct);
                return new(n, $"{n} complaints reported {start:yyyy-MM-dd}..{end:yyyy-MM-dd}");
            }
            case "inspection.first_pass":
            {
                var q = S(db.Inspections).Where(x => x.InspectedOn >= start && x.InspectedOn <= end && x.Status != InspectionStatus.InProgress && x.Status != InspectionStatus.Cancelled);
                var total = await q.CountAsync(ct);
                // Passed or released after passing; a failed inspection that is later released still counts as not first-pass.
                var passed = await q.CountAsync(x => x.Status == InspectionStatus.Passed || (x.Status == InspectionStatus.Released && !db.InspectionResults.Any(r => r.InspectionId == x.Id && !r.Pass)), ct);
                return Pct(passed, total, "inspections passed first time");
            }
            case "training.on_time":
            {
                var rows = await S(db.TrainingAssignments).Where(x => x.DueOn >= start && x.DueOn <= end && x.Status != AssignmentStatus.Cancelled && x.Status != AssignmentStatus.Superseded)
                    .Select(x => new { x.DueOn, x.CompletedAt }).ToListAsync(ct);
                var ok = rows.Count(r => r.CompletedAt is { } c && DateOnly.FromDateTime(c.UtcDateTime) <= r.DueOn);
                return Pct(ok, rows.Count, "training assignments due");
            }
            case "calibration.overdue":
            {
                var n = await S(db.Equipment).CountAsync(x => x.RequiresCalibration && x.Status != EquipmentStatus.Retired && x.NextDueOn != null && x.NextDueOn < end, ct);
                return new(n, $"{n} items with calibration due before {end:yyyy-MM-dd}");
            }
            case "audit.findings":
            {
                var n = await S(db.AuditFindings).CountAsync(x => x.CreatedAt >= from && x.CreatedAt < to && (x.FindingType == FindingType.MajorNonconformity || x.FindingType == FindingType.MinorNonconformity), ct);
                return new(n, $"{n} nonconformities raised {start:yyyy-MM-dd}..{end:yyyy-MM-dd}");
            }
            case "capa.closed":
            {
                var n = await S(db.Capas).CountAsync(x => x.ClosedAt >= from && x.ClosedAt < to && x.Status == CapaStatus.Closed, ct);
                return new(n, $"{n} CAPAs closed {start:yyyy-MM-dd}..{end:yyyy-MM-dd}");
            }
            default: throw new AppException($"Unknown KPI calculation '{key}'.");
        }
    }

    /// <summary>Rates a value against thresholds in the metric's direction.</summary>
    public static KpiRating Rate(KpiDirection dir, decimal value, decimal target, decimal warning, decimal critical) => dir == KpiDirection.HigherIsBetter
        ? value >= target ? KpiRating.OnTarget : value > critical ? KpiRating.Warning : KpiRating.Critical
        : value <= target ? KpiRating.OnTarget : value < critical ? KpiRating.Warning : KpiRating.Critical;
}
