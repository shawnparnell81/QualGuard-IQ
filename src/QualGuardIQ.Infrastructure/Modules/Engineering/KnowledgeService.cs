using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Enterprise;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Engineering;

public sealed record KnowledgeInput(KnowledgeKind Kind, string Title, string Body, Guid SiteId, Guid? DepartmentId, List<string>? Whys, Dictionary<string, List<string>>? Fishbone, string? RootCause, string? CorrectiveAction, string? Tags);
public sealed record KnowledgeHit(string Type, Guid Id, string Number, string Title, string Url, decimal Similarity, string? RootCause, string? CorrectiveAction, List<string> SharedTerms);
public sealed record PlaybookInput(string Title, PlaybookTrigger Trigger, List<PlaybookStep> Steps);
public sealed record ApplyPlaybookInput(string RecordType, Guid RecordId, Guid AssigneeId);
public sealed record ProcessInput(string Code, string Name, string? Purpose, Guid SiteId, Guid? DepartmentId, Guid? OwnerId, Guid? MapDocumentId, int ReviewIntervalDays = 365);
public sealed record ProcessReviewInput(DateOnly ReviewedOn, string Outcome, string? Notes);
public sealed record MaturityInput(MaturitySubject Subject, Guid SubjectId, DateOnly AssessedOn, List<MaturityScore> Scores);

/// <summary>
/// Quality knowledge base (lessons, practices, root-cause analyses captured from CAPAs, guides), similar-issue discovery,
/// playbooks that turn into shared action items on a record, business process management and maturity assessments.
/// </summary>
public sealed class KnowledgeService(AppDbContext db, ModuleSupport m, INumberingService numbers, ActionService actions, QualGuardIQ.Application.Records.IRecordRegistry registry)
{
    public const string KnowledgeType = "Knowledge", ProcessType = "BusinessProcess", MaturityType = "Maturity";
    static readonly HashSet<string> Stop = ["the", "and", "for", "with", "was", "are", "from", "this", "that", "not", "per", "after", "before", "found", "during", "into", "has", "had", "have", "were", "been"];
    static List<string> Terms(string? s) => Regex.Matches((s ?? "").ToLowerInvariant(), "[a-z0-9][a-z0-9\\-]{2,}").Select(x => x.Value).Where(t => !Stop.Contains(t)).ToList();

    // ---------- knowledge ----------
    static void Check(KnowledgeInput r)
    {
        ModuleSupport.Required(r.Title, "Title", 300); ModuleSupport.Required(r.Body, "Content", 20000);
        if (r.Kind == KnowledgeKind.FiveWhy && (r.Whys?.Count(w => !string.IsNullOrWhiteSpace(w)) ?? 0) < 2) throw new AppException("A 5 Why analysis needs at least two whys.");
        if (r.Kind == KnowledgeKind.Fishbone && (r.Fishbone?.Values.Sum(v => v.Count) ?? 0) == 0) throw new AppException("A fishbone analysis needs at least one cause.");
        if (r.Fishbone is { } f && f.Keys.Any(k => !CapaAnalysis.FishboneCategories.Contains(k))) throw new AppException($"Fishbone categories are {string.Join(", ", CapaAnalysis.FishboneCategories)}.");
        if (r.Kind == KnowledgeKind.RootCause && string.IsNullOrWhiteSpace(r.RootCause)) throw new AppException("A root cause entry needs the root cause.");
    }

    public async Task<KnowledgeArticle> CreateAsync(KnowledgeInput r, string? sourceType, Guid? sourceId, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.KnowledgeEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        Check(r);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var a = new KnowledgeArticle { Number = await numbers.NextAsync(KnowledgeType, r.SiteId, ct), AuthorId = m.UserId, SiteId = r.SiteId, DepartmentId = r.DepartmentId, SourceType = sourceType, SourceId = sourceId };
        Apply(a, r);
        db.KnowledgeArticles.Add(a);
        if (sourceType != null && sourceId is { } sid) db.RecordLinks.Add(new RecordLink { FromType = sourceType, FromId = sid, ToType = KnowledgeType, ToId = a.Id, LinkType = "Lesson learned" });
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(KnowledgeType, a.Id, Text(a), ct);
        await tx.CommitAsync(ct);
        return a;
    }

    static void Apply(KnowledgeArticle a, KnowledgeInput r)
    {
        a.Kind = r.Kind; a.Title = r.Title.Trim(); a.Body = r.Body.Trim(); a.Whys = (r.Whys ?? []).Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim()).ToList();
        a.Fishbone = r.Fishbone?.Where(kv => kv.Value.Count > 0).ToDictionary(kv => kv.Key, kv => kv.Value.Select(v => v.Trim()).ToList()) ?? [];
        a.RootCause = r.RootCause?.Trim(); a.CorrectiveAction = r.CorrectiveAction?.Trim(); a.Tags = r.Tags?.Trim();
    }

    static string Text(KnowledgeArticle a) => string.Join(" ", new[] { a.Title, a.Body, a.RootCause, a.CorrectiveAction, a.Tags }.Concat(a.Whys).Concat(a.Fishbone.Values.SelectMany(v => v)).Where(x => x != null));

    public async Task UpdateAsync(Guid id, KnowledgeInput r, CancellationToken ct)
    {
        await m.EditAsync(KnowledgeType, id, ct);
        Check(r);
        var a = await db.KnowledgeArticles.FirstAsync(x => x.Id == id, ct);
        Apply(a, r);
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(KnowledgeType, a.Id, Text(a), ct);
    }

    public async Task PublishAsync(Guid id, bool published, CancellationToken ct)
    {
        await m.EditAsync(KnowledgeType, id, ct);
        var a = await db.KnowledgeArticles.FirstAsync(x => x.Id == id, ct);
        a.Published = published;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Captures a closed CAPA's root-cause analysis (5 Why, fishbone, root cause, actions) as a knowledge entry linked back to the CAPA.</summary>
    public async Task<KnowledgeArticle> CaptureFromCapaAsync(Guid capaId, CancellationToken ct)
    {
        await m.ViewAsync("Capa", capaId, ct);
        var c = await db.Capas.AsNoTracking().FirstAsync(x => x.Id == capaId, ct);
        if (string.IsNullOrWhiteSpace(c.RootCause)) throw new AppException("The CAPA has no root cause recorded yet.");
        if (await db.KnowledgeArticles.AnyAsync(k => k.SourceType == "Capa" && k.SourceId == capaId, ct)) throw new ConflictException("This CAPA is already in the knowledge base.");
        var whys = new List<string>(); var fish = new Dictionary<string, List<string>>();
        try
        {
            using var doc = JsonDocument.Parse(c.AnalysisJson);
            if (doc.RootElement.TryGetProperty("whys", out var w)) whys = w.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList();
            if (doc.RootElement.TryGetProperty("fishbone", out var f)) fish = f.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList());
        }
        catch (JsonException) { }
        var actionsText = string.Join("; ", await db.ActionItems.AsNoTracking().Where(a => a.SourceType == "Capa" && a.SourceId == capaId && a.Status != ActionStatus.Cancelled).Select(a => a.Title).ToListAsync(ct));
        var kind = whys.Count >= 2 ? KnowledgeKind.FiveWhy : fish.Count > 0 ? KnowledgeKind.Fishbone : KnowledgeKind.RootCause;
        return await CreateAsync(new KnowledgeInput(kind, $"{c.Number}: {c.Title}", c.ProblemStatement, c.SiteId, c.DepartmentId, whys, fish, c.RootCause, actionsText.Length > 0 ? actionsText : null, c.CapaType), "Capa", capaId, ct);
    }

    public async Task<object> GetAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(KnowledgeType, id, ct);
        var a = await db.KnowledgeArticles.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        return new { Article = a, Kind = a.Kind.ToString(), Similar = (await SimilarAsync(Text(a), ct)).Where(h => h.Id != id).Take(5) };
    }

    public async Task<List<KnowledgeArticle>> ListAsync(string? q, KnowledgeKind? kind, CancellationToken ct)
    {
        var s = await m.ScopeAsync(db.KnowledgeArticles.AsNoTracking(), Permissions.KnowledgeView, ct);
        if (kind is { } k) s = s.Where(a => a.Kind == k);
        if (!string.IsNullOrWhiteSpace(q)) { var t = "%" + q.Trim() + "%"; s = s.Where(a => EF.Functions.ILike(a.Title, t) || EF.Functions.ILike(a.Body, t) || (a.RootCause != null && EF.Functions.ILike(a.RootCause, t)) || (a.Tags != null && EF.Functions.ILike(a.Tags, t))); }
        return await s.OrderByDescending(a => a.UpdatedAt).Take(300).ToListAsync(ct);
    }

    /// <summary>Similar-issue discovery ("AI search"): TF-IDF cosine similarity of the text against knowledge entries, closed CAPAs and NCRs the user can view.</summary>
    public async Task<List<KnowledgeHit>> SimilarAsync(string text, CancellationToken ct)
    {
        ModuleSupport.Required(text, "Text", 20000);
        var docs = new List<(string Type, Guid Id, string Number, string Title, string Url, string Text, string? Rc, string? Ca)>();
        var kq = await m.TryScopeAsync(db.KnowledgeArticles.AsNoTracking(), Permissions.KnowledgeView, ct);
        if (kq != null) docs.AddRange((await kq.OrderByDescending(a => a.UpdatedAt).Take(2000).ToListAsync(ct)).Select(a => ("Knowledge", a.Id, a.Number, a.Title, $"/knowledge/{a.Id}", Text(a), a.RootCause, a.CorrectiveAction)));
        var cq = await m.TryScopeAsync(db.Capas.AsNoTracking(), Permissions.CapaView, ct);
        if (cq != null) docs.AddRange((await cq.Where(c => c.RootCause != null).OrderByDescending(c => c.UpdatedAt).Take(2000).Select(c => new { c.Id, c.Number, c.Title, c.ProblemStatement, c.RootCause }).ToListAsync(ct))
            .Select(c => ("Capa", c.Id, c.Number, c.Title, $"/capa/{c.Id}", $"{c.Title} {c.ProblemStatement} {c.RootCause}", c.RootCause, (string?)null)));
        var nq = await m.TryScopeAsync(db.Ncrs.AsNoTracking(), Permissions.NcrView, ct);
        if (nq != null) docs.AddRange((await nq.OrderByDescending(c => c.UpdatedAt).Take(2000).Select(c => new { c.Id, c.Number, c.Title, c.Description, c.Investigation }).ToListAsync(ct))
            .Select(c => ("Ncr", c.Id, c.Number, c.Title, $"/ncr/{c.Id}", $"{c.Title} {c.Description} {c.Investigation}", (string?)null, (string?)null)));
        var tdocs = docs.Select(d => Terms(d.Text)).ToList();
        var df = new Dictionary<string, int>();
        foreach (var t in tdocs) foreach (var w in t.Distinct()) df[w] = df.GetValueOrDefault(w) + 1;
        double N = docs.Count + 1;
        Dictionary<string, double> Vec(List<string> t) => t.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count() * Math.Log(1 + N / (1 + df.GetValueOrDefault(g.Key)))); // smoothed IDF: stays positive with few documents
        var q = Vec(Terms(text)); var qn = Math.Sqrt(q.Values.Sum(v => v * v));
        if (qn == 0) return [];
        var hits = new List<KnowledgeHit>();
        for (var i = 0; i < docs.Count; i++)
        {
            var v = Vec(tdocs[i]); var vn = Math.Sqrt(v.Values.Sum(x => x * x));
            if (vn == 0) continue;
            var dot = q.Where(kv => v.ContainsKey(kv.Key)).Sum(kv => kv.Value * v[kv.Key]);
            var sim = dot / (qn * vn);
            if (sim < 0.08) continue;
            var d = docs[i];
            hits.Add(new(d.Type, d.Id, d.Number, d.Title, d.Url, Math.Round((decimal)sim, 3), d.Rc, d.Ca, q.Keys.Where(v.ContainsKey).OrderByDescending(k => q[k]).Take(6).ToList()));
        }
        return hits.OrderByDescending(h => h.Similarity).Take(20).ToList();
    }

    // ---------- playbooks ----------
    static void CheckPlaybook(PlaybookInput r)
    {
        ModuleSupport.Required(r.Title, "Title", 300);
        if (r.Steps is not { Count: > 0 }) throw new AppException("A playbook needs at least one step.");
        foreach (var s in r.Steps) { ModuleSupport.Required(s.Title, "Step title", 300); ModuleSupport.Required(s.Guidance, "Step guidance", 4000); if (s.DueDays is < 0 or > 365) throw new AppException("Step due days must be 0–365."); }
    }

    public async Task<List<Playbook>> PlaybooksAsync(PlaybookTrigger? trigger, CancellationToken ct) =>
        await db.Playbooks.AsNoTracking().Where(p => trigger == null || p.Trigger == trigger).OrderBy(p => p.Trigger).ThenBy(p => p.Title).ToListAsync(ct);

    public async Task<Playbook> CreatePlaybookAsync(PlaybookInput r, CancellationToken ct)
    {
        CheckPlaybook(r);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var p = new Playbook { Number = await numbers.NextAsync("Playbook", null, ct), Title = r.Title.Trim(), Trigger = r.Trigger, Steps = r.Steps };
        db.Playbooks.Add(p);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return p;
    }

    public async Task UpdatePlaybookAsync(Guid id, PlaybookInput r, bool? publish, CancellationToken ct)
    {
        var p = await db.Playbooks.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Playbook");
        CheckPlaybook(r);
        if (p.Published && JsonSerializer.Serialize(p.Steps) != JsonSerializer.Serialize(r.Steps)) p.Revision++;
        p.Title = r.Title.Trim(); p.Trigger = r.Trigger; p.Steps = r.Steps;
        if (publish is { } pb) p.Published = pb;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Applies a published playbook to a record: one shared action item per step, due by the step's due days.</summary>
    public async Task<int> ApplyPlaybookAsync(Guid id, ApplyPlaybookInput r, CancellationToken ct)
    {
        var p = await db.Playbooks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Playbook");
        if (!p.Published) throw new ConflictException("Only a published playbook can be applied.");
        await registry.RequireEditAsync(r.RecordType, r.RecordId, ct);
        await m.RequireActiveUserAsync(r.AssigneeId, "assignee", ct);
        var today = DateOnly.FromDateTime(m.Now.UtcDateTime);
        foreach (var s in p.Steps)
            await actions.CreateAsync(r.RecordType, r.RecordId, new CreateActionRequest($"{p.Number}: {s.Title}", s.Guidance, "Playbook", r.AssigneeId, today.AddDays(s.DueDays ?? 7), false), ct);
        db.RecordLinks.Add(new RecordLink { FromType = r.RecordType, FromId = r.RecordId, ToType = "Playbook", ToId = p.Id, LinkType = "Playbook applied" });
        await db.SaveChangesAsync(ct);
        return p.Steps.Count;
    }

    // ---------- business processes ----------
    public async Task<BusinessProcess> CreateProcessAsync(ProcessInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.ProcessEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.Code, "Code", 40); ModuleSupport.Required(r.Name, "Name", 200);
        if (r.ReviewIntervalDays is < 30 or > 1095) throw new AppException("Review interval must be 30–1095 days.");
        var code = r.Code.Trim().ToUpperInvariant();
        if (await db.BusinessProcesses.AnyAsync(p => p.SiteId == r.SiteId && p.Code == code, ct)) throw new ConflictException($"Process {code} already exists at this site.");
        if (r.MapDocumentId is { } d && !await db.Documents.AnyAsync(x => x.Id == d, ct)) throw new AppException("Select an existing process map document.");
        if (r.OwnerId is { } o) await m.RequireActiveUserAsync(o, "process owner", ct);
        var p = new BusinessProcess { Code = code, Name = r.Name.Trim(), Purpose = r.Purpose?.Trim(), SiteId = r.SiteId, DepartmentId = r.DepartmentId, OwnerId = r.OwnerId ?? m.UserId, MapDocumentId = r.MapDocumentId, ReviewIntervalDays = r.ReviewIntervalDays };
        db.BusinessProcesses.Add(p);
        if (r.MapDocumentId is { } md) db.RecordLinks.Add(new RecordLink { FromType = ProcessType, FromId = p.Id, ToType = "Document", ToId = md, LinkType = "Process map" });
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(ProcessType, p.Id, p.Purpose, ct);
        return p;
    }

    public async Task LinkKpiAsync(Guid id, Guid objectiveId, CancellationToken ct)
    {
        await m.EditAsync(ProcessType, id, ct);
        await m.ViewAsync("QualityObjective", objectiveId, ct);
        if (!await db.RecordLinks.AnyAsync(l => l.FromType == ProcessType && l.FromId == id && l.ToId == objectiveId, ct))
            db.RecordLinks.Add(new RecordLink { FromType = ProcessType, FromId = id, ToType = "QualityObjective", ToId = objectiveId, LinkType = "Process metric" });
        await db.SaveChangesAsync(ct);
    }

    public async Task ReviewProcessAsync(Guid id, ProcessReviewInput r, CancellationToken ct)
    {
        await m.EditAsync(ProcessType, id, ct);
        ModuleSupport.Required(r.Outcome, "Outcome", 200);
        var p = await db.BusinessProcesses.FirstAsync(x => x.Id == id, ct);
        db.BusinessProcessReviews.Add(new BusinessProcessReview { ProcessId = id, ReviewedOn = r.ReviewedOn, Outcome = r.Outcome.Trim(), Notes = r.Notes?.Trim(), ReviewerId = m.UserId });
        if (p.LastReviewedOn is null || r.ReviewedOn > p.LastReviewedOn) p.LastReviewedOn = r.ReviewedOn;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Process dashboard: KPIs linked as process metrics with their latest rating, NCRs raised against the process name, reviews and review status.</summary>
    public async Task<object> ProcessDashboardAsync(Guid? siteId, CancellationToken ct)
    {
        var ps = await (await m.ScopeAsync(db.BusinessProcesses.AsNoTracking(), Permissions.ProcessView, ct)).Where(p => siteId == null || p.SiteId == siteId).OrderBy(p => p.Code).ToListAsync(ct);
        var ids = ps.Select(p => p.Id).ToList();
        var links = await db.RecordLinks.AsNoTracking().Where(l => l.FromType == ProcessType && ids.Contains(l.FromId) && l.ToType == "QualityObjective").ToListAsync(ct);
        var oids = links.Select(l => l.ToId).ToList();
        var objs = await db.QualityObjectives.AsNoTracking().Where(o => oids.Contains(o.Id)).ToListAsync(ct);
        var meas = await db.KpiMeasurements.AsNoTracking().Where(x => oids.Contains(x.ObjectiveId) && x.SupersededById == null).ToListAsync(ct);
        var yr = DateOnly.FromDateTime(m.Now.UtcDateTime).AddYears(-1); var today = DateOnly.FromDateTime(m.Now.UtcDateTime);
        var names = ps.Select(p => p.Name).ToList();
        var ncrs = await db.Ncrs.AsNoTracking().Where(n => n.Process != null && names.Contains(n.Process) && n.DetectedOn >= yr).Select(n => new { n.Process, n.SiteId }).ToListAsync(ct);
        var mats = await db.MaturityAssessments.AsNoTracking().Where(a => a.Subject == MaturitySubject.Process && ids.Contains(a.SubjectId)).ToListAsync(ct);
        return ps.Select(p => new
        {
            p.Id, p.Code, p.Name, p.OwnerId, p.SiteId, p.LastReviewedOn,
            ReviewDue = (p.LastReviewedOn ?? DateOnly.FromDateTime(p.CreatedAt.UtcDateTime)).AddDays(p.ReviewIntervalDays), ReviewOverdue = (p.LastReviewedOn ?? DateOnly.FromDateTime(p.CreatedAt.UtcDateTime)).AddDays(p.ReviewIntervalDays) < today,
            Metrics = links.Where(l => l.FromId == p.Id).Select(l => objs.FirstOrDefault(o => o.Id == l.ToId)).Where(o => o != null).Select(o => new
            {
                o!.Id, o.Number, o.MetricName, o.Target, Latest = meas.Where(x => x.ObjectiveId == o.Id).OrderBy(x => x.PeriodStart).LastOrDefault()?.Value, Rating = meas.Where(x => x.ObjectiveId == o.Id).OrderBy(x => x.PeriodStart).LastOrDefault()?.Rating.ToString()
            }),
            Ncrs12m = ncrs.Count(n => n.Process == p.Name && n.SiteId == p.SiteId),
            Maturity = mats.Where(a => a.SubjectId == p.Id).OrderByDescending(a => a.AssessedOn).Select(a => (decimal?)a.Overall).FirstOrDefault()
        }).ToList();
    }

    public async Task<object> ProcessAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(ProcessType, id, ct);
        var p = await db.BusinessProcesses.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        return new { Process = p, Reviews = await db.BusinessProcessReviews.AsNoTracking().Where(r => r.ProcessId == id).OrderByDescending(r => r.ReviewedOn).ToListAsync(ct),
            Dashboard = ((IEnumerable<object>)await ProcessDashboardAsync(p.SiteId, ct)).FirstOrDefault(x => ((dynamic)x).Id == id) };
    }

    // ---------- maturity ----------
    public async Task<MaturityAssessment> AssessAsync(MaturityInput r, CancellationToken ct)
    {
        if (r.Scores is not { Count: > 0 }) throw new AppException("Score at least one area.");
        foreach (var s in r.Scores) { ModuleSupport.Required(s.Area, "Area", 200); if (s.Level is < 1 or > 5) throw new AppException("Maturity levels are 1–5."); }
        (Guid site, Guid? dept, string name) = r.Subject switch
        {
            MaturitySubject.Site => await db.Sites.AsNoTracking().Where(s => s.Id == r.SubjectId).Select(s => new { s.Id, s.Name }).FirstOrDefaultAsync(ct) is { } s ? (s.Id, (Guid?)null, s.Name) : throw new AppException("Select an existing site."),
            MaturitySubject.Process => await db.BusinessProcesses.AsNoTracking().Where(s => s.Id == r.SubjectId).Select(s => new { s.SiteId, s.DepartmentId, s.Name }).FirstOrDefaultAsync(ct) is { } p ? (p.SiteId, p.DepartmentId, p.Name) : throw new AppException("Select an existing process."),
            _ => await db.Suppliers.AsNoTracking().Where(s => s.Id == r.SubjectId).Select(s => new { s.SiteId, s.DepartmentId, s.LegalName }).FirstOrDefaultAsync(ct) is { } u ? (u.SiteId, u.DepartmentId, u.LegalName) : throw new AppException("Select an existing supplier."),
        };
        await m.RequireAsync(Permissions.ProcessEdit, site, dept, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var a = new MaturityAssessment { Number = await numbers.NextAsync(MaturityType, site, ct), Subject = r.Subject, SubjectId = r.SubjectId, SubjectName = name, AssessedOn = r.AssessedOn, Scores = r.Scores,
            Overall = Math.Round((decimal)r.Scores.Average(s => s.Level), 2), AssessorId = m.UserId, SiteId = site, DepartmentId = dept };
        db.MaturityAssessments.Add(a);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return a;
    }

    public async Task<object> MaturityAsync(MaturitySubject? subject, CancellationToken ct)
    {
        var l = await (await m.ScopeAsync(db.MaturityAssessments.AsNoTracking(), Permissions.ProcessView, ct)).Where(a => subject == null || a.Subject == subject).OrderByDescending(a => a.AssessedOn).Take(500).ToListAsync(ct);
        var latest = l.GroupBy(a => a.SubjectId).Select(g => g.First()).ToList();
        return new { Assessments = l, Benchmark = latest.GroupBy(a => a.Subject).ToDictionary(g => g.Key.ToString(), g => new { Count = g.Count(), Average = Math.Round(g.Average(a => a.Overall), 2),
            Ranking = g.OrderByDescending(a => a.Overall).Select((a, i) => new { Rank = i + 1, a.SubjectId, a.SubjectName, a.Overall, a.AssessedOn }) }) };
    }
}
