using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using QualGuardIQ.Api.Infrastructure;
using QualGuardIQ.Domain.Intelligence;
using QualGuardIQ.Infrastructure;
using QualGuardIQ.Infrastructure.Modules.Intelligence;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Security;
using QualGuardIQ.Infrastructure.Services;
using QualGuardIQ.Infrastructure.Storage;

namespace QualGuardIQ.Tests;

/// <summary>Follow-up fixes from the verification of the October 2026 security review (QG-13/QG-20 to QG-23, QG-15, QG-18).</summary>
[Collection("api")]
public sealed class SecurityFollowUpTests(TestApp app)
{
    static IConfiguration Config(Dictionary<string, string?>? values = null) => new ConfigurationBuilder().AddInMemoryCollection(values ?? new()).Build();

    /// <summary>A port on this computer that nothing listens on.</summary>
    static int ClosedPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    static string Repo(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docker-compose.yml"))) dir = dir.Parent;
        return Path.Combine([dir?.FullName ?? throw new InvalidOperationException("Repository root not found"), .. parts]);
    }

    // ---------- QG-13 / QG-20: generation providers and single sign-on go through the outbound guard ----------

    [Fact]
    public async Task QG20_external_generation_providers_cannot_reach_internal_addresses_and_local_ones_cannot_reach_metadata()
    {
        var http = new GenerationHttpProvider(new OutboundGuard(Config()), Config());
        using (var external = http.Create(TimeSpan.FromSeconds(5), GenerationProviderKind.ExternalHttp))
            foreach (var url in new[] { "http://127.0.0.1:9/v1", "http://169.254.169.254/latest/meta-data/", "http://10.0.0.5/v1", "http://localhost:9/v1" })
                Assert.Contains("internal address", (await Assert.ThrowsAsync<HttpRequestException>(() => external.GetAsync(url))).Message);

        using var local = http.Create(TimeSpan.FromSeconds(5), GenerationProviderKind.LocalHttp);
        Assert.Contains("internal address", (await Assert.ThrowsAsync<HttpRequestException>(() => local.GetAsync("http://169.254.169.254/latest/meta-data/"))).Message);
        // a model server on this computer is allowed; nothing listens there, so the connection itself fails
        var refused = await Assert.ThrowsAsync<HttpRequestException>(() => local.GetAsync($"http://127.0.0.1:{ClosedPort()}/gen"));
        Assert.DoesNotContain("internal address", refused.Message);

        var narrowed = new GenerationHttpProvider(new OutboundGuard(Config()), Config(new() { ["Generation:LocalNetworks"] = "10.9.0.0/16" }));
        using var narrow = narrowed.Create(TimeSpan.FromSeconds(5), GenerationProviderKind.LocalHttp);
        Assert.Contains("internal address", (await Assert.ThrowsAsync<HttpRequestException>(() => narrow.GetAsync("http://127.0.0.1:9/gen"))).Message);
        Assert.Throws<InvalidOperationException>(() => new GenerationHttpProvider(new OutboundGuard(Config()), Config(new() { ["Generation:LocalNetworks"] = "nope" })));
    }

    [Fact]
    public async Task QG20_a_provider_connection_test_to_cloud_metadata_is_refused_before_connecting()
    {
        var admin = await app.AdminAsync();
        Assert.Null(app.Services.GetRequiredService<GenerationHttpProvider>().Override);
        var id = (await (await admin.PostOkAsync("/api/generation/providers", new { name = "meta " + Guid.NewGuid().ToString("N")[..6], kind = 1, endpoint = "https://169.254.169.254/v1", model = "m", secretReference = (string?)null })).JsonAsync()).Id();
        var started = DateTime.UtcNow;
        var r = await (await admin.PostOkAsync($"/api/generation/providers/{id}/test")).JsonAsync();
        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.Contains("internal address", r.Str("message"));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10)); // refused, not left to time out
    }

    [Fact]
    public async Task QG20_sso_and_any_other_named_client_use_the_outbound_guard()
    {
        var factory = app.Services.GetRequiredService<IHttpClientFactory>();
        var sso = factory.CreateClient("sso");
        foreach (var url in new[] { "http://169.254.169.254/.well-known/openid-configuration", "http://10.0.0.5/realm/.well-known/openid-configuration", "http://[::1]:9/" })
            Assert.Contains("internal address", (await Assert.ThrowsAsync<HttpRequestException>(() => sso.GetAsync(url))).Message);
        // the tests list 127.0.0.0/8 in Sso:AllowedNetworks for the fake identity provider; nothing listens on this port
        Assert.DoesNotContain("internal address", (await Assert.ThrowsAsync<HttpRequestException>(() => sso.GetAsync($"http://127.0.0.1:{ClosedPort()}/"))).Message);

        // a client registered without its own handler is guarded too
        var other = factory.CreateClient("not-registered-" + Guid.NewGuid().ToString("N")[..6]);
        Assert.Contains("internal address", (await Assert.ThrowsAsync<HttpRequestException>(() => other.GetAsync("http://127.0.0.1:9/"))).Message);

        var guard = new OutboundGuard(Config());
        var extra = OutboundGuard.Parse("10.10.0.0/16,169.254.0.0/16", "Sso:AllowedNetworks");
        Assert.True(guard.IsAllowed(IPAddress.Parse("10.10.4.4"), extra));
        Assert.False(guard.IsAllowed(IPAddress.Parse("10.11.4.4"), extra));
        Assert.False(guard.IsAllowed(IPAddress.Parse("169.254.169.254"), extra)); // metadata stays blocked even when listed here
        Assert.True(new OutboundGuard(Config(new() { ["Outbound:AllowedNetworks"] = "169.254.169.254" })).IsAllowed(IPAddress.Parse("169.254.169.254"), extra));
    }

    // ---------- QG-21: the app login can't create schemas; migrate uses a fixed search path ----------

    [Fact]
    public async Task QG21_the_app_login_cannot_create_schemas_and_the_job_tables_still_install()
    {
        var name = "qgiq_test_schemas_" + Guid.NewGuid().ToString("N")[..8];
        var role = "qgiq_app_" + Guid.NewGuid().ToString("N")[..8];
        const string pw = "app-login-throwaway-2";
        var cs = new NpgsqlConnectionStringBuilder(TestApp.ConnectionString) { Database = name }.ConnectionString;
        await using var factory = app.WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Default", cs));
        try
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.SetConnectionString(DatabaseRoles.ForMigrate(cs));
            await db.Database.MigrateAsync();
            await DatabaseRoles.ApplyAsync(db, role, pw, cs);
            // an installation set up before this fix had CREATE on the database; the next migrate takes it away
            var grant = $"GRANT CREATE ON DATABASE \"{name}\" TO \"{role}\""; // test-generated names, not user input
            await db.Database.ExecuteSqlRawAsync(grant);
            await DatabaseRoles.ApplyAsync(db, role, pw, cs);

            await using (var owner = new NpgsqlConnection(DatabaseRoles.ForMigrate(cs)))
            {
                await owner.OpenAsync();
                await using var show = new NpgsqlCommand("SHOW search_path", owner);
                Assert.Equal(DatabaseRoles.MigrateSearchPath, await show.ExecuteScalarAsync());
                await using var ext = new NpgsqlCommand("SELECT n.nspname FROM pg_extension e JOIN pg_namespace n ON n.oid = e.extnamespace WHERE e.extname = 'pg_trgm'", owner);
                Assert.Equal("public", await ext.ExecuteScalarAsync());
            }

            await using var appConn = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(cs) { Username = role, Password = pw, Pooling = false }.ConnectionString);
            await appConn.OpenAsync();
            async Task Run(string sql) { await using var c = new NpgsqlCommand(sql, appConn); await c.ExecuteNonQueryAsync(); }
            foreach (var sql in new[] { $"CREATE SCHEMA \"{new NpgsqlConnectionStringBuilder(cs).Username}\"", "CREATE SCHEMA evil", "CREATE TABLE public.shadow (id int)", "CREATE FUNCTION public.similarity(text, text) RETURNS real LANGUAGE sql AS 'SELECT 1::real'" })
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => Run(sql))).SqlState);
            // the background job library finds the jobs schema the migrate step made and creates its tables there
            Hangfire.PostgreSql.PostgreSqlObjectsInstaller.Install(appConn, "jobs");
            await using var count = new NpgsqlCommand("SELECT count(*) FROM jobs.job", appConn);
            Assert.Equal(0L, await count.ExecuteScalarAsync());
        }
        finally
        {
            using (var scope = factory.Services.CreateScope())
                await TestDatabases.DropAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());
            await using var conn = new NpgsqlConnection(TestApp.ConnectionString);
            await conn.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP ROLE IF EXISTS \"{role}\"", conn);
            await drop.ExecuteNonQueryAsync();
        }
    }

    // ---------- QG-22: stricter file signatures ----------

    [Theory]
    [InlineData(".pdf", "%PDF-1.7\n1 0 obj", true)]
    [InlineData(".pdf", "<!-- %PDF-1.7 --><html>", false)]
    [InlineData(".pdf", "  %PDF-1.7", false)]
    [InlineData(".txt", "MZ\u0090\u0000\u0003", false)]
    [InlineData(".txt", "\u007fELF\u0002\u0001", false)]
    [InlineData(".csv", "PK\u0003\u0004", false)]
    [InlineData(".json", "\u00cf\u00fa\u00ed\u00fe", false)]
    [InlineData(".xml", "%PDF-1.4", false)]
    [InlineData(".txt", "line one\u0000line two", false)]
    [InlineData(".txt", "\u00ff\u00feh\u0000i\u0000", true)]
    [InlineData(".txt", "MZ-1001 inspection notes", true)]
    [InlineData(".txt", "BZh91AY&SY", false)]
    [InlineData(".txt", "BZhang inspection notes", true)]
    [InlineData(".csv", "\u001f\u008b\u0008\u0000", false)]
    [InlineData(".txt", "Inspection notes for lot 42", true)]
    [InlineData(".csv", "part,qty\nA,1", true)]
    [InlineData(".json", "{\"a\":1}", true)]
    [InlineData(".xml", "<?xml version=\"1.0\"?><a/>", true)]
    [InlineData(".dwg", "AC1032\u0000\u0000", true)]
    [InlineData(".dwg", "MZ\u0090\u0000", false)]
    [InlineData(".dwg", "<html>", false)]
    [InlineData(".dxf", "  0\nSECTION", true)]
    [InlineData(".step", "ISO-10303-21;", true)]
    [InlineData(".stp", "MZ\u0090\u0000\u0003", false)]
    [InlineData(".iges", "\u007fELF", false)]
    public void QG22_signature_checks(string ext, string head, bool ok) => Assert.Equal(ok, FileSignatures.Matches(ext, Encoding.Latin1.GetBytes(head)));

    [Fact]
    public async Task QG22_a_program_named_txt_is_refused_on_upload()
    {
        var site = await app.CreateSiteAsync();
        var (_, editor) = await app.CreateUserAsync([QualGuardIQ.Domain.Security.Permissions.NcrView, QualGuardIQ.Domain.Security.Permissions.NcrEdit], site);
        var ncr = (await (await editor.PostOkAsync("/api/ncrs", new { title = "Binary as text", description = "x", source = "In-process", siteId = site, detectedOn = TestDates.Today, severity = 0 })).JsonAsync()).Id();
        async Task<HttpResponseMessage> Upload(string name, byte[] bytes)
        {
            var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(bytes), "file", name); form.Add(new StringContent("0"), "classification");
            return await editor.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/records/Ncr/{ncr}/files") { Content = form });
        }
        var exe = await Upload("readme.txt", [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]);
        Assert.Equal(HttpStatusCode.BadRequest, exe.StatusCode);
        Assert.Contains("isn't a real TXT file", await exe.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload("fake.pdf", Encoding.ASCII.GetBytes("<!-- %PDF-1.7 --><html></html>"))).StatusCode);
        Assert.True((await Upload("notes.txt", Encoding.UTF8.GetBytes("Sorted 200 pieces, 3 rejected"))).IsSuccessStatusCode);
    }

    // ---------- QG-23: an unreachable scanner shows up in health and on the admin notice ----------

    /// <summary>Minimal clamd stand-in that answers PING with PONG.</summary>
    static (TcpListener Listener, Task Loop) FakeClamd(CancellationToken ct)
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var loop = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                using var c = await l.AcceptTcpClientAsync(ct);
                var s = c.GetStream();
                var buf = new byte[64];
                var n = await s.ReadAsync(buf, ct);
                if (Encoding.ASCII.GetString(buf, 0, n).StartsWith("zPING")) await s.WriteAsync("PONG\0"u8.ToArray(), ct);
            }
        }, ct);
        return (l, loop);
    }

    [Fact]
    public async Task QG23_the_health_check_is_degraded_when_clamav_does_not_answer()
    {
        var closed = new ClamAvScanner(Config(new() { ["MalwareScanning:ClamAV:Host"] = "127.0.0.1", ["MalwareScanning:ClamAV:Port"] = ClosedPort().ToString() }), NullLogger<ClamAvScanner>.Instance);
        Assert.False(await closed.IsReachableAsync(CancellationToken.None));
        var down = await new MalwareScannerHealthCheck(closed, Config()).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Degraded, down.Status);
        Assert.Contains("refused", down.Description);
        Assert.Contains("without being scanned", (await new MalwareScannerHealthCheck(closed, Config(new() { ["MalwareScanning:AllowWhenUnavailable"] = "true" })).CheckHealthAsync(new HealthCheckContext())).Description);

        using var cts = new CancellationTokenSource();
        var (listener, loop) = FakeClamd(cts.Token);
        try
        {
            var up = new ClamAvScanner(Config(new() { ["MalwareScanning:ClamAV:Host"] = "127.0.0.1", ["MalwareScanning:ClamAV:Port"] = ((IPEndPoint)listener.LocalEndpoint).Port.ToString() }), NullLogger<ClamAvScanner>.Instance);
            Assert.True(await up.IsReachableAsync(CancellationToken.None));
            Assert.Equal(HealthStatus.Healthy, (await new MalwareScannerHealthCheck(up, Config()).CheckHealthAsync(new HealthCheckContext())).Status);
        }
        finally { cts.Cancel(); listener.Stop(); try { await loop; } catch (Exception) { } }

        Assert.Equal(HealthStatus.Healthy, (await new MalwareScannerHealthCheck(new NoMalwareScanner(), Config()).CheckHealthAsync(new HealthCheckContext())).Status);
    }

    [Fact]
    public async Task QG23_admins_are_told_when_scanning_is_on_but_the_scanner_is_unreachable()
    {
        await using var factory = app.WithWebHostBuilder(b =>
        {
            b.UseSetting("MalwareScanning:Provider", "ClamAV");
            b.UseSetting("MalwareScanning:ClamAV:Host", "127.0.0.1");
            b.UseSetting("MalwareScanning:ClamAV:Port", ClosedPort().ToString());
        });
        var http = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var ready = await http.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode); // degraded, the API keeps serving
        Assert.Equal("Degraded", await ready.Content.ReadAsStringAsync());

        var admin = new ApiClient(http);
        await admin.RefreshCsrfAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync("/api/auth/login", new { email = TestApp.AdminEmail, password = TestApp.AdminPassword })).StatusCode);
        await admin.RefreshCsrfAsync();
        var s = await admin.GetElAsync("/api/admin/upload-scanning");
        Assert.True(s.GetProperty("enabled").GetBoolean());
        Assert.False(s.GetProperty("reachable").GetBoolean());
        Assert.Contains("can't be reached", s.Str("message"));

        var normal = await (await app.AdminAsync()).GetElAsync("/api/admin/upload-scanning");
        Assert.Equal(JsonValueKind.Null, normal.GetProperty("reachable").ValueKind);
        Assert.Equal(JsonValueKind.Null, normal.GetProperty("message").ValueKind);
    }

    // ---------- skipped migrate step: readiness says so instead of failing every signed-in request ----------

    [Fact]
    public async Task Readiness_is_unhealthy_while_the_database_has_migrations_that_were_not_applied()
    {
        var name = "qgiq_test_pending_" + Guid.NewGuid().ToString("N")[..8];
        await using (var conn = new NpgsqlConnection(TestApp.ConnectionString))
        {
            await conn.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", conn);
            await create.ExecuteNonQueryAsync();
        }
        var cs = new NpgsqlConnectionStringBuilder(TestApp.ConnectionString) { Database = name }.ConnectionString;
        await using var factory = app.WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Default", cs));
        try
        {
            var http = factory.CreateClient();
            var ready = await http.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
            Assert.Equal("Unhealthy", await ready.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health/live")).StatusCode);
            using (var scope = factory.Services.CreateScope())
            {
                var check = ActivatorUtilities.CreateInstance<DatabaseHealthCheck>(scope.ServiceProvider);
                var r = await check.CheckHealthAsync(new HealthCheckContext());
                Assert.Equal(HealthStatus.Unhealthy, r.Status);
                Assert.StartsWith(DatabaseHealthCheck.OutOfDate, r.Description);
            }
            // the shared test database is up to date
            Assert.Equal(HttpStatusCode.OK, (await app.CreateClient().GetAsync("/health/ready")).StatusCode);
        }
        finally
        {
            using var scope = factory.Services.CreateScope();
            await TestDatabases.DropAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }
    }

    // ---------- QG-18: backups include the key ring by default ----------

    [Fact]
    public void QG18_backups_save_the_keys_by_default_and_the_laptop_guide_explains_how()
    {
        var script = File.ReadAllText(Repo("scripts", "backup.sh"));
        Assert.Contains("QGIQ_SKIP_KEYS", script);
        Assert.Contains("label=com.docker.compose.service=api", script);
        Assert.DoesNotContain("the Data Protection keys were not backed up (set QGIQ_KEYS_CONTAINER", script);

        var compose = File.ReadAllText(Repo("docker-compose.yml")).Replace("\r\n", "\n");
        var backup = Regex.Match(compose, @"\n  backup:\n(?<body>(    .*\n|\s*\n)+?)  \w").Groups["body"].Value;
        Assert.Contains("profiles: [backup]", backup);
        Assert.Contains("dpkeys:/keys:ro", backup);
        Assert.Contains("QGIQ_KEYS_DIR: /keys", backup);
        Assert.Contains("/scripts/backup.sh", backup);

        var guide = File.ReadAllText(Repo("docs", "guides", "laptop-setup.md")).Replace("\r\n", "\n");
        var section = guide[guide.IndexOf("## Backing up", StringComparison.Ordinal)..guide.IndexOf("## Troubleshooting", StringComparison.Ordinal)];
        Assert.Contains("docker compose -f C:\\QualGuard-IQ\\docker-compose.yml run --rm backup", section);
        var blocks = Regex.Matches(section, "```powershell\n(?<cmd>[\\s\\S]*?)```").Select(m => m.Groups["cmd"].Value.Trim()).ToList();
        Assert.NotEmpty(blocks);
        Assert.All(blocks, b => { Assert.DoesNotContain('\n', b); Assert.DoesNotMatch(@"(^|\s)(cd|Set-Location)\s", b); });
    }

    // ---------- QG-15: every image is pinned by digest ----------

    [Fact]
    public void QG15_every_container_image_is_pinned_by_digest()
    {
        var lines = new[] { "docker-compose.yml", ".github/workflows/ci.yml" }.SelectMany(f => File.ReadAllLines(Repo(f.Split('/'))).Where(l => l.TrimStart().StartsWith("image:")))
            .Concat(Directory.GetFiles(Repo("deploy"), "Dockerfile.*").SelectMany(File.ReadAllLines).Where(l => l.StartsWith("FROM ")))
            .ToList();
        Assert.True(lines.Count >= 10, string.Join("\n", lines));
        Assert.All(lines, l => Assert.Matches(@"[\w.\-/]+:[\w.\-]+@sha256:[0-9a-f]{64}", l));
        Assert.DoesNotContain(lines, l => l.Contains(":stable") || l.Contains("sdk:10.0@") || l.Contains("aspnet:10.0@") || l.Contains("postgres:18@"));
        Assert.Contains(lines, l => l.Contains("clamav/clamav:") && l.Contains("-debian@")); // the Debian build has an arm64 image
    }
}
