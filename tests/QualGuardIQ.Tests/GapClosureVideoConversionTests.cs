using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Video;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Tests;

/// <summary>Gap 23: background normalisation of uploaded videos into validated playback copies, with an honest waiting state when no converter is configured.</summary>
[Collection("api")]
public sealed class GapClosureVideoConversionTests(TestApp app)
{
    static readonly byte[] Sample = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Video", "sample.mov"));
    const string Ffmpeg = "/usr/bin/ffmpeg";

    static async Task<Guid> UploadAsync(ApiClient c, Guid ncr, string name, byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes); file.Headers.ContentType = new MediaTypeHeaderValue("video/quicktime");
        form.Add(file, "file", name); form.Add(new StringContent("0"), "classification");
        var r = await c.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/records/Ncr/{ncr}/files") { Content = form });
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return (await r.JsonAsync()).Id();
    }

    async Task<Guid> NcrAsync(ApiClient c, Guid site) =>
        (await (await c.PostOkAsync("/api/ncrs", new { title = "Video evidence", description = "Line video", source = "In-process", siteId = site, detectedOn = TestDates.Today, severity = 1 })).JsonAsync()).Id();

    async Task RunAsync()
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<VideoConversionService>().RunAsync(CancellationToken.None, batch: 25);
    }

    async Task DueNowAsync()
    {
        using var scope = app.Services.CreateScope();
        var past = DateTimeOffset.UtcNow.AddSeconds(-1);
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().VideoRenditions.Where(r => r.Status == RenditionStatus.Queued).ExecuteUpdateAsync(u => u.SetProperty(r => r.NextAttemptAt, past));
    }

    VideoConverterProvider Provider => app.Services.GetRequiredService<VideoConverterProvider>();
    static IVideoConverter Real() => new FfmpegVideoConverter(Ffmpeg, "/usr/bin/ffprobe", 1080, TimeSpan.FromMinutes(2));

    [Fact]
    public async Task Without_a_converter_videos_wait_with_a_clear_reason_and_the_original_stays_available()
    {
        var admin = await app.AdminAsync();
        Provider.Override = new UnavailableVideoConverter("No video converter is configured. An administrator must install ffmpeg on the worker host and set Video:FfmpegPath.");
        try
        {
            var ncr = await NcrAsync(admin, await app.CreateSiteAsync());
            var id = await UploadAsync(admin, ncr, "line.mov", Sample);
            await RunAsync(); await RunAsync();
            var s = await admin.GetElAsync($"/api/files/{id}/renditions");
            Assert.False(s.GetProperty("converterAvailable").GetBoolean());
            Assert.Contains("Video:FfmpegPath", s.GetProperty("converter").GetString());
            var r = Assert.Single(s.GetProperty("renditions").EnumerateArray());
            Assert.Equal((int)RenditionStatus.Queued, r.GetProperty("status").GetInt32());
            Assert.Equal(0, r.GetProperty("attempts").GetInt32()); // waiting never uses up attempts
            Assert.Contains("Video:FfmpegPath", r.GetProperty("error").GetString());
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/files/{id}/playback/Mp4")).StatusCode);
            var original = await admin.GetAsync($"/api/files/{id}");
            Assert.Equal(Sample, await original.Content.ReadAsByteArrayAsync());
        }
        finally { Provider.Override = null; }
    }

    [FfmpegFact]
    public async Task Ffmpeg_produces_a_validated_mp4_with_metadata_and_identical_uploads_reuse_it()
    {
        var admin = await app.AdminAsync();
        Provider.Override = Real();
        try
        {
            var site = await app.CreateSiteAsync();
            var ncr = await NcrAsync(admin, site);
            var id = await UploadAsync(admin, ncr, "setup.mov", Sample);
            await RunAsync();
            var r = (await admin.GetElAsync($"/api/files/{id}/renditions")).GetProperty("renditions")[0];
            Assert.Equal((int)RenditionStatus.Succeeded, r.GetProperty("status").GetInt32());
            Assert.Equal("h264", r.GetProperty("videoCodec").GetString());
            Assert.Equal("aac", r.GetProperty("audioCodec").GetString());
            Assert.Equal("mpeg4", r.GetProperty("sourceVideoCodec").GetString());
            Assert.Equal(160, r.GetProperty("width").GetInt32());
            Assert.InRange(r.GetProperty("durationSeconds").GetDecimal(), 1.8m, 2.2m);

            var play = await admin.GetAsync($"/api/files/{id}/playback/Mp4");
            Assert.Equal("video/mp4", play.Content.Headers.ContentType!.MediaType);
            var bytes = await play.Content.ReadAsByteArrayAsync();
            Assert.Equal(r.GetProperty("sizeBytes").GetInt64(), bytes.Length);
            Assert.Equal("ftyp", System.Text.Encoding.ASCII.GetString(bytes, 4, 4));
            // the original is preserved byte for byte
            Assert.Equal(SHA256.HashData(Sample), SHA256.HashData(await (await admin.GetAsync($"/api/files/{id}")).Content.ReadAsByteArrayAsync()));

            // the same content uploaded again reuses the validated output instead of converting twice
            var again = await UploadAsync(admin, ncr, "setup-copy.mov", Sample);
            await RunAsync();
            var r2 = (await admin.GetElAsync($"/api/files/{again}/renditions")).GetProperty("renditions")[0];
            Assert.Equal((int)RenditionStatus.Succeeded, r2.GetProperty("status").GetInt32());
            Assert.True(r2.GetProperty("reused").GetBoolean());

            // playback follows the original's permissions
            var (_, outsider) = await app.CreateUserAsync([QualGuardIQ.Domain.Security.Permissions.NcrView], await app.CreateSiteAsync());
            var denied = await outsider.GetAsync($"/api/files/{id}/playback/Mp4");
            Assert.True(denied.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        }
        finally { Provider.Override = null; }
    }

    [FfmpegFact]
    public async Task Unreadable_video_is_retried_then_failed_never_reported_converted_and_can_be_requeued()
    {
        var admin = await app.AdminAsync();
        Provider.Override = Real();
        try
        {
            var ncr = await NcrAsync(admin, await app.CreateSiteAsync());
            var junk = new byte[4096]; Random.Shared.NextBytes(junk);
            new byte[] { 0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m' }.CopyTo(junk, 0); // looks like an MP4 at the start, unreadable after that
            var id = await UploadAsync(admin, ncr, "broken.mp4", junk);
            for (var i = 0; i < 3; i++) { await RunAsync(); await DueNowAsync(); }
            var r = (await admin.GetElAsync($"/api/files/{id}/renditions")).GetProperty("renditions")[0];
            Assert.Equal((int)RenditionStatus.Failed, r.GetProperty("status").GetInt32());
            Assert.Equal(3, r.GetProperty("attempts").GetInt32());
            Assert.Contains("could not be read as a video", r.GetProperty("error").GetString());
            Assert.Equal(JsonValueKind.Null, r.GetProperty("sizeBytes").ValueKind);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/files/{id}/playback/Mp4")).StatusCode);

            var s = await (await admin.PostOkAsync($"/api/files/{id}/renditions/retry")).JsonAsync();
            Assert.Equal((int)RenditionStatus.Queued, s.GetProperty("renditions")[0].GetProperty("status").GetInt32());
            Assert.Equal(0, s.GetProperty("renditions")[0].GetProperty("attempts").GetInt32());
            var (_, viewer) = await app.CreateUserAsync([QualGuardIQ.Domain.Security.Permissions.NcrView]);
            var forbidden = await viewer.PostAsync($"/api/files/{id}/renditions/retry");
            Assert.True(forbidden.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        }
        finally { Provider.Override = null; }
    }

    [FfmpegFact]
    public async Task A_converter_that_returns_a_wrong_codec_is_caught_by_output_validation()
    {
        var admin = await app.AdminAsync();
        Provider.Override = new CopyingConverter(Real());
        try
        {
            var ncr = await NcrAsync(admin, await app.CreateSiteAsync());
            var bytes = Sample.Concat(new byte[] { 0, 0, 0, 0 }).ToArray(); // distinct content so no earlier output is reused
            var id = await UploadAsync(admin, ncr, "copied.mov", bytes);
            await RunAsync();
            var r = (await admin.GetElAsync($"/api/files/{id}/renditions")).GetProperty("renditions")[0];
            Assert.Equal((int)RenditionStatus.Queued, r.GetProperty("status").GetInt32());
            Assert.Contains("expected h264", r.GetProperty("error").GetString());
        }
        finally { Provider.Override = null; }
    }

    /// <summary>Claims to convert but just copies the input: validation must refuse it.</summary>
    sealed class CopyingConverter(IVideoConverter inner) : IVideoConverter
    {
        public bool Available => true;
        public string Description => "copying test converter";
        public Task<VideoProbe> ProbeAsync(string path, CancellationToken ct) => inner.ProbeAsync(path, ct);
        public Task ConvertAsync(string input, string output, VideoFormat format, CancellationToken ct) { File.Copy(input, output, true); return Task.CompletedTask; }
    }
}

/// <summary>Runs only where ffmpeg is installed (reported as skipped elsewhere, never as passed).</summary>
public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute() { if (!File.Exists("/usr/bin/ffmpeg") || !File.Exists("/usr/bin/ffprobe")) Skip = "ffmpeg/ffprobe are not installed on this host."; }
}
