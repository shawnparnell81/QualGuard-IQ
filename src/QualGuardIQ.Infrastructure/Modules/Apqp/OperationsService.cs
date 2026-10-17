using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.MasterData;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Apqp;

public sealed record LpaTemplateInput(string Name, int Layer, string? Area, List<string> Questions, LpaAutoAction OnFailure, Guid? EscalateToId, Guid SiteId, Guid? DepartmentId);
public sealed record LpaScheduleInput(Guid TemplateId, Guid AssigneeId, LpaFrequency Frequency, DateOnly FirstDueOn);
public sealed record LpaAnswer(string Question, bool Pass, string? Note);
public sealed record LpaCompleteInput(List<LpaAnswer> Answers);
public sealed record CoqInput(DateOnly IncurredOn, CoqCategory Category, string Element, decimal Amount, string? Currency, string? Description,
    Guid? ProductId, Guid? PartId, Guid? SupplierId, Guid? CustomerId, string? SourceType, Guid? SourceId, Guid SiteId, Guid? DepartmentId);
public sealed record ScrapInput(DateOnly ScrappedOn, Guid PartId, decimal Quantity, decimal UnitCost, string Reason, Guid? NcrId, Guid? InspectionId, Guid? SupplierId, Guid? CustomerId,
    string? LotNumber, string? Material, Guid SiteId, Guid? DepartmentId);
public sealed record ReworkInput(Guid NcrId, string Instructions, decimal Quantity, Guid? CapaId, Guid? InspectionId, decimal LaborRate);
public sealed record ReworkCompleteInput(decimal LaborHours, decimal MaterialCost, string? Notes);
public sealed record ReworkValidateInput(bool Pass, string Notes);
public sealed record SupplierProgramInput(Guid SupplierId, SupplierProgramKind Kind, string Title, string? Objective, decimal? BaselineScore, decimal? TargetScore, DateOnly PlannedCompletion, Guid? OwnerId);
public sealed record SupplierProgramProgress(decimal? CurrentScore, SupplierProgramStatus Status, string? Outcome);
public sealed record ScoreWeightsInput(decimal Quality, decimal Delivery, decimal Responsiveness, decimal Cost, decimal CorrectiveAction, decimal AuditResults, decimal Certifications, decimal Risk);
public sealed record SupplierScoreRow(Guid SupplierId, string Code, string Name, decimal Quality, decimal Delivery, decimal Responsiveness, decimal Cost, decimal CorrectiveAction,
    decimal AuditResults, decimal Certifications, decimal Risk, decimal Overall, int Rank);
public sealed record CoqGroup(string Key, string Label, decimal Prevention, decimal Appraisal, decimal InternalFailure, decimal ExternalFailure, decimal Total);

public static class CoqElements
{
    public static readonly Dictionary<CoqCategory, string[]> ByCategory = new()
    {
        [CoqCategory.Prevention] = ["Training", "Audits", "Supplier development", "Calibration", "Validation"],
        [CoqCategory.Appraisal] = ["Inspection", "Testing", "FAI", "AQL", "Audits"],
        [CoqCategory.InternalFailure] = ["Scrap", "Rework", "Downtime", "Retesting", "Material loss"],
        [CoqCategory.ExternalFailure] = ["Warranty", "Returns", "Customer complaints", "Field failures", "Recalls"],
    };
}

/// <summary>Layered process audits.</summary>
public sealed class LpaService(AppDbContext db, ModuleSupport m, INumberingService numbers, INotifier notifier, NcrService ncrs, CapaService capas)
{
    public const string RecordType = "LpaAudit";

    public async Task<LpaTemplate> SaveTemplateAsync(Guid? id, LpaTemplateInput r, CancellationToken ct)
    {
        ModuleSupport.Required(r.Name, "Name", 200);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        await m.RequireAsync(Permissions.LpaEdit, r.SiteId, r.DepartmentId, ct);
        var qs = r.Questions.Where(q => !string.IsNullOrWhiteSpace(q)).Select(q => q.Trim()).ToList();
        if (qs.Count == 0) throw new AppException("Add at least one question.");
        if (r.Layer is < 1 or > 5) throw new AppException("Layer is 1 (supervisor) to 5 (executive).");
        if (r.EscalateToId is { } e) await m.RequireActiveUserAsync(e, "escalation contact", ct);
        var t = id is { } x ? await db.LpaTemplates.FirstOrDefaultAsync(t => t.Id == x, ct) ?? throw new NotFoundException("LPA template") : new LpaTemplate();
        if (id is not null) await m.RequireAsync(Permissions.LpaEdit, t.SiteId, t.DepartmentId, ct);
        t.Name = r.Name.Trim(); t.Layer = r.Layer; t.Area = r.Area; t.Questions = JsonSerializer.Serialize(qs); t.OnFailure = r.OnFailure; t.EscalateToId = r.EscalateToId; t.SiteId = r.SiteId; t.DepartmentId = r.DepartmentId;
        if (id is null) db.LpaTemplates.Add(t);
        await db.SaveChangesAsync(ct);
        return t;
    }

    public async Task<List<LpaTemplate>> TemplatesAsync(CancellationToken ct) =>
        await (await m.ScopeAsync(db.LpaTemplates.AsNoTracking(), Permissions.LpaView, ct)).Where(x => !x.IsArchived).OrderBy(x => x.Layer).ThenBy(x => x.Name).ToListAsync(ct);

    public async Task<LpaSchedule> ScheduleAsync(LpaScheduleInput r, CancellationToken ct)
    {
        var t = await db.LpaTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.TemplateId && !x.IsArchived, ct) ?? throw new AppException("Select an active LPA template.");
        await m.RequireAsync(Permissions.LpaEdit, t.SiteId, t.DepartmentId, ct);
        await m.RequireActiveUserAsync(r.AssigneeId, "auditor", ct);
        if (r.FirstDueOn < await m.TodayAsync(t.SiteId, ct)) throw new AppException("The first audit cannot be due in the past.");
        var s = new LpaSchedule { TemplateId = t.Id, AssigneeId = r.AssigneeId, Frequency = r.Frequency, NextDueOn = r.FirstDueOn, SiteId = t.SiteId, DepartmentId = t.DepartmentId };
        db.LpaSchedules.Add(s);
        await db.SaveChangesAsync(ct);
        await GenerateAsync(ct);
        return s;
    }

    public async Task<List<LpaSchedule>> SchedulesAsync(CancellationToken ct) =>
        await (await m.ScopeAsync(db.LpaSchedules.AsNoTracking(), Permissions.LpaView, ct)).Where(x => x.Active).OrderBy(x => x.NextDueOn).ToListAsync(ct);

    static DateOnly Next(DateOnly d, LpaFrequency f) => f switch { LpaFrequency.Daily => d.AddDays(1), LpaFrequency.Weekly => d.AddDays(7), _ => d.AddMonths(1) };

    /// <summary>Creates audit assignments for every active schedule due today or earlier, marks missed audits, and advances the schedule.</summary>
    public async Task<int> GenerateAsync(CancellationToken ct)
    {
        var created = 0;
        var schedules = await db.LpaSchedules.Where(x => x.Active).ToListAsync(ct);
        foreach (var s in schedules)
        {
            var today = await m.TodayAsync(s.SiteId, ct);
            while (s.NextDueOn <= today)
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var a = new LpaAudit { Number = await numbers.NextAsync(RecordType, s.SiteId, ct), TemplateId = s.TemplateId, ScheduleId = s.Id, AssigneeId = s.AssigneeId, DueOn = s.NextDueOn, Status = LpaStatus.Scheduled, SiteId = s.SiteId, DepartmentId = s.DepartmentId };
                db.LpaAudits.Add(a);
                s.NextDueOn = Next(s.NextDueOn, s.Frequency);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                created++;
                await notifier.NotifyAsync([s.AssigneeId], $"LPA {a.Number} due {a.DueOn:yyyy-MM-dd}", "Layered process audit assigned to you.", await m.RecordAsync(RecordType, a.Id, ct), ct: ct);
            }
        }
        var overdue = await db.LpaAudits.Where(a => a.Status == LpaStatus.Scheduled).ToListAsync(ct);
        foreach (var a in overdue) if (a.DueOn < await m.TodayAsync(a.SiteId, ct)) a.Status = LpaStatus.Missed;
        await db.SaveChangesAsync(ct);
        return created;
    }

    public async Task<List<LpaAudit>> AuditsAsync(LpaStatus? status, CancellationToken ct)
    {
        var q = await m.ScopeAsync(db.LpaAudits.AsNoTracking(), Permissions.LpaView, ct);
        if (status is { } s) q = q.Where(x => x.Status == s);
        return await q.OrderByDescending(x => x.DueOn).Take(500).ToListAsync(ct);
    }

    public async Task<(LpaAudit Audit, LpaTemplate Template)> GetAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var a = await db.LpaAudits.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        return (a, await db.LpaTemplates.AsNoTracking().FirstAsync(x => x.Id == a.TemplateId, ct));
    }

    public async Task<LpaAudit> CompleteAsync(Guid id, LpaCompleteInput r, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var a = await db.LpaAudits.FirstAsync(x => x.Id == id, ct);
        await m.RequireAsync(Permissions.LpaEdit, a.SiteId, a.DepartmentId, ct);
        if (a.Status == LpaStatus.Completed) throw new ConflictException("The audit is already completed.");
        var t = await db.LpaTemplates.AsNoTracking().FirstAsync(x => x.Id == a.TemplateId, ct);
        var qs = JsonSerializer.Deserialize<List<string>>(t.Questions) ?? [];
        if (r.Answers.Count != qs.Count || r.Answers.Select(x => x.Question).Except(qs).Any()) throw new AppException("Answer every question on the template.");
        if (r.Answers.Any(x => !x.Pass && string.IsNullOrWhiteSpace(x.Note))) throw new AppException("Describe each failed check.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        a.Answers = JsonSerializer.Serialize(r.Answers); a.Failures = r.Answers.Count(x => !x.Pass); a.Status = LpaStatus.Completed; a.CompletedAt = m.Now;
        await db.SaveChangesAsync(ct);
        if (a.Failures > 0)
        {
            var fails = string.Join("; ", r.Answers.Where(x => !x.Pass).Select(x => $"{x.Question}: {x.Note}"));
            if (t.OnFailure == LpaAutoAction.Ncr)
            {
                var n = await ncrs.CreateAsync(new CreateNcrRequest($"LPA {a.Number} failure: {t.Name}", fails, "Audit", a.SiteId, a.DepartmentId, t.EscalateToId, null, null, null, await m.TodayAsync(a.SiteId, ct), Severity.Minor,
                    new NcrDetails(Process: t.Area, DetectionPoint: "Layered process audit")), ct);
                a.NcrId = n.Id;
                db.RecordLinks.Add(new RecordLink { FromType = RecordType, FromId = a.Id, ToType = NcrService.RecordType, ToId = n.Id, LinkType = "Finding NCR" });
            }
            else if (t.OnFailure == LpaAutoAction.Capa)
            {
                var c = await capas.CreateAsync(new CreateCapaRequest($"LPA {a.Number} failure: {t.Name}", fails, "Corrective", "Audit", a.SiteId, a.DepartmentId, t.EscalateToId), ct, (RecordType, a.Id));
                a.CapaId = c.Id;
            }
            await db.SaveChangesAsync(ct);
            var to = new List<Guid>(); if (t.EscalateToId is { } e) to.Add(e);
            if (to.Count > 0) await notifier.NotifyAsync(to, $"LPA {a.Number}: {a.Failures} failed check(s)", fails, await m.RecordAsync(RecordType, a.Id, ct), ct: ct);
        }
        await tx.CommitAsync(ct);
        return a;
    }
}

/// <summary>Cost of quality, scrap and rework.</summary>
public sealed class CoqService(AppDbContext db, ModuleSupport m, INumberingService numbers, WorkflowService workflow, INotifier notifier)
{
    public const string ScrapType = "ScrapEvent", ReworkType = "ReworkOrder";

    public async Task<CoqEntry> AddEntryAsync(CoqInput r, CancellationToken ct, bool system = false)
    {
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        if (!system) await m.RequireAsync(Permissions.CoqEdit, r.SiteId, r.DepartmentId, ct);
        if (!CoqElements.ByCategory[r.Category].Contains(r.Element)) throw new AppException($"{r.Category} elements are: {string.Join(", ", CoqElements.ByCategory[r.Category])}.");
        if (r.Amount < 0) throw new AppException("Amount cannot be negative.");
        if (r.IncurredOn > await m.TodayAsync(r.SiteId, ct)) throw new AppException("The cost date cannot be in the future.");
        var e = new CoqEntry
        {
            IncurredOn = r.IncurredOn, Category = r.Category, Element = r.Element, Amount = Math.Round(r.Amount, 2), Currency = string.IsNullOrWhiteSpace(r.Currency) ? "USD" : r.Currency.Trim().ToUpperInvariant(),
            Description = r.Description, ProductId = r.ProductId, PartId = r.PartId, SupplierId = r.SupplierId, CustomerId = r.CustomerId, SourceType = r.SourceType, SourceId = r.SourceId, SiteId = r.SiteId, DepartmentId = r.DepartmentId
        };
        if (r.SourceType is { } st && r.SourceId is { } sid && !system) await m.ViewAsync(st, sid, ct);
        db.CoqEntries.Add(e);
        await db.SaveChangesAsync(ct);
        return e;
    }

    public async Task<List<CoqEntry>> EntriesAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var q = await m.ScopeAsync(db.CoqEntries.AsNoTracking(), Permissions.CoqView, ct);
        if (from is { } f) q = q.Where(x => x.IncurredOn >= f);
        if (to is { } t) q = q.Where(x => x.IncurredOn <= t);
        return await q.OrderByDescending(x => x.IncurredOn).Take(2000).ToListAsync(ct);
    }

    /// <summary>COQ grouped by month, product, supplier, customer or category.</summary>
    public async Task<List<CoqGroup>> SummaryAsync(string by, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var rows = await EntriesAsync(from, to, ct);
        Func<CoqEntry, Guid?> key = by switch
        {
            "product" => x => x.ProductId, "supplier" => x => x.SupplierId, "customer" => x => x.CustomerId, "month" => _ => null, "category" => _ => null,
            _ => throw new AppException("Group by month, product, supplier, customer or category.")
        };
        var names = by switch
        {
            "product" => await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct),
            "supplier" => await db.Suppliers.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.LegalName, ct),
            "customer" => await db.Customers.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct),
            _ => new Dictionary<Guid, string>()
        };
        IEnumerable<IGrouping<string, CoqEntry>> groups = by switch
        {
            "month" => rows.GroupBy(x => x.IncurredOn.ToString("yyyy-MM")),
            "category" => rows.GroupBy(x => x.Category.ToString()),
            _ => rows.GroupBy(x => key(x)?.ToString() ?? "")
        };
        decimal S(IEnumerable<CoqEntry> g, CoqCategory c) => g.Where(x => x.Category == c).Sum(x => x.Amount);
        return groups.Select(g => new CoqGroup(g.Key,
                by is "month" or "category" ? g.Key : g.Key == "" ? "(not attributed)" : names.GetValueOrDefault(Guid.Parse(g.Key), g.Key),
                S(g, CoqCategory.Prevention), S(g, CoqCategory.Appraisal), S(g, CoqCategory.InternalFailure), S(g, CoqCategory.ExternalFailure), g.Sum(x => x.Amount)))
            .OrderBy(x => by == "month" ? x.Key : "").ThenByDescending(x => x.Total).ToList();
    }

    // ---------- scrap ----------
    public async Task<ScrapEvent> ScrapAsync(ScrapInput r, CancellationToken ct)
    {
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        await m.RequireAsync(Permissions.CoqEdit, r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.Reason, "Scrap reason", 1000);
        if (r.Quantity <= 0) throw new AppException("Quantity must be greater than zero.");
        if (r.UnitCost < 0) throw new AppException("Unit cost cannot be negative.");
        if (r.ScrappedOn > await m.TodayAsync(r.SiteId, ct)) throw new AppException("The scrap date cannot be in the future.");
        var part = await db.Parts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.PartId, ct) ?? throw new AppException("Select an existing part.");
        if (r.NcrId is { } n)
        {
            await m.ViewAsync(NcrService.RecordType, n, ct);
        }
        if (r.InspectionId is { } i) await m.ViewAsync("Inspection", i, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var s = new ScrapEvent
        {
            Number = await numbers.NextAsync(ScrapType, r.SiteId, ct), ScrappedOn = r.ScrappedOn, PartId = part.Id, ProductId = part.ProductId, Quantity = r.Quantity, UnitCost = r.UnitCost,
            TotalCost = Math.Round(r.Quantity * r.UnitCost, 2, MidpointRounding.AwayFromZero), Reason = r.Reason.Trim(), NcrId = r.NcrId, InspectionId = r.InspectionId, SupplierId = r.SupplierId, CustomerId = r.CustomerId,
            LotNumber = r.LotNumber, Material = r.Material ?? part.Material, SiteId = r.SiteId, DepartmentId = r.DepartmentId
        };
        db.ScrapEvents.Add(s);
        if (r.NcrId is { } nid) db.RecordLinks.Add(new RecordLink { FromType = NcrService.RecordType, FromId = nid, ToType = ScrapType, ToId = s.Id, LinkType = "Scrap" });
        await db.SaveChangesAsync(ct);
        await AddEntryAsync(new CoqInput(s.ScrappedOn, CoqCategory.InternalFailure, "Scrap", s.TotalCost, null, $"Scrap {s.Number}: {s.Reason}", s.ProductId, s.PartId, s.SupplierId, s.CustomerId, ScrapType, s.Id, s.SiteId, s.DepartmentId), ct, system: true);
        await m.IndexAsync(ScrapType, s.Id, $"{part.PartNumber} {s.Reason} {s.LotNumber}", ct);
        await tx.CommitAsync(ct);
        return s;
    }

    public async Task<List<ScrapEvent>> ScrapListAsync(CancellationToken ct) =>
        await (await m.ScopeAsync(db.ScrapEvents.AsNoTracking(), Permissions.CoqView, ct)).OrderByDescending(x => x.ScrappedOn).Take(1000).ToListAsync(ct);

    public async Task<object> ScrapTrendAsync(int months, CancellationToken ct)
    {
        var since = DateOnly.FromDateTime(m.Now.UtcDateTime).AddMonths(-Math.Clamp(months, 1, 36));
        var rows = (await ScrapListAsync(ct)).Where(x => x.ScrappedOn >= since).ToList();
        return new
        {
            ByMonth = rows.GroupBy(x => x.ScrappedOn.ToString("yyyy-MM")).OrderBy(g => g.Key).Select(g => new { Month = g.Key, Events = g.Count(), Quantity = g.Sum(x => x.Quantity), Cost = g.Sum(x => x.TotalCost) }),
            ByReason = rows.GroupBy(x => x.Reason).Select(g => new { Reason = g.Key, Events = g.Count(), Cost = g.Sum(x => x.TotalCost) }).OrderByDescending(x => x.Cost).Take(10)
        };
    }

    // ---------- rework ----------
    async Task<ReworkOrder> LoadReworkAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(ReworkType, id, ct);
        var w = await db.ReworkOrders.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Rework order");
        await m.RequireAsync(Permissions.CoqEdit, w.SiteId, w.DepartmentId, ct);
        return w;
    }

    public async Task<ReworkOrder> CreateReworkAsync(ReworkInput r, CancellationToken ct)
    {
        await m.ViewAsync(NcrService.RecordType, r.NcrId, ct);
        var ncr = await db.Ncrs.AsNoTracking().FirstAsync(x => x.Id == r.NcrId, ct);
        await m.RequireAsync(Permissions.CoqEdit, ncr.SiteId, ncr.DepartmentId, ct);
        ModuleSupport.Required(r.Instructions, "Rework instructions", 4000);
        if (r.Quantity <= 0) throw new AppException("Quantity must be greater than zero.");
        if (r.LaborRate < 0) throw new AppException("Labor rate cannot be negative.");
        if (r.CapaId is { } c) await m.ViewAsync("Capa", c, ct);
        if (r.InspectionId is { } i) await m.ViewAsync("Inspection", i, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var w = new ReworkOrder { Number = await numbers.NextAsync(ReworkType, ncr.SiteId, ct), NcrId = ncr.Id, CapaId = r.CapaId, InspectionId = r.InspectionId, Instructions = r.Instructions.Trim(), Quantity = r.Quantity, LaborRate = r.LaborRate, Status = ReworkStatus.Draft, OwnerId = m.UserId, SiteId = ncr.SiteId, DepartmentId = ncr.DepartmentId };
        db.ReworkOrders.Add(w);
        db.RecordLinks.Add(new RecordLink { FromType = NcrService.RecordType, FromId = ncr.Id, ToType = ReworkType, ToId = w.Id, LinkType = "Rework" });
        if (r.CapaId is { } cid) db.RecordLinks.Add(new RecordLink { FromType = "Capa", FromId = cid, ToType = ReworkType, ToId = w.Id, LinkType = "Rework" });
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(ReworkType, w.Id, w.Instructions, ct);
        await tx.CommitAsync(ct);
        return w;
    }

    public async Task SubmitReworkAsync(Guid id, CancellationToken ct)
    {
        var w = await LoadReworkAsync(id, ct);
        if (w.Status is not (ReworkStatus.Draft or ReworkStatus.Rejected)) throw new ConflictException("Only a draft rework order can be submitted.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        w.Status = ReworkStatus.PendingApproval;
        await db.SaveChangesAsync(ct);
        await workflow.SubmitAsync(ReworkType, id, ct);
        await tx.CommitAsync(ct);
    }

    public async Task OnReworkDecidedAsync(Guid id, bool approved, string? comment, CancellationToken ct)
    {
        var w = await db.ReworkOrders.FirstAsync(x => x.Id == id, ct);
        if (w.Status != ReworkStatus.PendingApproval) return;
        w.Status = approved ? ReworkStatus.Approved : ReworkStatus.Rejected;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([w.OwnerId], $"Rework {w.Number} {(approved ? "approved" : "rejected")}", comment ?? w.Instructions, await m.RecordAsync(ReworkType, id, ct), ct: ct);
    }

    public async Task CompleteReworkAsync(Guid id, ReworkCompleteInput r, CancellationToken ct)
    {
        var w = await LoadReworkAsync(id, ct);
        if (w.Status != ReworkStatus.Approved) throw new ConflictException("Rework can be completed only after approval.");
        if (r.LaborHours < 0 || r.MaterialCost < 0) throw new AppException("Hours and material cost cannot be negative.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        w.LaborHours = r.LaborHours; w.MaterialCost = Math.Round(r.MaterialCost, 2); w.TotalCost = Math.Round(r.LaborHours * w.LaborRate + r.MaterialCost, 2, MidpointRounding.AwayFromZero);
        w.Status = ReworkStatus.Completed; w.ValidationNotes = r.Notes;
        await db.SaveChangesAsync(ct);
        var ncr = await db.Ncrs.AsNoTracking().FirstAsync(x => x.Id == w.NcrId, ct);
        await AddEntryAsync(new CoqInput(await m.TodayAsync(w.SiteId, ct), CoqCategory.InternalFailure, "Rework", w.TotalCost, null, $"Rework {w.Number}", ncr.ProductId, ncr.PartId, ncr.SupplierId, ncr.CustomerId, ReworkType, w.Id, w.SiteId, w.DepartmentId), ct, system: true);
        await tx.CommitAsync(ct);
    }

    public async Task ValidateReworkAsync(Guid id, ReworkValidateInput r, CancellationToken ct)
    {
        var w = await LoadReworkAsync(id, ct);
        if (w.Status != ReworkStatus.Completed) throw new ConflictException("Only completed rework can be validated.");
        if (w.OwnerId == m.UserId) throw new ForbiddenException("Rework must be validated by someone other than its owner.");
        ModuleSupport.Required(r.Notes, "Validation notes", 2000);
        w.Status = r.Pass ? ReworkStatus.Validated : ReworkStatus.Failed; w.ValidationNotes = r.Notes.Trim(); w.ValidatedById = m.UserId; w.ValidatedAt = m.Now;
        await db.SaveChangesAsync(ct);
        if (!r.Pass) await notifier.NotifyAsync([w.OwnerId], $"Rework {w.Number} failed validation", r.Notes, await m.RecordAsync(ReworkType, id, ct), ct: ct);
    }

    public async Task<List<ReworkOrder>> ReworkListAsync(Guid? ncrId, CancellationToken ct)
    {
        var q = await m.ScopeAsync(db.ReworkOrders.AsNoTracking(), Permissions.CoqView, ct);
        if (ncrId is { } n) q = q.Where(x => x.NcrId == n);
        return await q.OrderByDescending(x => x.CreatedAt).Take(1000).ToListAsync(ct);
    }

    public async Task<ReworkOrder> GetReworkAsync(Guid id, CancellationToken ct) { await m.ViewAsync(ReworkType, id, ct); return await db.ReworkOrders.AsNoTracking().FirstAsync(x => x.Id == id, ct); }
}

/// <summary>
/// Supplier development programs and the expanded weighted scorecard. Each dimension is 0–100 over the period:
///   Quality = accepted / received lots (supplier performance records plus completed AQL lots);
///   Delivery = on-time / deliveries; Responsiveness = SCARs answered by due date / SCARs due;
///   Cost = 100 × (1 − supplier failure COQ / highest supplier failure COQ in the ranking);
///   Corrective action = SCARs verified effective / SCARs closed with verification;
///   Audit results = 100 − 20 per major and 5 per minor finding on audits linked to the supplier (floor 0);
///   Certifications = certifications valid at period end / certifications on file (100 when none are required);
///   Risk = 100 / 75 / 50 / 25 for Low / Medium / High / Critical supplier risk.
/// Each dimension defaults to 100 when there is no data. Overall = Σ weight × score / Σ weights; rank 1 is best.
/// </summary>
public sealed class SupplierDevelopmentService(AppDbContext db, ModuleSupport m, INumberingService numbers, ActionService actions, IAccessService access)
{
    public const string RecordType = "SupplierProgram";

    public async Task<SupplierProgram> CreateAsync(SupplierProgramInput r, CancellationToken ct)
    {
        await m.ViewAsync(SupplierService.RecordType, r.SupplierId, ct);
        var s = await db.Suppliers.AsNoTracking().FirstAsync(x => x.Id == r.SupplierId, ct);
        await m.RequireAsync(Permissions.SuppliersEdit, s.SiteId, s.DepartmentId, ct);
        ModuleSupport.Required(r.Title, "Title", 300);
        if (r.PlannedCompletion < await m.TodayAsync(s.SiteId, ct)) throw new AppException("Planned completion cannot be in the past.");
        foreach (var v in new[] { r.BaselineScore, r.TargetScore }) if (v is < 0 or > 100) throw new AppException("Scores are 0 to 100.");
        if (r.OwnerId is { } o) await m.RequireActiveUserAsync(o, "owner", ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var p = new SupplierProgram
        {
            Number = await numbers.NextAsync(RecordType, s.SiteId, ct), SupplierId = s.Id, Kind = r.Kind, Title = r.Title.Trim(), Objective = r.Objective, BaselineScore = r.BaselineScore, CurrentScore = r.BaselineScore,
            TargetScore = r.TargetScore, PlannedCompletion = r.PlannedCompletion, Status = SupplierProgramStatus.Planned, OwnerId = r.OwnerId ?? m.UserId, SiteId = s.SiteId, DepartmentId = s.DepartmentId
        };
        db.SupplierPrograms.Add(p);
        db.RecordLinks.Add(new RecordLink { FromType = SupplierService.RecordType, FromId = s.Id, ToType = RecordType, ToId = p.Id, LinkType = "Development program" });
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, p.Id, $"{s.LegalName} {p.Title}", ct);
        await tx.CommitAsync(ct);
        return p;
    }

    public async Task<SupplierProgram> ProgressAsync(Guid id, SupplierProgramProgress r, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var p = await db.SupplierPrograms.FirstAsync(x => x.Id == id, ct);
        await m.RequireAsync(Permissions.SuppliersEdit, p.SiteId, p.DepartmentId, ct);
        if (p.Status is SupplierProgramStatus.Completed or SupplierProgramStatus.Cancelled) throw new ConflictException("The program is closed.");
        if (r.CurrentScore is < 0 or > 100) throw new AppException("Scores are 0 to 100.");
        if (r.Status == SupplierProgramStatus.Completed)
        {
            ModuleSupport.Required(r.Outcome, "Outcome", 2000);
            if (await db.ActionItems.AnyAsync(a => a.SourceType == RecordType && a.SourceId == id && (a.Status == ActionStatus.Open || a.Status == ActionStatus.InProgress || (a.Status == ActionStatus.Completed && a.RequiresVerification)), ct))
                throw new ConflictException("Close the program's action items first.");
            p.ActualCompletion = await m.TodayAsync(p.SiteId, ct);
        }
        p.CurrentScore = r.CurrentScore ?? p.CurrentScore; p.Status = r.Status; p.Outcome = r.Outcome ?? p.Outcome;
        await db.SaveChangesAsync(ct);
        return p;
    }

    public async Task<ActionItem> AddActionAsync(Guid id, CreateActionRequest r, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var p = await db.SupplierPrograms.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        await m.RequireAsync(Permissions.SuppliersEdit, p.SiteId, p.DepartmentId, ct);
        return await actions.CreateAsync(RecordType, id, r, ct);
    }

    public async Task<List<SupplierProgram>> ListAsync(Guid? supplierId, CancellationToken ct)
    {
        var q = await m.ScopeAsync(db.SupplierPrograms.AsNoTracking(), Permissions.SuppliersView, ct);
        if (supplierId is { } s) q = q.Where(x => x.SupplierId == s);
        return await q.OrderBy(x => x.PlannedCompletion).Take(500).ToListAsync(ct);
    }

    public async Task<SupplierProgram> GetAsync(Guid id, CancellationToken ct) { await m.ViewAsync(RecordType, id, ct); return await db.SupplierPrograms.AsNoTracking().FirstAsync(x => x.Id == id, ct); }

    public async Task<SupplierScoreWeights> WeightsAsync(CancellationToken ct) =>
        await db.SupplierScoreWeights.AsNoTracking().FirstOrDefaultAsync(ct) ?? new SupplierScoreWeights();

    public async Task SaveWeightsAsync(ScoreWeightsInput r, CancellationToken ct)
    {
        // Weights are organization-wide, so only someone who can approve suppliers at every site may change them.
        if (!(await access.GetScopeAsync(m.UserId, Permissions.SuppliersApprove, ct)).AllSites) throw new ForbiddenException("Changing scorecard weights needs supplier approval rights for all sites.");
        var vals = new[] { r.Quality, r.Delivery, r.Responsiveness, r.Cost, r.CorrectiveAction, r.AuditResults, r.Certifications, r.Risk };
        if (vals.Any(v => v < 0)) throw new AppException("Weights cannot be negative.");
        if (vals.Sum() != 100) throw new AppException($"Weights must add up to 100 (they add up to {vals.Sum()}).");
        var w = await db.SupplierScoreWeights.FirstOrDefaultAsync(ct);
        if (w is null) { w = new SupplierScoreWeights(); db.SupplierScoreWeights.Add(w); }
        (w.Quality, w.Delivery, w.Responsiveness, w.Cost, w.CorrectiveAction, w.AuditResults, w.Certifications, w.Risk) = (r.Quality, r.Delivery, r.Responsiveness, r.Cost, r.CorrectiveAction, r.AuditResults, r.Certifications, r.Risk);
        await db.SaveChangesAsync(ct);
    }

    static decimal Pct(decimal good, decimal total) => total <= 0 ? 100m : Math.Round(100m * good / total, 1, MidpointRounding.AwayFromZero);

    public async Task<List<SupplierScoreRow>> ScorecardAsync(DateOnly? from, DateOnly? to, Guid? onlySupplier, CancellationToken ct, bool unscoped = false)
    {
        var end = to ?? DateOnly.FromDateTime(m.Now.UtcDateTime);
        var start = from ?? end.AddMonths(-12);
        var sq = unscoped ? db.Suppliers.AsNoTracking() : await m.ScopeAsync(db.Suppliers.AsNoTracking(), Permissions.SuppliersView, ct);
        var suppliers = await sq.Where(s => s.Status != SupplierStatus.Disqualified || s.Id == onlySupplier).ToListAsync(ct);
        var ids = suppliers.Select(s => s.Id).ToList();
        var perf = await db.SupplierPerformance.AsNoTracking().Where(p => ids.Contains(p.SupplierId) && p.PeriodEnd >= start && p.PeriodStart <= end).ToListAsync(ct);
        var startTs = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero); var endTs = new DateTimeOffset(end.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var aql = await db.AqlInspections.AsNoTracking().Where(a => a.SupplierId != null && ids.Contains(a.SupplierId.Value) && a.OverallResult != null && a.CreatedAt >= startTs && a.CreatedAt <= endTs)
            .Select(a => new { a.SupplierId, a.OverallResult }).ToListAsync(ct);
        var scars = await db.SupplierCorrectiveActions.AsNoTracking().Where(c => ids.Contains(c.SupplierId) && c.ResponseDueOn >= start && c.ResponseDueOn <= end).ToListAsync(ct);
        var coq = await db.CoqEntries.AsNoTracking().Where(c => c.SupplierId != null && ids.Contains(c.SupplierId.Value) && c.IncurredOn >= start && c.IncurredOn <= end && (c.Category == CoqCategory.InternalFailure || c.Category == CoqCategory.ExternalFailure))
            .GroupBy(c => c.SupplierId!.Value).Select(g => new { Id = g.Key, Sum = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Id, x => x.Sum, ct);
        var maxCoq = coq.Count == 0 ? 0 : coq.Values.Max();
        var certs = await db.SupplierCertifications.AsNoTracking().Where(c => ids.Contains(c.SupplierId)).ToListAsync(ct);
        var auditLinks = await db.RecordLinks.AsNoTracking().Where(l => (l.FromType == SupplierService.RecordType && ids.Contains(l.FromId) && l.ToType == "Audit") || (l.ToType == SupplierService.RecordType && ids.Contains(l.ToId) && l.FromType == "Audit"))
            .Select(l => new { Supplier = l.FromType == "Audit" ? l.ToId : l.FromId, Audit = l.FromType == "Audit" ? l.FromId : l.ToId }).ToListAsync(ct);
        var auditIds = auditLinks.Select(a => a.Audit).Distinct().ToList();
        var findings = await db.AuditFindings.AsNoTracking().Where(f => auditIds.Contains(f.AuditId)).Select(f => new { f.AuditId, f.FindingType }).ToListAsync(ct);
        var w = await WeightsAsync(ct);
        var wsum = w.Quality + w.Delivery + w.Responsiveness + w.Cost + w.CorrectiveAction + w.AuditResults + w.Certifications + w.Risk;
        var rows = suppliers.Select(s =>
        {
            var p = perf.Where(x => x.SupplierId == s.Id).ToList();
            var a = aql.Where(x => x.SupplierId == s.Id).ToList();
            var lots = p.Sum(x => x.LotsReceived) + a.Count(x => x.OverallResult != AqlDecision.NextStage);
            var rej = p.Sum(x => x.LotsRejected) + a.Count(x => x.OverallResult == AqlDecision.Reject);
            var q = Pct(lots - rej, lots);
            var d = Pct(p.Sum(x => x.OnTimeDeliveries), p.Sum(x => x.Deliveries));
            var sc = scars.Where(x => x.SupplierId == s.Id).ToList();
            var resp = Pct(sc.Count(x => x.ResponseReceivedOn != null && x.ResponseReceivedOn <= x.ResponseDueOn), sc.Count);
            var cost = maxCoq <= 0 ? 100m : Math.Round(100m * (1 - coq.GetValueOrDefault(s.Id) / maxCoq), 1, MidpointRounding.AwayFromZero);
            var verified = sc.Where(x => x.WasEffective != null).ToList();
            var ca = Pct(verified.Count(x => x.WasEffective == true), verified.Count);
            var myAudits = auditLinks.Where(x => x.Supplier == s.Id).Select(x => x.Audit).ToHashSet();
            var f = findings.Where(x => myAudits.Contains(x.AuditId)).ToList();
            var audit = Math.Max(0, 100 - 20 * f.Count(x => x.FindingType == FindingType.MajorNonconformity) - 5 * f.Count(x => x.FindingType == FindingType.MinorNonconformity));
            var c = certs.Where(x => x.SupplierId == s.Id).ToList();
            var cert = Pct(c.Count(x => x.ExpiresOn >= end), c.Count);
            var risk = s.Risk switch { Criticality.Low => 100m, Criticality.Medium => 75m, Criticality.High => 50m, _ => 25m };
            var overall = wsum == 0 ? 0 : Math.Round((q * w.Quality + d * w.Delivery + resp * w.Responsiveness + cost * w.Cost + ca * w.CorrectiveAction + audit * w.AuditResults + cert * w.Certifications + risk * w.Risk) / wsum, 1, MidpointRounding.AwayFromZero);
            return new SupplierScoreRow(s.Id, s.Code, s.LegalName, q, d, resp, cost, ca, audit, cert, risk, overall, 0);
        }).OrderByDescending(x => x.Overall).ThenBy(x => x.Code).ToList();
        var ranked = rows.Select((r, i) => r with { Rank = i + 1 }).ToList();
        return onlySupplier is { } only ? ranked.Where(r => r.SupplierId == only).ToList() : ranked;
    }
}
