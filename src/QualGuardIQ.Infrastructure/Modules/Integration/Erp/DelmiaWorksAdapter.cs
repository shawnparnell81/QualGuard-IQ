using System.Text.Json.Nodes;
using QualGuardIQ.Domain.Integration;

namespace QualGuardIQ.Infrastructure.Modules.Integration.Erp;

/// <summary>
/// IQMS / DELMIAworks WebAPI (separately licensed). There is no public API reference: the contract ships in the SDK
/// help file, so every endpoint here is a configurable option and the whole adapter is marked unverified. Customers is
/// the only path cited (IQMS whitepaper). CSV exchange is the documented fallback. See docs/integrations/iqms-delmiaworks.md.
/// </summary>
public sealed class DelmiaWorksAdapter : IErpAdapter
{
    public ErpSystem System => ErpSystem.DelmiaWorks;
    public IReadOnlyList<string> Editions => ["WebApi"];
    const string Doc = "docs/integrations/iqms-delmiaworks.md";

    static readonly Dictionary<ErpEntity, (string DefaultPath, Dictionary<string, string> Map)> P = new()
    {
        [ErpEntity.Customer] = ("/CRM/CustomerCentral/Customers/", new() { ["code"] = "CustNo", ["name"] = "Company" }),
        [ErpEntity.Item] = ("", new() { ["number"] = "ItemNo", ["name"] = "Descrip", ["revision"] = "Rev" }),
        [ErpEntity.Supplier] = ("", new() { ["code"] = "VendorNo", ["name"] = "Company" }),
        [ErpEntity.ProductionOrder] = ("", new() { ["number"] = "WorkOrderNo", ["item"] = "ItemNo", ["quantity"] = "Qty" }),
        [ErpEntity.PurchaseOrder] = ("", new() { ["number"] = "PONo", ["supplierCode"] = "VendorNo" }),
    };

    public ErpCapabilities Capabilities(string edition) => new(edition, [.. P.Keys], [], false,
        "Session login (ERP user, password, database alias) returning a token sent on later calls", "WebAPI REST (JSON); GraphQL SDK also licensed",
        "Not public", "Not public", "Not public", 1, 30, Doc);

    public IReadOnlyList<UnverifiedItem> Unverified(string edition) =>
    [
        new("Whole contract", "No public API portal: endpoint and field names ship in the licensed SDK help. Every path is an option (path.<Entity>) to set from the customer's SDK docs; only Customers (/CRM/CustomerCentral/Customers/) is cited, from older IQMS material.", Doc),
        new("Login", "POST /User/Login returning AuthToken (option loginPath) and the token header (option tokenHeader, default AuthToken) are not confirmed.", Doc),
        new("Paging, delta, limits, errors, events, sandbox", "None of these are public; the adapter reads one page per call and skips unchanged rows by hash. The customer's test database is the sandbox.", Doc),
        new("Fallback", "CSV exchange (a CsvExchange connection) or read-only DB views are the fallback when WebAPI is not licensed.", Doc),
    ];

    public IReadOnlyDictionary<string, string> DefaultMap(string edition, ErpEntity entity) => P.TryGetValue(entity, out var p) ? p.Map : new();

    async Task<string> LoginAsync(ErpCallContext c, CancellationToken ct) => await c.Tokens.GetAsync(c.Connection.Id, "dw", async () =>
    {
        var n = await c.Http.JsonAsync(() => ErpWire.PostJson(c.Base + c.Opt("loginPath", "/User/Login"),
            new { UserName = c.Connection.Username ?? throw new ErpException("Set the ERP user."), Password = c.RequireSecret("password"), Database = c.Connection.Company ?? throw new ErpException("Set the database alias.") }), ct);
        return (n["AuthToken"]?.GetValue<string>() ?? n["token"]?.GetValue<string>() ?? throw new ErpException("Login returned no token."), TimeSpan.FromMinutes(c.OptInt("tokenMinutes", 20)));
    }, c.Now);

    public async Task<string> TestAsync(ErpCallContext c, CancellationToken ct)
    {
        var p = await ReadAsync(c, ErpEntity.Customer, new(null, null), ct);
        return $"Logged in; customers returned {p.Records.Count} row(s).";
    }

    public async Task<ErpPage> ReadAsync(ErpCallContext c, ErpEntity entity, ErpCursor cursor, CancellationToken ct)
    {
        this.RequireRead(c.Connection.Edition, entity);
        var path = c.Opt("path." + entity, P[entity].DefaultPath);
        if (path.Length == 0) throw new ErpException($"Set option path.{entity} from the customer's DELMIAworks WebAPI SDK documentation.");
        var token = await LoginAsync(c, ct);
        var n = await c.Http.JsonAsync(() => ErpWire.Get(c.Base + path, null, (c.Opt("tokenHeader", "AuthToken"), token)), ct);
        var arr = n as JsonArray ?? n["Items"] as JsonArray ?? [];
        var id = c.Opt("idField." + entity, DefaultMap(c.Connection.Edition, entity).Values.First());
        return new ErpPage(arr.OfType<JsonNode>().Select(x => c.Record(this, entity, x, id, null)).ToList(), null);
    }
}
