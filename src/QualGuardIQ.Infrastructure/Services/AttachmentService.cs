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

public sealed class AttachmentService(AppDbContext db, IFileStorage storage, IMalwareScanner scanner, IRecordRegistry records, IConfiguration config, ISiteCalendar calendar, IAccessService access, ICurrentUser current, Security.ISecurityEventLogger security)
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
            OwnerType = record.Type, OwnerId = record.Id, SiteId = record.SiteId, FileName = name,
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

    /// <summary>Active retention policy for the record type (without product or regulatory filters) sets the initial retention date.</summary>
    async Task<DateOnly?> PolicyRetentionAsync(RecordInfo record, CancellationToken ct)
    {
        var years = await db.RetentionPolicies.AsNoTracking().Where(p => p.IsActive && p.RecordType == record.Type && p.ProductFamily == null && p.RegulatoryCategory == null).MaxAsync(p => (int?)p.RetainYears, ct);
        return years is > 0 ? (await calendar.TodayAsync(record.SiteId, ct)).AddYears(years.Value) : null;
    }

    /// <summary>Enterprise content library: current versions of files across every record the user can view, filtered by kind and text.</summary>
    public async Task<List<LibraryItem>> LibraryAsync(string? kind, string? q, int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, 200);
        var query = db.Attachments.AsNoTracking().Where(x => !x.IsArchived && x.ScanStatus != ScanStatus.Infected);
        if (!string.IsNullOrWhiteSpace(q)) { var t = q.Trim().ToLower(); query = query.Where(x => x.FileName.ToLower().Contains(t) || (x.Description != null && x.Description.ToLower().Contains(t))); }
        if (kind == "Video") query = query.Where(x => x.ContentType.StartsWith("video/"));
        var candidates = await query.OrderByDescending(x => x.CreatedAt).Take(1000).ToListAsync(ct);
        var versions = await db.Attachments.AsNoTracking().Where(x => candidates.Select(c => c.LineageId).Contains(x.LineageId)).GroupBy(x => x.LineageId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var owners = new Dictionary<(string, Guid), RecordInfo?>();
        var result = new List<LibraryItem>();
        foreach (var a in candidates)
        {
            var k = FileKinds.Of(a.FileName, a.OwnerType, a.Category);
            if (kind is { Length: > 0 } && k != kind) continue;
            if (!owners.TryGetValue((a.OwnerType, a.OwnerId), out var owner))
            {
                try { owner = await records.RequireViewAsync(a.OwnerType, a.OwnerId, ct); } catch (AppException) { owner = null; }
                owners[(a.OwnerType, a.OwnerId)] = owner;
            }
            if (owner is null) continue;
            result.Add(new LibraryItem(a.Id, a.FileName, a.ContentType, k, a.SizeBytes, a.Version, a.LineageId, versions.GetValueOrDefault(a.LineageId, 1), a.Classification, a.RetainUntil, a.IsLocked, a.ScanStatus, a.CreatedAt,
                owner.Type, owner.Id, owner.Number, owner.Title, owner.Url, a.Category, a.Description));
            if (result.Count >= take) break;
        }
        return result;
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
