using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Storage;

namespace QualGuardIQ.Infrastructure.Modules.Video;

public sealed record VideoProbe(decimal? DurationSeconds, int? Width, int? Height, string? VideoCodec, string? AudioCodec);

/// <summary>Adapter boundary for video normalisation. The built-in implementation runs a locally installed ffmpeg/ffprobe.</summary>
public interface IVideoConverter
{
    bool Available { get; }
    /// <summary>What is configured, or what an administrator must configure.</summary>
    string Description { get; }
    Task<VideoProbe> ProbeAsync(string path, CancellationToken ct);
    Task ConvertAsync(string input, string output, VideoFormat format, CancellationToken ct);
}

public sealed class VideoConversionException(string message) : Exception(message);

/// <summary>Used when Video:FfmpegPath is not configured or not executable. It never converts and never reports success.</summary>
public sealed class UnavailableVideoConverter(string reason) : IVideoConverter
{
    public bool Available => false;
    public string Description => reason;
    public Task<VideoProbe> ProbeAsync(string path, CancellationToken ct) => throw new VideoConversionException(reason);
    public Task ConvertAsync(string input, string output, VideoFormat format, CancellationToken ct) => throw new VideoConversionException(reason);
}

public sealed class FfmpegVideoConverter(string ffmpeg, string ffprobe, int maxHeight, TimeSpan timeout) : IVideoConverter
{
    public bool Available => true;
    public string Description => $"ffmpeg at {ffmpeg} (MP4 H.264/AAC, WebM VP9/Opus; max height {maxHeight}px; timeout {timeout.TotalMinutes:0} min).";

    async Task<(int Code, string Out, string Err)> RunAsync(string exe, IEnumerable<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a); // no shell: file names are never interpreted
        using var p = Process.Start(psi) ?? throw new VideoConversionException($"Could not start {exe}.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(timeout);
        var so = p.StandardOutput.ReadToEndAsync(cts.Token); var se = p.StandardError.ReadToEndAsync(cts.Token);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch (InvalidOperationException) { } throw new VideoConversionException(ct.IsCancellationRequested ? "Conversion was cancelled." : $"Conversion exceeded {timeout.TotalMinutes:0} minutes."); }
        return (p.ExitCode, await so, await se);
    }

    public async Task<VideoProbe> ProbeAsync(string path, CancellationToken ct)
    {
        var (code, output, err) = await RunAsync(ffprobe, ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", path], ct);
        if (code != 0) throw new VideoConversionException("The file could not be read as a video: " + Last(err));
        using var doc = JsonDocument.Parse(output);
        var streams = doc.RootElement.TryGetProperty("streams", out var s) ? s.EnumerateArray().ToList() : [];
        var v = streams.FirstOrDefault(x => x.GetProperty("codec_type").GetString() == "video");
        var a = streams.FirstOrDefault(x => x.GetProperty("codec_type").GetString() == "audio");
        decimal? dur = doc.RootElement.TryGetProperty("format", out var f) && f.TryGetProperty("duration", out var d) && decimal.TryParse(d.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var dv) ? Math.Round(dv, 3) : null;
        return new VideoProbe(dur, v.ValueKind == JsonValueKind.Object && v.TryGetProperty("width", out var w) ? w.GetInt32() : null, v.ValueKind == JsonValueKind.Object && v.TryGetProperty("height", out var h) ? h.GetInt32() : null,
            v.ValueKind == JsonValueKind.Object ? v.GetProperty("codec_name").GetString() : null, a.ValueKind == JsonValueKind.Object ? a.GetProperty("codec_name").GetString() : null);
    }

    public async Task ConvertAsync(string input, string output, VideoFormat format, CancellationToken ct)
    {
        var scale = $"scale=-2:'min({maxHeight},trunc(ih/2)*2)'";
        string[] args = format == VideoFormat.Mp4
            ? ["-nostdin", "-y", "-v", "error", "-i", input, "-map", "0:v:0", "-map", "0:a:0?", "-vf", scale, "-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-profile:v", "high", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart", "-f", "mp4", output]
            : ["-nostdin", "-y", "-v", "error", "-i", input, "-map", "0:v:0", "-map", "0:a:0?", "-vf", scale, "-c:v", "libvpx-vp9", "-b:v", "0", "-crf", "33", "-row-mt", "1", "-deadline", "good", "-cpu-used", "4", "-c:a", "libopus", "-b:a", "96k", "-f", "webm", output];
        var (code, _, err) = await RunAsync(ffmpeg, args, ct);
        if (code != 0) throw new VideoConversionException("ffmpeg failed: " + Last(err));
    }

    static string Last(string s) { var t = s.Trim(); return t.Length > 500 ? t[^500..] : t.Length == 0 ? "no detail" : t; }
}

/// <summary>Builds the converter from configuration (Video:FfmpegPath, Video:FfprobePath); tests replace it.</summary>
public sealed class VideoConverterProvider(IConfiguration config)
{
    public IVideoConverter? Override { get; set; }
    public IVideoConverter Current
    {
        get
        {
            if (Override is { } o) return o;
            var ffmpeg = config["Video:FfmpegPath"];
            if (string.IsNullOrWhiteSpace(ffmpeg)) return new UnavailableVideoConverter("No video converter is configured. An administrator must install ffmpeg on the worker host and set Video:FfmpegPath (and Video:FfprobePath if ffprobe is elsewhere). Until then the original file is offered for download and playback.");
            if (!File.Exists(ffmpeg)) return new UnavailableVideoConverter($"Video:FfmpegPath points to {ffmpeg}, which does not exist on this host.");
            var ffprobe = config["Video:FfprobePath"] is { Length: > 0 } fp ? fp : Path.Combine(Path.GetDirectoryName(ffmpeg) ?? "", OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
            if (!File.Exists(ffprobe)) return new UnavailableVideoConverter($"ffprobe was not found at {ffprobe}; set Video:FfprobePath.");
            return new FfmpegVideoConverter(ffmpeg, ffprobe, Math.Clamp(config.GetValue("Video:MaxHeight", 1080), 144, 2160), TimeSpan.FromMinutes(Math.Clamp(config.GetValue("Video:TimeoutMinutes", 30), 1, 240)));
        }
    }

    public static IReadOnlyList<VideoFormat> Formats(IConfiguration config)
    {
        var list = (config["Video:Formats"] ?? "mp4").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(f => f.ToLowerInvariant() switch { "mp4" => (VideoFormat?)VideoFormat.Mp4, "webm" => VideoFormat.WebM, _ => null }).Where(f => f is not null).Select(f => f!.Value).Distinct().ToList();
        return list.Count == 0 ? [VideoFormat.Mp4] : list;
    }
}

public sealed record RenditionRow(Guid Id, VideoFormat Format, RenditionStatus Status, int Attempts, DateTimeOffset QueuedAt, DateTimeOffset? NextAttemptAt, DateTimeOffset? CompletedAt, string? Error, long? SizeBytes,
    decimal? DurationSeconds, int? Width, int? Height, string? VideoCodec, string? AudioCodec, string? SourceVideoCodec, bool Reused);
public sealed record VideoStatus(bool ConverterAvailable, string Converter, IReadOnlyList<VideoFormat> Formats, List<RenditionRow> Renditions);

/// <summary>Background normalisation of uploaded videos into playback copies, with probing, output validation, de-duplication, retry and a fallback to the original.</summary>
public sealed class VideoConversionService(AppDbContext db, IFileStorage storage, VideoConverterProvider converters, IConfiguration config, IClock clock, IRecordRegistry records, Services.AttachmentService files, ILogger<VideoConversionService> log)
{
    public static readonly TimeSpan[] Backoff = [TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2)];
    int MaxAttempts => Math.Clamp(config.GetValue("Video:MaxAttempts", 3), 1, 6);
    static string Mime(VideoFormat f) => f == VideoFormat.WebM ? "video/webm" : "video/mp4";
    static RenditionRow Row(VideoRendition r) => new(r.Id, r.Format, r.Status, r.Attempts, r.QueuedAt, r.NextAttemptAt, r.CompletedAt, r.Error, r.SizeBytes, r.DurationSeconds, r.Width, r.Height, r.VideoCodec, r.AudioCodec, r.SourceVideoCodec, r.ReusedFromId is not null);

    public async Task<VideoStatus> StatusAsync(Guid attachmentId, CancellationToken ct)
    {
        var a = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == attachmentId, ct) ?? throw new NotFoundException("File");
        await records.RequireViewAsync(a.OwnerType, a.OwnerId, ct);
        var c = converters.Current;
        var rows = await db.VideoRenditions.AsNoTracking().Where(r => r.AttachmentId == attachmentId).OrderBy(r => r.Format).ToListAsync(ct);
        return new VideoStatus(c.Available, c.Description, VideoConverterProvider.Formats(config), rows.Select(Row).ToList());
    }

    /// <summary>Requeues failed renditions (edit permission on the owning record), and queues any configured format that is missing.</summary>
    public async Task<VideoStatus> RetryAsync(Guid attachmentId, CancellationToken ct)
    {
        var a = await db.Attachments.FirstOrDefaultAsync(x => x.Id == attachmentId, ct) ?? throw new NotFoundException("File");
        await records.RequireEditAsync(a.OwnerType, a.OwnerId, ct);
        if (!Services.FileKinds.Videos.Contains(Path.GetExtension(a.FileName).ToLowerInvariant())) throw new AppException("Only videos have playback copies.");
        var existing = await db.VideoRenditions.Where(r => r.AttachmentId == attachmentId).ToListAsync(ct);
        foreach (var r in existing.Where(r => r.Status == RenditionStatus.Failed)) { r.Status = RenditionStatus.Queued; r.Attempts = 0; r.NextAttemptAt = null; r.Error = null; r.QueuedAt = clock.UtcNow; }
        foreach (var f in VideoConverterProvider.Formats(config).Where(f => existing.All(r => r.Format != f)))
            db.VideoRenditions.Add(new VideoRendition { AttachmentId = attachmentId, Format = f, QueuedAt = clock.UtcNow });
        await db.SaveChangesAsync(ct);
        return await StatusAsync(attachmentId, ct);
    }

    /// <summary>Opens a succeeded playback copy with the same permission rules as the original (checked by opening the original's metadata) and verifies its checksum.</summary>
    public async Task<(VideoRendition Meta, Stream Content)> OpenAsync(Guid attachmentId, VideoFormat format, CancellationToken ct)
    {
        var r = await db.VideoRenditions.AsNoTracking().FirstOrDefaultAsync(x => x.AttachmentId == attachmentId && x.Format == format && x.Status == RenditionStatus.Succeeded, ct) ?? throw new NotFoundException("Playback copy");
        var (_, original) = await files.OpenAsync(attachmentId, ct); // permission, quarantine and security-log rules of the original
        await original.DisposeAsync();
        var tmp = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var s = await storage.GetAsync(r.StorageKey!, ct))
            {
                var chunk = new byte[81920]; int n;
                while ((n = await s.ReadAsync(chunk, ct)) > 0) { sha.AppendData(chunk, 0, n); await tmp.WriteAsync(chunk.AsMemory(0, n), ct); }
            }
            if (Convert.ToHexStringLower(sha.GetHashAndReset()) != r.Sha256) throw new AppException("The playback copy failed its integrity check; download the original.", 500);
        }
        catch { await tmp.DisposeAsync(); throw; }
        tmp.Position = 0;
        return (r, tmp);
    }

    /// <summary>Worker: converts due renditions (a few per run), requeues stuck ones, and never marks success without a validated output.</summary>
    public async Task<int> RunAsync(CancellationToken ct, int batch = 2)
    {
        var now = clock.UtcNow;
        var timeout = TimeSpan.FromMinutes(Math.Clamp(config.GetValue("Video:TimeoutMinutes", 30), 1, 240));
        var stuck = now - timeout - TimeSpan.FromMinutes(10);
        await db.VideoRenditions.Where(r => r.Status == RenditionStatus.Processing && r.StartedAt < stuck)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, RenditionStatus.Queued).SetProperty(r => r.Error, "The previous conversion did not finish (worker stopped); requeued."), ct);
        var converter = converters.Current;
        if (!converter.Available)
        {
            // waiting is not a failure: attempts are not used up and nothing is reported as converted
            await db.VideoRenditions.Where(r => r.Status == RenditionStatus.Queued && (r.Error == null || r.Error != converter.Description)).ExecuteUpdateAsync(u => u.SetProperty(r => r.Error, converter.Description), ct);
            return 0;
        }
        var due = await db.VideoRenditions.AsNoTracking().Where(r => r.Status == RenditionStatus.Queued && (r.NextAttemptAt == null || r.NextAttemptAt <= now)).OrderBy(r => r.QueuedAt).Select(r => r.Id).Take(batch).ToListAsync(ct);
        var done = 0;
        foreach (var id in due)
        {
            // claim it so two workers never convert the same rendition
            var claimed = await db.VideoRenditions.Where(r => r.Id == id && r.Status == RenditionStatus.Queued)
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, RenditionStatus.Processing).SetProperty(r => r.StartedAt, clock.UtcNow).SetProperty(r => r.Attempts, r => r.Attempts + 1), ct);
            if (claimed == 0) continue;
            var r = await db.VideoRenditions.FirstAsync(x => x.Id == id, ct);
            await db.Entry(r).ReloadAsync(ct);
            try { await ConvertOneAsync(r, converter, ct); done++; }
            catch (Exception e) when (e is VideoConversionException or IOException or JsonException or AppException or InvalidOperationException && !ct.IsCancellationRequested)
            {
                r.Error = e.Message.Length > 2000 ? e.Message[..2000] : e.Message; r.StorageKey = null; r.Sha256 = null; r.SizeBytes = null;
                if (r.Attempts < MaxAttempts) { r.Status = RenditionStatus.Queued; r.NextAttemptAt = clock.UtcNow + Backoff[Math.Min(r.Attempts - 1, Backoff.Length - 1)]; }
                else { r.Status = RenditionStatus.Failed; r.NextAttemptAt = null; r.CompletedAt = clock.UtcNow; log.LogWarning("Video {Attachment} {Format} conversion failed: {Error}", r.AttachmentId, r.Format, r.Error); }
                await db.SaveChangesAsync(ct);
            }
        }
        return done;
    }

    async Task ConvertOneAsync(VideoRendition r, IVideoConverter converter, CancellationToken ct)
    {
        var a = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.AttachmentId, ct) ?? throw new AppException("The original file no longer exists.");
        if (a.ScanStatus == ScanStatus.Infected) throw new VideoConversionException("The original is quarantined; it is not converted.");

        // identical content already converted to this format: reuse the validated output
        var twin = await db.VideoRenditions.AsNoTracking().Where(x => x.Format == r.Format && x.Status == RenditionStatus.Succeeded && x.Id != r.Id && x.ReusedFromId == null)
            .Join(db.Attachments.Where(o => o.Sha256 == a.Sha256), x => x.AttachmentId, o => o.Id, (x, _) => x).FirstOrDefaultAsync(ct);
        if (twin is not null)
        {
            Copy(twin, r); r.ReusedFromId = twin.Id; r.Status = RenditionStatus.Succeeded; r.CompletedAt = clock.UtcNow; r.Error = null; r.NextAttemptAt = null;
            await db.SaveChangesAsync(ct);
            return;
        }

        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "qgiq-video-" + r.Id.ToString("N")));
        try
        {
            var input = Path.Combine(dir.FullName, "source" + Path.GetExtension(a.FileName).ToLowerInvariant());
            var output = Path.Combine(dir.FullName, "playback." + (r.Format == VideoFormat.WebM ? "webm" : "mp4"));
            await using (var src = await storage.GetAsync(a.StorageKey, ct))
            await using (var fs = File.Create(input))
            {
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var chunk = new byte[81920]; int n;
                while ((n = await src.ReadAsync(chunk, ct)) > 0) { sha.AppendData(chunk, 0, n); await fs.WriteAsync(chunk.AsMemory(0, n), ct); }
                if (Convert.ToHexStringLower(sha.GetHashAndReset()) != a.Sha256) throw new VideoConversionException("The stored original failed its integrity check.");
            }
            var source = await converter.ProbeAsync(input, ct);
            r.SourceVideoCodec = source.VideoCodec; r.SourceDurationSeconds = source.DurationSeconds;
            if (source.VideoCodec is null) throw new VideoConversionException("The file has no video stream.");
            await converter.ConvertAsync(input, output, r.Format, ct);

            // validate before claiming success
            var info = new FileInfo(output);
            if (!info.Exists || info.Length == 0) throw new VideoConversionException("The converter produced no output.");
            var outProbe = await converter.ProbeAsync(output, ct);
            var expected = r.Format == VideoFormat.WebM ? "vp9" : "h264";
            if (outProbe.VideoCodec != expected) throw new VideoConversionException($"The output video codec is {outProbe.VideoCodec ?? "missing"}, expected {expected}.");
            if (source.DurationSeconds is { } sd && outProbe.DurationSeconds is { } od && Math.Abs(sd - od) > Math.Max(1m, sd * 0.02m))
                throw new VideoConversionException($"The output lasts {od:0.##} s but the original lasts {sd:0.##} s.");
            if (source.DurationSeconds is not null && outProbe.DurationSeconds is null) throw new VideoConversionException("The output duration could not be read.");

            string hash;
            await using (var fs = File.OpenRead(output)) hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(fs, ct));
            var key = $"{a.StorageKey}/playback-{r.Format.ToString().ToLowerInvariant()}";
            await using (var fs = File.OpenRead(output)) await storage.PutAsync(key, fs, Mime(r.Format), ct);
            r.StorageKey = key; r.Sha256 = hash; r.SizeBytes = info.Length; r.ContentType = Mime(r.Format);
            r.DurationSeconds = outProbe.DurationSeconds; r.Width = outProbe.Width; r.Height = outProbe.Height; r.VideoCodec = outProbe.VideoCodec; r.AudioCodec = outProbe.AudioCodec;
            r.Converter = converter.Description.Length > 200 ? converter.Description[..200] : converter.Description;
            r.Status = RenditionStatus.Succeeded; r.CompletedAt = clock.UtcNow; r.Error = null; r.NextAttemptAt = null;
            await db.SaveChangesAsync(ct);
        }
        finally { try { dir.Delete(true); } catch (IOException) { } }
    }

    static void Copy(VideoRendition from, VideoRendition to)
    {
        to.StorageKey = from.StorageKey; to.Sha256 = from.Sha256; to.SizeBytes = from.SizeBytes; to.ContentType = from.ContentType; to.DurationSeconds = from.DurationSeconds; to.Width = from.Width; to.Height = from.Height;
        to.VideoCodec = from.VideoCodec; to.AudioCodec = from.AudioCodec; to.SourceVideoCodec = from.SourceVideoCodec; to.SourceDurationSeconds = from.SourceDurationSeconds; to.Converter = from.Converter;
    }
}
