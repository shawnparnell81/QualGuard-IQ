using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Infrastructure.Modules.Documents;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/documents")]
public sealed class DocumentsController(AppDbContext db, DocumentService docs, IAccessService access, ICurrentUser current) : ControllerBase
{
    public sealed record DocumentListItem(Guid Id, DocumentKind Kind, string Number, string Title, string DocumentType, DocumentStatus Status,
        string? CurrentRevision, DateOnly? EffectiveDate, string SiteCode, string? PartNumber, DateOnly? NextReviewDue, string OwnerName, bool IsReference = false);
    public sealed record RevisionDto(Guid Id, string Revision, int Sequence, RevisionStatus Status, string ChangeSummary, DateOnly? EffectiveDate,
        DateOnly? SupersededOn, DateTimeOffset? ApprovedAt, bool TrainingRequired, string? ObsoleteReason, uint RowVersion, string? FieldSchema = null, bool ReadyToSubmit = false);
    public sealed record DocumentDetail(Guid Id, DocumentKind Kind, string Number, string Title, string DocumentType, DocumentStatus Status, Guid SiteId,
        Guid? DepartmentId, Guid OwnerId, string OwnerName, int ReviewIntervalMonths, DateOnly? NextReviewDue, string? PartNumber, string? DrawingSize,
        int? SheetCount, uint RowVersion, IReadOnlyList<RevisionDto> Revisions, bool CanEdit, bool CanApprove, string? FormFamily = null, bool IsReference = false);
    public sealed record ReviseRequest(string? Revision, string ChangeSummary);
    public sealed record ObsoleteRequest(string Reason);
    public sealed record Page<T>(IReadOnlyList<T> Items, int Total, int PageNumber, int PageSize);

    [HttpGet]
    public async Task<Page<DocumentListItem>> List([FromQuery] DocumentKind kind, [FromQuery] string? q, [FromQuery] DocumentStatus? status, [FromQuery] Guid? siteId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var (view, _, _) = DocumentRecordTypes.Perms(kind);
        var scope = await access.GetScopeAsync(current.UserId!.Value, view, ct);
        if (scope.IsEmpty) throw new ForbiddenException();
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(1, page);
        var query = db.Documents.AsNoTracking().Where(d => d.Kind == kind && !d.IsArchived);
        if (!scope.AllSites)
        {
            var sites = scope.SiteIds.ToList();
            var pairs = scope.DepartmentPairs.Select(p => p.Site.ToString() + "|" + p.Dept.ToString()).ToList();
            query = query.Where(d => sites.Contains(d.SiteId) || (d.DepartmentId != null && pairs.Contains(d.SiteId.ToString() + "|" + d.DepartmentId.ToString())));
        }
        query = await query.VisibleToAsync(access, current.UserId!.Value, ct);
        if (status is not null) query = query.Where(d => d.Status == status);
        if (siteId is not null) query = query.Where(d => d.SiteId == siteId);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{q.Trim()}%";
            query = query.Where(d => EF.Functions.ILike(d.Number, like) || EF.Functions.ILike(d.Title, like) || (d.PartNumber != null && EF.Functions.ILike(d.PartNumber, like)));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(d => d.Number).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(d => new DocumentListItem(d.Id, d.Kind, d.Number, d.Title, d.DocumentType, d.Status,
                d.Revisions.Where(r => r.Status == RevisionStatus.Effective).Select(r => r.Revision).FirstOrDefault(),
                d.Revisions.Where(r => r.Status == RevisionStatus.Effective).Select(r => r.EffectiveDate).FirstOrDefault(),
                db.Sites.Where(s => s.Id == d.SiteId).Select(s => s.Code).First(), d.PartNumber, d.NextReviewDue,
                db.Users.Where(u => u.Id == d.OwnerId).Select(u => u.DisplayName).First(), d.IsReference))
            .ToListAsync(ct);
        return new Page<DocumentListItem>(items, total, page, pageSize);
    }

    [HttpGet("{id:guid}")]
    public async Task<DocumentDetail> Get(Guid id, CancellationToken ct)
    {
        var d = await db.Documents.AsNoTracking().Include(x => x.Revisions).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Document");
        var (view, edit, approve) = DocumentRecordTypes.Perms(d.Kind);
        var uid = current.UserId!.Value;
        if (!await access.CanAccessAsync(uid, view, d.SiteId, d.DepartmentId, ct) || !await ConfidentialContent.CanSeeAsync(access, uid, d, ct)) throw new NotFoundException("Document");
        var owner = await db.Users.Where(u => u.Id == d.OwnerId).Select(u => u.DisplayName).FirstAsync(ct);
        var revType = DocumentRecordTypes.ForRevision(d.Kind);
        var revIds = d.Revisions.Select(r => r.Id).ToList();
        var withFiles = (await db.Attachments.Where(a => a.OwnerType == revType && revIds.Contains(a.OwnerId) && !a.IsArchived).Select(a => a.OwnerId).Distinct().ToListAsync(ct)).ToHashSet();
        bool Ready(DocumentRevision r) => r.Status == RevisionStatus.Draft && (d.Kind == DocumentKind.FormTemplate ? !string.IsNullOrWhiteSpace(r.FieldSchema) : withFiles.Contains(r.Id));
        return new DocumentDetail(d.Id, d.Kind, d.Number, d.Title, d.DocumentType, d.Status, d.SiteId, d.DepartmentId, d.OwnerId, owner, d.ReviewIntervalMonths,
            d.NextReviewDue, d.PartNumber, d.DrawingSize, d.SheetCount, d.RowVersion,
            d.Revisions.OrderByDescending(r => r.Sequence).Select(r => new RevisionDto(r.Id, r.Revision, r.Sequence, r.Status, r.ChangeSummary, r.EffectiveDate,
                r.SupersededOn, r.ApprovedAt, r.TrainingRequired, r.ObsoleteReason, r.RowVersion, r.FieldSchema, Ready(r))).ToList(),
            await access.CanAccessAsync(uid, edit, d.SiteId, d.DepartmentId, ct), await access.CanAccessAsync(uid, approve, d.SiteId, d.DepartmentId, ct),
            // form templates: the family look a revision without its own look is shown and printed with, so blank previews match filled records
            d.Kind == DocumentKind.FormTemplate ? await QualGuardIQ.Infrastructure.Modules.Quality.FormFamily.InferAsync(db, d.Id, ct) : null, d.IsReference);
    }

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreateDocumentRequest req, CancellationToken ct) => (await docs.CreateAsync(req, ct)).Id;

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateDocumentRequest req, CancellationToken ct) { await docs.UpdateAsync(id, req, ct); return NoContent(); }

    [HttpPut("revisions/{revisionId:guid}")]
    public async Task<IActionResult> UpdateRevision(Guid revisionId, UpdateRevisionRequest req, CancellationToken ct) { await docs.UpdateRevisionAsync(revisionId, req, ct); return NoContent(); }

    [HttpPost("{id:guid}/revisions")]
    public async Task<ActionResult<Guid>> Revise(Guid id, ReviseRequest req, CancellationToken ct) => (await docs.ReviseAsync(id, req.Revision, req.ChangeSummary, ct)).Id;

    [HttpPost("revisions/{revisionId:guid}/submit")]
    public async Task<IActionResult> Submit(Guid revisionId, CancellationToken ct) { await docs.SubmitAsync(revisionId, ct); return NoContent(); }

    /// <summary>Reference copies only: put the attached copy in use without approval.</summary>
    [HttpPost("revisions/{revisionId:guid}/release-reference")]
    public async Task<IActionResult> ReleaseReference(Guid revisionId, CancellationToken ct) { await docs.ReleaseReferenceAsync(revisionId, ct); return NoContent(); }

    /// <summary>Reference copies only: file a newer edition (a new draft copy that is released without approval).</summary>
    [HttpPost("{id:guid}/replace-reference")]
    public async Task<ActionResult<Guid>> ReplaceReference(Guid id, ReviseRequest req, CancellationToken ct) => (await docs.ReplaceReferenceAsync(id, req.Revision, req.ChangeSummary, ct)).Id;

    [HttpPost("{id:guid}/obsolete")]
    public async Task<IActionResult> Obsolete(Guid id, ObsoleteRequest req, CancellationToken ct) { await docs.ObsoleteAsync(id, req.Reason, ct); return NoContent(); }

    /// <summary>Which revision was in effect on a date.</summary>
    [HttpGet("{id:guid}/effective")]
    public async Task<IActionResult> Effective(Guid id, [FromQuery] DateOnly? on, CancellationToken ct)
    {
        var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Document");
        if (!await ConfidentialContent.CanSeeAsync(access, current.UserId!.Value, doc, ct)) throw new NotFoundException("Document");
        var r = await docs.EffectiveOnAsync(id, on ?? await HttpContext.RequestServices.GetRequiredService<QualGuardIQ.Infrastructure.Services.ISiteCalendar>().TodayAsync(await db.Documents.Where(d => d.Id == id).Select(d => (Guid?)d.SiteId).FirstOrDefaultAsync(ct), ct), ct);
        return r is null ? NotFound(new { title = "No revision was effective on that date." })
            : Ok(new RevisionDto(r.Id, r.Revision, r.Sequence, r.Status, r.ChangeSummary, r.EffectiveDate, r.SupersededOn, r.ApprovedAt, r.TrainingRequired, r.ObsoleteReason, r.RowVersion, r.FieldSchema));
    }
}
