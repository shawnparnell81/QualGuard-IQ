using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Storage;

namespace QualGuardIQ.Infrastructure.Services;

public sealed record UploadRequest(string FileName, string ContentType, Stream Content, string? Category, string? Description, Classification Classification, Guid? ReplacesId);

public sealed class AttachmentService(AppDbContext db, IFileStorage storage, IMalwareScanner scanner, IRecordRegistry records, IConfiguration config, ISiteCalendar calendar)
{
    private static readonly HashSet<string> Blocked = [".exe", ".dll", ".bat", ".cmd", ".com", ".msi", ".ps1", ".sh", ".js", ".vbs", ".scr", ".jar", ".html", ".htm", ".svg"];

    private long MaxBytes => config.GetValue("Storage:MaxFileBytes", 100L * 1024 * 1024);
    private HashSet<string> Allowed => config.GetSection("Storage:AllowedExtensions").Get<string[]>()?.Select(e => e.ToLowerInvariant()).ToHashSet()
        ?? [".pdf", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".tif", ".tiff", ".txt", ".csv", ".docx", ".xlsx", ".pptx", ".doc", ".xls", ".dwg", ".dxf", ".step", ".stp", ".igs", ".iges", ".zip", ".json", ".xml"];

    public async Task<Attachment> UploadAsync(string ownerType, Guid ownerId, UploadRequest req, CancellationToken ct, bool skipPermission = false)
    {
        var record = skipPermission ? await records.GetAsync(ownerType, ownerId, ct) : await records.RequireEditAsync(ownerType, ownerId, ct);
        if (record.IsLocked && !skipPermission) throw new AppException("This record is locked; files cannot be added.");
        var name = Path.GetFileName(req.FileName ?? "").Trim();
        if (name.Length is 0 or > 255) throw new AppException("A file name of up to 255 characters is required.");
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (Blocked.Contains(ext) || !Allowed.Contains(ext)) throw new AppException($"Files of type '{ext}' are not allowed.");

        await using var buffer = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await req.Content.ReadAsync(chunk, ct)) > 0)
        {
            total += read;
            if (total > MaxBytes) throw new AppException($"Files may not exceed {MaxBytes / (1024 * 1024)} MB.");
            sha.AppendData(chunk, 0, read);
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        if (total == 0) throw new AppException("The file is empty.");
        buffer.Position = 0;
        var scan = await scanner.ScanAsync(buffer, name, ct);
        if (scan == ScanStatus.Infected) throw new AppException("The file was rejected by the malware scanner.");

        var a = new Attachment
        {
            OwnerType = record.Type, OwnerId = record.Id, SiteId = record.SiteId, FileName = name,
            ContentType = string.IsNullOrWhiteSpace(req.ContentType) ? "application/octet-stream" : req.ContentType,
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
        a.StorageKey = $"{record.Type.ToLowerInvariant()}/{record.Id:N}/{a.Id:N}";
        buffer.Position = 0;
        await storage.PutAsync(a.StorageKey, buffer, a.ContentType, ct);
        db.Attachments.Add(a);
        await db.SaveChangesAsync(ct);
        return a;
    }

    /// <summary>Opens a file after checking view permission on the owning record and verifying the checksum.</summary>
    public async Task<(Attachment Meta, Stream Content)> OpenAsync(Guid id, CancellationToken ct)
    {
        var a = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("File");
        await records.RequireViewAsync(a.OwnerType, a.OwnerId, ct);
        if (a.ScanStatus == ScanStatus.Infected) throw new AppException("This file is quarantined.", 403);
        var ms = new MemoryStream();
        await using (var s = await storage.GetAsync(a.StorageKey, ct)) await s.CopyToAsync(ms, ct);
        var hash = Convert.ToHexStringLower(SHA256.HashData(ms.ToArray()));
        if (hash != a.Sha256) throw new AppException("The stored file failed its integrity check. Contact an administrator.", 500);
        ms.Position = 0;
        return (a, ms);
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
