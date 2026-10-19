using System.Text.Json.Nodes;
using QualGuardIQ.Domain.Integration;

namespace QualGuardIQ.Infrastructure.Modules.Integration.Erp;

/// <summary>
/// Business Central manufacturing data, separate from the generic Dynamics connector (which reads only the standard API v2.0).
/// Two sources, chosen per connection:
/// <list type="bullet">
/// <item>CustomApi: API pages from a manufacturing extension installed in the customer's tenant (/api/{publisher}/{group}/{version}/companies({id})/{entitySet}).</item>
/// <item>WebServices: standard BC pages the administrator publishes on the Web Services page (/ODataV4/Company('{name}')/{serviceName}). No extension needed.</item>
/// </list>
/// Nothing is assumed to exist: every entity set, key field, filter and field path is configurable in the connection options, only the
/// entities listed in "entities" are read, and the connection test probes each one and reports which mapped fields it could not find.
/// See docs/integrations/dynamics365-bc-manufacturing.md.
/// </summary>
public sealed class BcManufacturingAdapter : IErpAdapter
{
    public const string CustomApi = "CustomApi", WebServices = "WebServices";
    const string Doc = "docs/integrations/dynamics365-bc-manufacturing.md";
    public ErpSystem System => ErpSystem.BcManufacturing;
    public IReadOnlyList<string> Editions => [CustomApi, WebServices];

    /// <summary>Entities read when the connection does not list its own ("entities" option).</summary>
    public static readonly ErpEntity[] DefaultEntities = [ErpEntity.ProductionOrder, ErpEntity.ProductionOrderLine, ErpEntity.WorkCenter, ErpEntity.MachineCenter];

    public sealed record Spec(string Set, string Id, string? Filter, Dictionary<string, string> Map);

    // Suggested names only. Extension API pages are defined by whoever builds the extension; web service names are chosen when publishing.
    static readonly Dictionary<ErpEntity, Spec> Api = new()
    {
        [ErpEntity.ProductionOrder] = new("productionOrders", "id", "status eq 'Released'", new() { ["number"] = "number", ["item"] = "sourceNumber", ["quantity"] = "quantity", ["dueOn"] = "dueDate", ["status"] = "status", ["location"] = "locationCode" }),
        [ErpEntity.ProductionOrderLine] = new("productionOrderLines", "id", null, new() { ["order"] = "productionOrderNumber", ["lineNumber"] = "lineNumber", ["item"] = "itemNumber", ["quantity"] = "quantity", ["finishedQuantity"] = "finishedQuantity", ["remainingQuantity"] = "remainingQuantity", ["scrapPercent"] = "scrapPercent", ["status"] = "status" }),
        [ErpEntity.Routing] = new("productionOrderRoutingLines", "id", null, new() { ["order"] = "productionOrderNumber", ["operation"] = "operationNumber", ["type"] = "type", ["resource"] = "number", ["setupTime"] = "setupTime", ["runTime"] = "runTime", ["status"] = "routingStatus" }),
        [ErpEntity.WorkCenter] = new("workCenters", "id", null, new() { ["code"] = "number", ["name"] = "name" }),
        [ErpEntity.MachineCenter] = new("machineCenters", "id", null, new() { ["code"] = "number", ["name"] = "name", ["workCenter"] = "workCenterNumber" }),
        [ErpEntity.ItemLedgerEntry] = new("itemLedgerEntries", "entryNumber", null, new() { ["entryNumber"] = "entryNumber", ["item"] = "itemNumber", ["type"] = "entryType", ["quantity"] = "quantity", ["lot"] = "lotNumber", ["serial"] = "serialNumber", ["order"] = "orderNumber", ["postedOn"] = "postingDate" }),
        [ErpEntity.CapacityLedgerEntry] = new("capacityLedgerEntries", "entryNumber", null, new() { ["entryNumber"] = "entryNumber", ["order"] = "orderNumber", ["operation"] = "operationNumber", ["workCenter"] = "workCenterNumber", ["outputQuantity"] = "outputQuantity", ["scrapQuantity"] = "scrapQuantity", ["postedOn"] = "postingDate" }),
        [ErpEntity.OutputJournalLine] = new("outputJournalLines", "id", null, new() { ["order"] = "orderNumber", ["operation"] = "operationNumber", ["item"] = "itemNumber", ["outputQuantity"] = "outputQuantity", ["scrapQuantity"] = "scrapQuantity", ["postingDate"] = "postingDate" }),
        [ErpEntity.ConsumptionJournalLine] = new("consumptionJournalLines", "id", null, new() { ["order"] = "orderNumber", ["item"] = "itemNumber", ["quantity"] = "quantity", ["lot"] = "lotNumber", ["serial"] = "serialNumber", ["postingDate"] = "postingDate" }),
        [ErpEntity.Lot] = new("itemLedgerEntries", "entryNumber", "lotNumber ne ''", new() { ["number"] = "lotNumber", ["item"] = "itemNumber", ["expiresOn"] = "expirationDate" }),
        [ErpEntity.Serial] = new("itemLedgerEntries", "entryNumber", "serialNumber ne ''", new() { ["number"] = "serialNumber", ["item"] = "itemNumber", ["lot"] = "lotNumber" }),
    };

    static readonly Dictionary<ErpEntity, Spec> Ws = new()
    {
        [ErpEntity.ProductionOrder] = new("ReleasedProductionOrders", "No", null, new() { ["number"] = "No", ["item"] = "Source_No", ["quantity"] = "Quantity", ["dueOn"] = "Due_Date", ["status"] = "Status", ["location"] = "Location_Code" }),
        [ErpEntity.ProductionOrderLine] = new("ReleasedProdOrderLines", "Prod_Order_No,Line_No", null, new() { ["order"] = "Prod_Order_No", ["lineNumber"] = "Line_No", ["item"] = "Item_No", ["quantity"] = "Quantity", ["finishedQuantity"] = "Finished_Quantity", ["remainingQuantity"] = "Remaining_Quantity", ["scrapPercent"] = "Scrap_Percent" }),
        [ErpEntity.Routing] = new("ProdOrderRouting", "Prod_Order_No,Routing_Reference_No,Operation_No", null, new() { ["order"] = "Prod_Order_No", ["operation"] = "Operation_No", ["type"] = "Type", ["resource"] = "No", ["setupTime"] = "Setup_Time", ["runTime"] = "Run_Time", ["status"] = "Routing_Status" }),
        [ErpEntity.WorkCenter] = new("WorkCenters", "No", null, new() { ["code"] = "No", ["name"] = "Name" }),
        [ErpEntity.MachineCenter] = new("MachineCenters", "No", null, new() { ["code"] = "No", ["name"] = "Name", ["workCenter"] = "Work_Center_No" }),
        [ErpEntity.ItemLedgerEntry] = new("ItemLedgerEntries", "Entry_No", null, new() { ["entryNumber"] = "Entry_No", ["item"] = "Item_No", ["type"] = "Entry_Type", ["quantity"] = "Quantity", ["lot"] = "Lot_No", ["serial"] = "Serial_No", ["order"] = "Order_No", ["postedOn"] = "Posting_Date" }),
        [ErpEntity.CapacityLedgerEntry] = new("CapacityLedgerEntries", "Entry_No", null, new() { ["entryNumber"] = "Entry_No", ["order"] = "Order_No", ["operation"] = "Operation_No", ["workCenter"] = "Work_Center_No", ["outputQuantity"] = "Output_Quantity", ["scrapQuantity"] = "Scrap_Quantity", ["postedOn"] = "Posting_Date" }),
        [ErpEntity.OutputJournalLine] = new("OutputJournal", "Journal_Template_Name,Journal_Batch_Name,Line_No", null, new() { ["order"] = "Order_No", ["operation"] = "Operation_No", ["item"] = "Item_No", ["outputQuantity"] = "Output_Quantity", ["scrapQuantity"] = "Scrap_Quantity", ["postingDate"] = "Posting_Date" }),
        [ErpEntity.ConsumptionJournalLine] = new("ConsumptionJournal", "Journal_Template_Name,Journal_Batch_Name,Line_No", null, new() { ["order"] = "Order_No", ["item"] = "Item_No", ["quantity"] = "Quantity", ["lot"] = "Lot_No", ["serial"] = "Serial_No", ["postingDate"] = "Posting_Date" }),
        [ErpEntity.Lot] = new("ItemLedgerEntries", "Entry_No", "Lot_No ne ''", new() { ["number"] = "Lot_No", ["item"] = "Item_No", ["expiresOn"] = "Expiration_Date" }),
        [ErpEntity.Serial] = new("ItemLedgerEntries", "Entry_No", "Serial_No ne ''", new() { ["number"] = "Serial_No", ["item"] = "Item_No", ["lot"] = "Lot_No" }),
    };

    static Dictionary<ErpEntity, Spec> Defaults(string edition) => edition == WebServices ? Ws : Api;

    public ErpCapabilities Capabilities(string edition) => new(edition, [.. Defaults(edition).Keys], [], edition == CustomApi,
        "Entra ID client credentials, scope https://api.businesscentral.dynamics.com/.default; app enabled on the Microsoft Entra Applications page with read permission sets for the manufacturing tables",
        edition == WebServices ? "OData V4 web services /v2.0/{tenant}/{environment}/ODataV4/Company('{company name}')/{service name}" : "Extension API pages /v2.0/{tenant}/{environment}/api/{publisher}/{group}/{version}/companies({id})/{entitySet}",
        "@odata.nextLink continuation; Prefer odata.maxpagesize", "Per user: 5 concurrent, about 6000 requests / 5 min (429 with Retry-After), 10-minute request timeout",
        "None used", 2, 600, Doc);

    public IReadOnlyList<UnverifiedItem> Unverified(string edition) => edition == WebServices
        ?
        [
            new("Published pages", "The default service names (ReleasedProductionOrders, ReleasedProdOrderLines, ProdOrderRouting, WorkCenters, MachineCenters, ItemLedgerEntries, CapacityLedgerEntries, OutputJournal, ConsumptionJournal) are suggestions. Publish the pages you want under these names or set entitySet.<Entity> to the names you chose.", Doc),
            new("Field names", "OData field names come from page control names (e.g. Prod_Order_No, Finished_Quantity, Lot_No). Pages differ between BC versions and customizations; the connection test lists every mapped field it could not find.", Doc),
            new("Keys", "Line and journal keys are composite (idField.<Entity>, comma separated). Confirm them against $metadata.", Doc),
            new("Delta", "Web service pages have no reliable last-modified field; entities are read in full and unchanged rows are skipped by hash.", Doc),
        ]
        :
        [
            new("Extension", "Business Central's standard API v2.0 has no manufacturing entities. This edition needs an extension with API pages for each entity you enable; the publisher, group, version and entity set names come from that extension.", Doc),
            new("Field names", "Default field names are suggestions in BC API style (productionOrderNumber, finishedQuantity, lotNumber). Set field maps to the extension's names; the connection test lists missing fields.", Doc),
            new("Delta", "Delta reads use modifiedField (default lastModifiedDateTime) only if the API page exposes it; otherwise clear modifiedField to read in full.", Doc),
        ];

    public IReadOnlyDictionary<string, string> DefaultMap(string edition, ErpEntity entity) => Defaults(edition).TryGetValue(entity, out var s) ? s.Map : new();

    /// <summary>The entity's effective settings after connection options (entitySet.X, idField.X, filter.X; filter.X = "" removes the default filter).</summary>
    public static Spec Effective(ErpCallContext c, ErpEntity e)
    {
        var d = Defaults(c.Connection.Edition).GetValueOrDefault(e) ?? throw new ErpException($"Business Central manufacturing has no read for {e}.");
        var filter = c.Options[$"filter.{e}"] is JsonValue fv ? fv.ToString() : d.Filter;
        return d with { Set = c.Opt($"entitySet.{e}", d.Set), Id = c.Opt($"idField.{e}", d.Id), Filter = string.IsNullOrWhiteSpace(filter) ? null : filter };
    }

    public static IReadOnlyList<ErpEntity> Enabled(ErpCallContext c)
    {
        if (c.Options["entities"] is not JsonArray arr) return DefaultEntities;
        var list = new List<ErpEntity>();
        foreach (var n in arr)
            if (Enum.TryParse<ErpEntity>(n?.ToString(), true, out var e) && Defaults(c.Connection.Edition).ContainsKey(e)) list.Add(e);
            else throw new ErpException($"Unknown or unsupported entity '{n}' in the entities option.");
        return list;
    }

    static async Task<string> TokenAsync(ErpCallContext c, CancellationToken ct)
    {
        var tenant = c.Connection.Account ?? throw new ErpException("Set the Entra tenant id.");
        var authority = c.Opt("authority", "https://login.microsoftonline.com");
        return await c.Tokens.GetAsync(c.Connection.Id, "entra", async () => ErpWire.Token(await c.Http.JsonAsync(() => ErpWire.Form($"{authority}/{tenant}/oauth2/v2.0/token",
            [new("grant_type", "client_credentials"), new("client_id", c.Connection.ClientId ?? ""), new("client_secret", c.RequireSecret("client secret")), new("scope", "https://api.businesscentral.dynamics.com/.default")]), ct)), c.Now);
    }

    public static string Root(ErpCallContext c)
    {
        var env = c.Opt("environment", "production");
        var company = c.Connection.Company ?? throw new ErpException(c.Connection.Edition == WebServices ? "Set the company name." : "Set the company id.");
        var host = string.IsNullOrWhiteSpace(c.Connection.BaseUrl) ? "https://api.businesscentral.dynamics.com" : c.Base;
        if (c.Connection.Edition == WebServices) return $"{host}/v2.0/{c.Connection.Account}/{env}/ODataV4/Company('{Uri.EscapeDataString(company.Replace("'", "''"))}')";
        var publisher = c.Opt("apiPublisher"); var group = c.Opt("apiGroup"); var version = c.Opt("apiVersion", "v1.0");
        if (publisher.Length == 0 || group.Length == 0) throw new ErpException("Set apiPublisher and apiGroup to the values of the installed manufacturing extension's API pages.");
        return $"{host}/v2.0/{c.Connection.Account}/{env}/api/{publisher}/{group}/{version}/companies({company})";
    }

    static string Url(ErpCallContext c, Spec s, DateTimeOffset? since, int? top)
    {
        var filters = new List<string>();
        if (s.Filter is not null) filters.Add($"({s.Filter})");
        var modified = ModifiedField(c);
        if (since is { } w && modified.Length > 0) filters.Add($"{modified} gt {ErpWire.Iso(w)}");
        var q = new List<string>();
        if (filters.Count > 0) q.Add("$filter=" + Uri.EscapeDataString(string.Join(" and ", filters)));
        if (top is { } t) q.Add("$top=" + t);
        return $"{Root(c)}/{s.Set}" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }

    /// <summary>Delta field; an explicit empty value turns delta reads off (full read, unchanged rows skipped by hash).</summary>
    static string ModifiedField(ErpCallContext c) => c.Options["modifiedField"] is JsonValue v ? v.ToString().Trim() : c.Connection.Edition == CustomApi ? "lastModifiedDateTime" : "";

    static string? Id(JsonNode row, string idSpec)
    {
        var parts = idSpec.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(p => ErpWire.Str(row, p)).ToList();
        return parts.Any(string.IsNullOrWhiteSpace) ? null : string.Join("|", parts);
    }

    /// <summary>
    /// Authenticates, then probes every enabled entity with one row: reachable or not (with the likely cause), and which mapped fields
    /// were not present in the sample row. Passes when authentication works and every enabled entity answers.
    /// </summary>
    public async Task<string> TestAsync(ErpCallContext c, CancellationToken ct)
    {
        c.Http.ErrorParser = DynamicsAdapter.ParseError;
        var t = await TokenAsync(c, ct);
        var lines = new List<string> { $"Token issued ({c.Connection.Edition})." };
        var failed = new List<string>();
        foreach (var e in Enabled(c))
        {
            var s = Effective(c, e);
            var (r, body) = await c.Http.SendAsync(() => ErpWire.Get(Url(c, s, null, 1), ErpWire.Bearer(t)), ct, throwOnError: false);
            if (!r.IsSuccessStatusCode)
            {
                var why = (int)r.StatusCode switch
                {
                    404 => c.Connection.Edition == WebServices ? $"'{s.Set}' is not published on the Web Services page (or the company name is wrong)" : $"'{s.Set}' was not found: the extension API page is not installed or apiPublisher/apiGroup/apiVersion differ",
                    401 or 403 => "the Entra application lacks a permission set that reads this table",
                    400 => "the filter or a field name is not valid for this page: " + (DynamicsAdapter.ParseError(r, body) ?? ErpHttp.Trim(body)),
                    _ => $"{(int)r.StatusCode} {DynamicsAdapter.ParseError(r, body) ?? ErpHttp.Trim(body)}",
                };
                lines.Add($"{e}: FAILED, {why}."); failed.Add(e.ToString());
                continue;
            }
            var row = (JsonNode.Parse(body)?["value"] as JsonArray)?.OfType<JsonNode>().FirstOrDefault();
            if (row is null) { lines.Add($"{e}: reachable, no rows to check field names against (fields unverified)."); continue; }
            var map = new Dictionary<string, string>(DefaultMap(c.Connection.Edition, e));
            foreach (var (k, v) in c.FieldMap) map[k] = v;
            var missing = map.Where(kv => !kv.Key.StartsWith("lines", StringComparison.Ordinal) && ErpWire.Path(row, kv.Value) is null && !(row is JsonObject o && o.ContainsKey(kv.Value))).Select(kv => $"{kv.Key}←{kv.Value}").ToList();
            var idOk = Id(row, s.Id) is not null;
            lines.Add($"{e}: OK" + (idOk ? "" : $", key field(s) '{s.Id}' not found") + (missing.Count == 0 ? ", all mapped fields present." : $", unverified fields (not in the sample row): {string.Join(", ", missing)}."));
            if (!idOk) failed.Add(e.ToString());
        }
        if (failed.Count > 0) throw new ErpException(string.Join(" ", lines) + $" Fix: {string.Join(", ", failed)}.") { Summary = $"Signed in OK, but these could not be read: {string.Join(", ", failed)}" };
        return string.Join(" ", lines);
    }

    public async Task<ErpPage> ReadAsync(ErpCallContext c, ErpEntity entity, ErpCursor cursor, CancellationToken ct)
    {
        this.RequireRead(c.Connection.Edition, entity);
        if (!Enabled(c).Contains(entity)) throw new ErpException($"{entity} is not in this connection's entities option; add it and run Test connection again.");
        c.Http.ErrorParser = DynamicsAdapter.ParseError;
        var t = await TokenAsync(c, ct);
        var s = Effective(c, entity);
        var url = cursor.PageToken ?? Url(c, s, cursor.Watermark, null);
        var pageSize = Math.Clamp(c.PageSize <= 0 ? 1000 : c.PageSize, 1, 20000);
        var n = await c.Http.JsonAsync(() => ErpWire.Get(url, ErpWire.Bearer(t), ("Prefer", $"odata.maxpagesize={pageSize}")), ct);
        var modified = ModifiedField(c);
        var rows = new List<ExternalRecord>();
        foreach (var row in (n["value"] as JsonArray ?? []).OfType<JsonNode>())
        {
            var id = Id(row, s.Id) ?? throw new ErpException($"A {entity} row has no key at '{s.Id}'. Set idField.{entity}.");
            var fields = ErpWire.Map(row, DefaultMap(c.Connection.Edition, entity), c.FieldMap);
            rows.Add(new ExternalRecord(id, ErpWire.Str(row, "@odata.etag"), modified.Length > 0 ? ErpWire.Date(row, modified) : null, fields, row.ToJsonString()));
        }
        return new ErpPage(rows, n["@odata.nextLink"]?.GetValue<string>());
    }
}
