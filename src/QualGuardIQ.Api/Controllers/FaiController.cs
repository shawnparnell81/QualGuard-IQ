using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Fai;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

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
public sealed class FaiController(AppDbContext db, FaiService svc, ModuleSupport m, AttachmentService files) : ControllerBase
{
    // ---------- Management ----------

    private async Task<IQueryable<FaiRecord>> Scoped(CancellationToken ct) => await m.ScopeAsync(db.Fais.AsNoTracking(), Permissions.FaiView, ct);

    private async Task<object> Page(IQueryable<FaiRecord> q, int page, int pageSize, CancellationToken ct)
    {
        var today = await m.TodayAsync(null, ct);
        return await Paging.PageAsync(q.OrderByDescending(x => x.CreatedAt).Select(x => new FaiSummaryDto(x.Id, x.Number, x.Title, x.Type, x.Reason, x.Status, x.FaiRevision, x.PartNumber, x.PartRevision, x.DrawingNumber, x.DrawingRevision,
            db.Suppliers.Where(s => s.Id == x.SupplierId).Select(s => s.Code).FirstOrDefault(), db.Customers.Where(c => c.Id == x.CustomerId).Select(c => c.Name).FirstOrDefault(), x.DueOn,
            x.DueOn < today && (int)x.Status <= 4, db.Users.Where(u => u.Id == x.InspectorId).Select(u => u.DisplayName).First(), db.Users.Where(u => u.Id == x.ReviewerId).Select(u => u.DisplayName).FirstOrDefault(),
            db.Sites.Where(s => s.Id == x.SiteId).Select(s => s.Code).First(), db.FaiCharacteristics.Count(c => c.FaiId == x.Id), db.FaiCharacteristics.Count(c => c.FaiId == x.Id && c.Result == CharResult.Pass),
            db.FaiCharacteristics.Count(c => c.FaiId == x.Id && c.Result == CharResult.Fail), x.CreatedAt)), page, pageSize, ct);
    }

    /// <summary>List FAIs in the caller's scope. Requires fai.view.</summary>
    [HttpGet] [RequirePermission(Permissions.FaiView)]
    public async Task<object> List([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) => await Page(await Scoped(ct), page, pageSize, ct);

    /// <summary>Free-text search over number, title, part, drawing, lot and serial. Requires fai.view.</summary>
    [HttpGet("search")] [RequirePermission(Permissions.FaiView)]
    public async Task<object> Search([FromQuery] string q, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var t = $"%{(q ?? "").Trim()}%";
        return await Page((await Scoped(ct)).Where(x => EF.Functions.ILike(x.Number + " " + x.Title + " " + x.PartNumber + " " + (x.DrawingNumber ?? "") + " " + (x.LotNumber ?? "") + " " + (x.SerialNumber ?? ""), t)), page, pageSize, ct);
    }

    /// <summary>Filter by status, type, reason, part, supplier, customer, drawing, site and due date. Requires fai.view.</summary>
    [HttpGet("filter")] [RequirePermission(Permissions.FaiView)]
    public async Task<object> Filter([FromQuery] FaiStatus? status, [FromQuery] FaiType? type, [FromQuery] FaiReason? reason, [FromQuery] Guid? partId, [FromQuery] Guid? supplierId, [FromQuery] Guid? customerId,
        [FromQuery] Guid? drawingId, [FromQuery] Guid? siteId, [FromQuery] DateOnly? dueBefore, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var q = await Scoped(ct);
        if (status is not null) q = q.Where(x => x.Status == status);
        if (type is not null) q = q.Where(x => x.Type == type);
        if (reason is not null) q = q.Where(x => x.Reason == reason);
        if (partId is not null) q = q.Where(x => x.PartId == partId);
        if (supplierId is not null) q = q.Where(x => x.SupplierId == supplierId);
        if (customerId is not null) q = q.Where(x => x.CustomerId == customerId);
        if (drawingId is not null) q = q.Where(x => x.DrawingId == drawingId);
        if (siteId is not null) q = q.Where(x => x.SiteId == siteId);
        if (dueBefore is not null) q = q.Where(x => x.DueOn <= dueBefore);
        return await Page(q, page, pageSize, ct);
    }

    [HttpGet("status/{status}")] [RequirePermission(Permissions.FaiView)]
    public async Task<object> ByStatus(FaiStatus status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) => await Page((await Scoped(ct)).Where(x => x.Status == status), page, pageSize, ct);
    [HttpGet("open")] [RequirePermission(Permissions.FaiView)]
    public async Task<object> Open([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) => await Page((await Scoped(ct)).Where(x => (int)x.Status <= 4), page, pageSize, ct);
    [HttpGet("overdue")] [RequirePermission(Permissions.FaiView)]
    public async Task<object> Overdue([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    { var today = await m.TodayAsync(null, ct); return await Page((await Scoped(ct)).Where(x => (int)x.Status <= 4 && x.DueOn < today), page, pageSize, ct); }
    [HttpGet("approval-pending")] [RequirePermission(Permissions.FaiView)]
    public async Task<object> ApprovalPending([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) => await Page((await Scoped(ct)).Where(x => x.Status == FaiStatus.PendingApproval), page, pageSize, ct);
    [HttpGet("closed")] [RequirePermission(Permissions.FaiView)]
    public async Task<object> Closed([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) => await Page((await Scoped(ct)).Where(x => x.Status == FaiStatus.Closed), page, pageSize, ct);

    /// <summary>FAI types and creation reasons.</summary>
    [HttpGet("types")] [RequirePermission(Permissions.FaiView)]
    public object Types() => new { Types = Enum.GetNames<FaiType>(), Reasons = Enum.GetNames<FaiReason>(), Statuses = Enum.GetNames<FaiStatus>(), CharacteristicClasses = Enum.GetNames<CharacteristicClass>() };

    /// <summary>Full record with characteristics, accountability, ballooned drawings, revisions and allowed actions. Requires fai.view.</summary>
    [HttpGet("{faiId:guid}")] [RequirePermission(Permissions.FaiView)]
    public async Task<object> Get(Guid faiId, CancellationToken ct)
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
            if (x.Status == FaiStatus.Review && x.ReviewerId == m.UserId) allowed.AddRange(["review", "return"]);
            if (x.Status == FaiStatus.Draft) allowed.Add("delete");
            if (acc.UnresolvedFailures.Count > 0) allowed.Add("create-ncr");
            allowed.AddRange(["copy", "clone", "create-capa"]);
        }
        if (canApprove)
        {
            if (x.Status == FaiStatus.PendingApproval) allowed.AddRange(["approve", "reject", "approval-return"]);
            if (x.Status == FaiStatus.Approved) allowed.AddRange(["release", "close"]);
            if (x.Status is FaiStatus.Closed or FaiStatus.Approved or FaiStatus.Rejected) allowed.Add("reopen");
        }
        return new
        {
            Fai = x,
            Product = await db.Products.Where(p => p.Id == x.ProductId).Select(p => new { p.Id, p.Name }).FirstOrDefaultAsync(ct),
            Supplier = await db.Suppliers.Where(s => s.Id == x.SupplierId).Select(s => new { s.Id, s.Code, s.LegalName }).FirstOrDefaultAsync(ct),
            Customer = await db.Customers.Where(c => c.Id == x.CustomerId).Select(c => new { c.Id, c.Name }).FirstOrDefaultAsync(ct),
            Change = await db.ChangeRequests.Where(c => c.Id == x.ChangeId).Select(c => new { c.Id, c.Number, c.Title }).FirstOrDefaultAsync(ct),
            InspectorName = await db.Users.Where(u => u.Id == x.InspectorId).Select(u => u.DisplayName).FirstAsync(ct),
            ReviewerName = await db.Users.Where(u => u.Id == x.ReviewerId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct),
            Characteristics = await db.FaiCharacteristics.AsNoTracking().Where(c => c.FaiId == faiId).OrderBy(c => c.BalloonNumber).ToListAsync(ct),
            BalloonDrawings = await db.FaiBalloonDrawings.AsNoTracking().Where(b => b.FaiId == faiId).OrderBy(b => b.UploadDate).ToListAsync(ct),
            Revisions = await db.FaiRevisionHistory.AsNoTracking().Where(h => h.FaiId == faiId).OrderBy(h => h.FaiRevision).ToListAsync(ct),
            Accountability = acc,
            Allowed = allowed
        };
    }

    /// <summary>Create an FAI (any type). Requires fai.edit at the site.</summary>
    [HttpPost] [RequirePermission(Permissions.FaiEdit)]
    public async Task<object> Create(FaiInput r, CancellationToken ct) { var x = await svc.CreateAsync(r, ct); return new { x.Id, x.Number }; }

    private async Task<object> CreateTyped(FaiInput r, FaiType t, CancellationToken ct) { var x = await svc.CreateAsync(r with { Type = t }, ct); return new { x.Id, x.Number }; }
    [HttpPost("full")] [RequirePermission(Permissions.FaiEdit)] public Task<object> Full(FaiInput r, CancellationToken ct) => CreateTyped(r, FaiType.Full, ct);
    [HttpPost("partial")] [RequirePermission(Permissions.FaiEdit)] public Task<object> Partial(FaiInput r, CancellationToken ct) => CreateTyped(r, FaiType.Partial, ct);
    [HttpPost("delta")] [RequirePermission(Permissions.FaiEdit)] public Task<object> Delta(FaiInput r, CancellationToken ct) => CreateTyped(r, FaiType.Delta, ct);
    [HttpPost("engineering-change")] [RequirePermission(Permissions.FaiEdit)] public Task<object> EngineeringChange(FaiInput r, CancellationToken ct) => CreateTyped(r, FaiType.EngineeringChange, ct);
    [HttpPost("process-change")] [RequirePermission(Permissions.FaiEdit)] public Task<object> ProcessChange(FaiInput r, CancellationToken ct) => CreateTyped(r, FaiType.ProcessChange, ct);
    [HttpPost("material-change")] [RequirePermission(Permissions.FaiEdit)] public Task<object> MaterialChange(FaiInput r, CancellationToken ct) => CreateTyped(r, FaiType.MaterialChange, ct);
    [HttpPost("tooling-change")] [RequirePermission(Permissions.FaiEdit)] public Task<object> ToolingChange(FaiInput r, CancellationToken ct) => CreateTyped(r, FaiType.ToolingChange, ct);
    [HttpPost("supplier-change")] [RequirePermission(Permissions.FaiEdit)] public Task<object> SupplierChange(FaiInput r, CancellationToken ct) => CreateTyped(r, FaiType.SupplierChange, ct);
    [HttpPost("requalification")] [RequirePermission(Permissions.FaiEdit)] public Task<object> Requalification(FaiInput r, CancellationToken ct) => CreateTyped(r, FaiType.Requalification, ct);

    [HttpPut("{faiId:guid}")] [RequirePermission(Permissions.FaiEdit)]
    public async Task<IActionResult> Update(Guid faiId, FaiInput r, CancellationToken ct) { await svc.UpdateAsync(faiId, r, ct); return NoContent(); }
    /// <summary>Deletes (cancels) a draft without results; history is never removed.</summary>
    [HttpDelete("{faiId:guid}")] [RequirePermission(Permissions.FaiEdit)]
    public async Task<IActionResult> Delete(Guid faiId, CancellationToken ct) { await svc.CancelAsync(faiId, ct); return NoContent(); }
    [HttpPost("{faiId:guid}/copy")] [RequirePermission(Permissions.FaiEdit)]
    public async Task<object> Copy(Guid faiId, CopyRequest? r, CancellationToken ct) { var x = await svc.CopyAsync(faiId, r?.Type, ct); return new { x.Id, x.Number }; }
    [HttpPost("{faiId:guid}/clone")] [RequirePermission(Permissions.FaiEdit)]
    public async Task<object> Clone(Guid faiId, CloneRequest? r, CancellationToken ct) { var x = await svc.CloneAsync(faiId, r?.ChangedBalloons, ct); return new { x.Id, x.Number }; }
    [HttpPost("{faiId:guid}/reopen")] [RequirePermission(Permissions.FaiApprove)]
    public async Task<IActionResult> Reopen(Guid faiId, ReasonRequest r, CancellationToken ct) { await svc.ReopenAsync(faiId, r.Reason, ct); return NoContent(); }
    [HttpPost("{faiId:guid}/close")] [RequirePermission(Permissions.FaiApprove)]
    public async Task<IActionResult> Close(Guid faiId, NotesRequest? r, CancellationToken ct) { await svc.CloseAsync(faiId, r?.Notes, ct); return NoContent(); }

    // ---------- Workflow ----------

    [HttpPost("{faiId:guid}/start-inspection")] [RequirePermission(Permissions.FaiEdit)]
    public async Task<IActionResult> StartInspection(Guid faiId, NotesRequest? r, CancellationToken ct) { await svc.StartInspectionAsync(faiId, r?.Notes, ct); return NoContent(); }
    [HttpPost("{faiId:guid}/complete-inspection")] [RequirePermission(Permissions.FaiEdit)]
    public async Task<IActionResult> CompleteInspection(Guid faiId, CancellationToken ct) { await svc.CompleteInspectionAsync(faiId, ct); return NoContent(); }
    /// <summary>Inspector submits the completed FAI to the assigned reviewer.</summary>
    [HttpPost("{faiId:guid}/submit")] [RequirePermission(Permissions.FaiEdit)]
    public async Task<IActionResult> Submit(Guid faiId, CancellationToken ct) { await svc.SubmitAsync(faiId, ct); return NoContent(); }
    /// <summary>Reviewer accepts and starts the configured approval route (blocked by missing characteristics or unresolved failures).</summary>
    [HttpPost("{faiId:guid}/review")] [RequirePermission(Permissions.FaiEdit)]
    public async Task<IActionResult> Review(Guid faiId, NotesRequest? r, CancellationToken ct) { await svc.ReviewAsync(faiId, r?.Notes, ct); return NoContent(); }
    [HttpPost("{faiId:guid}/return")] [RequirePermission(Permissions.FaiEdit)]
    public async Task<IActionResult> Return(Guid faiId, ReturnFaiRequest r, CancellationToken ct) { await svc.ReturnAsync(faiId, r.Notes, ct); return NoContent(); }
    /// <summary>Sign the current approval step (e-signature). Requires the step's permission (fai.approve by default).</summary>
    [HttpPost("{faiId:guid}/approve")] [RequirePermission(Permissions.FaiApprove)]
    public async Task<IActionResult> Approve(Guid faiId, DecideFaiRequest r, CancellationToken ct) { await svc.DecideAsync(faiId, true, r.Password, r.Comment, ct); return NoContent(); }
    [HttpPost("{faiId:guid}/reject")] [RequirePermission(Permissions.FaiApprove)]
    public async Task<IActionResult> Reject(Guid faiId, DecideFaiRequest r, CancellationToken ct) { await svc.DecideAsync(faiId, false, r.Password, r.Comment, ct); return NoContent(); }
    /// <summary>Record customer approval (when required) on an approved FAI. Requires fai.approve.</summary>
    [HttpPost("{faiId:guid}/release")] [RequirePermission(Permissions.FaiApprove)]
    public async Task<IActionResult> Release(Guid faiId, ReleaseRequest r, CancellationToken ct) { await svc.ReleaseAsync(faiId, r.CustomerApprovalReference, r.CustomerApprovedOn, ct); return NoContent(); }

    private async Task<object> Approvals(Guid faiId, CancellationToken ct)
    {
        await m.ViewAsync(FaiService.RecordType, faiId, ct);
        return await db.ApprovalRequests.AsNoTracking().Where(r => r.RecordType == FaiService.RecordType && r.RecordId == faiId).OrderBy(r => r.CreatedAt)
            .Select(r => new { r.Id, r.Status, r.CurrentStep, SubmittedAt = r.CreatedAt, r.CompletedAt, r.SubmittedById,
                Steps = db.ApprovalRouteSteps.Where(s => s.RouteId == r.RouteId).OrderBy(s => s.Order).Select(s => new { s.Order, s.Name, s.RequiredPermission }).ToList(),
                Decisions = r.Decisions.OrderBy(d => d.SignedAt).Select(d => new { d.StepOrder, d.StepName, d.SignerName, d.Approved, d.Meaning, d.Comment, d.SignedAt }).ToList() }).ToListAsync(ct);
    }

    /// <summary>Workflow state: status, accountability and approval requests with steps and signatures.</summary>
    [HttpGet("{faiId:guid}/workflow")] [RequirePermission(Permissions.FaiView)]
    public async Task<object> Workflow(Guid faiId, CancellationToken ct)
    {
        var a = await Approvals(faiId, ct);
        var x = await db.Fais.AsNoTracking().FirstAsync(f => f.Id == faiId, ct);
        return new { x.Status, x.FaiRevision, x.InspectionStartedAt, x.InspectionCompletedAt, x.ApprovedAt, x.ClosedAt, Accountability = await svc.AccountabilityAsync(faiId, ct), Approvals = a };
    }

    /// <summary>Status changes, submissions and signatures in time order (from the shared audit and workflow engines).</summary>
    [HttpGet("{faiId:guid}/history")] [RequirePermission(Permissions.FaiView)]
    public Task<List<HistoryItem>> History(Guid faiId, CancellationToken ct) => WorkflowHistory(faiId, ct);
    [HttpGet("{faiId:guid}/workflow-history")] [RequirePermission(Permissions.FaiView)]
    public async Task<List<HistoryItem>> WorkflowHistory(Guid faiId, CancellationToken ct)
    {
        await m.ViewAsync(FaiService.RecordType, faiId, ct);
        return await HistoryService.WorkflowAsync(db, FaiService.RecordType, faiId, [nameof(FaiRecord)], ct);
    }

    /// <summary>Every audited change on the FAI, its characteristics, measurements, ballooned drawings and revisions.</summary>
    [HttpGet("{faiId:guid}/audit-history")] [RequirePermission(Permissions.FaiView)]
    public async Task<object> AuditHistory(Guid faiId, CancellationToken ct)
    {
        await m.ViewAsync(FaiService.RecordType, faiId, ct);
        var ids = new List<Guid> { faiId };
        ids.AddRange(await db.FaiCharacteristics.Where(c => c.FaiId == faiId).Select(c => c.Id).ToListAsync(ct));
        ids.AddRange(await db.FaiCharacteristicResults.Where(c => c.FaiId == faiId).Select(c => c.Id).ToListAsync(ct));
        ids.AddRange(await db.FaiBalloonDrawings.Where(c => c.FaiId == faiId).Select(c => c.Id).ToListAsync(ct));
        ids.AddRange(await db.FaiRevisionHistory.Where(c => c.FaiId == faiId).Select(c => c.Id).ToListAsync(ct));
        return await db.AuditEntries.AsNoTracking().Where(e => ids.Contains(e.EntityId)).OrderBy(e => e.OccurredAt)
            .Select(e => new { e.Id, e.OccurredAt, e.UserName, e.Action, e.EntityType, e.EntityId, e.Description, e.Changes }).ToListAsync(ct);
    }

    // ---------- Approvals ----------

    [HttpGet("{faiId:guid}/approvals")] [RequirePermission(Permissions.FaiView)]
    public Task<object> ApprovalsList(Guid faiId, CancellationToken ct) => Approvals(faiId, ct);
    [HttpGet("{faiId:guid}/approvals/history")] [RequirePermission(Permissions.FaiView)]
    public Task<object> ApprovalsHistory(Guid faiId, CancellationToken ct) => Approvals(faiId, ct);
    /// <summary>Request approval: the reviewer's acceptance that starts the approval route.</summary>
    [HttpPost("{faiId:guid}/approvals/request")] [RequirePermission(Permissions.FaiEdit)]
    public Task<IActionResult> ApprovalsRequest(Guid faiId, NotesRequest? r, CancellationToken ct) => Review(faiId, r, ct);
    [HttpPost("{faiId:guid}/approvals/approve")] [RequirePermission(Permissions.FaiApprove)]
    public Task<IActionResult> ApprovalsApprove(Guid faiId, DecideFaiRequest r, CancellationToken ct) => Approve(faiId, r, ct);
    [HttpPost("{faiId:guid}/approvals/reject")] [RequirePermission(Permissions.FaiApprove)]
    public Task<IActionResult> ApprovalsReject(Guid faiId, DecideFaiRequest r, CancellationToken ct) => Reject(faiId, r, ct);
    /// <summary>An approver returns the FAI for correction (signed "not approved" decision). Requires fai.approve and the signer's password.</summary>
    [HttpPost("{faiId:guid}/approvals/return")] [RequirePermission(Permissions.FaiApprove)]
    public async Task<IActionResult> ApprovalsReturn(Guid faiId, ReturnFaiRequest r, CancellationToken ct) { await svc.ReturnFromApprovalAsync(faiId, r.Password ?? "", r.Notes, ct); return NoContent(); }

    // ---------- Balloon drawings ----------

    [HttpGet("{faiId:guid}/balloon-drawings")] [RequirePermission(Permissions.FaiView)]
    public async Task<object> BalloonDrawings(Guid faiId, CancellationToken ct)
    {
        await m.ViewAsync(FaiService.RecordType, faiId, ct);
        return await db.FaiBalloonDrawings.AsNoTracking().Where(b => b.FaiId == faiId).OrderBy(b => b.UploadDate)
            .Select(b => new { b.Id, b.DrawingId, b.DrawingRevision, b.FileAttachmentId, b.UploadDate, DrawingNumber = db.Documents.Where(d => d.Id == b.DrawingId).Select(d => d.Number).First(),
                FileName = db.Attachments.Where(a => a.Id == b.FileAttachmentId).Select(a => a.FileName).FirstOrDefault() }).ToListAsync(ct);
    }
    [HttpPost("{faiId:guid}/balloon-drawings")] [RequirePermission(Permissions.FaiEdit)]
    public async Task<object> AddBalloonDrawing(Guid faiId, BalloonDrawingInput r, CancellationToken ct) { var b = await svc.AddBalloonDrawingAsync(faiId, r, ct); return new { b.Id }; }
    [HttpPut("{faiId:guid}/balloon-drawings/{drawingId:guid}")] [RequirePermission(Permissions.FaiEdit)]
    public async Task<IActionResult> UpdateBalloonDrawing(Guid faiId, Guid drawingId, BalloonDrawingInput r, CancellationToken ct) { await svc.UpdateBalloonDrawingAsync(faiId, drawingId, r, ct); return NoContent(); }
    [HttpDelete("{faiId:guid}/balloon-drawings/{drawingId:guid}")] [RequirePermission(Permissions.FaiEdit)]
    public async Task<IActionResult> DeleteBalloonDrawing(Guid faiId, Guid drawingId, CancellationToken ct) { await svc.DeleteBalloonDrawingAsync(faiId, drawingId, ct); return NoContent(); }

    /// <summary>Upload a ballooned drawing file (shared attachment engine: malware scan, hashing, versioning) and register it against the drawing.</summary>
    [HttpPost("{faiId:guid}/balloon-drawings/upload")] [RequirePermission(Permissions.FaiEdit)] [RequestSizeLimit(200L * 1024 * 1024)]
    public async Task<object> UploadBalloonDrawing(Guid faiId, IFormFile file, [FromForm] Guid drawingId, [FromForm] string? drawingRevision, [FromForm] bool importCharacteristics, CancellationToken ct)
    {
        await svc.LoadEditAsync(faiId, ct);
        await using var s = file.OpenReadStream();
        var a = await files.UploadAsync(FaiService.RecordType, faiId, new UploadRequest(file.FileName, file.ContentType, s, "Ballooned drawing", null, Domain.Shared.Classification.Internal, null), ct);
        var b = await svc.AddBalloonDrawingAsync(faiId, new BalloonDrawingInput(drawingId, drawingRevision, a.Id, importCharacteristics), ct);
        return new { b.Id, AttachmentId = a.Id };
    }

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
    [HttpGet("{faiId:guid}/characteristics")] [RequirePermission(Permissions.FaiView)]
    public async Task<List<FaiCharacteristic>> Characteristics(Guid faiId, CancellationToken ct) => await (await Chars(faiId, ct)).ToListAsync(ct);
    [HttpGet("{faiId:guid}/characteristics/open")] [RequirePermission(Permissions.FaiView)]
    public async Task<List<FaiCharacteristic>> OpenCharacteristics(Guid faiId, CancellationToken ct) => await (await Chars(faiId, ct)).Where(c => c.Result == CharResult.NotInspected).ToListAsync(ct);
    [HttpGet("{faiId:guid}/characteristics/failed")] [RequirePermission(Permissions.FaiView)]
    public async Task<List<FaiCharacteristic>> FailedCharacteristics(Guid faiId, CancellationToken ct) => await (await Chars(faiId, ct)).Where(c => c.Result == CharResult.Fail).ToListAsync(ct);
    /// <summary>Balloons on the drawing's balloon map that are missing from the FAI, and gaps in balloon numbering.</summary>
    [HttpGet("{faiId:guid}/characteristics/missing")] [RequirePermission(Permissions.FaiView)]
    public async Task<object> MissingCharacteristics(Guid faiId, CancellationToken ct)
    {
        await m.ViewAsync(FaiService.RecordType, faiId, ct);
        var a = await svc.AccountabilityAsync(faiId, ct);
        var x = await db.Fais.AsNoTracking().FirstAsync(f => f.Id == faiId, ct);
        var balloons = await db.DrawingBalloons.AsNoTracking().Where(b => b.DrawingId == x.DrawingId && b.DrawingRevision == x.DrawingRevision && a.Missing.Contains(b.BalloonNumber)).OrderBy(b => b.BalloonNumber).ToListAsync(ct);
        return new { a.Missing, a.Gaps, Balloons = balloons };
    }
    [HttpGet("{faiId:guid}/characteristics/{characteristicId:guid}")] [RequirePermission(Permissions.FaiView)]
    public async Task<FaiCharacteristic> Characteristic(Guid faiId, Guid characteristicId, CancellationToken ct) =>
        await (await Chars(faiId, ct)).FirstOrDefaultAsync(c => c.Id == characteristicId, ct) ?? throw new NotFoundException("Characteristic");
    [HttpPost("{faiId:guid}/characteristics")] [RequirePermission(Permissions.FaiEdit)]
    public async Task<object> AddCharacteristic(Guid faiId, CharacteristicInput2 r, CancellationToken ct) { var c = await svc.AddCharacteristicAsync(faiId, r, ct); return new { c.Id }; }
    [HttpPut("{faiId:guid}/characteristics/{characteristicId:guid}")] [RequirePermission(Permissions.FaiEdit)]
    public async Task<IActionResult> UpdateCharacteristic(Guid faiId, Guid characteristicId, CharacteristicInput2 r, CancellationToken ct) { await svc.UpdateCharacteristicAsync(faiId, characteristicId, r, ct); return NoContent(); }
    [HttpDelete("{faiId:guid}/characteristics/{characteristicId:guid}")] [RequirePermission(Permissions.FaiEdit)]
    public async Task<IActionResult> DeleteCharacteristic(Guid faiId, Guid characteristicId, CancellationToken ct) { await svc.DeleteCharacteristicAsync(faiId, characteristicId, ct); return NoContent(); }

    // ---------- Results ----------

    [HttpPost("{faiId:guid}/characteristics/{characteristicId:guid}/results")] [RequirePermission(Permissions.FaiEdit)]
    public async Task<object> AddResult(Guid faiId, Guid characteristicId, ResultInput2 r, CancellationToken ct) { var x = await svc.RecordResultAsync(faiId, characteristicId, r, ct); return new { x.Id, x.InspectionSequence, x.PassFail }; }
    [HttpGet("{faiId:guid}/characteristics/{characteristicId:guid}/results")] [RequirePermission(Permissions.FaiView)]
    public async Task<object> Results(Guid faiId, Guid characteristicId, CancellationToken ct)
    {
        await m.ViewAsync(FaiService.RecordType, faiId, ct);
        return await db.FaiCharacteristicResults.AsNoTracking().Where(v => v.FaiId == faiId && v.CharacteristicId == characteristicId).OrderBy(v => v.InspectionSequence)
            .Select(v => new { v.Id, v.InspectionSequence, v.MeasuredValue, v.Observation, v.PassFail, v.InspectionDate, v.EquipmentNumber, v.EquipmentCalibrationDueOn, v.Evidence, v.Notes,
                Inspector = db.Users.Where(u => u.Id == v.InspectorId).Select(u => u.DisplayName).First() }).ToListAsync(ct);
    }
    /// <summary>Correct the latest measurement (kept as a new measurement; the original remains in history).</summary>
    [HttpPut("{faiId:guid}/results/{resultId:guid}")] [RequirePermission(Permissions.FaiEdit)]
    public async Task<IActionResult> UpdateResult(Guid faiId, Guid resultId, ResultInput2 r, CancellationToken ct) { await svc.UpdateResultAsync(faiId, resultId, r, ct); return NoContent(); }
    [HttpDelete("{faiId:guid}/results/{resultId:guid}")] [RequirePermission(Permissions.FaiEdit)]
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
    [HttpGet("{faiId:guid}/attachments")] [RequirePermission(Permissions.FaiView)]
    public Task<IEnumerable<RecordsController.AttachmentDto>> Attachments(Guid faiId, [FromQuery] bool history, CancellationToken ct) => Shared().Files(FaiService.RecordType, faiId, history, ct);
    [HttpPost("{faiId:guid}/attachments")] [RequirePermission(Permissions.FaiEdit)] [RequestSizeLimit(200L * 1024 * 1024)]
    public Task<ActionResult<RecordsController.AttachmentDto>> Upload(Guid faiId, IFormFile file, [FromForm] string? category, [FromForm] string? description, [FromForm] Domain.Shared.Classification classification, [FromForm] Guid? replacesId, CancellationToken ct) =>
        Shared().Upload(FaiService.RecordType, faiId, file, category, description, classification, replacesId, ct);
    [HttpGet("{faiId:guid}/attachments/{attachmentId:guid}")] [RequirePermission(Permissions.FaiView)]
    public async Task<IActionResult> Attachment(Guid faiId, Guid attachmentId, CancellationToken ct)
    {
        await m.ViewAsync(FaiService.RecordType, faiId, ct);
        if (!await db.Attachments.AnyAsync(a => a.Id == attachmentId && a.OwnerType == FaiService.RecordType && a.OwnerId == faiId, ct)) throw new NotFoundException("File");
        var (meta, content) = await files.OpenAsync(attachmentId, ct);
        return File(content, meta.ContentType, meta.FileName);
    }
    [HttpDelete("{faiId:guid}/attachments/{attachmentId:guid}")] [RequirePermission(Permissions.FaiEdit)]
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

    [HttpPost("{faiId:guid}/create-ncr")] [RequirePermission(Permissions.NcrEdit)]
    public async Task<object> CreateNcr(Guid faiId, CreateNcrFromRequest? r, CancellationToken ct) { var n = await svc.CreateNcrAsync(faiId, r?.CharacteristicIds, r?.Title, ct); return new { n.Id, n.Number }; }
    [HttpPost("{faiId:guid}/create-capa")] [RequirePermission(Permissions.CapaEdit)]
    public async Task<object> CreateCapa(Guid faiId, CreateCapaFromRequest r, CancellationToken ct) { var c = await svc.CreateCapaAsync(faiId, r.Title, r.ProblemStatement, ct); return new { c.Id, c.Number }; }
    [HttpGet("{faiId:guid}/ncrs")] [RequirePermission(Permissions.FaiView)]
    public async Task<IEnumerable<RecordsController.LinkDto>> Ncrs(Guid faiId, CancellationToken ct) => (await Shared().Links(FaiService.RecordType, faiId, ct)).Where(l => l.Type == "Ncr");
    [HttpGet("{faiId:guid}/capas")] [RequirePermission(Permissions.FaiView)]
    public async Task<IEnumerable<RecordsController.LinkDto>> Capas(Guid faiId, CancellationToken ct) => (await Shared().Links(FaiService.RecordType, faiId, ct)).Where(l => l.Type == "Capa");
    /// <summary>All linked records the caller can view (products, parts, drawings, changes, NCRs, CAPAs, prior FAIs …) plus the header relationships.</summary>
    [HttpGet("{faiId:guid}/linked-records")] [RequirePermission(Permissions.FaiView)]
    public async Task<object> LinkedRecords(Guid faiId, CancellationToken ct)
    {
        var links = await Shared().Links(FaiService.RecordType, faiId, ct);
        var x = await db.Fais.AsNoTracking().FirstAsync(f => f.Id == faiId, ct);
        return new { Links = links, x.ProductId, x.PartId, x.DrawingId, x.SpecificationId, x.SupplierId, x.CustomerId, x.ChangeId, x.InspectionPlanId, x.BaseFaiId };
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
        var detail = await Get(faiId, ct);
        var results = await db.FaiCharacteristicResults.AsNoTracking().Where(v => v.FaiId == faiId).OrderBy(v => v.InspectionSequence).ToListAsync(ct);
        return new { Detail = detail, Results = results, Approvals = await Approvals(faiId, ct) };
    }

    // ---------- Bulk ----------

    /// <summary>Close many FAIs; each id runs the single close action with the same permission, scope and rule checks.</summary>
    [HttpPost("bulk-close")] [RequirePermission(Permissions.FaiApprove)]
    public Task<List<BulkOutcome>> BulkClose(IdsRequest r, CancellationToken ct) => svc.BulkAsync(r.Ids, id => svc.CloseAsync(id, r.Notes, ct));
    /// <summary>Sign the current approval step of many FAIs with one e-signature; each id is checked individually.</summary>
    [HttpPost("bulk-approve")] [RequirePermission(Permissions.FaiApprove)]
    public Task<List<BulkOutcome>> BulkApprove(IdsRequest r, CancellationToken ct) => svc.BulkAsync(r.Ids, id => svc.DecideAsync(id, true, r.Password ?? "", r.Notes, ct));
}
