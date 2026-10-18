using System.Text.Json;
using System.Text.Json.Nodes;
using QualGuardIQ.Domain.Integration;

namespace QualGuardIQ.Infrastructure.Modules.Integration.Erp;

/// <summary>
/// QuickBooks Online Accounting API. OAuth 2.0 authorization code: an administrator connects the app once in the Intuit sign-in and the
/// refresh token is stored encrypted. Each run swaps the refresh token for a one-hour access token; Intuit can return a new refresh
/// token on every swap, so the newest one is written back. Reads use the query endpoint with STARTPOSITION / MAXRESULTS paging and
/// MetaData.LastUpdatedTime for deltas. See docs/integrations/quickbooks.md.
/// </summary>
public sealed class QuickBooksAdapter : IErpAdapter
{
    public ErpSystem System => ErpSystem.QuickBooks;
    public IReadOnlyList<string> Editions => ["Online"];
    const string Doc = "docs/integrations/quickbooks.md";
    public const string TokenUrl = "https://oauth.platform.intuit.com/oauth2/v1/tokens/bearer";
    public const string Production = "https://quickbooks.api.intuit.com", Sandbox = "https://sandbox-quickbooks.api.intuit.com";
    const int MaxResults = 1000;

    static readonly Dictionary<ErpEntity, (string Table, Dictionary<string, string> Map)> E = new()
    {
        [ErpEntity.Item] = ("Item", new() { ["number"] = "Sku", ["name"] = "Name", ["description"] = "Description", ["type"] = "Type" }),
        [ErpEntity.Customer] = ("Customer", new() { ["code"] = "DisplayName", ["name"] = "CompanyName", ["email"] = "PrimaryEmailAddr.Address" }),
        [ErpEntity.Supplier] = ("Vendor", new() { ["code"] = "DisplayName", ["name"] = "CompanyName", ["email"] = "PrimaryEmailAddr.Address" }),
        [ErpEntity.Employee] = ("Employee", new() { ["number"] = "EmployeeNumber", ["name"] = "DisplayName" }),
        [ErpEntity.PurchaseOrder] = ("PurchaseOrder", new() { ["number"] = "DocNumber", ["supplierCode"] = "VendorRef.name", ["orderedOn"] = "TxnDate",
            ["lines"] = "Line", ["lines.lineNumber"] = "LineNum", ["lines.item"] = "ItemBasedExpenseLineDetail.ItemRef.name", ["lines.quantity"] = "ItemBasedExpenseLineDetail.Qty" }),
    };

    public ErpCapabilities Capabilities(string edition) => new(edition, [.. E.Keys], [], true,
        "OAuth 2.0 authorization code (scope com.intuit.quickbooks.accounting); refresh token stored encrypted and rotated, access token 60 min",
        "GET /v3/company/{realmId}/query?query=SELECT … &minorversion=75", "STARTPOSITION / MAXRESULTS (max 1000)",
        "500 requests per minute per company (realm) and 10 concurrent requests; HTTP 429 when exceeded",
        "Webhooks exist (entity change notifications) but are not used: polling is the source of truth", 4, 400, Doc);

    public IReadOnlyList<UnverifiedItem> Unverified(string edition) =>
    [
        new("Item number", "Sku is only filled when the company turns on SKU tracking; map number to Name instead when it is off.", Doc),
        new("Manufacturing", "QuickBooks Online has no work orders, routings or BOMs; production data has to come from another system.", Doc),
        new("Refresh token", "Refresh tokens expire after 100 days without use and whenever the app is disconnected; reconnect from the setup screen then.", Doc),
    ];

    public IReadOnlyDictionary<string, string> DefaultMap(string edition, ErpEntity entity) => E.TryGetValue(entity, out var e) ? e.Map : new();

    public static string Root(ErpCallContext c) => c.Connection.BaseUrl is { Length: > 0 } ? c.Base : c.Connection.Environment == ErpEnvironment.Production ? Production : Sandbox;

    async Task<string> TokenAsync(ErpCallContext c, CancellationToken ct) => await c.Tokens.GetAsync(c.Connection.Id, "qbo", async () =>
    {
        var refresh = c.RequireNamed("refreshToken", "refresh token (connect the app once to get it)");
        var n = await c.Http.JsonAsync(() => ErpWire.Form(c.Opt("tokenUrl", TokenUrl), [new("grant_type", "refresh_token"), new("refresh_token", refresh)],
            ErpWire.Basic(c.Connection.ClientId ?? throw new ErpException("Set the client id of the Intuit app."), c.RequireNamed("clientSecret", "client secret", c.Secret))), ct);
        if (n["refresh_token"]?.GetValue<string>() is { Length: > 0 } next && next != refresh) c.Rotated["refreshToken"] = next;
        return ErpWire.Token(n);
    }, c.Now);

    public static string Query(ErpEntity e, ErpCursor cur, int max)
    {
        var start = int.TryParse(cur.PageToken, out var s) ? s : 1;
        var where = cur.Watermark is { } w ? $" WHERE MetaData.LastUpdatedTime > '{w.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss}Z'" : "";
        return $"SELECT * FROM {E[e].Table}{where} ORDERBY Id STARTPOSITION {start} MAXRESULTS {max}";
    }

    string Url(ErpCallContext c, string query)
    {
        var realm = c.Connection.Account is { Length: > 0 } r ? r : throw new ErpException("Set the company id (realm id).");
        return $"{Root(c)}/v3/company/{Uri.EscapeDataString(realm)}/query?query={Uri.EscapeDataString(query)}&minorversion={c.Opt("minorVersion", "75")}";
    }

    public static string? ParseError(HttpResponseMessage r, string body)
    {
        try
        {
            var n = JsonNode.Parse(body);
            var e = (n?["Fault"]?["Error"] as JsonArray)?.FirstOrDefault();
            return e is null ? n?["error"]?.ToString() : $"{e["code"]}: {e["Message"]} {e["Detail"]}".Trim();
        }
        catch (JsonException) { return null; }
    }

    public async Task<string> TestAsync(ErpCallContext c, CancellationToken ct)
    {
        c.Http.ErrorParser = ParseError;
        var t = await TokenAsync(c, ct);
        var n = await c.Http.JsonAsync(() => ErpWire.Get(Url(c, "SELECT * FROM CompanyInfo"), ErpWire.Bearer(t)), ct);
        var name = ErpWire.Str(n, "QueryResponse.CompanyInfo.0.CompanyName");
        return $"Token issued; connected to {(string.IsNullOrEmpty(name) ? "the company" : name)}.";
    }

    public async Task<ErpPage> ReadAsync(ErpCallContext c, ErpEntity entity, ErpCursor cursor, CancellationToken ct)
    {
        this.RequireRead(c.Connection.Edition, entity);
        c.Http.ErrorParser = ParseError;
        var max = Math.Clamp(c.PageSize <= 0 ? MaxResults : c.PageSize, 1, MaxResults);
        var t = await TokenAsync(c, ct);
        var n = await c.Http.JsonAsync(() => ErpWire.Get(Url(c, Query(entity, cursor, max)), ErpWire.Bearer(t)), ct);
        var rows = (ErpWire.Path(n, "QueryResponse." + E[entity].Table) as JsonArray ?? []).OfType<JsonNode>()
            .Select(x => c.Record(this, entity, x, "Id", "MetaData.LastUpdatedTime", "SyncToken")).ToList();
        var start = int.TryParse(cursor.PageToken, out var s) ? s : 1;
        return new ErpPage(rows, rows.Count == max ? (start + rows.Count).ToString() : null);
    }
}
