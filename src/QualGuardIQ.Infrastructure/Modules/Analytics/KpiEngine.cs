using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Analytics;

public enum KpiPeriod { Daily = 0, Weekly = 1, Monthly = 2, Quarterly = 3, Annual = 4 }
public sealed record KpiPoint(string Period, DateOnly Start, DateOnly End, decimal? Value, decimal Numerator, decimal Denominator);
public sealed record KpiSeries(string Key, string Label, string Unit, string Formula, KpiPeriod Period, List<KpiPoint> Points);

/// <summary>
/// Advanced KPI engine: PPM, DPPM, FPY, RTY, COPQ, scrap %, rework %, warranty %, supplier performance and audit completion,
/// bucketed daily / weekly (ISO, Monday start) / monthly / quarterly / annual. Every value carries numerator and denominator,
/// and every source is filtered to what the caller may view.
/// </summary>
public sealed class KpiEngine(AppDbContext db, ModuleSupport m)
{
    public static readonly (string Key, string Label, string Unit, string Formula)[] Definitions =
    [
        ("ppm", "Customer PPM", "ppm", "Units complained or returned by customers ÷ units shipped × 1,000,000."),
        ("dppm", "Supplier DPPM", "ppm", "Defects found in supplier AQL samples ÷ units sampled × 1,000,000."),
        ("fpy", "First-pass yield", "%", "First-pass good units ÷ (good + rejected + scrapped) units reported on the shop floor × 100."),
        ("rty", "Rolled throughput yield", "%", "Product of each operation's first-pass yield (first-pass good ÷ processed), averaged over work orders with activity × 100."),
        ("copq", "Cost of poor quality", "USD", "Internal + external failure cost entries plus scrap and rework cost in the period."),
        ("scrap", "Scrap %", "%", "Scrapped units ÷ units processed on the shop floor × 100."),
        ("rework", "Rework %", "%", "Reworked units ÷ units processed on the shop floor × 100."),
        ("warranty", "Warranty %", "%", "Warranty-claimed units ÷ units shipped × 100."),
        ("supplier", "Supplier performance", "%", "Accepted supplier AQL lots ÷ decided supplier AQL lots × 100."),
        ("audits", "Audit completion", "%", "Audits completed or closed ÷ audits planned to start in the period × 100."),
    ];

    public static List<(DateOnly Start, DateOnly End, string Label)> Buckets(KpiPeriod p, DateOnly from, DateOnly to)
    {
        var list = new List<(DateOnly, DateOnly, string)>();
        DateOnly s = p switch
        {
            KpiPeriod.Weekly => from.AddDays(-(((int)from.DayOfWeek + 6) % 7)),
            KpiPeriod.Monthly => new DateOnly(from.Year, from.Month, 1),
            KpiPeriod.Quarterly => new DateOnly(from.Year, (from.Month - 1) / 3 * 3 + 1, 1),
            KpiPeriod.Annual => new DateOnly(from.Year, 1, 1),
            _ => from
        };
        while (s <= to && list.Count < 400)
        {
            var e = p switch { KpiPeriod.Daily => s, KpiPeriod.Weekly => s.AddDays(6), KpiPeriod.Monthly => s.AddMonths(1).AddDays(-1), KpiPeriod.Quarterly => s.AddMonths(3).AddDays(-1), _ => s.AddYears(1).AddDays(-1) };
            var label = p switch
            {
                KpiPeriod.Daily => s.ToString("yyyy-MM-dd"), KpiPeriod.Weekly => $"{System.Globalization.ISOWeek.GetYear(s.ToDateTime(TimeOnly.MinValue))}-W{System.Globalization.ISOWeek.GetWeekOfYear(s.ToDateTime(TimeOnly.MinValue)):00}",
                KpiPeriod.Monthly => s.ToString("yyyy-MM"), KpiPeriod.Quarterly => $"{s.Year}-Q{(s.Month - 1) / 3 + 1}", _ => s.Year.ToString()
            };
            list.Add((s, e, label));
            s = e.AddDays(1);
        }
        return list;
    }


    public async Task<KpiSeries> SeriesAsync(string key, KpiPeriod period, DateOnly from, DateOnly to, Guid? siteId, CancellationToken ct)
    {
        var def = Definitions.FirstOrDefault(d => d.Key == key);
        if (def.Key is null) throw new AppException($"Unknown KPI. Available: {string.Join(", ", Definitions.Select(d => d.Key))}.");
        if (to < from) throw new AppException("The end date is before the start date.");
        var buckets = Buckets(period, from, to);
        var points = new List<KpiPoint>();
        async Task<IQueryable<T>?> Q<T>(IQueryable<T> q, string perm) where T : class, Domain.Common.ISiteScoped
        {
            var s = await m.TryScopeAsync(q, perm, ct);
            return s is null ? null : siteId is { } sid ? s.Where(x => x.SiteId == sid) : s;
        }
        var pe = await Q(db.ProductionEntries.AsNoTracking().Where(x => x.Kind == ProductionEntryKind.Production), Permissions.ProductionView);
        var aql = await Q(db.AqlInspections.AsNoTracking().Where(x => x.SupplierId != null), Permissions.InspectionView);
        var ship = await Q(db.TraceEvents.AsNoTracking().Where(x => x.Kind == TraceEventKind.Shipment), Permissions.TraceView);
        foreach (var (s, e, label) in buckets)
        {
            var (f, t) = await m.DayRangeAsync(siteId, s, e, ct);
            decimal num = 0, den = 0; decimal? val = null;
            switch (key)
            {
                case "ppm" or "warranty":
                {
                    den = ship is null ? 0 : await ship.Where(x => x.OccurredAt >= f && x.OccurredAt < t).SumAsync(x => x.Quantity ?? 0, ct);
                    if (key == "ppm")
                    {
                        var c = await Q(db.Complaints.AsNoTracking(), Permissions.ComplaintsView);
                        var r = await Q(db.CustomerReturns.AsNoTracking(), Permissions.ReturnsView);
                        num = (c is null ? 0 : await c.Where(x => x.ReportedOn >= s && x.ReportedOn <= e && x.Status != ComplaintStatus.Cancelled && x.ReturnAuthorization == null).SumAsync(x => x.Quantity ?? 1, ct))
                            + (r is null ? 0 : await r.Where(x => x.AuthorizedOn >= s && x.AuthorizedOn <= e && x.Status != ReturnStatus.Cancelled).SumAsync(x => x.Quantity, ct));
                        val = den == 0 ? null : Math.Round(num / den * 1_000_000m, 1);
                    }
                    else
                    {
                        var w = await Q(db.WarrantyClaims.AsNoTracking(), Permissions.ReturnsView);
                        num = w is null ? 0 : await w.Where(x => x.ClaimedOn >= s && x.ClaimedOn <= e && x.Status != WarrantyClaimStatus.Rejected).SumAsync(x => x.Quantity, ct);
                        val = den == 0 ? null : Math.Round(num / den * 100m, 3);
                    }
                    break;
                }
                case "dppm":
                    if (aql is not null)
                    {
                        var rows = await aql.Where(x => x.InspectionDate >= f && x.InspectionDate < t && x.OverallResult != null).Select(x => new { x.SampleSize, D = x.CriticalDefects + x.MajorDefects + x.MinorDefects }).ToListAsync(ct);
                        num = rows.Sum(x => x.D); den = rows.Sum(x => x.SampleSize);
                    }
                    val = den == 0 ? null : Math.Round(num / den * 1_000_000m, 1);
                    break;
                case "supplier":
                    if (aql is not null)
                    {
                        var d = aql.Where(x => x.InspectionDate >= f && x.InspectionDate < t && (x.OverallResult == AqlDecision.Accept || x.OverallResult == AqlDecision.Reject));
                        den = await d.CountAsync(ct); num = await d.CountAsync(x => x.OverallResult == AqlDecision.Accept, ct);
                    }
                    val = den == 0 ? null : Math.Round(num / den * 100m, 1);
                    break;
                case "fpy" or "scrap" or "rework":
                    if (pe is not null)
                    {
                        var rows = await pe.Where(x => x.RecordedAt >= f && x.RecordedAt < t).Select(x => new { x.Good, x.Rejected, x.Scrapped, x.Reworked, x.FirstPass }).ToListAsync(ct);
                        den = rows.Sum(x => x.Good + x.Rejected + x.Scrapped);
                        num = key switch { "fpy" => rows.Where(x => x.FirstPass).Sum(x => x.Good), "scrap" => rows.Sum(x => x.Scrapped), _ => rows.Sum(x => x.Reworked) };
                    }
                    val = den == 0 ? null : Math.Round(num / den * 100m, 2);
                    break;
                case "rty":
                    if (pe is not null)
                    {
                        var rows = await pe.Where(x => x.RecordedAt >= f && x.RecordedAt < t && x.WorkOrderId != null && x.OperationSequence != null)
                            .GroupBy(x => new { x.WorkOrderId, x.OperationSequence })
                            .Select(g => new { g.Key.WorkOrderId, Fp = g.Where(x => x.FirstPass).Sum(x => x.Good), Total = g.Where(x => x.FirstPass).Sum(x => x.Good + x.Rejected + x.Scrapped) }).ToListAsync(ct);
                        var perWo = rows.Where(r => r.Total > 0).GroupBy(r => r.WorkOrderId).Select(g => g.Aggregate(1m, (acc, r) => acc * (r.Fp / r.Total))).ToList();
                        den = perWo.Count; num = perWo.Sum();
                    }
                    val = den == 0 ? null : Math.Round(num / den * 100m, 2);
                    break;
                case "copq":
                {
                    var coq = await Q(db.CoqEntries.AsNoTracking(), Permissions.CoqView);
                    var scr = await Q(db.ScrapEvents.AsNoTracking(), Permissions.CoqView);
                    var rw = await Q(db.ReworkOrders.AsNoTracking(), Permissions.CoqView);
                    num = (coq is null ? 0 : await coq.Where(x => x.IncurredOn >= s && x.IncurredOn <= e && (x.Category == CoqCategory.InternalFailure || x.Category == CoqCategory.ExternalFailure) && x.SourceType == null).SumAsync(x => x.Amount, ct))
                        + (scr is null ? 0 : await scr.Where(x => x.ScrappedOn >= s && x.ScrappedOn <= e).SumAsync(x => x.TotalCost, ct))
                        + (rw is null ? 0 : await rw.Where(x => x.CreatedAt >= f && x.CreatedAt < t).SumAsync(x => x.TotalCost, ct));
                    den = 1; val = num;
                    break;
                }
                case "audits":
                {
                    var a = await Q(db.Audits.AsNoTracking(), Permissions.AuditsView);
                    if (a is not null)
                    {
                        var planned = a.Where(x => x.PlannedStart >= s && x.PlannedStart <= e && x.Status != AuditStatus.Cancelled);
                        den = await planned.CountAsync(ct); num = await planned.CountAsync(x => x.Status == AuditStatus.Reporting || x.Status == AuditStatus.Closed, ct);
                    }
                    val = den == 0 ? null : Math.Round(num / den * 100m, 1);
                    break;
                }
            }
            points.Add(new(label, s, e, val, num, den));
        }
        return new KpiSeries(def.Key, def.Label, def.Unit, def.Formula, period, points);
    }
}
