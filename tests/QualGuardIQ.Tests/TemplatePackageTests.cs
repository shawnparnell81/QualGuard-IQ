using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using QualGuardIQ.Domain.Security;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>Template marketplace (FI 5, TECH 3 template packages): industry packs, install as draft form templates, export and import of package files.</summary>
[Collection("api")]
public sealed class TemplatePackageTests(TestApp app)
{
    sealed record Pkg(string Key, string Version, string Name, int Origin, int Integrity, List<string> Categories, int TemplateCount, int InstalledCount, Guid? PackageId);
    sealed record Tpl(string Key, string Title, string Category, int FieldCount, Guid? InstalledDocumentId, string? InstalledNumber);
    sealed record Detail(Pkg Package, List<Tpl> Templates);
    sealed record Inst(string Key, string Title, Guid DocumentId, string Number, bool AlreadyInstalled);
    sealed record Result(string PackageKey, string Version, List<Inst> Templates, int Installed, int AlreadyInstalled);

    static async Task<HttpResponseMessage> UploadAsync(ApiClient c, byte[] bytes, string name)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes); file.Headers.ContentType = new("application/zip");
        form.Add(file, "file", name);
        return await c.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, "/api/v1/template-packages/import") { Content = form });
    }

    [Fact]
    public async Task Packs_are_listed_previewed_and_installed_as_draft_templates_once()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync([Permissions.FormsView, Permissions.DocumentsView, Permissions.TemplatesInstall], site);
        var (_, viewer) = await app.CreateUserAsync([Permissions.FormsView, Permissions.DocumentsView], site);
        var (_, outsider) = await app.CreateUserAsync([Permissions.NcrView], site);

        var list = await viewer.GetJsonAsync<List<Pkg>>("/api/v1/template-packages");
        Assert.Equal(["aerospace", "automotive", "electronics", "manufacturing", "medical-device"], list.Where(p => p.Origin == 0).Select(p => p.Key).Order());
        Assert.Equal(0, list.Single(p => p.Key == "electronics").TemplateCount);
        Assert.True(list.Single(p => p.Key == "automotive").TemplateCount >= 50);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync("/api/v1/template-packages")).StatusCode);

        var detail = await viewer.GetJsonAsync<Detail>("/api/v1/template-packages/aerospace");
        Assert.Contains(detail.Templates, t => t.Key == "configuration-change-record");
        Assert.Contains(detail.Templates, t => t.Category == "FAI");
        var preview = await viewer.GetJsonAsync<JsonElement>("/api/v1/template-packages/library/templates/ppap-warrant");
        Assert.True(preview.GetProperty("definition").GetProperty("fields").GetArrayLength() > 20);
        Assert.False(string.IsNullOrWhiteSpace(preview.GetProperty("purpose").GetString()));
        var search = await viewer.GetJsonAsync<List<Tpl>>("/api/v1/template-packages/library/templates?q=gage");
        Assert.Contains(search, t => t.Key == "variable-gage-rr");

        // installing needs templates.install at the site
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync("/api/v1/template-packages/automotive/install", new { siteId = site, templateKeys = new[] { "ppap-warrant" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PostAsync("/api/v1/template-packages/automotive/install", new { siteId = site, templateKeys = new[] { "device-history-record" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PostAsync("/api/v1/template-packages/library/install", new { siteId = site })).StatusCode);

        var r = await (await author.PostOkAsync("/api/v1/template-packages/automotive/install", new { siteId = site, templateKeys = new[] { "ppap-warrant", "apqp-control-plan" } })).Content.ReadFromJsonAsync<Result>();
        Assert.Equal(2, r!.Templates.Count);
        Assert.Equal(2, r.Installed + r.AlreadyInstalled);
        var warrant = r.Templates.Single(t => t.Key == "ppap-warrant");
        var doc = await author.GetElAsync($"/api/documents/{warrant.DocumentId}");
        Assert.Equal(2, doc.GetProperty("kind").GetInt32());
        var rev = doc.GetProperty("revisions")[0];
        Assert.Equal(0, rev.GetProperty("status").GetInt32());
        Assert.Contains("\"record_title\"", rev.GetProperty("fieldSchema").GetString());

        // the catalog files it under the package with its library category
        var cat = await author.GetJsonAsync<JsonElement>($"/api/template-library?q={Uri.EscapeDataString(warrant.Number)}");
        var item = cat.GetProperty("items")[0];
        Assert.Equal(5, item.GetProperty("source").GetInt32());
        Assert.Equal("automotive", item.GetProperty("industryPack").GetString());
        Assert.Equal("PPAP", item.GetProperty("category").GetString());

        // installing again does not duplicate
        var again = await (await author.PostOkAsync("/api/v1/template-packages/automotive/install", new { siteId = site, templateKeys = new[] { "ppap-warrant", "apqp-control-plan" } })).Content.ReadFromJsonAsync<Result>();
        Assert.Equal(0, again!.Installed);
        Assert.All(again.Templates, t => Assert.True(t.AlreadyInstalled));
        Assert.Equal(warrant.DocumentId, again.Templates.Single(t => t.Key == "ppap-warrant").DocumentId);

        // a whole pack installs every template it carries
        var aero = await (await author.PostOkAsync("/api/v1/template-packages/aerospace/install", new { siteId = site })).Content.ReadFromJsonAsync<Result>();
        Assert.Equal(detail.Templates.Count, aero!.Templates.Count);
        var after = await viewer.GetJsonAsync<List<Pkg>>("/api/v1/template-packages");
        Assert.Equal(detail.Templates.Count, after.Single(p => p.Key == "aerospace").InstalledCount);

        var audit = await (await app.AdminAsync()).GetJsonAsync<JsonElement>($"/api/audit-trail?entityId={after.Single(p => p.Key == "aerospace").PackageId}");
        Assert.Contains("Installed", audit.ToString());
    }

    [Fact]
    public async Task Templates_export_to_a_package_file_that_imports_with_integrity_checks()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync([Permissions.FormsView, Permissions.DocumentsView, Permissions.TemplatesInstall, Permissions.TemplatesExport], site);
        var (_, installer) = await app.CreateUserAsync([Permissions.FormsView, Permissions.DocumentsView, Permissions.TemplatesInstall], site);
        var r = await (await author.PostOkAsync("/api/v1/template-packages/library/install", new { siteId = site, templateKeys = new[] { "mr-minutes", "mr-decisions" } })).Content.ReadFromJsonAsync<Result>();
        var ids = r!.Templates.Select(t => t.DocumentId).ToArray();
        var key = "acme-review-" + Guid.NewGuid().ToString("N")[..6];

        Assert.Equal(HttpStatusCode.Forbidden, (await installer.PostAsync("/api/v1/template-packages/export", new { packageKey = key, name = "Acme reviews", version = "1.0", documentIds = ids })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PostAsync("/api/v1/template-packages/export", new { packageKey = "automotive", name = "x", version = "1.0", documentIds = ids })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PostAsync("/api/v1/template-packages/export", new { packageKey = "Bad Key", name = "x", version = "1.0", documentIds = ids })).StatusCode);

        var res = await author.PostOkAsync("/api/v1/template-packages/export", new { packageKey = key, name = "Acme reviews", version = "1.0", description = "Management review set", documentIds = ids });
        var bytes = await res.Content.ReadAsByteArrayAsync();
        using (var zip = new ZipArchive(new MemoryStream(bytes)))
        {
            var manifest = JsonNode.Parse(new StreamReader(zip.GetEntry("manifest.json")!.Open()).ReadToEnd())!;
            Assert.Equal(1, (int)manifest["formatVersion"]!);
            Assert.Equal(2, manifest["templates"]!.AsArray().Count);
            Assert.Contains(manifest["templates"]!.AsArray(), t => (string)t!["key"]! == "mr-minutes" && (string)t["category"]! == "MANAGEMENT_REVIEW");
        }
        Assert.Equal(HttpStatusCode.Conflict, (await author.PostAsync("/api/v1/template-packages/export", new { packageKey = key, name = "Acme reviews", version = "1.0", documentIds = ids })).StatusCode);

        // a damaged or altered file is refused
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(author, "not a zip"u8.ToArray(), "x.qgpkg")).StatusCode);
        var tampered = Tamper(bytes);
        var bad = await UploadAsync(author, tampered, "tampered.qgpkg");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("hash", await bad.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(installer, bytes, "p.qgpkg")).StatusCode);

        var imported = await (await UploadAsync(author, bytes, "p.qgpkg")).Content.ReadFromJsonAsync<Pkg>();
        Assert.Equal(key, imported!.Key);
        Assert.Equal(1, imported.Origin);
        Assert.Equal(2, imported.TemplateCount);
        Assert.Equal(HttpStatusCode.Conflict, (await UploadAsync(author, bytes, "p.qgpkg")).StatusCode);

        var inst = await (await installer.PostOkAsync($"/api/v1/template-packages/{key}/install", new { siteId = site })).Content.ReadFromJsonAsync<Result>();
        Assert.Equal(2, inst!.Installed);
        Assert.DoesNotContain(inst.Templates, t => ids.Contains(t.DocumentId));
        var again = await (await installer.PostOkAsync($"/api/v1/template-packages/{key}/install", new { siteId = site })).Content.ReadFromJsonAsync<Result>();
        Assert.Equal(2, again!.AlreadyInstalled);
        var detail = await installer.GetJsonAsync<Detail>($"/api/v1/template-packages/{key}");
        Assert.All(detail.Templates, t => Assert.NotNull(t.InstalledNumber));
    }

    static byte[] Tamper(byte[] bytes)
    {
        using var input = new ZipArchive(new MemoryStream(bytes));
        using var ms = new MemoryStream();
        using (var output = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            foreach (var e in input.Entries)
            {
                var text = new StreamReader(e.Open()).ReadToEnd();
                if (e.FullName.StartsWith("templates/")) text = text.Replace("Minutes", "Notes");
                using var w = new StreamWriter(output.CreateEntry(e.FullName).Open(), new UTF8Encoding(false));
                w.Write(text);
            }
        }
        return ms.ToArray();
    }
}
