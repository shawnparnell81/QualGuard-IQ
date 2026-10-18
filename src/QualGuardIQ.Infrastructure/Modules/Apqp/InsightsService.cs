using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Common;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Apqp;

/// <summary>One dashboard metric: value (null when there is no data or no access), the counts behind it, and how it is defined.</summary>
public sealed record Metric(string Key, string Label, decimal? Value, string Unit, decimal? Numerator, decimal? Denominator, string Definition, string? DrillUrl,
    string? DenominatorSource = null, string? DenominatorLabel = null, bool IsFallback = false, string? CalculationVersion = null);

/// <summary>Resolved denominator for a rate metric: the source used, its quantity, whether it was a fallback, and a human label.</summary>
public sealed record DenominatorChoice(DenominatorSource? Source, decimal Quantity, bool IsFallback, string Label);
public sealed record GraphNode(string Type, Guid Id, string Number, string Title, string? Status, string Url, int Depth);
public sealed record GraphEdge(string FromType, Guid FromId, string ToType, Guid ToId, string Label);

/// <summary>
/// Production and executive dashboards, the record relationship graph and the quality data warehouse.
/// Every figure is computed from live records; dashboards only count records the user can view.
/// </summary>
public sealed class InsightsService(AppDbContext db, ModuleSupport m, IAccessService access, IRecordRegistry records, SupplierDevelopmentService suppliers)
{
    public static readonly string[] MetricKeys = ["fpy", "scrap_rate", "rework_rate", "defect_rate", "coq_total", "supplier_score", "audit_completion", "lpa_completion", "spc_in_control", "calibration_compliance", "training_compliance", "open_ncrs", "open_capas"];

    static decimal? Pct(decimal n, decimal d) => d <= 0 ? null : Math.Round(100m * n / d, 1, MidpointRounding.AwayFromZero);

    /// <summary>Calculation version of the scrap and rework rates (version 1 divided by quantity inspected without saying so).</summary>
    public const string RateCalculationVersion = "rate/2 (configurable denominator)";
    /// <summary>Default preference: actual production volume first. Quantity inspected is never used unless an administrator configures it.</summary>
    public static readonly DenominatorSource[] DefaultSources = [DenominatorSource.MesProduction, DenominatorSource.ErpProduction, DenominatorSource.Produced, DenominatorSource.Completed, DenominatorSource.ManuallyApproved];
    public static readonly string[] RateMetrics = ["scrap_rate", "rework_rate"];
    public static string SourceLabel(DenominatorSource s) => s switch
    {
        DenominatorSource.Produced => "quantity produced (all production counts)", DenominatorSource.Completed => "quantity completed on work orders",
        DenominatorSource.Received => "quantity received", DenominatorSource.Inspected => "quantity inspected", DenominatorSource.Shipped => "quantity shipped",
        DenominatorSource.ManuallyApproved => "approved manual production volume", DenominatorSource.ErpProduction => "ERP production quantity", _ => "MES production quantity"
    };

    public async Task<(DenominatorSource[] Sources, bool AllowFallback)> DenominatorConfigAsync(string metric, Guid? siteId, CancellationToken ct)
    {
        var rows = await db.MetricDenominators.AsNoTracking().Where(x => x.Metric == metric && (x.SiteId == siteId || x.SiteId == null)).ToListAsync(ct);
        var row = rows.FirstOrDefault(x => x.SiteId == siteId && siteId != null) ?? rows.FirstOrDefault(x => x.SiteId == null);
        if (row is null) return (DefaultSources, true);
        var list = row.Sources.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => Enum.Parse<DenominatorSource>(x)).ToArray();
        return (list.Length == 0 ? DefaultSources : list, row.AllowFallback);
    }

    /// <summary>Quantity for one volume source in the period (site-filtered when siteId is set; scoped by the caller's view permission when scoped).</summary>
    public async Task<decimal> VolumeAsync(DenominatorSource src, bool scoped, Guid? siteId, DateOnly f, DateOnly t, CancellationToken ct)
    {
        var s = new Scoper(m, scoped, siteId);
        var (fTs, tTs) = await m.DayRangeAsync(siteId, f, t, ct);
        switch (src)
        {
            case DenominatorSource.Produced or DenominatorSource.ErpProduction or DenominatorSource.MesProduction:
                var pe = await s.Q(db.ProductionEntries.AsNoTracking(), Permissions.ProductionView, ct);
                if (pe is null) return 0;
                pe = pe.Where(x => x.Kind == ProductionEntryKind.Production && x.FirstPass && x.RecordedAt >= fTs && x.RecordedAt < tTs);
                if (src == DenominatorSource.ErpProduction) pe = pe.Where(x => x.Source == RecordSource.Erp);
                if (src == DenominatorSource.MesProduction) pe = pe.Where(x => x.Source == RecordSource.Mes);
                return await pe.SumAsync(x => (decimal?)(x.Good + x.Rejected + x.Scrapped), ct) ?? 0;
            case DenominatorSource.Completed:
                var wo = await s.Q(db.WorkOrders.AsNoTracking(), Permissions.ProductionView, ct);
                if (wo is null) return 0;
                return await wo.Where(x => (x.Status == WorkOrderStatus.Completed || x.Status == WorkOrderStatus.Closed) && x.UpdatedAt >= fTs && x.UpdatedAt < tTs).SumAsync(x => (decimal?)x.QuantityCompleted, ct) ?? 0;
            case DenominatorSource.Received:
                var rc = await s.Q(db.Receipts.AsNoTracking(), Permissions.InventoryView, ct);
                if (rc is null) return 0;
                var ids = rc.Where(x => x.ReceivedOn >= f && x.ReceivedOn <= t && x.Status != ReceiptStatus.Cancelled).Select(x => x.Id);
                return await db.ReceiptLines.AsNoTracking().Where(l => ids.Contains(l.ReceiptId)).SumAsync(l => (decimal?)l.QuantityReceived, ct) ?? 0;
            case DenominatorSource.Inspected:
                var ins = await s.Q(db.Inspections.AsNoTracking(), Permissions.InspectionView, ct);
                if (ins is null) return 0;
                return await ins.Where(i => i.InspectedOn >= f && i.InspectedOn <= t && i.Status != InspectionStatus.InProgress && i.Status != InspectionStatus.Cancelled).SumAsync(i => (decimal?)i.QuantityReceived, ct) ?? 0;
            case DenominatorSource.Shipped:
                var sh = await s.Q(db.Shipments.AsNoTracking(), Permissions.ShippingView, ct);
                if (sh is null) return 0;
                var sids = sh.Where(x => x.ShippedAt >= fTs && x.ShippedAt < tTs && x.Status != ShipmentStatus.Cancelled).Select(x => x.Id);
                return await db.ShipmentLines.AsNoTracking().Where(l => sids.Contains(l.ShipmentId)).SumAsync(l => (decimal?)l.Quantity, ct) ?? 0;
            default:
                var pv = await s.Q(db.ProductionVolumes.AsNoTracking(), Permissions.ProductionView, ct);
                if (pv is null) return 0;
                // Prorate approved manual volumes by the days that overlap the period.
                var vols = await pv.Where(x => x.Status == ProductionVolumeStatus.Approved && x.PeriodStart <= t && x.PeriodEnd >= f).ToListAsync(ct);
                return vols.Sum(v => { var days = v.PeriodEnd.DayNumber - v.PeriodStart.DayNumber + 1; var a = Math.Max(v.PeriodStart.DayNumber, f.DayNumber); var b = Math.Min(v.PeriodEnd.DayNumber, t.DayNumber); return Math.Round(v.Quantity * (b - a + 1) / days, 6); });
        }
    }

    public static List<DenominatorSource> ParseSources(IEnumerable<string>? names) =>
        (names ?? []).Select(n => Enum.TryParse<DenominatorSource>(n, true, out var v) && Enum.IsDefined(v) && !int.TryParse(n, out _) ? v
            : throw new AppException($"Unknown volume source '{n}'. Use one of: {string.Join(", ", Enum.GetNames<DenominatorSource>())}.")).ToList();

    public async Task SaveDenominatorAsync(string metric, Guid? siteId, IReadOnlyList<DenominatorSource> sources, bool allowFallback, CancellationToken ct)
    {
        if (!RateMetrics.Contains(metric)) throw new AppException($"Denominators are configurable for: {string.Join(", ", RateMetrics)}.");
        if (sources.Count is 0 or > 8 || sources.Distinct().Count() != sources.Count) throw new AppException("List 1 to 8 different volume sources, most preferred first.");
        if (siteId is { } sid && !await db.Sites.AnyAsync(x => x.Id == sid, ct)) throw new NotFoundException("Site");
        var row = await db.MetricDenominators.FirstOrDefaultAsync(x => x.Metric == metric && x.SiteId == siteId, ct);
        if (row is null) { row = new MetricDenominatorConfig { Metric = metric, SiteId = siteId }; db.MetricDenominators.Add(row); }
        row.Sources = string.Join(",", sources); row.AllowFallback = allowFallback;
        await db.SaveChangesAsync(ct);
    }

    public async Task<ProductionVolume> AddVolumeAsync(Guid siteId, DateOnly from, DateOnly to, decimal quantity, string? notes, CancellationToken ct)
    {
        await m.ValidateLocationAsync(siteId, null, ct);
        await m.RequireAsync(Permissions.ProductionEdit, siteId, null, ct);
        if (to < from) throw new AppException("The period ends before it starts.");
        if (quantity < 0) throw new AppException("Quantity cannot be negative.");
        var v = new ProductionVolume { SiteId = siteId, PeriodStart = from, PeriodEnd = to, Quantity = quantity, Notes = notes?.Trim(), Status = ProductionVolumeStatus.Pending };
        db.ProductionVolumes.Add(v);
        await db.SaveChangesAsync(ct);
        return v;
    }

    /// <summary>A second person approves or rejects a manual volume; the person who entered it cannot.</summary>
    public async Task DecideVolumeAsync(Guid id, bool approve, CancellationToken ct)
    {
        var v = await db.ProductionVolumes.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Production volume");
        await m.RequireAsync(Permissions.ProductionEdit, v.SiteId, v.DepartmentId, ct);
        if (v.Status != ProductionVolumeStatus.Pending) throw new ConflictException("This volume has already been decided.");
        if (v.CreatedById == m.UserId) throw new ForbiddenException("The person who entered a production volume cannot approve it.");
        v.Status = approve ? ProductionVolumeStatus.Approved : ProductionVolumeStatus.Rejected; v.DecidedById = m.UserId; v.DecidedAt = m.Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<ProductionVolume>> VolumesAsync(CancellationToken ct) =>
        await (await m.ScopeAsync(db.ProductionVolumes.AsNoTracking(), Permissions.ProductionView, ct)).OrderByDescending(x => x.PeriodStart).Take(500).ToListAsync(ct);

    /// <summary>Walks the configured sources in order and uses the first with volume. A lower source is labelled as a fallback; with none the rate has no value (no divide by zero).</summary>
    public async Task<DenominatorChoice> ResolveDenominatorAsync(string metric, bool scoped, Guid? siteId, DateOnly f, DateOnly t, CancellationToken ct)
    {
        var (sources, allowFallback) = await DenominatorConfigAsync(metric, siteId, ct);
        for (var i = 0; i < (allowFallback ? sources.Length : 1); i++)
        {
            var q = await VolumeAsync(sources[i], scoped, siteId, f, t, ct);
            if (q > 0)
                return new(sources[i], q, i > 0, i == 0 ? $"Denominator: {SourceLabel(sources[i])}" : $"Fallback denominator: {SourceLabel(sources[i])} (no {SourceLabel(sources[0])} in the period)");
        }
        return new(null, 0, false, $"No {string.Join(" / ", (allowFallback ? sources : sources.Take(1)).Select(SourceLabel))} recorded in the period, so the rate cannot be calculated.");
    }

    sealed class Scoper(ModuleSupport m, bool scoped, Guid? site)
    {
        public async Task<IQueryable<T>?> Q<T>(IQueryable<T> q, string perm, CancellationToken ct) where T : class, ISiteScoped
        {
            var s = scoped ? await m.TryScopeAsync(q, perm, ct) : q;
            if (s is not null && site is { } id) s = s.Where(x => x.SiteId == id);
            return s;
        }
    }

    public async Task<List<Metric>> ProductionAsync(Guid? siteId, DateOnly? from, DateOnly? to, CancellationToken ct) => await ComputeAsync(true, siteId, from, to, ct);

    async Task<List<Metric>> ComputeAsync(bool scoped, Guid? siteId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var today = await m.TodayAsync(siteId, ct);
        var t = to ?? today; var f = from ?? t.AddMonths(-3).AddDays(1);
        var (fTs, tTs) = await m.DayRangeAsync(siteId, f, t, ct);
        var s = new Scoper(m, scoped, siteId);
        var list = new List<Metric>();
        string Qs(string path) => $"{path}?from={f:yyyy-MM-dd}&to={t:yyyy-MM-dd}" + (siteId is { } x ? $"&siteId={x}" : "");

        var ins = await s.Q(db.Inspections.AsNoTracking(), Permissions.InspectionView, ct);
        if (ins is null) list.Add(new("fpy", "First pass yield", null, "%", null, null, "No access to inspections.", null));
        else
        {
            var done = await ins.Where(i => i.InspectedOn >= f && i.InspectedOn <= t && i.Status != InspectionStatus.InProgress && i.Status != InspectionStatus.Cancelled)
                .Select(i => new { i.Status, i.QuantityReceived }).ToListAsync(ct);
            var pass = done.Count(i => i.Status is InspectionStatus.Passed or InspectionStatus.Released);
            list.Add(new("fpy", "First pass yield", Pct(pass, done.Count), "%", pass, done.Count, "Inspections passed or released first time ÷ inspections completed in the period.", Qs("/inspection")));
        }
        var scrapQ = await s.Q(db.ScrapEvents.AsNoTracking(), Permissions.CoqView, ct);
        var reworkQ = await s.Q(db.ReworkOrders.AsNoTracking(), Permissions.CoqView, ct);
        if (scrapQ is null) list.Add(new("scrap_rate", "Scrap rate", null, "%", null, null, "No access to cost of quality.", null));
        else
        {
            var q = await scrapQ.Where(x => x.ScrappedOn >= f && x.ScrappedOn <= t).SumAsync(x => (decimal?)x.Quantity, ct) ?? 0;
            var d = await ResolveDenominatorAsync("scrap_rate", scoped, siteId, f, t, ct);
            list.Add(new("scrap_rate", "Scrap rate", Pct(q, d.Quantity), "%", q, d.Source is null ? null : d.Quantity, $"Quantity scrapped ÷ {(d.Source is { } ds ? SourceLabel(ds) : "production volume")} in the period. {d.Label}", Qs("/coq/scrap"),
                d.Source?.ToString(), d.Label, d.IsFallback, RateCalculationVersion));
        }
        if (reworkQ is null) list.Add(new("rework_rate", "Rework rate", null, "%", null, null, "No access to cost of quality.", null));
        else
        {
            var q = await reworkQ.Where(x => x.CreatedAt >= fTs && x.CreatedAt < tTs && x.Status != ReworkStatus.Rejected).SumAsync(x => (decimal?)x.Quantity, ct) ?? 0;
            var d = await ResolveDenominatorAsync("rework_rate", scoped, siteId, f, t, ct);
            list.Add(new("rework_rate", "Rework rate", Pct(q, d.Quantity), "%", q, d.Source is null ? null : d.Quantity, $"Quantity on rework orders ÷ {(d.Source is { } ds ? SourceLabel(ds) : "production volume")} in the period. {d.Label}", Qs("/coq/rework"),
                d.Source?.ToString(), d.Label, d.IsFallback, RateCalculationVersion));
        }
        var aql = await s.Q(db.AqlInspections.AsNoTracking(), Permissions.InspectionView, ct);
        if (aql is null) list.Add(new("defect_rate", "Defect rate", null, "%", null, null, "No access to AQL inspections.", null));
        else
        {
            var a = await aql.Where(x => x.OverallResult != null && x.InspectionDate >= fTs && x.InspectionDate < tTs).Select(x => new { x.SampleSize, D = x.CriticalDefects + x.MajorDefects + x.MinorDefects }).ToListAsync(ct);
            list.Add(new("defect_rate", "Defect rate", Pct(a.Sum(x => x.D), a.Sum(x => x.SampleSize)), "%", a.Sum(x => x.D), a.Sum(x => x.SampleSize), "Defects found ÷ units sampled on decided AQL lots.", Qs("/aql")));
        }
        var coq = await s.Q(db.CoqEntries.AsNoTracking(), Permissions.CoqView, ct);
        if (coq is null) list.Add(new("coq_total", "Cost of quality", null, "USD", null, null, "No access to cost of quality.", null));
        else
        {
            var total = await coq.Where(x => x.IncurredOn >= f && x.IncurredOn <= t).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
            list.Add(new("coq_total", "Cost of quality", total, "USD", null, null, "Sum of prevention, appraisal, internal and external failure costs in the period.", Qs("/coq")));
        }
        try
        {
            var sc = scoped ? await suppliers.ScorecardAsync(f, t, null, ct) : await suppliers.ScorecardAsync(f, t, null, ct, unscoped: true);
            if (siteId is { } sid) { var ids = await db.Suppliers.AsNoTracking().Where(x => x.SiteId == sid).Select(x => x.Id).ToListAsync(ct); sc = sc.Where(x => ids.Contains(x.SupplierId)).ToList(); }
            list.Add(new("supplier_score", "Supplier performance", sc.Count == 0 ? null : Math.Round(sc.Average(x => x.Overall), 1), "score", null, sc.Count, "Average weighted scorecard (8 dimensions) across suppliers.", "/suppliers/scorecard"));
        }
        catch (ForbiddenException) { list.Add(new("supplier_score", "Supplier performance", null, "score", null, null, "No access to suppliers.", null)); }
        var audits = await s.Q(db.Audits.AsNoTracking(), Permissions.AuditsView, ct);
        if (audits is null) list.Add(new("audit_completion", "Audit performance", null, "%", null, null, "No access to audits.", null));
        else
        {
            var due = await audits.Where(x => x.PlannedEnd >= f && x.PlannedEnd <= t && x.Status != AuditStatus.Cancelled).Select(x => x.Status).ToListAsync(ct);
            var closed = due.Count(x => x == AuditStatus.Closed);
            list.Add(new("audit_completion", "Audit performance", Pct(closed, due.Count), "%", closed, due.Count, "Audits closed ÷ audits planned to end in the period.", Qs("/audits")));
        }
        var lpa = await s.Q(db.LpaAudits.AsNoTracking(), Permissions.LpaView, ct);
        if (lpa is null) list.Add(new("lpa_completion", "LPA completion", null, "%", null, null, "No access to layered process audits.", null));
        else
        {
            var due = await lpa.Where(x => x.DueOn >= f && x.DueOn <= t && x.Status != LpaStatus.Scheduled).Select(x => x.Status).ToListAsync(ct);
            var ok = due.Count(x => x == LpaStatus.Completed);
            list.Add(new("lpa_completion", "LPA completion", Pct(ok, due.Count), "%", ok, due.Count, "LPAs completed ÷ LPAs completed or missed, by due date in the period.", "/lpa"));
        }
        var charts = await s.Q(db.SpcCharts.AsNoTracking(), Permissions.SpcView, ct);
        if (charts is null) list.Add(new("spc_in_control", "SPC performance", null, "%", null, null, "No access to SPC.", null));
        else
        {
            var ids = await charts.Where(c => !c.IsArchived).Select(c => c.Id).ToListAsync(ct);
            var last = await db.SpcSamples.AsNoTracking().Where(x => ids.Contains(x.ChartId)).GroupBy(x => x.ChartId)
                .Select(g => g.OrderByDescending(x => x.TakenAt).Select(x => x.Violations).FirstOrDefault()).ToListAsync(ct);
            var inControl = last.Count(v => v == null);
            list.Add(new("spc_in_control", "SPC performance", Pct(inControl, last.Count), "%", inControl, last.Count, "Active charts whose latest sample has no rule violation ÷ active charts with samples.", "/spc"));
        }
        var eq = await s.Q(db.Equipment.AsNoTracking(), Permissions.CalibrationView, ct);
        if (eq is null) list.Add(new("calibration_compliance", "Calibration compliance", null, "%", null, null, "No access to calibration.", null));
        else
        {
            var e = await eq.Where(x => x.Status == EquipmentStatus.Active && x.NextDueOn != null).Select(x => x.NextDueOn!.Value).ToListAsync(ct);
            var ok = e.Count(d => d >= today);
            list.Add(new("calibration_compliance", "Calibration compliance", Pct(ok, e.Count), "%", ok, e.Count, "Active calibrated equipment not past its due date ÷ active calibrated equipment.", "/calibration"));
        }
        var tr = await s.Q(db.TrainingAssignments.AsNoTracking(), Permissions.TrainingView, ct);
        if (tr is null) list.Add(new("training_compliance", "Training compliance", null, "%", null, null, "No access to training.", null));
        else
        {
            var a = await tr.Where(x => x.Status == AssignmentStatus.Assigned || x.Status == AssignmentStatus.Completed).Select(x => new { x.Status, x.DueOn }).ToListAsync(ct);
            var ok = a.Count(x => x.Status == AssignmentStatus.Completed || x.DueOn >= today);
            list.Add(new("training_compliance", "Training compliance", Pct(ok, a.Count), "%", ok, a.Count, "Training assignments completed or not yet due ÷ all current assignments.", "/training"));
        }
        var ncr = await s.Q(db.Ncrs.AsNoTracking(), Permissions.NcrView, ct);
        list.Add(ncr is null ? new("open_ncrs", "Open NCRs", null, "count", null, null, "No access to NCRs.", null)
            : new("open_ncrs", "Open NCRs", await ncr.CountAsync(x => x.Status != NcrStatus.Closed && x.Status != NcrStatus.Cancelled, ct), "count", null, null, "NCRs not closed or cancelled.", "/ncr?open=true"));
        var capa = await s.Q(db.Capas.AsNoTracking(), Permissions.CapaView, ct);
        list.Add(capa is null ? new("open_capas", "Open CAPAs", null, "count", null, null, "No access to CAPAs.", null)
            : new("open_capas", "Open CAPAs", await capa.CountAsync(x => x.Status != CapaStatus.Closed && x.Status != CapaStatus.Cancelled, ct), "count", null, null, "CAPAs not closed or cancelled.", "/capa?open=true"));
        return list;
    }

    /// <summary>Executive view: KPI tiles plus monthly trends and risk lists, each row with a drilldown link.</summary>
    public async Task<object> ExecutiveAsync(Guid? siteId, int months, CancellationToken ct)
    {
        months = Math.Clamp(months, 3, 36);
        var today = await m.TodayAsync(siteId, ct);
        var from = new DateOnly(today.Year, today.Month, 1).AddMonths(-(months - 1));
        var s = new Scoper(m, true, siteId);
        var tiles = (await ComputeAsync(true, siteId, from, today, ct)).Where(x => x.Key is "open_ncrs" or "open_capas" or "coq_total" or "supplier_score" or "fpy").ToList();
        var monthsList = Enumerable.Range(0, months).Select(i => from.AddMonths(i).ToString("yyyy-MM")).ToList();
        object? Trend<T>(List<T> rows, Func<T, string> month) => monthsList.Select(mo => new { Month = mo, Count = rows.Count(r => month(r) == mo) }).ToList();

        var ncr = await s.Q(db.Ncrs.AsNoTracking(), Permissions.NcrView, ct);
        var defects = ncr is null ? null : Trend(await ncr.Where(x => x.DetectedOn >= from).Select(x => x.DetectedOn).ToListAsync(ct), d => d.ToString("yyyy-MM"));
        var openBySite = ncr is null ? null : await ncr.Where(x => x.Status != NcrStatus.Closed && x.Status != NcrStatus.Cancelled).GroupBy(x => x.SiteId)
            .Select(g => new { SiteId = g.Key, Site = db.Sites.Where(z => z.Id == g.Key).Select(z => z.Name).First(), Count = g.Count() }).ToListAsync(ct);
        var comp = await s.Q(db.Complaints.AsNoTracking(), Permissions.ComplaintsView, ct);
        var complaints = comp is null ? null : Trend(await comp.Where(x => x.ReportedOn >= from).Select(x => x.ReportedOn).ToListAsync(ct), d => d.ToString("yyyy-MM"));
        var customerRisk = comp is null ? null : (await comp.Where(x => x.Status != ComplaintStatus.Closed && x.Status != ComplaintStatus.Cancelled).GroupBy(x => x.CustomerId)
            .Select(g => new { CustomerId = g.Key, Open = g.Count(), Overdue = g.Count(x => x.ResponseDueOn < today) }).ToListAsync(ct))
            .Join(await db.Customers.AsNoTracking().Select(c => new { c.Id, c.Name }).ToListAsync(ct), x => x.CustomerId, c => c.Id, (x, c) => new { x.CustomerId, Customer = c.Name, x.Open, x.Overdue, Url = $"/complaints?customerId={x.CustomerId}" })
            .OrderByDescending(x => x.Overdue).ThenByDescending(x => x.Open).Take(10).ToList();
        object? supplierRisk = null;
        try
        {
            supplierRisk = (await suppliers.ScorecardAsync(from, today, null, ct)).OrderBy(x => x.Overall).Where(x => x.Overall < 80 || x.Risk <= 50).Take(10)
                .Select(x => new { x.SupplierId, x.Code, x.Name, x.Overall, x.Rank, Url = $"/suppliers/{x.SupplierId}" }).ToList();
        }
        catch (ForbiddenException) { }
        var coqQ = await s.Q(db.CoqEntries.AsNoTracking(), Permissions.CoqView, ct);
        var coq = coqQ is null ? null : (await coqQ.Where(x => x.IncurredOn >= from).Select(x => new { x.IncurredOn, x.Category, x.Amount }).ToListAsync(ct))
            .Let(rows => monthsList.Select(mo => new
            {
                Month = mo,
                Prevention = rows.Where(r => r.IncurredOn.ToString("yyyy-MM") == mo && r.Category == CoqCategory.Prevention).Sum(r => r.Amount),
                Appraisal = rows.Where(r => r.IncurredOn.ToString("yyyy-MM") == mo && r.Category == CoqCategory.Appraisal).Sum(r => r.Amount),
                InternalFailure = rows.Where(r => r.IncurredOn.ToString("yyyy-MM") == mo && r.Category == CoqCategory.InternalFailure).Sum(r => r.Amount),
                ExternalFailure = rows.Where(r => r.IncurredOn.ToString("yyyy-MM") == mo && r.Category == CoqCategory.ExternalFailure).Sum(r => r.Amount),
            }).ToList());
        var fq = await s.Q(db.AuditFindings.AsNoTracking(), Permissions.AuditsView, ct);
        var findings = fq is null ? null : (await fq.Where(x => x.Status != FindingStatus.Closed).GroupBy(x => x.FindingType).Select(g => new { Type = g.Key, Open = g.Count(), Overdue = g.Count(x => x.ResponseDueOn < today) }).ToListAsync(ct))
            .Select(x => new { Type = x.Type.ToString(), x.Open, x.Overdue, Url = "/audits?tab=findings" }).ToList();
        return new { Tiles = tiles, DefectTrend = defects, ComplaintTrend = complaints, OpenNcrsBySite = openBySite, CustomerRisk = customerRisk, SupplierRisk = supplierRisk, CoqTrend = coq, AuditFindings = findings };
    }

    /// <summary>Breadth-first walk of record links (and built-in references) from a record, up to the given depth, keeping only records the user may view.</summary>
    public async Task<(List<GraphNode> Nodes, List<GraphEdge> Edges)> GraphAsync(string type, Guid id, int depth, CancellationToken ct)
    {
        depth = Math.Clamp(depth, 1, 3);
        var root = await m.ViewAsync(type, id, ct);
        var nodes = new Dictionary<(string, Guid), GraphNode> { [(type, id)] = new(root.Type, root.Id, root.Number, root.Title, root.Status, root.Url, 0) };
        var edges = new List<GraphEdge>();
        var frontier = new List<(string Type, Guid Id)> { (type, id) };
        var denied = new HashSet<(string, Guid)>();
        for (var d = 1; d <= depth && frontier.Count > 0 && nodes.Count < 150; d++)
        {
            var ids = frontier.Select(x => x.Id).ToList();
            var links = await db.RecordLinks.AsNoTracking().Where(l => ids.Contains(l.FromId) || ids.Contains(l.ToId)).Take(500).ToListAsync(ct);
            var found = links.Where(l => frontier.Contains((l.FromType, l.FromId)) || frontier.Contains((l.ToType, l.ToId)))
                .Select(l => new GraphEdge(l.FromType, l.FromId, l.ToType, l.ToId, l.LinkType)).ToList();
            found.AddRange(await ImpliedAsync(frontier, ct));
            var next = new List<(string, Guid)>();
            foreach (var e in found)
            {
                foreach (var (t, i) in new[] { (e.FromType, e.FromId), (e.ToType, e.ToId) })
                {
                    if (nodes.ContainsKey((t, i)) || denied.Contains((t, i))) continue;
                    if (!records.IsKnownType(t)) { denied.Add((t, i)); continue; }
                    try { var r = await records.RequireViewAsync(t, i, ct); nodes[(t, i)] = new(r.Type, r.Id, r.Number, r.Title, r.Status, r.Url, d); next.Add((t, i)); }
                    catch (AppException) { denied.Add((t, i)); }
                }
                if (nodes.ContainsKey((e.FromType, e.FromId)) && nodes.ContainsKey((e.ToType, e.ToId)) && !edges.Any(x => x.FromId == e.FromId && x.ToId == e.ToId && x.Label == e.Label)) edges.Add(e);
            }
            frontier = next;
        }
        return (nodes.Values.ToList(), edges);
    }

    async Task<List<GraphEdge>> ImpliedAsync(List<(string Type, Guid Id)> frontier, CancellationToken ct)
    {
        var e = new List<GraphEdge>();
        var fai = frontier.Where(x => x.Type == "Fai").Select(x => x.Id).ToList();
        foreach (var f in await db.Fais.AsNoTracking().Where(x => fai.Contains(x.Id)).Select(x => new { x.Id, x.ProductId, x.PartId, x.SupplierId, x.DrawingId }).ToListAsync(ct))
        {
            if (f.ProductId is { } p) e.Add(new("Product", p, "Fai", f.Id, "First article"));
            if (f.PartId is { } pt) e.Add(new("Part", pt, "Fai", f.Id, "First article"));
            if (f.SupplierId is { } s) e.Add(new("Supplier", s, "Fai", f.Id, "First article"));
            if (f.DrawingId is { } d) e.Add(new("Drawing", d, "Fai", f.Id, "Drawing"));
        }
        var aql = frontier.Where(x => x.Type == "AqlInspection").Select(x => x.Id).ToList();
        foreach (var a in await db.AqlInspections.AsNoTracking().Where(x => aql.Contains(x.Id)).Select(x => new { x.Id, x.SupplierId }).ToListAsync(ct))
            if (a.SupplierId is { } s) e.Add(new("Supplier", s, "AqlInspection", a.Id, "Received lot"));
        var plans = frontier.Where(x => x.Type == ApqpService.PlanType).Select(x => x.Id).ToList();
        foreach (var c in await db.ControlPlans.AsNoTracking().Where(x => plans.Contains(x.Id)).Select(x => new { x.Id, x.ProductId, x.SupplierId }).ToListAsync(ct))
        {
            if (c.ProductId is { } p) e.Add(new("Product", p, ApqpService.PlanType, c.Id, "Control plan"));
            if (c.SupplierId is { } s) e.Add(new("Supplier", s, ApqpService.PlanType, c.Id, "Supplier control plan"));
        }
        var comps = frontier.Where(x => x.Type == "Complaint").Select(x => x.Id).ToList();
        foreach (var c in await db.Complaints.AsNoTracking().Where(x => comps.Contains(x.Id)).Select(x => new { x.Id, x.ProductId }).ToListAsync(ct))
            if (c.ProductId is { } p) e.Add(new("Product", p, "Complaint", c.Id, "Complaint"));
        var prods = frontier.Where(x => x.Type == "Product").Select(x => x.Id).ToList();
        if (prods.Count > 0)
        {
            foreach (var c in await db.ControlPlans.AsNoTracking().Where(x => x.ProductId != null && prods.Contains(x.ProductId.Value) && x.Status != ApqpStatus.Obsolete).Select(x => new { x.Id, x.ProductId }).Take(50).ToListAsync(ct))
                e.Add(new("Product", c.ProductId!.Value, ApqpService.PlanType, c.Id, "Control plan"));
            foreach (var f in await db.Fais.AsNoTracking().Where(x => prods.Contains(x.ProductId)).Select(x => new { x.Id, x.ProductId }).Take(50).ToListAsync(ct))
                e.Add(new("Product", f.ProductId, "Fai", f.Id, "First article"));
            foreach (var c in await db.Complaints.AsNoTracking().Where(x => x.ProductId != null && prods.Contains(x.ProductId.Value)).Select(x => new { x.Id, x.ProductId }).Take(50).ToListAsync(ct))
                e.Add(new("Product", c.ProductId!.Value, "Complaint", c.Id, "Complaint"));
        }
        var scars = frontier.Where(x => x.Type == "Supplier").Select(x => x.Id).ToList();
        foreach (var c in await db.SupplierCorrectiveActions.AsNoTracking().Where(x => scars.Contains(x.SupplierId)).Select(x => new { x.Id, x.SupplierId }).Take(50).ToListAsync(ct))
            e.Add(new("Supplier", c.SupplierId, "SupplierCorrectiveAction", c.Id, "SCAR"));
        return e;
    }

    // ---------- data warehouse ----------
    static DateOnly PeriodStart(MetricPeriod p, DateOnly d) => p switch
    {
        MetricPeriod.Month => new DateOnly(d.Year, d.Month, 1),
        MetricPeriod.Quarter => new DateOnly(d.Year, (d.Month - 1) / 3 * 3 + 1, 1),
        _ => new DateOnly(d.Year, 1, 1)
    };
    static DateOnly PeriodEnd(MetricPeriod p, DateOnly s) => (p switch { MetricPeriod.Month => s.AddMonths(1), MetricPeriod.Quarter => s.AddMonths(3), _ => s.AddYears(1) }).AddDays(-1);

    /// <summary>Recomputes organization and per-site facts for the period containing <paramref name="date"/> from live data (all sites, unscoped).</summary>
    public async Task<int> SnapshotAsync(MetricPeriod period, DateOnly date, CancellationToken ct)
    {
        var scope = await access.GetScopeAsync(m.UserId, Permissions.AnalyticsView, ct);
        if (!scope.AllSites) throw new ForbiddenException("Warehouse snapshots need analytics access for all sites.");
        var start = PeriodStart(period, date); var end = PeriodEnd(period, start);
        var sites = new List<Guid?> { null };
        sites.AddRange(await db.Sites.AsNoTracking().Where(x => !x.IsArchived).Select(x => (Guid?)x.Id).ToListAsync(ct));
        var n = 0;
        foreach (var site in sites)
        {
            foreach (var mt in await ComputeAsync(false, site, start, end, ct))
            {
                if (mt.Value is null) continue;
                var fact = await db.QualityMetricFacts.FirstOrDefaultAsync(x => x.Metric == mt.Key && x.PeriodType == period && x.PeriodStart == start && x.SiteId == site, ct);
                if (fact is null) { fact = new QualityMetricFact { Metric = mt.Key, PeriodType = period, PeriodStart = start, SiteId = site }; db.QualityMetricFacts.Add(fact); }
                fact.Value = mt.Value.Value; fact.Numerator = mt.Numerator; fact.Denominator = mt.Denominator; fact.ComputedAt = m.Now;
                fact.DenominatorSource = mt.DenominatorSource is null ? null : (mt.IsFallback ? "Fallback:" : "") + mt.DenominatorSource; fact.CalculationVersion = mt.CalculationVersion;
                n++;
            }
        }
        await db.SaveChangesAsync(ct);
        return n;
    }

    public async Task<List<QualityMetricFact>> TrendAsync(string metric, MetricPeriod period, Guid? siteId, CancellationToken ct)
    {
        var scope = await access.GetScopeAsync(m.UserId, Permissions.AnalyticsView, ct);
        if (scope.IsEmpty) throw new ForbiddenException();
        if (siteId is null && !scope.AllSites) throw new ForbiddenException("Organization-wide figures need analytics access for all sites.");
        if (siteId is { } s && !scope.AllSites && !scope.SiteIds.Contains(s)) throw new ForbiddenException();
        return await db.QualityMetricFacts.AsNoTracking().Where(x => x.Metric == metric && x.PeriodType == period && x.SiteId == siteId).OrderBy(x => x.PeriodStart).Take(120).ToListAsync(ct);
    }
}

static class LetExt { public static R Let<T, R>(this T v, Func<T, R> f) => f(v); }
