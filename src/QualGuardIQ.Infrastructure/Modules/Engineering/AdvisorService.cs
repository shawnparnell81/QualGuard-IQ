using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Analytics;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Engineering;

public sealed record AdvisorAnswer(string Topic, string Answer, List<string> Points, List<AssistRecord> Records, string Method);
public sealed record ChatMessage(string Role, string Text);
public sealed record DocumentAssistInput(string Kind, string Title, string? Text, Guid? DocumentId);
public sealed record DocumentAssistResult(string Mode, string? Draft, List<string> Issues, List<string> Recommendations, string Method);

/// <summary>
/// Quality advisor built on the shared assistant (AssistService) and enterprise intelligence: explanations of trends, risks,
/// suppliers, complaints, NCRs and CAPAs; conversation; management review drafts held until a person approves them;
/// document drafting and consistency checks; audit preparation. Deterministic, permission-aware, and it never approves anything.
/// </summary>
public sealed class AdvisorService(AppDbContext db, ModuleSupport m, AssistService assist, IntelligenceService intel, KnowledgeService knowledge)
{
    public const string Method = AssistService.Method;

    public async Task<AdvisorAnswer> ExplainAsync(string topic, Guid? id, CancellationToken ct)
    {
        switch (topic)
        {
            case "trend":
            {
                var t = await assist.TrendsAsync(ct);
                var pts = t.SelectMany(kv => kv.Value.Where(r => r.Change != 0).Take(3).Select(r => $"{kv.Key}: {r.Label} {r.Previous}→{r.Current} NCRs ({(r.Change > 0 ? "+" : "")}{r.Change}) in the last 90 days vs the 90 before")).ToList();
                return new("trend", pts.Count == 0 ? "No recurring defect, supplier, customer or process trend in the last 180 days." : $"{pts.Count} notable movements in recurring NCR sources.", pts, [], Method);
            }
            case "risk":
            {
                var rid = id ?? throw new AppException("Select the risk to explain.");
                await m.ViewAsync("Risk", rid, ct);
                var r = await db.Risks.AsNoTracking().FirstAsync(x => x.Id == rid, ct);
                var pts = new List<string> { $"Initial probability {r.Probability} × severity {r.Severity} = {r.InitialScore} ({r.InitialBand})." };
                if (r.ResidualScore is { } rs) pts.Add($"After mitigation: {r.ResidualProbability} × {r.ResidualSeverity} = {rs} ({r.ResidualBand}).");
                if (r.ExistingControls is { Length: > 0 }) pts.Add("Existing controls: " + r.ExistingControls);
                if (r.MitigationPlan is { Length: > 0 }) pts.Add("Mitigation plan: " + r.MitigationPlan);
                if (r.Cause is { Length: > 0 }) pts.Add("Cause: " + r.Cause);
                return new("risk", $"{r.Number} is rated {(r.ResidualBand ?? r.InitialBand)} mainly because of severity {r.ResidualSeverity ?? r.Severity} and probability {r.ResidualProbability ?? r.Probability}.", pts, [new("Risk", r.Id, r.Number, r.Title, r.Status.ToString(), $"/risks/{r.Id}")], Method);
            }
            case "supplier":
            {
                var sid = id ?? throw new AppException("Select the supplier to analyze.");
                var s = (await intel.SupplierRiskAsync(sid, null, ct)).FirstOrDefault() ?? throw new NotFoundException("Supplier");
                var worst = s.Factors.Where(f => f.Score != null).OrderBy(f => f.Score).FirstOrDefault();
                return new("supplier", $"{s.Name} health {s.Health?.ToString() ?? "unknown"} ({s.Level} risk).{(worst is null ? "" : $" Weakest factor: {worst.Factor.ToLowerInvariant()} ({worst.Score}).")}",
                    s.Factors.Select(f => $"{f.Factor}: {(f.Score?.ToString() ?? "no data")} — {f.Basis}").ToList(), [new("Supplier", s.SupplierId, s.Code, s.Name, s.Status, $"/suppliers/{s.SupplierId}")], Method);
            }
            case "complaint":
            {
                var cid = id ?? throw new AppException("Select the complaint to analyze.");
                var sum = await assist.SummarizeAsync("Complaint", cid, ct);
                var c = await db.Complaints.AsNoTracking().FirstAsync(x => x.Id == cid, ct);
                var similar = await knowledge.SimilarAsync($"{c.Title} {c.Description} {c.FailureMode}", ct);
                var same = await db.Complaints.AsNoTracking().CountAsync(x => x.Id != cid && x.CustomerId == c.CustomerId && x.FailureMode != null && x.FailureMode == c.FailureMode, ct);
                var pts = new List<string> { sum.Answer };
                if (same > 0) pts.Add($"{same} other complaint(s) from this customer report the same failure mode.");
                pts.AddRange(similar.Where(h => h.RootCause != null).Take(3).Select(h => $"Similar {h.Type} {h.Number} (similarity {h.Similarity}): root cause \"{h.RootCause}\""));
                return new("complaint", sum.Answer, pts, sum.Records.Concat(similar.Take(5).Select(h => new AssistRecord(h.Type, h.Id, h.Number, h.Title, "", h.Url))).ToList(), Method);
            }
            case "ncr":
            {
                var nid = id ?? throw new AppException("Select the NCR to analyze.");
                var inv = await assist.InvestigateAsync(nid, ct);
                var pts = inv.RootCauses.Select(r => $"Possible root cause: {r.Text} (seen {r.Support}×, confidence {r.Confidence})").Concat(inv.Actions.Select(a => $"Action used before: {a.Text}")).Take(8).ToList();
                return new("ncr", $"{inv.Similar.Count} similar NCR(s) found. {inv.Disclaimer}", pts, inv.Similar.Select(s => s.Ncr).Prepend(inv.Subject).ToList(), inv.Method);
            }
            case "capa":
            {
                var cid = id ?? throw new AppException("Select the CAPA to analyze.");
                var sum = await assist.SummarizeAsync("Capa", cid, ct);
                var c = await db.Capas.AsNoTracking().FirstAsync(x => x.Id == cid, ct);
                var pts = new List<string> { sum.Answer };
                if (c.RootCause is null) pts.Add("No root cause recorded yet; use 5 Why or fishbone from the knowledge base.");
                if (c.EffectivenessCriteria is null) pts.Add("No effectiveness criteria defined.");
                if (c.DueOn < DateOnly.FromDateTime(m.Now.UtcDateTime) && c.Status != CapaStatus.Closed) pts.Add($"Overdue since {c.DueOn:yyyy-MM-dd}.");
                var similar = await knowledge.SimilarAsync($"{c.Title} {c.ProblemStatement}", ct);
                pts.AddRange(similar.Where(h => h.Id != cid && h.RootCause != null).Take(3).Select(h => $"Similar {h.Type} {h.Number}: root cause \"{h.RootCause}\""));
                return new("capa", sum.Answer, pts, sum.Records, Method);
            }
            default: throw new AppException("Topic must be trend, risk, supplier, complaint, ncr or capa.");
        }
    }

    /// <summary>Conversation: earlier turns give context (a follow-up like "and for suppliers?" reuses the previous subject); each answer comes from the shared assistant.</summary>
    public async Task<AssistAnswer> ChatAsync(List<ChatMessage> messages, CancellationToken ct)
    {
        var users = messages.Where(x => x.Role == "user" && !string.IsNullOrWhiteSpace(x.Text)).Select(x => x.Text.Trim()).ToList();
        if (users.Count == 0) throw new AppException("Ask a question.");
        if (messages.Count > 40) throw new AppException("Start a new conversation (40 messages max).");
        var q = users[^1];
        if (users.Count > 1 && Regex.IsMatch(q, @"^(and|what about|how about|for)\b", RegexOptions.IgnoreCase)) q = users[^2] + " " + Regex.Replace(q, @"^(and|what about|how about)\s*", "", RegexOptions.IgnoreCase);
        return await assist.AskAsync(q, ct);
    }

    // ---------- management review assistant ----------
    public async Task<string> DraftManagementReviewAsync(Guid reviewId, CancellationToken ct)
    {
        await m.EditAsync("ManagementReview", reviewId, ct);
        var r = await db.ManagementReviews.FirstAsync(x => x.Id == reviewId, ct);
        if (r.Status is ReviewStatus.Approved or ReviewStatus.Cancelled) throw new ConflictException($"The review is {r.Status}.");
        var sb = new StringBuilder();
        sb.AppendLine($"ASSISTANT DRAFT for {r.Number} ({r.PeriodStart:yyyy-MM-dd} to {r.PeriodEnd:yyyy-MM-dd}) — requires human approval before use.").AppendLine();
        dynamic score = await intel.GlobalScoreAsync(ct);
        QualityScore corp = score.Corporate;
        sb.AppendLine("QUALITY SUMMARY");
        sb.AppendLine($"- Corporate quality score {corp.Score?.ToString() ?? "n/a"}.");
        foreach (var c in corp.Components) sb.AppendLine($"- {c.Component}: {c.Score?.ToString() ?? "no data"} ({c.Basis}).");
        var start = r.PeriodStart; var end = r.PeriodEnd;
        var ncrs = await db.Ncrs.AsNoTracking().CountAsync(x => x.SiteId == r.SiteId && x.DetectedOn >= start && x.DetectedOn <= end, ct);
        var capas = await db.Capas.AsNoTracking().Where(x => x.SiteId == r.SiteId && x.ClosedAt != null && x.WasEffective != null).Select(x => x.WasEffective).ToListAsync(ct);
        sb.AppendLine($"- {ncrs} NCRs raised at the site in the period; {capas.Count(x => x == true)} of {capas.Count} verified CAPAs effective.").AppendLine();
        dynamic rm = await intel.RiskMatrixAsync(r.SiteId, ct);
        sb.AppendLine("RISK SUMMARY");
        foreach (var c in rm.Categories) sb.AppendLine($"- {c.Category}: {c.Open} open, {c.High} high.");
        sb.AppendLine();
        var sups = (await intel.SupplierRiskAsync(null, null, ct)).Where(s => s.Level == "High").Take(5).ToList();
        sb.AppendLine("SUPPLIER SUMMARY");
        if (sups.Count == 0) sb.AppendLine("- No supplier at high risk.");
        foreach (var s in sups) sb.AppendLine($"- {s.Name}: health {s.Health}, weakest {s.Factors.Where(f => f.Score != null).OrderBy(f => f.Score).Select(f => f.Factor).FirstOrDefault()}.");
        sb.AppendLine();
        var cc = await db.Complaints.AsNoTracking().Where(x => x.SiteId == r.SiteId && x.ComplaintDate >= start && x.ComplaintDate <= end).Select(x => new { x.Status, x.FailureMode }).ToListAsync(ct);
        sb.AppendLine("COMPLAINT SUMMARY");
        sb.AppendLine($"- {cc.Count} complaints received, {cc.Count(x => x.Status != ComplaintStatus.Closed && x.Status != ComplaintStatus.Cancelled)} still open.");
        foreach (var g in cc.Where(x => x.FailureMode != null).GroupBy(x => x.FailureMode).OrderByDescending(g => g.Count()).Take(3)) sb.AppendLine($"- Recurring failure mode \"{g.Key}\": {g.Count()}.");
        r.AssistantDraft = sb.ToString(); r.AssistantDraftApprovedAt = null; r.AssistantDraftApprovedById = null;
        await db.SaveChangesAsync(ct);
        return r.AssistantDraft;
    }

    public async Task ApproveManagementReviewDraftAsync(Guid reviewId, string? editedText, CancellationToken ct)
    {
        await m.EditAsync("ManagementReview", reviewId, ct);
        var r = await db.ManagementReviews.FirstAsync(x => x.Id == reviewId, ct);
        if (r.AssistantDraft is null) throw new ConflictException("There is no assistant draft to approve.");
        if (editedText is { Length: > 0 }) { ModuleSupport.Required(editedText, "Draft", 20000); r.AssistantDraft = editedText.Trim(); }
        r.AssistantDraftApprovedById = m.UserId; r.AssistantDraftApprovedAt = m.Now;
        await db.SaveChangesAsync(ct);
    }

    // ---------- document assistant ----------
    static readonly Dictionary<string, string[]> Sections = new()
    {
        ["Procedure"] = ["Purpose", "Scope", "Responsibilities", "Definitions", "Procedure", "Records", "References"],
        ["WorkInstruction"] = ["Purpose", "Scope", "Safety", "Equipment and materials", "Steps", "Acceptance criteria", "Records"],
        ["Form"] = ["Header", "Instructions", "Fields", "Sign-off"],
        ["Policy"] = ["Purpose", "Policy statement", "Scope", "Responsibilities", "Review"],
    };

    public async Task<DocumentAssistResult> DocumentAsync(DocumentAssistInput r, CancellationToken ct)
    {
        if (!Sections.TryGetValue(r.Kind, out var secs)) throw new AppException($"Kind must be one of: {string.Join(", ", Sections.Keys)}.");
        ModuleSupport.Required(r.Title, "Title", 300);
        if (string.IsNullOrWhiteSpace(r.Text))
        {
            var sb = new StringBuilder($"{r.Title}\n\n");
            foreach (var s in secs) sb.AppendLine($"{s.ToUpperInvariant()}\n[{Hint(s, r.Title)}]\n");
            return new("draft", sb.ToString(), [], ["Fill each bracketed section; the draft becomes a controlled document only through document control approval."], Method);
        }
        var text = r.Text!;
        var issues = new List<string>(); var recs = new List<string>();
        foreach (var s in secs) if (!Regex.IsMatch(text, $@"(^|\n)\s*(\d+[.)]\s*)?{Regex.Escape(s)}\b", RegexOptions.IgnoreCase)) issues.Add($"Missing section: {s}.");
        var refs = Regex.Matches(text, @"\b(DOC|DWG|FT)-\d+\b").Select(x => x.Value).Distinct().ToList();
        var known = await db.Documents.AsNoTracking().Where(d => refs.Contains(d.Number)).Select(d => new { d.Number, d.Status }).ToListAsync(ct);
        foreach (var x in refs) { var k = known.FirstOrDefault(d => d.Number == x); if (k == null) issues.Add($"Referenced document {x} does not exist."); else if (k.Status == Domain.Documents.DocumentStatus.Obsolete) issues.Add($"Referenced document {x} is obsolete."); }
        if (Regex.IsMatch(text, @"\bshall\b", RegexOptions.IgnoreCase) && Regex.IsMatch(text, @"\bshould\b", RegexOptions.IgnoreCase)) issues.Add("Mixes \"shall\" (requirement) and \"should\" (recommendation); check each is intended.");
        if (Regex.IsMatch(text, @"\b(TBD|TODO|XXX)\b")) issues.Add("Contains placeholders (TBD/TODO).");
        if (r.Kind == "WorkInstruction" && !Regex.IsMatch(text, @"(^|\n)\s*\d+[.)]", RegexOptions.Multiline)) recs.Add("Number the steps so operators can follow and reference them.");
        if (r.DocumentId is { } did)
        {
            await m.ViewAsync("Document", did, ct);
            var eff = await db.DocumentRevisions.AsNoTracking().Where(x => x.DocumentId == did && x.Status == Domain.Documents.RevisionStatus.Effective).Select(x => x.EffectiveDate).FirstOrDefaultAsync(ct);
            var links = await db.RecordLinks.AsNoTracking().Where(l => (l.ToId == did || l.FromId == did) && (l.FromType == "Ncr" || l.FromType == "Capa" || l.ToType == "Ncr" || l.ToType == "Capa")).CountAsync(ct);
            if (links > 0) recs.Add($"{links} NCR/CAPA record(s) are linked to this document; review whether the text addresses their causes.");
            if (eff is { } e && e < DateOnly.FromDateTime(m.Now.UtcDateTime).AddYears(-2)) recs.Add($"Effective since {e:yyyy-MM-dd}; consider a periodic review revision.");
        }
        var sim = await knowledge.SimilarAsync(r.Title + " " + text[..Math.Min(text.Length, 4000)], ct);
        recs.AddRange(sim.Where(h => h.Type == "Knowledge").Take(3).Select(h => $"Consider lesson {h.Number}: {h.Title}."));
        return new("check", null, issues, recs, Method);
    }

    static string Hint(string section, string title) => section switch
    {
        "Purpose" => $"Why \"{title}\" exists and what it controls",
        "Scope" => "Sites, departments, products and processes covered; exclusions",
        "Responsibilities" => "Roles (not names) and what each is accountable for",
        "Procedure" or "Steps" => "Numbered steps with inputs, outputs and decision points",
        "Records" => "Records produced, where they are kept and for how long",
        _ => $"Describe {section.ToLowerInvariant()}"
    };

    // ---------- audit assistant ----------
    public async Task<object> AuditPrepAsync(Guid siteId, string? process, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.AuditsView, siteId, null, ct);
        var yr = DateOnly.FromDateTime(m.Now.UtcDateTime).AddYears(-2);
        var findings = await db.AuditFindings.AsNoTracking().Where(f => f.SiteId == siteId).Join(db.Audits.AsNoTracking(), f => f.AuditId, a => a.Id, (f, a) => new { f.Clause, f.FindingType, f.Statement, f.Status, a.Number, a.Title, a.PlannedStart, AuditId = a.Id })
            .Where(x => x.PlannedStart >= yr).ToListAsync(ct);
        var ncrProc = await db.Ncrs.AsNoTracking().Where(n => n.SiteId == siteId && n.DetectedOn >= yr && n.Process != null && (process == null || n.Process == process)).GroupBy(n => n.Process!).Select(g => new { Process = g.Key, N = g.Count() }).OrderByDescending(x => x.N).Take(5).ToListAsync(ct);
        var areas = findings.Where(f => f.Clause != null).GroupBy(f => f.Clause!).Select(g => new { Area = "Clause " + g.Key, Findings = g.Count(), Major = g.Count(x => x.FindingType == FindingType.MajorNonconformity), Open = g.Count(x => x.Status != FindingStatus.Closed) })
            .OrderByDescending(x => x.Major * 3 + x.Findings + x.Open).Take(8).ToList();
        var obligations = await db.ComplianceObligations.AsNoTracking().Where(o => o.SiteId == siteId && o.Applicable).Select(o => new { o.Reference, o.Title }).Take(200).ToListAsync(ct);
        var checklist = areas.Select(a => $"Re-verify {a.Area}: {a.Findings} finding(s) in 2 years ({a.Major} major, {a.Open} open).")
            .Concat(ncrProc.Select(p => $"Process \"{p.Process}\": {p.N} NCRs in 2 years — sample records and check controls."))
            .Concat(obligations.Where(o => !areas.Any(a => a.Area == "Clause " + o.Reference)).Take(20).Select(o => $"{o.Reference} {o.Title}: confirm evidence.")).ToList();
        var audits = findings.GroupBy(f => new { f.AuditId, f.Number, f.Title, f.PlannedStart }).OrderByDescending(g => g.Key.PlannedStart).Take(2)
            .Select(g => new { g.Key.Number, g.Key.Title, g.Key.PlannedStart, Major = g.Count(x => x.FindingType == FindingType.MajorNonconformity), Minor = g.Count(x => x.FindingType == FindingType.MinorNonconformity), Observations = g.Count(x => x.FindingType == FindingType.Observation), Clauses = g.Where(x => x.Clause != null).Select(x => x.Clause!).Distinct().ToList() }).ToList();
        var repeat = audits.Count == 2 ? audits[0].Clauses.Intersect(audits[1].Clauses).ToList() : [];
        return new { RiskAreas = areas, ProcessHotspots = ncrProc, Checklist = checklist, HistoricalFindings = findings.OrderByDescending(f => f.PlannedStart).Take(30), Comparison = new { Audits = audits, RepeatClauses = repeat },
            Method, Note = "Preparation aid only. The assistant does not plan, approve or close audits." };
    }
}
