using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Enterprise;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Identity;
using QualGuardIQ.Infrastructure.Modules.Apqp;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Enterprise;

public sealed record ProjectInput(string Title, QualityProjectKind Kind, Guid SiteId, Guid? DepartmentId, Guid? OwnerId, Guid? ProductId, Guid? PartId, Guid? CustomerId, Guid? SupplierId, Guid? TargetSiteId, DateOnly? TargetLaunchOn);
public sealed record DeliverableInput(int Phase, string Name, DateOnly? DueOn, Guid? OwnerId);
public sealed record DeliverableUpdate(DeliverableStatus Status, string? LinkedType, Guid? LinkedId);
public sealed record GateInput(GateDecision Decision, string Password, string? Notes);
public sealed record PpapInput(Guid PartId, string? PartRevision, Guid? CustomerId, Guid? ProjectId, int Level, string Reason, Guid SiteId, Guid? DepartmentId);
public sealed record PpapElementUpdate(PpapElementStatus Status, string? LinkedType, Guid? LinkedId, string? Notes);
public sealed record CustomerDecisionInput(PpapStatus Decision, string CustomerName, DateOnly DecidedOn, string? Comments, string Password);
public sealed record CapabilityInput(string Title, string Characteristic, Guid SiteId, Guid? DepartmentId, Guid? ProductId, Guid? PartId, Guid? ControlPlanId, string? Process,
    decimal? Lsl, decimal? Usl, int SubgroupSize, List<decimal> Values, decimal RequiredCpk = 1.33m);

/// <summary>Project quality planning and APQP (phase gates, deliverables, readiness), PPAP submissions and capability studies.</summary>
public sealed class ProjectService(AppDbContext db, ModuleSupport m, INumberingService numbers, IRecordRegistry records, UserManager<ApplicationUser> users, INotifier notifier, ComplianceMappingService mappings)
{
    public const string ProjectType = "QualityProject", PpapType = "Ppap", CapabilityType = "CapabilityStudy";
    public static readonly string[] PhaseNames = ["", "Plan and define program", "Product design and development", "Process design and development", "Product and process validation", "Launch", "Feedback, assessment and corrective action"];

    /// <summary>Default deliverables per phase (generic APQP outputs); transfers add their own items. Editable per project.</summary>
    static IEnumerable<(int Phase, string Name)> Template(QualityProjectKind k)
    {
        (int, string)[] common =
        [
            (1, "Design and quality goals"), (1, "Preliminary bill of materials"), (1, "Preliminary process flow"), (1, "Preliminary special characteristics"), (1, "Product assurance plan"),
            (2, "Design FMEA"), (2, "Design verification plan"), (2, "Engineering drawings and specifications"), (2, "Prototype control plan"),
            (3, "Process flow chart"), (3, "Process FMEA"), (3, "Pre-launch control plan"), (3, "Process instructions"), (3, "Measurement system analysis plan"),
            (4, "Significant production run"), (4, "Measurement system evaluation"), (4, "Preliminary process capability study"), (4, "PPAP approval"), (4, "Production control plan"),
            (5, "Quality planning sign-off"), (5, "Production launch"),
            (6, "Lessons learned"), (6, "Customer satisfaction review")
        ];
        var extra = k switch
        {
            QualityProjectKind.SupplierTransfer => new[] { (1, "New supplier qualification"), (3, "Supplier audit"), (4, "First-lot AQL inspection"), (4, "Supplier approval") },
            QualityProjectKind.ProductTransfer or QualityProjectKind.ManufacturingTransfer => [(1, "Transfer plan and receiving-site readiness"), (3, "Receiving-site training"), (4, "Equipment and process validation"), (5, "Sending-site closeout")],
            _ => Array.Empty<(int, string)>()
        };
        return common.Concat(extra).OrderBy(x => x.Item1);
    }

    public async Task<QualityProject> CreateProjectAsync(ProjectInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.ApqpEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.Title, "Title", 300);
        if (r.Kind == QualityProjectKind.SupplierTransfer && r.SupplierId is null) throw new AppException("A supplier transfer needs the new supplier.");
        if (r.Kind is QualityProjectKind.ProductTransfer or QualityProjectKind.ManufacturingTransfer && r.TargetSiteId is null) throw new AppException("A transfer needs the receiving site.");
        if (r.SupplierId is { } s && !await db.Suppliers.AnyAsync(x => x.Id == s, ct)) throw new AppException("Select an existing supplier.");
        if (r.CustomerId is { } c && !await db.Customers.AnyAsync(x => x.Id == c, ct)) throw new AppException("Select an existing customer.");
        if (r.PartId is { } p && !await db.Parts.AnyAsync(x => x.Id == p, ct)) throw new AppException("Select an existing part.");
        if (r.ProductId is { } pr && !await db.Products.AnyAsync(x => x.Id == pr, ct)) throw new AppException("Select an existing product.");
        if (r.TargetSiteId is { } ts && !await db.Sites.AnyAsync(x => x.Id == ts, ct)) throw new AppException("Select an existing receiving site.");
        if (r.OwnerId is { } o) await m.RequireActiveUserAsync(o, "owner", ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var pj = new QualityProject { Number = await numbers.NextAsync(ProjectType, r.SiteId, ct), Title = r.Title.Trim(), Kind = r.Kind, SiteId = r.SiteId, DepartmentId = r.DepartmentId, OwnerId = r.OwnerId ?? m.UserId,
            ProductId = r.ProductId, PartId = r.PartId, CustomerId = r.CustomerId, SupplierId = r.SupplierId, TargetSiteId = r.TargetSiteId, TargetLaunchOn = r.TargetLaunchOn };
        db.QualityProjects.Add(pj);
        db.ProjectDeliverables.AddRange(Template(r.Kind).Select(t => new ProjectDeliverable { ProjectId = pj.Id, Phase = t.Phase, Name = t.Name, OwnerId = pj.OwnerId }));
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(ProjectType, pj.Id, pj.Title, ct);
        await tx.CommitAsync(ct);
        return pj;
    }

    public async Task<List<QualityProject>> ProjectsAsync(CancellationToken ct) =>
        await (await m.ScopeAsync(db.QualityProjects.AsNoTracking(), Permissions.ApqpView, ct)).OrderByDescending(p => p.CreatedAt).Take(500).ToListAsync(ct);

    public async Task<object> ProjectAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(ProjectType, id, ct);
        var p = await db.QualityProjects.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var dels = await db.ProjectDeliverables.AsNoTracking().Where(d => d.ProjectId == id).OrderBy(d => d.Phase).ThenBy(d => d.CreatedAt).ToListAsync(ct);
        var gates = await db.PhaseGates.AsNoTracking().Where(g => g.ProjectId == id).OrderBy(g => g.DecidedAt).ToListAsync(ct);
        var ppaps = await db.PpapSubmissions.AsNoTracking().Where(x => x.ProjectId == id).Select(x => new { x.Id, x.Number, x.Level, Status = x.Status.ToString() }).ToListAsync(ct);
        return new { Project = p, Kind = p.Kind.ToString(), Phases = Enumerable.Range(1, 6).Select(i => new { Phase = i, Name = PhaseNames[i] }), Deliverables = dels, Gates = gates, Ppaps = ppaps, Readiness = Readiness(p, dels) };
    }

    static object Readiness(QualityProject p, List<ProjectDeliverable> dels)
    {
        var byPhase = Enumerable.Range(1, 6).Select(i =>
        {
            var d = dels.Where(x => x.Phase == i).ToList();
            var done = d.Count(x => x.Status != DeliverableStatus.Open);
            return new { Phase = i, Name = PhaseNames[i], Total = d.Count, Done = done, Pct = d.Count == 0 ? 100m : Math.Round(100m * done / d.Count, 1), Overdue = d.Count(x => x.Status == DeliverableStatus.Open && x.DueOn < DateOnly.FromDateTime(DateTime.UtcNow)) };
        }).ToList();
        var all = dels.Count; var finished = dels.Count(x => x.Status != DeliverableStatus.Open);
        return new { OverallPct = all == 0 ? 100m : Math.Round(100m * finished / all, 1), CurrentPhase = p.CurrentPhase, ByPhase = byPhase,
            Rating = byPhase.Where(x => x.Phase <= p.CurrentPhase).Any(x => x.Overdue > 0) ? "Red" : byPhase.First(x => x.Phase == p.CurrentPhase).Pct >= 80 ? "Green" : "Yellow" };
    }

    public async Task<object> ReadinessReportAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(ProjectType, id, ct);
        var p = await db.QualityProjects.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var dels = await db.ProjectDeliverables.AsNoTracking().Where(d => d.ProjectId == id).ToListAsync(ct);
        return new { p.Number, p.Title, Kind = p.Kind.ToString(), Readiness = Readiness(p, dels), Open = dels.Where(d => d.Status == DeliverableStatus.Open).OrderBy(d => d.Phase).Select(d => new { d.Phase, d.Name, d.DueOn }) };
    }

    async Task<QualityProject> EditProjectAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(ProjectType, id, ct);
        var p = await db.QualityProjects.FirstAsync(x => x.Id == id, ct);
        if (p.Status is QualityProjectStatus.Completed or QualityProjectStatus.Cancelled) throw new ConflictException($"The project is {p.Status}.");
        return p;
    }

    public async Task<ProjectDeliverable> AddDeliverableAsync(Guid id, DeliverableInput r, CancellationToken ct)
    {
        await EditProjectAsync(id, ct);
        if (r.Phase is < 1 or > 6) throw new AppException("Phase must be 1–6.");
        ModuleSupport.Required(r.Name, "Name", 300);
        if (r.OwnerId is { } o) await m.RequireActiveUserAsync(o, "owner", ct);
        var d = new ProjectDeliverable { ProjectId = id, Phase = r.Phase, Name = r.Name.Trim(), DueOn = r.DueOn, OwnerId = r.OwnerId };
        db.ProjectDeliverables.Add(d);
        await db.SaveChangesAsync(ct);
        return d;
    }

    public async Task UpdateDeliverableAsync(Guid id, Guid deliverableId, DeliverableUpdate r, CancellationToken ct)
    {
        await EditProjectAsync(id, ct);
        var d = await db.ProjectDeliverables.FirstOrDefaultAsync(x => x.Id == deliverableId && x.ProjectId == id, ct) ?? throw new NotFoundException("Deliverable");
        if (r.LinkedType is { Length: > 0 } t && r.LinkedId is { } lid)
        {
            var target = await records.RequireViewAsync(t, lid, ct);
            d.LinkedType = target.Type; d.LinkedId = target.Id;
            if (!await db.RecordLinks.AnyAsync(l => l.FromType == ProjectType && l.FromId == id && l.ToType == target.Type && l.ToId == target.Id, ct))
                db.RecordLinks.Add(new RecordLink { FromType = ProjectType, FromId = id, ToType = target.Type, ToId = target.Id, LinkType = "Deliverable: " + d.Name });
        }
        d.Status = r.Status;
        d.CompletedAt = r.Status == DeliverableStatus.Open ? null : m.Now;
        await db.SaveChangesAsync(ct);
    }

    async Task<ApplicationUser> ReauthAsync(string password, string what, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(m.UserId.ToString()) ?? throw new ForbiddenException();
        if (string.IsNullOrEmpty(password) || !await users.CheckPasswordAsync(user, password)) throw new AppException($"Password is incorrect. The {what} was not signed.", 401);
        return user;
    }

    /// <summary>E-signed phase gate. Go needs every deliverable of the phase done or not applicable; Conditional needs notes and carries the open items forward.</summary>
    public async Task GateAsync(Guid id, GateInput r, CancellationToken ct)
    {
        // Gate reviewers need view + approve; they do not need edit rights on the project.
        await m.ViewAsync(ProjectType, id, ct);
        var p = await db.QualityProjects.FirstAsync(x => x.Id == id, ct);
        if (p.Status is QualityProjectStatus.Completed or QualityProjectStatus.Cancelled) throw new ConflictException($"The project is {p.Status}.");
        await m.RequireAsync(Permissions.ApqpApprove, p.SiteId, p.DepartmentId, ct);
        var open = await db.ProjectDeliverables.Where(d => d.ProjectId == id && d.Phase == p.CurrentPhase && d.Status == DeliverableStatus.Open).Select(d => d.Name).ToListAsync(ct);
        if (r.Decision == GateDecision.Go && open.Count > 0) throw new AppException($"Phase {p.CurrentPhase} still has open deliverables: {string.Join(", ", open)}.");
        if (r.Decision != GateDecision.Go && string.IsNullOrWhiteSpace(r.Notes)) throw new AppException("Notes are required for a conditional or no-go decision.");
        var user = await ReauthAsync(r.Password, "gate decision", ct);
        db.PhaseGates.Add(new PhaseGate { ProjectId = id, Phase = p.CurrentPhase, Decision = r.Decision, DecidedById = user.Id, DecidedByName = user.DisplayName, DecidedAt = m.Now, Notes = r.Notes?.Trim() });
        if (r.Decision != GateDecision.NoGo)
        {
            if (p.CurrentPhase == 6) p.Status = QualityProjectStatus.Completed; else p.CurrentPhase++;
        }
        await db.SaveChangesAsync(ct);
    }

    // ---------- PPAP ----------
    public static readonly (int Code, string Name, string? LinkType)[] PpapElements =
    [
        (1, "Design records", "Drawing"), (2, "Authorized engineering change documents", "ChangeRequest"), (3, "Customer engineering approval", null), (4, "Design FMEA", null),
        (5, "Process flow diagram", "ProcessFlow"), (6, "Process FMEA", "Pfmea"), (7, "Control plan", "ControlPlan"), (8, "Measurement system analysis studies", "MsaStudy"),
        (9, "Dimensional results", "Fai"), (10, "Records of material / performance test results", null), (11, "Initial process studies", CapabilityType), (12, "Qualified laboratory documentation", null),
        (13, "Appearance approval report", null), (14, "Sample production parts", null), (15, "Master sample", null), (16, "Checking aids", null),
        (17, "Customer-specific requirements", null), (18, "Part submission warrant", null)
    ];

    /// <summary>Submission requirement by level, from the AIAG PPAP Table 4.2 matrix in <see cref="PpapMatrix"/>.</summary>
    public static PpapRequirement RequirementFor(int level, int code) => PpapMatrix.RequirementFor(level, code);

    public async Task<PpapSubmission> CreatePpapAsync(PpapInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.ApqpEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        if (r.Level is < 1 or > 5) throw new AppException("PPAP level must be 1–5.");
        ModuleSupport.Required(r.Reason, "Reason for submission", 300);
        if (!await db.Parts.AnyAsync(x => x.Id == r.PartId, ct)) throw new AppException("Select an existing part.");
        if (r.CustomerId is { } c && !await db.Customers.AnyAsync(x => x.Id == c, ct)) throw new AppException("Select an existing customer.");
        if (r.ProjectId is { } pj) await m.ViewAsync(ProjectType, pj, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var s = new PpapSubmission { Number = await numbers.NextAsync(PpapType, r.SiteId, ct), PartId = r.PartId, PartRevision = r.PartRevision?.Trim(), CustomerId = r.CustomerId, ProjectId = r.ProjectId, Level = r.Level,
            Reason = r.Reason.Trim(), SiteId = r.SiteId, DepartmentId = r.DepartmentId, OwnerId = m.UserId };
        db.PpapSubmissions.Add(s);
        db.PpapElements.AddRange(PpapElements.Select(e => new PpapElement { SubmissionId = s.Id, Code = e.Code, Name = e.Name, Requirement = RequirementFor(r.Level, e.Code) }));
        if (r.ProjectId is { } pid) db.RecordLinks.Add(new RecordLink { FromType = ProjectType, FromId = pid, ToType = PpapType, ToId = s.Id, LinkType = "PPAP" });
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(PpapType, s.Id, s.Reason, ct);
        await tx.CommitAsync(ct);
        return s;
    }

    public async Task<List<PpapSubmission>> PpapsAsync(CancellationToken ct) =>
        await (await m.ScopeAsync(db.PpapSubmissions.AsNoTracking(), Permissions.ApqpView, ct)).OrderByDescending(p => p.CreatedAt).Take(500).ToListAsync(ct);

    public async Task<object> PpapAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(PpapType, id, ct);
        var s = await db.PpapSubmissions.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var els = await db.PpapElements.AsNoTracking().Where(e => e.SubmissionId == id).OrderBy(e => e.Code).ToListAsync(ct);
        var part = await db.Parts.AsNoTracking().Where(p => p.Id == s.PartId).Select(p => new { p.PartNumber, p.Name }).FirstOrDefaultAsync(ct);
        return new { Submission = s, Status = s.Status.ToString(), Part = part, Elements = els.Select(e => new { e.Id, e.Code, e.Name, Requirement = e.Requirement.ToString(), RequirementCode = PpapMatrix.CodeOf(e.Requirement),
            Condition = PpapMatrix.Rows.First(x => x.ElementCode == e.Code).Condition, Status = e.Status.ToString(), e.LinkedType, e.LinkedId, e.Notes,
            SuggestedType = PpapElements.First(x => x.Code == e.Code).LinkType }),
            Matrix = PpapMatrix.Rows.Select(x => new { x.Key, x.ElementCode, x.Name, x.Condition, Levels = Enumerable.Range(1, 5).Select(x.CodeFor).ToArray() }),
            Legend = PpapMatrix.Legend.Select(x => new { x.Code, x.Meaning }), MatrixVerification = (await mappings.StatusesAsync(ct)).GetValueOrDefault(ComplianceMappingService.PpapKey) };
    }

    public async Task UpdateElementAsync(Guid id, int code, PpapElementUpdate r, CancellationToken ct)
    {
        await m.EditAsync(PpapType, id, ct);
        var s = await db.PpapSubmissions.FirstAsync(x => x.Id == id, ct);
        if (s.Status is not (PpapStatus.Draft or PpapStatus.Rejected)) throw new ConflictException($"The submission is {s.Status}.");
        var e = await db.PpapElements.FirstOrDefaultAsync(x => x.SubmissionId == id && x.Code == code, ct) ?? throw new NotFoundException("Element");
        if (r.Status == PpapElementStatus.NotApplicable && code == 18) throw new AppException("The part submission warrant is always required.");
        if (r.LinkedType is { Length: > 0 } t && r.LinkedId is { } lid)
        {
            var target = await records.RequireViewAsync(t, lid, ct);
            e.LinkedType = target.Type; e.LinkedId = target.Id;
            if (!await db.RecordLinks.AnyAsync(l => l.FromType == PpapType && l.FromId == id && l.ToType == target.Type && l.ToId == target.Id, ct))
                db.RecordLinks.Add(new RecordLink { FromType = PpapType, FromId = id, ToType = target.Type, ToId = target.Id, LinkType = $"PPAP element {code}" });
        }
        if (r.Status == PpapElementStatus.Ready && e.LinkedId is null && string.IsNullOrWhiteSpace(r.Notes ?? e.Notes) && PpapElements.First(x => x.Code == code).LinkType is not null)
            throw new AppException($"Link the {e.Name.ToLowerInvariant()} record or add a note describing the evidence.");
        e.Status = r.Status; e.Notes = r.Notes?.Trim() ?? e.Notes;
        await db.SaveChangesAsync(ct);
    }

    public async Task SubmitPpapAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(PpapType, id, ct);
        var s = await db.PpapSubmissions.FirstAsync(x => x.Id == id, ct);
        if (s.Status is not (PpapStatus.Draft or PpapStatus.Rejected)) throw new ConflictException($"The submission is {s.Status}.");
        var missing = await db.PpapElements.Where(e => e.SubmissionId == id && e.Status == PpapElementStatus.Missing && (e.Code == 18 || e.Requirement == PpapRequirement.Submit || e.Requirement == PpapRequirement.SubmitOnRequest)).Select(e => e.Code + " " + e.Name).ToListAsync(ct);
        if (missing.Count > 0) throw new AppException("Elements still missing: " + string.Join("; ", missing));
        s.Status = PpapStatus.Submitted; s.SubmittedAt = m.Now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Records the customer's decision (approved, interim or rejected), e-signed by the person entering it. Approval completes the linked project's PPAP deliverable.</summary>
    public async Task CustomerDecisionAsync(Guid id, CustomerDecisionInput r, CancellationToken ct)
    {
        var info = await m.ViewAsync(PpapType, id, ct);
        await m.RequireAsync(Permissions.ApqpApprove, info.SiteId, info.DepartmentId, ct);
        var s = await db.PpapSubmissions.FirstAsync(x => x.Id == id, ct);
        if (s.Status != PpapStatus.Submitted) throw new ConflictException("Only a submitted PPAP can receive the customer's decision.");
        if (r.Decision is not (PpapStatus.Approved or PpapStatus.InterimApproved or PpapStatus.Rejected)) throw new AppException("The decision must be approved, interim approval or rejected.");
        ModuleSupport.Required(r.CustomerName, "Customer representative", 200);
        if (r.Decision != PpapStatus.Approved && string.IsNullOrWhiteSpace(r.Comments)) throw new AppException("Record the customer's comments for an interim approval or rejection.");
        await ReauthAsync(r.Password, "customer decision", ct);
        s.Status = r.Decision; s.CustomerDecisionBy = r.CustomerName.Trim(); s.CustomerDecisionOn = r.DecidedOn; s.CustomerComments = r.Comments?.Trim();
        if (r.Decision == PpapStatus.Approved && s.ProjectId is { } pid)
            foreach (var d in await db.ProjectDeliverables.Where(d => d.ProjectId == pid && d.Name == "PPAP approval" && d.Status == DeliverableStatus.Open).ToListAsync(ct))
            { d.Status = DeliverableStatus.Done; d.CompletedAt = m.Now; d.LinkedType = PpapType; d.LinkedId = s.Id; }
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([s.OwnerId], $"PPAP {s.Number}: customer {r.Decision}", r.Comments, info, ct: ct);
    }

    // ---------- capability ----------
    public static CapabilityStudy Compute(CapabilityStudy c)
    {
        if (c.Values.Count < 2) throw new AppException("Enter at least two measurements.");
        if (c.Lsl is null && c.Usl is null) throw new AppException("Enter at least one specification limit.");
        if (c.Lsl is { } l && c.Usl is { } u && l >= u) throw new AppException("The lower limit must be below the upper limit.");
        if (c.SubgroupSize is < 1 or > 10) throw new AppException("Subgroup size must be 1–10.");
        if (c.SubgroupSize > 1 && c.Values.Count % c.SubgroupSize != 0) throw new AppException($"The number of measurements must be a multiple of the subgroup size ({c.SubgroupSize}).");
        var groups = c.SubgroupSize == 1 ? c.Values.Select(v => new SpcSubgroup([v])).ToList() : c.Values.Chunk(c.SubgroupSize).Select(g => new SpcSubgroup(g.ToList())).ToList();
        var res = SpcMath.Compute(c.SubgroupSize == 1 ? SpcChartType.IndividualsMR : SpcChartType.XBarR, groups, c.Lsl, c.Usl);
        var cap = res.Capability!;
        c.Mean = cap.Mean; c.SigmaWithin = cap.SigmaWithin; c.SigmaOverall = cap.SigmaOverall; c.Cp = cap.Cp; c.Cpk = cap.Cpk; c.Pp = cap.Pp; c.Ppk = cap.Ppk;
        return c;
    }

    public async Task<CapabilityStudy> CreateCapabilityAsync(CapabilityInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.ApqpEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.Title, "Title", 300); ModuleSupport.Required(r.Characteristic, "Characteristic", 300);
        if (r.Values.Count > 5000) throw new AppException("A study is limited to 5000 measurements.");
        if (r.ControlPlanId is { } cp && !await db.ControlPlans.AnyAsync(x => x.Id == cp, ct)) throw new AppException("Select an existing control plan.");
        if (r.PartId is { } p && !await db.Parts.AnyAsync(x => x.Id == p, ct)) throw new AppException("Select an existing part.");
        if (r.ProductId is { } pr && !await db.Products.AnyAsync(x => x.Id == pr, ct)) throw new AppException("Select an existing product.");
        var c = Compute(new CapabilityStudy { Title = r.Title.Trim(), Characteristic = r.Characteristic.Trim(), SiteId = r.SiteId, DepartmentId = r.DepartmentId, ProductId = r.ProductId, PartId = r.PartId,
            ControlPlanId = r.ControlPlanId, Process = r.Process?.Trim(), Lsl = r.Lsl, Usl = r.Usl, SubgroupSize = r.SubgroupSize, Values = r.Values, RequiredCpk = r.RequiredCpk });
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        c.Number = await numbers.NextAsync(CapabilityType, r.SiteId, ct);
        db.CapabilityStudies.Add(c);
        if (r.ControlPlanId is { } cpid) db.RecordLinks.Add(new RecordLink { FromType = ApqpService.PlanType, FromId = cpid, ToType = CapabilityType, ToId = c.Id, LinkType = "Capability study" });
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(CapabilityType, c.Id, c.Characteristic, ct);
        await tx.CommitAsync(ct);
        return c;
    }

    public async Task<List<CapabilityStudy>> CapabilityStudiesAsync(Guid? partId, CancellationToken ct) =>
        await (await m.ScopeAsync(db.CapabilityStudies.AsNoTracking(), Permissions.ApqpView, ct)).Where(c => partId == null || c.PartId == partId).OrderByDescending(c => c.CreatedAt).Take(500).ToListAsync(ct);
}
