using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Storage;

namespace QualGuardIQ.Infrastructure.Services;

public sealed record RetentionRequest(DateOnly RetainUntil, string Reason);
public enum LibrarySort { Newest = 0, Oldest = 1, Name = 2, Largest = 3 }
public enum LibraryRetention { Any = 0, None = 1, Retained = 2, Expired = 3 }
public sealed record LibraryQuery(string? Kind = null, string? Q = null, DateOnly? From = null, DateOnly? To = null, string? ContentType = null, Guid? UploadedById = null, Guid? SiteId = null,
    Guid? DepartmentId = null, Classification? Classification = null, LibraryRetention Retention = LibraryRetention.Any, string? OwnerType = null, LibrarySort Sort = LibrarySort.Newest, int Page = 1, int PageSize = 50);
/// <summary>One page of the content library. Total counts every current file in the caller's scope that matches; items on the page are re-verified against the owning record.</summary>
public sealed record LibraryPage(IReadOnlyList<LibraryItem> Items, int Total, int Page, int PageSize, bool TotalIsExact);
public sealed record LibraryItem(Guid Id, string FileName, string ContentType, string Kind, long SizeBytes, int Version, Guid LineageId, int Versions, Classification Classification,
    DateOnly? RetainUntil, bool IsLocked, ScanStatus ScanStatus, DateTimeOffset CreatedAt, string OwnerType, Guid OwnerId, string OwnerNumber, string OwnerTitle, string OwnerUrl, string? Category, string? Description);

/// <summary>Content kinds of the enterprise content library, derived from the file itself and its owning record.</summary>
public static class FileKinds
{
    public static readonly string[] All = ["Drawing", "Document", "Image", "Video", "Attachment"];
    static readonly HashSet<string> Cad = [".dwg", ".dxf", ".step", ".stp", ".igs", ".iges"];
    static readonly HashSet<string> Docs = [".pdf", ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".txt", ".csv"];
    static readonly HashSet<string> Images = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".tif", ".tiff"];
    public static readonly HashSet<string> Videos = [".mp4", ".m4v", ".webm", ".mov"];
    public static string Of(string fileName, string ownerType, string? category)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (Videos.Contains(ext)) return "Video";
        if (Cad.Contains(ext) || ownerType.Contains("Drawing", StringComparison.OrdinalIgnoreCase) || string.Equals(category, "Drawing", StringComparison.OrdinalIgnoreCase)) return "Drawing";
        if (Images.Contains(ext)) return "Image";
        if (Docs.Contains(ext)) return "Document";
        return "Attachment";
    }
    public static string VideoContentType(string ext) => ext switch { ".webm" => "video/webm", ".mov" => "video/quicktime", _ => "video/mp4" };
}

public sealed record UploadRequest(string FileName, string ContentType, Stream Content, string? Category, string? Description, Classification Classification, Guid? ReplacesId);

public sealed class AttachmentService(AppDbContext db, IFileStorage storage, IMalwareScanner scanner, IRecordRegistry records, IConfiguration config, ISiteCalendar calendar, IAccessService access, ICurrentUser current, Security.ISecurityEventLogger security, Modules.Enterprise.RetentionEngine retention)
{
    private static readonly HashSet<string> Blocked = [".exe", ".dll", ".bat", ".cmd", ".com", ".msi", ".ps1", ".sh", ".js", ".vbs", ".scr", ".jar", ".html", ".htm", ".svg"];

    private long MaxBytes => config.GetValue("Storage:MaxFileBytes", 100L * 1024 * 1024);
    /// <summary>Videos have their own, larger limit (default 1 GB); the request limit on the upload endpoint matches it.</summary>
    private long MaxVideoBytes => config.GetValue("Storage:MaxVideoBytes", 1024L * 1024 * 1024);
    /// <summary>Files larger than this are buffered to a temporary file instead of memory when opened.</summary>
    const long MemoryLimit = 32L * 1024 * 1024;
    private HashSet<string> Allowed => config.GetSection("Storage:AllowedExtensions").Get<string[]>()?.Select(e => e.ToLowerInvariant()).ToHashSet()
        ?? [".pdf", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".tif", ".tiff", ".txt", ".csv", ".docx", ".xlsx", ".pptx", ".doc", ".xls", ".dwg", ".dxf", ".step", ".stp", ".igs", ".iges", ".zip", ".json", ".xml", ".mp4", ".m4v", ".webm", ".mov"];

    public async Task<Attachment> UploadAsync(string ownerType, Guid ownerId, UploadRequest req, CancellationToken ct, bool skipPermission = false)
    {
        var record = skipPermission ? await records.GetAsync(ownerType, ownerId, ct) : await records.RequireEditAsync(ownerType, ownerId, ct);
        if (record.IsLocked && !skipPermission) throw new AppException("This record is locked; files cannot be added.");
        var name = Path.GetFileName(req.FileName ?? "").Trim();
        if (name.Length is 0 or > 255) throw new AppException("A file name of up to 255 characters is required.");
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (Blocked.Contains(ext) || !Allowed.Contains(ext)) throw new AppException($"Files of type '{ext}' are not allowed.");

        var isVideo = FileKinds.Videos.Contains(ext);
        var limit = isVideo ? MaxVideoBytes : MaxBytes;
        await using var buffer = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await req.Content.ReadAsync(chunk, ct)) > 0)
        {
            total += read;
            if (total > limit) throw new AppException($"{(isVideo ? "Videos" : "Files")} may not exceed {limit / (1024 * 1024)} MB.");
            sha.AppendData(chunk, 0, read);
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        if (total == 0) throw new AppException("The file is empty.");
        buffer.Position = 0;
        var scan = await scanner.ScanAsync(buffer, name, ct);
        buffer.Position = 0;
        if (scan == ScanStatus.Infected) throw new AppException("The file was rejected by the malware scanner.");
        if (scan == ScanStatus.Failed && !config.GetValue("MalwareScanning:AllowWhenUnavailable", false))
            throw new AppException("The malware scanner is unavailable, so the file was not accepted. Try again later.", 503);

        var a = new Attachment
        {
            OwnerType = record.Type, OwnerId = record.Id, SiteId = record.SiteId, DepartmentId = record.DepartmentId, ViewPermission = record.ViewPermission, FileName = name,
            ContentType = isVideo ? FileKinds.VideoContentType(ext) : string.IsNullOrWhiteSpace(req.ContentType) ? "application/octet-stream" : req.ContentType,
            SizeBytes = total, Sha256 = Convert.ToHexStringLower(sha.GetHashAndReset()),
            Category = req.Category, Description = req.Description, Classification = req.Classification, ScanStatus = scan
        };
        a.LineageId = a.Id;
        if (req.ReplacesId is { } prevId)
        {
            var prev = await db.Attachments.FirstOrDefaultAsync(x => x.Id == prevId && x.OwnerType == record.Type && x.OwnerId == record.Id, ct)
                ?? throw new NotFoundException("File to replace");
            if (prev.IsLocked) throw new AppException("This file is locked and cannot be replaced.");
            a.LineageId = prev.LineageId;
            a.Version = await db.Attachments.Where(x => x.LineageId == prev.LineageId).MaxAsync(x => x.Version, ct) + 1;
            prev.IsArchived = true;
            prev.ArchivedAt = DateTimeOffset.UtcNow;
        }
        a.RetainUntil = await PolicyRetentionAsync(record, ct);
        if (req.ReplacesId is not null && await db.Attachments.Where(x => x.LineageId == a.LineageId && x.Id != a.Id).MaxAsync(x => x.RetainUntil, ct) is { } carried && (a.RetainUntil is null || carried > a.RetainUntil))
            a.RetainUntil = carried; // a new version never shortens the retention already set on the lineage
        a.StorageKey = $"{record.Type.ToLowerInvariant()}/{record.Id:N}/{a.Id:N}";
        buffer.Position = 0;
        await storage.PutAsync(a.StorageKey, buffer, a.ContentType, ct);
        db.Attachments.Add(a);
        await db.SaveChangesAsync(ct);
        return a;
    }

    /// <summary>
    /// Opens a file after checking permission on the owning record and verifying the checksum. Restricted files need edit permission on the record;
    /// opening a Confidential or Restricted file is written to the security log. Large files (videos) are buffered to a temporary file, and the
    /// returned stream is seekable so the endpoint can serve byte ranges for playback.
    /// </summary>
    public async Task<(Attachment Meta, Stream Content)> OpenAsync(Guid id, CancellationToken ct)
    {
        var a = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("File");
        if (a.Classification == Classification.Restricted) await records.RequireEditAsync(a.OwnerType, a.OwnerId, ct);
        else await records.RequireViewAsync(a.OwnerType, a.OwnerId, ct);
        if (a.ScanStatus == ScanStatus.Infected) throw new AppException("This file is quarantined.", 403);
        Stream buffer = a.SizeBytes > MemoryLimit
            ? new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous)
            : new MemoryStream();
        try
        {
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var s = await storage.GetAsync(a.StorageKey, ct))
            {
                var chunk = new byte[81920]; int read;
                while ((read = await s.ReadAsync(chunk, ct)) > 0) { sha.AppendData(chunk, 0, read); await buffer.WriteAsync(chunk.AsMemory(0, read), ct); }
            }
            if (Convert.ToHexStringLower(sha.GetHashAndReset()) != a.Sha256) throw new AppException("The stored file failed its integrity check. Contact an administrator.", 500);
        }
        catch { await buffer.DisposeAsync(); throw; }
        if (a.Classification is Classification.Confidential or Classification.Restricted)
            await security.LogAsync("FileOpened", true, current.UserId, current.UserName, $"{a.Classification} file {a.FileName} v{a.Version} ({a.Id}) on {a.OwnerType} {a.OwnerId}", ct);
        buffer.Position = 0;
        return (a, buffer);
    }

    /// <summary>Sets or extends a file's retention date (governance.edit at the record's site). Retention can never be shortened here; every change is audited.</summary>
    public async Task<Attachment> RetainAsync(Guid id, RetentionRequest r, CancellationToken ct)
    {
        var a = await db.Attachments.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("File");
        var rec = await records.RequireViewAsync(a.OwnerType, a.OwnerId, ct);
        if (!(await access.GetScopeAsync(current.UserId ?? Guid.Empty, QualGuardIQ.Domain.Security.Permissions.GovernanceEdit, ct)).Allows(rec.SiteId, rec.DepartmentId)) throw new ForbiddenException();
        if (string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Length > 500) throw new AppException("Give a reason for the retention change (up to 500 characters).");
        if (r.RetainUntil <= await calendar.TodayAsync(rec.SiteId, ct)) throw new AppException("The retention date must be in the future.");
        var lineage = await db.Attachments.Where(x => x.LineageId == a.LineageId).ToListAsync(ct);
        if (lineage.Any(x => x.RetainUntil is { } cur && cur > r.RetainUntil)) throw new AppException("Retention can only be extended, not shortened.");
        foreach (var x in lineage) x.RetainUntil = r.RetainUntil;
        await db.SaveChangesAsync(ct);
        await security.LogAsync("FileRetention", true, current.UserId, current.UserName, $"{a.FileName} lineage {a.LineageId} retained until {r.RetainUntil:yyyy-MM-dd}: {r.Reason.Trim()}", ct);
        return a;
    }

    /// <summary>The retention policy that governs the record (all filters and priority resolution, see RetentionEngine) sets the initial retention date.</summary>
    async Task<DateOnly?> PolicyRetentionAsync(RecordInfo record, CancellationToken ct)
    {
        var c = await retention.ForRecordAsync(record.Type, record.Id, ct);
        return c?.RetainYears is int years && years > 0 ? (await calendar.TodayAsync(record.SiteId, ct)).AddYears(years) : null;
    }

    /// <summary>Enterprise content library (compatibility form): first page of the paged search.</summary>
    public async Task<List<LibraryItem>> LibraryAsync(string? kind, string? q, int take, CancellationToken ct) =>
        [.. (await SearchLibraryAsync(new LibraryQuery(Kind: kind, Q: q, PageSize: Math.Clamp(take, 1, 200)), ct)).Items];

    static readonly string[] CadExt = [".dwg", ".dxf", ".step", ".stp", ".igs", ".iges"];
    static readonly string[] DocExt = [".pdf", ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".txt", ".csv"];
    static readonly string[] ImgExt = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".tif", ".tiff"];
    static readonly string[] VidExt = [".mp4", ".m4v", ".webm", ".mov"];

    /// <summary>The same classification as FileKinds.Of, expressed so the database can filter on it.</summary>
    static IQueryable<Attachment> WhereKind(IQueryable<Attachment> q, string kind)
    {
        System.Linq.Expressions.Expression<Func<Attachment, bool>> video = x => x.Extension != null && VidExt.Contains(x.Extension);
        System.Linq.Expressions.Expression<Func<Attachment, bool>> drawing = x => (x.Extension != null && CadExt.Contains(x.Extension)) || x.OwnerType.ToLower().Contains("drawing") || (x.Category != null && x.Category.ToLower() == "drawing");
        return kind switch
        {
            "Video" => q.Where(video),
            "Drawing" => q.Where(x => !(x.Extension != null && VidExt.Contains(x.Extension))).Where(drawing),
            "Image" => q.Where(x => x.Extension != null && ImgExt.Contains(x.Extension) && !VidExt.Contains(x.Extension) && !CadExt.Contains(x.Extension) && !x.OwnerType.ToLower().Contains("drawing") && (x.Category == null || x.Category.ToLower() != "drawing")),
            "Document" => q.Where(x => x.Extension != null && DocExt.Contains(x.Extension) && !x.OwnerType.ToLower().Contains("drawing") && (x.Category == null || x.Category.ToLower() != "drawing")),
            "Attachment" => q.Where(x => (x.Extension == null || (!VidExt.Contains(x.Extension) && !CadExt.Contains(x.Extension) && !ImgExt.Contains(x.Extension) && !DocExt.Contains(x.Extension)))
                && !x.OwnerType.ToLower().Contains("drawing") && (x.Category == null || x.Category.ToLower() != "drawing")),
            _ => throw new AppException($"Kind must be one of: {string.Join(", ", FileKinds.All)}."),
        };
    }

    /// <summary>
    /// Server-side, permission-aware content library search. Scope is applied in the database from the view permission, site and
    /// department copied onto each file at upload (so nothing is loaded into memory beyond one page); every item on the page is then
    /// re-checked against its owning record. Sorting is stable (ties broken by id).
    /// </summary>
    public async Task<LibraryPage> SearchLibraryAsync(LibraryQuery r, CancellationToken ct)
    {
        var uid = current.UserId ?? throw new ForbiddenException();
        var size = Math.Clamp(r.PageSize, 1, 200);
        var page = Math.Clamp(r.Page, 1, 100_000);
        var query = db.Attachments.AsNoTracking().Where(x => !x.IsArchived && x.ScanStatus != ScanStatus.Infected);

        // scope: (view permission, site[, department]) pairs the caller holds
        var perms = await db.Attachments.AsNoTracking().Where(x => x.ViewPermission != null).Select(x => x.ViewPermission!).Distinct().ToListAsync(ct);
        var allSitePerms = new List<string>(); var siteKeys = new List<string>(); var deptKeys = new List<string>();
        foreach (var p in perms)
        {
            var scope = await access.GetScopeAsync(uid, p, ct);
            if (scope.AllSites) allSitePerms.Add(p);
            else
            {
                siteKeys.AddRange(scope.SiteIds.Select(sid => $"{p}|{sid}"));
                deptKeys.AddRange(scope.DepartmentPairs.Select(d => $"{p}|{d.Site}|{d.Dept}"));
            }
        }
        query = query.Where(x => x.ViewPermission == null // not yet backfilled: verified per item below
            || allSitePerms.Contains(x.ViewPermission)
            || (x.SiteId != null && siteKeys.Contains(x.ViewPermission + "|" + x.SiteId.ToString()))
            || (x.SiteId != null && x.DepartmentId != null && deptKeys.Contains(x.ViewPermission + "|" + x.SiteId.ToString() + "|" + x.DepartmentId.ToString())));

        if (!string.IsNullOrWhiteSpace(r.Q))
        {
            var t = "%" + r.Q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(x => EF.Functions.ILike(x.FileName, t) || (x.Description != null && EF.Functions.ILike(x.Description, t)));
        }
        if (r.Kind is { Length: > 0 } k) query = WhereKind(query, k);
        if (r.From is { } from) { var f = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero); query = query.Where(x => x.CreatedAt >= f); }
        if (r.To is { } to) { var tt = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero); query = query.Where(x => x.CreatedAt < tt); }
        if (r.ContentType is { Length: > 0 } ctp) { var c = ctp.Trim().ToLower(); query = query.Where(x => x.ContentType.ToLower().StartsWith(c)); }
        if (r.UploadedById is { } by) query = query.Where(x => x.CreatedById == by);
        if (r.SiteId is { } site) query = query.Where(x => x.SiteId == site);
        if (r.DepartmentId is { } dept) query = query.Where(x => x.DepartmentId == dept);
        if (r.Classification is { } cl) query = query.Where(x => x.Classification == cl);
        if (r.OwnerType is { Length: > 0 } ot) query = query.Where(x => x.OwnerType == ot);
        if (r.Retention != LibraryRetention.Any)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            query = r.Retention switch
            {
                LibraryRetention.None => query.Where(x => x.RetainUntil == null),
                LibraryRetention.Retained => query.Where(x => x.RetainUntil != null && x.RetainUntil > today),
                _ => query.Where(x => x.RetainUntil != null && x.RetainUntil <= today),
            };
        }

        var total = await query.CountAsync(ct);
        IOrderedQueryable<Attachment> ordered = r.Sort switch
        {
            LibrarySort.Oldest => query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            LibrarySort.Name => query.OrderBy(x => x.FileName).ThenBy(x => x.Id),
            LibrarySort.Largest => query.OrderByDescending(x => x.SizeBytes).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id),
        };
        var rows = await ordered.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var lineages = rows.Select(c => c.LineageId).ToList();
        var versions = await db.Attachments.AsNoTracking().Where(x => lineages.Contains(x.LineageId)).GroupBy(x => x.LineageId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var owners = new Dictionary<(string, Guid), RecordInfo?>();
        var items = new List<LibraryItem>();
        var dropped = 0;
        foreach (var a in rows)
        {
            if (!owners.TryGetValue((a.OwnerType, a.OwnerId), out var owner))
            {
                try { owner = await records.RequireViewAsync(a.OwnerType, a.OwnerId, ct); } catch (AppException) { owner = null; }
                owners[(a.OwnerType, a.OwnerId)] = owner;
            }
            if (owner is null) { dropped++; continue; }
            items.Add(new LibraryItem(a.Id, a.FileName, a.ContentType, FileKinds.Of(a.FileName, a.OwnerType, a.Category), a.SizeBytes, a.Version, a.LineageId, versions.GetValueOrDefault(a.LineageId, 1), a.Classification, a.RetainUntil, a.IsLocked, a.ScanStatus, a.CreatedAt,
                owner.Type, owner.Id, owner.Number, owner.Title, owner.Url, a.Category, a.Description));
        }
        return new LibraryPage(items, total, page, size, dropped == 0);
    }

    /// <summary>Copies the owning record's site, department and view permission onto files uploaded before those columns existed. Idempotent; runs at startup in batches.</summary>
    public async Task<int> BackfillScopeAsync(CancellationToken ct, int batch = 500)
    {
        var done = 0;
        while (true)
        {
            var rows = await db.Attachments.Where(x => x.ViewPermission == null).OrderBy(x => x.Id).Take(batch).ToListAsync(ct);
            if (rows.Count == 0) break;
            foreach (var a in rows)
            {
                RecordInfo? rec;
                try { rec = await records.GetAsync(a.OwnerType, a.OwnerId, ct); } catch (AppException) { rec = null; }
                // a file whose record no longer resolves stays out of the library ("" matches no grant)
                a.ViewPermission = rec?.ViewPermission ?? ""; a.SiteId = rec?.SiteId ?? a.SiteId; a.DepartmentId = rec?.DepartmentId;
            }
            await db.SaveChangesAsync(ct);
            done += rows.Count;
            if (rows.Count < batch) break;
        }
        return done;
    }

    /// <summary>All versions of a file, newest first.</summary>
    public async Task<List<Attachment>> VersionsAsync(Guid id, CancellationToken ct)
    {
        var a = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("File");
        await records.RequireViewAsync(a.OwnerType, a.OwnerId, ct);
        return await db.Attachments.AsNoTracking().Where(x => x.LineageId == a.LineageId).OrderByDescending(x => x.Version).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Attachment>> ListAsync(string ownerType, Guid ownerId, bool includeHistory, CancellationToken ct)
    {
        var r = await records.RequireViewAsync(ownerType, ownerId, ct);
        var q = db.Attachments.AsNoTracking().Where(x => x.OwnerType == r.Type && x.OwnerId == r.Id);
        if (!includeHistory) q = q.Where(x => !x.IsArchived);
        return await q.OrderBy(x => x.FileName).ThenByDescending(x => x.Version).ToListAsync(ct);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct)
    {
        var a = await db.Attachments.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("File");
        var r = await records.RequireEditAsync(a.OwnerType, a.OwnerId, ct);
        if (a.IsLocked || r.IsLocked) throw new AppException("This file is locked and cannot be removed.");
        if (a.RetainUntil is { } until && until > await calendar.TodayAsync(r.SiteId, ct))
            throw new AppException($"This file is under retention until {until:yyyy-MM-dd}.");
        a.IsArchived = true;
        a.ArchivedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
