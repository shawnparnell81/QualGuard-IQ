using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Tests;

/// <summary>Runs the real API against a local test database that is rebuilt once per test run.</summary>
public sealed class TestApp : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "correct-horse-battery";

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("QMS_TEST_DB") ?? "Host=localhost;Port=5433;Database=qgiq_test;Username=qgiq;Password=qgiq_local_dev";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Seed:ApplicationName", "QualGuard IQ");
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
        builder.UseSetting("RateLimits:LoginPerMinute", "1000");
        builder.UseSetting("RateLimits:RequestsPerMinute", "100000");
        builder.UseSetting("Storage:ServiceUrl", Environment.GetEnvironmentVariable("QMS_TEST_S3") ?? "http://localhost:9000");
        builder.UseSetting("Storage:AccessKey", Environment.GetEnvironmentVariable("QMS_TEST_S3_KEY") ?? "qgiqminio");
        builder.UseSetting("Storage:SecretKey", Environment.GetEnvironmentVariable("QMS_TEST_S3_SECRET") ?? "qgiqminio-local-dev");
        builder.UseSetting("Storage:Bucket", "qms-test");
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<Seeder>().SeedAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    public async Task<ApiClient> AnonymousAsync()
    {
        var http = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var c = new ApiClient(http);
        await c.RefreshCsrfAsync();
        return c;
    }

    public async Task<ApiClient> SignInAsync(string email, string password)
    {
        var c = await AnonymousAsync();
        var r = await c.PostAsync("/api/auth/login", new { email, password });
        if (r.StatusCode != HttpStatusCode.NoContent) throw new InvalidOperationException($"Sign-in failed for {email}: {r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        await c.RefreshCsrfAsync();
        return c;
    }

    public Task<ApiClient> AdminAsync() => SignInAsync(AdminEmail, AdminPassword);

    /// <summary>Creates a user and assigns a new role with the given permissions at the given scope.</summary>
    public async Task<(Guid UserId, ApiClient Client)> CreateUserAsync(string[] permissions, Guid? siteId = null, Guid? departmentId = null)
    {
        var admin = await AdminAsync();
        var email = $"u{Guid.NewGuid():N}@test.local";
        var userId = await (await admin.PostOkAsync("/api/admin/users", new { email, displayName = "Test " + email[..9], password = "long-enough-password" })).Content.ReadFromJsonAsync<Guid>();
        var roleId = await (await admin.PostOkAsync("/api/admin/roles", new { name = "R" + Guid.NewGuid().ToString("N"), description = (string?)null, permissions })).Content.ReadFromJsonAsync<Guid>();
        await admin.PostOkAsync($"/api/admin/users/{userId}/roles", new { roleId, siteId, departmentId });
        return (userId, await SignInAsync(email, "long-enough-password"));
    }

    public async Task<Guid> CreateSiteAsync(string? code = null)
    {
        var admin = await AdminAsync();
        code ??= "S" + Guid.NewGuid().ToString("N")[..8];
        return await (await admin.PostOkAsync("/api/organization/sites", new { code, name = "Site " + code, address = (string?)null })).Content.ReadFromJsonAsync<Guid>();
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<TestApp>;

public sealed class ApiClient(HttpClient http)
{
    private string? _csrf;
    public HttpClient Http => http;

    public async Task RefreshCsrfAsync()
    {
        var r = await http.GetFromJsonAsync<CsrfResponse>("/api/auth/csrf");
        _csrf = r!.Token;
    }

    private HttpRequestMessage Req(HttpMethod m, string url, object? body)
    {
        var req = new HttpRequestMessage(m, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        if (_csrf is not null) req.Headers.Add("X-CSRF-TOKEN", _csrf);
        return req;
    }

    public Task<HttpResponseMessage> SendWithCsrfAsync(HttpRequestMessage req)
    {
        if (_csrf is not null) req.Headers.Add("X-CSRF-TOKEN", _csrf);
        return http.SendAsync(req);
    }

    public Task<HttpResponseMessage> GetAsync(string url) => http.GetAsync(url);
    public Task<HttpResponseMessage> PostAsync(string url, object? body = null) => http.SendAsync(Req(HttpMethod.Post, url, body ?? new { }));
    public Task<HttpResponseMessage> PutAsync(string url, object body) => http.SendAsync(Req(HttpMethod.Put, url, body));
    public Task<HttpResponseMessage> DeleteAsync(string url) => http.SendAsync(Req(HttpMethod.Delete, url, null));

    public async Task<HttpResponseMessage> PostOkAsync(string url, object? body = null)
    {
        var r = await PostAsync(url, body);
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"POST {url} failed: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        return r;
    }

    public async Task<T> GetJsonAsync<T>(string url)
    {
        var r = await http.GetAsync(url);
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"GET {url} failed: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    private sealed record CsrfResponse(string Token);
}
