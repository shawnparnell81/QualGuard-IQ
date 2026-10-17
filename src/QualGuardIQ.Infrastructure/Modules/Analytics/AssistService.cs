using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Analytics;

public sealed record AssistRecord(string Type, Guid Id, string Number, string Title, string Status, string Url, string? Detail = null);
/// <summary>Every answer states how it was produced (Method) and lists the records it used; nothing is changed, closed or approved.</summary>
public sealed record AssistAnswer(string Question, string Intent, string Answer, List<AssistRecord> Records, List<TrendRow> Trend, string Method, decimal? Confidence);
public sealed record TrendRow(string Label, decimal Current, decimal Previous, decimal Change);
public sealed record SimilarNcr(AssistRecord Ncr, decimal Similarity, List<string> SharedTerms);
public sealed record Suggestion(string Text, int Support, decimal Confidence, List<string> Sources);
public sealed record Investigation(AssistRecord Subject, List<SimilarNcr> Similar, List<Suggestion> RootCauses, List<Suggestion> Actions, List<TrendRow> RecurringDefects, string Method, string Disclaimer);
public sealed record RiskScore(string Kind, Guid Id, string Label, decimal Score, decimal Confidence, int Observations, List<string> Drivers);
public sealed record ExecutiveSummary(string Period, string Text, List<string> Highlights, List<string> Risks, List<KpiSeries> Kpis);

/// <summary>
/// Quality assistant. Built-in, deterministic and permission-aware: questions are parsed into structured queries over the
/// records the user can already view (TryScopeAsync), similarity uses TF-IDF cosine over NCR text, suggestions come from the
/// root causes and actions of linked CAPAs on similar NCRs, and every estimate carries a confidence based on sample size.
/// No external text-generation service is called and no record is created, closed or approved.
/// </summary>
public sealed class AssistService(AppDbContext db, ModuleSupport m, KpiEngine kpis)
{
    public const string Method = "Built-in rule-based assistant over records you can view (no external text-generation service).";
    static readonly HashSet<string> Stop = ["the", "a", "an", "of", "on", "in", "for", "to", "and", "or", "with", "at", "by", "is", "was", "are", "from", "this", "that", "be", "not", "no", "per", "after", "before", "found", "during"];

    static List<string> Terms(string? s) => Regex.Matches((s ?? "").ToLowerInvariant(), "[a-z0-9][a-z0-9\\-]{1,}").Select(x => x.Value).Where(t => !Stop.Contains(t) && t.Length > 2).ToList();

    public async Task<AssistAnswer> AskAsync(string question, CancellationToken ct)
    {
        ModuleSupport.Required(question, "Question", 1000);
        var q = question.Trim(); var ql = q.ToLowerInvariant();
        var today = await m.TodayAsync(null, ct);
        if (ql.Contains("overdue") && ql.Contains("capa"))
        {
            var capas = await m.TryScopeAsync(db.Capas.AsNoTracking(), Permissions.CapaView, ct) ?? throw new ForbiddenException();
            var overdueIds = await db.ActionItems.AsNoTracking().Where(a => a.SourceType == "Capa" && a.CompletedAt == null && a.Status != ActionStatus.Cancelled && a.DueDate < today).Select(a => a.SourceId).Distinct().ToListAsync(ct);
            var due = await capas.Where(c => overdueIds.Contains(c.Id) && c.Status != CapaStatus.Closed && c.Status != CapaStatus.Cancelled).ToListAsync(ct);
            var eff = await capas.Where(c => c.Status == CapaStatus.EffectivenessCheck && c.EffectivenessDueOn < today).ToListAsync(ct);
            var all = due.Concat(eff).DistinctBy(c => c.Id).ToList();
            return new(q, "overdue-capas", all.Count == 0 ? "No CAPAs are overdue." : $"{all.Count} CAPA(s) are overdue: {due.Count} with overdue actions, {eff.Count} past their effectiveness check date.",
                all.Select(c => new AssistRecord("Capa", c.Id, c.Number, c.Title, c.Status.ToString(), $"/capa/{c.Id}")).ToList(), [], Method, null);
        }
        if (ql.Contains("supplier") && ql.Contains("ncr"))
        {
            var ncrs = await m.TryScopeAsync(db.Ncrs.AsNoTracking(), Permissions.NcrView, ct) ?? throw new ForbiddenException();
            var sups = await m.TryScopeAsync(db.Suppliers.AsNoTracking(), Permissions.SuppliersView, ct);
            var name = Regex.Match(q, "(?:for|from)\\s+(?:supplier\\s+)?(.+?)[\\.\\?]?$", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
            var supplier = sups is null || name.Length == 0 ? null : await sups.FirstOrDefaultAsync(s => s.Code == name || s.LegalName.ToLower().Contains(name.ToLower()), ct);
            var list = await ncrs.Where(n => n.SupplierId != null && (supplier == null || n.SupplierId == supplier.Id)).OrderByDescending(n => n.DetectedOn).Take(100).ToListAsync(ct);
            var text = supplier is null && name.Length > 0 ? $"No supplier you can view matches \"{name}\". Showing all supplier NCRs ({list.Count})." : $"{list.Count} supplier NCR(s){(supplier is null ? "" : $" for {supplier.LegalName}")}.";
            return new(q, "supplier-ncrs", text, list.Select(n => new AssistRecord("Ncr", n.Id, n.Number, n.Title, n.Status.ToString(), $"/ncr/{n.Id}", n.DetectedOn.ToString("yyyy-MM-dd"))).ToList(), [], Method, null);
        }
        if (ql.Contains("complaint") && (ql.Contains("trend") || ql.Contains("by product")))
        {
            var cps = await m.TryScopeAsync(db.Complaints.AsNoTracking(), Permissions.ComplaintsView, ct) ?? throw new ForbiddenException();
            var cur = today.AddDays(-90); var prev = today.AddDays(-180);
            var rows = await cps.Where(c => c.ReportedOn > prev && c.Status != ComplaintStatus.Cancelled).Select(c => new { c.ProductId, c.ReportedOn }).ToListAsync(ct);
            var names = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Number + " " + p.Name, ct);
            var trend = rows.GroupBy(r => r.ProductId).Select(g => { var c = g.Count(x => x.ReportedOn > cur); var p = g.Count(x => x.ReportedOn <= cur); return new TrendRow(g.Key is { } k && names.TryGetValue(k, out var n) ? n : "No product", c, p, c - p); })
                .OrderByDescending(t => t.Current).ToList();
            var top = trend.FirstOrDefault();
            return new(q, "complaint-trend", top is null ? "No complaints in the last 180 days." : $"Complaints by product, last 90 days vs the 90 before. Highest: {top.Label} ({top.Current}, {(top.Change >= 0 ? "+" : "")}{top.Change}).", [], trend, Method, null);
        }
        if (ql.Contains("scrap") && (ql.Contains("increase") || ql.Contains("explain") || ql.Contains("why")))
            return await ExplainScrapAsync(q, today, ct);
        if (ql.Contains("audit") && ql.Contains("prep"))
            return await AuditPrepAsync(q, today, ct);
        // fall back to permission-filtered record discovery through the shared search
        var hits = await (await m.TryScopeAsync(db.Ncrs.AsNoTracking(), Permissions.NcrView, ct) ?? db.Ncrs.Where(_ => false)).Where(n => EF.Functions.ILike(n.Title, $"%{q}%") || EF.Functions.ILike(n.Description, $"%{q}%")).Take(20).ToListAsync(ct);
        return new(q, "search", hits.Count == 0 ? "I could not match that question to a known query. Try: \"Show overdue CAPAs\", \"Show me all supplier NCRs for <supplier>\", \"Show complaint trends by product\", \"Explain the increase in scrap\", or \"Audit preparation\"." : $"{hits.Count} NCR(s) mention \"{q}\".",
            hits.Select(n => new AssistRecord("Ncr", n.Id, n.Number, n.Title, n.Status.ToString(), $"/ncr/{n.Id}")).ToList(), [], Method, null);
    }

    async Task<AssistAnswer> ExplainScrapAsync(string q, DateOnly today, CancellationToken ct)
    {
        var scrap = await m.TryScopeAsync(db.ScrapEvents.AsNoTracking(), Permissions.CoqView, ct);
        var pe = await m.TryScopeAsync(db.ProductionEntries.AsNoTracking(), Permissions.ProductionView, ct);
        if (scrap is null && pe is null) throw new ForbiddenException();
        var cur = today.AddDays(-30); var prev = today.AddDays(-60);
        var rows = scrap is null ? [] : await scrap.Where(s => s.ScrappedOn > prev).Select(s => new { s.ScrappedOn, PartId = (Guid?)s.PartId, s.SupplierId, s.Reason, s.Quantity, s.TotalCost }).ToListAsync(ct);
        var floor = pe is null ? [] : await pe.Where(x => x.Scrapped > 0 && x.RecordedAt > new DateTimeOffset(prev.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))
            .Select(x => new { On = DateOnly.FromDateTime(x.RecordedAt.UtcDateTime), x.PartId, x.WorkCenter, x.Scrapped }).ToListAsync(ct);
        decimal C<T>(IEnumerable<T> s, Func<T, DateOnly> d, Func<T, decimal> v, bool current) => s.Where(x => current ? d(x) > cur : d(x) <= cur).Sum(v);
        var curTotal = C(rows, x => x.ScrappedOn, x => x.Quantity, true) + C(floor, x => x.On, x => x.Scrapped, true);
        var prevTotal = C(rows, x => x.ScrappedOn, x => x.Quantity, false) + C(floor, x => x.On, x => x.Scrapped, false);
        var parts = await db.Parts.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.PartNumber, ct);
        var sups = await db.Suppliers.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.LegalName, ct);
        var contributors = new List<TrendRow>();
        contributors.AddRange(rows.Concat(floor.Select(f => new { ScrappedOn = f.On, f.PartId, SupplierId = (Guid?)null, Reason = "Shop floor " + (f.WorkCenter ?? ""), Quantity = f.Scrapped, TotalCost = 0m }))
            .GroupBy(r => r.PartId).Select(g => new TrendRow("Part " + (g.Key is { } k && parts.TryGetValue(k, out var pn) ? pn : "unknown"), g.Where(x => x.ScrappedOn > cur).Sum(x => x.Quantity), g.Where(x => x.ScrappedOn <= cur).Sum(x => x.Quantity), 0)));
        contributors.AddRange(rows.Where(r => r.SupplierId != null).GroupBy(r => r.SupplierId).Select(g => new TrendRow("Supplier " + sups.GetValueOrDefault(g.Key!.Value, "?"), g.Where(x => x.ScrappedOn > cur).Sum(x => x.Quantity), g.Where(x => x.ScrappedOn <= cur).Sum(x => x.Quantity), 0)));
        contributors.AddRange(rows.GroupBy(r => r.Reason.Trim().ToLowerInvariant()).Select(g => new TrendRow("Reason: " + g.First().Reason, g.Where(x => x.ScrappedOn > cur).Sum(x => x.Quantity), g.Where(x => x.ScrappedOn <= cur).Sum(x => x.Quantity), 0)));
        var ranked = contributors.Select(c => c with { Change = c.Current - c.Previous }).Where(c => c.Change != 0).OrderByDescending(c => c.Change).Take(8).ToList();
        var delta = curTotal - prevTotal;
        var text = delta <= 0 ? $"Scrap did not increase: {curTotal:0.##} units in the last 30 days vs {prevTotal:0.##} in the 30 before."
            : $"Scrap rose by {delta:0.##} units ({(prevTotal == 0 ? "from zero" : $"{delta / prevTotal * 100:0}%")}): {curTotal:0.##} vs {prevTotal:0.##}. Largest increases: {string.Join("; ", ranked.Take(3).Select(r => $"{r.Label} +{r.Change:0.##}"))}.";
        return new(q, "explain-scrap", text, [], ranked, Method + " Compares the last 30 days with the previous 30 by part, supplier and reason.", Confidence(rows.Count + floor.Count));
    }

    async Task<AssistAnswer> AuditPrepAsync(string q, DateOnly today, CancellationToken ct)
    {
        var recs = new List<AssistRecord>();
        if (await m.TryScopeAsync(db.Capas.AsNoTracking(), Permissions.CapaView, ct) is { } c)
            recs.AddRange((await c.Where(x => x.Status != CapaStatus.Closed && x.Status != CapaStatus.Cancelled).Take(20).ToListAsync(ct)).Select(x => new AssistRecord("Capa", x.Id, x.Number, x.Title, x.Status.ToString(), $"/capa/{x.Id}", "Open CAPA")));
        if (await m.TryScopeAsync(db.AuditFindings.AsNoTracking(), Permissions.AuditsView, ct) is { } f)
            recs.AddRange((await f.Where(x => x.Status != FindingStatus.Closed).Take(20).ToListAsync(ct)).Select(x => new AssistRecord("AuditFinding", x.Id, x.Number, x.Statement.Length > 100 ? x.Statement[..100] : x.Statement, x.Status.ToString(), $"/audits/findings/{x.Id}", "Open finding")));
        if (await m.TryScopeAsync(db.Equipment.AsNoTracking(), Permissions.CalibrationView, ct) is { } e)
            recs.AddRange((await e.Where(x => x.NextDueOn < today && x.Status != EquipmentStatus.Retired).Take(20).ToListAsync(ct)).Select(x => new AssistRecord("Equipment", x.Id, x.Number, x.Name, x.Status.ToString(), $"/calibration/equipment/{x.Id}", "Calibration overdue")));
        if (await m.TryScopeAsync(db.Ncrs.AsNoTracking(), Permissions.NcrView, ct) is { } n)
            recs.AddRange((await n.Where(x => x.Status != NcrStatus.Closed && x.Status != NcrStatus.Cancelled && x.DetectedOn < today.AddDays(-30)).Take(20).ToListAsync(ct)).Select(x => new AssistRecord("Ncr", x.Id, x.Number, x.Title, x.Status.ToString(), $"/ncr/{x.Id}", "Open over 30 days")));
        return new(q, "audit-prep", recs.Count == 0 ? "Nothing open that an auditor would flag in the records you can view." : $"{recs.Count} item(s) to resolve or explain before the audit: open CAPAs, open findings, overdue calibration and NCRs open over 30 days.", recs, [], Method, null);
    }

    static decimal Confidence(int n) => n <= 0 ? 0 : Math.Round(1 - 1 / (decimal)Math.Sqrt(n + 1), 2);

    /// <summary>Summary of one record (NCR, CAPA or complaint) from its own fields.</summary>
    public async Task<AssistAnswer> SummarizeAsync(string type, Guid id, CancellationToken ct)
    {
        await m.ViewAsync(type, id, ct);
        string text; AssistRecord rec;
        switch (type)
        {
            case "Ncr":
            {
                var n = await db.Ncrs.AsNoTracking().FirstAsync(x => x.Id == id, ct);
                text = $"{n.Number} ({n.Status}, {n.Severity} severity) detected {n.DetectedOn:yyyy-MM-dd}: {n.Title}. {(n.PartNumber is null ? "" : $"Part {n.PartNumber}{(n.LotNumber is null ? "" : $", lot {n.LotNumber}")}. ")}"
                    + $"{(n.QuantityAffected is null ? "" : $"{n.QuantityAffected:0.##} affected. ")}{(n.Disposition == Disposition.None ? "No disposition yet." : $"Disposition {n.Disposition}.")}{(n.Investigation is null ? "" : $" Investigation: {Trim(n.Investigation)}")}";
                rec = new("Ncr", n.Id, n.Number, n.Title, n.Status.ToString(), $"/ncr/{n.Id}");
                break;
            }
            case "Capa":
            {
                var c = await db.Capas.AsNoTracking().FirstAsync(x => x.Id == id, ct);
                var open = await db.ActionItems.CountAsync(a => a.SourceType == "Capa" && a.SourceId == id && a.CompletedAt == null && a.Status != ActionStatus.Cancelled, ct);
                text = $"{c.Number} ({c.Status}): {c.Title}. Problem: {Trim(c.ProblemStatement)} {(c.RootCause is null ? "Root cause not recorded yet." : $"Root cause ({c.RootCauseMethod}): {Trim(c.RootCause)}")} {open} open action(s).";
                rec = new("Capa", c.Id, c.Number, c.Title, c.Status.ToString(), $"/capa/{c.Id}");
                break;
            }
            case "Complaint":
            {
                var c = await db.Complaints.AsNoTracking().FirstAsync(x => x.Id == id, ct);
                text = $"{c.Number} ({c.Status}, {c.Severity}) from customer reported {c.ReportedOn:yyyy-MM-dd}: {c.Title}. {(c.ReturnAuthorization is null ? "" : $"Return {c.ReturnAuthorization}. ")}{(c.RootCause is null ? "Root cause open." : $"Root cause: {Trim(c.RootCause)}")}";
                rec = new("Complaint", c.Id, c.Number, c.Title, c.Status.ToString(), $"/complaints/{c.Id}");
                break;
            }
            default: throw new AppException("Summaries are available for NCRs, CAPAs and complaints.");
        }
        return new($"Summarize {type}", "summary", text, [rec], [], Method + " Assembled from the record's own fields.", null);
    }

    static string Trim(string? s) => s is null ? "" : s.Length > 240 ? s[..240] + "…" : s;

    /// <summary>
    /// Investigation assistant for an NCR: similar historical NCRs (TF-IDF cosine over title, description, defect code, part,
    /// process), suggested root causes and actions from CAPAs linked to those NCRs, and recurring defect codes. Suggestions only.
    /// </summary>
    public async Task<Investigation> InvestigateAsync(Guid ncrId, CancellationToken ct)
    {
        await m.ViewAsync("Ncr", ncrId, ct);
        var subject = await db.Ncrs.AsNoTracking().FirstAsync(x => x.Id == ncrId, ct);
        var pool = await (await m.ScopeAsync(db.Ncrs.AsNoTracking(), Permissions.NcrView, ct)).Where(x => x.Id != ncrId && x.Status != NcrStatus.Cancelled).OrderByDescending(x => x.CreatedAt).Take(2000).ToListAsync(ct);
        string Doc(Ncr n) => string.Join(' ', n.Title, n.Description, n.DefectCode, n.PartNumber, n.Process, n.Operation, n.ActualCondition);
        var docs = pool.Select(n => (n, Terms(Doc(n)))).ToList();
        var df = new Dictionary<string, int>();
        foreach (var (_, t) in docs) foreach (var w in t.Distinct()) df[w] = df.GetValueOrDefault(w) + 1;
        var N = docs.Count + 1;
        Dictionary<string, double> Vec(List<string> t) => t.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count() * Math.Log((double)N / (1 + df.GetValueOrDefault(g.Key))) + 1e-9);
        var sv = Vec(Terms(Doc(subject)));
        double Norm(Dictionary<string, double> v) => Math.Sqrt(v.Values.Sum(x => x * x));
        var sn = Norm(sv);
        var similar = docs.Select(d =>
        {
            var v = Vec(d.Item2); var dot = sv.Where(k => v.ContainsKey(k.Key)).Sum(k => k.Value * v[k.Key]);
            var sim = sn == 0 || Norm(v) == 0 ? 0 : dot / (sn * Norm(v));
            if (subject.PartId is not null && d.n.PartId == subject.PartId) sim += 0.1;
            if (subject.SupplierId is not null && d.n.SupplierId == subject.SupplierId) sim += 0.05;
            if (subject.DefectCode is not null && d.n.DefectCode == subject.DefectCode) sim += 0.15;
            return new SimilarNcr(new("Ncr", d.n.Id, d.n.Number, d.n.Title, d.n.Status.ToString(), $"/ncr/{d.n.Id}", d.n.DetectedOn.ToString("yyyy-MM-dd")), (decimal)Math.Round(Math.Min(sim, 1), 3),
                sv.Keys.Intersect(v.Keys).OrderByDescending(k => sv[k]).Take(5).ToList());
        }).Where(x => x.Similarity >= 0.15m).OrderByDescending(x => x.Similarity).Take(10).ToList();
        var simIds = similar.Select(s => s.Ncr.Id).ToList();
        var capaIds = await db.RecordLinks.AsNoTracking().Where(l => l.FromType == "Ncr" && simIds.Contains(l.FromId) && l.ToType == "Capa").Select(l => new { l.FromId, l.ToId }).ToListAsync(ct);
        var capaScope = await m.TryScopeAsync(db.Capas.AsNoTracking(), Permissions.CapaView, ct);
        var ids = capaIds.Select(x => x.ToId).Distinct().ToList();
        var capas = capaScope is null ? [] : await capaScope.Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        var causes = capas.Where(c => !string.IsNullOrWhiteSpace(c.RootCause)).GroupBy(c => c.RootCause!.Trim().ToLowerInvariant())
            .Select(g => new Suggestion(g.First().RootCause!.Trim(), g.Count(), Confidence(g.Count()) * (decimal)(similar.Where(s => capaIds.Any(x => x.FromId == s.Ncr.Id && g.Any(c => c.Id == x.ToId))).Select(s => (double)s.Similarity).DefaultIfEmpty(0).Max()),
                g.Select(c => c.Number).ToList())).OrderByDescending(s => s.Support).ThenByDescending(s => s.Confidence).Take(5).ToList();
        var acts = await db.ActionItems.AsNoTracking().Where(a => a.SourceType == "Capa" && ids.Contains(a.SourceId)).Select(a => new { a.Title, OwnerId = a.SourceId }).ToListAsync(ct);
        var actions = acts.GroupBy(a => a.Title.Trim().ToLowerInvariant()).Select(g => new Suggestion(g.First().Title.Trim(), g.Count(), Confidence(g.Count()), g.Select(a => capas.FirstOrDefault(c => c.Id == a.OwnerId)?.Number ?? "").Distinct().ToList()))
            .OrderByDescending(s => s.Support).Take(5).ToList();
        var today = await m.TodayAsync(subject.SiteId, ct);
        var recurring = pool.Where(n => n.DefectCode != null && n.DetectedOn > today.AddDays(-180)).GroupBy(n => n.DefectCode!)
            .Select(g => new TrendRow(g.Key, g.Count(x => x.DetectedOn > today.AddDays(-90)), g.Count(x => x.DetectedOn <= today.AddDays(-90)), 0)).Select(t => t with { Change = t.Current - t.Previous })
            .Where(t => t.Current + t.Previous >= 2).OrderByDescending(t => t.Current).Take(10).ToList();
        return new Investigation(new("Ncr", subject.Id, subject.Number, subject.Title, subject.Status.ToString(), $"/ncr/{subject.Id}"), similar, causes, actions, recurring,
            "TF-IDF cosine similarity over NCR title, description, defect code, part, process and operation (+0.15 same defect code, +0.1 same part, +0.05 same supplier). Root causes and actions come from CAPAs linked to similar NCRs.",
            "Suggestions only. The assistant never closes or approves records; the investigation team decides.");
    }

    /// <summary>Recurring defects, suppliers, customers and process failures (last 90 days vs the 90 before).</summary>
    public async Task<Dictionary<string, List<TrendRow>>> TrendsAsync(CancellationToken ct)
    {
        var today = await m.TodayAsync(null, ct); var cut = today.AddDays(-90); var start = today.AddDays(-180);
        var res = new Dictionary<string, List<TrendRow>>();
        List<TrendRow> Rank<T>(IEnumerable<T> rows, Func<T, string?> key, Func<T, DateOnly> on) => rows.Where(r => key(r) is not null).GroupBy(key!)
            .Select(g => new TrendRow(g.Key!, g.Count(x => on(x) > cut), g.Count(x => on(x) <= cut), 0)).Select(t => t with { Change = t.Current - t.Previous }).Where(t => t.Current >= 2 || t.Current + t.Previous >= 3).OrderByDescending(t => t.Current).Take(10).ToList();
        if (await m.TryScopeAsync(db.Ncrs.AsNoTracking(), Permissions.NcrView, ct) is { } n)
        {
            var rows = await n.Where(x => x.DetectedOn > start && x.Status != NcrStatus.Cancelled).Select(x => new { x.DefectCode, x.SupplierId, x.CustomerId, x.Process, x.DetectedOn }).ToListAsync(ct);
            var sups = await db.Suppliers.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.LegalName, ct);
            var custs = await db.Customers.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
            res["defects"] = Rank(rows, r => r.DefectCode, r => r.DetectedOn);
            res["suppliers"] = Rank(rows, r => r.SupplierId is { } s ? sups.GetValueOrDefault(s) : null, r => r.DetectedOn);
            res["customers"] = Rank(rows, r => r.CustomerId is { } c ? custs.GetValueOrDefault(c) : null, r => r.DetectedOn);
            res["processes"] = Rank(rows, r => r.Process, r => r.DetectedOn);
        }
        return res;
    }

    /// <summary>Risk estimates (0–100) with confidence from the number of observations. Transparent weighted indicators, not a trained model.</summary>
    public async Task<List<RiskScore>> PredictAsync(string kind, CancellationToken ct)
    {
        var today = await m.TodayAsync(null, ct); var since = today.AddDays(-180);
        var list = new List<RiskScore>();
        switch (kind)
        {
            case "supplier":
            {
                var sups = await m.ScopeAsync(db.Suppliers.AsNoTracking(), Permissions.SuppliersView, ct);
                var aql = await m.TryScopeAsync(db.AqlInspections.AsNoTracking(), Permissions.InspectionView, ct);
                var ncr = await m.TryScopeAsync(db.Ncrs.AsNoTracking(), Permissions.NcrView, ct);
                foreach (var s in await sups.ToListAsync(ct))
                {
                    var lots = aql is null ? [] : await aql.Where(a => a.SupplierId == s.Id && a.OverallResult != null).Select(a => a.OverallResult).ToListAsync(ct);
                    var ncrs = ncr is null ? 0 : await ncr.CountAsync(x => x.SupplierId == s.Id && x.DetectedOn > since, ct);
                    var rej = lots.Count == 0 ? 0 : lots.Count(x => x == AqlDecision.Reject) / (decimal)lots.Count;
                    var status = s.Status is Domain.MasterData.SupplierStatus.Suspended or Domain.MasterData.SupplierStatus.Disqualified ? 30 : s.Status == Domain.MasterData.SupplierStatus.Conditional ? 15 : s.Status == Domain.MasterData.SupplierStatus.Approved ? 0 : 10;
                    var score = Math.Min(100, rej * 50 + Math.Min(ncrs, 5) * 4 + status + (int)s.Risk * 5);
                    var drivers = new List<string>();
                    if (rej > 0) drivers.Add($"{rej:P0} of {lots.Count} AQL lots rejected"); if (ncrs > 0) drivers.Add($"{ncrs} NCR(s) in 180 days"); if (status > 0) drivers.Add($"status {s.Status}"); if (s.Risk > 0) drivers.Add($"risk rated {s.Risk}");
                    list.Add(new("supplier", s.Id, $"{s.Code} {s.LegalName}", Math.Round(score, 1), Confidence(lots.Count + ncrs), lots.Count + ncrs, drivers));
                }
                break;
            }
            case "product" or "complaint" or "ncr":
            {
                var parts = await m.ScopeAsync(db.Parts.AsNoTracking(), Permissions.MasterDataView, ct);
                var ncr = await m.TryScopeAsync(db.Ncrs.AsNoTracking(), Permissions.NcrView, ct);
                var cps = await m.TryScopeAsync(db.Complaints.AsNoTracking(), Permissions.ComplaintsView, ct);
                foreach (var p in await parts.Take(500).ToListAsync(ct))
                {
                    var n = ncr is null ? 0 : await ncr.CountAsync(x => x.PartId == p.Id && x.DetectedOn > since, ct);
                    var c = cps is null ? 0 : await cps.CountAsync(x => x.PartId == p.Id && x.ReportedOn > since, ct);
                    var months = 6m;
                    // probability of at least one event next month from a Poisson rate λ = events / months
                    decimal P(int events) => events == 0 ? 0 : Math.Round((decimal)(1 - Math.Exp(-(double)(events / months))) * 100, 1);
                    var score = kind == "complaint" ? P(c) : kind == "ncr" ? P(n) : Math.Min(100, n * 6 + c * 12 + (p.HasSpecialCharacteristics ? 10 : 0));
                    if (n + c == 0 && kind != "product") continue;
                    list.Add(new(kind, p.Id, p.PartNumber + " " + p.Name, score, Confidence(n + c), n + c,
                        [$"{n} NCR(s) and {c} complaint(s) in 180 days", .. p.HasSpecialCharacteristics ? new[] { "has special characteristics" } : []]));
                }
                break;
            }
            case "process":
            {
                var pe = await m.ScopeAsync(db.ProductionEntries.AsNoTracking(), Permissions.ProductionView, ct);
                var from = new DateTimeOffset(since.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                var rows = await pe.Where(x => x.RecordedAt > from && x.WorkCenter != null).GroupBy(x => x.WorkCenter!)
                    .Select(g => new { Wc = g.Key, Bad = g.Sum(x => x.Rejected + x.Scrapped), Total = g.Sum(x => x.Good + x.Rejected + x.Scrapped), Oos = g.Count(x => x.InSpec == false), Down = g.Sum(x => x.DowntimeMinutes ?? 0), N = g.Count() }).ToListAsync(ct);
                foreach (var r in rows)
                {
                    var rate = r.Total == 0 ? 0 : r.Bad / r.Total;
                    list.Add(new("process", Guid.Empty, r.Wc, Math.Round(Math.Min(100, rate * 200 + r.Oos * 3 + Math.Min(r.Down / 60, 20)), 1), Confidence(r.N), r.N,
                        [$"{rate:P1} reject/scrap", $"{r.Oos} out-of-spec reading(s)", $"{r.Down:0} min downtime"]));
                }
                break;
            }
            default: throw new AppException("Kind is supplier, product, process, complaint or ncr.");
        }
        return list.OrderByDescending(x => x.Score).ToList();
    }

    /// <summary>Monthly executive summary from KPIs, trends and open risks in scope.</summary>
    public async Task<ExecutiveSummary> ExecutiveAsync(DateOnly month, CancellationToken ct)
    {
        var start = new DateOnly(month.Year, month.Month, 1); var end = start.AddMonths(1).AddDays(-1);
        var keys = new[] { "ppm", "dppm", "fpy", "copq", "scrap", "supplier", "audits" };
        var series = new List<KpiSeries>();
        foreach (var k in keys) series.Add(await kpis.SeriesAsync(k, KpiPeriod.Monthly, start.AddMonths(-2), end, null, ct));
        var hl = new List<string>(); var risks = new List<string>();
        foreach (var s in series)
        {
            var cur = s.Points[^1].Value; var prev = s.Points.Count > 1 ? s.Points[^2].Value : null;
            if (cur is null) continue;
            hl.Add($"{s.Label}: {cur:0.##} {s.Unit}{(prev is null ? "" : $" (prev {prev:0.##})")}");
        }
        foreach (var r in (await PredictAsync("supplier", ct)).Where(x => x.Score >= 40).Take(3)) risks.Add($"Supplier risk {r.Label}: {r.Score:0} ({string.Join(", ", r.Drivers)})");
        var trends = await TrendsAsync(ct);
        if (trends.TryGetValue("defects", out var d)) foreach (var t in d.Where(x => x.Change > 0).Take(3)) risks.Add($"Recurring defect {t.Label}: {t.Current} in 90 days (+{t.Change})");
        var text = $"Quality summary for {start:MMMM yyyy}. " + (hl.Count == 0 ? "No KPI data in scope for the month." : string.Join(". ", hl) + ".") + (risks.Count == 0 ? " No elevated risks." : $" {risks.Count} risk(s) to review.");
        return new($"{start:yyyy-MM}", text, hl, risks, series);
    }
}
