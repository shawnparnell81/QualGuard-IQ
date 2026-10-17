using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Serialization;

namespace QualGuardIQ.Tests;

/// <summary>OpenAPI generated from code: staleness of docs/api/openapi.yaml, the FAI/AQL reference contract, and bearer auth alongside the cookie.</summary>
[Collection("api")]
public sealed class OpenApiTests(TestApp app)
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, ".git"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>Fails when docs/api/openapi.yaml differs from what the code generates. Regenerate with scripts/openapi.sh (sets UPDATE_OPENAPI=1).</summary>
    [Fact]
    public async Task Committed_openapi_yaml_is_current()
    {
        var anon = await app.AnonymousAsync();
        var r = await anon.GetAsync("/openapi/v1.yaml");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var yaml = (await r.Content.ReadAsStringAsync()).Replace("\r\n", "\n");
        Assert.Contains("bearerAuth", yaml);
        Assert.Contains("cookieAuth", yaml);
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync("/openapi/v1.json")).StatusCode);
        var path = Path.Combine(Root(), "docs", "api", "openapi.yaml");
        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, yaml);
        }
        Assert.True(File.Exists(path), "docs/api/openapi.yaml is missing; run scripts/openapi.sh.");
        Assert.True(yaml == (await File.ReadAllTextAsync(path)).Replace("\r\n", "\n"), "docs/api/openapi.yaml is stale; run scripts/openapi.sh and commit the result.");
    }

    private static JsonNode LoadReference()
    {
        var obj = new DeserializerBuilder().Build().Deserialize(new StringReader(File.ReadAllText(Path.Combine(Root(), "docs", "requirements", "openapi-reference.yaml"))));
        var json = new SerializerBuilder().JsonCompatible().Build().Serialize(obj!);
        return JsonNode.Parse(json)!;
    }

    private static string Norm(string p) => System.Text.RegularExpressions.Regex.Replace(p, "\\{[^}]+\\}", "{}").TrimEnd('/');

    private static JsonObject? Resolve(JsonNode root, JsonNode? schema)
    {
        for (var i = 0; i < 5 && schema?["$ref"] is { } r; i++)
            schema = root["components"]!["schemas"]![r.GetValue<string>().Split('/').Last()];
        if (schema?["allOf"] is JsonArray all)
        {
            var merged = new JsonObject { ["properties"] = new JsonObject(), ["required"] = new JsonArray() };
            foreach (var part in all) if (Resolve(root, part) is { } p)
                {
                    foreach (var kv in p["properties"]?.AsObject() ?? []) merged["properties"]!.AsObject()[kv.Key] = kv.Value?.DeepClone();
                    foreach (var q in p["required"]?.AsArray() ?? []) merged["required"]!.AsArray().Add(q?.DeepClone());
                }
            return merged;
        }
        return schema as JsonObject;
    }

    private static void CompareSchema(List<string> gaps, string where, JsonNode refRoot, JsonNode? refSchema, JsonNode genRoot, JsonNode? genSchema)
    {
        var rs = Resolve(refRoot, refSchema); var gs = Resolve(genRoot, genSchema);
        if (rs?["properties"] is not JsonObject rp) return;
        if (gs?["properties"] is not JsonObject gp) { gaps.Add($"{where}: no object schema"); return; }
        var missing = rp.Select(kv => kv.Key).Where(k => !gp.ContainsKey(k)).ToList();
        if (missing.Count > 0) gaps.Add($"{where}: missing properties {string.Join(", ", missing)}");
    }

    /// <summary>Every operation in docs/requirements/openapi-reference.yaml must exist with the same operationId, success status, error responses and schema properties.</summary>
    [Fact]
    public async Task Fai_aql_reference_contract_is_implemented()
    {
        var reference = LoadReference();
        var anon = await app.AnonymousAsync();
        var gen = JsonNode.Parse(await (await anon.GetAsync("/openapi/v1.json")).Content.ReadAsStringAsync())!;
        var genOps = new Dictionary<string, JsonObject>();
        foreach (var (p, item) in gen["paths"]!.AsObject())
            foreach (var (m, op) in item!.AsObject())
                if (op is JsonObject o) genOps[$"{m.ToUpperInvariant()} {Norm(p)}"] = o;
        var gaps = new List<string>();
        foreach (var (p, item) in reference["paths"]!.AsObject())
            foreach (var (m, opNode) in item!.AsObject())
            {
                if (opNode is not JsonObject rop || m == "parameters") continue;
                var key = $"{m.ToUpperInvariant()} {Norm("/api/v1" + p)}";
                var id = rop["operationId"]!.GetValue<string>();
                if (!genOps.TryGetValue(key, out var gop)) { gaps.Add($"{key} ({id}): missing"); continue; }
                if (gop["operationId"]?.GetValue<string>() != id) gaps.Add($"{key}: operationId {gop["operationId"]} ≠ {id}");
                var gres = gop["responses"]?.AsObject() ?? [];
                foreach (var (code, rres) in rop["responses"]!.AsObject())
                {
                    if (!gres.ContainsKey(code)) { gaps.Add($"{key} ({id}): no {code} response"); continue; }
                    if (code.StartsWith('2') && rres?["content"]?["application/json"]?["schema"] is { } rsch)
                        CompareSchema(gaps, $"{key} ({id}) {code}", reference, rsch, gen, gres[code]?["content"]?["application/json"]?["schema"]);
                }
                if (rop["requestBody"]?["content"]?["application/json"]?["schema"] is { } rb)
                    CompareSchema(gaps, $"{key} ({id}) request", reference, rb, gen, gop["requestBody"]?["content"]?["application/json"]?["schema"]);
            }
        Assert.True(gaps.Count == 0, $"{gaps.Count} contract gap(s):\n" + string.Join("\n", gaps));
    }

    /// <summary>Bearer tokens work for API clients without replacing the cookie; bearer writes need no CSRF header; bad credentials are refused.</summary>
    [Fact]
    public async Task Bearer_token_authenticates_alongside_cookie()
    {
        var http = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/api/auth/token", new { email = TestApp.AdminEmail, password = "wrong-password-xx" })).StatusCode);
        var tok = await http.PostAsJsonAsync("/api/auth/token", new { email = TestApp.AdminEmail, password = TestApp.AdminPassword });
        Assert.Equal(HttpStatusCode.OK, tok.StatusCode);
        var access = (await tok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        var api = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync("/api/auth/me")).StatusCode);
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/api/auth/me")).StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, (await api.PostAsJsonAsync("/api/v1/aql/calculate-ppm", new { defects = 1, units = 100 })).StatusCode);
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync("/api/auth/me")).StatusCode);
        // cookie sessions still work and still require CSRF on writes
        var cookie = await app.AdminAsync();
        Assert.Equal(HttpStatusCode.OK, (await cookie.GetAsync("/api/auth/me")).StatusCode);
    }
}
