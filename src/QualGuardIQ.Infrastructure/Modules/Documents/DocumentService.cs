using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Documents;

public static class DocumentRecordTypes
{
    public const string Document = "Document";
    public const string Drawing = "Drawing";
    public const string DocumentRevision = "DocumentRevision";
    public const string DrawingRevision = "DrawingRevision";
    public const string FormTemplate = "FormTemplate";
    public const string FormTemplateRevision = "FormTemplateRevision";

    public static string ForDocument(DocumentKind k) => k switch { DocumentKind.Drawing => Drawing, DocumentKind.FormTemplate => FormTemplate, _ => Document };
    public static string ForRevision(DocumentKind k) => k switch { DocumentKind.Drawing => DrawingRevision, DocumentKind.FormTemplate => FormTemplateRevision, _ => DocumentRevision };
    public static string Path(DocumentKind k) => k switch { DocumentKind.Drawing => "drawings", DocumentKind.FormTemplate => "forms/templates", _ => "documents" };
    public static (string View, string Edit, string Approve) Perms(DocumentKind k) => k switch
    {
        DocumentKind.Drawing => (Permissions.DrawingsView, Permissions.DrawingsEdit, Permissions.DrawingsApprove),
        DocumentKind.FormTemplate => (Permissions.FormsView, Permissions.FormsEdit, Permissions.FormsApprove),
        _ => (Permissions.DocumentsView, Permissions.DocumentsEdit, Permissions.DocumentsApprove)
    };
}

/// <summary>Notified when a revision becomes effective (for example, Training assigns retraining).</summary>
public interface IRevisionReleasedHandler
{
    Task OnReleasedAsync(ControlledDocument document, DocumentRevision revision, CancellationToken ct);
}

public sealed record CreateDocumentRequest(DocumentKind Kind, string Title, string DocumentType, Guid SiteId, Guid? DepartmentId, Guid? OwnerId,
    int ReviewIntervalMonths, string? InitialRevision, string? PartNumber, string? DrawingSize, int? SheetCount, string? ChangeSummary);
public sealed record UpdateDocumentRequest(string Title, string DocumentType, Guid? DepartmentId, Guid OwnerId, int ReviewIntervalMonths,
    string? PartNumber, string? DrawingSize, int? SheetCount, uint RowVersion);
public sealed record UpdateRevisionRequest(string ChangeSummary, DateOnly? EffectiveDate, bool TrainingRequired, uint RowVersion, string? FieldSchema = null);

public sealed class DocumentService(
    AppDbContext db, IAccessService access, ICurrentUser current, INumberingService numbers, IRecordRegistry records,
    WorkflowService workflow, ISearchIndexer search, INotifier notifier, IClock clock, IEnumerable<IRevisionReleasedHandler> released, ISiteCalendar calendar, DocumentTypeService types)
{
    private Task<DateOnly> TodayAsync(Guid siteId, CancellationToken ct) => calendar.TodayAsync(siteId, ct);

    public async Task<ControlledDocument> CreateAsync(CreateDocumentRequest req, CancellationToken ct)
    {
        var (_, edit, _) = DocumentRecordTypes.Perms(req.Kind);
        if (!await access.CanAccessAsync(current.UserId!.Value, edit, req.SiteId, req.DepartmentId, ct)) throw new ForbiddenException();
        Validate(req.Title, req.DocumentType, req.ReviewIntervalMonths, req.SheetCount);
        if (!await db.Sites.AnyAsync(s => s.Id == req.SiteId && !s.IsArchived, ct)) throw new AppException("Select an active site.");
        if (req.DepartmentId is { } d && !await db.Departments.AnyAsync(x => x.Id == d && x.SiteId == req.SiteId, ct)) throw new AppException("The department does not belong to the site.");
        var docType = await types.RequireActiveAsync(req.Kind, req.DocumentType, null, ct);
        var rev = string.IsNullOrWhiteSpace(req.InitialRevision) ? "A" : req.InitialRevision.Trim().ToUpperInvariant();
        if (rev.Length > 10) throw new AppException("Revision labels are limited to 10 characters.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var doc = new ControlledDocument
        {
            Kind = req.Kind, Number = await numbers.NextAsync(DocumentRecordTypes.ForDocument(req.Kind), req.SiteId, ct),
            Title = req.Title.Trim(), DocumentType = docType, SiteId = req.SiteId, DepartmentId = req.DepartmentId,
            OwnerId = req.OwnerId ?? current.UserId!.Value, Status = DocumentStatus.Draft, ReviewIntervalMonths = req.ReviewIntervalMonths,
            PartNumber = req.PartNumber?.Trim(), DrawingSize = req.DrawingSize?.Trim(), SheetCount = req.SheetCount
        };
        doc.Revisions.Add(new DocumentRevision { DocumentId = doc.Id, Revision = rev, Sequence = 1, Status = RevisionStatus.Draft, ChangeSummary = req.ChangeSummary?.Trim() is { Length: > 0 } cs ? cs : "Initial release" });
        db.Documents.Add(doc);
        await db.SaveChangesAsync(ct);
        await IndexAsync(doc, ct);
        await tx.CommitAsync(ct);
        return doc;
    }

    public async Task UpdateAsync(Guid id, UpdateDocumentRequest req, CancellationToken ct)
    {
        var doc = await LoadForEditAsync(id, ct);
        if (doc.Status == DocumentStatus.Obsolete) throw new AppException("Obsolete documents cannot be changed.");
        Validate(req.Title, req.DocumentType, req.ReviewIntervalMonths, req.SheetCount);
        if (req.DepartmentId is { } d && !await db.Departments.AnyAsync(x => x.Id == d && x.SiteId == doc.SiteId, ct)) throw new AppException("The department does not belong to the site.");
        var docType = await types.RequireActiveAsync(doc.Kind, req.DocumentType, doc.DocumentType, ct);
        db.Entry(doc).Property(x => x.RowVersion).OriginalValue = req.RowVersion;
        doc.Title = req.Title.Trim();
        doc.DocumentType = docType;
        doc.DepartmentId = req.DepartmentId;
        doc.OwnerId = req.OwnerId;
        doc.ReviewIntervalMonths = req.ReviewIntervalMonths;
        doc.PartNumber = req.PartNumber?.Trim();
        doc.DrawingSize = req.DrawingSize?.Trim();
        doc.SheetCount = req.SheetCount;
        await db.SaveChangesAsync(ct);
        await IndexAsync(doc, ct);
    }

    public async Task UpdateRevisionAsync(Guid revisionId, UpdateRevisionRequest req, CancellationToken ct)
    {
        var rev = await db.DocumentRevisions.Include(r => r.Document).FirstOrDefaultAsync(r => r.Id == revisionId, ct) ?? throw new NotFoundException("Revision");
        await records.RequireEditAsync(DocumentRecordTypes.ForDocument(rev.Document!.Kind), rev.DocumentId, ct);
        if (rev.Status != RevisionStatus.Draft) throw new AppException("Only draft revisions can be edited.", 409);
        if (string.IsNullOrWhiteSpace(req.ChangeSummary)) throw new AppException("A change summary is required.");
        if (req.EffectiveDate is { } e && e < await TodayAsync(rev.Document.SiteId, ct)) throw new AppException("The effective date cannot be in the past.");
        db.Entry(rev).Property(x => x.RowVersion).OriginalValue = req.RowVersion;
        rev.ChangeSummary = req.ChangeSummary.Trim();
        rev.EffectiveDate = req.EffectiveDate;
        rev.TrainingRequired = req.TrainingRequired;
        if (rev.Document.Kind == DocumentKind.FormTemplate && req.FieldSchema is not null)
        {
            Application.Forms.FormSchema.Parse(req.FieldSchema);
            rev.FieldSchema = req.FieldSchema;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Starts a new draft revision. Earlier revisions are never modified.</summary>
    public async Task<DocumentRevision> ReviseAsync(Guid id, string? label, string changeSummary, CancellationToken ct)
    {
        var doc = await LoadForEditAsync(id, ct);
        if (doc.Status != DocumentStatus.Active) throw new AppException("Only active documents can be revised.");
        if (doc.Revisions.Any(r => r.Status is RevisionStatus.Draft or RevisionStatus.InReview)) throw new ConflictException("A revision is already in progress.");
        if (string.IsNullOrWhiteSpace(changeSummary)) throw new AppException("A change summary is required.");
        var last = doc.Revisions.OrderByDescending(r => r.Sequence).First();
        var next = string.IsNullOrWhiteSpace(label) ? NextLabel(last.Revision) : label.Trim().ToUpperInvariant();
        if (doc.Revisions.Any(r => r.Revision == next)) throw new ConflictException($"Revision {next} already exists.");
        var rev = new DocumentRevision { DocumentId = doc.Id, Revision = next, Sequence = last.Sequence + 1, Status = RevisionStatus.Draft, ChangeSummary = changeSummary.Trim(), TrainingRequired = last.TrainingRequired, FieldSchema = last.FieldSchema };
        db.DocumentRevisions.Add(rev);
        await db.SaveChangesAsync(ct);
        return rev;
    }

    public async Task SubmitAsync(Guid revisionId, CancellationToken ct)
    {
        var rev = await db.DocumentRevisions.Include(r => r.Document).FirstOrDefaultAsync(r => r.Id == revisionId, ct) ?? throw new NotFoundException("Revision");
        var type = DocumentRecordTypes.ForRevision(rev.Document!.Kind);
        await records.RequireEditAsync(type, rev.Id, ct);
        if (rev.Status != RevisionStatus.Draft) throw new ConflictException("Only draft revisions can be submitted.");
        if (rev.Document.Kind == DocumentKind.FormTemplate)
        {
            if (string.IsNullOrWhiteSpace(rev.FieldSchema)) throw new AppException("Define the form fields before submitting for approval.");
            Application.Forms.FormSchema.Parse(rev.FieldSchema);
        }
        else if (!await db.Attachments.AnyAsync(a => a.OwnerType == type && a.OwnerId == rev.Id && !a.IsArchived, ct))
            throw new AppException("Attach the controlled file before submitting for approval.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await workflow.SubmitAsync(type, rev.Id, ct);
        rev.Status = RevisionStatus.InReview;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    internal async Task OnApprovedAsync(Guid revisionId, CancellationToken ct)
    {
        var rev = await db.DocumentRevisions.Include(r => r.Document).FirstAsync(r => r.Id == revisionId, ct);
        rev.Status = RevisionStatus.Approved;
        rev.ApprovedAt = clock.UtcNow;
        var today = await TodayAsync(rev.Document!.SiteId, ct);
        rev.EffectiveDate ??= today;
        if (rev.EffectiveDate < today) rev.EffectiveDate = today;
        var type = DocumentRecordTypes.ForRevision(rev.Document!.Kind);
        foreach (var a in await db.Attachments.Where(a => a.OwnerType == type && a.OwnerId == rev.Id && !a.IsArchived).ToListAsync(ct)) a.IsLocked = true;
        await db.SaveChangesAsync(ct);
        if (rev.EffectiveDate <= today) await MakeEffectiveAsync(rev, ct);
    }

    internal async Task OnRejectedAsync(Guid revisionId, CancellationToken ct)
    {
        var rev = await db.DocumentRevisions.FirstAsync(r => r.Id == revisionId, ct);
        if (rev.Status == RevisionStatus.InReview) { rev.Status = RevisionStatus.Draft; await db.SaveChangesAsync(ct); }
    }

    /// <summary>Releases approved revisions whose effective date has arrived. Run daily by the worker.</summary>
    public async Task<int> ReleaseDueAsync(CancellationToken ct)
    {
        // Candidates up to tomorrow in UTC, then checked against each site's own date.
        var horizon = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddDays(1);
        var candidates = await db.DocumentRevisions.Include(r => r.Document)
            .Where(r => r.Status == RevisionStatus.Approved && r.EffectiveDate <= horizon).ToListAsync(ct);
        var count = 0;
        foreach (var r in candidates)
            if (r.EffectiveDate <= await TodayAsync(r.Document!.SiteId, ct)) { await MakeEffectiveAsync(r, ct); count++; }
        return count;
    }

    private async Task MakeEffectiveAsync(DocumentRevision rev, CancellationToken ct)
    {
        var doc = rev.Document!;
        var previous = await db.DocumentRevisions.Where(r => r.DocumentId == doc.Id && r.Status == RevisionStatus.Effective).ToListAsync(ct);
        foreach (var p in previous) { p.Status = RevisionStatus.Superseded; p.SupersededOn = rev.EffectiveDate; }
        await db.SaveChangesAsync(ct);
        rev.Status = RevisionStatus.Effective;
        doc.Status = DocumentStatus.Active;
        doc.NextReviewDue = rev.EffectiveDate!.Value.AddMonths(doc.ReviewIntervalMonths);
        await db.SaveChangesAsync(ct);
        await IndexAsync(doc, ct);
        foreach (var h in released) await h.OnReleasedAsync(doc, rev, ct);
        var info = await records.GetAsync(DocumentRecordTypes.ForDocument(doc.Kind), doc.Id, ct);
        await notifier.NotifyAsync([doc.OwnerId], $"{doc.Number} revision {rev.Revision} is effective", doc.Title, info, ct: ct);
    }

    public async Task ObsoleteAsync(Guid id, string reason, CancellationToken ct)
    {
        var doc = await db.Documents.Include(d => d.Revisions).FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Document");
        var (_, _, approve) = DocumentRecordTypes.Perms(doc.Kind);
        await records.RequireViewAsync(DocumentRecordTypes.ForDocument(doc.Kind), id, ct);
        if (!await access.CanAccessAsync(current.UserId!.Value, approve, doc.SiteId, doc.DepartmentId, ct)) throw new ForbiddenException();
        if (doc.Status != DocumentStatus.Active) throw new AppException("Only active documents can be made obsolete.");
        if (string.IsNullOrWhiteSpace(reason)) throw new AppException("A reason is required.");
        if (doc.Revisions.Any(r => r.Status is RevisionStatus.Draft or RevisionStatus.InReview)) throw new ConflictException("Withdraw the revision in progress first.");
        var obsoleteDay = await TodayAsync(doc.SiteId, ct);
        foreach (var r in doc.Revisions.Where(r => r.Status is RevisionStatus.Effective or RevisionStatus.Approved))
        {
            r.Status = RevisionStatus.Obsolete;
            r.ObsoletedAt = clock.UtcNow;
            r.ObsoleteReason = reason;
            r.SupersededOn ??= obsoleteDay;
        }
        doc.Status = DocumentStatus.Obsolete;
        await db.SaveChangesAsync(ct);
        await IndexAsync(doc, ct);
    }

    /// <summary>The revision that was effective on a date (for audits and investigations), or null.</summary>
    public async Task<DocumentRevision?> EffectiveOnAsync(Guid id, DateOnly on, CancellationToken ct)
    {
        var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Document");
        await records.RequireViewAsync(DocumentRecordTypes.ForDocument(doc.Kind), id, ct);
        return await db.DocumentRevisions.AsNoTracking()
            .Where(r => r.DocumentId == id && r.EffectiveDate != null && r.EffectiveDate <= on &&
                        (r.Status == RevisionStatus.Effective || r.Status == RevisionStatus.Superseded || r.Status == RevisionStatus.Obsolete) &&
                        (r.SupersededOn == null || r.SupersededOn > on))
            .OrderByDescending(r => r.Sequence).FirstOrDefaultAsync(ct);
    }

    private async Task<ControlledDocument> LoadForEditAsync(Guid id, CancellationToken ct)
    {
        var doc = await db.Documents.Include(d => d.Revisions).FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Document");
        await records.RequireEditAsync(DocumentRecordTypes.ForDocument(doc.Kind), id, ct);
        return doc;
    }

    private async Task IndexAsync(ControlledDocument doc, CancellationToken ct)
    {
        var info = await records.GetAsync(DocumentRecordTypes.ForDocument(doc.Kind), doc.Id, ct);
        await search.IndexAsync(info, string.Join(' ', doc.DocumentType, doc.PartNumber), ct);
    }

    private static void Validate(string title, string type, int interval, int? sheets)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 300) throw new AppException("A title of up to 300 characters is required.");
        if (string.IsNullOrWhiteSpace(type) || type.Length > 60) throw new AppException("A document type is required.");
        if (interval is < 1 or > 120) throw new AppException("The review interval must be 1 to 120 months.");
        if (sheets is <= 0) throw new AppException("Sheet count must be positive.");
    }

    /// <summary>A -> B, Z -> AA, 1 -> 2, 01 -> 02.</summary>
    public static string NextLabel(string current)
    {
        if (int.TryParse(current, out var n)) return (n + 1).ToString().PadLeft(current.Length, '0');
        if (current.All(char.IsAsciiLetterUpper))
        {
            var chars = current.ToCharArray();
            for (var i = chars.Length - 1; i >= 0; i--)
            {
                if (chars[i] != 'Z') { chars[i]++; return new string(chars); }
                chars[i] = 'A';
            }
            return "A" + new string(chars);
        }
        return current + ".1";
    }
}

public sealed class DocumentRecordProvider(AppDbContext db, string type, DocumentKind kind) : IRecordTypeProvider
{
    public string Type => type;
    public async Task<RecordInfo?> FindAsync(Guid id, CancellationToken ct)
    {
        var d = await db.Documents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Kind == kind, ct);
        if (d is null) return null;
        var (v, e, _) = DocumentRecordTypes.Perms(kind);
        var path = DocumentRecordTypes.Path(kind);
        return new RecordInfo(type, d.Id, d.SiteId, d.DepartmentId, d.Number, d.Title, d.Status.ToString(), v, e, $"/{path}/{d.Id}", d.Status == DocumentStatus.Obsolete);
    }
}

public sealed class RevisionRecordProvider(AppDbContext db, string type, DocumentKind kind) : IRecordTypeProvider
{
    public string Type => type;
    public async Task<RecordInfo?> FindAsync(Guid id, CancellationToken ct)
    {
        var r = await db.DocumentRevisions.AsNoTracking().Include(x => x.Document).FirstOrDefaultAsync(x => x.Id == id && x.Document!.Kind == kind, ct);
        if (r is null) return null;
        var (v, e, _) = DocumentRecordTypes.Perms(kind);
        var path = DocumentRecordTypes.Path(kind);
        return new RecordInfo(type, r.Id, r.Document!.SiteId, r.Document.DepartmentId, $"{r.Document.Number} Rev {r.Revision}", r.Document.Title,
            r.Status.ToString(), v, e, $"/{path}/{r.DocumentId}?rev={r.Id}", r.Status != RevisionStatus.Draft);
    }
}

public sealed class RevisionApprovalHandler(string type, Func<DocumentService> service) : IApprovalHandler
{
    public string RecordType => type;
    public Task OnApprovedAsync(Guid recordId, CancellationToken ct) => service().OnApprovedAsync(recordId, ct);
    public Task OnRejectedAsync(Guid recordId, string? comment, CancellationToken ct) => service().OnRejectedAsync(recordId, ct);
}
