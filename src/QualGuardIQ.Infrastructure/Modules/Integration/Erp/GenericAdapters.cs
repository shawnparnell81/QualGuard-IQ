using System.Text.Json.Nodes;
using QualGuardIQ.Domain.Integration;

namespace QualGuardIQ.Infrastructure.Modules.Integration.Erp;

/// <summary>
/// Custom ERP / MES over REST, OData or GraphQL-over-HTTP: per entity the administrator sets url.&lt;Entity&gt;, items.&lt;Entity&gt;
/// (JSON path of the row array), id.&lt;Entity&gt; and next.&lt;Entity&gt; (JSON path of the next-page link). Auth: bearer, apikey or basic.
/// </summary>
public sealed class CustomRestAdapter : IErpAdapter
{
    public ErpSystem System => ErpSystem.CustomRest;
    public IReadOnlyList<string> Editions => ["Rest"];
    public ErpCapabilities Capabilities(string edition) => new(edition, Enum.GetValues<ErpEntity>(), [], false, "Bearer token, API key header or Basic (option auth)",
        "Configured per entity", "next-link path per entity", "Configured on the connection", "Inbound events through POST /api/mes/events", 2, 120, "docs/integrations/README.md");
    public IReadOnlyList<UnverifiedItem> Unverified(string edition) => [new("Custom", "Paths and field maps are whatever the administrator configures; test against the customer's system before enabling.", "docs/integrations/README.md")];
    public IReadOnlyDictionary<string, string> DefaultMap(string edition, ErpEntity entity) => entity switch
    {
        ErpEntity.Item => new Dictionary<string, string> { ["number"] = "number", ["name"] = "name", ["revision"] = "revision" },
        ErpEntity.Customer or ErpEntity.Supplier or ErpEntity.WorkCenter => new Dictionary<string, string> { ["code"] = "code", ["name"] = "name" },
        ErpEntity.ProductionOrder => new Dictionary<string, string> { ["number"] = "number", ["item"] = "item", ["revision"] = "revision", ["quantity"] = "quantity", ["dueOn"] = "dueOn", ["status"] = "status" },
        ErpEntity.PurchaseOrder => new Dictionary<string, string> { ["number"] = "number", ["supplierCode"] = "supplierCode", ["orderedOn"] = "orderedOn", ["lines"] = "lines",
            ["lines.lineNumber"] = "lineNumber", ["lines.item"] = "item", ["lines.revision"] = "revision", ["lines.quantity"] = "quantity", ["lines.dueOn"] = "dueOn" },
        _ => new Dictionary<string, string> { ["number"] = "number", ["name"] = "name" },
    };

    System.Net.Http.Headers.AuthenticationHeaderValue? Auth(ErpCallContext c) => c.Opt("auth", "bearer") switch
    {
        "basic" => ErpWire.Basic(c.Connection.Username ?? "", c.Secret ?? ""),
        "apikey" => null,
        _ => string.IsNullOrEmpty(c.Secret) ? null : ErpWire.Bearer(c.Secret),
    };

    public async Task<string> TestAsync(ErpCallContext c, CancellationToken ct) => $"{(await ReadAsync(c, Enum.Parse<ErpEntity>(c.Opt("testEntity", "Item")), new(null, null), ct)).Records.Count} row(s) read.";

    public async Task<ErpPage> ReadAsync(ErpCallContext c, ErpEntity entity, ErpCursor cursor, CancellationToken ct)
    {
        var url = cursor.PageToken ?? c.Opt("url." + entity);
        if (url.Length == 0) throw new ErpException($"Set option url.{entity}.");
        if (!url.StartsWith("http")) url = c.Base + "/" + url.TrimStart('/');
        if (cursor.Watermark is { } w && cursor.PageToken is null && c.Opt("since." + entity) is { Length: > 0 } p) url += (url.Contains('?') ? "&" : "?") + p + "=" + Uri.EscapeDataString(ErpWire.Iso(w));
        var headers = c.Opt("auth") == "apikey" ? new[] { (c.Opt("apiKeyHeader", "X-Api-Key"), c.Secret ?? "") } : [];
        var n = await c.Http.JsonAsync(() => ErpWire.Get(url, Auth(c), headers), ct);
        var arr = (c.Opt("items." + entity) is { Length: > 0 } ip ? ErpWire.Path(n, ip) : n) as JsonArray ?? [];
        var rows = arr.OfType<JsonNode>().Select(x => c.Record(this, entity, x, c.Opt("id." + entity, "id"), c.Opt("modified." + entity) is { Length: > 0 } m ? m : null)).ToList();
        var next = c.Opt("next." + entity) is { Length: > 0 } np ? ErpWire.Str(n, np) : null;
        return new ErpPage(rows, string.IsNullOrWhiteSpace(next) ? null : next);
    }
}

/// <summary>CSV exchange: files are uploaded (or dropped by a scheduled import) and parsed with a header row; column names are canonical or mapped.</summary>
public sealed class CsvExchangeAdapter : IErpAdapter
{
    public ErpSystem System => ErpSystem.CsvExchange;
    public IReadOnlyList<string> Editions => ["Csv"];
    public ErpCapabilities Capabilities(string edition) => new(edition, Enum.GetValues<ErpEntity>(), [], false, "None (file upload by a signed-in user with integrations.edit)",
        "CSV with header row; UTF-8", "Whole file", "n/a", "n/a", 1, 0, "docs/integrations/README.md");
    public IReadOnlyList<UnverifiedItem> Unverified(string edition) => [];
    public IReadOnlyDictionary<string, string> DefaultMap(string edition, ErpEntity entity) => new CustomRestAdapter().DefaultMap(edition, entity);
    public Task<string> TestAsync(ErpCallContext c, CancellationToken ct) => Task.FromResult("CSV connections need no test: upload a file to sync.");
    public Task<ErpPage> ReadAsync(ErpCallContext c, ErpEntity entity, ErpCursor cursor, CancellationToken ct) =>
        throw new ErpException("CSV connections are read from uploaded files: use Upload CSV.");

    /// <summary>Parses RFC 4180 CSV (quoted fields, doubled quotes, embedded newlines).</summary>
    public static List<Dictionary<string, string>> Parse(string text)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var f = new System.Text.StringBuilder(); var q = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (q) { if (ch == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { f.Append('"'); i++; } else q = false; } else f.Append(ch); continue; }
            switch (ch)
            {
                case '"': q = true; break;
                case ',': row.Add(f.ToString()); f.Clear(); break;
                case '\r': break;
                case '\n': row.Add(f.ToString()); f.Clear(); rows.Add(row); row = []; break;
                default: f.Append(ch); break;
            }
        }
        if (f.Length > 0 || row.Count > 0) { row.Add(f.ToString()); rows.Add(row); }
        if (rows.Count == 0) return [];
        var head = rows[0].Select(h => h.Trim().TrimStart('\uFEFF')).ToList();
        return rows.Skip(1).Where(r => r.Any(x => x.Length > 0)).Select(r => head.Select((h, k) => (h, v: k < r.Count ? r[k] : "")).ToDictionary(x => x.h, x => x.v)).ToList();
    }

    public ExternalRecord ToRecord(ErpCallContext c, ErpEntity entity, Dictionary<string, string> row)
    {
        var node = new JsonObject();
        foreach (var (k, v) in row) node[k] = v;
        var id = c.Opt("id." + entity, DefaultMap("Csv", entity).Values.First());
        return c.Record(this, entity, node, id, null);
    }
}
