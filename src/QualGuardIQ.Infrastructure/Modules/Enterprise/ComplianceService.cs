using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Enterprise;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Enterprise;

public sealed record PackClause(string Ref, string Title);
public sealed record CompliancePack(string Code, string Name, string Source, List<PackClause> Clauses);
public sealed record ObligationInput(ObligationSource Source, string Reference, string Title, string? Party, Guid SiteId, Guid? DepartmentId, Guid? OwnerId, string? Notes);
public sealed record MappingInput(string TargetType, Guid TargetId, string? LinkType);
public sealed record ObligationRow(ComplianceObligation Obligation, List<RecordInfo> Mappings);

/// <summary>
/// Compliance obligations and optional compliance packs. Packs are data (clause numbers and short titles only, loaded from
/// compliance-packs.json); enabling a pack for a site creates one obligation per clause. Mapping an obligation to procedures,
/// forms, records or audits uses the shared relationship engine. No compliance logic is hard-coded per standard.
/// </summary>
public sealed class ComplianceService(AppDbContext db, ModuleSupport m, INumberingService numbers, IRecordRegistry records)
{
    public const string ObligationType = "ComplianceObligation";
    public static readonly string[] MappableTypes = ["Document", "FormTemplate", "FormRecord", "Audit", "AuditProgram", "TrainingCourse", "ControlPlan", "Pfmea", "Validation", "Ncr", "Capa", "Equipment", "Skill", "TrainingCurriculum", "CorporateStandard"];

    static readonly Lazy<List<CompliancePack>> PackData = new(() =>
    {
        using var s = typeof(ComplianceService).Assembly.GetManifestResourceStream("compliance-packs.json") ?? throw new InvalidOperationException("compliance-packs.json is missing.");
        var doc = JsonDocument.Parse(s);
        return doc.RootElement.GetProperty("packs").EnumerateArray().Select(p => new CompliancePack(p.GetProperty("code").GetString()!, p.GetProperty("name").GetString()!, p.GetProperty("source").GetString()!,
            p.GetProperty("clauses").EnumerateArray().Select(c => new PackClause(c.GetProperty("ref").GetString()!, c.GetProperty("title").GetString()!)).ToList())).ToList();
    });

    public static IReadOnlyList<CompliancePack> Packs => PackData.Value;

    public async Task<object> PacksAsync(Guid siteId, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.ComplianceView, siteId, null, ct);
        var active = await db.CompliancePackActivations.AsNoTracking().Where(a => a.SiteId == siteId && a.Enabled).Select(a => a.PackCode).ToListAsync(ct);
        return Packs.Select(p => new { p.Code, p.Name, p.Source, ClauseCount = p.Clauses.Count, Enabled = active.Contains(p.Code) });
    }

    public async Task<int> EnablePackAsync(string code, Guid siteId, bool enabled, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.ComplianceEdit, siteId, null, ct);
        var pack = Packs.FirstOrDefault(p => p.Code == code) ?? throw new NotFoundException("Compliance pack");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var act = await db.CompliancePackActivations.FirstOrDefaultAsync(a => a.PackCode == code && a.SiteId == siteId, ct);
        if (act is null) db.CompliancePackActivations.Add(act = new CompliancePackActivation { PackCode = code, SiteId = siteId });
        act.Enabled = enabled;
        var created = 0;
        if (enabled)
        {
            var have = await db.ComplianceObligations.Where(o => o.PackCode == code && o.SiteId == siteId).ToListAsync(ct);
            foreach (var o in have) o.Applicable = true;
            foreach (var c in pack.Clauses.Where(c => have.All(h => h.Reference != c.Ref)))
            {
                db.ComplianceObligations.Add(new ComplianceObligation
                {
                    Number = await numbers.NextAsync(ObligationType, siteId, ct), Source = Enum.Parse<ObligationSource>(pack.Source), PackCode = code, Reference = c.Ref, Title = c.Title, Party = pack.Name, SiteId = siteId
                });
                created++;
            }
        }
        else
        {
            // Disabling keeps the obligations and their mappings for history; they stop counting as applicable.
            foreach (var o in await db.ComplianceObligations.Where(o => o.PackCode == code && o.SiteId == siteId).ToListAsync(ct)) o.Applicable = false;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return created;
    }

    public async Task<ComplianceObligation> CreateAsync(ObligationInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.ComplianceEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.Reference, "Reference", 60); ModuleSupport.Required(r.Title, "Title", 300);
        if (r.OwnerId is { } o) await m.RequireActiveUserAsync(o, "owner", ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ob = new ComplianceObligation { Number = await numbers.NextAsync(ObligationType, r.SiteId, ct), Source = r.Source, Reference = r.Reference.Trim(), Title = r.Title.Trim(), Party = r.Party?.Trim(),
            SiteId = r.SiteId, DepartmentId = r.DepartmentId, OwnerId = r.OwnerId, Notes = r.Notes?.Trim() };
        db.ComplianceObligations.Add(ob);
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(ObligationType, ob.Id, ob.Title, ct);
        await tx.CommitAsync(ct);
        return ob;
    }

    public async Task<List<ObligationRow>> ListAsync(Guid? siteId, string? packCode, ObligationSource? source, bool unmappedOnly, CancellationToken ct)
    {
        var q = (await m.ScopeAsync(db.ComplianceObligations.AsNoTracking(), Permissions.ComplianceView, ct)).Where(o => o.Applicable);
        if (siteId is { } s) q = q.Where(o => o.SiteId == s);
        if (packCode is { Length: > 0 }) q = q.Where(o => o.PackCode == packCode);
        if (source is { } src) q = q.Where(o => o.Source == src);
        if (unmappedOnly) q = q.Where(o => !db.RecordLinks.Any(l => l.FromType == ObligationType && l.FromId == o.Id));
        var list = await q.OrderBy(o => o.PackCode).ThenBy(o => o.Reference).Take(1000).ToListAsync(ct);
        var ids = list.Select(o => o.Id).ToList();
        var links = await db.RecordLinks.AsNoTracking().Where(l => l.FromType == ObligationType && ids.Contains(l.FromId)).ToListAsync(ct);
        var rows = new List<ObligationRow>();
        foreach (var o in list)
        {
            var maps = new List<RecordInfo>();
            foreach (var l in links.Where(l => l.FromId == o.Id))
                try { maps.Add(await records.RequireViewAsync(l.ToType, l.ToId, ct)); } catch (AppException) { /* not visible to the caller */ }
            rows.Add(new ObligationRow(o, maps));
        }
        return rows;
    }

    public async Task MapAsync(Guid obligationId, MappingInput r, CancellationToken ct)
    {
        var o = await m.EditAsync(ObligationType, obligationId, ct);
        if (!MappableTypes.Contains(r.TargetType)) throw new AppException($"Obligations map to: {string.Join(", ", MappableTypes)}.");
        var target = await records.RequireViewAsync(r.TargetType, r.TargetId, ct);
        if (await db.RecordLinks.AnyAsync(l => l.FromType == ObligationType && l.FromId == obligationId && l.ToType == target.Type && l.ToId == target.Id, ct)) throw new ConflictException("Already mapped.");
        db.RecordLinks.Add(new RecordLink { FromType = ObligationType, FromId = obligationId, ToType = target.Type, ToId = target.Id, LinkType = string.IsNullOrWhiteSpace(r.LinkType) ? "Satisfied by" : r.LinkType.Trim() });
        await db.SaveChangesAsync(ct);
    }

    public async Task UnmapAsync(Guid obligationId, string targetType, Guid targetId, CancellationToken ct)
    {
        await m.EditAsync(ObligationType, obligationId, ct);
        var l = await db.RecordLinks.FirstOrDefaultAsync(x => x.FromType == ObligationType && x.FromId == obligationId && x.ToType == targetType && x.ToId == targetId, ct) ?? throw new NotFoundException("Mapping");
        db.RecordLinks.Remove(l);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Coverage per pack and per source: applicable obligations and how many have at least one mapping.</summary>
    public async Task<object> CoverageAsync(Guid siteId, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.ComplianceView, siteId, null, ct);
        var rows = await db.ComplianceObligations.AsNoTracking().Where(o => o.SiteId == siteId && o.Applicable)
            .Select(o => new { o.PackCode, o.Source, Mapped = db.RecordLinks.Any(l => l.FromType == ObligationType && l.FromId == o.Id),
                Audited = db.RecordLinks.Any(l => l.FromType == ObligationType && l.FromId == o.Id && (l.ToType == "Audit" || l.ToType == "AuditProgram")) }).ToListAsync(ct);
        return new
        {
            Total = rows.Count, Mapped = rows.Count(r => r.Mapped), Audited = rows.Count(r => r.Audited),
            ByPack = rows.GroupBy(r => r.PackCode ?? "(own)").Select(g => new { Pack = g.Key, Total = g.Count(), Mapped = g.Count(r => r.Mapped), Audited = g.Count(r => r.Audited) }).OrderBy(x => x.Pack),
            BySource = rows.GroupBy(r => r.Source).Select(g => new { Source = g.Key.ToString(), Total = g.Count(), Mapped = g.Count(r => r.Mapped) })
        };
    }
}

public sealed record AuditProgramInput(string Title, int Year, Guid SiteId, Guid? DepartmentId, string? Objectives, Guid? OwnerId);
public sealed record PlanItemInput(string AuditType, string Subject, string? Criteria, int PlannedMonth, Guid? LeadAuditorId, List<Guid>? TeamIds, Guid? SupplierId, Guid? DepartmentId);
public sealed record ScheduleAuditInput(DateOnly PlannedStart, DateOnly PlannedEnd, string Scope, List<ChecklistQuestion> Checklist);

/// <summary>Audit programs: annual plans with typed audits (internal, supplier, layered, process, product, system), teams and qualified auditors. Scheduling creates an audit in the existing audit module.</summary>
public sealed class AuditProgramService(AppDbContext db, ModuleSupport m, INumberingService numbers, QualificationService q, AuditService audits)
{
    public const string RecordType = "AuditProgram";
    public static readonly string[] AuditTypes = ["Internal", "Supplier", "Layered", "Process", "Product", "System", "Regulatory", "Customer"];

    public async Task<AuditProgram> CreateAsync(AuditProgramInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.AuditsEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.Title, "Title", 300);
        if (r.Year is < 2000 or > 2100) throw new AppException("Enter a valid year.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var p = new AuditProgram { Number = await numbers.NextAsync(RecordType, r.SiteId, ct), Title = r.Title.Trim(), Year = r.Year, SiteId = r.SiteId, DepartmentId = r.DepartmentId, Objectives = r.Objectives?.Trim(), OwnerId = r.OwnerId ?? m.UserId };
        db.AuditPrograms.Add(p);
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, p.Id, p.Title, ct);
        await tx.CommitAsync(ct);
        return p;
    }

    public async Task<List<AuditProgram>> ListAsync(CancellationToken ct) =>
        await (await m.ScopeAsync(db.AuditPrograms.AsNoTracking(), Permissions.AuditsView, ct)).OrderByDescending(p => p.Year).ThenBy(p => p.Number).ToListAsync(ct);

    public async Task<(AuditProgram Program, List<AuditPlanItem> Items)> GetAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        return (await db.AuditPrograms.AsNoTracking().FirstAsync(p => p.Id == id, ct), await db.AuditPlanItems.AsNoTracking().Where(i => i.ProgramId == id).OrderBy(i => i.PlannedMonth).ThenBy(i => i.Subject).ToListAsync(ct));
    }

    async Task CheckTeamAsync(AuditProgram p, PlanItemInput r, CancellationToken ct)
    {
        if (!AuditTypes.Contains(r.AuditType)) throw new AppException($"Audit type must be one of: {string.Join(", ", AuditTypes)}.");
        ModuleSupport.Required(r.Subject, "Subject", 300);
        if (r.PlannedMonth is < 1 or > 12) throw new AppException("Planned month must be 1–12.");
        if (r.AuditType == "Supplier" && r.SupplierId is null) throw new AppException("A supplier audit needs the supplier.");
        if (r.SupplierId is { } s && !await db.Suppliers.AnyAsync(x => x.Id == s, ct)) throw new AppException("Select an existing supplier.");
        if (r.LeadAuditorId is { } lead)
        {
            await m.RequireActiveUserAsync(lead, "lead auditor", ct);
            await q.RequireAuditorAsync(lead, r.AuditType, p.SiteId, "lead auditor", ct);
        }
        foreach (var t in (r.TeamIds ?? []).Distinct())
        {
            await m.RequireActiveUserAsync(t, "audit team member", ct);
            await q.RequireAuditorAsync(t, r.AuditType, p.SiteId, "audit team member", ct);
        }
    }

    public async Task<AuditPlanItem> AddItemAsync(Guid programId, PlanItemInput r, CancellationToken ct)
    {
        await m.EditAsync(RecordType, programId, ct);
        var p = await db.AuditPrograms.AsNoTracking().FirstAsync(x => x.Id == programId, ct);
        if (p.Status == AuditProgramStatus.Closed) throw new ConflictException("The program is closed.");
        await CheckTeamAsync(p, r, ct);
        var i = new AuditPlanItem { ProgramId = programId, SiteId = p.SiteId, DepartmentId = r.DepartmentId ?? p.DepartmentId, AuditType = r.AuditType, Subject = r.Subject.Trim(), Criteria = r.Criteria?.Trim(),
            PlannedMonth = r.PlannedMonth, LeadAuditorId = r.LeadAuditorId, TeamIds = (r.TeamIds ?? []).Distinct().ToList(), SupplierId = r.SupplierId };
        db.AuditPlanItems.Add(i);
        await db.SaveChangesAsync(ct);
        return i;
    }

    public async Task UpdateItemAsync(Guid itemId, PlanItemInput r, CancellationToken ct)
    {
        var i = await db.AuditPlanItems.FirstOrDefaultAsync(x => x.Id == itemId, ct) ?? throw new NotFoundException("Plan item");
        await m.EditAsync(RecordType, i.ProgramId, ct);
        if (i.AuditId is not null) throw new ConflictException("This item is already scheduled; change the audit itself.");
        var p = await db.AuditPrograms.AsNoTracking().FirstAsync(x => x.Id == i.ProgramId, ct);
        await CheckTeamAsync(p, r, ct);
        i.AuditType = r.AuditType; i.Subject = r.Subject.Trim(); i.Criteria = r.Criteria?.Trim(); i.PlannedMonth = r.PlannedMonth; i.LeadAuditorId = r.LeadAuditorId; i.TeamIds = (r.TeamIds ?? []).Distinct().ToList(); i.SupplierId = r.SupplierId;
        await db.SaveChangesAsync(ct);
    }

    public async Task ApproveAsync(Guid programId, CancellationToken ct)
    {
        var info = await m.ViewAsync(RecordType, programId, ct);
        await m.RequireAsync(Permissions.AuditsApprove, info.SiteId, info.DepartmentId, ct);
        var p = await db.AuditPrograms.FirstAsync(x => x.Id == programId, ct);
        if (p.Status != AuditProgramStatus.Draft) throw new ConflictException("Only a draft program can be approved.");
        if (!await db.AuditPlanItems.AnyAsync(i => i.ProgramId == programId, ct)) throw new AppException("Add at least one planned audit.");
        p.Status = AuditProgramStatus.Approved;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Creates the audit in the audit module from a plan item (the lead auditor is re-checked there).</summary>
    public async Task<Audit> ScheduleAsync(Guid itemId, ScheduleAuditInput r, CancellationToken ct)
    {
        var i = await db.AuditPlanItems.FirstOrDefaultAsync(x => x.Id == itemId, ct) ?? throw new NotFoundException("Plan item");
        await m.EditAsync(RecordType, i.ProgramId, ct);
        if (i.AuditId is not null) throw new ConflictException("Already scheduled.");
        if (i.LeadAuditorId is not { } lead) throw new AppException("Assign a lead auditor first.");
        var p = await db.AuditPrograms.AsNoTracking().FirstAsync(x => x.Id == i.ProgramId, ct);
        if (p.Status != AuditProgramStatus.Approved) throw new ConflictException("Approve the program before scheduling audits.");
        foreach (var t in i.TeamIds) await q.RequireAuditorAsync(t, i.AuditType, p.SiteId, "audit team member", ct);
        var a = await audits.CreateAsync(new CreateAuditRequest(i.Subject, i.AuditType, string.IsNullOrWhiteSpace(r.Scope) ? i.Subject : r.Scope, i.Criteria, i.SiteId, i.DepartmentId, lead, r.PlannedStart, r.PlannedEnd, r.Checklist), ct);
        i.AuditId = a.Id;
        db.RecordLinks.Add(new RecordLink { FromType = RecordType, FromId = p.Id, ToType = AuditService.RecordType, ToId = a.Id, LinkType = "Planned audit" });
        await db.SaveChangesAsync(ct);
        return a;
    }
}
