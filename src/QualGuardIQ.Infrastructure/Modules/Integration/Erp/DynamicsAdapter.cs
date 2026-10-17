using System.Text.Json;
using System.Text.Json.Nodes;
using QualGuardIQ.Domain.Integration;

namespace QualGuardIQ.Infrastructure.Modules.Integration.Erp;

/// <summary>
/// Microsoft Dynamics 365: Business Central API v2.0 and Finance &amp; Supply Chain OData /data. Entra ID client
/// credentials for both. See docs/integrations/dynamics365.md.
/// </summary>
public sealed class DynamicsAdapter : IErpAdapter
{
    public const string BusinessCentral = "BusinessCentral", FinanceSupplyChain = "FinanceSupplyChain";
    public ErpSystem System => ErpSystem.Dynamics;
    public IReadOnlyList<string> Editions => [BusinessCentral, FinanceSupplyChain];
    const string Doc = "docs/integrations/dynamics365.md";

    static readonly Dictionary<ErpEntity, (string Set, string? Expand, bool Delta, Dictionary<string, string> Map)> Bc = new()
    {
        [ErpEntity.Item] = ("items", null, true, new() { ["number"] = "number", ["name"] = "displayName", ["uom"] = "baseUnitOfMeasureCode", ["type"] = "type", ["blocked"] = "blocked" }),
        [ErpEntity.Customer] = ("customers", null, true, new() { ["code"] = "number", ["name"] = "displayName", ["email"] = "email" }),
        [ErpEntity.Supplier] = ("vendors", null, true, new() { ["code"] = "number", ["name"] = "displayName", ["email"] = "email", ["blocked"] = "blocked" }),
        [ErpEntity.Employee] = ("employees", null, true, new() { ["number"] = "number", ["name"] = "displayName" }),
        [ErpEntity.PurchaseOrder] = ("purchaseOrders", "purchaseOrderLines", true, new() { ["number"] = "number", ["supplierCode"] = "vendorNumber", ["orderedOn"] = "orderDate",
            ["lines"] = "purchaseOrderLines", ["lines.lineNumber"] = "sequence", ["lines.item"] = "lineObjectNumber", ["lines.quantity"] = "quantity", ["lines.dueOn"] = "expectedReceiptDate", ["lines.uom"] = "unitOfMeasureCode" }),
        [ErpEntity.Receipt] = ("purchaseReceipts", "purchaseReceiptLines", false, new() { ["number"] = "number", ["supplierCode"] = "vendorNumber" }),
    };

    static readonly Dictionary<ErpEntity, (string Collection, string Id, Dictionary<string, string> Map)> Fo = new()
    {
        [ErpEntity.Item] = ("ReleasedProductsV2", "ItemNumber", new() { ["number"] = "ItemNumber", ["name"] = "SearchName" }),
        [ErpEntity.ProductionOrder] = ("ProductionOrderHeaders", "ProductionOrderNumber", new() { ["number"] = "ProductionOrderNumber", ["item"] = "ItemNumber", ["quantity"] = "ScheduledQuantity" }),
        [ErpEntity.Customer] = ("CustomersV3", "CustomerAccount", new() { ["code"] = "CustomerAccount", ["name"] = "OrganizationName" }),
        [ErpEntity.Supplier] = ("VendorsV2", "VendorAccountNumber", new() { ["code"] = "VendorAccountNumber", ["name"] = "VendorOrganizationName" }),
        [ErpEntity.PurchaseOrder] = ("PurchaseOrderHeadersV2", "PurchaseOrderNumber", new() { ["number"] = "PurchaseOrderNumber", ["supplierCode"] = "OrderVendorAccountNumber" }),
    };

    public ErpCapabilities Capabilities(string edition) => edition == FinanceSupplyChain
        ? new(edition, [.. Fo.Keys], [], false, "Entra ID client credentials, scope https://<env>.operations.dynamics.com/.default; app mapped to a service user in Microsoft Entra applications",
            "OData /data/<PublicCollectionName> (IsPublic entities only); cross-company=true + dataAreaId filter", "@odata.nextLink", "Service protection + resource throttling: 429 with Retry-After; set throttling priority mapping",
            "Business events to HTTPS / Service Bus / Event Grid (not used)", 2, 0, Doc)
        : new(edition, [.. Bc.Keys], [], true, "Entra ID client credentials, scope https://api.businesscentral.dynamics.com/.default; app enabled on the Microsoft Entra Applications page",
            "API v2.0 /v2.0/{tenant}/{environment}/api/v2.0/companies({id})/<entitySet>", "$filter=lastModifiedDateTime gt …; @odata.nextLink continuation; max page 20,000",
            "Per user: 5 concurrent (queued up to 8 min, then 503), about 6000 requests / 5 min (429), 10-minute request timeout (504)",
            "Webhook subscriptions (3-day expiry, renew by PATCH) can trigger a delta run (not enabled)", 5, 1200, Doc);

    public IReadOnlyList<UnverifiedItem> Unverified(string edition) => edition == FinanceSupplyChain
        ?
        [
            new("Collection names", "Only ReleasedProductsV2 is confirmed. ProductionOrderHeaders, CustomersV3, VendorsV2, PurchaseOrderHeadersV2 and field names come from each tenant's /data/$metadata.", Doc),
            new("Delta", "Needs SQL change tracking per entity; $filter on a modified field depends on the entity. The adapter reads in full and skips unchanged rows by hash.", Doc),
            new("Errors / sandbox", "Exact OData error shape and Tier-2 sandbox steps are not confirmed.", Doc),
        ]
        :
        [
            new("Manufacturing data", "The standard v2.0 API has no production orders, production BOMs, routings, work/machine centers or item tracking (lot/serial). A companion BC extension (AL PageType = API) must be installed for those; not shipped yet.", Doc),
            new("Purchase order fields", "vendorNumber and orderDate on purchaseOrders, and the purchaseOrderLines field set, are not listed in the research file; confirm on the resource pages.", Doc),
            new("Receipts delta", "purchaseReceiptLine has no lastModifiedDateTime, so receipts read in full.", Doc),
        ];

    public IReadOnlyDictionary<string, string> DefaultMap(string edition, ErpEntity entity) =>
        edition == FinanceSupplyChain ? (Fo.TryGetValue(entity, out var f) ? f.Map : new()) : (Bc.TryGetValue(entity, out var b) ? b.Map : new());

    public static string? ParseError(HttpResponseMessage r, string body)
    {
        try { var e = JsonNode.Parse(body)?["error"]; return e is null ? null : $"{e["code"]}: {e["message"]}"; }
        catch (JsonException) { return null; }
    }

    async Task<string> TokenAsync(ErpCallContext c, CancellationToken ct)
    {
        var tenant = c.Connection.Account ?? throw new ErpException("Set the Entra tenant id.");
        var scope = c.Connection.Edition == FinanceSupplyChain ? c.Base + "/.default" : "https://api.businesscentral.dynamics.com/.default";
        var authority = c.Opt("authority", "https://login.microsoftonline.com");
        return await c.Tokens.GetAsync(c.Connection.Id, "entra", async () => ErpWire.Token(await c.Http.JsonAsync(() => ErpWire.Form($"{authority}/{tenant}/oauth2/v2.0/token",
            [new("grant_type", "client_credentials"), new("client_id", c.Connection.ClientId ?? ""), new("client_secret", c.RequireSecret("client secret")), new("scope", scope)]), ct)), c.Now);
    }

    public static string BcRoot(ErpCallContext c)
    {
        var env = c.Opt("environment", "production");
        var company = c.Connection.Company ?? throw new ErpException("Set the company id.");
        var host = string.IsNullOrWhiteSpace(c.Connection.BaseUrl) ? "https://api.businesscentral.dynamics.com" : c.Base;
        return $"{host}/v2.0/{c.Connection.Account}/{env}/api/v2.0/companies({company})";
    }

    string FirstUrl(ErpCallContext c, ErpEntity e, ErpCursor cur)
    {
        if (c.Connection.Edition == FinanceSupplyChain)
        {
            var f = Fo[e];
            var area = c.Opt("dataAreaId");
            return $"{c.Base}/data/{f.Collection}" + (area.Length > 0 ? $"?cross-company=true&$filter={Uri.EscapeDataString($"dataAreaId eq '{area}'")}" : "");
        }
        var b = Bc[e];
        var q = new List<string>();
        if (b.Delta && cur.Watermark is { } w) q.Add("$filter=" + Uri.EscapeDataString($"lastModifiedDateTime gt {ErpWire.Iso(w)}"));
        if (b.Expand is not null) q.Add("$expand=" + b.Expand);
        return $"{BcRoot(c)}/{b.Set}" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }

    public async Task<string> TestAsync(ErpCallContext c, CancellationToken ct)
    {
        c.Http.ErrorParser = ParseError;
        var t = await TokenAsync(c, ct);
        var url = c.Connection.Edition == FinanceSupplyChain ? $"{c.Base}/data/ReleasedProductsV2?$top=1" : $"{BcRoot(c)}/items?$top=1";
        var n = await c.Http.JsonAsync(() => ErpWire.Get(url, ErpWire.Bearer(t)), ct);
        return $"Token issued; {(n["value"] as JsonArray)?.Count ?? 0} row(s) read.";
    }

    public async Task<ErpPage> ReadAsync(ErpCallContext c, ErpEntity entity, ErpCursor cursor, CancellationToken ct)
    {
        this.RequireRead(c.Connection.Edition, entity);
        c.Http.ErrorParser = ParseError;
        var t = await TokenAsync(c, ct);
        var url = cursor.PageToken ?? FirstUrl(c, entity, cursor);
        var pageSize = Math.Clamp(c.PageSize <= 0 ? 1000 : c.PageSize, 1, 20000);
        var n = await c.Http.JsonAsync(() => ErpWire.Get(url, ErpWire.Bearer(t), ("Prefer", $"odata.maxpagesize={pageSize}")), ct);
        var fo = c.Connection.Edition == FinanceSupplyChain;
        var rows = (n["value"] as JsonArray ?? []).OfType<JsonNode>()
            .Select(x => c.Record(this, entity, x, fo ? Fo[entity].Id : "id", fo ? null : (Bc[entity].Delta ? "lastModifiedDateTime" : null), fo ? "@odata.etag" : "@odata.etag")).ToList();
        return new ErpPage(rows, n["@odata.nextLink"]?.GetValue<string>());
    }
}
