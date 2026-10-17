using System.Text.Json;
using System.Text.Json.Nodes;
using QualGuardIQ.Domain.Integration;

namespace QualGuardIQ.Infrastructure.Modules.Integration.Erp;

/// <summary>
/// Plex (Rockwell) Connect REST: static subscription key in X-Plex-Connect-Api-Key, optional X-Plex-Connect-Tenant-Id
/// for multi-PCN access; https://connect.plex.com (production) or https://test.connect.plex.com (test, separate key).
/// See docs/integrations/plex.md.
/// </summary>
public sealed class PlexAdapter : IErpAdapter
{
    public ErpSystem System => ErpSystem.Plex;
    public IReadOnlyList<string> Editions => ["Connect"];
    const string Doc = "docs/integrations/plex.md";

    static readonly Dictionary<ErpEntity, (string Path, Dictionary<string, string> Map)> P = new()
    {
        [ErpEntity.Item] = ("mdm/v1/parts", new() { ["number"] = "number", ["name"] = "name", ["revision"] = "revision" }),
        [ErpEntity.Supplier] = ("mdm/v1/suppliers", new() { ["code"] = "code", ["name"] = "name" }),
        [ErpEntity.Customer] = ("mdm/v1/customers", new() { ["code"] = "code", ["name"] = "name" }),
        [ErpEntity.Employee] = ("mdm/v1/employees", new() { ["number"] = "employeeNo", ["name"] = "name" }),
        [ErpEntity.ProductionOrder] = ("production/v1/scheduling/jobs", new() { ["number"] = "jobNo", ["item"] = "partNumber", ["quantity"] = "quantity", ["dueOn"] = "dueDate" }),
        [ErpEntity.PurchaseOrder] = ("purchasing/v1/purchase-orders", new() { ["number"] = "poNo", ["supplierCode"] = "supplierCode" }),
        [ErpEntity.Lot] = ("inventory/v1/inventory-tracking/containers", new() { ["number"] = "serialNo", ["item"] = "partNumber" }),
    };

    public ErpCapabilities Capabilities(string edition) => new(edition, [.. P.Keys], [], false,
        "Static API key in X-Plex-Connect-Api-Key (no OAuth); 401 when missing, invalid or expired", "https://connect.plex.com/{collection}/{version}/{resource}",
        "List query parameters per API product (names not public); one page per call", "About 200 calls/min reported by the community", "None documented", 1, 100, Doc);

    public IReadOnlyList<UnverifiedItem> Unverified(string edition) =>
    [
        new("Paths", "Every collection path (mdm/v1/parts, …/suppliers, …/customers, …/employees, production/v1/scheduling/jobs, purchasing/v1/purchase-orders, inventory/v1/inventory-tracking/containers) needs confirming in the developer portal after subscribing.", Doc),
        new("Fields and paging", "Response fields (id, number, name …) and list query parameters are not public; set field maps and option query.<Entity> after checking the portal.", Doc),
        new("Customer Id header", "The getstarted page names a Customer Id header; its exact name is not captured, so only X-Plex-Connect-Tenant-Id is sent.", Doc),
        new("Rate limit", "200 calls/min is a community figure; the connection defaults to 100/min.", Doc),
        new("Setup steps", "Developer portal access needs Plex IAM and a support case; step wording is not confirmed.", Doc),
    ];

    public IReadOnlyDictionary<string, string> DefaultMap(string edition, ErpEntity entity) => P.TryGetValue(entity, out var p) ? p.Map : new();

    public async Task<string> TestAsync(ErpCallContext c, CancellationToken ct)
    {
        var page = await ReadAsync(c, ErpEntity.Item, new(null, null), ct);
        return $"API key accepted; parts returned {page.Records.Count} row(s).";
    }

    public async Task<ErpPage> ReadAsync(ErpCallContext c, ErpEntity entity, ErpCursor cursor, CancellationToken ct)
    {
        this.RequireRead(c.Connection.Edition, entity);
        var key = c.RequireSecret("Plex Connect API key");
        var root = string.IsNullOrWhiteSpace(c.Connection.BaseUrl) ? "https://connect.plex.com" : c.Base;
        var query = c.Opt("query." + entity);
        var headers = new List<(string, string)> { ("X-Plex-Connect-Api-Key", key) };
        if (!string.IsNullOrWhiteSpace(c.Connection.Account)) headers.Add(("X-Plex-Connect-Tenant-Id", c.Connection.Account!));
        var n = await c.Http.JsonAsync(() => ErpWire.Get($"{root}/{P[entity].Path}{(query.Length > 0 ? "?" + query : "")}", null, [.. headers]), ct);
        var arr = n as JsonArray ?? n["data"] as JsonArray ?? [];
        return new ErpPage(arr.OfType<JsonNode>().Select(x => c.Record(this, entity, x, c.Opt("idField", "id"), null)).ToList(), null);
    }
}
