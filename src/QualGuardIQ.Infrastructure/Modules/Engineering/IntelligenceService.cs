using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Enterprise;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Enterprise;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Engineering;

public sealed record RiskFactor(string Factor, decimal? Score, decimal Weight, string Basis);
public sealed record SupplierRisk(Guid SupplierId, string Code, string Name, string Status, decimal? Health, decimal? Risk, string Level, List<RiskFactor> Factors);
public sealed record ScoreComponent(string Component, decimal? Score, string Basis);
public sealed record QualityScore(Guid Id, string Code, string Name, string Level, decimal? Score, List<ScoreComponent> Components);
public sealed record SupplierWeights(decimal Audit = 0.25m, decimal Quality = 0.25m, decimal Delivery = 0.20m, decimal CorrectiveActions = 0.15m, decimal Complaints = 0.15m);

/// <summary>
/// Enterprise intelligence computed only from recorded data: supplier risk and health, the enterprise risk matrix,
/// global quality scores (site, region, corporate), factory and executive command centers, KPI rollups, data marts,
/// benchmarking and what-if models. A factor without data is reported as "no data" and left out of the weighting, never guessed.
/// </summary>
public sealed partial class IntelligenceService(AppDbContext db, ModuleSupport m, EnterpriseService enterprise)
{
    public static readonly string[] EnterpriseRiskCategories = ["Strategic", "Operational", "Supplier", "Product", "Customer"];

    static decimal Clamp(decimal v) => Math.Max(0, Math.Min(100, Math.Round(v, 1)));
    static decimal? Weighted(IEnumerable<(decimal? Score, decimal Weight)> xs)
    {
        var l = xs.Where(x => x.Score != null && x.Weight > 0).ToList();
        var w = l.Sum(x => x.Weight);
        return w == 0 ? null : Math.Round(l.Sum(x => x.Score!.Value * x.Weight) / w, 1);
    }
    public static string RiskLevel(decimal? risk) => risk switch { null => "Unknown", >= 50 => "High", >= 25 => "Medium", _ => "Low" };

    // ---------- supplier risk ----------
    public async Task<List<SupplierRisk>> SupplierRiskAsync(Guid? supplierId, SupplierWeights? weights, CancellationToken ct)
    {
        var w = weights ?? new SupplierWeights();
        if (new[] { w.Audit, w.Quality, w.Delivery, w.CorrectiveActions, w.Complaints }.Any(x => x < 0) || w.Audit + w.Quality + w.Delivery + w.CorrectiveActions + w.Complaints <= 0) throw new AppException("Weights must be zero or more and not all zero.");
        var q = await m.ScopeAsync(db.Suppliers.AsNoTracking(), Permissions.SuppliersView, ct);
        var sups = await q.Where(s => supplierId == null || s.Id == supplierId).OrderBy(s => s.Code).Take(500).ToListAsync(ct);
        var ids = sups.Select(s => s.Id).ToList();
        var today = DateOnly.FromDateTime(m.Now.UtcDateTime); var yr = today.AddYears(-1);
        var reviews = await db.SupplierReviews.AsNoTracking().Where(r => ids.Contains(r.SupplierId)).GroupBy(r => r.SupplierId).Select(g => g.OrderByDescending(r => r.ReviewedOn).First()).ToListAsync(ct);
        var perf = await db.SupplierPerformance.AsNoTracking().Where(r => ids.Contains(r.SupplierId)).GroupBy(r => r.SupplierId).Select(g => g.OrderByDescending(r => r.PeriodEnd).First()).ToListAsync(ct);
        var aql = await db.AqlInspections.AsNoTracking().Where(a => a.SupplierId != null && ids.Contains(a.SupplierId.Value) && a.OverallResult != null && a.InspectionDate >= m.Now.AddYears(-1))
            .GroupBy(a => a.SupplierId!.Value).Select(g => new { g.Key, N = g.Count(), Acc = g.Count(a => a.OverallResult == AqlDecision.Accept) }).ToListAsync(ct);
        var scars = await db.SupplierCorrectiveActions.AsNoTracking().Where(s => ids.Contains(s.SupplierId) && (s.IssuedOn >= yr || (s.Status != ScarStatus.Closed && s.Status != ScarStatus.Cancelled)))
            .Select(s => new { s.SupplierId, Open = s.Status != ScarStatus.Closed && s.Status != ScarStatus.Cancelled, Overdue = s.Status == ScarStatus.Issued && s.ResponseDueOn < today, s.WasEffective }).ToListAsync(ct);
        var cc = await db.Complaints.AsNoTracking().Where(c => c.SupplierId != null && ids.Contains(c.SupplierId.Value) && c.ComplaintDate >= yr).GroupBy(c => c.SupplierId!.Value).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
        var mat = await db.MaturityAssessments.AsNoTracking().Where(a => a.Subject == MaturitySubject.Supplier && ids.Contains(a.SubjectId)).GroupBy(a => a.SubjectId).Select(g => g.OrderByDescending(a => a.AssessedOn).First()).ToListAsync(ct);
        var list = new List<SupplierRisk>();
        foreach (var s in sups)
        {
            var f = new List<RiskFactor>();
            var rv = reviews.FirstOrDefault(r => r.SupplierId == s.Id); var ma = mat.FirstOrDefault(a => a.SubjectId == s.Id);
            if (rv != null) f.Add(new("Audit performance", rv.Outcome switch { ReviewOutcome.Continue => 100, ReviewOutcome.Conditional => 60, ReviewOutcome.Suspend => 25, _ => 0 }, w.Audit, $"Last review {rv.ReviewedOn:yyyy-MM-dd}: {rv.Outcome}"));
            else if (ma != null) f.Add(new("Audit performance", Clamp((ma.Overall - 1) * 25), w.Audit, $"Maturity {ma.Overall} on {ma.AssessedOn:yyyy-MM-dd}"));
            else f.Add(new("Audit performance", null, w.Audit, "No supplier review or assessment"));
            var pf = perf.FirstOrDefault(p => p.SupplierId == s.Id); var a = aql.FirstOrDefault(x => x.Key == s.Id);
            if (pf != null && pf.LotsReceived > 0) f.Add(new("Quality performance", pf.QualityScore, w.Quality, $"{pf.LotsRejected}/{pf.LotsReceived} lots rejected ({pf.PeriodStart:yyyy-MM}–{pf.PeriodEnd:yyyy-MM})"));
            else if (a != null) f.Add(new("Quality performance", Clamp(100m * a.Acc / a.N), w.Quality, $"{a.Acc}/{a.N} AQL lots accepted in 12 months"));
            else f.Add(new("Quality performance", null, w.Quality, "No receiving or AQL results"));
            f.Add(pf != null && pf.Deliveries > 0 ? new("Delivery performance", pf.DeliveryScore, w.Delivery, $"{pf.OnTimeDeliveries}/{pf.Deliveries} on time") : new("Delivery performance", null, w.Delivery, "No delivery data"));
            var sc = scars.Where(x => x.SupplierId == s.Id).ToList();
            f.Add(new("Corrective actions", Clamp(100 - 15 * sc.Count(x => x.Open) - 15 * sc.Count(x => x.Overdue) - 20 * sc.Count(x => x.WasEffective == false)), w.CorrectiveActions,
                $"{sc.Count(x => x.Open)} open SCARs, {sc.Count(x => x.Overdue)} overdue response, {sc.Count(x => x.WasEffective == false)} ineffective"));
            var n = cc.FirstOrDefault(x => x.Key == s.Id)?.N ?? 0;
            f.Add(new("Complaint history", Clamp(100 - 20 * n), w.Complaints, $"{n} customer complaints attributed in 12 months"));
            var health = Weighted(f.Select(x => (x.Score, x.Weight)));
            var risk = health is { } h ? 100 - h : (decimal?)null;
            if (s.Status is SupplierStatus.Suspended or SupplierStatus.Disqualified) risk = 100;
            list.Add(new(s.Id, s.Code, s.LegalName, s.Status.ToString(), health, risk, RiskLevel(risk), f));
        }
        return list.OrderByDescending(x => x.Risk ?? -1).ToList();
    }

    // ---------- enterprise risk matrix ----------
    public async Task<object> RiskMatrixAsync(Guid? siteId, CancellationToken ct)
    {
        var q = await m.ScopeAsync(db.Risks.AsNoTracking(), Permissions.RisksView, ct);
        var risks = await q.Where(r => (siteId == null || r.SiteId == siteId) && r.Kind == RiskKind.Risk && r.Status != RiskStatus.Cancelled)
            .Select(r => new { r.Id, r.Number, r.Title, r.Category, r.SiteId, r.Status, P = r.ResidualProbability ?? r.Probability, S = r.ResidualSeverity ?? r.Severity, Score = r.ResidualScore ?? r.InitialScore, Band = r.ResidualBand ?? r.InitialBand, r.CreatedAt, r.UpdatedAt }).ToListAsync(ct);
        string Cat(string c) => c is "Organizational" or "Project" ? "Strategic" : c is "Quality" or "Equipment" or "Process" ? (c == "Quality" ? "Product" : "Operational") : EnterpriseRiskCategories.FirstOrDefault(x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase)) ?? "Operational";
        var open = risks.Where(r => r.Status != RiskStatus.Closed).ToList();
        var heat = Enumerable.Range(1, 5).SelectMany(p => Enumerable.Range(1, 5).Select(s => new { Probability = p, Severity = s, Count = open.Count(r => r.P == p && r.S == s) })).ToList();
        var today = DateOnly.FromDateTime(m.Now.UtcDateTime);
        var months = Enumerable.Range(0, 12).Select(i => today.AddMonths(-11 + i)).Select(d => d.ToString("yyyy-MM")).ToList();
        var trend = months.Select(mo => new
        {
            Month = mo,
            Opened = risks.Count(r => r.CreatedAt.ToString("yyyy-MM") == mo),
            Closed = risks.Count(r => r.Status == RiskStatus.Closed && r.UpdatedAt.ToString("yyyy-MM") == mo),
            Open = risks.Count(r => string.Compare(r.CreatedAt.ToString("yyyy-MM"), mo, StringComparison.Ordinal) <= 0 && !(r.Status == RiskStatus.Closed && string.Compare(r.UpdatedAt.ToString("yyyy-MM"), mo, StringComparison.Ordinal) <= 0))
        }).ToList();
        var byCat = EnterpriseRiskCategories.Select(c => new { Category = c, Open = open.Count(r => Cat(r.Category) == c), High = open.Count(r => Cat(r.Category) == c && (r.Band == "High" || r.Band == "Critical")), AvgScore = open.Where(r => Cat(r.Category) == c).Select(r => (decimal)r.Score).DefaultIfEmpty().Average() }).ToList();
        var supplierHigh = (await SupplierRiskAsync(null, null, ct)).Where(s => s.Level == "High").Take(10).ToList();
        return new { Heatmap = heat, Categories = byCat, Ranking = open.OrderByDescending(r => r.Score).Take(25).Select(r => new { r.Id, r.Number, r.Title, Category = Cat(r.Category), r.P, r.S, r.Score, r.Band }), Trend = trend, SupplierRisks = supplierHigh };
    }

    // ---------- global quality score ----------
    static List<ScoreComponent> Components(SiteMetrics s, decimal? supplier)
    {
        static decimal? Ratio(int bad, int total) => total == 0 ? 100 : Clamp(100m - 100m * bad / total);
        return
        [
            new("NCR performance", Ratio(s.OverdueNcrs, s.OpenNcrs), $"{s.OverdueNcrs} of {s.OpenNcrs} open NCRs overdue"),
            new("CAPA performance", Ratio(s.OverdueCapas, s.OpenCapas), $"{s.OverdueCapas} of {s.OpenCapas} open CAPAs overdue"),
            new("Audit performance", Clamp(100 - 10 * s.OverdueFindings), $"{s.OverdueFindings} audit findings past response date"),
            new("Complaint performance", Ratio(s.OverdueComplaints, s.OpenComplaints), $"{s.OverdueComplaints} of {s.OpenComplaints} open complaints overdue"),
            new("Supplier performance", supplier, supplier is null ? "No scored suppliers" : "Average supplier health"),
            new("Training performance", Ratio(s.TrainingOverdue, s.TrainingAssigned), $"{s.TrainingOverdue} of {s.TrainingAssigned} assignments overdue"),
        ];
    }

    public async Task<object> GlobalScoreAsync(CancellationToken ct)
    {
        var metrics = await enterprise.SiteMetricsAsync(null, ct);
        var sup = await SupplierRiskAsync(null, null, ct);
        var supSite = await db.Suppliers.AsNoTracking().Select(s => new { s.Id, s.SiteId }).ToListAsync(ct);
        var sites = metrics.Select(s =>
        {
            var hs = sup.Where(x => x.Health != null && supSite.Any(z => z.Id == x.SupplierId && z.SiteId == s.SiteId)).Select(x => x.Health!.Value).ToList();
            var comps = Components(s, hs.Count == 0 ? null : Math.Round(hs.Average(), 1));
            return new QualityScore(s.SiteId, s.SiteCode, s.SiteName, "Site", Weighted(comps.Select(c => (c.Score, 1m))), comps);
        }).ToList();
        var units = await enterprise.UnitsAsync(ct);
        var regions = units.Where(u => u.Level is OrgLevel.Region or OrgLevel.Division or OrgLevel.BusinessUnit).Select(u =>
        {
            var under = EnterpriseService.SitesUnder(units, u.Id);
            var l = sites.Where(s => under.Contains(s.Id)).ToList();
            var comps = Components0(l);
            return new QualityScore(u.Id, u.Code, u.Name, u.Level.ToString(), Weighted(l.Select(s => (s.Score, 1m))), comps);
        }).Where(r => r.Components.Count > 0).ToList();
        var corp = new QualityScore(Guid.Empty, "CORP", "Corporate", "Corporate", Weighted(sites.Select(s => (s.Score, 1m))), Components0(sites));
        return new { Corporate = corp, Regions = regions, Sites = sites.OrderByDescending(s => s.Score) };
    }

    static List<ScoreComponent> Components0(List<QualityScore> sites) => sites.Count == 0 ? [] :
        sites[0].Components.Select((c, i) => new ScoreComponent(c.Component, Weighted(sites.Select(s => (s.Components[i].Score, 1m))), $"Average of {sites.Count} site(s)")).ToList();
}
