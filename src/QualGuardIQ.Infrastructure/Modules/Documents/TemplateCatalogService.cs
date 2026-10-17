using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Documents;

public sealed record CatalogQuery(string? View, string? Q, Guid? CategoryId, string? Module, string? IndustryPack, Guid? SiteId, Guid? DepartmentId,
    Guid? OwnerId, DocumentStatus? Status, TemplateSource? Source, bool FavoritesOnly, string? Sort, bool Desc, int Page = 1, int PageSize = 50);
public sealed record CatalogItem(Guid Id, string Number, string Title, string? Category, Guid? CategoryId, string? Module, string? IndustryPack,
    string SiteCode, string OwnerName, Guid OwnerId, DocumentStatus Status, string? CurrentRevision, RevisionStatus? CurrentRevisionStatus, string? EffectiveRevision,
    DateOnly? EffectiveDate, DateOnly? ReviewDate, TemplateSource Source, string Confidentiality, bool Favorite, DateTimeOffset? LastUsed, int Usage);
public sealed record CatalogPage(IReadOnlyList<CatalogItem> Items, int Total, int Page, int PageSize);
public sealed record CategoryDto(Guid Id, string Code, string Name, Guid? ParentId, string? ModuleKey, string? IndustryPackKey, bool IsSystem, bool IsActive, int Count);
public sealed record CreateCategoryRequest(string Name, Guid? ParentId, string? ModuleKey, string? Description);
public sealed record ClassifyRequest(Guid? CategoryId, string? ModuleKey, string? IndustryPack, string? Description, string Confidentiality, Guid? RetentionPolicyId);
public sealed record CloneRequest(string Title, Guid SiteId, Guid? DepartmentId);
public sealed record FieldDiff(string Key, string Change, string? Before, string? After);
public sealed record RevisionCompare(string From, string To, IReadOnlyList<FieldDiff> Fields);
public sealed record UsageDto(int Records, int Open, DateTimeOffset? LastUsed, IReadOnlyList<RevisionUsage> ByRevision);
public sealed record RevisionUsage(string Revision, int Records);
public sealed record HistoryDto(DateTimeOffset At, string User, string Action, string Description, string? Reason);
public sealed record SavedViewDto(Guid Id, string Name, string Settings, bool IsDefault);
public sealed record SaveViewRequest(string Name, string Settings, bool IsDefault);

/// <summary>Enterprise Template Library catalog (TECH section 3): views, list functions and the catalog-level template actions.</summary>
public sealed class TemplateCatalogService(AppDbContext db, IAccessService access, ICurrentUser current, DocumentService docs, IClock clock)
{
    public const string ListKey = "template-library";
    public static readonly string[] Views = ["library", "mine", "organization", "site", "industry", "imported", "draft", "review", "effective", "retired"];
    public static readonly string[] Markings = ["Public", "Internal", "Confidential", "Restricted"];

    private Guid Me => current.UserId ?? throw new ForbiddenException();

    private async Task<IQueryable<ControlledDocument>> ScopedAsync(string perm, CancellationToken ct)
    {
        var scope = await access.GetScopeAsync(Me, perm, ct);
        if (scope.IsEmpty) throw new ForbiddenException();
        var q = db.Documents.AsNoTracking().Where(d => d.Kind == DocumentKind.FormTemplate && !d.IsArchived);
        if (!scope.AllSites)
        {
            var sites = scope.SiteIds.ToList();
            var pairs = scope.DepartmentPairs.Select(p => p.Site.ToString() + "|" + p.Dept.ToString()).ToList();
            q = q.Where(d => sites.Contains(d.SiteId) || (d.DepartmentId != null && pairs.Contains(d.SiteId.ToString() + "|" + d.DepartmentId.ToString())));
        }
        return q;
    }

    public async Task<CatalogPage> ListAsync(CatalogQuery f, CancellationToken ct)
    {
        var me = Me;
        var q = await ScopedAsync(Permissions.FormsView, ct);
        switch (f.View ?? "library")
        {
            case "mine": q = q.Where(d => d.OwnerId == me); break;
            case "organization": q = q.Where(d => d.DepartmentId == null); break;
            case "site": q = q.Where(d => d.DepartmentId != null); break;
            case "industry": q = q.Where(d => d.IndustryPack != null); break;
            case "imported": q = q.Where(d => d.Source == TemplateSource.Excel || d.Source == TemplateSource.Word || d.Source == TemplateSource.Csv || d.Source == TemplateSource.Pdf); break;
            case "draft": q = q.Where(d => d.Revisions.Any(r => r.Status == RevisionStatus.Draft)); break;
            case "review": q = q.Where(d => d.Revisions.Any(r => r.Status == RevisionStatus.InReview)); break;
            case "effective": q = q.Where(d => d.Status == DocumentStatus.Active); break;
            case "retired": q = q.Where(d => d.Status == DocumentStatus.Obsolete); break;
            case "library": break;
            default: throw new AppException("Unknown view.");
        }
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var like = $"%{f.Q.Trim()}%";
            q = q.Where(d => EF.Functions.ILike(d.Number, like) || EF.Functions.ILike(d.Title, like) || (d.Description != null && EF.Functions.ILike(d.Description, like)));
        }
        if (f.CategoryId is { } c) q = q.Where(d => d.CategoryId == c);
        if (!string.IsNullOrWhiteSpace(f.Module)) q = q.Where(d => d.ModuleKey == f.Module);
        if (!string.IsNullOrWhiteSpace(f.IndustryPack)) q = q.Where(d => d.IndustryPack == f.IndustryPack);
        if (f.SiteId is { } s) q = q.Where(d => d.SiteId == s);
        if (f.DepartmentId is { } dep) q = q.Where(d => d.DepartmentId == dep);
        if (f.OwnerId is { } o) q = q.Where(d => d.OwnerId == o);
        if (f.Status is { } st) q = q.Where(d => d.Status == st);
        if (f.Source is { } src) q = q.Where(d => d.Source == src);
        if (f.FavoritesOnly) q = q.Where(d => db.TemplateFavorites.Any(x => x.UserId == me && x.DocumentId == d.Id));

        var rows = q.Select(d => new
        {
            D = d,
            Cur = d.Revisions.OrderByDescending(r => r.Sequence).FirstOrDefault(),
            Eff = d.Revisions.Where(r => r.Status == RevisionStatus.Effective).FirstOrDefault(),
            LastUsed = db.FormRecords.Where(r => r.TemplateId == d.Id).Max(r => (DateTimeOffset?)r.CreatedAt),
            Usage = db.FormRecords.Count(r => r.TemplateId == d.Id),
        });
        rows = (f.Sort ?? "number") switch
        {
            "title" => f.Desc ? rows.OrderByDescending(x => x.D.Title) : rows.OrderBy(x => x.D.Title),
            "status" => f.Desc ? rows.OrderByDescending(x => x.D.Status) : rows.OrderBy(x => x.D.Status),
            "effective" => f.Desc ? rows.OrderByDescending(x => x.Eff!.EffectiveDate) : rows.OrderBy(x => x.Eff!.EffectiveDate),
            "review" => f.Desc ? rows.OrderByDescending(x => x.D.NextReviewDue) : rows.OrderBy(x => x.D.NextReviewDue),
            "lastUsed" => f.Desc ? rows.OrderByDescending(x => x.LastUsed) : rows.OrderBy(x => x.LastUsed),
            "usage" => f.Desc ? rows.OrderByDescending(x => x.Usage) : rows.OrderBy(x => x.Usage),
            _ => f.Desc ? rows.OrderByDescending(x => x.D.Number) : rows.OrderBy(x => x.D.Number),
        };
        var size = Math.Clamp(f.PageSize, 1, 200);
        var page = Math.Max(1, f.Page);
        var total = await q.CountAsync(ct);
        var list = await rows.Skip((page - 1) * size).Take(size).Select(x => new CatalogItem(x.D.Id, x.D.Number, x.D.Title,
                db.TemplateCategories.Where(c => c.Id == x.D.CategoryId).Select(c => c.Name).FirstOrDefault(), x.D.CategoryId, x.D.ModuleKey, x.D.IndustryPack,
                db.Sites.Where(s => s.Id == x.D.SiteId).Select(s => s.Code).First(), db.Users.Where(u => u.Id == x.D.OwnerId).Select(u => u.DisplayName).First(), x.D.OwnerId,
                x.D.Status, x.Cur!.Revision, x.Cur!.Status, x.Eff!.Revision, x.Eff!.EffectiveDate, x.D.NextReviewDue, x.D.Source, x.D.Confidentiality,
                db.TemplateFavorites.Any(t => t.UserId == me && t.DocumentId == x.D.Id), x.LastUsed, x.Usage))
            .ToListAsync(ct);
        return new CatalogPage(list, total, page, size);
    }

    public async Task<IReadOnlyList<CategoryDto>> CategoriesAsync(CancellationToken ct)
    {
        var q = await ScopedAsync(Permissions.FormsView, ct);
        var counts = await q.Where(d => d.CategoryId != null).GroupBy(d => d.CategoryId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return (await db.TemplateCategories.AsNoTracking().OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ToListAsync(ct))
            .Select(c => new CategoryDto(c.Id, c.Code, c.Name, c.ParentId, c.ModuleKey, c.IndustryPackKey, c.IsSystem, c.IsActive, counts.GetValueOrDefault(c.Id))).ToList();
    }

    private async Task RequireAnyAsync(string perm, CancellationToken ct)
    {
        if ((await access.GetScopeAsync(Me, perm, ct)).IsEmpty) throw new ForbiddenException();
    }

    public async Task<Guid> CreateCategoryAsync(CreateCategoryRequest req, CancellationToken ct)
    {
        await RequireAnyAsync(Permissions.FormsEdit, ct);
        var name = (req.Name ?? "").Trim();
        if (name.Length is 0 or > 100) throw new AppException("A category name of up to 100 characters is required.");
        if (req.ParentId is { } p && !await db.TemplateCategories.AnyAsync(c => c.Id == p, ct)) throw new AppException("The parent category does not exist.");
        if (await db.TemplateCategories.AnyAsync(c => c.ParentId == req.ParentId && c.Name.ToLower() == name.ToLower(), ct)) throw new ConflictException("A category with that name already exists here.");
        var code = new string(name.ToUpperInvariant().Where(char.IsLetterOrDigit).Take(12).ToArray());
        var baseCode = code.Length == 0 ? "CAT" : code; code = baseCode;
        for (var i = 2; await db.TemplateCategories.AnyAsync(c => c.Code == code, ct); i++) code = baseCode + i;
        var cat = new TemplateCategory { Code = code, Name = name, ParentId = req.ParentId, ModuleKey = req.ModuleKey, Description = req.Description, DisplayOrder = 1000 };
        db.TemplateCategories.Add(cat);
        await db.SaveChangesAsync(ct);
        return cat.Id;
    }

    private async Task<ControlledDocument> LoadAsync(Guid id, string perm, CancellationToken ct)
    {
        var d = await db.Documents.Include(x => x.Revisions).FirstOrDefaultAsync(x => x.Id == id && x.Kind == DocumentKind.FormTemplate, ct) ?? throw new NotFoundException("Template");
        if (!await access.CanAccessAsync(Me, Permissions.FormsView, d.SiteId, d.DepartmentId, ct)) throw new NotFoundException("Template");
        if (perm != Permissions.FormsView && !await access.CanAccessAsync(Me, perm, d.SiteId, d.DepartmentId, ct)) throw new ForbiddenException();
        return d;
    }

    public async Task ClassifyAsync(Guid id, ClassifyRequest req, CancellationToken ct)
    {
        var d = await LoadAsync(id, Permissions.FormsEdit, ct);
        if (!Markings.Contains(req.Confidentiality)) throw new AppException("Choose Public, Internal, Confidential or Restricted.");
        if (req.CategoryId is { } c && !await db.TemplateCategories.AnyAsync(x => x.Id == c && x.IsActive, ct)) throw new AppException("Choose an active category.");
        if (req.RetentionPolicyId is { } r && !await db.Set<Domain.Enterprise.RetentionPolicy>().AnyAsync(x => x.Id == r, ct)) throw new AppException("The retention rule does not exist.");
        d.CategoryId = req.CategoryId;
        d.ModuleKey = string.IsNullOrWhiteSpace(req.ModuleKey) ? null : req.ModuleKey.Trim();
        d.IndustryPack = string.IsNullOrWhiteSpace(req.IndustryPack) ? null : req.IndustryPack.Trim();
        d.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        d.Confidentiality = req.Confidentiality;
        d.RetentionPolicyId = req.RetentionPolicyId;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetFavoriteAsync(Guid id, bool on, CancellationToken ct)
    {
        await LoadAsync(id, Permissions.FormsView, ct);
        var fav = await db.TemplateFavorites.FindAsync([Me, id], ct);
        if (on && fav is null) db.TemplateFavorites.Add(new TemplateFavorite { UserId = Me, DocumentId = id, CreatedAt = clock.UtcNow });
        if (!on && fav is not null) db.TemplateFavorites.Remove(fav);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Clone: a new template whose first draft revision carries the source's latest approved (or latest) form definition.</summary>
    public async Task<Guid> CloneAsync(Guid id, CloneRequest req, CancellationToken ct)
    {
        var src = await LoadAsync(id, Permissions.FormsView, ct);
        var from = src.Revisions.Where(r => r.Status == RevisionStatus.Effective).FirstOrDefault() ?? src.Revisions.OrderByDescending(r => r.Sequence).First();
        var created = await docs.CreateAsync(new CreateDocumentRequest(DocumentKind.FormTemplate, req.Title, src.DocumentType, req.SiteId, req.DepartmentId, null,
            src.ReviewIntervalMonths, null, src.PartNumber, null, null, $"Cloned from {src.Number} revision {from.Revision}"), ct);
        var doc = await db.Documents.Include(x => x.Revisions).FirstAsync(x => x.Id == created.Id, ct);
        doc.Source = TemplateSource.Clone; doc.ClonedFromId = src.Id; doc.CategoryId = src.CategoryId; doc.ModuleKey = src.ModuleKey;
        doc.IndustryPack = src.IndustryPack; doc.Description = src.Description; doc.Confidentiality = src.Confidentiality; doc.RetentionPolicyId = src.RetentionPolicyId;
        await db.SaveChangesAsync(ct);
        if (!string.IsNullOrWhiteSpace(from.FieldSchema))
        {
            var rev = doc.Revisions.Single();
            await docs.UpdateRevisionAsync(rev.Id, new UpdateRevisionRequest(rev.ChangeSummary, null, from.TrainingRequired, rev.RowVersion, from.FieldSchema), ct);
        }
        return doc.Id;
    }

    /// <summary>Restore a retired template: its last obsolete revision becomes effective again (lifecycle columns only).</summary>
    public async Task RestoreAsync(Guid id, string reason, CancellationToken ct)
    {
        var d = await LoadAsync(id, Permissions.FormsApprove, ct);
        if (d.Status != DocumentStatus.Obsolete) throw new AppException("Only retired templates can be restored.");
        if (string.IsNullOrWhiteSpace(reason)) throw new AppException("A reason is required.");
        var last = d.Revisions.Where(r => r.Status == RevisionStatus.Obsolete && r.ApprovedAt != null).OrderByDescending(r => r.Sequence).FirstOrDefault()
            ?? throw new AppException("There is no approved revision to restore.");
        last.Status = RevisionStatus.Effective; last.ObsoletedAt = null; last.ObsoleteReason = null;
        d.Status = DocumentStatus.Active;
        db.AuditEntries.Add(new Domain.Audit.AuditEntry { OccurredAt = clock.UtcNow, UserId = Me, UserName = current.UserName, Action = "Restored", EntityType = nameof(ControlledDocument),
            EntityId = d.Id, SiteId = d.SiteId, Description = $"Template {d.Number} restored at revision {last.Revision}", Reason = reason.Trim() });
        await db.SaveChangesAsync(ct);
    }

    public async Task<RevisionCompare> CompareAsync(Guid id, Guid fromRevisionId, Guid toRevisionId, CancellationToken ct)
    {
        var d = await LoadAsync(id, Permissions.FormsView, ct);
        var a = d.Revisions.FirstOrDefault(r => r.Id == fromRevisionId) ?? throw new NotFoundException("Revision");
        var b = d.Revisions.FirstOrDefault(r => r.Id == toRevisionId) ?? throw new NotFoundException("Revision");
        var fa = Fields(a.FieldSchema); var fb = Fields(b.FieldSchema);
        var diffs = new List<FieldDiff>();
        foreach (var (k, v) in fa) if (!fb.TryGetValue(k, out var nv)) diffs.Add(new FieldDiff(k, "Removed", v, null)); else if (nv != v) diffs.Add(new FieldDiff(k, "Changed", v, nv));
        foreach (var (k, v) in fb) if (!fa.ContainsKey(k)) diffs.Add(new FieldDiff(k, "Added", null, v));
        return new RevisionCompare(a.Revision, b.Revision, diffs.OrderBy(x => x.Key).ToList());
    }

    /// <summary>Flattens every object with a "key" in the schema to key → compact JSON, so any change to a field, section or table is reported.</summary>
    internal static Dictionary<string, string> Fields(string? schema)
    {
        var map = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(schema)) return map;
        void Walk(JsonElement e, string path)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                if (e.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String)
                {
                    var o = new Dictionary<string, JsonElement>();
                    foreach (var p in e.EnumerateObject()) if (p.Value.ValueKind is not JsonValueKind.Array and not JsonValueKind.Object || p.Name is "options" or "validation") o[p.Name] = p.Value;
                    map[path + k.GetString()] = JsonSerializer.Serialize(o);
                    path = path + k.GetString() + ".";
                }
                foreach (var p in e.EnumerateObject()) Walk(p.Value, path);
            }
            else if (e.ValueKind == JsonValueKind.Array) foreach (var i in e.EnumerateArray()) Walk(i, path);
        }
        using var doc = JsonDocument.Parse(schema);
        Walk(doc.RootElement, "");
        return map;
    }

    public async Task<UsageDto> UsageAsync(Guid id, CancellationToken ct)
    {
        await LoadAsync(id, Permissions.FormsView, ct);
        var recs = db.FormRecords.AsNoTracking().Where(r => r.TemplateId == id);
        var by = await recs.GroupBy(r => r.TemplateRevision).Select(g => new RevisionUsage(g.Key, g.Count())).ToListAsync(ct);
        return new UsageDto(await recs.CountAsync(ct), await recs.CountAsync(r => r.SubmittedAt == null, ct), await recs.MaxAsync(r => (DateTimeOffset?)r.CreatedAt, ct), by.OrderBy(x => x.Revision).ToList());
    }

    public async Task<IReadOnlyList<HistoryDto>> HistoryAsync(Guid id, CancellationToken ct)
    {
        var d = await LoadAsync(id, Permissions.FormsView, ct);
        var ids = d.Revisions.Select(r => r.Id).Append(d.Id).ToList();
        return await db.AuditEntries.AsNoTracking().Where(a => ids.Contains(a.EntityId)).OrderByDescending(a => a.OccurredAt).Take(500)
            .Select(a => new HistoryDto(a.OccurredAt, a.UserName, a.Action, a.Description, a.Reason)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SavedViewDto>> ViewsAsync(CancellationToken ct) =>
        await db.SavedListViews.AsNoTracking().Where(v => v.UserId == Me && v.ListKey == ListKey).OrderBy(v => v.Name).Select(v => new SavedViewDto(v.Id, v.Name, v.Settings, v.IsDefault)).ToListAsync(ct);

    public async Task<Guid> SaveViewAsync(SaveViewRequest req, CancellationToken ct)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length is 0 or > 100) throw new AppException("A view name of up to 100 characters is required.");
        try { using var _ = JsonDocument.Parse(req.Settings); } catch (JsonException) { throw new AppException("The view settings are not valid."); }
        var me = Me;
        if (req.IsDefault) await db.SavedListViews.Where(v => v.UserId == me && v.ListKey == ListKey && v.IsDefault).ExecuteUpdateAsync(s => s.SetProperty(v => v.IsDefault, false), ct);
        var v = await db.SavedListViews.FirstOrDefaultAsync(x => x.UserId == me && x.ListKey == ListKey && x.Name == name, ct);
        if (v is null) { v = new SavedListView { UserId = me, ListKey = ListKey, Name = name }; db.SavedListViews.Add(v); }
        v.Settings = req.Settings; v.IsDefault = req.IsDefault;
        await db.SaveChangesAsync(ct);
        return v.Id;
    }

    public async Task DeleteViewAsync(Guid viewId, CancellationToken ct)
    {
        var me = Me;
        var v = await db.SavedListViews.FirstOrDefaultAsync(x => x.Id == viewId && x.UserId == me, ct) ?? throw new NotFoundException("View");
        db.SavedListViews.Remove(v);
        await db.SaveChangesAsync(ct);
    }
}
