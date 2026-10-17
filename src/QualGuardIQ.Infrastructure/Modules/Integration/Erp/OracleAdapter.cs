using System.Text.Json;
using System.Text.Json.Nodes;
using QualGuardIQ.Domain.Integration;

namespace QualGuardIQ.Infrastructure.Modules.Integration.Erp;

/// <summary>
/// Oracle Fusion Cloud SCM REST (fscmRestApi 11.13.18.05). OAuth 2.0 client credentials from the Fusion IAM domain
/// confidential application (or Basic over HTTPS); collections page with limit/offset/hasMore. See docs/integrations/oracle.md.
/// </summary>
public sealed class OracleAdapter : IErpAdapter
{
    public ErpSystem System => ErpSystem.Oracle;
    public IReadOnlyList<string> Editions => ["FusionScm"];
    const string Doc = "docs/integrations/oracle.md";
    const string Version = "11.13.18.05";

    static readonly Dictionary<ErpEntity, (string Api, string Resource, string Id, Dictionary<string, string> Map)> R = new()
    {
        [ErpEntity.Item] = ("fscmRestApi", "itemsV2", "ItemId", new() { ["number"] = "ItemNumber", ["name"] = "ItemDescription", ["uom"] = "PrimaryUOMValue" }),
        [ErpEntity.Bom] = ("fscmRestApi", "itemStructures", "BillSequenceId", new() { ["parent"] = "ItemNumber" }),
        [ErpEntity.WorkCenter] = ("fscmRestApi", "workCenters", "WorkCenterId", new() { ["code"] = "WorkCenterCode", ["name"] = "WorkCenterName" }),
        [ErpEntity.ProductionOrder] = ("fscmRestApi", "workOrders", "WorkOrderId", new() { ["number"] = "WorkOrderNumber", ["item"] = "ItemNumber", ["quantity"] = "PlannedStartQuantity", ["dueOn"] = "PlannedCompletionDate", ["status"] = "WorkOrderStatusCode" }),
        [ErpEntity.Supplier] = ("fscmRestApi", "suppliers", "SupplierId", new() { ["code"] = "SupplierNumber", ["name"] = "Supplier" }),
        [ErpEntity.PurchaseOrder] = ("fscmRestApi", "purchaseOrders", "POHeaderId", new() { ["number"] = "OrderNumber", ["supplierCode"] = "SupplierNumber", ["orderedOn"] = "CreationDate" }),
        [ErpEntity.Lot] = ("fscmRestApi", "inventoryItemLots", "LotNumber", new() { ["number"] = "LotNumber", ["item"] = "ItemNumber" }),
        [ErpEntity.Serial] = ("fscmRestApi", "inventoryItemSerialNumbers", "SerialNumber", new() { ["number"] = "SerialNumber", ["item"] = "ItemNumber" }),
        [ErpEntity.Employee] = ("hcmRestApi", "publicWorkers", "PersonId", new() { ["number"] = "PersonNumber", ["name"] = "DisplayName" }),
    };

    public ErpCapabilities Capabilities(string edition) => new(edition, [.. R.Keys], [], true,
        "OAuth 2.0 client credentials (or JWT assertion) from a Fusion IAM confidential application with the Fusion Applications scope; Basic over HTTPS also supported",
        "/fscmRestApi/resources/11.13.18.05/<resource> (employees: /hcmRestApi/...)", "limit (default 25) + offset; hasMore; q=LastUpdateDate > '…' where queryable",
        "None published", "Business events through Oracle Integration (not used)", 2, 0, Doc);

    public IReadOnlyList<UnverifiedItem> Unverified(string edition) =>
    [
        new("Pod host", "The host is customer-specific; use the customer's Fusion URL as Base URL.", Doc),
        new("Roles", "Integration user job/duty role codes depend on the customer's role design; check each endpoint's \"Required roles\".", Doc),
        new("Field names", "ItemId/ItemNumber/ItemDescription/PrimaryUOMValue, WorkOrderNumber, SupplierNumber, OrderNumber and similar row fields are not confirmed from each resource page; LastUpdateDate is documented.", Doc),
        new("Item revisions", "The itemsV2 revisions child resource name is not confirmed.", Doc),
        new("Customers", "Customers live in the CX/TCA REST pillar (accounts / hubOrganizations), not the SCM index, so Customer is not read by this adapter.", Doc),
        new("Delta", "q=LastUpdateDate filtering per resource is not confirmed; the adapter sends it and also skips unchanged rows by hash.", Doc),
        new("Max page", "A 500-row maximum limit is not on an official page; the adapter uses 200.", Doc),
        new("Token client auth", "The token endpoint is <IDCS>/oauth2/v1/token; sending the client id/secret as HTTP Basic is assumed.", Doc),
        new("Errors / limits / sandbox", "Error body format, rate limits, and test pods are not documented publicly.", Doc),
    ];

    public IReadOnlyDictionary<string, string> DefaultMap(string edition, ErpEntity entity) => R.TryGetValue(entity, out var r) ? r.Map : new();

    async Task<System.Net.Http.Headers.AuthenticationHeaderValue> AuthAsync(ErpCallContext c, CancellationToken ct)
    {
        if (c.Opt("auth", "oauth") == "basic")
            return ErpWire.Basic(c.Connection.Username ?? throw new ErpException("Set the integration user."), c.RequireSecret("password"));
        var tokenUrl = c.Opt("tokenUrl");
        if (tokenUrl.Length == 0) throw new ErpException("Set the IAM domain token URL (<IDCS>/oauth2/v1/token).");
        var t = await c.Tokens.GetAsync(c.Connection.Id, "oracle", async () => ErpWire.Token(await c.Http.JsonAsync(() => ErpWire.Form(tokenUrl,
            [new("grant_type", "client_credentials"), new("scope", c.Opt("scope"))], ErpWire.Basic(c.Connection.ClientId ?? "", c.RequireSecret("client secret"))), ct)), c.Now);
        return ErpWire.Bearer(t);
    }

    public static string Url(ErpCallContext c, ErpEntity e, ErpCursor cur, int limit)
    {
        var r = R[e];
        var offset = int.TryParse(cur.PageToken, out var o) ? o : 0;
        var q = $"limit={limit}&offset={offset}&totalResults=false";
        if (cur.Watermark is { } w) q += "&q=" + Uri.EscapeDataString($"LastUpdateDate > '{ErpWire.Iso(w)}'");
        return $"{c.Base}/{r.Api}/resources/{Version}/{r.Resource}?{q}";
    }

    public async Task<string> TestAsync(ErpCallContext c, CancellationToken ct)
    {
        var auth = await AuthAsync(c, ct);
        var n = await c.Http.JsonAsync(() => ErpWire.Get(Url(c, ErpEntity.Item, new(null, null), 1), auth), ct);
        return $"Authenticated; itemsV2 returned {(n["items"] as JsonArray)?.Count ?? 0} row(s).";
    }

    public async Task<ErpPage> ReadAsync(ErpCallContext c, ErpEntity entity, ErpCursor cursor, CancellationToken ct)
    {
        this.RequireRead(c.Connection.Edition, entity);
        var limit = Math.Clamp(c.PageSize <= 0 ? 200 : c.PageSize, 1, 500);
        var auth = await AuthAsync(c, ct);
        var n = await c.Http.JsonAsync(() => ErpWire.Get(Url(c, entity, cursor, limit), auth), ct);
        var rows = (n["items"] as JsonArray ?? []).OfType<JsonNode>().Select(x => c.Record(this, entity, x, R[entity].Id, "LastUpdateDate")).ToList();
        var offset = (int.TryParse(cursor.PageToken, out var o) ? o : 0) + rows.Count;
        return new ErpPage(rows, n["hasMore"]?.GetValue<bool>() == true ? offset.ToString() : null);
    }
}
