using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Complaints;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Controllers;

[ApiController, Authorize, Route("api/complaints")]
public sealed class ComplaintsController(AppDbContext db, ComplaintService svc, ModuleSupport m) : ControllerBase
{
    public sealed record LinkedRecordRequest(string Title, string? Description);

    [HttpGet]
    [RequirePermission(Permissions.ComplaintsView)]
    public async Task<object> List([FromQuery] string? q, [FromQuery] ComplaintStatus? status, [FromQuery] bool openOnly = false, [FromQuery] Guid? customerId = null, [FromQuery] Guid? partId = null, [FromQuery] Guid? productId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var query = await m.ScopeAsync(db.Complaints.AsNoTracking(), Permissions.ComplaintsView, ct);
        if (status is not null) query = query.Where(x => x.Status == status);
        if (openOnly) query = query.Where(x => x.Status != ComplaintStatus.Closed && x.Status != ComplaintStatus.Cancelled);
        if (customerId is not null) query = query.Where(x => x.CustomerId == customerId);
        if (partId is not null) query = query.Where(x => x.PartId == partId);
        if (productId is not null) query = query.Where(x => x.ProductId == productId);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.Number + " " + x.Title + " " + (x.PartNumber ?? "") + " " + (x.LotNumber ?? "") + " " + (x.SerialNumber ?? "") + " " + (x.CustomerReference ?? "") + " " + (x.ReturnAuthorization ?? "") + " " + (x.FailureMode ?? ""), $"%{q.Trim()}%"));
        return await Paging.PageAsync(query.OrderByDescending(x => x.CreatedAt).Select(x => new
        {
            x.Id, x.Number, x.Title, x.Status, x.Severity, x.ComplaintDate, x.ResponseDueOn, x.PartNumber, x.FailureMode, x.ReturnAuthorization,
            Customer = db.Customers.Where(c => c.Id == x.CustomerId).Select(c => c.Code + " " + c.Name).First(),
            OwnerName = db.Users.Where(u => u.Id == x.OwnerId).Select(u => u.DisplayName).First()
        }), page, pageSize, ct);
    }

    /// <summary>Complaint trends for the records the user may see: by month, failure mode, customer and product.</summary>
    [HttpGet("trends")]
    [RequirePermission(Permissions.ComplaintsView)]
    public async Task<object> Trends([FromQuery] int months = 12, CancellationToken ct = default)
    {
        var query = await m.ScopeAsync(db.Complaints.AsNoTracking(), Permissions.ComplaintsView, ct);
        var today = await m.TodayAsync(null, ct);
        var from = new DateOnly(today.Year, today.Month, 1).AddMonths(-(Math.Clamp(months, 1, 60) - 1));
        var rows = await query.Where(x => x.ComplaintDate >= from && x.Status != ComplaintStatus.Cancelled)
            .Select(x => new { x.ComplaintDate, x.FailureMode, x.CustomerId, x.ProductId, x.Severity, x.Status }).ToListAsync(ct);
        var customers = await db.Customers.Where(c => rows.Select(r => r.CustomerId).Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code + " " + c.Name, ct);
        var productIds = rows.Where(r => r.ProductId != null).Select(r => r.ProductId!.Value).Distinct().ToList();
        var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Number + " " + p.Name, ct);
        var byMonth = Enumerable.Range(0, Math.Clamp(months, 1, 60)).Select(i => from.AddMonths(i))
            .Select(mo => new { Month = mo.ToString("yyyy-MM"), Count = rows.Count(r => r.ComplaintDate.Year == mo.Year && r.ComplaintDate.Month == mo.Month), Critical = rows.Count(r => r.ComplaintDate.Year == mo.Year && r.ComplaintDate.Month == mo.Month && r.Severity == Severity.Critical) }).ToList();
        static IEnumerable<object> Top<TK>(IEnumerable<TK> keys, Func<TK, string> label) => keys.GroupBy(k => k).Select(g => new { Label = label(g.Key), Count = g.Count() }).OrderByDescending(x => x.Count).Take(10);
        return new
        {
            From = from, Total = rows.Count, Open = rows.Count(r => r.Status is not ComplaintStatus.Closed),
            ByMonth = byMonth,
            ByFailureMode = Top(rows.Select(r => r.FailureMode ?? "(not classified)"), k => k),
            ByCustomer = Top(rows.Select(r => r.CustomerId), k => customers.GetValueOrDefault(k, "?")),
            ByProduct = Top(rows.Select(r => r.ProductId), k => k is null ? "(no product)" : products.GetValueOrDefault(k.Value, "?")),
        };
    }

    [HttpPost]
    [RequirePermission(Permissions.ComplaintsEdit)]
    public async Task<object> Create(ComplaintRequest r, CancellationToken ct) { var c = await svc.CreateAsync(r, ct); return new { c.Id, c.Number }; }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, ComplaintRequest r, CancellationToken ct) { await svc.UpdateAsync(id, r, ct); return NoContent(); }

    [HttpGet("{id:guid}")]
    public async Task<object> Get(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(ComplaintService.RecordType, id, ct);
        var c = await db.Complaints.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var canEdit = await m.CanAsync(Permissions.ComplaintsEdit, c.SiteId, c.DepartmentId, ct);
        var canApprove = await m.CanAsync(Permissions.ComplaintsApprove, c.SiteId, c.DepartmentId, ct);
        var s = c.Status;
        var active = s is not (ComplaintStatus.Closed or ComplaintStatus.Cancelled);
        var linkIds = await db.RecordLinks.Where(l => (l.FromType == ComplaintService.RecordType && l.FromId == id) || (l.ToType == ComplaintService.RecordType && l.ToId == id))
            .Select(l => l.FromId == id ? new { Type = l.ToType, Id = l.ToId } : new { Type = l.FromType, Id = l.FromId }).ToListAsync(ct);
        var ncrIds = linkIds.Where(l => l.Type == NcrService.RecordType).Select(l => l.Id).ToList();
        var capaIds = linkIds.Where(l => l.Type == CapaService.RecordType).Select(l => l.Id).ToList();
        var ncrQ = await m.TryScopeAsync(db.Ncrs.AsNoTracking(), Permissions.NcrView, ct);
        var capaQ = await m.TryScopeAsync(db.Capas.AsNoTracking(), Permissions.CapaView, ct);
        var allowed = Allowed.Of(("edit", canEdit && active), ("contain", canEdit && s is ComplaintStatus.Open or ComplaintStatus.Investigation),
            ("receiveReturn", canEdit && active), ("evaluateReturn", canEdit && active && c.ReturnReceivedOn != null),
            ("investigate", canEdit && s is ComplaintStatus.Open or ComplaintStatus.Investigation && c.Containment != null),
            ("respond", canEdit && s == ComplaintStatus.Investigation && c.RootCause != null && (!c.ReturnExpected || c.ReturnEvaluation != null)),
            ("acceptance", canEdit && s == ComplaintStatus.AwaitingCustomer),
            ("verify", canApprove && c.OwnerId != m.UserId && s == ComplaintStatus.Verification && c.VerifiedAt == null),
            ("close", canEdit && s == ComplaintStatus.Verification && c.VerifiedAt != null && await Allowed.ActionsClosedAsync(db, ComplaintService.RecordType, id, false, ct)),
            ("reopen", canApprove && s == ComplaintStatus.Closed), ("cancel", canEdit && active),
            ("ncr", canEdit && active && await m.CanAsync(Permissions.NcrEdit, c.SiteId, c.DepartmentId, ct)),
            ("capa", canEdit && active && await m.CanAsync(Permissions.CapaEdit, c.SiteId, c.DepartmentId, ct)),
            ("actions", canEdit && active));
        return new
        {
            c.Id, c.Number, c.Title, c.Description, c.CustomerId, c.CustomerContact, c.CustomerReference, c.ComplaintDate, c.ReportedOn, c.ProductId, c.PartId, c.PartNumber, c.PartRevision,
            c.LotNumber, c.BatchNumber, c.SerialNumber, c.Quantity, c.FailureMode, c.Severity, c.Risk, c.Warranty, c.SupplierId, c.SiteId, c.DepartmentId, c.OwnerId, c.ResponseDueOn, c.Status,
            c.Containment, c.ContainedAt, c.ReturnAuthorization, c.ReturnExpected, c.ReturnReceivedOn, c.ReturnQuantity, c.ReturnCondition, c.ReturnEvaluation, c.TestResults, c.FailureConfirmed,
            c.Investigation, c.RootCause, c.CorrectiveAction, c.CustomerResponse, c.ResponseSentOn, c.CustomerAccepted, c.CustomerFeedback, c.AcceptedOn,
            c.VerificationNotes, c.VerifiedAt, c.ClosureNotes, c.ClosedAt, c.ReopenCount, c.ReopenReason, c.RowVersion, c.CreatedAt,
            CustomerLabel = await db.Customers.Where(x => x.Id == c.CustomerId).Select(x => x.Code + " " + x.Name).FirstAsync(ct),
            ProductLabel = c.ProductId == null ? null : await db.Products.Where(x => x.Id == c.ProductId).Select(x => x.Number + " " + x.Name).FirstAsync(ct),
            PartLabel = c.PartId == null ? null : await db.Parts.Where(x => x.Id == c.PartId).Select(x => x.PartNumber + " " + x.Name).FirstAsync(ct),
            SupplierLabel = c.SupplierId == null ? null : await db.Suppliers.Where(x => x.Id == c.SupplierId).Select(x => x.Code + " " + x.LegalName).FirstAsync(ct),
            OwnerName = await db.Users.Where(u => u.Id == c.OwnerId).Select(u => u.DisplayName).FirstAsync(ct),
            VerifiedByName = c.VerifiedById == null ? null : await db.Users.Where(u => u.Id == c.VerifiedById).Select(u => u.DisplayName).FirstAsync(ct),
            SiteCode = await db.Sites.Where(x => x.Id == c.SiteId).Select(x => x.Code).FirstAsync(ct),
            Ncrs = ncrQ == null ? null : await ncrQ.Where(n => ncrIds.Contains(n.Id)).Select(n => new { n.Id, n.Number, n.Title, n.Status }).ToListAsync(ct),
            Capas = capaQ == null ? null : await capaQ.Where(n => capaIds.Contains(n.Id)).Select(n => new { n.Id, n.Number, n.Title, n.Status }).ToListAsync(ct),
            CanEdit = canEdit, Allowed = allowed
        };
    }

    [HttpPost("{id:guid}/containment")] public async Task<IActionResult> Contain(Guid id, TextRequest r, CancellationToken ct) { await svc.ContainAsync(id, r.Text, ct); return NoContent(); }
    [HttpPost("{id:guid}/return")] public async Task<IActionResult> Return(Guid id, ReturnReceiptRequest r, CancellationToken ct) { await svc.ReceiveReturnAsync(id, r, ct); return NoContent(); }
    [HttpPost("{id:guid}/return-evaluation")] public async Task<IActionResult> Evaluate(Guid id, ReturnEvaluationRequest r, CancellationToken ct) { await svc.EvaluateReturnAsync(id, r, ct); return NoContent(); }
    [HttpPost("{id:guid}/investigation")] public async Task<IActionResult> Investigate(Guid id, InvestigationRequest r, CancellationToken ct) { await svc.InvestigateAsync(id, r, ct); return NoContent(); }
    [HttpPost("{id:guid}/response")] public async Task<IActionResult> Respond(Guid id, CustomerResponseRequest r, CancellationToken ct) { await svc.RespondAsync(id, r, ct); return NoContent(); }
    [HttpPost("{id:guid}/acceptance")] public async Task<IActionResult> Acceptance(Guid id, CustomerAcceptanceRequest r, CancellationToken ct) { await svc.RecordAcceptanceAsync(id, r, ct); return NoContent(); }
    [HttpPost("{id:guid}/verify")] public async Task<IActionResult> Verify(Guid id, TextRequest r, CancellationToken ct) { await svc.VerifyAsync(id, r.Text, ct); return NoContent(); }
    [HttpPost("{id:guid}/close")] public async Task<IActionResult> Close(Guid id, TextRequest r, CancellationToken ct) { await svc.CloseAsync(id, r.Text, ct); return NoContent(); }
    [HttpPost("{id:guid}/reopen")] public async Task<IActionResult> Reopen(Guid id, TextRequest r, CancellationToken ct) { await svc.ReopenAsync(id, r.Text, ct); return NoContent(); }
    [HttpPost("{id:guid}/cancel")] public async Task<IActionResult> Cancel(Guid id, TextRequest r, CancellationToken ct) { await svc.CancelAsync(id, r.Text, ct); return NoContent(); }
    [HttpPost("{id:guid}/ncr")] public async Task<object> CreateNcr(Guid id, LinkedRecordRequest r, CancellationToken ct) { var n = await svc.CreateNcrAsync(id, r.Title, r.Description, ct); return new { n.Id, n.Number }; }
    [HttpPost("{id:guid}/capa")] public async Task<object> CreateCapa(Guid id, LinkedRecordRequest r, CancellationToken ct) { var c = await svc.CreateCapaAsync(id, r.Title, r.Description ?? "", ct); return new { c.Id, c.Number }; }
}
