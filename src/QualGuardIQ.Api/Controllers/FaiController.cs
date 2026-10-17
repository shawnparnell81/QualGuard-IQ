using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Api.Contracts;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Fai;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;
using static QualGuardIQ.Api.Contracts.FaiAqlMapper;

namespace QualGuardIQ.Api.Controllers;

public sealed record IdsRequest(IReadOnlyList<Guid> Ids, string? Notes = null, string? Password = null);
public sealed record ReasonRequest(string Reason);
public sealed record NotesRequest(string? Notes);
public sealed record DecideFaiRequest(string Password, string? Comment);
public sealed record ReturnFaiRequest(string Notes, string? Password = null);
public sealed record ReleaseRequest(string CustomerApprovalReference, DateOnly CustomerApprovedOn);
public sealed record CopyRequest(FaiType? Type);
public sealed record CloneRequest(IReadOnlyList<int>? ChangedBalloons);
public sealed record CreateNcrFromRequest(IReadOnlyList<Guid>? CharacteristicIds, string? Title);
public sealed record CreateCapaFromRequest(string Title, string ProblemStatement);
public sealed record ImportCsvRequest(string Csv);
public sealed record BalloonMapRequest(string Revision, IReadOnlyList<CharacteristicInput2> Balloons);
public sealed record FaiSummaryDto(Guid Id, string Number, string Title, FaiType Type, FaiReason Reason, FaiStatus Status, int FaiRevision, string PartNumber, string? PartRevision, string? DrawingNumber,
    string? DrawingRevision, string? SupplierCode, string? CustomerName, DateOnly? DueOn, bool Overdue, string InspectorName, string? ReviewerName, string SiteCode, int Characteristics, int Passed, int Failed, DateTimeOffset CreatedAt);

/// <summary>
/// First article inspection API (v1). Permissions: fai.view to read, fai.edit to create/inspect/review, fai.approve to sign approval steps,
/// release, close, reopen. Records outside the caller's sites/departments return 404. Attachments, comments, links, approvals and audit
/// come from the shared engines; bulk actions run the single-record action per id with the same gates.
/// </summary>
[ApiController, Authorize, Route("api/v1/fai"), Tags("FAI")]
[ProducesResponseType<ProblemDetails>(400, "application/problem+json"), ProducesResponseType<ProblemDetails>(404, "application/problem+json"), ProducesResponseType<ProblemDetails>(409, "application/problem+json"),
 ProducesResponseType<ProblemDetails>(422, "application/problem+json"), ProducesResponseType<ProblemDetails>(500, "application/problem+json")]
public sealed class FaiController(AppDbContext db, FaiService svc, ModuleSupport m, AttachmentService files) : ControllerBase
{
    // ---------- helpers ----------

    private async Task<IQueryable<FaiRecord>> Scoped(CancellationToken ct) => await m.ScopeAsync(db.Fais.AsNoTracking(), Permissions.FaiView, ct);

    private async Task<PagedFAI> Paged(IQueryable<FaiRecord> q, int page, int pageSize, CancellationToken ct)
    {
        var (p, n) = Clamp(page, pageSize);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(x => x.CreatedAt).Skip((p - 1) * n).Take(n).ToListAsync(ct);
        return new PagedFAI(items.Select(ToDto).ToList(), Meta(p, n, total));
    }

    private async Task<(DateTimeOffset? From, DateTimeOffset? To)> RangeAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (from is not null && to is not null && from > to) throw new AppException("dateFrom must be on or before dateTo.");
        var zone = await m.ZoneAsync(null, ct);
        DateTimeOffset At(DateOnly d) { var l = d.ToDateTime(TimeOnly.MinValue); return new DateTimeOffset(l, zone.GetUtcOffset(l)).ToUniversalTime(); }
        return (from is { } f ? At(f) : null, to is { } t ? At(t.AddDays(1)) : null);
    }

    private async Task<FAI> Dto(Guid faiId, CancellationToken ct) => ToDto(await db.Fais.AsNoTracking().FirstAsync(f => f.Id == faiId, ct));

    private async Task<FaiRecord> ExpectAsync(Guid faiId, long? version, CancellationToken ct)
    {
        await m.ViewAsync(FaiService.RecordType, faiId, ct);
        var x = await db.Fais.AsNoTracking().FirstAsync(f => f.Id == faiId, ct);
        if (version is { } v && v != x.RowVersion) throw new ConflictException("The FAI was changed by someone else; reload it and try again.");
        return x;
    }

    private static string Password(string? pw) => string.IsNullOrEmpty(pw) ? throw new AppException("This action is an electronic signature: enter your password.") : pw;

    // ---------- Management ----------

    /// <summary>List FAIs in the caller's scope. search: number, title, part, drawing, lot, serial. status: contract status. dateFrom/dateTo: creation date (organization-local days).</summary>
    [HttpGet(Name = "listFai")] [RequirePermission(Permissions.FaiView)]
    public async Task<PagedFAI> List([FromQuery] int page = 1, [FromQuery] int pageSize = 25, [FromQuery] string? search = null, [FromQuery] FaiStatusCode? status = null,
        [FromQuery] DateOnly? dateFrom = null, [FromQuery] DateOnly? dateTo = null, CancellationToken ct = default)
    {
        var q = await Scoped(ct);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var t = $"%{search.Trim()}%";
            q = q.Where(x => EF.Functions.ILike(x.Number + " " + x.Title + " " + x.PartNumber + " " + (x.DrawingNumber ?? "") + " " + (x.LotNumber ?? "") + " " + (x.SerialNumber ?? ""), t));
        }
        if (status is { } s)
        {
            var st = FaiAqlMapper.Domain(s);
            q = q.Where(x => st.Contains(x.Status));
            if (s == FaiStatusCode.Reopened) q = q.Where(x => x.FaiRevision > 1);
            if (s == FaiStatusCode.InspectionInProgress) q = q.Where(x => x.FaiRevision == 1);
        }
        var (f, to) = await RangeAsync(dateFrom, dateTo, ct);
        if (f is not null) q = q.Where(x => x.CreatedAt >= f);
        if (to is not null) q = q.Where(x => x.CreatedAt < to);
        return await Paged(q, page, pageSize, ct);
    }

    [HttpGet("open", Name = "listOpenFai")] [RequirePermission(Permissions.FaiView)]
    public async Task<PagedFAI> Open([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) => await Paged((await Scoped(ct)).Where(x => (int)x.Status <= 4), page, pageSize, ct);
    [HttpGet("overdue", Name = "listOverdueFai")] [RequirePermission(Permissions.FaiView)]
    public async Task<PagedFAI> Overdue([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    { var today = await m.TodayAsync(null, ct); return await Paged((await Scoped(ct)).Where(x => (int)x.Status <= 4 && x.DueOn < today), page, pageSize, ct); }
    [HttpGet("approval-pending", Name = "listApprovalPendingFai")] [RequirePermission(Permissions.FaiView)]
    public async Task<PagedFAI> ApprovalPending([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) => await Paged((await Scoped(ct)).Where(x => x.Status == FaiStatus.PendingApproval), page, pageSize, ct);
    [HttpGet("closed", Name = "listClosedFai")] [RequirePermission(Permissions.FaiView)]
    public async Task<PagedFAI> Closed([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) => await Paged((await Scoped(ct)).Where(x => x.Status == FaiStatus.Closed), page, pageSize, ct);

    /// <summary>Extension: summary rows (names, counts) filtered by internal fields, for list screens.</summary>
    [HttpGet("filter")] [RequirePermission(Permissions.FaiView)]
    public async Task<object> Filter([FromQuery] FaiStatus? status, [FromQuery] FaiType? type, [FromQuery] FaiReason? reason, [FromQuery] Guid? partId, [FromQuery] Guid? supplierId, [FromQuery] Guid? customerId,
        [FromQuery] Guid? drawingId, [FromQuery] Guid? siteId, [FromQuery] DateOnly? dueBefore, [FromQuery] string? search, [FromQuery] bool overdue = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var q = await Scoped(ct);
        var today = await m.TodayAsync(null, ct);
        if (status is not null) q = q.Where(x => x.Status == status);
        if (type is not null) q = q.Where(x => x.Type == type);
        if (reason is not null) q = q.Where(x => x.Reason == reason);
        if (partId is not null) q = q.Where(x => x.PartId == partId);
        if (supplierId is not null) q = q.Where(x => x.SupplierId == supplierId);
        if (customerId is not null) q = q.Where(x => x.CustomerId == customerId);
        if (drawingId is not null) q = q.Where(x => x.DrawingId == drawingId);
        if (siteId is not null) q = q.Where(x => x.SiteId == siteId);
        if (dueBefore is not null) q = q.Where(x => x.DueOn <= dueBefore);
        if (overdue) q = q.Where(x => (int)x.Status <= 4 && x.DueOn < today);
        if (!string.IsNullOrWhiteSpace(search)) { var t = $"%{search.Trim()}%"; q = q.Where(x => EF.Functions.ILike(x.Number + " " + x.Title + " " + x.PartNumber + " " + (x.DrawingNumber ?? "") + " " + (x.LotNumber ?? "") + " " + (x.SerialNumber ?? ""), t)); }
        return await Paging.PageAsync(q.OrderByDescending(x => x.CreatedAt).Select(x => new FaiSummaryDto(x.Id, x.Number, x.Title, x.Type, x.Reason, x.Status, x.FaiRevision, x.PartNumber, x.PartRevision, x.DrawingNumber, x.DrawingRevision,
            db.Suppliers.Where(s => s.Id == x.SupplierId).Select(s => s.Code).FirstOrDefault(), db.Customers.Where(c => c.Id == x.CustomerId).Select(c => c.Name).FirstOrDefault(), x.DueOn,
            x.DueOn < today && (int)x.Status <= 4, db.Users.Where(u => u.Id == x.InspectorId).Select(u => u.DisplayName).First(), db.Users.Where(u => u.Id == x.ReviewerId).Select(u => u.DisplayName).FirstOrDefault(),
            db.Sites.Where(s => s.Id == x.SiteId).Select(s => s.Code).First(), db.FaiCharacteristics.Count(c => c.FaiId == x.Id), db.FaiCharacteristics.Count(c => c.FaiId == x.Id && c.Result == CharResult.Pass),
            db.FaiCharacteristics.Count(c => c.FaiId == x.Id && c.Result == CharResult.Fail), x.CreatedAt)), page, pageSize, ct);
    }

    /// <summary>Extension: FAI types, reasons, statuses and characteristic classes.</summary>
    [HttpGet("types")] [RequirePermission(Permissions.FaiView)]
    public object Types() => new { Types = Enum.GetNames<FaiTypeCode>(), TypeDetails = Enum.GetNames<FaiType>(), Reasons = Enum.GetNames<FaiReason>(), Statuses = Enum.GetNames<FaiStatusCode>(), CharacteristicTypes = Enum.GetNames<CharacteristicTypeCode>(), DataTypes = Enum.GetNames<DataTypeCode>() };

    [HttpGet("{faiId:guid}", Name = "getFai")] [RequirePermission(Permissions.FaiView)]
    public async Task<FAI> Get(Guid faiId, CancellationToken ct) { await m.ViewAsync(FaiService.RecordType, faiId, ct); return await Dto(faiId, ct); }

    /// <summary>Extension: the full working view (characteristics, accountability, drawings, revisions, names and allowed actions) used by the FAI workspace.</summary>
    [HttpGet("{faiId:guid}/detail")] [RequirePermission(Permissions.FaiView)]
    public async Task<object> Detail(Guid faiId, CancellationToken ct)
    {
        await m.ViewAsync(FaiService.RecordType, faiId, ct);
        var x = await db.Fais.AsNoTracking().FirstAsync(f => f.Id == faiId, ct);
        var acc = await svc.AccountabilityAsync(faiId, ct);
        var canEdit = await m.CanAsync(Permissions.FaiEdit, x.SiteId, x.DepartmentId, ct);
        var canApprove = await m.CanAsync(Permissions.FaiApprove, x.SiteId, x.DepartmentId, ct);
        var allowed = new List<string>();
        if (canEdit)
        {
            if (x.Status is FaiStatus.Draft or FaiStatus.InProgress or FaiStatus.Returned) allowed.AddRange(["edit", "characteristics"]);
            if (x.Status is FaiStatus.Draft or FaiStatus.Returned or FaiStatus.Rejected) allowed.Add("start-inspection");
            if (x.Status == FaiStatus.InProgress) allowed.AddRange(["results", "complete-inspection", "submit"]);
            if (x.Status == FaiStatus.Review && x.ReviewerId == m.UserId) allowed.AddRange(["request-approval", "return"]);
            if (x.Status == FaiStatus.Draft) allowed.Add("delete");
            if (acc.UnresolvedFailures.Count > 0) allowed.Add("ncr");
            allowed.AddRange(["copy", "clone", "capa"]);
        }
        if (canApprove)
        {
            if (x.Status == FaiStatus.PendingApproval) allowed.AddRange(["approve", "reject", "approval-return"]);
            if (x.Status == FaiStatus.Approved) allowed.AddRange(["release", "close"]);
            if (x.Status is FaiStatus.Closed or FaiStatus.Approved or FaiStatus.Rejected) allowed.Add("reopen");
        }
        return new
        {
            Fai = ToDto(x),
            Part = new { x.PartNumber, x.PartRevision }, Drawing = new { x.DrawingNumber, x.DrawingRevision }, x.Specification, x.Process, x.ReviewNotes, x.CustomerApprovalReference, x.CustomerApprovedOn, x.ClosedAt,
            Site = await db.Sites.Where(s => s.Id == x.SiteId).Select(s => new { s.Id, s.Code, s.Name }).FirstOrDefaultAsync(ct),
            Product = await db.Products.Where(p => p.Id == x.ProductId).Select(p => new { p.Id, p.Name }).FirstOrDefaultAsync(ct),
            Supplier = await db.Suppliers.Where(s => s.Id == x.SupplierId).Select(s => new { s.Id, s.Code, s.LegalName }).FirstOrDefaultAsync(ct),
            Customer = await db.Customers.Where(c => c.Id == x.CustomerId).Select(c => new { c.Id, c.Name }).FirstOrDefaultAsync(ct),
            Change = await db.ChangeRequests.Where(c => c.Id == x.ChangeId).Select(c => new { c.Id, c.Number, c.Title }).FirstOrDefaultAsync(ct),
            InspectorName = await db.Users.Where(u => u.Id == x.InspectorId).Select(u => u.DisplayName).FirstAsync(ct),
            ReviewerName = await db.Users.Where(u => u.Id == x.ReviewerId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct),
            Characteristics = (await db.FaiCharacteristics.AsNoTracking().Where(c => c.FaiId == faiId).OrderBy(c => c.BalloonNumber).ToListAsync(ct)).Select(ToDto),
            BalloonDrawings = await BalloonList(faiId, ct),
            Revisions = await db.FaiRevisionHistory.AsNoTracking().Where(h => h.FaiId == faiId).OrderBy(h => h.FaiRevision).Select(h => new { h.FaiRevision, h.Reason, h.DrawingRevision, h.CreatedAt }).ToListAsync(ct),
            Accountability = acc,
            Approvals = await ApprovalList(faiId, ct),
            Allowed = allowed
        };
    }

    private async Task CheckProductAsync(FAICreate r, CancellationToken ct)
    {
        var product = await db.Parts.AsNoTracking().Where(p => p.Id == r.PartId).Select(p => p.ProductId).FirstOrDefaultAsync(ct);
        if (product is not null && product != r.ProductId) throw new AppException("productId does not match the product of the part.");
    }

    private async Task<ActionResult<FAI>> CreateCore(FAICreate r, FaiType? exact, CancellationToken ct)
    {
        await CheckProductAsync(r, ct);
        var partNumber = await db.Parts.AsNoTracking().Where(p => p.Id == r.PartId).Select(p => p.PartNumber).FirstOrDefaultAsync(ct);
        var input = ToInput(r, partNumber: partNumber);
        var x = await svc.CreateAsync(exact is { } t ? input with { Type = t } : input, ct);
        return CreatedAtRoute("getFai", new { faiId = x.Id }, await Dto(x.Id, ct));
    }

    /// <summary>Create an FAI. Requires fai.edit at the site.</summary>
    [HttpPost(Name = "createFai")] [RequirePermission(Permissions.FaiEdit)] [ProducesResponseType<FAI>(StatusCodes.Status201Created)]
    public Task<ActionResult<FAI>> Create(FAICreate r, CancellationToken ct) => CreateCore(r, null, ct);

    // Extension: typed creates keep the finer internal FAI types (engineering/process/material/tooling/supplier change).
    [HttpPost("full")] [RequirePermission(Permissions.FaiEdit)] [ProducesResponseType<FAI>(201)] public Task<ActionResult<FAI>> Full(FAICreate r, CancellationToken ct) => CreateCore(r, FaiType.Full, ct);
    [HttpPost("partial")] [RequirePermission(Permissions.FaiEdit)] [ProducesResponseType<FAI>(201)] public Task<ActionResult<FAI>> Partial(FAICreate r, CancellationToken ct) => CreateCore(r, FaiType.Partial, ct);
    [HttpPost("delta")] [RequirePermission(Permissions.FaiEdit)] [ProducesResponseType<FAI>(201)] public Task<ActionResult<FAI>> Delta(FAICreate r, CancellationToken ct) => CreateCore(r, FaiType.Delta, ct);
    [HttpPost("engineering-change")] [RequirePermission(Permissions.FaiEdit)] [ProducesResponseType<FAI>(201)] public Task<ActionResult<FAI>> EngineeringChange(FAICreate r, CancellationToken ct) => CreateCore(r, FaiType.EngineeringChange, ct);
    [HttpPost("process-change")] [RequirePermission(Permissions.FaiEdit)] [ProducesResponseType<FAI>(201)] public Task<ActionResult<FAI>> ProcessChange(FAICreate r, CancellationToken ct) => CreateCore(r, FaiType.ProcessChange, ct);
    [HttpPost("material-change")] [RequirePermission(Permissions.FaiEdit)] [ProducesResponseType<FAI>(201)] public Task<ActionResult<FAI>> MaterialChange(FAICreate r, CancellationToken ct) => CreateCore(r, FaiType.MaterialChange, ct);
    [HttpPost("tooling-change")] [RequirePermission(Permissions.FaiEdit)] [ProducesResponseType<FAI>(201)] public Task<ActionResult<FAI>> ToolingChange(FAICreate r, CancellationToken ct) => CreateCore(r, FaiType.ToolingChange, ct);
    [HttpPost("supplier-change")] [RequirePermission(Permissions.FaiEdit)] [ProducesResponseType<FAI>(201)] public Task<ActionResult<FAI>> SupplierChange(FAICreate r, CancellationToken ct) => CreateCore(r, FaiType.SupplierChange, ct);
    [HttpPost("requalification")] [RequirePermission(Permissions.FaiEdit)] [ProducesResponseType<FAI>(201)] public Task<ActionResult<FAI>> Requalification(FAICreate r, CancellationToken ct) => CreateCore(r, FaiType.Requalification, ct);

    /// <summary>Update an editable FAI. version must match the current version (409 otherwise).</summary>
    [HttpPut("{faiId:guid}", Name = "updateFai")] [RequirePermission(Permissions.FaiEdit)]
    public async Task<FAI> Update(Guid faiId, FAIUpdate r, CancellationToken ct)
    {
        var x = await svc.LoadEditAsync(faiId, ct);
        await CheckProductAsync(r, ct);
        await svc.UpdateAsync(faiId, ToInput(r, x.Title, x.PartNumber, (uint)r.Version, x.Type), ct);
        return await Dto(faiId, ct);
    }

    /// <summary>Deletes (cancels) a draft without results; history is never removed.</summary>
    [HttpDelete("{faiId:guid}", Name = "deleteFai")] [RequirePermission(Permissions.FaiEdit)]
    public async Task<IActionResult> Delete(Guid faiId, CancellationToken ct) { await svc.CancelAsync(faiId, ct); return NoContent(); }

    [HttpPost("{faiId:guid}/copy")] [RequirePermission(Permissions.FaiEdit)] [ProducesResponseType<FAI>(201)]
    public async Task<ActionResult<FAI>> Copy(Guid faiId, CopyRequest? r, CancellationToken ct) { var x = await svc.CopyAsync(faiId, r?.Type, ct); return CreatedAtRoute("getFai", new { faiId = x.Id }, await Dto(x.Id, ct)); }
    [HttpPost("{faiId:guid}/clone")] [RequirePermission(Permissions.FaiEdit)] [ProducesResponseType<FAI>(201)]
    public async Task<ActionResult<FAI>> Clone(Guid faiId, CloneRequest? r, CancellationToken ct) { var x = await svc.CloneAsync(faiId, r?.ChangedBalloons, ct); return CreatedAtRoute("getFai", new { faiId = x.Id }, await Dto(x.Id, ct)); }

    // ---------- Workflow ----------

    [HttpPost("{faiId:guid}/start-inspection", Name = "start_inspectionFai")] [RequirePermission(Permissions.FaiEdit)] [Tags("FAI Workflow")]
    public async Task<FAI> StartInspection(Guid faiId, WorkflowAction r, CancellationToken ct) { await ExpectAsync(faiId, r.ExpectedVersion, ct); await svc.StartInspectionAsync(faiId, r.Comment, ct); return await Dto(faiId, ct); }

    [HttpPost("{faiId:guid}/complete-inspection", Name = "complete_inspectionFai")] [RequirePermission(Permissions.FaiEdit)] [Tags("FAI Workflow")]
    public async Task<FAI> CompleteInspection(Guid faiId, WorkflowAction r, CancellationToken ct) { await ExpectAsync(faiId, r.ExpectedVersion, ct); await svc.CompleteInspectionAsync(faiId, ct); return await Dto(faiId, ct); }

    /// <summary>Inspector submits the completed FAI for review. assigneeId sets (or changes) the reviewer, who must not be the inspector.</summary>
    [HttpPost("{faiId:guid}/submit", Name = "submitFai")] [RequirePermission(Permissions.FaiEdit)] [Tags("FAI Workflow")]
    public async Task<FAI> Submit(Guid faiId, WorkflowAction r, CancellationToken ct)
    {
        await ExpectAsync(faiId, r.ExpectedVersion, ct);
        if (r.AssigneeId is { } rv) await svc.AssignReviewerAsync(faiId, rv, ct);
        await svc.SubmitAsync(faiId, ct);
        return await Dto(faiId, ct);
    }

    /// <summary>Extension: reviewer accepts and starts the approval route (same as POST /approvals).</summary>
    [HttpPost("{faiId:guid}/review")] [RequirePermission(Permissions.FaiEdit)] [Tags("FAI Workflow")]
    public async Task<FAI> Review(Guid faiId, WorkflowAction r, CancellationToken ct) { await ExpectAsync(faiId, r.ExpectedVersion, ct); await svc.ReviewAsync(faiId, r.Comment, ct); return await Dto(faiId, ct); }

    /// <summary>Return for correction. In review: the assigned reviewer returns it. Pending approval: an approver returns it with a signed "not approved" decision (password required).</summary>
    [HttpPost("{faiId:guid}/return", Name = "returnFai")] [RequirePermission(Permissions.FaiEdit)] [Tags("FAI Workflow")]
    public async Task<FAI> Return(Guid faiId, WorkflowAction r, CancellationToken ct)
    {
        var x = await ExpectAsync(faiId, r.ExpectedVersion, ct);
        if (x.Status == FaiStatus.PendingApproval) await svc.ReturnFromApprovalAsync(faiId, Password(r.Password), r.Comment, ct);
        else await svc.ReturnAsync(faiId, r.Comment, ct);
        return await Dto(faiId, ct);
    }

    /// <summary>Sign the current approval step (e-signature; password required). The step's permission is checked (fai.approve by default).</summary>
    [HttpPost("{faiId:guid}/approve", Name = "approveFai")] [RequirePermission(Permissions.FaiApprove)] [Tags("FAI Workflow")]
    public async Task<FAI> Approve(Guid faiId, WorkflowAction r, CancellationToken ct) { await ExpectAsync(faiId, r.ExpectedVersion, ct); await svc.DecideAsync(faiId, true, Password(r.Password), r.Comment, ct); return await Dto(faiId, ct); }

    [HttpPost("{faiId:guid}/reject", Name = "rejectFai")] [RequirePermission(Permissions.FaiApprove)] [Tags("FAI Workflow")]
    public async Task<FAI> Reject(Guid faiId, WorkflowAction r, CancellationToken ct) { await ExpectAsync(faiId, r.ExpectedVersion, ct); await svc.DecideAsync(faiId, false, Password(r.Password), r.Comment, ct); return await Dto(faiId, ct); }

    /// <summary>Records the customer's approval on an approved FAI: reasonCode (or the comment) is the customer approval reference, dated today.</summary>
    [HttpPost("{faiId:guid}/release", Name = "releaseFai")] [RequirePermission(Permissions.FaiApprove)] [Tags("FAI Workflow")]
    public async Task<FAI> Release(Guid faiId, WorkflowAction r, CancellationToken ct)
    {
        var x = await ExpectAsync(faiId, r.ExpectedVersion, ct);
        await svc.ReleaseAsync(faiId, string.IsNullOrWhiteSpace(r.ReasonCode) ? r.Comment : r.ReasonCode, await m.TodayAsync(x.SiteId, ct), ct);
        return await Dto(faiId, ct);
    }

    /// <summary>Extension: release with an explicit customer approval date.</summary>
    [HttpPost("{faiId:guid}/customer-approval")] [RequirePermission(Permissions.FaiApprove)] [Tags("FAI Workflow")]
    public async Task<FAI> CustomerApproval(Guid faiId, ReleaseRequest r, CancellationToken ct) { await svc.ReleaseAsync(faiId, r.CustomerApprovalReference, r.CustomerApprovedOn, ct); return await Dto(faiId, ct); }

    [HttpPost("{faiId:guid}/close", Name = "closeFai")] [RequirePermission(Permissions.FaiApprove)] [Tags("FAI Workflow")]
    public async Task<FAI> Close(Guid faiId, WorkflowAction r, CancellationToken ct) { await ExpectAsync(faiId, r.ExpectedVersion, ct); await svc.CloseAsync(faiId, r.Comment, ct); return await Dto(faiId, ct); }

    [HttpPost("{faiId:guid}/reopen", Name = "reopenFai")] [RequirePermission(Permissions.FaiApprove)] [Tags("FAI Workflow")]
    public async Task<FAI> Reopen(Guid faiId, WorkflowAction r, CancellationToken ct) { await ExpectAsync(faiId, r.ExpectedVersion, ct); await svc.ReopenAsync(faiId, r.Comment, ct); return await Dto(faiId, ct); }

    // ---------- History ----------

    private static async Task<PagedAuditEvents> PageEvents(List<AuditEvent> all, int page, int pageSize)
    {
        var (p, n) = Clamp(page, pageSize);
        await Task.CompletedTask;
        return new PagedAuditEvents(all.Skip((p - 1) * n).Take(n).ToList(), Meta(p, n, all.Count));
    }

    /// <summary>Every audited change on the FAI, its characteristics, results, ballooned drawings and revisions, oldest first.</summary>
    [HttpGet("{faiId:guid}/audit-history", Name = "getFaiAuditHistory")] [RequirePermission(Permissions.FaiView)] [Tags("FAI Workflow")]
    public async Task<PagedAuditEvents> AuditHistory(Guid faiId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        await m.ViewAsync(FaiService.RecordType, faiId, ct);
        var ids = new List<Guid> { faiId };
        ids.AddRange(await db.FaiCharacteristics.Where(c => c.FaiId == faiId).Select(c => c.Id).ToListAsync(ct));
        ids.AddRange(await db.FaiCharacteristicResults.Where(c => c.FaiId == faiId).Select(c => c.Id).ToListAsync(ct));
        ids.AddRange(await db.FaiBalloonDrawings.Where(c => c.FaiId == faiId).Select(c => c.Id).ToListAsync(ct));
        ids.AddRange(await db.FaiRevisionHistory.Where(c => c.FaiId == faiId).Select(c => c.Id).ToListAsync(ct));
        var rows = await db.AuditEntries.AsNoTracking().Where(e => ids.Contains(e.EntityId)).OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).ToListAsync(ct);
        return await PageEvents(rows.Select(ToDto).ToList(), page, pageSize);
    }

    /// <summary>Status changes and approval signatures, oldest first. Signature events carry the decision id and the signature meaning as reason.</summary>
    [HttpGet("{faiId:guid}/workflow-history", Name = "getFaiWorkflowHistory")] [RequirePermission(Permissions.FaiView)] [Tags("FAI Workflow")]
    public async Task<PagedAuditEvents> WorkflowHistory(Guid faiId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        await m.ViewAsync(FaiService.RecordType, faiId, ct);
        var status = (await db.AuditEntries.AsNoTracking().Where(e => e.EntityId == faiId && e.EntityType == nameof(FaiRecord) && (e.Action == "Created" || (e.Changes != null && e.Changes.Contains("\"Status\""))))
            .ToListAsync(ct)).Select(ToDto).Select(e => e with { Action = e.Action == "Created" ? "Created" : "StatusChanged" });
        var signatures = await db.ApprovalDecisions.AsNoTracking().Join(db.ApprovalRequests.Where(r => r.RecordType == FaiService.RecordType && r.RecordId == faiId), d => d.RequestId, r => r.Id, (d, r) => d).ToListAsync(ct);
        var sig = signatures.Select(d => new AuditEvent(d.Id, d.Approved ? "Approved" : "NotApproved", d.SignerId, d.SignedAt, null,
            System.Text.Json.JsonSerializer.SerializeToElement(new { step = d.StepName, stepOrder = d.StepOrder, approved = d.Approved }), d.Approved ? d.Meaning : d.Comment, null, "ApprovalDecision", d.RequestId, d.SignerName, d.Comment));
        return await PageEvents(status.Concat(sig).OrderBy(e => e.OccurredAt).ToList(), page, pageSize);
    }

    /// <summary>Extension: workflow state with accountability.</summary>
    [HttpGet("{faiId:guid}/workflow")] [RequirePermission(Permissions.FaiView)] [Tags("FAI Workflow")]
    public async Task<object> Workflow(Guid faiId, CancellationToken ct)
    {
        await m.ViewAsync(FaiService.RecordType, faiId, ct);
        var x = await db.Fais.AsNoTracking().FirstAsync(f => f.Id == faiId, ct);
        return new { Status = Code(x.Status, x.FaiRevision), x.FaiRevision, x.InspectionStartedAt, x.InspectionCompletedAt, x.ApprovedAt, x.ClosedAt, Accountability = await svc.AccountabilityAsync(faiId, ct), Approvals = await ApprovalList(faiId, ct) };
    }

    // ---------- Approvals ----------

    private async Task<List<Approval>> ApprovalList(Guid faiId, CancellationToken ct)
    {
        var reqs = await db.ApprovalRequests.AsNoTracking().Include(r => r.Decisions).Where(r => r.RecordType == FaiService.RecordType && r.RecordId == faiId).OrderBy(r => r.CreatedAt).ToListAsync(ct);
        var routeIds = reqs.Select(r => r.RouteId).Distinct().ToList();
        var steps = await db.ApprovalRouteSteps.AsNoTracking().Where(s => routeIds.Contains(s.RouteId)).ToListAsync(ct);
        var list = new List<Approval>();
        foreach (var r in reqs)
        {
            DateTimeOffset prev = r.CreatedAt;
            foreach (var d in r.Decisions.OrderBy(d => d.SignedAt))
            {
                var returned = !d.Approved && (d.Comment ?? "").StartsWith("Returned for correction", StringComparison.Ordinal);
                list.Add(new Approval(d.Id, d.StepName, d.SignerId, d.Approved ? ApprovalStatusCode.Approved : returned ? ApprovalStatusCode.Returned : ApprovalStatusCode.Rejected,
                    d.Comment, prev, d.SignedAt, r.Id, d.StepOrder, d.Meaning, d.SignerName));
                prev = d.SignedAt;
            }
            if (r.Status == ApprovalStatus.Pending && steps.FirstOrDefault(s => s.RouteId == r.RouteId && s.Order == r.CurrentStep) is { } st)
                list.Add(new Approval(PendingStepId(r.Id, st.Order), st.Name, null, ApprovalStatusCode.Pending, null, prev, null, r.Id, st.Order, st.SignatureMeaning, null));
        }
        return list;
    }

    [HttpGet("{faiId:guid}/approvals", Name = "listFaiApprovals")] [RequirePermission(Permissions.FaiView)] [Tags("FAI Workflow")]
    public async Task<List<Approval>> Approvals(Guid faiId, CancellationToken ct) { await m.ViewAsync(FaiService.RecordType, faiId, ct); return await ApprovalList(faiId, ct); }

    /// <summary>Request approval: the assigned reviewer's acceptance starts the configured approval route. approvalRole must name a step of that route;
    /// approverId (optional) must be an active user. Returns the first pending step.</summary>
    [HttpPost("{faiId:guid}/approvals", Name = "requestFaiApproval")] [RequirePermission(Permissions.FaiEdit)] [Tags("FAI Workflow")] [ProducesResponseType<Approval>(201)]
    public async Task<ActionResult<Approval>> RequestApproval(Guid faiId, Contracts.ApprovalRequest r, CancellationToken ct)
    {
        await m.ViewAsync(FaiService.RecordType, faiId, ct);
        if (string.IsNullOrWhiteSpace(r.ApprovalRole)) throw new AppException("approvalRole is required.");
        var route = await db.ApprovalRoutes.AsNoTracking().Include(x => x.Steps).Where(x => x.RecordType == FaiService.RecordType && x.IsActive).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct)
            ?? throw new ConflictException("No approval route is configured for FAIs.");
        if (!route.Steps.Any(s => string.Equals(s.Name, r.ApprovalRole.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new AppException($"approvalRole must be one of the route's steps: {string.Join(", ", route.Steps.OrderBy(s => s.Order).Select(s => s.Name))}.");
        if (r.ApproverId is { } a && !await db.Users.AnyAsync(u => u.Id == a && u.IsActive, ct)) throw new AppException("The approver must be an active user.");
        if (r.DueAt is { } due && due < m.Now) throw new AppException("dueAt cannot be in the past.");
        await svc.ReviewAsync(faiId, r.Comment, ct);
        var pending = (await ApprovalList(faiId, ct)).Last(x => x.Status == ApprovalStatusCode.Pending);
        return CreatedAtRoute("listFaiApprovals", new { faiId }, pending);
    }

    /// <summary>Sign the current approval step: Approved, Rejected or Returned (for correction). An electronic signature: password is required by the server.</summary>
    [HttpPost("{faiId:guid}/approval-decision", Name = "decideFaiApproval")] [RequirePermission(Permissions.FaiApprove)] [Tags("FAI Workflow")]
    public async Task<Approval> DecideApproval(Guid faiId, Contracts.ApprovalDecision r, CancellationToken ct)
    {
        await ExpectAsync(faiId, r.ExpectedVersion, ct);
        if (string.IsNullOrWhiteSpace(r.Comment)) throw new AppException("comment is required.");
        var pw = Password(r.Password);
        var reqId = (await svc.PendingRequestAsync(faiId, ct)).Id;
        switch (r.Decision)
        {
            case DecisionCode.Approved: await svc.DecideAsync(faiId, true, pw, r.Comment, ct); break;
            case DecisionCode.Rejected: await svc.DecideAsync(faiId, false, pw, r.Comment, ct); break;
            default: await svc.ReturnFromApprovalAsync(faiId, pw, r.Comment, ct); break;
        }
        return (await ApprovalList(faiId, ct)).Last(x => x.RequestId == reqId && x.ApproverId == m.UserId && x.Status != ApprovalStatusCode.Pending);
    }

    [HttpGet("{faiId:guid}/approvals/history")] [RequirePermission(Permissions.FaiView)] [Tags("FAI Workflow")]
    public Task<List<Approval>> ApprovalsHistory(Guid faiId, CancellationToken ct) => Approvals(faiId, ct);

    // ---------- Balloon drawings ----------

    private async Task<List<BalloonDrawing>> BalloonList(Guid faiId, CancellationToken ct)
    {
        var rows = await db.FaiBalloonDrawings.AsNoTracking().Where(b => b.FaiId == faiId).OrderBy(b => b.UploadDate).ToListAsync(ct);
        var attIds = rows.Where(r => r.FileAttachmentId != null).Select(r => r.FileAttachmentId!.Value).ToList();
        var atts = await db.Attachments.AsNoTracking().Where(a => attIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var dIds = rows.Select(r => r.DrawingId).Distinct().ToList();
        var nums = await db.Documents.AsNoTracking().Where(d => dIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Number, ct);
        return rows.Select(b => new BalloonDrawing(b.Id, b.FaiId, b.DrawingId, b.DrawingRevision ?? "", b.FileAttachmentId is { } f && atts.TryGetValue(f, out var at) ? ToDto(at) : null, nums.GetValueOrDefault(b.DrawingId))).ToList();
    }

    [HttpGet("{faiId:guid}/balloon-drawings", Name = "listBalloonDrawings")] [RequirePermission(Permissions.FaiView)] [Tags("FAI Characteristics")]
    public async Task<List<BalloonDrawing>> BalloonDrawings(Guid faiId, CancellationToken ct) { await m.ViewAsync(FaiService.RecordType, faiId, ct); return await BalloonList(faiId, ct); }

    /// <summary>Upload a ballooned drawing (shared attachment engine: malware scan, hashing, versioning) for a drawing revision. importCharacteristics (extension) imports the drawing's balloon map.</summary>
    [HttpPost("{faiId:guid}/balloon-drawings", Name = "uploadBalloonDrawing")] [RequirePermission(Permissions.FaiEdit)] [Tags("FAI Characteristics")] [RequestSizeLimit(200L * 1024 * 1024)] [ProducesResponseType<BalloonDrawing>(201)]
    public async Task<ActionResult<BalloonDrawing>> UploadBalloonDrawing(Guid faiId, IFormFile file, [FromForm] Guid drawingId, [FromForm] string drawingRevision, [FromForm] bool importCharacteristics, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(drawingRevision)) throw new AppException("drawingRevision is required.");
        await svc.LoadEditAsync(faiId, ct);
        await using var s = file.OpenReadStream();
        var a = await files.UploadAsync(FaiService.RecordType, faiId, new UploadRequest(file.FileName, file.ContentType, s, "Ballooned drawing", null, Domain.Shared.Classification.Internal, null), ct);
        var b = await svc.AddBalloonDrawingAsync(faiId, new BalloonDrawingInput(drawingId, drawingRevision, a.Id, importCharacteristics), ct);
        return CreatedAtRoute("listBalloonDrawings", new { faiId }, (await BalloonList(faiId, ct)).First(x => x.Id == b.Id));
    }

    /// <summary>Extension: register a drawing revision without a file (JSON).</summary>
    [HttpPost("{faiId:guid}/balloon-drawings/register")] [RequirePermission(Permissions.FaiEdit)] [Tags("FAI Characteristics")] [ProducesResponseType<BalloonDrawing>(201)]
    public async Task<ActionResult<BalloonDrawing>> AddBalloonDrawing(Guid faiId, BalloonDrawingInput r, CancellationToken ct)
    { var b = await svc.AddBalloonDrawingAsync(faiId, r, ct); return CreatedAtRoute("listBalloonDrawings", new { faiId }, (await BalloonList(faiId, ct)).First(x => x.Id == b.Id)); }
    [HttpPut("{faiId:guid}/balloon-drawings/{drawingId:guid}")] [RequirePermission(Permissions.FaiEdit)]
    public async Task<IActionResult> UpdateBalloonDrawing(Guid faiId, Guid drawingId, BalloonDrawingInput r, CancellationToken ct) { await svc.UpdateBalloonDrawingAsync(faiId, drawingId, r, ct); return NoContent(); }
    [HttpDelete("{faiId:guid}/balloon-drawings/{drawingId:guid}")] [RequirePermission(Permissions.FaiEdit)]
    public async Task<IActionResult> DeleteBalloonDrawing(Guid faiId, Guid drawingId, CancellationToken ct) { await svc.DeleteBalloonDrawingAsync(faiId, drawingId, ct); return NoContent(); }

    /// <summary>Download the latest ballooned drawing file.</summary>
    [HttpGet("{faiId:guid}/balloon-drawings/download")] [RequirePermission(Permissions.FaiView)]
    public async Task<IActionResult> DownloadBalloonDrawing(Guid faiId, CancellationToken ct)
    {
        await m.ViewAsync(FaiService.RecordType, faiId, ct);
        var att = await db.FaiBalloonDrawings.Where(b => b.FaiId == faiId && b.FileAttachmentId != null).OrderByDescending(b => b.UploadDate).Select(b => b.FileAttachmentId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Ballooned drawing file");
        var (meta, content) = await files.OpenAsync(att, ct);
        return File(content, meta.ContentType, meta.FileName);
    }

    /// <summary>The ballooned drawing as PDF (the uploaded file when it is a PDF, otherwise a generated balloon index).</summary>
    [HttpGet("{faiId:guid}/ballooned-pdf")] [RequirePermission(Permissions.FaiView)]
    public async Task<IActionResult> BalloonedPdf(Guid faiId, CancellationToken ct)
    {
        await m.ViewAsync(FaiService.RecordType, faiId, ct);
        var att = await db.FaiBalloonDrawings.Where(b => b.FaiId == faiId && b.FileAttachmentId != null).OrderByDescending(b => b.UploadDate)
            .Join(db.Attachments, b => b.FileAttachmentId, a => a.Id, (b, a) => a).Where(a => a.ContentType == "application/pdf").Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
        if (att is { } id) { var (meta, content) = await files.OpenAsync(id, ct); return File(content, "application/pdf", meta.FileName); }
        var x = await db.Fais.AsNoTracking().FirstAsync(f => f.Id == faiId, ct);
        var chars = await db.FaiCharacteristics.AsNoTracking().Where(c => c.FaiId == faiId).OrderBy(c => c.BalloonNumber).ToListAsync(ct);
        var pdf = new SimplePdf($"{x.Number} balloon index").Title($"Balloon index - {x.Number}").Field("Drawing", $"{x.DrawingNumber} rev {x.DrawingRevision}").Field("Part", $"{x.PartNumber} rev {x.PartRevision}").Gap()
            .Table([("Balloon", 50), ("Ref", 60), ("Class", 60), ("Description", 200), ("Requirement", 142)], chars.Select(c => (IReadOnlyList<string?>)[c.BalloonNumber.ToString(), c.DrawingReference, c.Class.ToString(), c.Description, Req(c)]));
        return File(pdf.ToBytes(), "application/pdf", $"{x.Number}-balloons.pdf");
    }

    /// <summary>Balloon map of a drawing revision (the characteristics every FAI on that revision must account for). Requires fai.edit.</summary>
    [HttpPut("drawing-balloons/{drawingId:guid}")] [RequirePermission(Permissions.FaiEdit)]
    public async Task<IActionResult> SaveBalloonMap(Guid drawingId, BalloonMapRequest r, CancellationToken ct) { await svc.SaveBalloonMapAsync(drawingId, r.Revision, r.Balloons, ct); return NoContent(); }
    [HttpGet("drawing-balloons/{drawingId:guid}")] [RequirePermission(Permissions.FaiView)]
    public async Task<object> BalloonMap(Guid drawingId, [FromQuery] string revision, CancellationToken ct)
    {
        await m.ViewAsync("Drawing", drawingId, ct);
        return await db.DrawingBalloons.AsNoTracking().Where(b => b.DrawingId == drawingId && b.DrawingRevision == revision).OrderBy(b => b.BalloonNumber).ToListAsync(ct);
    }

    // ---------- Characteristics ----------

    private async Task<IQueryable<FaiCharacteristic>> Chars(Guid faiId, CancellationToken ct) { await m.ViewAsync(FaiService.RecordType, faiId, ct); return db.FaiCharacteristics.AsNoTracking().Where(c => c.FaiId == faiId).OrderBy(c => c.BalloonNumber); }
    private static async Task<PagedCharacteristics> PageChars(IQueryable<FaiCharacteristic> q, int page, int pageSize, CancellationToken ct)
    {
        var (p, n) = Clamp(page, pageSize);
        var total = await q.CountAsync(ct);
        return new PagedCharacteristics((await q.Skip((p - 1) * n).Take(n).ToListAsync(ct)).Select(ToDto).ToList(), Meta(p, n, total));
    }
    private async Task<FAICharacteristic> CharDto(Guid faiId, Guid id, CancellationToken ct) =>
        ToDto(await (await Chars(faiId, ct)).FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Characteristic"));

    [HttpGet("{faiId:guid}/characteristics", Name = "listFaiCharacteristics")] [RequirePermission(Permissions.FaiView)] [Tags("FAI Characteristics")]
    public async Task<PagedCharacteristics> Characteristics(Guid faiId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) => await PageChars(await Chars(faiId, ct), page, pageSize, ct);
    /// <summary>Characteristics without a result yet.</summary>
    [HttpGet("{faiId:guid}/characteristics/open", Name = "listOpenFaiCharacteristics")] [RequirePermission(Permissions.FaiView)] [Tags("FAI Characteristics")]
    public async Task<PagedCharacteristics> OpenCharacteristics(Guid faiId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) => await PageChars((await Chars(faiId, ct)).Where(c => c.Result == CharResult.NotInspected), page, pageSize, ct);
    [HttpGet("{faiId:guid}/characteristics/failed", Name = "listFailedFaiCharacteristics")] [RequirePermission(Permissions.FaiView)] [Tags("FAI Characteristics")]
    public async Task<PagedCharacteristics> FailedCharacteristics(Guid faiId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) => await PageChars((await Chars(faiId, ct)).Where(c => c.Result == CharResult.Fail), page, pageSize, ct);

    /// <summary>Balloons on the drawing revision's balloon map that the FAI does not account for. Items are the map entries (id = balloon map entry, version 0, status "Missing").</summary>
    [HttpGet("{faiId:guid}/characteristics/missing", Name = "listMissingFaiCharacteristics")] [RequirePermission(Permissions.FaiView)] [Tags("FAI Characteristics")]
    public async Task<PagedCharacteristics> MissingCharacteristics(Guid faiId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        await m.ViewAsync(FaiService.RecordType, faiId, ct);
        var a = await svc.AccountabilityAsync(faiId, ct);
        var x = await db.Fais.AsNoTracking().FirstAsync(f => f.Id == faiId, ct);
        var all = (await db.DrawingBalloons.AsNoTracking().Where(b => b.DrawingId == x.DrawingId && b.DrawingRevision == x.DrawingRevision && a.Missing.Contains(b.BalloonNumber)).OrderBy(b => b.BalloonNumber).ToListAsync(ct))
            .Select(b => new FAICharacteristic(b.Id, faiId, b.BalloonNumber, b.DrawingReference, Code(b.Class), b.Description, b.Kind == CharacteristicKind.Variable ? DataTypeCode.Numeric : DataTypeCode.Visual,
                b.Nominal, b.Nominal + b.LowerTolerance, b.Nominal + b.UpperTolerance, b.Units, b.InspectionMethod, b.RequirementSource, b.AcceptanceCriteria, 0, "Missing", null, false, null)).ToList();
        var (p, n) = Clamp(page, pageSize);
        return new PagedCharacteristics(all.Skip((p - 1) * n).Take(n).ToList(), Meta(p, n, all.Count));
    }
    /// <summary>Extension: missing balloons and numbering gaps.</summary>
    [HttpGet("{faiId:guid}/characteristics/accountability")] [RequirePermission(Permissions.FaiView)] [Tags("FAI Characteristics")]
    public async Task<Accountability> CharacteristicAccountability(Guid faiId, CancellationToken ct) { await m.ViewAsync(FaiService.RecordType, faiId, ct); return await svc.AccountabilityAsync(faiId, ct); }

    [HttpGet("{faiId:guid}/characteristics/{characteristicId:guid}", Name = "getFaiCharacteristic")] [RequirePermission(Permissions.FaiView)] [Tags("FAI Characteristics")]
    public Task<FAICharacteristic> Characteristic(Guid faiId, Guid characteristicId, CancellationToken ct) => CharDto(faiId, characteristicId, ct);
    [HttpPost("{faiId:guid}/characteristics", Name = "createFaiCharacteristic")] [RequirePermission(Permissions.FaiEdit)] [Tags("FAI Characteristics")] [ProducesResponseType<FAICharacteristic>(201)]
    public async Task<ActionResult<FAICharacteristic>> AddCharacteristic(Guid faiId, FAICharacteristicInput r, CancellationToken ct)
    { var c = await svc.AddCharacteristicAsync(faiId, ToInput(r), ct); return CreatedAtRoute("getFaiCharacteristic", new { faiId, characteristicId = c.Id }, await CharDto(faiId, c.Id, ct)); }
    [HttpPut("{faiId:guid}/characteristics/{characteristicId:guid}", Name = "updateFaiCharacteristic")] [RequirePermission(Permissions.FaiEdit)] [Tags("FAI Characteristics")]
    public async Task<FAICharacteristic> UpdateCharacteristic(Guid faiId, Guid characteristicId, FAICharacteristicInput r, CancellationToken ct)
    { await svc.UpdateCharacteristicAsync(faiId, characteristicId, ToInput(r), ct); return await CharDto(faiId, characteristicId, ct); }
    [HttpDelete("{faiId:guid}/characteristics/{characteristicId:guid}", Name = "deleteFaiCharacteristic")] [RequirePermission(Permissions.FaiEdit)] [Tags("FAI Characteristics")]
    public async Task<IActionResult> DeleteCharacteristic(Guid faiId, Guid characteristicId, CancellationToken ct) { await svc.DeleteCharacteristicAsync(faiId, characteristicId, ct); return NoContent(); }

    // ---------- Results ----------

    private async Task<CharacteristicResult> ResultDto(FaiCharacteristicResult v, CancellationToken ct) => ToDto(v,
        v.InspectionEquipmentId is null ? null : v.EquipmentCalibrationDueOn is { } due ? due >= DateOnly.FromDateTime(v.InspectionDate.UtcDateTime) : null,
        await db.Users.Where(u => u.Id == v.InspectorId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct));

    /// <summary>For numeric characteristics pass/fail is computed from the limits; a contradicting result is refused (400).</summary>
    private async Task CheckResultAsync(Guid faiId, Guid characteristicId, CharacteristicResultInput r, CancellationToken ct)
    {
        var c = await db.FaiCharacteristics.AsNoTracking().FirstOrDefaultAsync(x => x.Id == characteristicId && x.FaiId == faiId, ct) ?? throw new NotFoundException("Characteristic");
        if (c.DataType != CharDataType.Numeric || r.Result == ResultCode.NotApplicable) return;
        if (r.MeasuredValue is not { } v) throw new AppException("A numeric characteristic needs measuredValue.");
        var pass = (c.Nominal + c.LowerTolerance is not { } lo || v >= lo) && (c.Nominal + c.UpperTolerance is not { } up || v <= up);
        if ((r.Result == ResultCode.Pass) != pass) throw new AppException($"The measured value {v} is {(pass ? "within" : "outside")} the specification limits, so the result must be {(pass ? "Pass" : "Fail")}.");
    }

    [HttpGet("{faiId:guid}/characteristics/{characteristicId:guid}/results", Name = "listCharacteristicResults")] [RequirePermission(Permissions.FaiView)] [Tags("FAI Characteristics")]
    public async Task<PagedCharacteristicResults> Results(Guid faiId, Guid characteristicId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        await m.ViewAsync(FaiService.RecordType, faiId, ct);
        var (p, n) = Clamp(page, pageSize);
        var q = db.FaiCharacteristicResults.AsNoTracking().Where(v => v.FaiId == faiId && v.CharacteristicId == characteristicId).OrderBy(v => v.InspectionSequence);
        var total = await q.CountAsync(ct);
        var items = new List<CharacteristicResult>();
        foreach (var v in await q.Skip((p - 1) * n).Take(n).ToListAsync(ct)) items.Add(await ResultDto(v, ct));
        return new PagedCharacteristicResults(items, Meta(p, n, total));
    }
    /// <summary>Record a result. sequence must be the next sequence for the characteristic (409 otherwise). NotApplicable needs notes.</summary>
    [HttpPost("{faiId:guid}/characteristics/{characteristicId:guid}/results", Name = "createCharacteristicResult")] [RequirePermission(Permissions.FaiEdit)] [Tags("FAI Characteristics")] [ProducesResponseType<CharacteristicResult>(201)]
    public async Task<ActionResult<CharacteristicResult>> AddResult(Guid faiId, Guid characteristicId, CharacteristicResultInput r, CancellationToken ct)
    {
        await svc.LoadEditAsync(faiId, ct);
        await CheckResultAsync(faiId, characteristicId, r, ct);
        var x = await svc.RecordResultAsync(faiId, characteristicId, ToInput(r), ct);
        return CreatedAtRoute("listCharacteristicResults", new { faiId, characteristicId }, await ResultDto(x, ct));
    }
    /// <summary>Correct the latest result: kept as a new result (the original stays in history). version, when sent, must match.</summary>
    [HttpPut("{faiId:guid}/results/{resultId:guid}", Name = "updateCharacteristicResult")] [RequirePermission(Permissions.FaiEdit)] [Tags("FAI Characteristics")]
    public async Task<CharacteristicResult> UpdateResult(Guid faiId, Guid resultId, CharacteristicResultInput r, CancellationToken ct)
    {
        await svc.LoadEditAsync(faiId, ct);
        var old = await db.FaiCharacteristicResults.AsNoTracking().FirstOrDefaultAsync(v => v.Id == resultId && v.FaiId == faiId, ct) ?? throw new NotFoundException("Result");
        if (r.Version is { } ver && ver != old.RowVersion) throw new ConflictException("The result was changed by someone else; reload it and try again.");
        await CheckResultAsync(faiId, old.CharacteristicId, r, ct);
        var x = await svc.UpdateResultAsync(faiId, resultId, ToInput(r) with { Sequence = null }, ct);
        return await ResultDto(x, ct);
    }
    [HttpDelete("{faiId:guid}/results/{resultId:guid}", Name = "deleteCharacteristicResult")] [RequirePermission(Permissions.FaiEdit)] [Tags("FAI Characteristics")]
    public async Task<IActionResult> DeleteResult(Guid faiId, Guid resultId, CancellationToken ct) { await svc.DeleteResultAsync(faiId, resultId, ct); return NoContent(); }
    /// <summary>Record many results; each row is validated exactly like a single result and reported individually.</summary>
    [HttpPost("{faiId:guid}/bulk-results")] [RequirePermission(Permissions.FaiEdit)]
    public Task<List<BulkOutcome>> BulkResults(Guid faiId, IReadOnlyList<BulkResult> rows, CancellationToken ct) => svc.BulkResultsAsync(faiId, rows, ct);
    /// <summary>Import CSV rows: balloon,measured,pass,equipmentNumber,notes.</summary>
    [HttpPost("{faiId:guid}/import-results")] [RequirePermission(Permissions.FaiEdit)]
    public Task<List<BulkOutcome>> ImportResults(Guid faiId, ImportCsvRequest r, CancellationToken ct) => svc.ImportResultsAsync(faiId, r.Csv, ct);
    /// <summary>CSV export of characteristics with their latest results. Requires reports.export; written to the audit trail.</summary>
    [HttpGet("{faiId:guid}/export-results")] [RequirePermission(Permissions.ReportsExport)]
    public async Task<IActionResult> ExportResults(Guid faiId, CancellationToken ct)
    {
        await m.ViewAsync(FaiService.RecordType, faiId, ct);
        var x = await db.Fais.AsNoTracking().FirstAsync(f => f.Id == faiId, ct);
        var chars = await db.FaiCharacteristics.AsNoTracking().Where(c => c.FaiId == faiId).OrderBy(c => c.BalloonNumber).ToListAsync(ct);
        var sb = new StringBuilder("balloon,description,class,nominal,lower_tol,upper_tol,units,measured,result\n");
        static string E(object? v) { var s = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? ""; if (s.Length > 0 && "=+-@".Contains(s[0]) && !decimal.TryParse(s, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _)) s = "'" + s; return s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s; }
        foreach (var c in chars) sb.AppendLine(string.Join(',', new object?[] { c.BalloonNumber, c.Description, c.Class, c.Nominal, c.LowerTolerance, c.UpperTolerance, c.Units, c.MeasuredValue, c.Result }.Select(E)));
        await Exported($"Exported FAI {x.Number} results as CSV", faiId, ct);
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"{x.Number}-results.csv");
    }

    private async Task Exported(string description, Guid id, CancellationToken ct)
    {
        var current = HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
        db.AuditEntries.Add(new Domain.Audit.AuditEntry { OccurredAt = m.Now, UserId = current.UserId, UserName = current.UserName, Action = "Exported", EntityType = nameof(FaiRecord), EntityId = id, Description = description, CorrelationId = current.CorrelationId });
        await db.SaveChangesAsync(ct);
    }

    // ---------- Attachments / comments (shared engines) ----------

    private RecordsController Shared() { var c = ActivatorUtilities.CreateInstance<RecordsController>(HttpContext.RequestServices); c.ControllerContext = ControllerContext; return c; }
    [HttpGet("{faiId:guid}/attachments", Name = "listFaiAttachments")] [RequirePermission(Permissions.FaiView)]
    public async Task<List<Contracts.Attachment>> Attachments(Guid faiId, [FromQuery] bool history, CancellationToken ct)
    { await m.ViewAsync(FaiService.RecordType, faiId, ct); return (await files.ListAsync(FaiService.RecordType, faiId, history, ct)).Select(ToDto).ToList(); }
    [HttpPost("{faiId:guid}/attachments", Name = "uploadFaiAttachment")] [RequirePermission(Permissions.FaiEdit)] [RequestSizeLimit(200L * 1024 * 1024)] [ProducesResponseType<Contracts.Attachment>(201)]
    public async Task<ActionResult<Contracts.Attachment>> Upload(Guid faiId, IFormFile file, [FromForm] string? category, [FromForm] string? description, [FromForm] Domain.Shared.Classification classification, [FromForm] Guid? replacesId, CancellationToken ct)
    {
        await svc.LoadEditAsync(faiId, ct);
        await using var s = file.OpenReadStream();
        var a = await files.UploadAsync(FaiService.RecordType, faiId, new UploadRequest(file.FileName, file.ContentType, s, category, description, classification, replacesId), ct);
        return CreatedAtRoute("downloadFaiAttachment", new { faiId, attachmentId = a.Id }, ToDto(a));
    }
    [HttpGet("{faiId:guid}/attachments/{attachmentId:guid}", Name = "downloadFaiAttachment")] [ProducesResponseType<FileStreamResult>(200, "application/octet-stream")] [RequirePermission(Permissions.FaiView)]
    public async Task<IActionResult> Attachment(Guid faiId, Guid attachmentId, CancellationToken ct)
    {
        await m.ViewAsync(FaiService.RecordType, faiId, ct);
        if (!await db.Attachments.AnyAsync(a => a.Id == attachmentId && a.OwnerType == FaiService.RecordType && a.OwnerId == faiId, ct)) throw new NotFoundException("File");
        var (meta, content) = await files.OpenAsync(attachmentId, ct);
        return File(content, meta.ContentType, meta.FileName);
    }
    [HttpDelete("{faiId:guid}/attachments/{attachmentId:guid}", Name = "deleteFaiAttachment")] [RequirePermission(Permissions.FaiEdit)]
    public async Task<IActionResult> DeleteAttachment(Guid faiId, Guid attachmentId, CancellationToken ct)
    {
        if (!await db.Attachments.AnyAsync(a => a.Id == attachmentId && a.OwnerType == FaiService.RecordType && a.OwnerId == faiId, ct)) throw new NotFoundException("File");
        await files.ArchiveAsync(attachmentId, ct);
        return NoContent();
    }
    [HttpGet("{faiId:guid}/comments")] [RequirePermission(Permissions.FaiView)]
    public Task<IEnumerable<RecordsController.CommentDto>> Comments(Guid faiId, CancellationToken ct) => Shared().Comments(FaiService.RecordType, faiId, ct);
    [HttpPost("{faiId:guid}/comments")] [RequirePermission(Permissions.FaiView)]
    public Task<ActionResult<RecordsController.CommentDto>> AddComment(Guid faiId, RecordsController.AddCommentRequest r, CancellationToken ct) => Shared().AddComment(FaiService.RecordType, faiId, r, ct);
    [HttpPut("{faiId:guid}/comments/{commentId:guid}")] [RequirePermission(Permissions.FaiView)]
    public Task<ActionResult<RecordsController.CommentDto>> EditComment(Guid faiId, Guid commentId, RecordsController.AddCommentRequest r, CancellationToken ct) => Shared().EditComment(FaiService.RecordType, faiId, commentId, r, ct);
    [HttpDelete("{faiId:guid}/comments/{commentId:guid}")] [RequirePermission(Permissions.FaiView)]
    public Task<IActionResult> DeleteComment(Guid faiId, Guid commentId, CancellationToken ct) => Shared().DeleteComment(FaiService.RecordType, faiId, commentId, ct);

    // ---------- NCR / CAPA / links ----------

    /// <summary>Raise an NCR for failed characteristics without one (all by default; characteristicIds narrows it). title/description/ownerId default from the FAI.</summary>
    [HttpPost("{faiId:guid}/ncr", Name = "createNCRFromFai")] [RequirePermission(Permissions.NcrEdit)] [Tags("Integrations")] [ProducesResponseType<LinkedRecord>(201)]
    public async Task<ActionResult<LinkedRecord>> CreateNcr(Guid faiId, CreateLinkedRecordRequest? r, CancellationToken ct)
    {
        var n = await svc.CreateNcrAsync(faiId, r?.CharacteristicIds, r?.Title, ct, r?.Description, r?.OwnerId);
        return CreatedAtRoute("listFaiLinkedRecords", new { faiId }, new LinkedRecord(LinkedRecordTypeCode.NCR, n.Id, n.Number, "Nonconformance", n.Title));
    }
    [HttpPost("{faiId:guid}/capa", Name = "createCAPAFromFai")] [RequirePermission(Permissions.CapaEdit)] [Tags("Integrations")] [ProducesResponseType<LinkedRecord>(201)]
    public async Task<ActionResult<LinkedRecord>> CreateCapa(Guid faiId, CreateLinkedRecordRequest? r, CancellationToken ct)
    {
        var x = await svc.LoadEditAsync(faiId, ct);
        var title = string.IsNullOrWhiteSpace(r?.Title) ? $"Corrective action for FAI {x.Number}" : r.Title;
        var c = await svc.CreateCapaAsync(faiId, title, string.IsNullOrWhiteSpace(r?.Description) ? $"FAI {x.Number} ({x.PartNumber} rev {x.PartRevision}) needs corrective action." : r.Description, ct, r?.OwnerId);
        return CreatedAtRoute("listFaiLinkedRecords", new { faiId }, new LinkedRecord(LinkedRecordTypeCode.CAPA, c.Id, c.Number, "Corrective action", c.Title));
    }
    [HttpPost("{faiId:guid}/create-ncr")] [RequirePermission(Permissions.NcrEdit)] [Tags("Integrations")]
    public Task<ActionResult<LinkedRecord>> CreateNcrLegacy(Guid faiId, CreateLinkedRecordRequest? r, CancellationToken ct) => CreateNcr(faiId, r, ct);


    /// <summary>Records linked through the shared link engine (NCRs, CAPAs, changes, prior FAIs …) plus the header relationships (product, part, drawing, specification, supplier, customer, change, plan). Records the caller cannot view are omitted.</summary>
    [HttpGet("{faiId:guid}/linked-records", Name = "listFaiLinkedRecords")] [RequirePermission(Permissions.FaiView)] [Tags("Integrations")]
    public async Task<List<LinkedRecord>> LinkedRecords(Guid faiId, CancellationToken ct)
    {
        var links = await Shared().Links(FaiService.RecordType, faiId, ct);
        var x = await db.Fais.AsNoTracking().FirstAsync(f => f.Id == faiId, ct);
        var list = links.Select(l => new LinkedRecord(LinkType(l.Type), l.RecordId, l.Number, l.LinkType, l.Title)).ToList();
        void Add(LinkedRecordTypeCode t, Guid? id, string? number, string? title) { if (id is { } i && number is not null && !list.Any(l => l.RecordId == i)) list.Add(new LinkedRecord(t, i, number, "Header", title)); }
        Add(LinkedRecordTypeCode.Product, x.ProductId, await db.Products.Where(p => p.Id == x.ProductId).Select(p => p.Name).FirstOrDefaultAsync(ct), null);
        Add(LinkedRecordTypeCode.Part, x.PartId, x.PartNumber, x.PartRevision);
        Add(LinkedRecordTypeCode.Drawing, x.DrawingId, x.DrawingNumber, x.DrawingRevision);
        Add(LinkedRecordTypeCode.Specification, x.SpecificationId, await db.Documents.Where(d => d.Id == x.SpecificationId).Select(d => d.Number).FirstOrDefaultAsync(ct), x.Specification);
        Add(LinkedRecordTypeCode.Supplier, x.SupplierId, await db.Suppliers.Where(s => s.Id == x.SupplierId).Select(s => s.Code).FirstOrDefaultAsync(ct), null);
        Add(LinkedRecordTypeCode.Customer, x.CustomerId, await db.Customers.Where(c => c.Id == x.CustomerId).Select(c => c.Name).FirstOrDefaultAsync(ct), null);
        Add(LinkedRecordTypeCode.ChangeRequest, x.ChangeId, await db.ChangeRequests.Where(c => c.Id == x.ChangeId).Select(c => c.Number).FirstOrDefaultAsync(ct), null);
        Add(LinkedRecordTypeCode.InspectionPlan, x.InspectionPlanId, await db.InspectionPlans.Where(c => c.Id == x.InspectionPlanId).Select(c => c.Number).FirstOrDefaultAsync(ct), null);
        Add(LinkedRecordTypeCode.FAI, x.BaseFaiId, await db.Fais.Where(c => c.Id == x.BaseFaiId).Select(c => c.Number).FirstOrDefaultAsync(ct), "Base FAI");
        return list;
    }

    // ---------- PDF / reports ----------

    private static string Req(FaiCharacteristic c) => c.Kind == CharacteristicKind.Variable ? $"{c.Nominal:0.####} {c.LowerTolerance:+0.####;-0.####;0}/{c.UpperTolerance:+0.####;-0.####;0} {c.Units}" : c.AcceptanceCriteria ?? "";

    private async Task<IActionResult> Pdf(Guid faiId, bool detail, CancellationToken ct)
    {
        await m.ViewAsync(FaiService.RecordType, faiId, ct);
        var x = await db.Fais.AsNoTracking().FirstAsync(f => f.Id == faiId, ct);
        var acc = await svc.AccountabilityAsync(faiId, ct);
        var chars = await db.FaiCharacteristics.AsNoTracking().Where(c => c.FaiId == faiId).OrderBy(c => c.BalloonNumber).ToListAsync(ct);
        var name = (Guid? id) => id is null ? null : db.Users.Where(u => u.Id == id).Select(u => u.DisplayName).FirstOrDefault();
        var pdf = new SimplePdf($"{x.Number} rev {x.FaiRevision} - printed {m.Now:yyyy-MM-dd HH:mm} UTC").Title($"First Article Inspection {x.Number}")
            .Field("Title", x.Title).Field("Type / reason", $"{x.Type} / {x.Reason}").Field("Status", x.Status.ToString()).Field("Part", $"{x.PartNumber} rev {x.PartRevision}")
            .Field("Drawing", x.DrawingNumber is null ? null : $"{x.DrawingNumber} rev {x.DrawingRevision}").Field("Specification", x.Specification).Field("Process", x.Process)
            .Field("Lot / batch / serial", string.Join(" / ", new[] { x.LotNumber, x.BatchNumber, x.SerialNumber }.Select(v => v ?? "-"))).Field("Production date", x.ProductionDate?.ToString("yyyy-MM-dd"))
            .Field("Inspector", name(x.InspectorId)).Field("Reviewer", name(x.ReviewerId)).Field("Approved", x.ApprovedAt?.ToString("yyyy-MM-dd")).Field("Customer approval", x.CustomerApprovalReference)
            .Heading("Accountability").Line($"{acc.Total} characteristics, {acc.Inspected} inspected, {acc.Passed} passed, {acc.Failed} failed, {acc.Open} open. Missing: {(acc.Missing.Count == 0 ? "none" : string.Join(", ", acc.Missing))}.");
        if (detail)
        {
            pdf.Heading("Characteristics").Table([("No.", 30), ("Class", 55), ("Description", 170), ("Requirement", 120), ("Measured", 70), ("Result", 67)],
                chars.Select(c => (IReadOnlyList<string?>)[c.BalloonNumber.ToString(), c.Class.ToString(), c.Description, Req(c), c.MeasuredValue?.ToString("0.####") ?? (c.Result == CharResult.NotInspected ? "" : "see notes"), c.Result + (c.CarriedOver ? " (carried)" : "")]));
            var decisions = await db.ApprovalDecisions.AsNoTracking().Join(db.ApprovalRequests.Where(r => r.RecordType == FaiService.RecordType && r.RecordId == faiId), d => d.RequestId, r => r.Id, (d, r) => d).OrderBy(d => d.SignedAt).ToListAsync(ct);
            pdf.Heading("Signatures").Table([("Step", 110), ("Signer", 140), ("Meaning", 130), ("Signed (UTC)", 132)], decisions.Select(d => (IReadOnlyList<string?>)[d.StepName, d.SignerName, d.Approved ? d.Meaning : "Not approved: " + d.Comment, d.SignedAt.ToString("yyyy-MM-dd HH:mm")]));
        }
        var current = HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
        db.AuditEntries.Add(new Domain.Audit.AuditEntry { OccurredAt = m.Now, UserId = current.UserId, UserName = current.UserName, Action = "Printed", EntityType = nameof(FaiRecord), EntityId = faiId, Description = $"Printed FAI {x.Number} ({(detail ? "detail" : "summary")}) as PDF", CorrelationId = current.CorrelationId });
        await db.SaveChangesAsync(ct);
        return File(pdf.ToBytes(), "application/pdf", $"{x.Number}{(detail ? "" : "-summary")}.pdf");
    }

    /// <summary>FAI report as PDF (detail with characteristics and signatures). Printing is audited.</summary>
    [HttpGet("{faiId:guid}/pdf")] [RequirePermission(Permissions.FaiView)] public Task<IActionResult> PdfReport(Guid faiId, CancellationToken ct) => Pdf(faiId, true, ct);
    [HttpGet("{faiId:guid}/report-detail")] [RequirePermission(Permissions.FaiView)] public Task<IActionResult> ReportDetail(Guid faiId, CancellationToken ct) => Pdf(faiId, true, ct);
    [HttpGet("{faiId:guid}/report-summary")] [RequirePermission(Permissions.FaiView)] public Task<IActionResult> ReportSummary(Guid faiId, CancellationToken ct) => Pdf(faiId, false, ct);
    /// <summary>Structured report data (JSON) for the FAI: header, accountability, characteristics with result history and approvals.</summary>
    [HttpGet("{faiId:guid}/report")] [RequirePermission(Permissions.FaiView)]
    public async Task<object> Report(Guid faiId, CancellationToken ct)
    {
        var detail = await Detail(faiId, ct);
        var results = await db.FaiCharacteristicResults.AsNoTracking().Where(v => v.FaiId == faiId).OrderBy(v => v.InspectionSequence).ToListAsync(ct);
        return new { Detail = detail, Results = results, Approvals = await ApprovalList(faiId, ct) };
    }

    // ---------- Bulk ----------

    /// <summary>Close many FAIs; each id runs the single close action with the same permission, scope and rule checks.</summary>
    [HttpPost("bulk-close")] [RequirePermission(Permissions.FaiApprove)]
    public Task<List<BulkOutcome>> BulkClose(IdsRequest r, CancellationToken ct) => svc.BulkAsync(r.Ids, id => svc.CloseAsync(id, r.Notes, ct));
    /// <summary>Sign the current approval step of many FAIs with one e-signature; each id is checked individually.</summary>
    [HttpPost("bulk-approve")] [RequirePermission(Permissions.FaiApprove)]
    public Task<List<BulkOutcome>> BulkApprove(IdsRequest r, CancellationToken ct) => svc.BulkAsync(r.Ids, id => svc.DecideAsync(id, true, r.Password ?? "", r.Notes, ct));
}
