using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Enterprise;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Enterprise;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Engineering;

public sealed record RequirementInput(string Title, string Text, RequirementSource Source, Guid SiteId, Guid? DepartmentId, Guid? ProductId, Guid? CustomerId, string? Reference, Guid? OwnerId);
public sealed record RequirementRevisionInput(string Title, string Text, string Reason);
public sealed record TraceInput(string Target, string RecordType, Guid RecordId);
public sealed record TraceRow(string Target, string RecordType, Guid RecordId, string Number, string Title, string Status, string Url);
public sealed record DesignReviewInput(string Title, DesignReviewKind Kind, Guid SiteId, Guid? DepartmentId, Guid? ProductId, Guid? ProjectId, Guid? ChairId);
public sealed record FindingInput(string Text, bool IsRisk);
public sealed record HoldReviewInput(DateOnly HeldOn, string Decision);
public sealed record VariantInput(string Code, string Name, List<string>? Options, List<string>? Features, string? ManufacturingImpact);
public sealed record BomLineInput(Guid ComponentPartId, string? ComponentRevision, decimal Quantity, string? Operation, Guid? SupplierId);
public sealed record BomIssue(Guid ComponentPartId, string Component, string Severity, string Issue);

/// <summary>
/// Engineering quality: product requirements with immutable revision history and traceability (shared record links),
/// design reviews (findings, shared action items, shared approval engine), product variants and engineering/manufacturing BOM validation.
/// Design FMEAs are PFMEA documents of type Design (ApqpService) and verification/validation plans are validation projects (ValidationService).
/// </summary>
public sealed class EngineeringService(AppDbContext db, ModuleSupport m, INumberingService numbers, WorkflowService workflow, ActionService actions, IRecordRegistry registry)
{
    public const string RequirementType = "Requirement", ReviewType = "DesignReview";
    /// <summary>Trace targets and the record types each accepts.</summary>
    public static readonly Dictionary<string, string[]> TraceTargets = new()
    {
        ["Product"] = ["Product", "Part"], ["Drawing"] = ["Drawing"], ["Specification"] = ["Document"],
        ["Test"] = [ValidationService.RecordType], ["Validation"] = [ValidationService.RecordType], ["Release"] = [ProjectService.ProjectType, ProjectService.PpapType],
        ["Risk"] = [Apqp.ApqpService.PfmeaType], ["Change"] = ["ChangeRequest"], ["Complaint"] = ["Complaint"],
    };

    // ---------- requirements ----------
    public async Task<ProductRequirement> CreateRequirementAsync(RequirementInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.EngineeringEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.Title, "Title", 300); ModuleSupport.Required(r.Text, "Requirement text", 8000);
        if (r.Source == RequirementSource.Customer && r.CustomerId is null && string.IsNullOrWhiteSpace(r.Reference)) throw new AppException("A customer requirement needs the customer or the customer document reference.");
        if (r.ProductId is { } p && !await db.Products.AnyAsync(x => x.Id == p, ct)) throw new AppException("Select an existing product.");
        if (r.CustomerId is { } c && !await db.Customers.AnyAsync(x => x.Id == c, ct)) throw new AppException("Select an existing customer.");
        if (r.OwnerId is { } o) await m.RequireActiveUserAsync(o, "owner", ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var q = new ProductRequirement { Number = await numbers.NextAsync(RequirementType, r.SiteId, ct), Title = r.Title.Trim(), Text = r.Text.Trim(), Source = r.Source, SiteId = r.SiteId, DepartmentId = r.DepartmentId,
            ProductId = r.ProductId, CustomerId = r.CustomerId, Reference = r.Reference?.Trim(), OwnerId = r.OwnerId ?? m.UserId };
        db.ProductRequirements.Add(q);
        db.RequirementRevisions.Add(new RequirementRevision { RequirementId = q.Id, Revision = 1, Title = q.Title, Text = q.Text, Reason = "Initial", ChangedById = m.UserId });
        if (r.ProductId is { } pid) db.RecordLinks.Add(new RecordLink { FromType = RequirementType, FromId = q.Id, ToType = "Product", ToId = pid, LinkType = "Traces: Product" });
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RequirementType, q.Id, q.Text, ct);
        await tx.CommitAsync(ct);
        return q;
    }

    public async Task<List<ProductRequirement>> RequirementsAsync(Guid? productId, CancellationToken ct) =>
        await (await m.ScopeAsync(db.ProductRequirements.AsNoTracking(), Permissions.EngineeringView, ct)).Where(x => productId == null || x.ProductId == productId).OrderBy(x => x.Number).Take(1000).ToListAsync(ct);

    /// <summary>A new revision keeps the old text in the immutable history and resets verification and validation, which must be shown again.</summary>
    public async Task ReviseRequirementAsync(Guid id, RequirementRevisionInput r, CancellationToken ct)
    {
        await m.EditAsync(RequirementType, id, ct);
        var q = await db.ProductRequirements.FirstAsync(x => x.Id == id, ct);
        if (q.Status == RequirementStatus.Obsolete) throw new ConflictException("The requirement is obsolete.");
        ModuleSupport.Required(r.Title, "Title", 300); ModuleSupport.Required(r.Text, "Requirement text", 8000); ModuleSupport.Required(r.Reason, "Reason for the revision", 2000);
        q.Revision++; q.Title = r.Title.Trim(); q.Text = r.Text.Trim(); q.Status = RequirementStatus.Draft;
        q.Verification = VerificationState.NotStarted; q.Validation = VerificationState.NotStarted;
        db.RequirementRevisions.Add(new RequirementRevision { RequirementId = id, Revision = q.Revision, Title = q.Title, Text = q.Text, Reason = r.Reason.Trim(), ChangedById = m.UserId });
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RequirementType, q.Id, q.Text, ct);
    }

    public async Task ApproveRequirementAsync(Guid id, CancellationToken ct)
    {
        var info = await m.ViewAsync(RequirementType, id, ct);
        var q = await db.ProductRequirements.FirstAsync(x => x.Id == id, ct);
        await m.RequireAsync(Permissions.EngineeringApprove, q.SiteId, q.DepartmentId, ct);
        if (q.Status != RequirementStatus.Draft) throw new ConflictException($"The requirement is {q.Status}.");
        q.Status = RequirementStatus.Approved;
        await db.SaveChangesAsync(ct);
    }

    public async Task TraceAsync(Guid id, TraceInput r, CancellationToken ct)
    {
        await m.EditAsync(RequirementType, id, ct);
        if (!TraceTargets.TryGetValue(r.Target, out var types)) throw new AppException($"Trace target must be one of: {string.Join(", ", TraceTargets.Keys)}.");
        if (!types.Contains(r.RecordType)) throw new AppException($"A {r.Target.ToLowerInvariant()} trace links a {string.Join(" or ", types)} record.");
        await m.ViewAsync(r.RecordType, r.RecordId, ct);
        if (r.RecordType == ValidationService.RecordType)
        {
            var kind = await db.ValidationProjects.Where(v => v.Id == r.RecordId).Select(v => v.Kind).FirstAsync(ct);
            if (r.Target == "Test" && kind != ValidationKind.DesignVerification) throw new AppException("A test trace links a design verification plan.");
            if (r.Target == "Validation" && kind != ValidationKind.DesignValidation) throw new AppException("A validation trace links a design validation plan.");
        }
        var lt = "Traces: " + r.Target;
        if (await db.RecordLinks.AnyAsync(l => l.FromType == RequirementType && l.FromId == id && l.ToType == r.RecordType && l.ToId == r.RecordId && l.LinkType == lt, ct)) throw new ConflictException("Already traced.");
        db.RecordLinks.Add(new RecordLink { FromType = RequirementType, FromId = id, ToType = r.RecordType, ToId = r.RecordId, LinkType = lt });
        await db.SaveChangesAsync(ct);
        await RefreshStatusAsync([id], ct);
    }

    public async Task UntraceAsync(Guid id, Guid linkId, CancellationToken ct)
    {
        await m.EditAsync(RequirementType, id, ct);
        var l = await db.RecordLinks.FirstOrDefaultAsync(x => x.Id == linkId && x.FromType == RequirementType && x.FromId == id, ct) ?? throw new NotFoundException("Trace");
        db.RecordLinks.Remove(l);
        await db.SaveChangesAsync(ct);
        await RefreshStatusAsync([id], ct);
    }

    /// <summary>Verification follows the linked design verification plans, validation the linked design validation plans: Passed when all are released, Failed when a test failed, otherwise in progress.</summary>
    async Task RefreshStatusAsync(List<Guid> ids, CancellationToken ct)
    {
        var links = await db.RecordLinks.AsNoTracking().Where(l => l.FromType == RequirementType && ids.Contains(l.FromId) && l.ToType == ValidationService.RecordType).ToListAsync(ct);
        var vids = links.Select(l => l.ToId).Distinct().ToList();
        var vals = await db.ValidationProjects.AsNoTracking().Where(v => vids.Contains(v.Id)).Select(v => new { v.Id, v.Status, Failed = db.ValidationTests.Any(t => t.ProjectId == v.Id && t.Passed == false) }).ToListAsync(ct);
        VerificationState S(IEnumerable<Guid> vs)
        {
            var l = vals.Where(v => vs.Contains(v.Id)).ToList();
            if (l.Count == 0) return VerificationState.NotStarted;
            if (l.All(v => v.Status == ValidationStatus.Released)) return VerificationState.Passed;
            return l.Any(v => v.Failed) ? VerificationState.Failed : VerificationState.InProgress;
        }
        foreach (var q in await db.ProductRequirements.Where(x => ids.Contains(x.Id)).ToListAsync(ct))
        {
            q.Verification = S(links.Where(l => l.FromId == q.Id && l.LinkType == "Traces: Test").Select(l => l.ToId));
            q.Validation = S(links.Where(l => l.FromId == q.Id && l.LinkType == "Traces: Validation").Select(l => l.ToId));
        }
        await db.SaveChangesAsync(ct);
    }

    async Task<List<(Guid LinkId, Guid From, TraceRow Row)>> TracesAsync(List<Guid> ids, CancellationToken ct)
    {
        var links = await db.RecordLinks.AsNoTracking().Where(l => l.FromType == RequirementType && ids.Contains(l.FromId) && l.LinkType.StartsWith("Traces: ")).ToListAsync(ct);
        var list = new List<(Guid, Guid, TraceRow)>();
        foreach (var l in links)
        {
            RecordInfo? info = null;
            try { info = await registry.RequireViewAsync(l.ToType, l.ToId, ct); } catch (AppException) { }
            list.Add((l.Id, l.FromId, new TraceRow(l.LinkType[8..], l.ToType, l.ToId, info?.Number ?? "—", info?.Title ?? "(not visible)", info?.Status ?? "", info?.Url ?? "")));
        }
        return list;
    }

    public async Task<object> RequirementAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(RequirementType, id, ct);
        await RefreshStatusAsync([id], ct);
        var q = await db.ProductRequirements.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var revs = await db.RequirementRevisions.AsNoTracking().Where(x => x.RequirementId == id).OrderByDescending(x => x.Revision).ToListAsync(ct);
        var traces = await TracesAsync([id], ct);
        return new { Requirement = q, Revisions = revs, Traces = traces.Select(t => new { Id = t.LinkId, t.Row.Target, t.Row.RecordType, t.Row.RecordId, t.Row.Number, t.Row.Title, t.Row.Status, t.Row.Url }),
            Coverage = TraceTargets.Keys.Take(6).ToDictionary(k => k, k => traces.Any(t => t.Row.Target == k)) };
    }

    /// <summary>Traceability report: one row per requirement with the records for each target and the gaps.</summary>
    public async Task<object> TraceReportAsync(Guid? productId, CancellationToken ct)
    {
        var reqs = await RequirementsAsync(productId, ct);
        var ids = reqs.Select(r => r.Id).ToList();
        await RefreshStatusAsync(ids, ct);
        reqs = await RequirementsAsync(productId, ct);
        var traces = await TracesAsync(ids, ct);
        string[] cols = ["Product", "Drawing", "Specification", "Test", "Validation", "Release"];
        var rows = reqs.Select(r => new
        {
            r.Id, r.Number, r.Title, r.Revision, Source = r.Source.ToString(), Status = r.Status.ToString(), Verification = r.Verification.ToString(), Validation = r.Validation.ToString(),
            Links = cols.ToDictionary(c => c, c => traces.Where(t => t.From == r.Id && t.Row.Target == c).Select(t => t.Row.Number).ToList()),
            Gaps = cols.Where(c => !traces.Any(t => t.From == r.Id && t.Row.Target == c)).ToList()
        }).ToList();
        return new { Columns = cols, Rows = rows, Total = rows.Count, FullyTraced = rows.Count(r => r.Gaps.Count == 0), Verified = rows.Count(r => r.Verification == "Passed"), Validated = rows.Count(r => r.Validation == "Passed") };
    }

    // ---------- design reviews ----------
    public async Task<DesignReview> CreateReviewAsync(DesignReviewInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.EngineeringEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.Title, "Title", 300);
        if (r.ProductId is null && r.ProjectId is null) throw new AppException("A design review covers a product or a launch project.");
        if (r.ProductId is { } p && !await db.Products.AnyAsync(x => x.Id == p, ct)) throw new AppException("Select an existing product.");
        if (r.ProjectId is { } pj && !await db.QualityProjects.AnyAsync(x => x.Id == pj, ct)) throw new AppException("Select an existing project.");
        if (r.ChairId is { } c) await m.RequireActiveUserAsync(c, "chair", ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var d = new DesignReview { Number = await numbers.NextAsync(ReviewType, r.SiteId, ct), Title = r.Title.Trim(), Kind = r.Kind, SiteId = r.SiteId, DepartmentId = r.DepartmentId, ProductId = r.ProductId, ProjectId = r.ProjectId, ChairId = r.ChairId ?? m.UserId };
        db.DesignReviews.Add(d);
        if (r.ProjectId is { } pid) db.RecordLinks.Add(new RecordLink { FromType = ProjectService.ProjectType, FromId = pid, ToType = ReviewType, ToId = d.Id, LinkType = "Design review" });
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(ReviewType, d.Id, d.Title, ct);
        await tx.CommitAsync(ct);
        return d;
    }

    public async Task<List<DesignReview>> ReviewsAsync(CancellationToken ct) =>
        await (await m.ScopeAsync(db.DesignReviews.AsNoTracking(), Permissions.EngineeringView, ct)).OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct);

    public async Task<object> ReviewAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(ReviewType, id, ct);
        var d = await db.DesignReviews.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var f = await db.DesignReviewFindings.AsNoTracking().Where(x => x.ReviewId == id).OrderBy(x => x.CreatedAt).ToListAsync(ct);
        var a = await db.ActionItems.AsNoTracking().Where(x => x.SourceType == ReviewType && x.SourceId == id).OrderBy(x => x.DueDate).ToListAsync(ct);
        var ap = await db.ApprovalRequests.AsNoTracking().Include(x => x.Decisions).Where(x => x.RecordType == ReviewType && x.RecordId == id).OrderBy(x => x.CreatedAt).ToListAsync(ct);
        return new { Review = d, Kind = d.Kind.ToString(), Status = d.Status.ToString(), Findings = f, Actions = a, Approvals = ap, OpenRisks = f.Count(x => x.IsRisk && !x.Resolved) };
    }

    async Task<DesignReview> EditReviewAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(ReviewType, id, ct);
        var d = await db.DesignReviews.FirstAsync(x => x.Id == id, ct);
        if (d.Status is not (DesignReviewStatus.Planned or DesignReviewStatus.Held or DesignReviewStatus.Rejected)) throw new ConflictException($"The design review is {d.Status}.");
        return d;
    }

    public async Task<DesignReviewFinding> AddFindingAsync(Guid id, FindingInput r, CancellationToken ct)
    {
        await EditReviewAsync(id, ct);
        ModuleSupport.Required(r.Text, "Finding", 4000);
        var f = new DesignReviewFinding { ReviewId = id, Text = r.Text.Trim(), IsRisk = r.IsRisk };
        db.DesignReviewFindings.Add(f);
        await db.SaveChangesAsync(ct);
        return f;
    }

    public async Task ResolveFindingAsync(Guid id, Guid findingId, CancellationToken ct)
    {
        await EditReviewAsync(id, ct);
        var f = await db.DesignReviewFindings.FirstOrDefaultAsync(x => x.Id == findingId && x.ReviewId == id, ct) ?? throw new NotFoundException("Finding");
        f.Resolved = true;
        await db.SaveChangesAsync(ct);
    }

    public async Task<ActionItem> AddActionAsync(Guid id, CreateActionRequest r, CancellationToken ct)
    {
        await EditReviewAsync(id, ct);
        return await actions.CreateAsync(ReviewType, id, r, ct);
    }

    public async Task HoldAsync(Guid id, HoldReviewInput r, CancellationToken ct)
    {
        var d = await EditReviewAsync(id, ct);
        ModuleSupport.Required(r.Decision, "Decision", 4000);
        d.HeldOn = r.HeldOn; d.Decision = r.Decision.Trim(); d.Status = DesignReviewStatus.Held;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Submitting for approval needs the review held and every risk finding resolved or covered by an action.</summary>
    public async Task SubmitReviewAsync(Guid id, CancellationToken ct)
    {
        var d = await EditReviewAsync(id, ct);
        if (d.Status != DesignReviewStatus.Held) throw new ConflictException("Record the review meeting and decision before submitting.");
        var risks = await db.DesignReviewFindings.Where(x => x.ReviewId == id && x.IsRisk && !x.Resolved).CountAsync(ct);
        var acts = await db.ActionItems.CountAsync(x => x.SourceType == ReviewType && x.SourceId == id && x.Status != ActionStatus.Cancelled, ct);
        if (risks > 0 && acts == 0) throw new AppException($"{risks} open risk finding(s) need an action item or resolution before approval.");
        await workflow.SubmitAsync(ReviewType, id, ct);
        d.Status = DesignReviewStatus.PendingApproval;
        await db.SaveChangesAsync(ct);
    }

    public async Task OnReviewApprovedAsync(Guid id, CancellationToken ct)
    {
        var d = await db.DesignReviews.FirstAsync(x => x.Id == id, ct);
        d.Status = DesignReviewStatus.Approved;
        await db.SaveChangesAsync(ct);
    }

    public async Task OnReviewRejectedAsync(Guid id, string? comment, CancellationToken ct)
    {
        var d = await db.DesignReviews.FirstAsync(x => x.Id == id, ct);
        d.Status = DesignReviewStatus.Rejected;
        await db.SaveChangesAsync(ct);
    }

    // ---------- configuration ----------
    async Task RequireProductEditAsync(Guid productId, CancellationToken ct)
    {
        var p = await db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == productId, ct) ?? throw new NotFoundException("Product");
        await m.RequireAsync(Permissions.EngineeringEdit, p.SiteId, p.DepartmentId, ct);
    }

    public async Task<List<ProductVariant>> VariantsAsync(Guid productId, CancellationToken ct)
    {
        await m.ViewAsync("Product", productId, ct);
        return await db.ProductVariants.AsNoTracking().Where(v => v.ProductId == productId).OrderBy(v => v.Code).ToListAsync(ct);
    }

    public async Task<ProductVariant> SaveVariantAsync(Guid productId, Guid? id, VariantInput r, CancellationToken ct)
    {
        await RequireProductEditAsync(productId, ct);
        ModuleSupport.Required(r.Code, "Code", 60); ModuleSupport.Required(r.Name, "Name", 200);
        var code = r.Code.Trim().ToUpperInvariant();
        if (await db.ProductVariants.AnyAsync(v => v.ProductId == productId && v.Code == code && v.Id != id, ct)) throw new ConflictException($"Variant {code} already exists for this product.");
        ProductVariant v;
        if (id is { } vid)
        {
            v = await db.ProductVariants.FirstOrDefaultAsync(x => x.Id == vid && x.ProductId == productId, ct) ?? throw new NotFoundException("Variant");
            v.Revision++;
        }
        else { v = new ProductVariant { ProductId = productId }; db.ProductVariants.Add(v); }
        v.Code = code; v.Name = r.Name.Trim(); v.Options = (r.Options ?? []).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList();
        v.Features = (r.Features ?? []).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList(); v.ManufacturingImpact = r.ManufacturingImpact?.Trim();
        await db.SaveChangesAsync(ct);
        return v;
    }

    // ---------- BOM quality ----------
    public async Task<object> BomAsync(Guid partId, BomType type, CancellationToken ct)
    {
        await m.ViewAsync("Part", partId, ct);
        var lines = await db.BomLines.AsNoTracking().Where(b => b.ParentPartId == partId && b.BomType == type)
            .Select(b => new { b.Id, b.ComponentPartId, Component = db.Parts.Where(p => p.Id == b.ComponentPartId).Select(p => p.PartNumber).FirstOrDefault(), b.ComponentRevision, b.Quantity, b.Operation, b.SupplierId,
                Supplier = db.Suppliers.Where(s => s.Id == b.SupplierId).Select(s => s.LegalName).FirstOrDefault(), b.Source, b.UpdatedAt }).ToListAsync(ct);
        return new { Type = type.ToString(), Lines = lines, Issues = await ValidateBomAsync(partId, type, ct) };
    }

    public async Task SaveBomAsync(Guid partId, BomType type, List<BomLineInput> lines, CancellationToken ct)
    {
        var part = await db.Parts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == partId, ct) ?? throw new NotFoundException("Part");
        await m.RequireAsync(Permissions.EngineeringEdit, part.SiteId, part.DepartmentId, ct);
        if (lines.GroupBy(l => l.ComponentPartId).Any(g => g.Count() > 1)) throw new AppException("Each component appears once per BOM.");
        foreach (var l in lines)
        {
            if (l.ComponentPartId == partId) throw new AppException("A part cannot contain itself.");
            if (l.Quantity <= 0) throw new AppException("Quantity must be greater than zero.");
        }
        db.BomLines.RemoveRange(await db.BomLines.Where(b => b.ParentPartId == partId && b.BomType == type && b.Source == RecordSource.Local).ToListAsync(ct));
        db.BomLines.AddRange(lines.Select(l => new BomLine { ParentPartId = partId, ComponentPartId = l.ComponentPartId, ComponentRevision = l.ComponentRevision?.Trim(), Quantity = l.Quantity, Operation = l.Operation?.Trim(), SupplierId = l.SupplierId, BomType = type, Source = RecordSource.Local }));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Checks every line: the part exists and is active, the revision is a released revision of the part, the source supplier is approved; the manufacturing BOM must match the engineering BOM.</summary>
    public async Task<List<BomIssue>> ValidateBomAsync(Guid partId, BomType type, CancellationToken ct)
    {
        var lines = await db.BomLines.AsNoTracking().Where(b => b.ParentPartId == partId && b.BomType == type).ToListAsync(ct);
        var comp = lines.Select(l => l.ComponentPartId).ToList();
        var parts = await db.Parts.AsNoTracking().Where(p => comp.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var revs = await db.PartRevisions.AsNoTracking().Where(r => comp.Contains(r.PartId)).Select(r => new { r.PartId, r.Revision }).ToListAsync(ct);
        var sids = lines.Where(l => l.SupplierId != null).Select(l => l.SupplierId!.Value).ToList();
        var sups = await db.Suppliers.AsNoTracking().Where(s => sids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var issues = new List<BomIssue>();
        foreach (var l in lines)
        {
            var name = parts.TryGetValue(l.ComponentPartId, out var p) ? p.PartNumber : l.ComponentPartId.ToString();
            if (p is null) { issues.Add(new(l.ComponentPartId, name, "Error", "Part number does not exist.")); continue; }
            if (p.Status != Domain.MasterData.MasterStatus.Active) issues.Add(new(l.ComponentPartId, name, "Error", $"Part is {p.Status}."));
            if (l.ComponentRevision is { Length: > 0 } rv && !revs.Any(r => r.PartId == p.Id && r.Revision == rv) && p.Revision != rv) issues.Add(new(l.ComponentPartId, name, "Error", $"Revision {rv} is not a revision of {name}."));
            if (l.ComponentRevision is null or "") issues.Add(new(l.ComponentPartId, name, "Warning", "No revision specified."));
            if (l.SupplierId is { } sid)
            {
                if (!sups.TryGetValue(sid, out var s)) issues.Add(new(l.ComponentPartId, name, "Error", "Source supplier does not exist."));
                else if (s.Status is not (Domain.MasterData.SupplierStatus.Approved or Domain.MasterData.SupplierStatus.Conditional)) issues.Add(new(l.ComponentPartId, name, "Error", $"Source supplier {s.LegalName} is {s.Status}, not approved."));
            }
        }
        if (type == BomType.Manufacturing)
        {
            var ebom = await db.BomLines.AsNoTracking().Where(b => b.ParentPartId == partId && b.BomType == BomType.Engineering).ToListAsync(ct);
            if (ebom.Count > 0)
            {
                foreach (var e in ebom.Where(e => !lines.Any(l => l.ComponentPartId == e.ComponentPartId)))
                    issues.Add(new(e.ComponentPartId, parts.GetValueOrDefault(e.ComponentPartId)?.PartNumber ?? await db.Parts.Where(x => x.Id == e.ComponentPartId).Select(x => x.PartNumber).FirstOrDefaultAsync(ct) ?? "?", "Error", "In the engineering BOM but missing from the manufacturing BOM."));
                foreach (var l in lines)
                    if (ebom.FirstOrDefault(e => e.ComponentPartId == l.ComponentPartId) is { } e && (e.Quantity != l.Quantity || (e.ComponentRevision ?? "") != (l.ComponentRevision ?? "")))
                        issues.Add(new(l.ComponentPartId, parts.GetValueOrDefault(l.ComponentPartId)?.PartNumber ?? "?", "Warning", "Quantity or revision differs from the engineering BOM."));
            }
        }
        return issues;
    }

    /// <summary>BOM change history from the shared audit trail.</summary>
    public async Task<object> BomHistoryAsync(Guid partId, CancellationToken ct)
    {
        await m.ViewAsync("Part", partId, ct);
        var ids = await db.BomLines.AsNoTracking().Where(b => b.ParentPartId == partId).Select(b => b.Id).ToListAsync(ct);
        var key = partId.ToString();
        var rows = await db.AuditEntries.AsNoTracking().Where(a => a.EntityType == nameof(BomLine)).OrderByDescending(a => a.OccurredAt).Take(5000).ToListAsync(ct);
        return rows.Where(a => ids.Contains(a.EntityId) || (a.Changes != null && a.Changes.Contains(key))).Take(200).ToList();
    }
}
