using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Enterprise;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;

namespace QualGuardIQ.Infrastructure.Modules.Engineering;

public sealed record TwinProcessInput(Guid ChartId, decimal MeanShift = 0, decimal SigmaFactor = 1);
public sealed record TwinProductInput(Guid PfmeaId);
public sealed record TwinSupplierInput(Guid SupplierId, SupplierWeights Weights);

public sealed partial class IntelligenceService
{
    // ---------- digital factory quality center ----------
    public async Task<object> FactoryCenterAsync(Guid siteId, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.EnterpriseView, siteId, null, ct);
        var week = m.Now.AddDays(-7);
        var ncr = await m.TryScopeAsync(db.Ncrs.AsNoTracking(), Permissions.NcrView, ct);
        var capa = await m.TryScopeAsync(db.Capas.AsNoTracking(), Permissions.CapaView, ct);
        var scar = await m.TryScopeAsync(db.SupplierCorrectiveActions.AsNoTracking(), Permissions.SuppliersView, ct);
        var spc = await m.TryScopeAsync(db.SpcCharts.AsNoTracking(), Permissions.SpcView, ct);
        var wo = await m.TryScopeAsync(db.WorkOrders.AsNoTracking(), Permissions.ProductionView, ct);
        var holds = await m.TryScopeAsync(db.InventoryHolds.AsNoTracking(), Permissions.InventoryView, ct);
        var ncrs = ncr == null ? [] : await ncr.Where(x => x.SiteId == siteId && x.Status != NcrStatus.Closed && x.Status != NcrStatus.Cancelled).OrderBy(x => x.DueOn)
            .Select(x => new Item("NCR", x.Number, x.Title, x.Severity.ToString(), $"/ncr/{x.Id}", x.DueOn)).Take(50).ToListAsync(ct);
        var capas = capa == null ? [] : await capa.Where(x => x.SiteId == siteId && x.Status != CapaStatus.Closed && x.Status != CapaStatus.Cancelled).OrderBy(x => x.DueOn)
            .Select(x => new Item("CAPA", x.Number, x.Title, x.Status.ToString(), $"/capa/{x.Id}", x.DueOn)).Take(50).ToListAsync(ct);
        var scars = scar == null ? [] : await scar.Where(x => x.SiteId == siteId && x.Status != ScarStatus.Closed && x.Status != ScarStatus.Cancelled)
            .Select(x => new Item("SCAR", x.Number, x.Title, x.Status.ToString(), $"/suppliers/scars/{x.Id}", x.ResponseDueOn)).Take(50).ToListAsync(ct);
        var viol = spc == null ? [] : await (from s in db.SpcSamples.AsNoTracking() join c in spc on s.ChartId equals c.Id
                where c.SiteId == siteId && s.Violations != null && s.TakenAt >= week orderby s.TakenAt descending
                select new Item("SPC", c.Number, c.Title + ": " + s.Violations, "Violation", "/spc/" + c.Id, null)).Take(50).ToListAsync(ct);
        var prod = new List<Item>();
        if (wo != null) prod.AddRange(await wo.Where(x => x.SiteId == siteId && x.Status == WorkOrderStatus.OnHold).Select(x => new Item("Work order", x.Number, "On hold", "OnHold", "/production/orders/" + x.Id, null)).Take(25).ToListAsync(ct));
        if (holds != null) prod.AddRange(await holds.Where(x => x.SiteId == siteId && x.Status == Domain.Quality.HoldStatus.Active).Select(x => new Item("Hold", x.Number, x.Reason, x.Kind.ToString(), "/inventory/holds/" + x.Id, null)).Take(25).ToListAsync(ct));
        var visible = await db.SearchEntries.AsNoTracking().Where(e => e.SiteId == siteId).Select(e => e.RecordId).ToListAsync(ct);
        var alerts = await db.AlertEvents.AsNoTracking().Where(a => a.ClearedAt == null && a.RecordId != null && visible.Contains(a.RecordId.Value)).OrderByDescending(a => a.RaisedAt)
            .Select(a => new Item("Alert", a.SubjectKey, a.Title, a.RecordType ?? "", "", null)).Take(50).ToListAsync(ct);
        return new { QualityAlerts = alerts, ProductionAlerts = prod, SpcViolations = viol, OpenNcrs = ncrs, OpenCapas = capas, SupplierIssues = scars,
            Counts = new { Alerts = alerts.Count, Production = prod.Count, Spc = viol.Count, Ncrs = ncrs.Count, Capas = capas.Count, Suppliers = scars.Count } };
    }
    public sealed record Item(string Kind, string Number, string Title, string Status, string Url, DateOnly? DueOn);

    // ---------- executive command center ----------
    public async Task<object> CommandCenterAsync(CancellationToken ct)
    {
        var score = await GlobalScoreAsync(ct);
        var metrics = await enterprise.SiteMetricsAsync(null, ct);
        var suppliers = await SupplierRiskAsync(null, null, ct);
        var yr = DateOnly.FromDateTime(m.Now.UtcDateTime).AddYears(-1); var d90 = DateOnly.FromDateTime(m.Now.UtcDateTime).AddDays(-90);
        var cq = await m.TryScopeAsync(db.Complaints.AsNoTracking(), Permissions.ComplaintsView, ct);
        var complaints = cq == null ? [] : await cq.Where(c => c.ComplaintDate >= yr).Select(c => new { c.CustomerId, Customer = db.Customers.Where(x => x.Id == c.CustomerId).Select(x => x.Name).FirstOrDefault(), c.Status, c.ComplaintDate, c.ClosedAt, c.ResponseDueOn }).ToListAsync(ct);
        var coqQ = await m.TryScopeAsync(db.CoqEntries.AsNoTracking(), Permissions.CoqView, ct);
        var coq = coqQ == null ? [] : await coqQ.Where(c => c.IncurredOn >= d90).GroupBy(c => c.Category).Select(g => new { Category = g.Key.ToString(), Amount = g.Sum(x => x.Amount) }).ToListAsync(ct);
        var closed = complaints.Where(c => c.ClosedAt != null).Select(c => (decimal)(c.ClosedAt!.Value.UtcDateTime.Date - c.ComplaintDate.ToDateTime(TimeOnly.MinValue)).TotalDays).ToList();
        return new
        {
            GlobalStatus = score,
            SitePerformance = metrics,
            SupplierPerformance = new { Scored = suppliers.Count(s => s.Health != null), High = suppliers.Count(s => s.Level == "High"), Medium = suppliers.Count(s => s.Level == "Medium"), Worst = suppliers.Take(10) },
            CustomerPerformance = complaints.GroupBy(c => c.Customer ?? "?").Select(g => new { Customer = g.Key, Complaints = g.Count(), Open = g.Count(c => c.Status != ComplaintStatus.Closed && c.Status != ComplaintStatus.Cancelled) }).OrderByDescending(x => x.Complaints).Take(10),
            CostOfQuality = coq,
            ComplaintPerformance = new { Received12m = complaints.Count, Open = complaints.Count(c => c.Status != ComplaintStatus.Closed && c.Status != ComplaintStatus.Cancelled),
                Overdue = complaints.Count(c => c.Status != ComplaintStatus.Closed && c.Status != ComplaintStatus.Cancelled && c.ResponseDueOn < DateOnly.FromDateTime(m.Now.UtcDateTime)),
                AvgDaysToClose = closed.Count == 0 ? (decimal?)null : Math.Round(closed.Average(), 1) }
        };
    }

    // ---------- KPI framework ----------
    /// <summary>KPIs by level with the latest rating, warning/critical alerts and the trend; corporate and regional KPIs show the site KPIs with the same metric under their unit.</summary>
    public async Task<object> KpiFrameworkAsync(CancellationToken ct)
    {
        var objs = await (await m.ScopeAsync(db.QualityObjectives.AsNoTracking(), Permissions.ObjectivesView, ct)).Where(o => o.Status == ObjectiveStatus.Active).ToListAsync(ct);
        var ids = objs.Select(o => o.Id).ToList();
        var meas = await db.KpiMeasurements.AsNoTracking().Where(x => ids.Contains(x.ObjectiveId) && x.SupersededById == null).OrderBy(x => x.PeriodStart).ToListAsync(ct);
        var units = await enterprise.UnitsAsync(ct);
        object Row(QualityObjective o)
        {
            var ms = meas.Where(x => x.ObjectiveId == o.Id).ToList(); var last = ms.LastOrDefault();
            return new { o.Id, o.Number, o.Title, Level = o.Level.ToString(), o.MetricName, o.Unit, o.Target, o.Warning, o.Critical, Direction = o.Direction.ToString(), o.SiteId, o.OrgUnitId,
                Latest = last?.Value, Rating = last?.Rating.ToString(), Trend = ms.TakeLast(6).Select(x => new { Period = x.PeriodStart.ToString("yyyy-MM"), x.Value }) };
        }
        var rollups = objs.Where(o => o.Level is KpiLevel.Corporate or KpiLevel.Regional).Select(o =>
        {
            var sites = o.OrgUnitId is { } u ? Enterprise.EnterpriseService.SitesUnder(units, u) : objs.Select(x => x.SiteId).Distinct().ToList();
            var children = objs.Where(c => c.Level == KpiLevel.Site && c.MetricName == o.MetricName && sites.Contains(c.SiteId)).ToList();
            var latest = children.Select(c => meas.LastOrDefault(x => x.ObjectiveId == c.Id)?.Value).Where(v => v != null).Select(v => v!.Value).ToList();
            return new { Objective = Row(o), Children = children.Select(Row), RollupValue = latest.Count == 0 ? (decimal?)null : Math.Round(latest.Average(), 2) };
        }).ToList();
        var alerts = objs.Select(o => (o, r: meas.LastOrDefault(x => x.ObjectiveId == o.Id)?.Rating)).Where(x => x.r is KpiRating.Warning or KpiRating.Critical).Select(x => Row(x.o)).ToList();
        return new { Levels = Enum.GetNames<KpiLevel>().ToDictionary(l => l, l => objs.Where(o => o.Level.ToString() == l).Select(Row).ToList()), Rollups = rollups, Alerts = alerts };
    }

    // ---------- data marts ----------
    public static readonly string[] Marts = ["supplier", "customer", "product", "manufacturing", "audit"];

    public async Task<object> MartAsync(string name, CancellationToken ct)
    {
        var yr = DateOnly.FromDateTime(m.Now.UtcDateTime).AddYears(-1); var t = m.Now.AddYears(-1);
        switch (name)
        {
            case "supplier":
                return (await SupplierRiskAsync(null, null, ct)).Select(s => new Dictionary<string, object?> { ["supplier"] = s.Code, ["name"] = s.Name, ["status"] = s.Status, ["health"] = s.Health, ["risk"] = s.Level,
                    ["quality"] = s.Factors[1].Score, ["delivery"] = s.Factors[2].Score, ["correctiveActions"] = s.Factors[3].Score, ["complaints"] = s.Factors[4].Score }).ToList();
            case "customer":
            {
                var cq = await m.ScopeAsync(db.Customers.AsNoTracking(), Permissions.MasterDataView, ct);
                return (await cq.Select(c => new { customer = c.Code, name = c.Name,
                    complaints12m = db.Complaints.Count(x => x.CustomerId == c.Id && x.ComplaintDate >= yr), openComplaints = db.Complaints.Count(x => x.CustomerId == c.Id && x.Status != ComplaintStatus.Closed && x.Status != ComplaintStatus.Cancelled),
                    returns12m = db.CustomerReturns.Count(x => x.CustomerId == c.Id && x.CreatedAt >= t), warrantyClaims12m = db.WarrantyClaims.Count(x => x.CustomerId == c.Id && x.CreatedAt >= t),
                    externalFailureCost12m = db.CoqEntries.Where(x => x.CustomerId == c.Id && x.IncurredOn >= yr && x.Category == CoqCategory.ExternalFailure).Sum(x => (decimal?)x.Amount) ?? 0 }).ToListAsync(ct)).Select(D).ToList();
            }
            case "product":
            {
                var pq = await m.ScopeAsync(db.Products.AsNoTracking(), Permissions.MasterDataView, ct);
                return (await pq.Select(p => new { product = p.Number, name = p.Name, family = db.ProductFamilies.Where(f => f.Id == p.FamilyId).Select(f => f.Name).FirstOrDefault(),
                    ncrs12m = db.Ncrs.Count(x => x.ProductId == p.Id && x.DetectedOn >= yr), complaints12m = db.Complaints.Count(x => x.ProductId == p.Id && x.ComplaintDate >= yr),
                    requirements = db.ProductRequirements.Count(x => x.ProductId == p.Id), coq12m = db.CoqEntries.Where(x => x.ProductId == p.Id && x.IncurredOn >= yr).Sum(x => (decimal?)x.Amount) ?? 0 }).ToListAsync(ct)).Select(D).ToList();
            }
            case "manufacturing":
                return (await enterprise.SiteMetricsAsync(null, ct)).Select(s => new Dictionary<string, object?> { ["site"] = s.SiteCode, ["name"] = s.SiteName, ["openNcrs"] = s.OpenNcrs, ["scrapEvents30d"] = s.ScrapEvents30d, ["scrapQty30d"] = s.ScrapQty30d,
                    ["inspections30d"] = s.Inspections30d, ["rejected30d"] = s.InspectionsRejected30d, ["firstPassRate30d"] = s.Inspections30d == 0 ? null : Math.Round(100m * (s.Inspections30d - s.InspectionsRejected30d) / s.Inspections30d, 1), ["coq90d"] = s.Coq90d }).ToList();
            case "audit":
            {
                var aq = await m.ScopeAsync(db.Audits.AsNoTracking(), Permissions.AuditsView, ct);
                var rows = await aq.Where(a => a.PlannedStart >= yr).Select(a => new { a.SiteId, a.Status, Findings = db.AuditFindings.Where(f => f.AuditId == a.Id).Select(f => f.FindingType).ToList() }).ToListAsync(ct);
                var sites = await db.Sites.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Code, ct);
                return rows.GroupBy(r => r.SiteId).Select(g => new Dictionary<string, object?> { ["site"] = sites.GetValueOrDefault(g.Key), ["audits12m"] = g.Count(), ["closed"] = g.Count(x => x.Status == AuditStatus.Closed),
                    ["major"] = g.Sum(x => x.Findings.Count(f => f == FindingType.MajorNonconformity)), ["minor"] = g.Sum(x => x.Findings.Count(f => f == FindingType.MinorNonconformity)), ["observations"] = g.Sum(x => x.Findings.Count(f => f == FindingType.Observation)) }).ToList();
            }
            default: throw new AppException($"Data mart must be one of: {string.Join(", ", Marts)}.");
        }
    }

    static Dictionary<string, object?> D(object o) => o.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(o));

    // ---------- benchmarking ----------
    public async Task<object> BenchmarkAsync(string kind, CancellationToken ct)
    {
        switch (kind)
        {
            case "plants":
            {
                dynamic g = await GlobalScoreAsync(ct);
                var sites = ((IEnumerable<QualityScore>)g.Sites).ToList();
                var mat = await db.MaturityAssessments.AsNoTracking().Where(a => a.Subject == MaturitySubject.Site).ToListAsync(ct);
                return sites.Select((s, i) => new { Rank = i + 1, s.Code, s.Name, s.Score, Maturity = mat.Where(a => a.SubjectId == s.Id).OrderByDescending(a => a.AssessedOn).Select(a => (decimal?)a.Overall).FirstOrDefault() }).ToList();
            }
            case "suppliers":
                return (await SupplierRiskAsync(null, null, ct)).Where(s => s.Health != null).OrderByDescending(s => s.Health).Select((s, i) => new { Rank = i + 1, s.Code, s.Name, Score = s.Health, s.Level }).ToList();
            case "families":
            {
                var rows = (await MartAsync("product", ct) as List<Dictionary<string, object?>>)!;
                return rows.GroupBy(r => (string?)r["family"] ?? "(no family)").Select(g => new { Family = g.Key, Products = g.Count(), Ncrs = g.Sum(r => (int)r["ncrs12m"]!), Complaints = g.Sum(r => (int)r["complaints12m"]!), Coq = g.Sum(r => (decimal)r["coq12m"]!) })
                    .OrderBy(x => x.Ncrs + x.Complaints).Select((x, i) => new { Rank = i + 1, x.Family, x.Products, x.Ncrs, x.Complaints, x.Coq }).ToList();
            }
            default: throw new AppException("Benchmark must be plants, suppliers or families.");
        }
    }

    // ---------- digital twin (what-if on recorded data) ----------
    static double Phi(double z) => 0.5 * (1 + Erf(z / Math.Sqrt(2)));
    static double Erf(double x)
    {
        var t = 1 / (1 + 0.3275911 * Math.Abs(x));
        var y = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
        return x >= 0 ? y : -y;
    }

    /// <summary>Process simulation from a chart's recorded samples: current mean and sigma, expected out-of-spec ppm, and the ppm after a mean shift and sigma change.</summary>
    public async Task<object> SimulateProcessAsync(TwinProcessInput r, CancellationToken ct)
    {
        await m.ViewAsync(Apqp.SpcService.RecordType, r.ChartId, ct);
        if (r.SigmaFactor <= 0 || r.SigmaFactor > 5) throw new AppException("Sigma factor must be between 0 and 5.");
        var c = await db.SpcCharts.AsNoTracking().FirstAsync(x => x.Id == r.ChartId, ct);
        if (c.Lsl is null && c.Usl is null) throw new AppException("The chart has no specification limits to simulate against.");
        var values = (await db.SpcSamples.AsNoTracking().Where(s => s.ChartId == r.ChartId).Select(s => s.Values).ToListAsync(ct)).SelectMany(v => JsonSerializer.Deserialize<List<decimal>>(v) ?? []).Select(v => (double)v).ToList();
        if (values.Count < 20) throw new AppException($"The chart has {values.Count} recorded values; at least 20 are needed for a simulation.");
        var mean = values.Average(); var sd = Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
        object Model(double mu, double s)
        {
            double below = c.Lsl is { } l ? Phi(((double)l - mu) / s) : 0, above = c.Usl is { } u ? 1 - Phi(((double)u - mu) / s) : 0;
            double? cpk = s == 0 ? null : Math.Min(c.Usl is { } u2 ? ((double)u2 - mu) / (3 * s) : double.MaxValue, c.Lsl is { } l2 ? (mu - (double)l2) / (3 * s) : double.MaxValue);
            return new { Mean = Math.Round(mu, 4), Sigma = Math.Round(s, 4), Ppk = cpk is { } k ? Math.Round(k, 2) : (double?)null, PpmOutOfSpec = Math.Round((below + above) * 1_000_000, 0) };
        }
        return new { Chart = c.Number, c.Characteristic, c.Lsl, c.Usl, Observations = values.Count, Current = Model(mean, sd), Scenario = Model(mean + (double)r.MeanShift, sd * (double)r.SigmaFactor),
            Method = "Normal model fitted to the chart's recorded values (overall sigma)." };
    }

    /// <summary>Product risk simulation: FMEA risk now and after the recommended actions take effect, using the revised ratings already recorded on each line.</summary>
    public async Task<object> SimulateProductRiskAsync(TwinProductInput r, CancellationToken ct)
    {
        await m.ViewAsync(Apqp.ApqpService.PfmeaType, r.PfmeaId, ct);
        var f = await db.Pfmeas.AsNoTracking().FirstAsync(x => x.Id == r.PfmeaId, ct);
        var lines = await db.PfmeaLines.AsNoTracking().Where(x => x.PfmeaId == r.PfmeaId).ToListAsync(ct);
        if (lines.Count == 0) throw new AppException("The FMEA has no lines to simulate.");
        var after = lines.Select(l => l.RevisedRpn is { } rr ? (Rpn: rr, P: Apqp.PfmeaMath.Priority(l.RevisedSeverity!.Value, l.RevisedOccurrence!.Value, l.RevisedDetection!.Value)) : (Rpn: l.Rpn, P: l.Priority)).ToList();
        return new { f.Number, Type = f.Type.ToString(), Lines = lines.Count,
            Current = new { TotalRpn = lines.Sum(l => l.Rpn), MaxRpn = lines.Max(l => l.Rpn), High = lines.Count(l => l.Priority == ActionPriority.High), Medium = lines.Count(l => l.Priority == ActionPriority.Medium) },
            AfterActions = new { TotalRpn = after.Sum(a => a.Rpn), MaxRpn = after.Max(a => a.Rpn), High = after.Count(a => a.P == ActionPriority.High), Medium = after.Count(a => a.P == ActionPriority.Medium) },
            LinesWithoutRevisedRatings = lines.Count(l => l.RevisedRpn == null && l.RecommendedAction != null),
            Method = "Revised severity/occurrence/detection recorded on each line; lines without revised ratings keep their current rating." };
    }

    public async Task<object> SimulateSupplierAsync(TwinSupplierInput r, CancellationToken ct)
    {
        var current = (await SupplierRiskAsync(r.SupplierId, null, ct)).FirstOrDefault() ?? throw new NotFoundException("Supplier");
        var scenario = (await SupplierRiskAsync(r.SupplierId, r.Weights, ct)).First();
        return new { Current = current, Scenario = scenario, Method = "Same recorded factors re-weighted." };
    }

    // ---------- board level reporting ----------
    public static readonly string[] BoardReports = ["executive", "board", "strategic-risk"];

    /// <summary>Executive, board and strategic risk reports assembled from the command center, global score and risk matrix. Scheduled distribution uses the shared report scheduler.</summary>
    public async Task<object> BoardReportAsync(string kind, CancellationToken ct)
    {
        var generated = m.Now;
        switch (kind)
        {
            case "executive":
                return new { Title = "Executive quality report", Generated = generated, Sections = await CommandCenterAsync(ct) };
            case "board":
            {
                dynamic g = await GlobalScoreAsync(ct);
                dynamic rm = await RiskMatrixAsync(null, ct);
                dynamic cc = await CommandCenterAsync(ct);
                return new { Title = "Board quality report", Generated = generated, CorporateScore = g.Corporate, Regions = g.Regions, TopRisks = ((IEnumerable<object>)rm.Ranking).Take(10), Complaints = cc.ComplaintPerformance, CostOfQuality = cc.CostOfQuality };
            }
            case "strategic-risk":
            {
                dynamic rm = await RiskMatrixAsync(null, ct);
                return new { Title = "Strategic risk report", Generated = generated, rm.Categories, rm.Heatmap, rm.Trend, Ranking = rm.Ranking, rm.SupplierRisks };
            }
            default: throw new AppException($"Report must be one of: {string.Join(", ", BoardReports)}.");
        }
    }
}
