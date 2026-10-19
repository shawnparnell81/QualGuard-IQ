using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure;
using QualGuardIQ.Infrastructure.Identity;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Security;
using QualGuardIQ.Infrastructure.Services;
using QualGuardIQ.Infrastructure.Storage;
using static QualGuardIQ.Tests.TestDates;

namespace QualGuardIQ.Tests;

/// <summary>Fixes from the October 2026 security review (QG-1 to QG-19), one or more checks per finding.</summary>
[Collection("api")]
public sealed class SecurityHardeningTests(TestApp app)
{
    const string Pw = "long-enough-password";

    async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> q)
    {
        using var scope = app.Services.CreateScope();
        return await q(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Turns on two-step verification for a signed-in user; returns the authenticator key and recovery codes.</summary>
    static async Task<(string Key, List<string> Codes)> EnrolAsync(ApiClient c)
    {
        var key = (await (await c.PostOkAsync("/api/account/mfa/setup", new { password = Pw })).JsonAsync()).Str("key");
        var en = await (await c.PostOkAsync("/api/account/mfa/enable", new { code = TestTotp.Now(key, -1) })).JsonAsync();
        await c.RefreshCsrfAsync();
        return (key, en.GetProperty("recoveryCodes").EnumerateArray().Select(x => x.GetString()!).ToList());
    }

    async Task<(Guid DocId, string Number)> SubmittedDocumentAsync(ApiClient author, Guid site, string title)
    {
        var docId = await author.CreateDocumentAsync(site, title: title);
        var doc = await author.GetDocumentAsync(docId);
        (await author.UploadAsync("DocumentRevision", doc.Revisions[0].Id, "wi.pdf", "%PDF-1.4 work instruction")).EnsureSuccessStatusCode();
        await author.PostOkAsync($"/api/documents/revisions/{doc.Revisions[0].Id}/submit");
        return (docId, doc.Number);
    }

    // ---------- QG-1: e-signatures for accounts with two-step verification ----------

    [Fact]
    public async Task QG1_people_with_two_step_verification_sign_approvals_and_training_with_their_authenticator_code()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync([Permissions.DocumentsView, Permissions.DocumentsEdit, Permissions.TrainingView, Permissions.TrainingEdit], site);
        var (signerId, signer) = await app.CreateUserAsync([Permissions.DocumentsView, Permissions.DocumentsApprove], site);
        var (key, recovery) = await EnrolAsync(signer);

        var (docId, number) = await SubmittedDocumentAsync(author, site, "Signed with a code " + Guid.NewGuid().ToString("N")[..6]);
        var approval = await signer.PendingApprovalIdAsync(number);
        Assert.Equal(HttpStatusCode.Unauthorized, (await signer.PostAsync($"/api/my/approvals/{approval}/sign", new { approve = true, password = Pw, comment = (string?)null })).StatusCode);
        Assert.False((await signer.PostAsync($"/api/my/approvals/{approval}/sign", new { approve = true, password = Pw, comment = (string?)null, mfaCode = recovery[0] })).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await signer.PostAsync($"/api/my/approvals/{approval}/sign", new { approve = true, password = Pw, comment = (string?)null, mfaCode = "000000" })).StatusCode);
        (await signer.PostAsync($"/api/my/approvals/{approval}/sign", new { approve = true, password = Pw, comment = (string?)null, mfaCode = TestTotp.Now(key, 0) })).EnsureSuccessStatusCode();
        await (await app.AdminAsync()).PostOkAsync($"/api/admin/users/{signerId}/unlock");

        var course = (await (await author.PostOkAsync("/api/training/courses", new { title = "Signed WI", description = "Read", siteId = site, departmentId = (Guid?)null, documentId = docId, retrainOnRevision = false, validityMonths = 24, dueDays = 14 })).JsonAsync()).Id();
        await author.PostOkAsync($"/api/training/courses/{course}/assign", new { userIds = new[] { signerId }, dueOn = (DateOnly?)null });
        var assignment = (await signer.GetElAsync("/api/training/my"))[0].Id();
        Assert.Equal(HttpStatusCode.Unauthorized, (await signer.PostAsync($"/api/training/assignments/{assignment}/complete", new { password = Pw, notes = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await signer.PostAsync($"/api/training/assignments/{assignment}/complete", new { password = Pw, notes = "Read", mfaCode = TestTotp.Now(key, 1) })).StatusCode);
        await (await app.AdminAsync()).PostOkAsync($"/api/admin/users/{signerId}/unlock");
    }

    // ---------- QG-5: wrong signature passwords count toward the lockout and are logged ----------

    [Fact]
    public async Task QG5_wrong_signature_passwords_lock_the_account_and_are_logged()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync([Permissions.DocumentsView, Permissions.DocumentsEdit], site);
        var (signerId, signer) = await app.CreateUserAsync([Permissions.DocumentsView, Permissions.DocumentsApprove], site);
        var (_, number) = await SubmittedDocumentAsync(author, site, "Lockout " + Guid.NewGuid().ToString("N")[..6]);
        var approval = await signer.PendingApprovalIdAsync(number);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await signer.SignAsync(approval, password: "wrong-password-" + i)).StatusCode);
        Assert.Equal((HttpStatusCode)423, (await signer.SignAsync(approval)).StatusCode);
        Assert.True(await DbAsync(db => db.SecurityEvents.AnyAsync(e => e.UserId == signerId && e.EventType == "SignatureFailed")));
        await (await app.AdminAsync()).PostOkAsync($"/api/admin/users/{signerId}/unlock");
    }

    // ---------- QG-3: sign-out ends the session on the server; sessions have a maximum length ----------

    sealed class RawSession(HttpClient http)
    {
        public readonly Dictionary<string, string> Cookies = new();
        string? _csrf;
        public async Task<HttpResponseMessage> SendAsync(HttpMethod m, string url, object? body = null, Dictionary<string, string>? cookies = null)
        {
            var req = new HttpRequestMessage(m, url);
            if (body is not null) req.Content = JsonContent.Create(body);
            if (_csrf is not null) req.Headers.Add("X-CSRF-TOKEN", _csrf);
            var jar = cookies ?? Cookies;
            if (jar.Count > 0) req.Headers.Add("Cookie", string.Join("; ", jar.Select(kv => $"{kv.Key}={kv.Value}")));
            var r = await http.SendAsync(req);
            if (cookies is null && r.Headers.TryGetValues("Set-Cookie", out var set))
                foreach (var c in set) { var kv = c.Split(';')[0]; var i = kv.IndexOf('='); Cookies[kv[..i]] = kv[(i + 1)..]; }
            return r;
        }
        public async Task CsrfAsync() => _csrf = (await (await SendAsync(HttpMethod.Get, "/api/auth/csrf")).Content.ReadFromJsonAsync<JsonElement>()).Str("token");
    }

    [Fact]
    public async Task QG3_a_copied_session_cookie_stops_working_after_sign_out()
    {
        var (userId, _) = await app.CreateUserAsync([Permissions.NcrView]);
        var email = (await (await app.AdminAsync()).GetJsonAsync<List<UserDto>>("/api/admin/users")).Single(u => u.Id == userId).Email;
        var s = new RawSession(app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false }));
        await s.CsrfAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await s.SendAsync(HttpMethod.Post, "/api/auth/login", new { email, password = Pw })).StatusCode);
        await s.CsrfAsync();
        Assert.Equal(HttpStatusCode.OK, (await s.SendAsync(HttpMethod.Get, "/api/auth/me")).StatusCode);
        var copied = new Dictionary<string, string>(s.Cookies);
        var other = await app.SignInAsync(email, Pw); // a second session of the same person
        Assert.True((await s.SendAsync(HttpMethod.Post, "/api/auth/logout")).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.SendAsync(HttpMethod.Get, "/api/auth/me", cookies: copied)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task QG3_sessions_end_after_the_maximum_length_and_old_style_sessions_are_refused()
    {
        using var scope = app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionRevocation>();
        ClaimsPrincipal P(params Claim[] c) => new(new ClaimsIdentity(c, "test"));
        var now = DateTimeOffset.UtcNow;
        Claim Iat(DateTimeOffset t) => new(SessionClaims.IssuedAt, t.ToUnixTimeSeconds().ToString());
        var sid = new Claim(SessionClaims.SessionId, Guid.NewGuid().ToString("N"));
        Assert.Null(await sessions.RejectReasonAsync(P(sid, Iat(now.AddMinutes(-5)))));
        Assert.NotNull(await sessions.RejectReasonAsync(P(sid, Iat(now - sessions.AbsoluteLifetime - TimeSpan.FromMinutes(1)))));
        Assert.NotNull(await sessions.RejectReasonAsync(P(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()))));
    }

    // ---------- QG-4: only known proxies may set the client address ----------

    [Fact]
    public void QG4_forwarded_headers_trust_only_configured_proxies_and_one_hop()
    {
        var o = new ForwardedHeadersOptions();
        o.KnownProxies.Clear(); o.KnownIPNetworks.Clear();
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ForwardedHeaders:KnownNetworks"] = "172.16.0.0/12, 10.0.0.0/8", ["ForwardedHeaders:KnownProxies"] = "192.0.2.10" }).Build();
        QualGuardIQ.Api.Infrastructure.ProxySettings.Apply(o, cfg);
        Assert.Equal(1, o.ForwardLimit);
        Assert.Equal(2, o.KnownIPNetworks.Count);
        Assert.Contains(IPAddress.Parse("192.0.2.10"), o.KnownProxies);
        Assert.Throws<InvalidOperationException>(() => QualGuardIQ.Api.Infrastructure.ProxySettings.Apply(new ForwardedHeadersOptions(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ForwardedHeaders:KnownNetworks"] = "not-a-range" }).Build()));
    }

    // ---------- QG-6 and QG-14: fresh install, limited app login, add-only audit tables, administrators need two-step ----------

    [Fact]
    public async Task QG6_QG14_fresh_install_uses_a_limited_app_login_and_requires_two_step_verification_for_administrators()
    {
        var name = "qgiq_test_roles_" + Guid.NewGuid().ToString("N")[..8];
        var role = "qgiq_app_" + Guid.NewGuid().ToString("N")[..8];
        var cs = new NpgsqlConnectionStringBuilder(TestApp.ConnectionString) { Database = name }.ConnectionString;
        await using var factory = app.WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Default", cs);
            b.UseSetting("Seed:AdministratorRequiresMfa", "true");
        });
        try
        {
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.MigrateAsync();
                await scope.ServiceProvider.GetRequiredService<Seeder>().SeedAsync();
                Assert.True(await db.QmsRoles.Where(r => r.IsSystem && r.Name == "Administrator").Select(r => r.RequiresMfa).SingleAsync());

                var ownerPassword = new NpgsqlConnectionStringBuilder(cs).Password!;
                await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseRoles.ApplyAsync(db, role, ownerPassword, cs));
                await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseRoles.ApplyAsync(db, role, "short", cs));
                await DatabaseRoles.ApplyAsync(db, role, "app-login-throwaway-1", cs);
                await DatabaseRoles.ApplyAsync(db, role, "app-login-throwaway-1", cs); // safe to repeat on every start

                await using (var appConn = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(cs) { Username = role, Password = "app-login-throwaway-1", Pooling = false }.ConnectionString))
                {
                    await appConn.OpenAsync();
                    async Task<int> Run(string sql) { await using var c = new NpgsqlCommand(sql, appConn); return await c.ExecuteNonQueryAsync(); }
                    Assert.Equal(1, await Run("""INSERT INTO qms.audit_entries ("OccurredAt","UserName","Action","EntityType","EntityId","Description") VALUES (now(), 'Test', 'Created', 'Test', gen_random_uuid(), 'Created test entry')"""));
                    await Run("""UPDATE qms.organization_settings SET "UpdatedAt" = "UpdatedAt" """); // ordinary data can be changed
                    foreach (var sql in new[] { """UPDATE qms.audit_entries SET "Description" = 'edited'""", "DELETE FROM qms.audit_entries", "TRUNCATE qms.security_events",
                                 "CREATE TABLE qms.not_allowed (id int)", "DROP TABLE qms.ncrs", """DELETE FROM qms."__ef_migrations" """,
                                 "CREATE SCHEMA not_allowed" })
                        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => Run(sql))).SqlState);
                    await Run("CREATE TABLE IF NOT EXISTS jobs.job_probe (id int)"); // the job library manages its own tables
                }
                // the owner can't rewrite the audit trail either
                foreach (var sql in new[] { """UPDATE qms.audit_entries SET "Description" = 'edited'""", "DELETE FROM qms.audit_entries", "TRUNCATE qms.security_events" })
                    Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql))).SqlState);
            }

            var c = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
            var csrf = (await c.GetFromJsonAsync<JsonElement>("/api/auth/csrf")).Str("token");
            var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { email = TestApp.AdminEmail, password = TestApp.AdminPassword }) };
            login.Headers.Add("X-CSRF-TOKEN", csrf);
            var step = await (await c.SendAsync(login)).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("enrol", step.Str("next"));
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

    // ---------- QG-7 and QG-8: plain-language audit entries, audited tags, history with comments ----------

    [Fact]
    public async Task QG7_QG8_audit_entries_read_as_plain_sentences_and_tags_and_comments_show_in_history()
    {
        var site = await app.CreateSiteAsync();
        var (_, editor) = await app.CreateUserAsync([Permissions.NcrView, Permissions.NcrEdit], site);
        var title = "Plain audit " + Guid.NewGuid().ToString("N")[..6];
        var ncr = (await (await editor.PostOkAsync("/api/ncrs", new { title, description = "Burr on edge", source = "In-process", siteId = site, detectedOn = Today, severity = 0 })).JsonAsync()).Id();
        await editor.PostOkAsync($"/api/records/Ncr/{ncr}/tags", new { name = "Line 7" });
        Assert.Equal(HttpStatusCode.NoContent, (await editor.DeleteAsync($"/api/records/Ncr/{ncr}/tags/line%207")).StatusCode);
        await editor.PostOkAsync($"/api/records/Ncr/{ncr}/comments", new { body = "Containment started" });

        var history = await editor.GetElAsync($"/api/records/Ncr/{ncr}/history");
        var lines = history.EnumerateArray().Select(h => (Action: h.Str("action"), Text: h.Str("description"), Who: h.Str("userName"))).ToList();
        Assert.Contains(lines, l => l.Action == "TagAdded" && l.Text.Contains("\"Line 7\""));
        Assert.Contains(lines, l => l.Action == "TagRemoved" && l.Text.Contains("\"Line 7\""));
        Assert.Contains(lines, l => l.Action == "Created" && l.Text.StartsWith("Created comment"));
        Assert.All(lines, l => Assert.False(string.IsNullOrWhiteSpace(l.Who)));
        Assert.All(lines, l => Assert.DoesNotMatch("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", l.Text));
    }

    [Fact]
    public async Task QG8_role_permission_changes_are_audited_in_the_same_save()
    {
        var admin = await app.AdminAsync();
        var name = "Audited role " + Guid.NewGuid().ToString("N")[..6];
        var roleId = await (await admin.PostOkAsync("/api/admin/roles", new { name, description = (string?)null, permissions = new[] { Permissions.NcrView } })).Content.ReadFromJsonAsync<Guid>();
        var role = (await admin.GetElAsync("/api/admin/roles")).EnumerateArray().Single(r => r.Id() == roleId);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"/api/admin/roles/{roleId}", new { name, description = (string?)null, permissions = new[] { Permissions.NcrView, Permissions.NcrEdit }, rowVersion = role.GetProperty("rowVersion").GetUInt32() })).StatusCode);
        var page = await admin.GetJsonAsync<PageDto<AuditDto>>($"/api/audit-trail?entityId={roleId}&pageSize=50");
        Assert.True(page.Items.Count(a => a.Action == "PermissionsChanged") >= 2, string.Join(" | ", page.Items.Select(a => a.Action + ": " + a.Description)));
        Assert.Contains(page.Items, a => a.Description.Contains(Permissions.NcrEdit));
    }

    // ---------- QG-9: administrators see when upload scanning is off ----------

    [Fact]
    public async Task QG9_admins_can_see_whether_upload_scanning_is_on()
    {
        var admin = await app.AdminAsync();
        var s = await admin.GetElAsync("/api/admin/upload-scanning");
        Assert.False(s.GetProperty("enabled").GetBoolean());
        Assert.Equal("None", s.Str("provider"));
        var (_, user) = await app.CreateUserAsync([Permissions.NcrView]);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/admin/upload-scanning")).StatusCode);
    }

    // ---------- QG-10, QG-15, QG-17: deployment files ----------

    static string Repo(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docker-compose.yml"))) dir = dir.Parent;
        return Path.Combine([dir?.FullName ?? throw new InvalidOperationException("Repository root not found"), .. parts]);
    }

    [Fact]
    public void QG10_QG15_QG17_compose_keeps_localhost_adds_optional_https_and_never_hands_the_app_owner_or_root_keys()
    {
        var compose = File.ReadAllText(Repo("docker-compose.yml"));
        Assert.Contains("\"${WEB_PORT:-8081}:80\"", compose);                 // http://localhost:8081 still works
        Assert.Contains("profiles: [https]", compose);                         // HTTPS front end is opt-in
        Assert.Contains("Username=qgiq_app", compose);                         // API and worker use the limited login
        Assert.Contains("command: [\"migrate\"]", compose);
        Assert.DoesNotContain("Storage__AccessKey: ${STORAGE_ACCESS_KEY:-${MINIO_ROOT_USER", compose);
        Assert.DoesNotContain("MigrateOnStartup", compose);
        var caddy = File.ReadAllText(Repo("deploy", "Caddyfile"));
        Assert.Contains("Strict-Transport-Security", caddy);
        Assert.Contains("{$QG_DOMAIN}", caddy);
        var web = File.ReadAllText(Repo("deploy", "Dockerfile.web"));
        Assert.DoesNotContain("node:20", web);
        Assert.Matches(@"FROM nginx:1\.(2[89]|[3-9]\d)\.\d+-alpine@sha256:", web);
        var ci = File.ReadAllText(Repo(".github", "workflows", "ci.yml"));
        Assert.Contains("permissions:\n  contents: read", ci.Replace("\r\n", "\n"));
        Assert.DoesNotMatch(@"uses: [\w\-/]+@v\d", ci);
        var init = File.ReadAllText(Repo("deploy", "minio-init.sh"));
        Assert.Contains("arn:aws:s3:::$BUCKET/*", init);
        Assert.Contains("admin user add", init);
    }

    // ---------- QG-11: file types come from the extension and must match the bytes ----------

    static async Task<HttpResponseMessage> UploadBytesAsync(ApiClient c, string url, string name, byte[] bytes, string contentType)
    {
        var form = new MultipartFormDataContent();
        var f = new ByteArrayContent(bytes); f.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(f, "file", name); form.Add(new StringContent("0"), "classification");
        return await c.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, url) { Content = form });
    }

    [Fact]
    public async Task QG11_uploads_must_match_their_extension_and_are_stored_with_the_type_for_that_extension()
    {
        var site = await app.CreateSiteAsync();
        var (_, editor) = await app.CreateUserAsync([Permissions.NcrView, Permissions.NcrEdit], site);
        var ncr = (await (await editor.PostOkAsync("/api/ncrs", new { title = "Upload types", description = "x", source = "In-process", siteId = site, detectedOn = Today, severity = 0 })).JsonAsync()).Id();
        var url = $"/api/records/Ncr/{ncr}/files";
        var html = Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>");
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadBytesAsync(editor, url, "evil.pdf", html, "text/html")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadBytesAsync(editor, url, "photo.png", html, "image/png")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadBytesAsync(editor, url, "report.docx", html, "application/octet-stream")).StatusCode);
        var pdf = await (await UploadBytesAsync(editor, url, "real.pdf", Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj"), "text/html")).JsonAsync();
        Assert.Equal("application/pdf", pdf.Str("contentType"));
        var png = await (await UploadBytesAsync(editor, url, "real.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D], "text/html")).JsonAsync();
        Assert.Equal("image/png", png.Str("contentType"));
        var txt = await (await UploadBytesAsync(editor, url, "notes.txt", html, "text/html")).JsonAsync();
        Assert.Equal("text/plain", txt.Str("contentType"));
    }

    [Theory]
    [InlineData(".pdf", "%PDF-1.4", true)]
    [InlineData(".pdf", "<html>", false)]
    [InlineData(".gif", "GIF89a....", true)]
    [InlineData(".zip", "PK\u0003\u0004rest", true)]
    [InlineData(".xlsx", "MZ", false)]
    [InlineData(".jpg", "GIF89a", false)]
    [InlineData(".csv", "a,b,c", true)]
    public void QG11_signature_checks(string ext, string head, bool ok) => Assert.Equal(ok, FileSignatures.Matches(ext, Encoding.Latin1.GetBytes(head)));

    // ---------- QG-12: sign-in answers don't reveal which accounts exist ----------

    [Fact]
    public async Task QG12_unknown_accounts_and_locked_accounts_with_a_wrong_password_get_the_same_answer()
    {
        var (userId, _) = await app.CreateUserAsync([Permissions.NcrView]);
        var admin = await app.AdminAsync();
        var email = (await admin.GetJsonAsync<List<UserDto>>("/api/admin/users")).Single(u => u.Id == userId).Email;
        var c = await app.AnonymousAsync();
        var unknown = await c.PostAsync("/api/auth/login", new { email = $"nobody{Guid.NewGuid():N}@test.local", password = "wrong-password-xx" });
        var wrong = await c.PostAsync("/api/auth/login", new { email, password = "wrong-password-xx" });
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await wrong.Content.ReadAsStringAsync());
        for (var i = 0; i < 5; i++) await c.PostAsync("/api/auth/login", new { email, password = "wrong-password-xx" });
        var lockedWrong = await c.PostAsync("/api/auth/login", new { email, password = "wrong-password-xx" });
        Assert.Equal(HttpStatusCode.Unauthorized, lockedWrong.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await lockedWrong.Content.ReadAsStringAsync());
        Assert.Equal((HttpStatusCode)423, (await c.PostAsync("/api/auth/login", new { email, password = Pw })).StatusCode);
        await admin.PostOkAsync($"/api/admin/users/{userId}/unlock");
    }

    // ---------- QG-13: outbound calls can't reach internal addresses ----------

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.20.0.5", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    public void QG13_internal_addresses_are_refused_by_default(string ip, bool allowed) =>
        Assert.Equal(allowed, new OutboundGuard(new ConfigurationBuilder().Build()).IsAllowed(IPAddress.Parse(ip)));

    [Fact]
    public async Task QG13_allow_list_and_printer_networks_are_configurable_and_connections_are_checked()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Outbound:AllowedNetworks"] = "10.20.0.0/16", ["Outbound:PrinterNetworks"] = "192.168.50.0/24" }).Build();
        var guard = new OutboundGuard(cfg);
        Assert.True(guard.IsAllowed(IPAddress.Parse("10.20.1.1")));
        Assert.False(guard.IsAllowed(IPAddress.Parse("10.21.1.1")));
        Assert.True(guard.IsPrinterAllowed(IPAddress.Parse("192.168.50.7")));
        Assert.False(guard.IsPrinterAllowed(IPAddress.Parse("192.168.51.7")));
        Assert.False(new OutboundGuard(new ConfigurationBuilder().Build()).IsPrinterAllowed(IPAddress.Parse("127.0.0.1")));
        Assert.False(new OutboundGuard(new ConfigurationBuilder().Build()).IsPrinterAllowed(IPAddress.Parse("169.254.169.254")));
        Assert.True(new OutboundGuard(new ConfigurationBuilder().Build()).IsPrinterAllowed(IPAddress.Parse("192.168.1.40")));
        Assert.Throws<InvalidOperationException>(() => new OutboundGuard(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Outbound:AllowedNetworks"] = "nope" }).Build()));

        using var http = new HttpClient(app.Services.GetRequiredService<OutboundGuard>().Handler());
        var e = await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync("http://127.0.0.1:9/"));
        Assert.Contains("internal address", e.Message);
        e = await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync("http://localhost:9/"));
        Assert.Contains("internal address", e.Message);
    }

    // ---------- QG-16: portal uploads respect the record lock; portal downloads are verified ----------

    [Fact]
    public async Task QG16_portal_users_cannot_add_files_to_locked_records_and_damaged_files_are_not_served()
    {
        var admin = await app.AdminAsync();
        var site = await app.CreateSiteAsync();
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpper();
        var supplier = (await (await admin.PostOkAsync("/api/suppliers", new { code = "L" + tag, legalName = $"Lock Castings {tag}", category = "Castings", criticality = 1, risk = 1, siteId = site })).JsonAsync()).Id();
        var ncr = (await (await admin.PostOkAsync("/api/ncrs", new { title = "Locked " + tag, description = "Porosity", source = "Supplier", siteId = site, detectedOn = Today, severity = 1, details = new { supplierId = supplier } })).JsonAsync()).Id();
        await admin.PostOkAsync("/api/portal/admin/accounts", new { email = $"lock{tag}@sup.test", displayName = "Lee", kind = "Supplier", partyId = supplier, password = Pw, mustChangePassword = false });
        var portal = await app.SignInAsync($"lock{tag}@sup.test", Pw);

        var up = await (await UploadBytesAsync(portal, $"/api/portal/ncrs/{ncr}/evidence", "sort.txt", "sorted 200"u8.ToArray(), "text/plain")).JsonAsync();
        Assert.Equal(HttpStatusCode.OK, (await portal.GetAsync($"/api/portal/evidence/{up.Id()}")).StatusCode);

        // the stored copy no longer matches the checksum taken at upload
        var key = await DbAsync(db => db.Attachments.Where(a => a.Id == up.Id()).Select(a => a.StorageKey).SingleAsync());
        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IFileStorage>().PutAsync(key, new MemoryStream("tampered"u8.ToArray()), "text/plain", CancellationToken.None);
        Assert.Equal(HttpStatusCode.InternalServerError, (await portal.GetAsync($"/api/portal/evidence/{up.Id()}")).StatusCode);

        await DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE qms.ncrs SET \"Status\" = 4 WHERE \"Id\" = {ncr}"));
        var before = await DbAsync(db => db.Attachments.CountAsync(a => a.OwnerId == ncr));
        Assert.False((await UploadBytesAsync(portal, $"/api/portal/ncrs/{ncr}/evidence", "late.txt", "late"u8.ToArray(), "text/plain")).IsSuccessStatusCode);
        Assert.Equal(before, await DbAsync(db => db.Attachments.CountAsync(a => a.OwnerId == ncr)));
    }

    // ---------- QG-18: key ring encrypted with a certificate when one is configured ----------

    [Fact]
    public void QG18_key_ring_can_be_encrypted_with_a_certificate()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var req = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=qgiq-test-keys", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var dir = Directory.CreateTempSubdirectory("qgiq-keys-");
        var pfx = Path.Combine(dir.FullName, "keys.pfx");
        File.WriteAllBytes(pfx, cert.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx, "test-only"));
        var keys = Directory.CreateDirectory(Path.Combine(dir.FullName, "ring"));
        try
        {
            IServiceProvider Build() => new ServiceCollection().AddLogging().AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = TestApp.ConnectionString, ["DataProtection:KeyPath"] = keys.FullName,
                ["DataProtection:CertificatePath"] = pfx, ["DataProtection:CertificatePassword"] = "test-only"
            }).Build()).BuildServiceProvider();
            var secret = Build().GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>().CreateProtector("t").Protect("kept secret");
            var xml = File.ReadAllText(Directory.GetFiles(keys.FullName, "*.xml").Single());
            Assert.Contains("EncryptedData", xml);
            Assert.DoesNotContain("<masterKey", xml);
            Assert.Equal("kept secret", Build().GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>().CreateProtector("t").Unprotect(secret));
            Assert.Throws<InvalidOperationException>(() => DataProtectionCertificate.Load(Path.Combine(dir.FullName, "missing.pfx"), null));
        }
        finally { dir.Delete(true); }
    }

    // ---------- QG-19: webhooks need organization-wide platform administration ----------

    [Fact]
    public async Task QG19_site_scoped_platform_admins_cannot_manage_webhooks()
    {
        // administration roles can only be assigned organization-wide today; a site-limited grant (older data, or a later change to
        // that rule) must still not reach webhooks, which carry every site's events
        var admin = await app.AdminAsync();
        var (userId, _) = await app.CreateUserAsync([Permissions.NcrView]);
        var roleId = await (await admin.PostOkAsync("/api/admin/roles", new { name = "Site platform " + Guid.NewGuid().ToString("N")[..6], description = (string?)null, permissions = new[] { Permissions.PlatformAdmin } })).Content.ReadFromJsonAsync<Guid>();
        var site = await app.CreateSiteAsync();
        await DbAsync(async db => { db.UserRoleAssignments.Add(new UserRoleAssignment { UserId = userId, RoleId = roleId, SiteId = site }); return await db.SaveChangesAsync(); });
        var email = (await admin.GetJsonAsync<List<UserDto>>("/api/admin/users")).Single(u => u.Id == userId).Email;
        var siteAdmin = await app.SignInAsync(email, Pw);
        Assert.Equal(HttpStatusCode.Forbidden, (await siteAdmin.GetAsync("/api/platform/webhooks")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await siteAdmin.PostAsync("/api/platform/webhooks", new { name = "All events", url = "https://example.com/hook", events = new[] { "record.created" }, recordTypes = (string[]?)null })).StatusCode);
        var (_, orgAdmin) = await app.CreateUserAsync([Permissions.PlatformAdmin]);
        Assert.Equal(HttpStatusCode.OK, (await orgAdmin.GetAsync("/api/platform/webhooks")).StatusCode);
    }
}
