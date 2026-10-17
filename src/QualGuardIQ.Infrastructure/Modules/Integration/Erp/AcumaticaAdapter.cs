using System.Text.Json;
using System.Text.Json.Nodes;
using QualGuardIQ.Domain.Integration;

namespace QualGuardIQ.Infrastructure.Modules.Integration.Erp;

/// <summary>
/// Acumatica contract-based REST. No client-credentials grant: server-to-server uses Resource Owner Password with a
/// dedicated integration user, token from /identity/connect/token, scope api. One token per connection, requests one at a
/// time by default (license request limits). See docs/integrations/acumatica.md.
/// </summary>
public sealed class AcumaticaAdapter : IErpAdapter
{
    public ErpSystem System => ErpSystem.Acumatica;
    public IReadOnlyList<string> Editions => ["ContractRest"];
    const string Doc = "docs/integrations/acumatica.md";

    static readonly Dictionary<ErpEntity, (string Endpoint, string Entity, string? Expand, Dictionary<string, string> Map)> E = new()
    {
        [ErpEntity.Item] = ("Default", "StockItem", null, new() { ["number"] = "InventoryID", ["name"] = "Description", ["uom"] = "BaseUOM" }),
        [ErpEntity.Customer] = ("Default", "Customer", null, new() { ["code"] = "CustomerID", ["name"] = "CustomerName" }),
        [ErpEntity.Supplier] = ("Default", "Vendor", null, new() { ["code"] = "VendorID", ["name"] = "VendorName" }),
        [ErpEntity.PurchaseOrder] = ("Default", "PurchaseOrder", "Details", new() { ["number"] = "OrderNbr", ["supplierCode"] = "VendorID", ["orderedOn"] = "Date",
            ["lines"] = "Details", ["lines.lineNumber"] = "LineNbr", ["lines.item"] = "InventoryID", ["lines.quantity"] = "OrderQty", ["lines.dueOn"] = "Promised" }),
        [ErpEntity.Bom] = ("MANUFACTURING", "BillOfMaterial", "Operations,Operations/Material", new() { ["parent"] = "InventoryID", ["revision"] = "Revision" }),
        [ErpEntity.ProductionOrder] = ("MANUFACTURING", "ProductionOrder", null, new() { ["number"] = "ProductionNbr", ["item"] = "InventoryID", ["quantity"] = "QtytoProduce" }),
    };

    public ErpCapabilities Capabilities(string edition) => new(edition, [.. E.Keys], [], true,
        "OAuth 2.0 Resource Owner Password (Connected Applications SM303010), scope api; token POST /identity/connect/token",
        "/entity/Default/<version>/<Entity>; manufacturing via /entity/MANUFACTURING/<version>/…", "$top/$skip ($skip applies first), $filter LastModifiedDateTime gt datetimeoffset'…', $expand for details",
        "License: max API sessions, concurrent requests and requests per minute (SM604000); requests delayed at 50% of the per-minute limit, declined when the queue exceeds 20 or waits 10 min",
        "Push notifications (SM302000) can trigger a delta run (not enabled)", 1, 0, Doc);

    public IReadOnlyList<UnverifiedItem> Unverified(string edition) =>
    [
        new("Entities", "Customer, Vendor, PurchaseOrder, ProductionOrder and the lot/serial inquiry and their field lists are not confirmed; read /entity/Default/<ver>/swagger.json on the customer instance.", Doc),
        new("Field wrapper", "Field values are read from {\"value\": …} wrappers as in the documented BOM example; confirm per entity.", Doc),
        new("Sign-in path", "/entity/auth/login (cookie sign-in) is not used; OAuth is.", Doc),
        new("Errors / sandbox", "Error body (message, exceptionMessage, per-field error) and partner sandbox details are not confirmed.", Doc),
        new("Versions", "Endpoint versions default to Default 25.200.001 and MANUFACTURING 25.100.001 (from the cited examples); set options defaultVersion / manufacturingVersion to the instance's versions.", Doc),
    ];

    public IReadOnlyDictionary<string, string> DefaultMap(string edition, ErpEntity entity) => E.TryGetValue(entity, out var e) ? e.Map : new();

    async Task<string> TokenAsync(ErpCallContext c, CancellationToken ct) => await c.Tokens.GetAsync(c.Connection.Id, "acu", async () =>
        ErpWire.Token(await c.Http.JsonAsync(() => ErpWire.Form($"{c.Base}/identity/connect/token",
        [
            new("grant_type", "password"), new("client_id", c.Connection.ClientId ?? ""), new("client_secret", c.SecondarySecret ?? ""),
            new("username", c.Connection.Username ?? throw new ErpException("Set the integration user.")), new("password", c.RequireSecret("integration user password")), new("scope", "api")
        ]), ct)), c.Now);

    public static string Url(ErpCallContext c, ErpEntity e, ErpCursor cur, int top)
    {
        var d = E[e];
        var ver = d.Endpoint == "Default" ? c.Opt("defaultVersion", "25.200.001") : c.Opt("manufacturingVersion", "25.100.001");
        var skip = int.TryParse(cur.PageToken, out var s) ? s : 0;
        var q = $"$top={top}&$skip={skip}";
        if (cur.Watermark is { } w) q += "&$filter=" + Uri.EscapeDataString($"LastModifiedDateTime gt datetimeoffset'{w.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss.fff}'");
        if (d.Expand is not null) q += "&$expand=" + d.Expand;
        return $"{c.Base}/entity/{d.Endpoint}/{ver}/{d.Entity}?{q}";
    }

    public static string? ParseError(HttpResponseMessage r, string body)
    {
        try { var n = JsonNode.Parse(body); return n?["exceptionMessage"]?.ToString() ?? n?["message"]?.ToString(); }
        catch (JsonException) { return null; }
    }

    public async Task<string> TestAsync(ErpCallContext c, CancellationToken ct)
    {
        c.Http.ErrorParser = ParseError;
        var t = await TokenAsync(c, ct);
        var n = await c.Http.JsonAsync(() => ErpWire.Get(Url(c, ErpEntity.Item, new(null, null), 1), ErpWire.Bearer(t)), ct);
        return $"Token issued; StockItem returned {(n as JsonArray)?.Count ?? 0} row(s).";
    }

    public async Task<ErpPage> ReadAsync(ErpCallContext c, ErpEntity entity, ErpCursor cursor, CancellationToken ct)
    {
        this.RequireRead(c.Connection.Edition, entity);
        c.Http.ErrorParser = ParseError;
        var top = Math.Clamp(c.PageSize <= 0 ? 100 : c.PageSize, 1, 1000);
        var t = await TokenAsync(c, ct);
        var n = await c.Http.JsonAsync(() => ErpWire.Get(Url(c, entity, cursor, top), ErpWire.Bearer(t)), ct);
        var rows = (n as JsonArray ?? []).OfType<JsonNode>().Select(x => c.Record(this, entity, x, "id", "LastModifiedDateTime")).ToList();
        var skip = (int.TryParse(cursor.PageToken, out var s) ? s : 0) + rows.Count;
        return new ErpPage(rows, rows.Count == top ? skip.ToString() : null);
    }
}
