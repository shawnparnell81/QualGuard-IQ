using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using QualGuardIQ.Domain.Integration;

namespace QualGuardIQ.Infrastructure.Modules.Integration.Erp;

/// <summary>
/// SAP: S/4HANA Cloud / on-prem A2X OData V2 services (communication user, Basic or OAuth client credentials) and
/// Business One Service Layer (B1SESSION login, Prefer: odata.maxpagesize, odata.nextLink). See docs/integrations/sap.md.
/// </summary>
public sealed class SapAdapter : IErpAdapter
{
    public const string S4Cloud = "S4HanaCloud", S4OnPrem = "S4HanaOnPrem", BusinessOne = "BusinessOne";
    public ErpSystem System => ErpSystem.Sap;
    public IReadOnlyList<string> Editions => [S4Cloud, S4OnPrem, BusinessOne];
    const string Doc = "docs/integrations/sap.md";

    static readonly Dictionary<ErpEntity, (string Service, string Set, string Id, string? Modified, string? Expand, Dictionary<string, string> Map)> S4 = new()
    {
        [ErpEntity.Item] = ("API_PRODUCT_SRV", "A_Product", "Product", "LastChangeDateTime", null, new() { ["number"] = "Product", ["type"] = "ProductType", ["uom"] = "BaseUnit" }),
        [ErpEntity.Customer] = ("API_BUSINESS_PARTNER", "A_Customer", "Customer", null, null, new() { ["code"] = "Customer", ["name"] = "CustomerName" }),
        [ErpEntity.Supplier] = ("API_BUSINESS_PARTNER", "A_Supplier", "Supplier", null, null, new() { ["code"] = "Supplier", ["name"] = "SupplierName" }),
        [ErpEntity.PurchaseOrder] = ("API_PURCHASEORDER_PROCESS_SRV", "A_PurchaseOrder", "PurchaseOrder", null, "to_PurchaseOrderItem",
            new() { ["number"] = "PurchaseOrder", ["supplierCode"] = "Supplier", ["orderedOn"] = "PurchaseOrderDate", ["lines"] = "to_PurchaseOrderItem.results",
                ["lines.lineNumber"] = "PurchaseOrderItem", ["lines.item"] = "Material", ["lines.quantity"] = "OrderQuantity", ["lines.uom"] = "PurchaseOrderQuantityUnit" }),
        [ErpEntity.ProductionOrder] = ("API_PRODUCTION_ORDER_2_SRV", "A_ProductionOrder_2", "ManufacturingOrder", null, null,
            new() { ["number"] = "ManufacturingOrder", ["item"] = "Material", ["quantity"] = "TotalQuantity", ["dueOn"] = "MfgOrderPlannedEndDate" }),
        [ErpEntity.Bom] = ("API_BILL_OF_MATERIAL_SRV;v=0002", "MaterialBOM", "BillOfMaterial", null, "to_BillOfMaterialItem",
            new() { ["parent"] = "Material", ["lines"] = "to_BillOfMaterialItem.results", ["lines.component"] = "BillOfMaterialComponent", ["lines.quantity"] = "BillOfMaterialItemQuantity" }),
        [ErpEntity.Lot] = ("API_BATCH_SRV", "Batch", "Batch", null, null, new() { ["number"] = "Batch", ["item"] = "Material" }),
    };

    static readonly Dictionary<ErpEntity, (string Set, string Id, string? Filter, Dictionary<string, string> Map)> B1 = new()
    {
        [ErpEntity.Item] = ("Items", "ItemCode", null, new() { ["number"] = "ItemCode", ["name"] = "ItemName" }),
        [ErpEntity.Customer] = ("BusinessPartners", "CardCode", "CardType eq 'cCustomer'", new() { ["code"] = "CardCode", ["name"] = "CardName", ["email"] = "EmailAddress" }),
        [ErpEntity.Supplier] = ("BusinessPartners", "CardCode", "CardType eq 'cSupplier'", new() { ["code"] = "CardCode", ["name"] = "CardName", ["email"] = "EmailAddress" }),
        [ErpEntity.PurchaseOrder] = ("PurchaseOrders", "DocEntry", null, new() { ["number"] = "DocNum", ["supplierCode"] = "CardCode", ["orderedOn"] = "DocDate",
            ["lines"] = "DocumentLines", ["lines.lineNumber"] = "LineNum", ["lines.item"] = "ItemCode", ["lines.quantity"] = "Quantity", ["lines.dueOn"] = "ShipDate" }),
        [ErpEntity.ProductionOrder] = ("ProductionOrders", "AbsoluteEntry", null, new() { ["number"] = "DocumentNumber", ["item"] = "ItemNo", ["quantity"] = "PlannedQuantity", ["dueOn"] = "DueDate" }),
    };

    public ErpCapabilities Capabilities(string edition) => edition == BusinessOne
        ? new(edition, [.. B1.Keys], [], false, "Session login POST /b1s/v1/Login (CompanyDB, UserName, Password) → B1SESSION + ROUTEID cookies, 30-minute timeout", "Service Layer OData (v1 = OData V3, v2 = OData V4)",
            "Default page 20; Prefer: odata.maxpagesize=N; follow odata.nextLink / @odata.nextLink", "None published", "EventSubscription webhooks in newer B1 versions (not used)", 1, 0, Doc)
        : new(edition, [.. S4.Keys], [], true, "Communication arrangement user: Basic, or OAuth 2.0 client credentials / X.509 by arrangement", "A2X OData V2 /sap/opu/odata/sap/<SERVICE>",
            "$top/$skip with $format=json; d.__next when the server pages", "None published", "Event Mesh business events need entitlement (not used)", 2, 0, Doc);

    public IReadOnlyList<UnverifiedItem> Unverified(string edition) => edition == BusinessOne
        ?
        [
            new("Entities", "ProductTrees, PurchaseDeliveryNotes, BatchNumberDetails, SerialNumberDetails, EmployeesInfo and Resources are not named in the guide; read GET /b1s/v1/$metadata on the customer server.", Doc),
            new("Field names", "ItemName, CardName, EmailAddress, DocNum, DocDate, DocumentLines, ShipDate, DocumentNumber, ItemNo, PlannedQuantity, DueDate are taken from common B1 usage, not the guide. Confirm in $metadata.", Doc),
            new("Delta", "UpdateDate/UpdateTime filtering is not confirmed, so the adapter reads in full and skips unchanged rows by hash.", Doc),
            new("Sandbox", "No vendor sandbox: use a customer test company database.", Doc),
        ]
        :
        [
            new("Communication scenarios", "SAP_COM_0009 (product) and SAP_COM_0008 (business partner) are cited; SAP_COM_0105 BOM, 0104 production order, 0053 PO, 0108 material document, 0337 batch and 0318 inspection lot are from SDK/community material (api.sap.com is login-gated).", Doc),
            new("Entity and field names", "A_Customer, A_Supplier, A_PurchaseOrder/A_PurchaseOrderItem, A_ProductionOrder_2, MaterialBOM, Batch and their fields are not confirmed on api.sap.com. Check each service $metadata.", Doc),
            new("Base URL", "https://<tenant>-api.s4hana.ondemand.com/sap/opu/odata/sap/<SERVICE> comes from a community post; use the URL shown in the communication arrangement.", Doc),
            new("Delta", "LastChangeDateTime filtering is used on A_Product only; other entities read in full and skip unchanged rows by hash.", Doc),
            new("Error format", "OData V2 error {error:{code,message:{value}}} is not cited from an official page.", Doc),
            new("Rate limits", "None published; default concurrency 2.", Doc),
            new("PO service", "API_PURCHASEORDER_PROCESS_SRV is deprecated in favour of OData V4 API_PURCHASEORDER_2; switch with option poService when the tenant offers V4.", Doc),
        ];

    public IReadOnlyDictionary<string, string> DefaultMap(string edition, ErpEntity entity) =>
        edition == BusinessOne ? (B1.TryGetValue(entity, out var b) ? b.Map : new()) : (S4.TryGetValue(entity, out var s) ? s.Map : new());

    public static string? ParseError(HttpResponseMessage r, string body)
    {
        try
        {
            var e = JsonNode.Parse(body)?["error"];
            return e is null ? null : $"{e["code"]}: {e["message"]?["value"] ?? e["message"]}";
        }
        catch (JsonException) { return null; }
    }

    // ---------- S/4HANA OData V2
    async Task<System.Net.Http.Headers.AuthenticationHeaderValue> S4AuthAsync(ErpCallContext c, CancellationToken ct)
    {
        if (c.Opt("auth", "basic") == "oauth")
        {
            var token = await c.Tokens.GetAsync(c.Connection.Id, "sap", async () => ErpWire.Token(await c.Http.JsonAsync(() => ErpWire.Form(c.Opt("tokenUrl"), [new("grant_type", "client_credentials")],
                ErpWire.Basic(c.Connection.ClientId ?? "", c.RequireSecret("client secret"))), ct)), c.Now);
            return ErpWire.Bearer(token);
        }
        return ErpWire.Basic(c.Connection.Username ?? throw new ErpException("Set the communication user."), c.RequireSecret("communication user password"));
    }

    static string S4Url(ErpCallContext c, ErpEntity e, ErpCursor cur, int top)
    {
        var d = S4[e];
        var service = e == ErpEntity.PurchaseOrder ? c.Opt("poService", d.Service) : d.Service;
        var skip = int.TryParse(cur.PageToken, out var s) ? s : 0;
        var q = $"$format=json&$top={top}&$skip={skip}";
        if (d.Modified is not null && cur.Watermark is { } w) q += $"&$filter={Uri.EscapeDataString($"{d.Modified} gt datetimeoffset'{ErpWire.Iso(w)}'")}";
        if (d.Expand is not null) q += "&$expand=" + d.Expand;
        return $"{c.Base}/sap/opu/odata/sap/{service}/{d.Set}?{q}";
    }

    // ---------- Business One Service Layer
    string B1Root(ErpCallContext c) => $"{c.Base}/b1s/{c.Opt("version", "v1")}";

    async Task<string> B1SessionAsync(ErpCallContext c, CancellationToken ct) => await c.Tokens.GetAsync(c.Connection.Id, "b1", async () =>
    {
        var (r, body) = await c.Http.SendAsync(() => ErpWire.PostJson(B1Root(c) + "/Login",
            new { CompanyDB = c.Connection.Company ?? throw new ErpException("Set the CompanyDB."), UserName = c.Connection.Username ?? throw new ErpException("Set the user name."), Password = c.RequireSecret("password") }), ct);
        var cookies = r.Headers.TryGetValues("Set-Cookie", out var v) ? v.ToList() : [];
        string? Cookie(string name) => cookies.Select(x => x.Split(';')[0]).FirstOrDefault(x => x.StartsWith(name + "="));
        var session = Cookie("B1SESSION") ?? throw new ErpException("Login returned no B1SESSION cookie.");
        var route = Cookie("ROUTEID");
        var minutes = JsonNode.Parse(body)?["SessionTimeout"] is JsonValue t && t.TryGetValue<int>(out var m) ? m : 30;
        return (route is null ? session : session + "; " + route, TimeSpan.FromMinutes(minutes));
    }, c.Now);

    async Task<JsonNode> B1GetAsync(ErpCallContext c, string url, int pageSize, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var cookie = await B1SessionAsync(c, ct);
            var (r, body) = await c.Http.SendAsync(() => ErpWire.Get(url, null, ("Cookie", cookie), ("Prefer", $"odata.maxpagesize={pageSize}")), ct, throwOnError: false);
            // 401 with code 301 = invalid or timed-out session: log in again once.
            if (r.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) { c.Tokens.Invalidate(c.Connection.Id, "b1"); continue; }
            if (!r.IsSuccessStatusCode) throw new ErpException($"{(int)r.StatusCode}: {ParseError(r, body) ?? ErpHttp.Trim(body)}", (int)r.StatusCode);
            return JsonNode.Parse(body)!;
        }
    }

    public async Task<string> TestAsync(ErpCallContext c, CancellationToken ct)
    {
        c.Http.ErrorParser = ParseError;
        if (c.Connection.Edition == BusinessOne)
        {
            var n = await B1GetAsync(c, B1Root(c) + "/Items?$select=ItemCode", 1, ct);
            return $"Session opened; Items returned {(n["value"] as JsonArray)?.Count ?? 0} row(s).";
        }
        var auth = await S4AuthAsync(c, ct);
        var p = await c.Http.JsonAsync(() => ErpWire.Get(S4Url(c, ErpEntity.Item, new(null, null), 1), auth), ct);
        return $"Authenticated; A_Product returned {(p["d"]?["results"] as JsonArray)?.Count ?? 0} row(s).";
    }

    public async Task<ErpPage> ReadAsync(ErpCallContext c, ErpEntity entity, ErpCursor cursor, CancellationToken ct)
    {
        this.RequireRead(c.Connection.Edition, entity);
        c.Http.ErrorParser = ParseError;
        if (c.Connection.Edition == BusinessOne)
        {
            var d = B1[entity];
            var url = cursor.PageToken ?? $"{B1Root(c)}/{d.Set}" + (d.Filter is null ? "" : "?$filter=" + Uri.EscapeDataString(d.Filter));
            if (!url.StartsWith("http")) url = $"{c.Base}{(url.StartsWith('/') ? "" : "/b1s/" + c.Opt("version", "v1") + "/")}{url}";
            var n = await B1GetAsync(c, url, c.PageSize <= 0 ? 100 : c.PageSize, ct);
            var rows = (n["value"] as JsonArray ?? []).OfType<JsonNode>().Select(x => c.Record(this, entity, x, d.Id, null)).ToList();
            var next = (n["odata.nextLink"] ?? n["@odata.nextLink"])?.GetValue<string>();
            return new ErpPage(rows, next);
        }
        var top = c.PageSize <= 0 ? 500 : c.PageSize;
        var auth = await S4AuthAsync(c, ct);
        var page = await c.Http.JsonAsync(() => ErpWire.Get(S4Url(c, entity, cursor, top), auth), ct);
        var s4 = S4[entity];
        var list = (page["d"]?["results"] as JsonArray ?? []).OfType<JsonNode>().Select(x => c.Record(this, entity, x, s4.Id, s4.Modified)).ToList();
        var skip = (int.TryParse(cursor.PageToken, out var sk) ? sk : 0) + list.Count;
        return new ErpPage(list, list.Count == top || page["d"]?["__next"] is not null ? skip.ToString() : null);
    }
}
