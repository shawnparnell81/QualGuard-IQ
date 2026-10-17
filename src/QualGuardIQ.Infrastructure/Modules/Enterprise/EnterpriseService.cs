using QualGuardIQ.Application.Records;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Enterprise;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Enterprise;

public sealed record OrgUnitInput(string Code, string Name, OrgLevel Level, Guid? ParentId, Guid? SiteId, Guid? DepartmentId);
public sealed record StandardInput(string Title, Guid DocumentId, Guid SiteId, bool AllowLocalProcedure, Guid? OwnerId);
public sealed record RolloutInput(List<Guid> SiteIds, DateOnly? DueOn);
public sealed record RolloutAdoptInput(Guid? LocalDocumentId, Guid? TrainingCourseId, List<Guid>? TraineeIds, string? Notes);

/// <summary>Per-site quality metrics. Every count is limited to sites the caller can view in the source module.</summary>
public sealed record SiteMetrics(Guid SiteId, string SiteCode, string SiteName,
    int OpenNcrs, int OverdueNcrs, int OpenCapas, int OverdueCapas, int OpenAudits, int OverdueFindings, int OpenComplaints, int OverdueComplaints,
    int OpenScars, int ScrapEvents30d, decimal ScrapQty30d, int InspectionsRejected30d, int Inspections30d,
    int TrainingAssigned, int TrainingOverdue, int Obligations, int ObligationsMapped, decimal Coq90d, int HighRisks)
{
    public decimal TrainingCompliancePct => TrainingAssigned == 0 ? 100 : Math.Round(100m * (TrainingAssigned - TrainingOverdue) / TrainingAssigned, 1);
    public decimal ComplianceCoveragePct => Obligations == 0 ? 100 : Math.Round(100m * ObligationsMapped / Obligations, 1);
    public decimal FirstPassPct => Inspections30d == 0 ? 100 : Math.Round(100m * (Inspections30d - InspectionsRejected30d) / Inspections30d, 1);
}

public sealed record UnitRollup(Guid Id, string Code, string Name, OrgLevel Level, Guid? ParentId, List<Guid> SiteIds, SiteMetrics Totals);

/// <summary>Multi-site governance: organization tree, corporate standards and plant rollout, rollup metrics, war room and governance dashboards.</summary>
public sealed class EnterpriseService(AppDbContext db, ModuleSupport m, INumberingService numbers, TrainingService training, INotifier notifier)
{
    public const string StandardType = "CorporateStandard";

    // ---------- organization tree ----------
    public async Task<List<OrgUnit>> UnitsAsync(CancellationToken ct) => await db.OrgUnits.AsNoTracking().Where(u => !u.IsArchived).OrderBy(u => u.Level).ThenBy(u => u.Code).ToListAsync(ct);

    async Task ValidateUnitAsync(OrgUnitInput r, Guid? self, CancellationToken ct)
    {
        ModuleSupport.Required(r.Code, "Code", 30); ModuleSupport.Required(r.Name, "Name", 200);
        if (r.Level is OrgLevel.Plant or OrgLevel.Warehouse or OrgLevel.Laboratory && r.SiteId is null) throw new AppException($"A {r.Level.ToString().ToLowerInvariant()} must point at a site.");
        if (r.Level == OrgLevel.Department && r.DepartmentId is null) throw new AppException("A department unit must point at a department.");
        if (r.Level == OrgLevel.Corporate && r.ParentId is not null) throw new AppException("The corporate level has no parent.");
        if (r.Level != OrgLevel.Corporate && r.ParentId is null) throw new AppException("Choose the parent unit.");
        if (r.SiteId is { } s && !await db.Sites.AnyAsync(x => x.Id == s, ct)) throw new AppException("Select an existing site.");
        if (r.DepartmentId is { } d && !await db.Departments.AnyAsync(x => x.Id == d, ct)) throw new AppException("Select an existing department.");
        if (r.ParentId is { } p)
        {
            var parent = await db.OrgUnits.AsNoTracking().FirstOrDefaultAsync(x => x.Id == p, ct) ?? throw new AppException("Select an existing parent unit.");
            if (parent.Level >= r.Level && !(r.Level == OrgLevel.Department)) throw new AppException($"A {r.Level} cannot sit under a {parent.Level}.");
            // walk up to detect a cycle
            var cursor = parent; var guard = 0;
            while (cursor is not null && guard++ < 20)
            {
                if (cursor.Id == self) throw new AppException("A unit cannot be placed under itself.");
                cursor = cursor.ParentId is { } pp ? await db.OrgUnits.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pp, ct) : null;
            }
        }
    }

    public async Task<OrgUnit> CreateUnitAsync(OrgUnitInput r, CancellationToken ct)
    {
        await ValidateUnitAsync(r, null, ct);
        if (await db.OrgUnits.AnyAsync(x => x.Code == r.Code.Trim(), ct)) throw new ConflictException("That code is already used.");
        var u = new OrgUnit { Code = r.Code.Trim(), Name = r.Name.Trim(), Level = r.Level, ParentId = r.ParentId, SiteId = r.SiteId, DepartmentId = r.DepartmentId };
        db.OrgUnits.Add(u);
        await db.SaveChangesAsync(ct);
        return u;
    }

    public async Task UpdateUnitAsync(Guid id, OrgUnitInput r, CancellationToken ct)
    {
        var u = await db.OrgUnits.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Org unit");
        await ValidateUnitAsync(r, id, ct);
        u.Name = r.Name.Trim(); u.Level = r.Level; u.ParentId = r.ParentId; u.SiteId = r.SiteId; u.DepartmentId = r.DepartmentId;
        await db.SaveChangesAsync(ct);
    }

    public async Task ArchiveUnitAsync(Guid id, CancellationToken ct)
    {
        var u = await db.OrgUnits.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Org unit");
        if (await db.OrgUnits.AnyAsync(x => x.ParentId == id && !x.IsArchived, ct)) throw new ConflictException("Move or archive the units below it first.");
        u.IsArchived = true; u.ArchivedAt = m.Now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Sites under a unit (inclusive), following the tree.</summary>
    public static List<Guid> SitesUnder(List<OrgUnit> units, Guid unitId)
    {
        var result = new HashSet<Guid>();
        var stack = new Stack<Guid>([unitId]);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            var u = units.FirstOrDefault(x => x.Id == id);
            if (u?.SiteId is { } s) result.Add(s);
            foreach (var c in units.Where(x => x.ParentId == id)) stack.Push(c.Id);
        }
        return result.ToList();
    }

    // ---------- metrics ----------
    async Task<HashSet<Guid>?> ScopeSitesAsync(string permission, CancellationToken ct)
    {
        var all = await db.Sites.AsNoTracking().Select(s => s.Id).ToListAsync(ct);
        var allowed = new HashSet<Guid>();
        foreach (var s in all) if (await m.CanAsync(permission, s, null, ct)) allowed.Add(s);
        return allowed;
    }

    public async Task<List<SiteMetrics>> SiteMetricsAsync(IReadOnlyCollection<Guid>? onlySites, CancellationToken ct)
    {
        var sites = await db.Sites.AsNoTracking().Where(s => !s.IsArchived && (onlySites == null || onlySites.Contains(s.Id))).OrderBy(s => s.Code).ToListAsync(ct);
        var ncr = await ScopeSitesAsync(Permissions.NcrView, ct); var capa = await ScopeSitesAsync(Permissions.CapaView, ct); var aud = await ScopeSitesAsync(Permissions.AuditsView, ct);
        var cc = await ScopeSitesAsync(Permissions.ComplaintsView, ct); var sup = await ScopeSitesAsync(Permissions.SuppliersView, ct); var coq = await ScopeSitesAsync(Permissions.CoqView, ct);
        var ins = await ScopeSitesAsync(Permissions.InspectionView, ct); var trn = await ScopeSitesAsync(Permissions.TrainingView, ct); var cmp = await ScopeSitesAsync(Permissions.ComplianceView, ct);
        var rsk = await ScopeSitesAsync(Permissions.RisksView, ct);
        var ids = sites.Select(s => s.Id).ToList();
        var result = new List<SiteMetrics>();
        var today = DateOnly.FromDateTime(m.Now.UtcDateTime);
        var d30 = today.AddDays(-30); var d90 = today.AddDays(-90); var t30 = m.Now.AddDays(-30);
        var ncrs = await db.Ncrs.AsNoTracking().Where(x => ids.Contains(x.SiteId) && x.Status != NcrStatus.Closed && x.Status != NcrStatus.Cancelled).Select(x => new { x.SiteId, x.DueOn }).ToListAsync(ct);
        var capas = await db.Capas.AsNoTracking().Where(x => ids.Contains(x.SiteId) && x.Status != CapaStatus.Closed && x.Status != CapaStatus.Cancelled).Select(x => new { x.SiteId, x.DueOn }).ToListAsync(ct);
        var audits = await db.Audits.AsNoTracking().Where(x => ids.Contains(x.SiteId) && x.Status != AuditStatus.Closed && x.Status != AuditStatus.Cancelled).Select(x => x.SiteId).ToListAsync(ct);
        var findings = await db.AuditFindings.AsNoTracking().Where(x => ids.Contains(x.SiteId) && x.Status != FindingStatus.Closed && x.ResponseDueOn < today).Select(x => x.SiteId).ToListAsync(ct);
        var complaints = await db.Complaints.AsNoTracking().Where(x => ids.Contains(x.SiteId) && x.Status != ComplaintStatus.Closed && x.Status != ComplaintStatus.Cancelled).Select(x => new { x.SiteId, x.ResponseDueOn }).ToListAsync(ct);
        var scars = await db.SupplierCorrectiveActions.AsNoTracking().Where(x => ids.Contains(x.SiteId) && x.Status != ScarStatus.Closed && x.Status != ScarStatus.Cancelled).Select(x => x.SiteId).ToListAsync(ct);
        var scrap = await db.ScrapEvents.AsNoTracking().Where(x => ids.Contains(x.SiteId) && x.ScrappedOn >= d30).Select(x => new { x.SiteId, x.Quantity }).ToListAsync(ct);
        var inspections = await db.Inspections.AsNoTracking().Where(x => ids.Contains(x.SiteId) && x.CreatedAt >= t30 && x.Status != InspectionStatus.Cancelled).Select(x => new { x.SiteId, x.Status }).ToListAsync(ct);
        var assigns = await db.TrainingAssignments.AsNoTracking().Where(x => ids.Contains(x.SiteId) && x.Status == AssignmentStatus.Assigned).Select(x => new { x.SiteId, x.DueOn }).ToListAsync(ct);
        var obligations = await db.ComplianceObligations.AsNoTracking().Where(x => ids.Contains(x.SiteId) && x.Applicable)
            .Select(x => new { x.SiteId, Mapped = db.RecordLinks.Any(l => l.FromType == ComplianceService.ObligationType && l.FromId == x.Id) }).ToListAsync(ct);
        var coqs = await db.CoqEntries.AsNoTracking().Where(x => ids.Contains(x.SiteId) && x.IncurredOn >= d90).GroupBy(x => x.SiteId).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToListAsync(ct);
        var risks = await db.Risks.AsNoTracking().Where(x => ids.Contains(x.SiteId) && x.Status != RiskStatus.Closed && x.Status != RiskStatus.Cancelled && (x.ResidualBand ?? x.InitialBand) == "High").Select(x => x.SiteId).ToListAsync(ct);
        static int C<T>(bool ok, IEnumerable<T> xs) => ok ? xs.Count() : 0;
        foreach (var s in sites)
        {
            var id = s.Id;
            bool N = ncr!.Contains(id), Ca = capa!.Contains(id), A = aud!.Contains(id), Co = cc!.Contains(id), Su = sup!.Contains(id), Q = coq!.Contains(id), I = ins!.Contains(id), T = trn!.Contains(id), Cm = cmp!.Contains(id), R = rsk!.Contains(id);
            result.Add(new SiteMetrics(id, s.Code, s.Name,
                C(N, ncrs.Where(x => x.SiteId == id)), C(N, ncrs.Where(x => x.SiteId == id && x.DueOn < today)),
                C(Ca, capas.Where(x => x.SiteId == id)), C(Ca, capas.Where(x => x.SiteId == id && x.DueOn < today)),
                C(A, audits.Where(x => x == id)), C(A, findings.Where(x => x == id)),
                C(Co, complaints.Where(x => x.SiteId == id)), C(Co, complaints.Where(x => x.SiteId == id && x.ResponseDueOn < today)),
                C(Su, scars.Where(x => x == id)), C(Q, scrap.Where(x => x.SiteId == id)), Q ? scrap.Where(x => x.SiteId == id).Sum(x => x.Quantity) : 0,
                C(I, inspections.Where(x => x.SiteId == id && (x.Status == InspectionStatus.Failed || x.Status == InspectionStatus.Rejected))), C(I, inspections.Where(x => x.SiteId == id)),
                C(T, assigns.Where(x => x.SiteId == id)), C(T, assigns.Where(x => x.SiteId == id && x.DueOn < today)),
                C(Cm, obligations.Where(x => x.SiteId == id)), C(Cm, obligations.Where(x => x.SiteId == id && x.Mapped)),
                Q ? coqs.FirstOrDefault(x => x.Key == id)?.Sum ?? 0 : 0, C(R, risks.Where(x => x == id))));
        }
        return result;
    }

    static SiteMetrics Sum(Guid id, string code, string name, IEnumerable<SiteMetrics> xs)
    {
        var l = xs.ToList();
        return new SiteMetrics(id, code, name, l.Sum(x => x.OpenNcrs), l.Sum(x => x.OverdueNcrs), l.Sum(x => x.OpenCapas), l.Sum(x => x.OverdueCapas), l.Sum(x => x.OpenAudits), l.Sum(x => x.OverdueFindings),
            l.Sum(x => x.OpenComplaints), l.Sum(x => x.OverdueComplaints), l.Sum(x => x.OpenScars), l.Sum(x => x.ScrapEvents30d), l.Sum(x => x.ScrapQty30d), l.Sum(x => x.InspectionsRejected30d), l.Sum(x => x.Inspections30d),
            l.Sum(x => x.TrainingAssigned), l.Sum(x => x.TrainingOverdue), l.Sum(x => x.Obligations), l.Sum(x => x.ObligationsMapped), l.Sum(x => x.Coq90d), l.Sum(x => x.HighRisks));
    }

    /// <summary>Plant, regional and corporate rollups: each unit totals the sites below it.</summary>
    public async Task<List<UnitRollup>> RollupAsync(CancellationToken ct)
    {
        var units = await UnitsAsync(ct);
        var metrics = await SiteMetricsAsync(null, ct);
        return units.Select(u =>
        {
            var sites = SitesUnder(units, u.Id);
            return new UnitRollup(u.Id, u.Code, u.Name, u.Level, u.ParentId, sites, Sum(u.Id, u.Code, u.Name, metrics.Where(x => sites.Contains(x.SiteId))));
        }).ToList();
    }

    /// <summary>Governance dashboard for a corporate, regional or plant unit (or every site in scope when no unit is given).</summary>
    public async Task<object> GovernanceAsync(Guid? unitId, CancellationToken ct)
    {
        var units = await UnitsAsync(ct);
        List<Guid>? sites = unitId is { } u ? SitesUnder(units, u) : null;
        var metrics = await SiteMetricsAsync(sites, ct);
        var unit = unitId is { } uid ? units.FirstOrDefault(x => x.Id == uid) ?? throw new NotFoundException("Org unit") : null;
        var totals = Sum(unit?.Id ?? Guid.Empty, unit?.Code ?? "ALL", unit?.Name ?? "All sites in scope", metrics);
        var children = unit is null ? [] : units.Where(x => x.ParentId == unit.Id).Select(c =>
        {
            var cs = SitesUnder(units, c.Id);
            return Sum(c.Id, c.Code, c.Name, metrics.Where(x => cs.Contains(x.SiteId)));
        }).ToList();
        return new
        {
            Unit = unit, Level = unit?.Level.ToString() ?? "Corporate", Totals = totals,
            Risk = new { totals.HighRisks }, Cost = new { totals.Coq90d, totals.ScrapQty30d },
            Compliance = new { totals.Obligations, totals.ObligationsMapped, Pct = totals.ComplianceCoveragePct },
            Training = new { totals.TrainingAssigned, totals.TrainingOverdue, Pct = totals.TrainingCompliancePct },
            SupplierQuality = new { totals.OpenScars }, CustomerQuality = new { totals.OpenComplaints, totals.OverdueComplaints },
            Children = children, Sites = metrics
        };
    }

    /// <summary>Plant war room: live counts plus the oldest open items for each area, each linking to its record.</summary>
    public async Task<object> WarRoomAsync(Guid siteId, CancellationToken ct)
    {
        if (!await db.Sites.AnyAsync(s => s.Id == siteId, ct)) throw new NotFoundException("Site");
        var metrics = (await SiteMetricsAsync([siteId], ct)).Single();
        var ncrs = await m.CanAsync(Permissions.NcrView, siteId, null, ct)
            ? await db.Ncrs.AsNoTracking().Where(x => x.SiteId == siteId && x.Status != NcrStatus.Closed && x.Status != NcrStatus.Cancelled).OrderBy(x => x.CreatedAt).Take(8).Select(x => new { x.Id, x.Number, x.Title, Status = x.Status.ToString(), x.DueOn, Url = "/ncr/" + x.Id }).ToListAsync(ct) : [];
        var capas = await m.CanAsync(Permissions.CapaView, siteId, null, ct)
            ? await db.Capas.AsNoTracking().Where(x => x.SiteId == siteId && x.Status != CapaStatus.Closed && x.Status != CapaStatus.Cancelled).OrderBy(x => x.CreatedAt).Take(8).Select(x => new { x.Id, x.Number, x.Title, Status = x.Status.ToString(), x.DueOn, Url = "/capa/" + x.Id }).ToListAsync(ct) : [];
        var audits = await m.CanAsync(Permissions.AuditsView, siteId, null, ct)
            ? await db.Audits.AsNoTracking().Where(x => x.SiteId == siteId && x.Status != AuditStatus.Closed && x.Status != AuditStatus.Cancelled).OrderBy(x => x.PlannedStart).Take(8).Select(x => new { x.Id, x.Number, x.Title, Status = x.Status.ToString(), DueOn = (DateOnly?)x.PlannedEnd, Url = "/audits/" + x.Id }).ToListAsync(ct) : [];
        var complaints = await m.CanAsync(Permissions.ComplaintsView, siteId, null, ct)
            ? await db.Complaints.AsNoTracking().Where(x => x.SiteId == siteId && x.Status != ComplaintStatus.Closed && x.Status != ComplaintStatus.Cancelled).OrderBy(x => x.ResponseDueOn).Take(8).Select(x => new { x.Id, x.Number, x.Title, Status = x.Status.ToString(), DueOn = (DateOnly?)x.ResponseDueOn, Url = "/complaints/" + x.Id }).ToListAsync(ct) : [];
        var scars = await m.CanAsync(Permissions.SuppliersView, siteId, null, ct)
            ? await db.SupplierCorrectiveActions.AsNoTracking().Where(x => x.SiteId == siteId && x.Status != ScarStatus.Closed && x.Status != ScarStatus.Cancelled).OrderBy(x => x.CreatedAt).Take(8).Select(x => new { x.Id, x.Number, Title = x.Number, Status = x.Status.ToString(), DueOn = (DateOnly?)null, Url = "/suppliers/scar/" + x.Id }).ToListAsync(ct) : [];
        return new { Metrics = metrics, Ncrs = ncrs, Capas = capas, Audits = audits, Complaints = complaints, SupplierIssues = scars, GeneratedAt = m.Now };
    }

    // ---------- corporate standards ----------
    public async Task<CorporateStandard> CreateStandardAsync(StandardInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.EnterpriseEdit, r.SiteId, null, ct);
        ModuleSupport.Required(r.Title, "Title", 300);
        var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == r.DocumentId && d.Kind == DocumentKind.Document, ct) ?? throw new AppException("Select an existing controlled document.");
        if (r.OwnerId is { } o) await m.RequireActiveUserAsync(o, "owner", ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var s = new CorporateStandard { Number = await numbers.NextAsync(StandardType, r.SiteId, ct), Title = r.Title.Trim(), DocumentId = doc.Id, SiteId = r.SiteId, OwnerId = r.OwnerId ?? m.UserId,
            AllowLocalProcedure = r.AllowLocalProcedure, Status = StandardStatus.Published };
        db.CorporateStandards.Add(s);
        db.RecordLinks.Add(new RecordLink { FromType = StandardType, FromId = s.Id, ToType = "Document", ToId = doc.Id, LinkType = "Issued as" });
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(StandardType, s.Id, s.Title, ct);
        await tx.CommitAsync(ct);
        return s;
    }

    public async Task<List<CorporateStandard>> StandardsAsync(CancellationToken ct) => await db.CorporateStandards.AsNoTracking().OrderBy(s => s.Number).ToListAsync(ct);

    public async Task<List<StandardRollout>> RolloutsAsync(Guid standardId, CancellationToken ct)
    {
        await m.ViewAsync(StandardType, standardId, ct);
        return await db.StandardRollouts.AsNoTracking().Where(r => r.StandardId == standardId).ToListAsync(ct);
    }

    public async Task<int> RollOutAsync(Guid standardId, RolloutInput r, CancellationToken ct)
    {
        await m.EditAsync(StandardType, standardId, ct);
        var s = await db.CorporateStandards.AsNoTracking().FirstAsync(x => x.Id == standardId, ct);
        if (s.Status != StandardStatus.Published) throw new ConflictException("Only a published standard can be rolled out.");
        var ids = r.SiteIds.Distinct().ToList();
        if (ids.Count == 0) throw new AppException("Select at least one site.");
        if (await db.Sites.CountAsync(x => ids.Contains(x.Id), ct) != ids.Count) throw new AppException("Select existing sites.");
        var existing = await db.StandardRollouts.Where(x => x.StandardId == standardId).Select(x => x.SiteId).ToListAsync(ct);
        var n = 0;
        foreach (var site in ids.Except(existing))
        {
            db.StandardRollouts.Add(new StandardRollout { StandardId = standardId, SiteId = site, Status = RolloutStatus.Planned, DueOn = r.DueOn });
            n++;
            var managers = await db.UserRoleAssignments.Where(a => a.SiteId == site).Join(db.RolePermissions.Where(p => p.Permission == Permissions.EnterpriseEdit), a => a.RoleId, p => p.RoleId, (a, p) => a.UserId).Distinct().ToListAsync(ct);
            if (managers.Count > 0) await notifier.NotifyAsync(managers, $"Corporate standard {s.Number} to adopt", s.Title, await m.RecordAsync(StandardType, s.Id, ct), ct: ct);
        }
        await db.SaveChangesAsync(ct);
        return n;
    }

    /// <summary>The plant adopts the standard: optional local procedure, the training course used for rollout and who must be trained.</summary>
    public async Task AdoptAsync(Guid rolloutId, RolloutAdoptInput r, CancellationToken ct)
    {
        var ro = await db.StandardRollouts.FirstOrDefaultAsync(x => x.Id == rolloutId, ct) ?? throw new NotFoundException("Rollout");
        await m.RequireAsync(Permissions.EnterpriseEdit, ro.SiteId, null, ct);
        var s = await db.CorporateStandards.AsNoTracking().FirstAsync(x => x.Id == ro.StandardId, ct);
        if (ro.Status is not (RolloutStatus.Planned or RolloutStatus.Adopted)) throw new ConflictException($"The rollout is already {ro.Status}.");
        if (r.LocalDocumentId is { } ld)
        {
            if (!s.AllowLocalProcedure) throw new AppException("This standard is adopted as issued; no local procedure is allowed.");
            if (!await db.Documents.AnyAsync(d => d.Id == ld && d.SiteId == ro.SiteId, ct)) throw new AppException("The local procedure must be a document of the adopting site.");
            ro.LocalDocumentId = ld;
            db.RecordLinks.Add(new RecordLink { FromType = StandardType, FromId = s.Id, ToType = "Document", ToId = ld, LinkType = "Implemented locally by" });
        }
        ro.Notes = r.Notes?.Trim();
        ro.Status = RolloutStatus.Adopted;
        if (r.TrainingCourseId is { } cid)
        {
            var course = await db.TrainingCourses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid, ct) ?? throw new AppException("Select an existing course.");
            if (course.SiteId != ro.SiteId) throw new AppException("Use a training course of the adopting site.");
            ro.TrainingCourseId = cid;
            var trainees = (r.TraineeIds ?? []).Distinct().ToList();
            if (trainees.Count > 0)
            {
                if (await db.Users.CountAsync(u => trainees.Contains(u.Id) && u.IsActive, ct) != trainees.Count) throw new AppException("Every trainee must be an active user.");
                await db.SaveChangesAsync(ct);
                await training.AssignSystemAsync(cid, trainees, $"Rollout of corporate standard {s.Number}", ct);
            }
            ro.Status = RolloutStatus.Training;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Verification: every trainee of the rollout course has completed it. Verified by someone other than the adopter is not enforced; the e-signed approval engine is used for that when configured.</summary>
    public async Task<object> RolloutStatusAsync(Guid rolloutId, CancellationToken ct)
    {
        var ro = await db.StandardRollouts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == rolloutId, ct) ?? throw new NotFoundException("Rollout");
        await m.RequireAsync(Permissions.EnterpriseView, ro.SiteId, null, ct);
        var s = await db.CorporateStandards.AsNoTracking().FirstAsync(x => x.Id == ro.StandardId, ct);
        var reason = $"Rollout of corporate standard {s.Number}";
        var assigns = ro.TrainingCourseId is { } c
            ? await db.TrainingAssignments.AsNoTracking().Where(a => a.CourseId == c && a.Reason == reason && a.Status != AssignmentStatus.Cancelled && a.Status != AssignmentStatus.Superseded).Select(a => new { a.UserId, Status = a.Status.ToString(), a.DueOn }).ToListAsync(ct)
            : [];
        return new { Rollout = ro, Trainees = assigns.Count, Completed = assigns.Count(a => a.Status == "Completed"), Assignments = assigns };
    }

    public async Task VerifyRolloutAsync(Guid rolloutId, string? notes, CancellationToken ct)
    {
        var ro = await db.StandardRollouts.FirstOrDefaultAsync(x => x.Id == rolloutId, ct) ?? throw new NotFoundException("Rollout");
        await m.RequireAsync(Permissions.EnterpriseEdit, ro.SiteId, null, ct);
        if (ro.Status is RolloutStatus.Planned) throw new ConflictException("The site has not adopted the standard yet.");
        if (ro.Status is RolloutStatus.Verified) throw new ConflictException("Already verified.");
        var s = await db.CorporateStandards.AsNoTracking().FirstAsync(x => x.Id == ro.StandardId, ct);
        if (ro.TrainingCourseId is { } c)
        {
            var reason = $"Rollout of corporate standard {s.Number}";
            var open = await db.TrainingAssignments.CountAsync(a => a.CourseId == c && a.Reason == reason && a.Status == AssignmentStatus.Assigned, ct);
            if (open > 0) throw new ConflictException($"{open} trainee(s) have not completed the rollout training.");
        }
        ro.Status = RolloutStatus.Verified; ro.VerifiedById = m.UserId; ro.VerifiedAt = m.Now;
        if (!string.IsNullOrWhiteSpace(notes)) ro.Notes = (ro.Notes + "\nVerification: " + notes.Trim()).Trim();
        await db.SaveChangesAsync(ct);
    }
}
