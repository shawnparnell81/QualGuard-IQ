using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Tests;

/// <summary>Fill from record: the PPAP common data page and characteristics list fill other PPAP forms, and a CAPA fills its CAPA report.</summary>
[Collection("api")]
public sealed class FormPrefillTests(TestApp app)
{
    sealed record Inst(string Key, string Title, Guid DocumentId, string Number, bool AlreadyInstalled);
    sealed record Result(string PackageKey, string Version, List<Inst> Templates, int Installed, int AlreadyInstalled);

    async Task<Dictionary<string, Guid>> InstallEffectiveAsync(ApiClient c, Guid site, params string[] keys)
    {
        var r = await (await c.PostOkAsync("/api/v1/template-packages/automotive/install", new { siteId = site, templateKeys = keys })).Content.ReadFromJsonAsync<Result>();
        var ids = r!.Templates.ToDictionary(t => t.Key, t => t.DocumentId);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var docs = ids.Values.ToList();
        await db.DocumentRevisions.Where(x => docs.Contains(x.DocumentId)).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, RevisionStatus.Effective));
        return ids;
    }

    static async Task<Guid> StartAsync(ApiClient c, Guid template, Guid site, string title) =>
        (await (await c.PostOkAsync("/api/forms/records", new { templateId = template, siteId = site, departmentId = (Guid?)null, title })).JsonAsync()).Id();

    static async Task SaveAsync(ApiClient c, Guid id, Dictionary<string, string?> values)
    {
        var rec = await c.GetElAsync($"/api/forms/records/{id}");
        var r = await c.PutAsync($"/api/forms/records/{id}", new { values, rowVersion = rec.GetProperty("rowVersion").GetUInt32() });
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"save failed: {await r.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task Common_data_page_and_characteristics_list_fill_the_dimensional_results()
    {
        var site = await app.CreateSiteAsync();
        var (_, eng) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsEdit, Permissions.DocumentsView, Permissions.TemplatesInstall], site);
        var (_, viewer) = await app.CreateUserAsync([Permissions.FormsView], site);
        var t = await InstallEffectiveAsync(eng, site, "ppap-common-data", "ppap-characteristics-list", "ppap-dimensional-results");

        var common = await StartAsync(eng, t["ppap-common-data"], site, "Bracket common data");
        await SaveAsync(eng, common, new() { ["part_name"] = "Mounting bracket", ["customer_part_number"] = "CB-1001", ["engineering_level"] = "C", ["org_name"] = "Parnell Precision" });
        var chars = await StartAsync(eng, t["ppap-characteristics-list"], site, "Bracket characteristics");
        var rows = new JsonArray(
            new JsonObject { ["char_no"] = "1", ["char_type"] = "Dimensional", ["char_desc"] = "Hole diameter", ["char_nominal"] = 8.0, ["char_lsl"] = 7.95, ["char_usl"] = 8.05, ["char_method"] = "CMM" },
            new JsonObject { ["char_no"] = "2", ["char_type"] = "Material", ["char_desc"] = "Hardness" },
            new JsonObject { ["char_no"] = "3", ["char_type"] = "Dimensional", ["char_desc"] = "Overall length", ["char_lsl"] = 99.8, ["char_usl"] = 100.2 });
        await SaveAsync(eng, chars, new() { ["characteristics"] = new JsonObject { ["rows"] = rows }.ToJsonString() });

        var dim = await StartAsync(eng, t["ppap-dimensional-results"], site, "Bracket dimensional");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"/api/forms/records/{dim}/prefill", new { sourceType = "FormRecord", sourceId = common, overwrite = false })).StatusCode);
        var a = await (await eng.PostOkAsync($"/api/forms/records/{dim}/prefill", new { sourceType = "FormRecord", sourceId = common, overwrite = false })).JsonAsync();
        Assert.Contains(a.GetProperty("keys").EnumerateArray(), k => k.GetString() == "customer_part_number");
        var b = await (await eng.PostOkAsync($"/api/forms/records/{dim}/prefill", new { sourceType = "FormRecord", sourceId = chars, overwrite = false })).JsonAsync();
        Assert.Contains(b.GetProperty("keys").EnumerateArray(), k => k.GetString() == "dim_rows");

        var rec = await eng.GetElAsync($"/api/forms/records/{dim}");
        var values = rec.GetProperty("values");
        Assert.Equal("CB-1001", values.Str("customer_part_number"));
        Assert.Equal("Mounting bracket", values.Str("part_name"));
        var dimRows = JsonNode.Parse(values.Str("dim_rows"))!["rows"]!.AsArray();
        Assert.Equal(2, dimRows.Count);                                    // material rows are left out
        Assert.Equal("Hole diameter", (string?)dimRows[0]!["dim_char"]);
        Assert.Equal("CMM", (string?)dimRows[0]!["dim_method"]);
        Assert.Equal("3", (string?)dimRows[1]!["dim_no"]);

        // existing entries are kept unless asked to overwrite
        await SaveAsync(eng, common, new() { ["part_name"] = "Mounting bracket", ["customer_part_number"] = "CB-2002", ["engineering_level"] = "C", ["org_name"] = "Parnell Precision" });
        await eng.PostOkAsync($"/api/forms/records/{dim}/prefill", new { sourceType = "FormRecord", sourceId = common, overwrite = false });
        Assert.Equal("CB-1001", (await eng.GetElAsync($"/api/forms/records/{dim}")).GetProperty("values").Str("customer_part_number"));
        await eng.PostOkAsync($"/api/forms/records/{dim}/prefill", new { sourceType = "FormRecord", sourceId = common, overwrite = true });
        Assert.Equal("CB-2002", (await eng.GetElAsync($"/api/forms/records/{dim}")).GetProperty("values").Str("customer_part_number"));

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.AuditEntries.AnyAsync(x => x.EntityId == dim && x.Description!.StartsWith("Filled ")));
    }

    [Fact]
    public async Task A_capa_starts_its_capa_report_already_filled()
    {
        var site = await app.CreateSiteAsync();
        var (_, eng) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsEdit, Permissions.DocumentsView, Permissions.TemplatesInstall, Permissions.CapaView, Permissions.CapaEdit], site);
        var capaId = (await (await eng.PostOkAsync("/api/capas", new { title = "Burr on bracket edge", problemStatement = "Customer found burrs on 12 brackets", capaType = "Corrective", source = "Customer complaint", siteId = site })).JsonAsync()).Id();

        // without an effective CAPA report template there is nothing to start
        Assert.False((await eng.PostAsync($"/api/forms/records/from-capa/{capaId}", new { })).IsSuccessStatusCode);

        await InstallEffectiveAsync(eng, site, "capa-report");
        var id = (await (await eng.PostOkAsync($"/api/forms/records/from-capa/{capaId}", new { })).JsonAsync()).Id();
        var values = (await eng.GetElAsync($"/api/forms/records/{id}")).GetProperty("values");
        Assert.Equal("Customer found burrs on 12 brackets", values.Str("problem_statement"));
        Assert.Equal("Burr on bracket edge", values.Str("record_title"));
    }
}
