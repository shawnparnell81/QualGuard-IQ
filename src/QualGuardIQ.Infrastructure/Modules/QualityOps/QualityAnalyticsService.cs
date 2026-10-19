using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Common;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.QualityOps;

public sealed record OpenRecord(Guid Id, string Number, string Status, DateTimeOffset CreatedAt, bool Open);
public sealed record AgingBand(string Band, int Count);
public sealed record StatusDwell(string Status, int Transitions, double AverageHours, double MaxHours, int CurrentlyIn);
public sealed record CycleTime(string Module, int Closed, double AverageDays, double MedianDays, double P90Days);
public sealed record RecurrenceGroup(string Key, string Part, string Category, int Count, bool Systemic, List<string> Records, DateTimeOffset First, DateTimeOffset Last);
public sealed record ScoreFactor(string Key, string Label, int Total, int Late, double Score, double Weight);
public sealed record ReadinessScore(string Kind, double Score, string Band, List<ScoreFactor> Factors);
public sealed record SupplierRisk(Guid SupplierId, string Code, string Name, string Status, double Score, string Band, List<string> Signals);

/// <summary>
/// Analytics over the module records and their audit history: cycle time, bottlenecks (time spent in each status), record aging,
/// CAPA effectiveness, recurrence and systemic-issue detection, quality and audit readiness scores and dynamic supplier risk.
/// Every query is limited to the sites the user may view in each module.
/// </summary>
public sealed class QualityAnalyticsService(AppDbContext db, ModuleSupport m, QualityEventService events)
{
    /// <summary>Module key, label, audit entity name, view permission, enum type of the status.</summary>
    public static readonly IReadOnlyList<(string Key, string Label, string Entity, string Permission, Type Status)> Modules =
    [
        ("ncr", "Nonconformances", nameof(Ncr), Permissions.NcrView, typeof(NcrStatus)),
        ("capa", "Corrective actions", nameof(Capa), Permissions.CapaView, typeof(CapaStatus)),
        ("complaints", "Complaints", nameof(Complaint), Permissions.ComplaintsView, typeof(ComplaintStatus)),
        ("changes", "Changes", nameof(ChangeRequest), Permissions.ChangesView, typeof(ChangeStatus)),
        ("findings", "Audit findings", nameof(AuditFinding), Permissions.AuditsView, typeof(FindingStatus)),
        ("events", "Quality events", nameof(QualityEvent), Permissions.QualityOpsView, typeof(QualityEventStatus)),
        ("deviations", "Deviations", nameof(Deviation), Permissions.QualityOpsView, typeof(QualityDeviationStatus)),
        ("investigations", "Investigations", InvestigationService.RecordType, Permissions.QualityOpsView, typeof(InvestigationStatus)),
    ];

    static readonly HashSet<string> ClosedNames = ["Closed", "Cancelled", "Rejected", "Verified"];
    static bool IsOpen(string status) => !ClosedNames.Contains(status);

    static (string Key, string Label, string Entity, string Permission, Type Status) Module(string key) =>
        Modules.FirstOrDefault(x => x.Key == key) is { Key: not null } mod ? mod : throw new AppException("Unknown module.");

    async Task<IQueryable<T>> Scope<T>(IQueryable<T> q, string permission, CancellationToken ct) where T : class, ISiteScoped => await m.ScopeAsync(q, permission, ct);

    /// <summary>Every record of the module the user may view (id, number, status, created).</summary>
    public async Task<List<OpenRecord>> RecordsAsync(string key, CancellationToken ct)
    {
        var mod = Module(key);
        async Task<List<OpenRecord>> L<T>(IQueryable<T> q, Func<IQueryable<T>, IQueryable<(Guid, string, int, DateTimeOffset)>> sel) where T : class, ISiteScoped
        {
            var rows = await sel(await Scope(q.AsNoTracking(), mod.Permission, ct)).Take(20000).ToListAsync(ct);
            return rows.Select(r => { var s = Enum.GetName(mod.Status, r.Item3) ?? r.Item3.ToString(); return new OpenRecord(r.Item1, r.Item2, s, r.Item4, IsOpen(s)); }).ToList();
        }
        return key switch
        {
            "ncr" => await L(db.Ncrs, q => q.Select(x => new ValueTuple<Guid, string, int, DateTimeOffset>(x.Id, x.Number, (int)x.Status, x.CreatedAt))),
            "capa" => await L(db.Capas, q => q.Select(x => new ValueTuple<Guid, string, int, DateTimeOffset>(x.Id, x.Number, (int)x.Status, x.CreatedAt))),
            "complaints" => await L(db.Complaints, q => q.Select(x => new ValueTuple<Guid, string, int, DateTimeOffset>(x.Id, x.Number, (int)x.Status, x.CreatedAt))),
            "changes" => await L(db.ChangeRequests, q => q.Select(x => new ValueTuple<Guid, string, int, DateTimeOffset>(x.Id, x.Number, (int)x.Status, x.CreatedAt))),
            "findings" => await L(db.AuditFindings, q => q.Select(x => new ValueTuple<Guid, string, int, DateTimeOffset>(x.Id, x.Number, (int)x.Status, x.CreatedAt))),
            "events" => await L(db.QualityEvents, q => q.Select(x => new ValueTuple<Guid, string, int, DateTimeOffset>(x.Id, x.Number, (int)x.Status, x.CreatedAt))),
            "deviations" => await L(db.Deviations, q => q.Select(x => new ValueTuple<Guid, string, int, DateTimeOffset>(x.Id, x.Number, (int)x.Status, x.CreatedAt))),
            "investigations" => await L(db.Investigations, q => q.Select(x => new ValueTuple<Guid, string, int, DateTimeOffset>(x.Id, x.Number, (int)x.Status, x.CreatedAt))),
            _ => throw new AppException("Unknown module.")
        };
    }

    /// <summary>Status transitions of the given records from the audit trail, oldest first.</summary>
    async Task<Dictionary<Guid, List<(DateTimeOffset At, string Status)>>> TransitionsAsync(string entity, Type statusType, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var result = new Dictionary<Guid, List<(DateTimeOffset, string)>>();
        foreach (var chunk in ids.Chunk(2000))
        {
            var rows = await db.AuditEntries.AsNoTracking().Where(a => a.EntityType == entity && chunk.Contains(a.EntityId) && a.Changes != null && EF.Functions.JsonExists(a.Changes, "Status"))
                .OrderBy(a => a.OccurredAt).ThenBy(a => a.Id).Select(a => new { a.EntityId, a.OccurredAt, a.Changes }).ToListAsync(ct);
            foreach (var r in rows)
            {
                using var doc = JsonDocument.Parse(r.Changes!);
                if (!doc.RootElement.TryGetProperty("Status", out var st) || !st.TryGetProperty("to", out var to)) continue;
                var name = to.ValueKind == JsonValueKind.Number ? Enum.GetName(statusType, to.GetInt32()) ?? to.GetInt32().ToString() : to.ToString();
                if (!result.TryGetValue(r.EntityId, out var list)) result[r.EntityId] = list = [];
                list.Add((r.OccurredAt, name));
            }
        }
        return result;
    }

    public async Task<List<AgingBand>> AgingAsync(string key, CancellationToken ct)
    {
        var now = m.Now;
        var open = (await RecordsAsync(key, ct)).Where(r => r.Open).ToList();
        int C(double lo, double hi) => open.Count(r => (now - r.CreatedAt).TotalDays >= lo && (now - r.CreatedAt).TotalDays < hi);
        return [new("0-30 days", C(0, 31)), new("31-60 days", C(31, 61)), new("61-90 days", C(61, 91)), new("91-180 days", C(91, 181)), new("Over 180 days", C(181, double.MaxValue))];
    }

    /// <summary>Time spent in each status (from the audit trail) for records created in the period; the slowest status is the bottleneck.</summary>
    public async Task<List<StatusDwell>> BottlenecksAsync(string key, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var mod = Module(key);
        var (start, end) = await m.DayRangeAsync(null, from, to, ct);
        var recs = (await RecordsAsync(key, ct)).Where(r => r.CreatedAt >= start && r.CreatedAt < end).ToList();
        var trans = await TransitionsAsync(mod.Entity, mod.Status, recs.Select(r => r.Id).ToList(), ct);
        var dwell = new Dictionary<string, List<double>>();
        var current = recs.Where(r => r.Open).GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.Count());
        var now = m.Now;
        foreach (var r in recs)
        {
            if (!trans.TryGetValue(r.Id, out var list) || list.Count == 0) continue;
            for (var i = 0; i < list.Count; i++)
            {
                var (at, status) = list[i];
                if (!IsOpen(status)) continue;
                var until = i + 1 < list.Count ? list[i + 1].At : now;
                if (!dwell.TryGetValue(status, out var d)) dwell[status] = d = [];
                d.Add((until - at).TotalHours);
            }
        }
        return Enum.GetNames(mod.Status).Where(IsOpen).Select(s => dwell.TryGetValue(s, out var d) && d.Count > 0
                ? new StatusDwell(s, d.Count, Math.Round(d.Average(), 1), Math.Round(d.Max(), 1), current.GetValueOrDefault(s))
                : new StatusDwell(s, 0, 0, 0, current.GetValueOrDefault(s)))
            .OrderByDescending(x => x.AverageHours).ToList();
    }

    /// <summary>Days from creation to the first closed status, for records closed in the period.</summary>
    public async Task<List<CycleTime>> CycleTimesAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var (start, end) = await m.DayRangeAsync(null, from, to, ct);
        var result = new List<CycleTime>();
        foreach (var mod in Modules)
        {
            if (!await m.CanAnyAsync(mod.Permission, ct)) continue;
            var recs = (await RecordsAsync(mod.Key, ct)).Where(r => !r.Open && r.Status is "Closed" or "Verified").ToList();
            var trans = await TransitionsAsync(mod.Entity, mod.Status, recs.Select(r => r.Id).ToList(), ct);
            var days = new List<double>();
            foreach (var r in recs)
                if (trans.TryGetValue(r.Id, out var list) && list.FirstOrDefault(t => t.Status is "Closed" or "Verified") is { Status: not null } c && c.At >= start && c.At < end)
                    days.Add((c.At - r.CreatedAt).TotalDays);
            days.Sort();
            result.Add(days.Count == 0 ? new(mod.Label, 0, 0, 0, 0)
                : new(mod.Label, days.Count, Math.Round(days.Average(), 1), Math.Round(Pct(days, 0.5), 1), Math.Round(Pct(days, 0.9), 1)));
        }
        return result;
    }

    static double Pct(List<double> sorted, double p)
    {
        var rank = p * (sorted.Count - 1);
        var lo = (int)Math.Floor(rank); var hi = (int)Math.Ceiling(rank);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
    }

    /// <summary>CAPA effectiveness: share of verified CAPAs found effective, overdue effectiveness checks, and repeats after closure.</summary>
    public async Task<object> CapaEffectivenessAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var (start, end) = await m.DayRangeAsync(null, from, to, ct);
        var q = await Scope(db.Capas.AsNoTracking(), Permissions.CapaView, ct);
        var closed = await q.Where(c => c.ClosedAt >= start && c.ClosedAt < end && c.Status == CapaStatus.Closed)
            .Select(c => new { c.Id, c.Number, c.Title, c.WasEffective, c.ClosedAt, c.SiteId }).ToListAsync(ct);
        var today = await m.TodayAsync(null, ct);
        var overdue = await q.Where(c => c.Status == CapaStatus.EffectivenessCheck && c.EffectivenessDueOn < today).CountAsync(ct);
        var ids = closed.Select(c => c.Id).ToList();
        // a nonconformance linked to the CAPA's source records after the CAPA closed counts as a repeat
        var links = await db.RecordLinks.AsNoTracking().Where(l => (l.ToType == CapaService.RecordType && ids.Contains(l.ToId)) || (l.FromType == CapaService.RecordType && ids.Contains(l.FromId)))
            .Select(l => new { Capa = l.ToType == CapaService.RecordType ? l.ToId : l.FromId, Other = l.ToType == CapaService.RecordType ? l.FromId : l.ToId, OtherType = l.ToType == CapaService.RecordType ? l.FromType : l.ToType }).ToListAsync(ct);
        var ncrIds = links.Where(l => l.OtherType == NcrService.RecordType).Select(l => l.Other).Distinct().ToList();
        var ncrInfo = await db.Ncrs.AsNoTracking().Where(n => ncrIds.Contains(n.Id)).Select(n => new { n.Id, n.PartId, n.DefectCode, n.Process }).ToListAsync(ct);
        var repeats = new List<string>();
        foreach (var c in closed)
        {
            var srcs = links.Where(l => l.Capa == c.Id && l.OtherType == NcrService.RecordType).Select(l => ncrInfo.FirstOrDefault(n => n.Id == l.Other)).Where(n => n is not null).ToList();
            foreach (var s in srcs)
                if (await db.Ncrs.AsNoTracking().AnyAsync(n => n.Id != s!.Id && n.SiteId == c.SiteId && n.CreatedAt > c.ClosedAt && n.PartId == s.PartId && n.PartId != null
                        && (n.DefectCode == s.DefectCode || (n.Process != null && n.Process == s.Process)), ct)) { repeats.Add(c.Number); break; }
        }
        var verified = closed.Count(c => c.WasEffective != null);
        var effective = closed.Count(c => c.WasEffective == true);
        return new
        {
            Closed = closed.Count, Verified = verified, Effective = effective, NotEffective = closed.Count(c => c.WasEffective == false),
            EffectiveRate = verified == 0 ? (double?)null : Math.Round(100.0 * effective / verified, 1), OverdueChecks = overdue,
            RepeatsAfterClosure = repeats.Count, RepeatCapas = repeats
        };
    }

    /// <summary>
    /// Recurrence and systemic-issue detection over quality events, nonconformances and complaints: same part (or process) and category
    /// (defect code, failure mode) within the configured window; groups at or above the systemic threshold are systemic.
    /// </summary>
    public async Task<List<RecurrenceGroup>> RecurrenceAsync(CancellationToken ct)
    {
        var s = await events.GetSettingsAsync(ct);
        var since = m.Now.AddDays(-s.RecurrenceWindowDays);
        var items = new List<(string Part, string Category, string Number, DateTimeOffset At)>();
        if (await m.CanAnyAsync(Permissions.QualityOpsView, ct))
            items.AddRange((await (await Scope(db.QualityEvents.AsNoTracking(), Permissions.QualityOpsView, ct)).Where(e => e.CreatedAt >= since && e.Status != QualityEventStatus.Rejected && e.Category != null)
                .Select(e => new { Part = e.PartId != null ? db.Parts.Where(p => p.Id == e.PartId).Select(p => p.PartNumber).First() : e.Process, e.Category, e.Number, e.CreatedAt }).ToListAsync(ct))
                .Where(e => e.Part != null).Select(e => (e.Part!, e.Category!, e.Number, e.CreatedAt)));
        if (await m.CanAnyAsync(Permissions.NcrView, ct))
            items.AddRange((await (await Scope(db.Ncrs.AsNoTracking(), Permissions.NcrView, ct)).Where(n => n.CreatedAt >= since && n.Status != NcrStatus.Cancelled)
                .Select(n => new { Part = n.PartNumber ?? n.Process, Category = n.DefectCode ?? n.Source, n.Number, n.CreatedAt }).ToListAsync(ct))
                .Where(n => n.Part != null).Select(n => (n.Part!, n.Category, n.Number, n.CreatedAt)));
        if (await m.CanAnyAsync(Permissions.ComplaintsView, ct))
            items.AddRange((await (await Scope(db.Complaints.AsNoTracking(), Permissions.ComplaintsView, ct)).Where(c => c.CreatedAt >= since && c.Status != ComplaintStatus.Cancelled && c.FailureMode != null)
                .Select(c => new { c.PartNumber, c.FailureMode, c.Number, c.CreatedAt }).ToListAsync(ct))
                .Where(c => c.PartNumber != null).Select(c => (c.PartNumber!, c.FailureMode!, c.Number, c.CreatedAt)));
        return items.GroupBy(i => (Part: i.Part.Trim().ToUpperInvariant(), Category: i.Category.Trim().ToUpperInvariant()))
            .Where(g => g.Count() >= 2)
            .Select(g => new RecurrenceGroup($"{g.Key.Part}|{g.Key.Category}", g.First().Part, g.First().Category, g.Count(), g.Count() >= s.SystemicThreshold,
                g.OrderBy(i => i.At).Select(i => i.Number).ToList(), g.Min(i => i.At), g.Max(i => i.At)))
            .OrderByDescending(g => g.Count).ThenByDescending(g => g.Last).ToList();
    }

    static string Band(double score) => score >= 90 ? "Ready" : score >= 75 ? "Mostly ready" : score >= 50 ? "At risk" : "Not ready";

    static ScoreFactor Factor(string key, string label, int total, int late, double weight) =>
        new(key, label, total, late, total == 0 ? 100 : Math.Round(100.0 * (total - late) / total, 1), weight);

    static ReadinessScore Score(string kind, List<ScoreFactor> f)
    {
        var w = f.Sum(x => x.Weight);
        // nothing the person can see to measure: say so instead of reporting a perfect score
        if (w == 0) return new(kind, 0, "No data", f);
        var score = Math.Round(f.Sum(x => x.Score * x.Weight) / w, 1);
        return new(kind, score, Band(score), f);
    }

    /// <summary>
    /// Quality readiness (are the controls current?) and audit readiness (would an audit find overdue items?), each a weighted share of
    /// items that are on time. Factors the user cannot view are left out rather than counted.
    /// </summary>
    public async Task<List<ReadinessScore>> ReadinessAsync(CancellationToken ct)
    {
        var today = await m.TodayAsync(null, ct);
        var quality = new List<ScoreFactor>();
        var audit = new List<ScoreFactor>();
        if (await m.CanAnyAsync(Permissions.NcrView, ct))
        {
            var q = (await Scope(db.Ncrs.AsNoTracking(), Permissions.NcrView, ct)).Where(n => n.Status != NcrStatus.Closed && n.Status != NcrStatus.Cancelled);
            quality.Add(Factor("ncr", "Open nonconformances within their due date", await q.CountAsync(ct), await q.CountAsync(n => n.DueOn != null && n.DueOn < today, ct), 2));
        }
        if (await m.CanAnyAsync(Permissions.CapaView, ct))
        {
            var q = (await Scope(db.Capas.AsNoTracking(), Permissions.CapaView, ct)).Where(c => c.Status != CapaStatus.Closed && c.Status != CapaStatus.Cancelled);
            var f = Factor("capa", "Open corrective actions within their due date", await q.CountAsync(ct), await q.CountAsync(c => (c.DueOn != null && c.DueOn < today) || (c.Status == CapaStatus.EffectivenessCheck && c.EffectivenessDueOn < today), ct), 3);
            quality.Add(f); audit.Add(f);
        }
        if (await m.CanAnyAsync(Permissions.CalibrationView, ct))
        {
            var q = (await Scope(db.Equipment.AsNoTracking(), Permissions.CalibrationView, ct)).Where(e => e.Status == EquipmentStatus.Active && e.NextDueOn != null);
            var f = Factor("calibration", "Equipment within its calibration date", await q.CountAsync(ct), await q.CountAsync(e => e.NextDueOn < today, ct), 3);
            quality.Add(f); audit.Add(f);
        }
        if (await m.CanAnyAsync(Permissions.TrainingView, ct))
        {
            var q = (await Scope(db.TrainingAssignments.AsNoTracking(), Permissions.TrainingView, ct)).Where(a => a.Status == AssignmentStatus.Assigned || (a.Status == AssignmentStatus.Completed && a.ExpiresOn != null));
            var f = Factor("training", "Training assignments current", await q.CountAsync(ct), await q.CountAsync(a => (a.Status == AssignmentStatus.Assigned && a.DueOn < today) || (a.Status == AssignmentStatus.Completed && a.ExpiresOn < today), ct), 2);
            quality.Add(f); audit.Add(f);
        }
        if (await m.CanAnyAsync(Permissions.DocumentsView, ct))
        {
            var q = (await Scope(db.Documents.AsNoTracking(), Permissions.DocumentsView, ct)).Where(d => d.Status == DocumentStatus.Active && d.NextReviewDue != null);
            audit.Add(Factor("documents", "Active documents within their review date", await q.CountAsync(ct), await q.CountAsync(d => d.NextReviewDue < today, ct), 2));
        }
        if (await m.CanAnyAsync(Permissions.AuditsView, ct))
        {
            var f = (await Scope(db.AuditFindings.AsNoTracking(), Permissions.AuditsView, ct)).Where(x => x.Status == FindingStatus.Open || x.Status == FindingStatus.ActionPlanned);
            audit.Add(Factor("findings", "Open audit findings within their response date", await f.CountAsync(ct), await f.CountAsync(x => x.ResponseDueOn < today, ct), 3));
            var a = (await Scope(db.Audits.AsNoTracking(), Permissions.AuditsView, ct)).Where(x => x.Status != AuditStatus.Cancelled && x.PlannedEnd < today);
            audit.Add(Factor("audits", "Planned audits completed by their planned end", await a.CountAsync(ct), await a.CountAsync(x => x.Status == AuditStatus.Planned || x.Status == AuditStatus.InProgress, ct), 2));
        }
        if (await m.CanAnyAsync(Permissions.QualityOpsView, ct))
        {
            var d = (await Scope(db.Deviations.AsNoTracking(), Permissions.QualityOpsView, ct)).Where(x => x.Status == QualityDeviationStatus.Approved && x.EffectiveTo != null);
            quality.Add(Factor("deviations", "Approved deviations within their window", await d.CountAsync(ct), await d.CountAsync(x => x.EffectiveTo < today, ct), 1));
            var e = (await Scope(db.QualityEvents.AsNoTracking(), Permissions.QualityOpsView, ct)).Where(x => x.Status == QualityEventStatus.Classified && x.RespondBy != null);
            var now = m.Now;
            quality.Add(Factor("events", "Classified quality events within their response time", await e.CountAsync(ct), await e.CountAsync(x => x.RespondBy < now, ct), 2));
        }
        return [Score("Quality readiness", quality), Score("Audit readiness", audit)];
    }

    /// <summary>
    /// Dynamic supplier risk: a 0-100 score from the latest performance period, open and overdue supplier corrective actions,
    /// nonconformances against the supplier in the last 180 days, expired or expiring certificates and the supplier's status.
    /// </summary>
    public async Task<List<SupplierRisk>> SupplierRiskAsync(CancellationToken ct)
    {
        if (!await m.CanAnyAsync(Permissions.SuppliersView, ct)) throw new ForbiddenException();
        var today = await m.TodayAsync(null, ct);
        var since = m.Now.AddDays(-180);
        // only suppliers (and their corrective actions) at sites where the person may view suppliers
        var suppliers = await (await Scope(db.Suppliers.AsNoTracking(), Permissions.SuppliersView, ct)).Where(s => s.Status != SupplierStatus.Disqualified).Select(s => new { s.Id, s.Code, s.LegalName, s.Status, s.Risk }).ToListAsync(ct);
        var perf = await db.SupplierPerformance.AsNoTracking().GroupBy(p => p.SupplierId).Select(g => g.OrderByDescending(p => p.PeriodEnd).First()).ToListAsync(ct);
        var scars = await (await Scope(db.SupplierCorrectiveActions.AsNoTracking(), Permissions.SuppliersView, ct)).Where(x => x.Status != ScarStatus.Closed && x.Status != ScarStatus.Cancelled).Select(x => new { x.SupplierId, Late = x.ResponseDueOn < today }).ToListAsync(ct);
        var ncrs = await (await Scope(db.Ncrs.AsNoTracking(), Permissions.NcrView, ct)).Where(n => n.SupplierId != null && n.CreatedAt >= since).GroupBy(n => n.SupplierId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var certs = await db.SupplierCertifications.AsNoTracking().Select(c => new { c.SupplierId, c.ExpiresOn }).ToListAsync(ct);
        var result = new List<SupplierRisk>();
        foreach (var s in suppliers)
        {
            var signals = new List<string>();
            double score = s.Risk switch { Criticality.High => 20, Criticality.Critical => 30, Criticality.Medium => 10, _ => 0 };
            if (s.Risk >= Criticality.High) signals.Add($"Assessed risk {s.Risk}");
            if (perf.FirstOrDefault(p => p.SupplierId == s.Id) is { } p)
            {
                if (p.OverallScore < 70) { score += 25; signals.Add($"Performance {p.OverallScore:0.#} in the last period"); }
                else if (p.OverallScore < 85) { score += 10; signals.Add($"Performance {p.OverallScore:0.#} in the last period"); }
            }
            var open = scars.Where(x => x.SupplierId == s.Id).ToList();
            if (open.Count > 0) { score += Math.Min(20, open.Count * 5) + open.Count(x => x.Late) * 5; signals.Add($"{open.Count} open corrective action request(s), {open.Count(x => x.Late)} overdue"); }
            var n = ncrs.FirstOrDefault(x => x.Key == s.Id)?.Count ?? 0;
            if (n > 0) { score += Math.Min(25, n * 5); signals.Add($"{n} nonconformance(s) in 180 days"); }
            var c = certs.Where(x => x.SupplierId == s.Id).ToList();
            if (c.Any(x => x.ExpiresOn < today)) { score += 15; signals.Add("Expired certificate"); }
            else if (c.Any(x => x.ExpiresOn <= today.AddDays(60))) { score += 5; signals.Add("Certificate expires within 60 days"); }
            if (s.Status is SupplierStatus.Conditional or SupplierStatus.Suspended) { score += 15; signals.Add($"Status {s.Status}"); }
            score = Math.Min(100, score);
            result.Add(new(s.Id, s.Code, s.LegalName, s.Status.ToString(), score, score >= 60 ? "High" : score >= 30 ? "Elevated" : "Low", signals));
        }
        return result.OrderByDescending(r => r.Score).ThenBy(r => r.Code).ToList();
    }
}
