using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Tests;

/// <summary>Gap 21: the content library pages and searches on the server with no 1,000-file cap, stays permission-aware and has stable ordering.</summary>
[Collection("api")]
public sealed class GapClosureContentLibraryTests(TestApp app)
{
    const int N = 1250;

    [Fact]
    public async Task Library_pages_and_searches_beyond_1000_files_with_filters_scope_and_stable_order()
    {
        var site = await app.CreateSiteAsync();
        var admin = await app.AdminAsync();
        var dept = await (await admin.PostOkAsync($"/api/organization/sites/{site}/departments", new { code = "QL", name = "Quality lab" })).Content.ReadFromJsonAsync<Guid>();
        var (_, viewer) = await app.CreateUserAsync([Permissions.NcrView, Permissions.NcrEdit], site);
        var (_, deptOnly) = await app.CreateUserAsync([Permissions.NcrView], site, dept);
        var other = await app.CreateSiteAsync();
        var (_, outsider) = await app.CreateUserAsync([Permissions.NcrView], other);
        var ncr = (await (await viewer.PostOkAsync("/api/ncrs", new { title = "Library volume test", description = "Many files", source = "In-process", siteId = site, departmentId = dept, detectedOn = TestDates.Today, severity = 1 })).JsonAsync()).Id();
        var tag = "lib" + Guid.NewGuid().ToString("N")[..8];

        // 1,250 current files (bulk insert): #0001 is the oldest; every 5th is a video, files 101-110 are in the Drawing category (2 of them are videos, which stay videos), 40 confidential, 60 in the department, 30 retained.
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var bulkSql = $$"""
                INSERT INTO qms.attachments ("Id","OwnerType","OwnerId","SiteId","DepartmentId","ViewPermission","FileName","ContentType","SizeBytes","Sha256","StorageKey","Category","Description","Classification","LineageId","Version","IsLocked","ScanStatus","RetainUntil","IsArchived","CreatedAt","UpdatedAt")
                SELECT u.id, 'Ncr', {0}, {1}, CASE WHEN u.g <= 60 THEN {2} ELSE NULL END, 'ncr.view',
                       '{{tag}}-' || lpad(g::text, 4, '0') || CASE WHEN g % 5 = 0 THEN '.mp4' ELSE '.pdf' END,
                       CASE WHEN g % 5 = 0 THEN 'video/mp4' ELSE 'application/pdf' END, 1000 + g, repeat('a', 64), 'test/{{tag}}/' || g,
                       CASE WHEN g BETWEEN 101 AND 110 THEN 'Drawing' ELSE NULL END, 'bulk file ' || g,
                       CASE WHEN g BETWEEN 201 AND 240 THEN 1 ELSE 0 END, u.id, 1, false, 1,
                       CASE WHEN g BETWEEN 301 AND 330 THEN CURRENT_DATE + 3650 ELSE NULL END, false,
                       TIMESTAMPTZ '2025-01-01 00:00:00+00' + g * INTERVAL '1 hour', now()
                FROM (SELECT g, gen_random_uuid() AS id FROM generate_series(1, {{N}}) g) u
                """;
            await db.Database.ExecuteSqlRawAsync(bulkSql, ncr, site, dept);
        }
        async Task<JsonElement> Page(ApiClient c, string qs) => await c.GetElAsync($"/api/files/search?q={tag}&{qs}");

        var sw = Stopwatch.StartNew();
        var first = await Page(viewer, "pageSize=100");
        Assert.Equal(N, first.GetProperty("total").GetInt32());
        Assert.True(first.GetProperty("totalIsExact").GetBoolean());
        Assert.Equal(100, first.GetProperty("items").GetArrayLength());
        Assert.Equal($"{tag}-1250.mp4", first.GetProperty("items")[0].Str("fileName")); // newest first

        // Beyond the old 1,000 cap: the last page holds the oldest files.
        var last = await Page(viewer, "pageSize=100&page=13");
        Assert.Equal(50, last.GetProperty("items").GetArrayLength());
        Assert.Equal($"{tag}-0001.pdf", last.GetProperty("items")[49].Str("fileName"));
        Assert.True(sw.ElapsedMilliseconds < 10_000, $"two pages took {sw.ElapsedMilliseconds} ms");
        var oldest = await viewer.GetElAsync($"/api/files/search?q={tag}-0001");
        Assert.Equal(1, oldest.GetProperty("total").GetInt32());

        // Stable order: name-sorted pages neither overlap nor skip.
        var seen = new HashSet<string>();
        for (var p = 1; p <= 13; p++)
            foreach (var i in (await Page(viewer, $"sort=2&pageSize=100&page={p}")).GetProperty("items").EnumerateArray()) Assert.True(seen.Add(i.Str("id")), "duplicate across pages");
        Assert.Equal(N, seen.Count);

        // Filters: kind, content type, date range, classification, retention, department.
        Assert.Equal(250, (await Page(viewer, "kind=Video")).GetProperty("total").GetInt32());
        Assert.Equal(8, (await Page(viewer, "kind=Drawing")).GetProperty("total").GetInt32());
        Assert.Equal(992, (await Page(viewer, "kind=Document")).GetProperty("total").GetInt32());
        Assert.Equal(250, (await Page(viewer, "contentType=video/")).GetProperty("total").GetInt32());
        Assert.Equal(24, (await Page(viewer, "from=2025-01-02&to=2025-01-02")).GetProperty("total").GetInt32()); // hours 24..47 → files 24..47
        Assert.Equal(40, (await Page(viewer, "classification=1")).GetProperty("total").GetInt32());
        Assert.Equal(30, (await Page(viewer, "retention=2")).GetProperty("total").GetInt32());
        Assert.Equal(N - 30, (await Page(viewer, "retention=1")).GetProperty("total").GetInt32());
        Assert.Equal(60, (await Page(viewer, $"departmentId={dept}")).GetProperty("total").GetInt32());
        Assert.Equal(1, (await viewer.GetElAsync($"/api/files/search?q=bulk%20file%201250")).GetProperty("total").GetInt32()); // description search
        Assert.Equal(0, (await viewer.GetElAsync($"/api/files/search?q={tag}%25")).GetProperty("total").GetInt32()); // wildcards are literal

        // Permission-aware: other site sees nothing; a department-scoped user sees only the department's files.
        Assert.Equal(0, (await Page(outsider, "")).GetProperty("total").GetInt32());
        var d = await Page(deptOnly, "pageSize=200");
        Assert.Equal(60, d.GetProperty("total").GetInt32());

        // Compatibility endpoint still answers (first page only).
        Assert.Equal(200, (await viewer.GetElAsync($"/api/files?q={tag}&take=500")).GetArrayLength());
    }

    [Fact]
    public async Task Files_uploaded_before_the_scope_columns_are_backfilled_from_their_record()
    {
        var site = await app.CreateSiteAsync();
        var (_, viewer) = await app.CreateUserAsync([Permissions.NcrView, Permissions.NcrEdit], site);
        var ncr = (await (await viewer.PostOkAsync("/api/ncrs", new { title = "Backfill test", description = "Old files", source = "In-process", siteId = site, detectedOn = TestDates.Today, severity = 1 })).JsonAsync()).Id();
        var tag = "bf" + Guid.NewGuid().ToString("N")[..8];
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var backfillSql = $$"""
            INSERT INTO qms.attachments ("Id","OwnerType","OwnerId","SiteId","FileName","ContentType","SizeBytes","Sha256","StorageKey","Classification","LineageId","Version","IsLocked","ScanStatus","IsArchived","CreatedAt","UpdatedAt")
            SELECT u.id, 'Ncr', {0}, NULL, '{{tag}}-' || g || '.pdf', 'application/pdf', 10, repeat('b', 64), 'test/{{tag}}/' || g, 0, u.id, 1, g = 1, 1, false, now(), now()
            FROM (SELECT g, gen_random_uuid() AS id FROM generate_series(1, 3) g) u
            """;
        await db.Database.ExecuteSqlRawAsync(backfillSql, ncr);
        // before backfill the files are still visible (verified per item), after backfill they are scoped in the database
        Assert.Equal(3, (await viewer.GetElAsync($"/api/files/search?q={tag}")).GetProperty("total").GetInt32());
        Assert.True(await scope.ServiceProvider.GetRequiredService<AttachmentService>().BackfillScopeAsync(CancellationToken.None) >= 3);
        var rows = await db.Attachments.AsNoTracking().Where(x => x.FileName.StartsWith(tag)).ToListAsync();
        Assert.All(rows, r => { Assert.Equal("ncr.view", r.ViewPermission); Assert.Equal(site, r.SiteId); Assert.Equal(".pdf", r.Extension); });
        Assert.Equal(3, (await viewer.GetElAsync($"/api/files/search?q={tag}")).GetProperty("total").GetInt32());
    }
}
