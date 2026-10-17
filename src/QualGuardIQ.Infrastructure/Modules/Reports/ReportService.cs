using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Common;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Reports;

public sealed record ReportParams(DateOnly? From, DateOnly? To, Guid? SiteId, Guid? DepartmentId);
public sealed record ReportColumn(string Key, string Label, string Type = "text");
public sealed record ReportChart(string Type, string X, string[] Y);
public sealed record ReportDefinition(string Key, string Title, string Category, string Permission, string Description, bool UsesDates);
public sealed record ReportResult(ReportDefinition Definition, ReportParams Params, List<ReportColumn> Columns, List<Dictionary<string, object?>> Rows, ReportChart? Chart);

/// <summary>
/// Permission-aware report engine. Every report reads through ModuleSupport.ScopeAsync with its module's view permission,
/// so rows outside the user's sites/departments never appear in tables, charts or exports. Rows carry "_url" for drill-down.
/// </summary>
public sealed class ReportService(AppDbContext db, ModuleSupport m, IAccessService access)
{
    private delegate Task<(List<ReportColumn>, List<Dictionary<string, object?>>, ReportChart?)> Runner(ReportParams p, DateOnly from, DateOnly to, CancellationToken ct);
    private Dictionary<string, (ReportDefinition Def, Runner Run)>? _built;
    private Dictionary<string, (ReportDefinition Def, Runner Run)> _reports => _built ??= Build();

    public IEnumerable<ReportDefinition> Definitions => _reports.Values.Select(r => r.Def);

    public async Task<List<ReportDefinition>> AvailableAsync(CancellationToken ct)
    {
        var list = new List<ReportDefinition>();
        foreach (var d in Definitions) if (!(await access.GetScopeAsync(m.UserId, d.Permission, ct)).IsEmpty) list.Add(d);
        return list;
    }

    public async Task<ReportResult> RunAsync(string key, ReportParams p, CancellationToken ct)
    {
        if (!_reports.TryGetValue(key, out var r)) throw new NotFoundException("Report");
        var today = await m.TodayAsync(p.SiteId, ct);
        var to = p.To ?? today; var from = p.From ?? to.AddMonths(-12).AddDays(1);
        if (from > to) throw new AppException("The start date must be on or before the end date.");
        if (to.DayNumber - from.DayNumber > 3660) throw new AppException("The date range is limited to ten years.");
        _zone = await m.ZoneAsync(p.SiteId, ct);
        var (cols, rows, chart) = await r.Run(p, from, to, ct);
        return new ReportResult(r.Def, p with { From = from, To = to }, cols, rows, chart);
    }

    public static string ToCsv(ReportResult r)
    {
        var sb = new StringBuilder();
        static string Esc(object? v)
        {
            var s = v switch { null => "", DateOnly d => d.ToString("yyyy-MM-dd"), DateTimeOffset t => t.ToString("u"), IFormattable f => f.ToString(null, CultureInfo.InvariantCulture), _ => v.ToString() ?? "" };
            if (s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s; // spreadsheet formula injection guard
            return s.Contains(',') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
        sb.AppendLine(string.Join(',', r.Columns.Select(c => Esc(c.Label))));
        foreach (var row in r.Rows) sb.AppendLine(string.Join(',', r.Columns.Select(c => Esc(row.GetValueOrDefault(c.Key)))));
        return sb.ToString();
    }

    private async Task<IQueryable<T>> Q<T>(IQueryable<T> q, string perm, ReportParams p, CancellationToken ct) where T : class, ISiteScoped
    {
        var s = await m.ScopeAsync(q, perm, ct);
        if (p.SiteId is { } site) s = s.Where(x => x.SiteId == site);
        if (p.DepartmentId is { } d) s = s.Where(x => x.DepartmentId == d);
        return s;
    }

    private static Dictionary<string, object?> R(params (string K, object? V)[] kv) => kv.ToDictionary(x => x.K, x => x.V);
    private TimeZoneInfo _zone = TimeZoneInfo.Utc;
    /// <summary>Start of a reporting day in the site's (or organization's) zone, as UTC.</summary>
    private DateTimeOffset Utc(DateOnly d) { var local = d.ToDateTime(TimeOnly.MinValue); return new DateTimeOffset(local, _zone.GetUtcOffset(local)).ToUniversalTime(); }
    private static List<Dictionary<string, object?>> Months(DateOnly from, DateOnly to, Func<string, Dictionary<string, object?>> row)
    {
        var list = new List<Dictionary<string, object?>>();
        for (var d = new DateOnly(from.Year, from.Month, 1); d <= to; d = d.AddMonths(1)) list.Add(row(d.ToString("yyyy-MM")));
        return list;
    }


    private Dictionary<string, (ReportDefinition Def, Runner Run)> Build()
    {
        var r = new Dictionary<string, (ReportDefinition, Runner)>();
        void Add(string key, string title, string category, string perm, string description, bool usesDates, Runner run) => r[key] = (new(key, title, category, perm, description, usesDates), run);
        static ReportColumn C(string k, string l, string t = "text") => new(k, l, t);

        // ---------- Documents ----------
        Add("documents.status", "Document status", "Documents", Permissions.DocumentsView, "Controlled documents with current revision, status and next review.", false, async (p, f, t, ct) =>
        {
            var q = (await Q(db.Documents.AsNoTracking(), Permissions.DocumentsView, p, ct)).Where(d => d.Kind == DocumentKind.Document && !d.IsArchived);
            var rows = await q.OrderBy(d => d.Number).Select(d => new { d.Id, d.Number, d.Title, d.DocumentType, d.Status, d.NextReviewDue,
                Rev = d.Revisions.Where(x => x.Status == RevisionStatus.Effective).Select(x => x.Revision).FirstOrDefault(), Pending = d.Revisions.Any(x => x.Status == RevisionStatus.InReview),
                Site = db.Sites.Where(s => s.Id == d.SiteId).Select(s => s.Code).First() }).Take(5000).ToListAsync(ct);
            return ([C("number", "Number"), C("title", "Title"), C("type", "Type"), C("revision", "Effective rev"), C("status", "Status"), C("pending", "Revision in review"), C("nextReview", "Next review", "date"), C("site", "Site")],
                rows.Select(d => R(("number", d.Number), ("title", d.Title), ("type", d.DocumentType), ("revision", d.Rev ?? "—"), ("status", d.Status.ToString()), ("pending", d.Pending ? "Yes" : ""), ("nextReview", d.NextReviewDue), ("site", d.Site), ("_url", $"/documents/{d.Id}"))).ToList(),
                null);
        });
        Add("documents.review_due", "Documents due for review", "Documents", Permissions.DocumentsView, "Active documents whose periodic review is due on or before the end date.", true, async (p, f, t, ct) =>
        {
            var q = (await Q(db.Documents.AsNoTracking(), Permissions.DocumentsView, p, ct)).Where(d => d.Status == DocumentStatus.Active && d.NextReviewDue != null && d.NextReviewDue <= t);
            var rows = await q.OrderBy(d => d.NextReviewDue).Select(d => new { d.Id, d.Number, d.Title, d.NextReviewDue, Owner = db.Users.Where(u => u.Id == d.OwnerId).Select(u => u.DisplayName).First() }).ToListAsync(ct);
            return ([C("number", "Number"), C("title", "Title"), C("due", "Review due", "date"), C("owner", "Owner")], rows.Select(d => R(("number", d.Number), ("title", d.Title), ("due", d.NextReviewDue), ("owner", d.Owner), ("_url", $"/documents/{d.Id}"))).ToList(), null);
        });
        Add("approvals.pending", "Pending approvals", "Workflow", Permissions.DocumentsView, "Open approval requests with age in days (records you can see).", false, async (p, f, t, ct) =>
        {
            var q = await Q(db.ApprovalRequests.AsNoTracking(), Permissions.DocumentsView, p, ct);
            var rows = await q.Where(a => a.Status == ApprovalStatus.Pending).OrderBy(a => a.CreatedAt).Select(a => new { a.RecordType, a.RecordNumber, a.CreatedAt, a.CurrentStep }).ToListAsync(ct);
            var now = m.Now;
            return ([C("type", "Record type"), C("number", "Record"), C("step", "Step", "number"), C("submitted", "Submitted", "datetime"), C("age", "Age (days)", "number")],
                rows.Select(a => R(("type", a.RecordType), ("number", a.RecordNumber), ("step", a.CurrentStep), ("submitted", a.CreatedAt), ("age", (int)(now - a.CreatedAt).TotalDays))).ToList(), null);
        });
        Add("approvals.cycle_time", "Approval cycle time", "Workflow", Permissions.DocumentsView, "Average days from submission to decision by record type, for approvals completed in the period; slowest types are bottlenecks.", true, async (p, f, t, ct) =>
        {
            var q = await Q(db.ApprovalRequests.AsNoTracking(), Permissions.DocumentsView, p, ct);
            var rows = await q.Where(a => a.CompletedAt != null && a.CompletedAt >= Utc(f) && a.CompletedAt < Utc(t.AddDays(1))).Select(a => new { a.RecordType, a.CreatedAt, CompletedAt = a.CompletedAt!.Value, a.Status }).ToListAsync(ct);
            var g = rows.GroupBy(a => a.RecordType).Select(x => R(("type", x.Key), ("count", x.Count()), ("avgDays", Math.Round(x.Average(a => (a.CompletedAt - a.CreatedAt).TotalDays), 1)),
                ("maxDays", Math.Round(x.Max(a => (a.CompletedAt - a.CreatedAt).TotalDays), 1)), ("rejected", x.Count(a => a.Status == ApprovalStatus.Rejected)))).OrderByDescending(x => (double)x["avgDays"]!).ToList();
            return ([C("type", "Record type"), C("count", "Completed", "number"), C("avgDays", "Average days", "number"), C("maxDays", "Longest days", "number"), C("rejected", "Rejected", "number")], g, new("bar", "type", ["avgDays"]));
        });

        // ---------- Training ----------
        Add("training.compliance", "Training compliance", "Training", Permissions.TrainingView, "Assignments by course: completed, outstanding and overdue (current status).", false, async (p, f, t, ct) =>
        {
            var today = await m.TodayAsync(p.SiteId, ct);
            var q = (await Q(db.TrainingAssignments.AsNoTracking(), Permissions.TrainingView, p, ct)).Where(a => a.Status == AssignmentStatus.Assigned || a.Status == AssignmentStatus.Completed);
            var rows = await q.GroupBy(a => a.CourseId).Select(g => new { CourseId = g.Key, Total = g.Count(), Done = g.Count(a => a.Status == AssignmentStatus.Completed), Overdue = g.Count(a => a.Status == AssignmentStatus.Assigned && a.DueOn < today) })
                .Join(db.TrainingCourses, g => g.CourseId, c => c.Id, (g, c) => new { c.Id, c.Number, c.Title, g.Total, g.Done, g.Overdue }).OrderBy(x => x.Number).ToListAsync(ct);
            return ([C("number", "Course"), C("title", "Title"), C("total", "Assigned", "number"), C("done", "Completed", "number"), C("overdue", "Overdue", "number"), C("pct", "Compliance %", "number")],
                rows.Select(x => R(("number", x.Number), ("title", x.Title), ("total", x.Total), ("done", x.Done), ("overdue", x.Overdue), ("pct", x.Total == 0 ? 100 : Math.Round(100.0 * x.Done / x.Total, 1)), ("_url", $"/training/courses/{x.Id}"))).ToList(),
                new("bar", "number", ["pct"]));
        });
        Add("training.expiring", "Expiring qualifications", "Training", Permissions.TrainingView, "Completed training that expires on or before the end date.", true, async (p, f, t, ct) =>
        {
            var q = (await Q(db.TrainingAssignments.AsNoTracking(), Permissions.TrainingView, p, ct)).Where(a => a.Status == AssignmentStatus.Completed && a.ExpiresOn != null && a.ExpiresOn <= t);
            var rows = await q.OrderBy(a => a.ExpiresOn).Select(a => new { a.ExpiresOn, Person = db.Users.Where(u => u.Id == a.UserId).Select(u => u.DisplayName).First(), Course = db.TrainingCourses.Where(c => c.Id == a.CourseId).Select(c => c.Number + " " + c.Title).First(), a.CourseId }).ToListAsync(ct);
            return ([C("person", "Person"), C("course", "Course"), C("expires", "Expires", "date")], rows.Select(x => R(("person", x.Person), ("course", x.Course), ("expires", x.ExpiresOn), ("_url", $"/training/courses/{x.CourseId}"))).ToList(), null);
        });

        // ---------- Calibration ----------
        Add("calibration.status", "Calibration status", "Equipment", Permissions.CalibrationView, "Calibrated equipment with status and due dates; overdue first.", false, async (p, f, t, ct) =>
        {
            var today = await m.TodayAsync(p.SiteId, ct);
            var q = (await Q(db.Equipment.AsNoTracking(), Permissions.CalibrationView, p, ct)).Where(e => e.RequiresCalibration && e.Status != EquipmentStatus.Retired);
            var rows = await q.OrderBy(e => e.NextDueOn).Select(e => new { e.Id, e.Number, e.Name, e.Status, e.LastCalibratedOn, e.NextDueOn }).ToListAsync(ct);
            return ([C("number", "ID"), C("name", "Name"), C("status", "Status"), C("last", "Last calibrated", "date"), C("next", "Next due", "date"), C("overdue", "Overdue")],
                rows.Select(e => R(("number", e.Number), ("name", e.Name), ("status", e.Status.ToString()), ("last", e.LastCalibratedOn), ("next", e.NextDueOn), ("overdue", e.NextDueOn < today ? "Yes" : ""), ("_url", $"/calibration/equipment/{e.Id}"))).ToList(), null);
        });
        Add("calibration.oot", "Out-of-tolerance events", "Equipment", Permissions.CalibrationView, "Impact assessments opened in the period after out-of-tolerance results.", true, async (p, f, t, ct) =>
        {
            var q = (await Q(db.ImpactAssessments.AsNoTracking(), Permissions.CalibrationView, p, ct)).Where(i => i.CreatedAt >= Utc(f) && i.CreatedAt < Utc(t.AddDays(1)));
            var rows = await q.OrderByDescending(i => i.CreatedAt).Select(i => new { i.Id, i.Number, i.Status, i.CreatedAt, i.LastKnownGoodOn, Eq = db.Equipment.Where(e => e.Id == i.EquipmentId).Select(e => e.Number + " " + e.Name).First() }).ToListAsync(ct);
            return ([C("number", "Assessment"), C("equipment", "Equipment"), C("opened", "Opened", "datetime"), C("lastGood", "Last known good", "date"), C("status", "Status")],
                rows.Select(i => R(("number", i.Number), ("equipment", i.Eq), ("opened", i.CreatedAt), ("lastGood", i.LastKnownGoodOn), ("status", i.Status.ToString()), ("_url", $"/calibration/impact/{i.Id}"))).ToList(), null);
        });

        // ---------- NCR / CAPA ----------
        Add("ncr.volume", "NCR volume and cost by month", "Nonconformance", Permissions.NcrView, "NCRs detected per month, with quantity and cost of poor quality.", true, async (p, f, t, ct) =>
        {
            var q = (await Q(db.Ncrs.AsNoTracking(), Permissions.NcrView, p, ct)).Where(n => n.DetectedOn >= f && n.DetectedOn <= t && n.Status != NcrStatus.Cancelled);
            var rows = await q.GroupBy(n => new { n.DetectedOn.Year, n.DetectedOn.Month }).Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count(), Qty = g.Sum(n => n.QuantityAffected ?? 0), Cost = g.Sum(n => n.CostOfPoorQuality ?? 0) }).ToListAsync(ct);
            return ([C("month", "Month"), C("count", "NCRs", "number"), C("qty", "Quantity", "number"), C("cost", "Cost of poor quality", "money")],
                Months(f, t, mo => { var x = rows.FirstOrDefault(r => $"{r.Year:D4}-{r.Month:D2}" == mo); return R(("month", (object?)mo), ("count", x?.Count ?? 0), ("qty", x?.Qty ?? 0), ("cost", x?.Cost ?? 0)); }), new("bar", "month", ["count"]));
        });
        Add("ncr.aging", "Open NCR aging", "Nonconformance", Permissions.NcrView, "Open NCRs with days open, oldest first.", false, async (p, f, t, ct) =>
        {
            var today = await m.TodayAsync(p.SiteId, ct);
            var q = (await Q(db.Ncrs.AsNoTracking(), Permissions.NcrView, p, ct)).Where(n => n.Status != NcrStatus.Closed && n.Status != NcrStatus.Cancelled);
            var rows = await q.OrderBy(n => n.DetectedOn).Select(n => new { n.Id, n.Number, n.Title, n.Status, n.DetectedOn, n.Source }).ToListAsync(ct);
            return ([C("number", "NCR"), C("title", "Title"), C("source", "Source"), C("status", "Status"), C("detected", "Detected", "date"), C("days", "Days open", "number")],
                rows.Select(n => R(("number", n.Number), ("title", n.Title), ("source", n.Source), ("status", n.Status.ToString()), ("detected", n.DetectedOn), ("days", today.DayNumber - n.DetectedOn.DayNumber), ("_url", $"/ncr/{n.Id}"))).ToList(), null);
        });
        Add("ncr.pareto", "NCR sources, defects and dispositions", "Nonconformance", Permissions.NcrView, "Counts by source, defect code and disposition for NCRs detected in the period.", true, async (p, f, t, ct) =>
        {
            var q = (await Q(db.Ncrs.AsNoTracking(), Permissions.NcrView, p, ct)).Where(n => n.DetectedOn >= f && n.DetectedOn <= t && n.Status != NcrStatus.Cancelled);
            var src = await q.GroupBy(n => n.Source).Select(g => new { K = g.Key, N = g.Count() }).ToListAsync(ct);
            var def = await q.GroupBy(n => n.DefectCode ?? "Uncoded").Select(g => new { K = g.Key, N = g.Count() }).ToListAsync(ct);
            var dis = await q.Where(n => n.Disposition != Disposition.None).GroupBy(n => n.Disposition).Select(g => new { K = g.Key, N = g.Count(), Cost = g.Sum(n => n.CostOfPoorQuality ?? 0) }).ToListAsync(ct);
            var rows = src.OrderByDescending(x => x.N).Select(x => R(("dimension", "Source"), ("value", x.K), ("count", x.N), ("cost", null)))
                .Concat(def.OrderByDescending(x => x.N).Select(x => R(("dimension", "Defect code"), ("value", x.K), ("count", x.N), ("cost", null))))
                .Concat(dis.OrderByDescending(x => x.N).Select(x => R(("dimension", "Disposition"), ("value", x.K.ToString()), ("count", x.N), ("cost", x.Cost)))).ToList();
            return ([C("dimension", "Dimension"), C("value", "Value"), C("count", "NCRs", "number"), C("cost", "Cost", "money")], rows, null);
        });
        Add("capa.status", "CAPA status and aging", "Nonconformance", Permissions.CapaView, "CAPAs open now or closed in the period, with age and effectiveness result.", true, async (p, f, t, ct) =>
        {
            var q = (await Q(db.Capas.AsNoTracking(), Permissions.CapaView, p, ct)).Where(c => (c.Status != CapaStatus.Closed && c.Status != CapaStatus.Cancelled) || (c.ClosedAt >= Utc(f) && c.ClosedAt < Utc(t.AddDays(1))));
            var rows = await q.OrderBy(c => c.CreatedAt).Select(c => new { c.Id, c.Number, c.Title, c.Status, c.CreatedAt, c.ClosedAt, c.WasEffective, c.EffectivenessDueOn }).ToListAsync(ct);
            var now = m.Now;
            return ([C("number", "CAPA"), C("title", "Title"), C("status", "Status"), C("opened", "Opened", "datetime"), C("days", "Age (days)", "number"), C("effective", "Effective"), C("effDue", "Effectiveness due", "date")],
                rows.Select(c => R(("number", c.Number), ("title", c.Title), ("status", c.Status.ToString()), ("opened", c.CreatedAt), ("days", (int)((c.ClosedAt ?? now) - c.CreatedAt).TotalDays),
                    ("effective", c.WasEffective is null ? "" : c.WasEffective.Value ? "Yes" : "No"), ("effDue", c.EffectivenessDueOn), ("_url", $"/capa/{c.Id}"))).ToList(), null);
        });

        // ---------- Audits ----------
        Add("audits.findings", "Audit findings", "Audits", Permissions.AuditsView, "Findings raised in the period by type and status; repeat findings share a clause.", true, async (p, f, t, ct) =>
        {
            var q = (await Q(db.AuditFindings.AsNoTracking(), Permissions.AuditsView, p, ct)).Where(x => x.CreatedAt >= Utc(f) && x.CreatedAt < Utc(t.AddDays(1)));
            var rows = await q.OrderByDescending(x => x.CreatedAt).Select(x => new { x.AuditId, x.Number, x.FindingType, x.Status, x.Clause, x.CreatedAt,
                Repeat = db.AuditFindings.Count(o => o.Clause != null && o.Clause == x.Clause && o.SiteId == x.SiteId && o.CreatedAt < x.CreatedAt) }).ToListAsync(ct);
            return ([C("number", "Finding"), C("type", "Type"), C("clause", "Clause"), C("status", "Status"), C("raised", "Raised", "datetime"), C("repeat", "Earlier findings on clause", "number")],
                rows.Select(x => R(("number", x.Number), ("type", x.FindingType.ToString()), ("clause", x.Clause), ("status", x.Status.ToString()), ("raised", x.CreatedAt), ("repeat", x.Repeat), ("_url", $"/audits/{x.AuditId}"))).ToList(), null);
        });
        Add("audits.schedule", "Audit schedule", "Audits", Permissions.AuditsView, "Audits planned to start in the period.", true, async (p, f, t, ct) =>
        {
            var q = (await Q(db.Audits.AsNoTracking(), Permissions.AuditsView, p, ct)).Where(a => a.PlannedStart >= f && a.PlannedStart <= t);
            var rows = await q.OrderBy(a => a.PlannedStart).Select(a => new { a.Id, a.Number, a.Title, a.AuditType, a.PlannedStart, a.PlannedEnd, a.Status }).ToListAsync(ct);
            return ([C("number", "Audit"), C("title", "Title"), C("type", "Type"), C("start", "Start", "date"), C("end", "End", "date"), C("status", "Status")],
                rows.Select(a => R(("number", a.Number), ("title", a.Title), ("type", a.AuditType), ("start", a.PlannedStart), ("end", a.PlannedEnd), ("status", a.Status.ToString()), ("_url", $"/audits/{a.Id}"))).ToList(), null);
        });

        // ---------- Suppliers ----------
        Add("suppliers.performance", "Supplier performance", "Suppliers", Permissions.SuppliersView, "Scorecard periods ending in the date range.", true, async (p, f, t, ct) =>
        {
            var sup = await Q(db.Suppliers.AsNoTracking(), Permissions.SuppliersView, p, ct);
            var rows = await db.SupplierPerformance.AsNoTracking().Where(x => x.PeriodEnd >= f && x.PeriodEnd <= t).Join(sup, x => x.SupplierId, s => s.Id, (x, s) => new { s.Id, s.Code, s.LegalName, x.PeriodEnd, x.QualityScore, x.DeliveryScore, x.ResponsivenessScore, x.OverallScore, x.LotsReceived, x.LotsRejected })
                .OrderBy(x => x.OverallScore).ToListAsync(ct);
            return ([C("code", "Supplier"), C("name", "Name"), C("period", "Period end", "date"), C("lots", "Lots", "number"), C("rejected", "Rejected", "number"), C("quality", "Quality", "number"), C("delivery", "Delivery", "number"), C("resp", "Responsiveness", "number"), C("overall", "Overall", "number")],
                rows.Select(x => R(("code", x.Code), ("name", x.LegalName), ("period", x.PeriodEnd), ("lots", x.LotsReceived), ("rejected", x.LotsRejected), ("quality", x.QualityScore), ("delivery", x.DeliveryScore), ("resp", x.ResponsivenessScore), ("overall", x.OverallScore), ("_url", $"/suppliers/{x.Id}"))).ToList(),
                new("bar", "code", ["overall"]));
        });
        Add("suppliers.scars", "Supplier corrective actions", "Suppliers", Permissions.SuppliersView, "SCARs open now or issued in the period.", true, async (p, f, t, ct) =>
        {
            var q = (await Q(db.SupplierCorrectiveActions.AsNoTracking(), Permissions.SuppliersView, p, ct)).Where(c => (c.Status != ScarStatus.Closed && c.Status != ScarStatus.Cancelled) || (c.CreatedAt >= Utc(f) && c.CreatedAt < Utc(t.AddDays(1))));
            var rows = await q.OrderBy(c => c.ResponseDueOn).Select(c => new { c.Id, c.Number, c.Title, c.Status, c.ResponseDueOn, Supplier = db.Suppliers.Where(s => s.Id == c.SupplierId).Select(s => s.Code).First() }).ToListAsync(ct);
            return ([C("number", "SCAR"), C("supplier", "Supplier"), C("title", "Title"), C("status", "Status"), C("due", "Response due", "date")],
                rows.Select(c => R(("number", c.Number), ("supplier", c.Supplier), ("title", c.Title), ("status", c.Status.ToString()), ("due", c.ResponseDueOn), ("_url", $"/suppliers/scar/{c.Id}"))).ToList(), null);
        });

        // ---------- Complaints / inspection / risk / change ----------
        Add("complaints.trend", "Complaint trend", "Complaints", Permissions.ComplaintsView, "Complaints reported per month.", true, async (p, f, t, ct) =>
        {
            var q = (await Q(db.Complaints.AsNoTracking(), Permissions.ComplaintsView, p, ct)).Where(c => c.ReportedOn >= f && c.ReportedOn <= t && c.Status != ComplaintStatus.Cancelled);
            var rows = await q.GroupBy(c => new { c.ReportedOn.Year, c.ReportedOn.Month }).Select(g => new { g.Key.Year, g.Key.Month, N = g.Count(), Open = g.Count(c => c.Status != ComplaintStatus.Closed) }).ToListAsync(ct);
            return ([C("month", "Month"), C("count", "Complaints", "number"), C("open", "Still open", "number")],
                Months(f, t, mo => { var x = rows.FirstOrDefault(r => $"{r.Year:D4}-{r.Month:D2}" == mo); return R(("month", (object?)mo), ("count", x?.N ?? 0), ("open", x?.Open ?? 0)); }), new("line", "month", ["count"]));
        });
        Add("inspection.results", "Inspection results and rejection trend", "Inspection", Permissions.InspectionView, "Completed inspections per month with pass, fail and rejection counts.", true, async (p, f, t, ct) =>
        {
            var q = (await Q(db.Inspections.AsNoTracking(), Permissions.InspectionView, p, ct)).Where(i => i.InspectedOn >= f && i.InspectedOn <= t && i.Status != InspectionStatus.InProgress && i.Status != InspectionStatus.Cancelled);
            var rows = await q.GroupBy(i => new { i.InspectedOn.Year, i.InspectedOn.Month }).Select(g => new { g.Key.Year, g.Key.Month, N = g.Count(),
                Failed = g.Count(i => i.Status == InspectionStatus.Failed || i.Status == InspectionStatus.Rejected || db.InspectionResults.Any(r => r.InspectionId == i.Id && !r.Pass)), Rejected = g.Count(i => i.Status == InspectionStatus.Rejected) }).ToListAsync(ct);
            return ([C("month", "Month"), C("count", "Inspections", "number"), C("failed", "Failed", "number"), C("rejected", "Rejected", "number"), C("fpy", "First-pass %", "number")],
                Months(f, t, mo => { var x = rows.FirstOrDefault(r => $"{r.Year:D4}-{r.Month:D2}" == mo); var n = x?.N ?? 0; var fl = x?.Failed ?? 0;
                    return R(("month", (object?)mo), ("count", n), ("failed", fl), ("rejected", x?.Rejected ?? 0), ("fpy", n == 0 ? null : Math.Round(100.0 * (n - fl) / n, 1))); }), new("bar", "month", ["count", "failed"]));
        });
        Add("risks.status", "Risk status", "Risk", Permissions.RisksView, "Open risks and opportunities by current band.", false, async (p, f, t, ct) =>
        {
            var q = (await Q(db.Risks.AsNoTracking(), Permissions.RisksView, p, ct)).Where(x => x.Status != RiskStatus.Closed && x.Status != RiskStatus.Cancelled);
            var rows = await q.OrderByDescending(x => x.ResidualScore ?? x.InitialScore).Select(x => new { x.Id, x.Number, x.Kind, x.Category, x.Title, x.Status, Score = x.ResidualScore ?? x.InitialScore, Band = x.ResidualBand ?? x.InitialBand, x.ReviewDueOn, x.Escalated }).ToListAsync(ct);
            return ([C("number", "Number"), C("kind", "Type"), C("category", "Category"), C("title", "Title"), C("score", "Score", "number"), C("band", "Band"), C("status", "Status"), C("review", "Review due", "date"), C("escalated", "Escalated")],
                rows.Select(x => R(("number", x.Number), ("kind", x.Kind.ToString()), ("category", x.Category), ("title", x.Title), ("score", x.Score), ("band", x.Band), ("status", x.Status.ToString()), ("review", x.ReviewDueOn), ("escalated", x.Escalated ? "Yes" : ""), ("_url", $"/risks/{x.Id}"))).ToList(), null);
        });
        Add("changes.status", "Change status", "Changes", Permissions.ChangesView, "Changes open now or closed in the period, with open affected items.", true, async (p, f, t, ct) =>
        {
            var q = (await Q(db.ChangeRequests.AsNoTracking(), Permissions.ChangesView, p, ct)).Where(c => (c.Status != ChangeStatus.Closed && c.Status != ChangeStatus.Cancelled && c.Status != ChangeStatus.Rejected) || (c.ClosedAt >= Utc(f) && c.ClosedAt < Utc(t.AddDays(1))));
            var rows = await q.OrderBy(c => c.CreatedAt).Select(c => new { c.Id, c.Number, c.Title, c.ChangeType, c.Status, c.PlannedEffectiveOn, c.ImplementedOn, Open = db.ChangeItems.Count(i => i.ChangeId == c.Id && i.Status == ChangeItemStatus.Open) }).ToListAsync(ct);
            return ([C("number", "Change"), C("title", "Title"), C("type", "Type"), C("status", "Status"), C("planned", "Planned effective", "date"), C("implemented", "Implemented", "date"), C("open", "Open items", "number")],
                rows.Select(c => R(("number", c.Number), ("title", c.Title), ("type", c.ChangeType), ("status", c.Status.ToString()), ("planned", c.PlannedEffectiveOn), ("implemented", c.ImplementedOn), ("open", c.Open), ("_url", $"/changes/{c.Id}"))).ToList(), null);
        });
        Add("objectives.kpi", "Quality objectives and KPI performance", "Performance", Permissions.ObjectivesView, "Active objectives with their latest result and rating.", false, async (p, f, t, ct) =>
        {
            var q = (await Q(db.QualityObjectives.AsNoTracking(), Permissions.ObjectivesView, p, ct)).Where(o => o.Status == ObjectiveStatus.Active);
            var rows = await q.OrderBy(o => o.Number).Select(o => new { o.Id, o.Number, o.Title, o.MetricName, o.Target, o.Unit,
                Latest = db.KpiMeasurements.Where(k => k.ObjectiveId == o.Id && k.SupersededById == null).OrderByDescending(k => k.PeriodStart).Select(k => new { k.PeriodStart, k.Value, k.Rating }).FirstOrDefault() }).ToListAsync(ct);
            return ([C("number", "Objective"), C("title", "Title"), C("metric", "Metric"), C("target", "Target", "number"), C("value", "Latest", "number"), C("period", "Period", "date"), C("rating", "Rating")],
                rows.Select(o => R(("number", o.Number), ("title", o.Title), ("metric", o.MetricName), ("target", o.Target), ("value", o.Latest?.Value), ("period", o.Latest?.PeriodStart), ("rating", o.Latest?.Rating.ToString()), ("_url", $"/objectives/{o.Id}"))).ToList(), null);
        });

        // ---------- Actions / comparisons ----------
        Add("actions.overdue", "Overdue actions", "Actions", Permissions.ActionsView, "Open actions past their due date across all modules you can see.", false, async (p, f, t, ct) =>
        {
            var today = await m.TodayAsync(p.SiteId, ct);
            var q = (await Q(db.ActionItems.AsNoTracking(), Permissions.ActionsView, p, ct)).Where(a => (a.Status == ActionStatus.Open || a.Status == ActionStatus.InProgress) && a.DueDate < today);
            var rows = await q.OrderBy(a => a.DueDate).Select(a => new { a.Id, a.Number, a.Title, a.SourceType, a.DueDate, a.EscalatedAt, Assignee = db.Users.Where(u => u.Id == a.AssigneeId).Select(u => u.DisplayName).First() }).ToListAsync(ct);
            return ([C("number", "Action"), C("title", "Title"), C("source", "Source"), C("assignee", "Assignee"), C("due", "Due", "date"), C("days", "Days overdue", "number"), C("escalated", "Escalated", "datetime")],
                rows.Select(a => R(("number", a.Number), ("title", a.Title), ("source", a.SourceType), ("assignee", a.Assignee), ("due", a.DueDate), ("days", today.DayNumber - a.DueDate.DayNumber), ("escalated", a.EscalatedAt), ("_url", $"/actions?focus={a.Id}"))).ToList(), null);
        });
        Add("compare.sites", "Site and department comparison", "Performance", Permissions.NcrView, "NCRs, complaints, overdue actions and open CAPAs per site and department for the period (only sites you can see).", true, async (p, f, t, ct) =>
        {
            var ncr = await (await Q(db.Ncrs.AsNoTracking(), Permissions.NcrView, p, ct)).Where(n => n.DetectedOn >= f && n.DetectedOn <= t && n.Status != NcrStatus.Cancelled).GroupBy(n => new { n.SiteId, n.DepartmentId }).Select(g => new { g.Key.SiteId, g.Key.DepartmentId, N = g.Count() }).ToListAsync(ct);
            var capaQ = await m.TryScopeAsync(db.Capas.AsNoTracking(), Permissions.CapaView, ct);
            var capa = capaQ is null ? [] : await capaQ.Where(c => c.Status != CapaStatus.Closed && c.Status != CapaStatus.Cancelled).GroupBy(n => new { n.SiteId, n.DepartmentId }).Select(g => new { g.Key.SiteId, g.Key.DepartmentId, N = g.Count() }).ToListAsync(ct);
            var cmpQ = await m.TryScopeAsync(db.Complaints.AsNoTracking(), Permissions.ComplaintsView, ct);
            var cmp = cmpQ is null ? [] : await cmpQ.Where(c => c.ReportedOn >= f && c.ReportedOn <= t && c.Status != ComplaintStatus.Cancelled).GroupBy(n => new { n.SiteId, n.DepartmentId }).Select(g => new { g.Key.SiteId, g.Key.DepartmentId, N = g.Count() }).ToListAsync(ct);
            var keys = ncr.Select(x => (x.SiteId, x.DepartmentId)).Concat(capa.Select(x => (x.SiteId, x.DepartmentId))).Concat(cmp.Select(x => (x.SiteId, x.DepartmentId))).Distinct().ToList();
            if (p.SiteId is { } only) keys = keys.Where(k => k.SiteId == only).ToList();
            var sites = await db.Sites.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Code, ct);
            var depts = await db.Departments.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
            var rows = keys.Select(k => R(("site", sites.GetValueOrDefault(k.SiteId)), ("department", k.DepartmentId is { } d ? depts.GetValueOrDefault(d) : "(site level)"),
                ("ncrs", ncr.FirstOrDefault(x => x.SiteId == k.SiteId && x.DepartmentId == k.DepartmentId)?.N ?? 0), ("complaints", cmp.FirstOrDefault(x => x.SiteId == k.SiteId && x.DepartmentId == k.DepartmentId)?.N ?? 0),
                ("capas", capa.FirstOrDefault(x => x.SiteId == k.SiteId && x.DepartmentId == k.DepartmentId)?.N ?? 0))).OrderBy(x => (string?)x["site"]).ThenBy(x => (string?)x["department"]).ToList();
            return ([C("site", "Site"), C("department", "Department"), C("ncrs", "NCRs", "number"), C("complaints", "Complaints", "number"), C("capas", "Open CAPAs", "number")], rows, new("bar", "site", ["ncrs", "complaints"]));
        });
        // ---------- FAI / AQL ----------
        async Task<List<Dictionary<string, object?>>> FaiRows(IQueryable<FaiRecord> q, CancellationToken ct)
        {
            var today = await m.TodayAsync(null, ct);
            var rows = await q.OrderByDescending(x => x.CreatedAt).Take(5000).Select(x => new { x.Id, x.Number, x.Title, x.Type, x.Status, x.PartNumber, x.PartRevision, x.DrawingNumber, x.DrawingRevision, x.DueOn, x.ApprovedAt,
                Total = db.FaiCharacteristics.Count(c => c.FaiId == x.Id), Passed = db.FaiCharacteristics.Count(c => c.FaiId == x.Id && c.Result == CharResult.Pass), Failed = db.FaiCharacteristics.Count(c => c.FaiId == x.Id && c.Result == CharResult.Fail),
                Supplier = db.Suppliers.Where(s => s.Id == x.SupplierId).Select(s => s.Code).FirstOrDefault() }).ToListAsync(ct);
            return rows.Select(x => R(("number", x.Number), ("title", x.Title), ("type", x.Type.ToString()), ("status", x.Status.ToString()), ("part", $"{x.PartNumber} {x.PartRevision}".Trim()), ("drawing", x.DrawingNumber is null ? null : $"{x.DrawingNumber} rev {x.DrawingRevision}"),
                ("supplier", x.Supplier), ("characteristics", x.Total), ("passed", x.Passed), ("failed", x.Failed), ("due", x.DueOn), ("overdue", x.DueOn < today && (int)x.Status <= 4 ? "Yes" : ""), ("approved", x.ApprovedAt), ("_url", $"/fai/{x.Id}"))).ToList();
        }
        List<ReportColumn> faiCols = [C("number", "FAI"), C("title", "Title"), C("type", "Type"), C("status", "Status"), C("part", "Part"), C("drawing", "Drawing"), C("supplier", "Supplier"), C("characteristics", "Characteristics", "number"), C("passed", "Passed", "number"), C("failed", "Failed", "number"), C("due", "Due", "date"), C("overdue", "Overdue"), C("approved", "Approved", "datetime")];
        Add("fai.register", "FAI register", "FAI", Permissions.FaiView, "First article inspections created in the period with characteristic accountability.", true, async (p, f, t, ct) =>
            (faiCols, await FaiRows((await Q(db.Fais.AsNoTracking(), Permissions.FaiView, p, ct)).Where(x => x.CreatedAt >= Utc(f) && x.CreatedAt < Utc(t.AddDays(1))), ct), null));
        Add("fai.open", "Open FAIs", "FAI", Permissions.FaiView, "FAIs in draft, inspection, review, correction or approval.", false, async (p, f, t, ct) =>
            (faiCols, await FaiRows((await Q(db.Fais.AsNoTracking(), Permissions.FaiView, p, ct)).Where(x => (int)x.Status <= 4), ct), null));
        Add("fai.overdue", "Overdue FAIs", "FAI", Permissions.FaiView, "Open FAIs past their due date.", false, async (p, f, t, ct) =>
        {
            var today = await m.TodayAsync(p.SiteId, ct);
            return (faiCols, await FaiRows((await Q(db.Fais.AsNoTracking(), Permissions.FaiView, p, ct)).Where(x => (int)x.Status <= 4 && x.DueOn < today), ct), null);
        });
        Add("fai.summary", "FAI summary", "FAI", Permissions.FaiView, "FAIs by status and type for FAIs created in the period.", true, async (p, f, t, ct) =>
        {
            var q = (await Q(db.Fais.AsNoTracking(), Permissions.FaiView, p, ct)).Where(x => x.CreatedAt >= Utc(f) && x.CreatedAt < Utc(t.AddDays(1)));
            var g = await q.GroupBy(x => new { x.Type, x.Status }).Select(x => new { x.Key.Type, x.Key.Status, N = x.Count() }).ToListAsync(ct);
            return ([C("type", "Type"), C("status", "Status"), C("count", "FAIs", "number")], g.OrderBy(x => x.Type).ThenBy(x => x.Status).Select(x => R(("type", x.Type.ToString()), ("status", x.Status.ToString()), ("count", x.N))).ToList(), new("bar", "status", ["count"]));
        });
        Add("fai.yield", "FAI first-pass yield", "FAI", Permissions.FaiView, "FAIs approved per month; first pass = approved at revision 1 with no failed characteristic.", true, async (p, f, t, ct) =>
        {
            var q = (await Q(db.Fais.AsNoTracking(), Permissions.FaiView, p, ct)).Where(x => x.ApprovedAt >= Utc(f) && x.ApprovedAt < Utc(t.AddDays(1)) || (x.Status == FaiStatus.Rejected && x.UpdatedAt >= Utc(f) && x.UpdatedAt < Utc(t.AddDays(1))));
            var rows = await q.Select(x => new { At = x.ApprovedAt ?? x.UpdatedAt, x.Status, First = x.FaiRevision == 1 && !db.FaiCharacteristicResults.Any(r => r.FaiId == x.Id && r.PassFail == CharResult.Fail) }).ToListAsync(ct);
            return ([C("month", "Month"), C("decided", "Decided", "number"), C("firstPass", "First pass", "number"), C("failed", "Failed / reworked", "number"), C("passRate", "Pass rate %", "number"), C("failRate", "Fail rate %", "number")],
                Months(f, t, mo => { var x = rows.Where(r => r.At.ToString("yyyy-MM") == mo).ToList(); var fp = x.Count(r => r.First && r.Status != FaiStatus.Rejected);
                    return R(("month", (object?)mo), ("decided", x.Count), ("firstPass", fp), ("failed", x.Count - fp), ("passRate", x.Count == 0 ? null : Math.Round(100.0 * fp / x.Count, 1)), ("failRate", x.Count == 0 ? null : Math.Round(100.0 * (x.Count - fp) / x.Count, 1))); }),
                new("bar", "month", ["firstPass", "failed"]));
        });
        Add("fai.characteristics", "Characteristic pass rate", "FAI", Permissions.FaiView, "Inspected FAI characteristics by class with pass rate (measurements recorded in the period).", true, async (p, f, t, ct) =>
        {
            var fais = (await Q(db.Fais.AsNoTracking(), Permissions.FaiView, p, ct)).Select(x => x.Id);
            var rows = await db.FaiCharacteristicResults.AsNoTracking().Where(r => fais.Contains(r.FaiId) && r.InspectionDate >= Utc(f) && r.InspectionDate < Utc(t.AddDays(1)))
                .Join(db.FaiCharacteristics, r => r.CharacteristicId, c => c.Id, (r, c) => new { c.Class, r.PassFail }).GroupBy(x => x.Class).Select(g => new { g.Key, N = g.Count(), P = g.Count(x => x.PassFail == CharResult.Pass) }).ToListAsync(ct);
            return ([C("class", "Class"), C("measured", "Measurements", "number"), C("passed", "Passed", "number"), C("failed", "Failed", "number"), C("passRate", "Pass rate %", "number")],
                rows.Select(x => R(("class", x.Key.ToString()), ("measured", x.N), ("passed", x.P), ("failed", x.N - x.P), ("passRate", x.N == 0 ? 0 : Math.Round(100.0 * x.P / x.N, 1)))).ToList(), new("bar", "class", ["passRate"]));
        });
        Add("aql.lots", "AQL lot decisions", "AQL", Permissions.InspectionView, "AQL-sampled lots decided in the period.", true, async (p, f, t, ct) =>
        {
            var q = (await Q(db.AqlInspections.AsNoTracking(), Permissions.InspectionView, p, ct)).Where(a => a.Status == AqlInspectionStatus.Decided && a.InspectionDate >= Utc(f) && a.InspectionDate < Utc(t.AddDays(1)));
            var rows = await q.OrderByDescending(a => a.InspectionDate).Select(a => new { a.Id, a.InspectionId, a.InspectionNumber, a.LotNumber, a.LotSize, a.SampleSize, a.InspectionLevel, a.AqlLevel, a.Severity, a.OverallResult, a.AcceptanceQuantity, a.RejectionQuantity, a.CriticalDefects, a.MajorDefects, a.MinorDefects, a.InspectionDate,
                Supplier = db.Suppliers.Where(s => s.Id == a.SupplierId).Select(s => s.Code).FirstOrDefault() }).ToListAsync(ct);
            return ([C("number", "Inspection"), C("supplier", "Supplier"), C("lot", "Lot"), C("lotSize", "Lot size", "number"), C("sample", "Sample", "number"), C("level", "Level"), C("aql", "AQL", "number"), C("mode", "Mode"), C("ac", "Ac", "number"), C("re", "Re", "number"), C("critical", "Critical", "number"), C("major", "Major", "number"), C("minor", "Minor", "number"), C("result", "Result"), C("date", "Date", "datetime")],
                rows.Select(a => R(("number", a.InspectionNumber), ("supplier", a.Supplier), ("lot", a.LotNumber), ("lotSize", a.LotSize), ("sample", a.SampleSize), ("level", a.InspectionLevel), ("aql", a.AqlLevel), ("mode", a.Severity.ToString()), ("ac", a.AcceptanceQuantity), ("re", a.RejectionQuantity),
                    ("critical", a.CriticalDefects), ("major", a.MajorDefects), ("minor", a.MinorDefects), ("result", a.OverallResult.ToString()), ("date", a.InspectionDate), ("_url", $"/inspection/{a.InspectionId}"))).ToList(), null);
        });
        Add("aql.summary", "AQL acceptance, rejection, defect rate and PPM by month", "AQL", Permissions.InspectionView, "Lot acceptance/rejection rates, defect rate and PPM of decided AQL lots.", true, async (p, f, t, ct) =>
        {
            var q = (await Q(db.AqlInspections.AsNoTracking(), Permissions.InspectionView, p, ct)).Where(a => a.Status == AqlInspectionStatus.Decided && a.InspectionDate >= Utc(f) && a.InspectionDate < Utc(t.AddDays(1)));
            var rows = await q.Select(a => new { a.InspectionDate, a.OverallResult, a.SampleSize, D = a.CriticalDefects + a.MajorDefects + a.MinorDefects }).ToListAsync(ct);
            return ([C("month", "Month"), C("lots", "Lots", "number"), C("accepted", "Accepted", "number"), C("rejected", "Rejected", "number"), C("acceptanceRate", "Acceptance %", "number"), C("rejectionRate", "Rejection %", "number"), C("defectRate", "Defect rate %", "number"), C("ppm", "PPM", "number")],
                Months(f, t, mo => { var x = rows.Where(r => r.InspectionDate.ToString("yyyy-MM") == mo).ToList(); var n = x.Count; var acc = x.Count(r => r.OverallResult == AqlDecision.Accept); var units = x.Sum(r => r.SampleSize); var d = x.Sum(r => r.D);
                    return R(("month", (object?)mo), ("lots", n), ("accepted", acc), ("rejected", n - acc), ("acceptanceRate", n == 0 ? null : Modules.Fai.AqlService.Rate(acc, n)), ("rejectionRate", n == 0 ? null : Modules.Fai.AqlService.Rate(n - acc, n)),
                        ("defectRate", units == 0 ? null : Modules.Fai.AqlService.Rate(d, units)), ("ppm", units == 0 ? null : Modules.Fai.AqlService.Ppm(d, units))); }), new("line", "month", ["acceptanceRate", "rejectionRate"]));
        });
        Add("aql.suppliers", "Supplier AQL performance and ranking", "AQL", Permissions.InspectionView, "Suppliers ranked by lot acceptance rate then PPM for lots decided in the period.", true, async (p, f, t, ct) =>
        {
            var q = (await Q(db.AqlInspections.AsNoTracking(), Permissions.InspectionView, p, ct)).Where(a => a.SupplierId != null && a.Status == AqlInspectionStatus.Decided && a.InspectionDate >= Utc(f) && a.InspectionDate < Utc(t.AddDays(1)));
            var g = await q.GroupBy(a => a.SupplierId).Select(x => new { Id = x.Key, N = x.Count(), Acc = x.Count(a => a.OverallResult == AqlDecision.Accept), Units = x.Sum(a => a.SampleSize), D = x.Sum(a => a.CriticalDefects + a.MajorDefects + a.MinorDefects) }).ToListAsync(ct);
            var names = await db.Suppliers.AsNoTracking().Where(s => g.Select(x => x.Id).Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Code + " " + s.LegalName, ct);
            var rows = g.Select(x => new { x, rate = Modules.Fai.AqlService.Rate(x.Acc, x.N), ppm = Modules.Fai.AqlService.Ppm(x.D, x.Units) }).OrderByDescending(x => x.rate).ThenBy(x => x.ppm)
                .Select((x, k) => R(("rank", k + 1), ("supplier", names.GetValueOrDefault(x.x.Id!.Value)), ("lots", x.x.N), ("accepted", x.x.Acc), ("rejected", x.x.N - x.x.Acc), ("acceptanceRate", x.rate), ("rejectionRate", Modules.Fai.AqlService.Rate(x.x.N - x.x.Acc, x.x.N)), ("ppm", x.ppm), ("_url", $"/suppliers/{x.x.Id}"))).ToList();
            return ([C("rank", "Rank", "number"), C("supplier", "Supplier"), C("lots", "Lots", "number"), C("accepted", "Accepted", "number"), C("rejected", "Rejected", "number"), C("acceptanceRate", "Acceptance %", "number"), C("rejectionRate", "Rejection %", "number"), C("ppm", "PPM", "number")], rows, new("bar", "supplier", ["acceptanceRate"]));
        });
        Add("aql.defects", "AQL defect analysis", "AQL", Permissions.InspectionView, "Defects found in AQL samples by code, classification and category.", true, async (p, f, t, ct) =>
        {
            var lots = (await Q(db.AqlInspections.AsNoTracking(), Permissions.InspectionView, p, ct)).Where(a => a.InspectionDate >= Utc(f) && a.InspectionDate < Utc(t.AddDays(1))).Select(a => a.Id);
            var g = await db.AqlInspectionDefects.AsNoTracking().Where(d => lots.Contains(d.InspectionId)).Join(db.AqlDefectCategories, d => d.DefectCategoryId, c => c.Id, (d, c) => new { c.DefectCode, c.Description, c.DefectType, c.Category, d.QuantityFound })
                .GroupBy(x => new { x.DefectCode, x.Description, x.DefectType, x.Category }).Select(x => new { x.Key, Q = x.Sum(y => y.QuantityFound) }).ToListAsync(ct);
            return ([C("code", "Code"), C("description", "Description"), C("type", "Classification"), C("category", "Category"), C("quantity", "Quantity", "number")],
                g.OrderByDescending(x => x.Q).Select(x => R(("code", x.Key.DefectCode), ("description", x.Key.Description), ("type", x.Key.DefectType.ToString()), ("category", x.Key.Category), ("quantity", x.Q))).ToList(), new("bar", "code", ["quantity"]));
        });
        Add("aql.ppm", "Supplier defect PPM trend", "AQL", Permissions.InspectionView, "Defect PPM by supplier and month (decided AQL lots).", true, async (p, f, t, ct) =>
        {
            var q = (await Q(db.AqlInspections.AsNoTracking(), Permissions.InspectionView, p, ct)).Where(a => a.SupplierId != null && a.Status == AqlInspectionStatus.Decided && a.InspectionDate >= Utc(f) && a.InspectionDate < Utc(t.AddDays(1)));
            var rows = await q.Select(a => new { a.SupplierId, a.InspectionDate, a.SampleSize, D = a.CriticalDefects + a.MajorDefects + a.MinorDefects }).ToListAsync(ct);
            var names = await db.Suppliers.AsNoTracking().Where(s => rows.Select(r => r.SupplierId).Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Code, ct);
            var g = rows.GroupBy(r => (r.SupplierId, Month: r.InspectionDate.ToString("yyyy-MM"))).OrderBy(x => x.Key.Month)
                .Select(x => R(("month", x.Key.Month), ("supplier", names.GetValueOrDefault(x.Key.SupplierId!.Value)), ("units", x.Sum(r => r.SampleSize)), ("defects", x.Sum(r => r.D)), ("ppm", Modules.Fai.AqlService.Ppm(x.Sum(r => r.D), x.Sum(r => r.SampleSize))))).ToList();
            return ([C("month", "Month"), C("supplier", "Supplier"), C("units", "Units sampled", "number"), C("defects", "Defects", "number"), C("ppm", "PPM", "number")], g, null);
        });
        return r;
    }
}
