using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Api.Controllers;

/// <summary>Shared record features (files, comments, links, actions, custom fields, approvals, history) for every record type.</summary>
[ApiController]
[Authorize]
[Route("api/records/{type}/{id:guid}")]
public sealed class RecordsController(AppDbContext db, IRecordRegistry records, ICurrentUser current, AttachmentService files,
    ActionService actions, CustomFieldService customFields) : ControllerBase
{
    public sealed record AttachmentDto(Guid Id, string FileName, string ContentType, long SizeBytes, string Sha256, int Version, Guid LineageId,
        string? Category, string? Description, Classification Classification, bool IsLocked, bool IsArchived, ScanStatus ScanStatus, DateTimeOffset CreatedAt);
    public sealed record CommentDto(Guid Id, string AuthorName, string Body, DateTimeOffset CreatedAt);
    public sealed record LinkDto(Guid Id, string Direction, string LinkType, string Type, Guid RecordId, string Number, string Title, string? Status, string Url);
    public sealed record AddLinkRequest(string ToType, Guid ToId, string? LinkType);
    public sealed record AddCommentRequest(string Body);
    public sealed record ActionDto(Guid Id, string Number, string Title, string? Description, string ActionType, Guid AssigneeId, string AssigneeName,
        DateOnly DueDate, ActionStatus Status, bool RequiresVerification, string? CompletionNotes, string? VerificationNotes, bool IsOverdue, Guid? CompletedById = null);
    public sealed record ApprovalDto(Guid Id, ApprovalStatus Status, int CurrentStep, DateTimeOffset SubmittedAt, IReadOnlyList<DecisionDto> Decisions);
    public sealed record DecisionDto(int StepOrder, string StepName, string SignerName, bool Approved, string Meaning, string? Comment, DateTimeOffset SignedAt);

    internal static AttachmentDto ToDto(Attachment a) => new(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.Sha256, a.Version, a.LineageId,
        a.Category, a.Description, a.Classification, a.IsLocked, a.IsArchived, a.ScanStatus, a.CreatedAt);

    /// <summary>Number, title, status and URL of any record the user may view (used to open linked records generically).</summary>
    [HttpGet]
    public async Task<object> Summary(string type, Guid id, CancellationToken ct)
    {
        var r = await records.RequireViewAsync(type, id, ct);
        return new { r.Type, r.Id, r.Number, r.Title, r.Status, r.Url };
    }

    [HttpGet("files")]
    public async Task<IEnumerable<AttachmentDto>> Files(string type, Guid id, [FromQuery] bool history, CancellationToken ct) =>
        (await files.ListAsync(type, id, history, ct)).Select(ToDto);

    [HttpPost("files")]
    [RequestSizeLimit(200L * 1024 * 1024)]
    public async Task<ActionResult<AttachmentDto>> Upload(string type, Guid id, IFormFile file, [FromForm] string? category, [FromForm] string? description,
        [FromForm] Classification classification, [FromForm] Guid? replacesId, CancellationToken ct)
    {
        await using var s = file.OpenReadStream();
        var a = await files.UploadAsync(type, id, new UploadRequest(file.FileName, file.ContentType, s, category, description, classification, replacesId), ct);
        return ToDto(a);
    }

    [HttpGet("comments")]
    public async Task<IEnumerable<CommentDto>> Comments(string type, Guid id, CancellationToken ct)
    {
        var r = await records.RequireViewAsync(type, id, ct);
        return await db.Comments.AsNoTracking().Where(c => c.OwnerType == r.Type && c.OwnerId == r.Id).OrderBy(c => c.CreatedAt)
            .Select(c => new CommentDto(c.Id, c.AuthorName, c.Body, c.CreatedAt)).ToListAsync(ct);
    }

    /// <summary>Anyone who can view a record can comment on it.</summary>
    [HttpPost("comments")]
    public async Task<ActionResult<CommentDto>> AddComment(string type, Guid id, AddCommentRequest req, CancellationToken ct)
    {
        var r = await records.RequireViewAsync(type, id, ct);
        if (string.IsNullOrWhiteSpace(req.Body) || req.Body.Length > 10000) throw new AppException("A comment of up to 10,000 characters is required.");
        var c = new Comment { OwnerType = r.Type, OwnerId = r.Id, AuthorId = current.UserId!.Value, AuthorName = current.UserName, Body = req.Body.Trim() };
        db.Comments.Add(c);
        await db.SaveChangesAsync(ct);
        return new CommentDto(c.Id, c.AuthorName, c.Body, c.CreatedAt);
    }

    /// <summary>Linked records the user cannot view are omitted.</summary>
    [HttpGet("links")]
    public async Task<IEnumerable<LinkDto>> Links(string type, Guid id, CancellationToken ct)
    {
        var r = await records.RequireViewAsync(type, id, ct);
        var links = await db.RecordLinks.AsNoTracking().Where(l => (l.FromType == r.Type && l.FromId == r.Id) || (l.ToType == r.Type && l.ToId == r.Id)).ToListAsync(ct);
        var result = new List<LinkDto>();
        foreach (var l in links)
        {
            var outgoing = l.FromType == r.Type && l.FromId == r.Id;
            var (ot, oid) = outgoing ? (l.ToType, l.ToId) : (l.FromType, l.FromId);
            try
            {
                var other = await records.RequireViewAsync(ot, oid, ct);
                result.Add(new LinkDto(l.Id, outgoing ? "To" : "From", l.LinkType, other.Type, other.Id, other.Number, other.Title, other.Status, other.Url));
            }
            catch (NotFoundException) { }
        }
        return result;
    }

    [HttpPost("links")]
    public async Task<ActionResult<Guid>> AddLink(string type, Guid id, AddLinkRequest req, CancellationToken ct)
    {
        var from = await records.RequireEditAsync(type, id, ct);
        var to = await records.RequireViewAsync(req.ToType, req.ToId, ct);
        var lt = string.IsNullOrWhiteSpace(req.LinkType) ? "Related" : req.LinkType.Trim();
        if (from.Type == to.Type && from.Id == to.Id) throw new AppException("A record cannot be linked to itself.");
        if (await db.RecordLinks.AnyAsync(l => l.FromType == from.Type && l.FromId == from.Id && l.ToType == to.Type && l.ToId == to.Id && l.LinkType == lt, ct))
            throw new ConflictException("These records are already linked.");
        var link = new RecordLink { FromType = from.Type, FromId = from.Id, ToType = to.Type, ToId = to.Id, LinkType = lt };
        db.RecordLinks.Add(link);
        await db.SaveChangesAsync(ct);
        return link.Id;
    }

    [HttpDelete("links/{linkId:guid}")]
    public async Task<IActionResult> RemoveLink(string type, Guid id, Guid linkId, CancellationToken ct)
    {
        var r = await records.RequireEditAsync(type, id, ct);
        var l = await db.RecordLinks.FirstOrDefaultAsync(x => x.Id == linkId && ((x.FromType == r.Type && x.FromId == r.Id) || (x.ToType == r.Type && x.ToId == r.Id)), ct)
            ?? throw new NotFoundException("Link");
        db.RecordLinks.Remove(l);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    public sealed record AddTagRequest(string Name);

    [HttpGet("tags")]
    public async Task<IEnumerable<string>> Tags(string type, Guid id, CancellationToken ct)
    {
        var r = await records.RequireViewAsync(type, id, ct);
        return await (from rt in db.RecordTags.AsNoTracking() join t in db.Tags on rt.TagId equals t.Id
                      where rt.OwnerType == r.Type && rt.OwnerId == r.Id orderby t.Name select t.Name).ToListAsync(ct);
    }

    /// <summary>Adds a tag (created on first use, case-insensitive match on existing names). Needs edit rights on the record.</summary>
    [HttpPost("tags")]
    public async Task<IActionResult> AddTag(string type, Guid id, AddTagRequest req, CancellationToken ct)
    {
        var r = await records.RequireEditAsync(type, id, ct);
        var name = (req.Name ?? "").Trim();
        if (name.Length is 0 or > 50) throw new AppException("A tag of 1 to 50 characters is required.");
        var lower = name.ToLowerInvariant();
        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name.ToLower() == lower, ct);
        if (tag is null) { tag = new Tag { Name = name }; db.Tags.Add(tag); }
        if (!await db.RecordTags.AnyAsync(x => x.OwnerType == r.Type && x.OwnerId == r.Id && x.TagId == tag.Id, ct))
            db.RecordTags.Add(new RecordTag { OwnerType = r.Type, OwnerId = r.Id, TagId = tag.Id });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("tags/{name}")]
    public async Task<IActionResult> RemoveTag(string type, Guid id, string name, CancellationToken ct)
    {
        var r = await records.RequireEditAsync(type, id, ct);
        var lower = name.Trim().ToLowerInvariant();
        var rt = await (from x in db.RecordTags join t in db.Tags on x.TagId equals t.Id
                        where x.OwnerType == r.Type && x.OwnerId == r.Id && t.Name.ToLower() == lower select x).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Tag");
        db.RecordTags.Remove(rt);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("actions")]
    public async Task<IEnumerable<ActionDto>> Actions(string type, Guid id, CancellationToken ct) =>
        await ActionsController.ToDtos(db, await actions.ForRecordAsync(type, id, ct), HttpContext.RequestServices.GetRequiredService<QualGuardIQ.Infrastructure.Services.ISiteCalendar>(), ct);

    [HttpPost("actions")]
    public async Task<ActionResult<Guid>> AddAction(string type, Guid id, CreateActionRequest req, CancellationToken ct) =>
        (await actions.CreateAsync(type, id, req, ct)).Id;

    [HttpGet("custom-fields")]
    public async Task<IActionResult> CustomFields(string type, Guid id, CancellationToken ct)
    {
        var (fields, values) = await customFields.GetAsync(type, id, ct);
        return Ok(new { fields = fields.Select(f => new { f.Key, f.Label, fieldType = f.FieldType.ToString(), f.IsRequired, f.Options }), values });
    }

    [HttpPut("custom-fields")]
    public async Task<IActionResult> SaveCustomFields(string type, Guid id, Dictionary<string, string?> values, CancellationToken ct)
    {
        await customFields.SaveAsync(type, id, values, ct);
        return NoContent();
    }

    [HttpGet("approvals")]
    public async Task<IEnumerable<ApprovalDto>> Approvals(string type, Guid id, CancellationToken ct)
    {
        var r = await records.RequireViewAsync(type, id, ct);
        var list = await db.ApprovalRequests.AsNoTracking().Include(a => a.Decisions).Where(a => a.RecordType == r.Type && a.RecordId == r.Id).OrderBy(a => a.CreatedAt).ToListAsync(ct);
        return list.Select(a => new ApprovalDto(a.Id, a.Status, a.CurrentStep, a.CreatedAt,
            a.Decisions.OrderBy(d => d.SignedAt).Select(d => new DecisionDto(d.StepOrder, d.StepName, d.SignerName, d.Approved, d.Meaning, d.Comment, d.SignedAt)).ToList()));
    }

    /// <summary>Record history. Anyone who can view the record can see its history.</summary>
    [HttpGet("history")]
    public async Task<IActionResult> History(string type, Guid id, CancellationToken ct)
    {
        var r = await records.RequireViewAsync(type, id, ct);
        var list = await db.AuditEntries.AsNoTracking().Where(a => a.EntityId == r.Id || (a.EntityType == "Attachment" && db.Attachments.Any(f => f.Id == a.EntityId && f.OwnerId == r.Id)))
            .OrderByDescending(a => a.Id).Take(500)
            .Select(a => new { a.OccurredAt, a.UserName, a.Action, a.Description, a.Changes, a.Reason }).ToListAsync(ct);
        return Ok(list);
    }
}

[ApiController]
[Authorize]
[Route("api/files")]
public sealed class FilesController(AttachmentService files) : ControllerBase
{
    /// <summary>Streams a file after checking permission on its owning record. There are no public file URLs.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Download(Guid id, [FromQuery] bool inline, CancellationToken ct)
    {
        var (meta, content) = await files.OpenAsync(id, ct);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        var previewable = meta.ContentType is "application/pdf" or "image/png" or "image/jpeg" or "image/gif" or "image/webp" or "text/plain";
        return inline && previewable ? File(content, meta.ContentType) : File(content, meta.ContentType, meta.FileName);
    }

    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        await files.ArchiveAsync(id, ct);
        return NoContent();
    }
}
