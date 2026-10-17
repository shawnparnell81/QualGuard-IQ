using System.Text.Json;
using System.Text.Json.Nodes;
using QualGuardIQ.Domain.Integration;

namespace QualGuardIQ.Infrastructure.Modules.Integration.Erp;

/// <summary>
/// Epicor Kinetic REST v2 (OData v4). Two credentials on every call: user authentication (Basic with a Kinetic
/// integration user, or a bearer token from Epicor Identity Provider client credentials) plus the API key in x-api-key.
/// A BAQ can replace any business-object read (option baq.&lt;Entity&gt;). See docs/integrations/epicor.md.
/// </summary>
public sealed class EpicorAdapter : IErpAdapter
{
    public ErpSystem System => ErpSystem.Epicor;
    public IReadOnlyList<string> Editions => ["KineticRestV2"];
    const string Doc = "docs/integrations/epicor.md";

    static readonly Dictionary<ErpEntity, (string Service, string Set, string Key, Dictionary<string, string> Map)> S = new()
    {
        [ErpEntity.Item] = ("Erp.BO.PartSvc", "Parts", "PartNum", new() { ["number"] = "PartNum", ["name"] = "PartDescription", ["uom"] = "IUM" }),
        [ErpEntity.Customer] = ("Erp.BO.CustomerSvc", "Customers", "CustNum", new() { ["code"] = "CustID", ["name"] = "Name", ["email"] = "EMailAddress" }),
        [ErpEntity.Supplier] = ("Erp.BO.VendorSvc", "Vendors", "VendorNum", new() { ["code"] = "VendorID", ["name"] = "Name" }),
        [ErpEntity.ProductionOrder] = ("Erp.BO.JobEntrySvc", "JobEntries", "JobNum", new() { ["number"] = "JobNum", ["item"] = "PartNum", ["revision"] = "RevisionNum", ["quantity"] = "ProdQty", ["dueOn"] = "DueDate" }),
        [ErpEntity.PurchaseOrder] = ("Erp.BO.POSvc", "POes", "PONum", new() { ["number"] = "PONum", ["supplierCode"] = "VendorVendorID", ["orderedOn"] = "OrderDate" }),
        [ErpEntity.WorkCenter] = ("Erp.BO.ResourceGroupSvc", "ResourceGroups", "ResourceGrpID", new() { ["code"] = "ResourceGrpID", ["name"] = "Description" }),
        [ErpEntity.Employee] = ("Erp.BO.EmpBasicSvc", "EmpBasics", "EmpID", new() { ["number"] = "EmpID", ["name"] = "Name" }),
    };

    public ErpCapabilities Capabilities(string edition) => new(edition, [.. S.Keys], [], false,
        "User authentication (Basic integration user, or Epicor IdP client credentials at login.epicor.com, scope epicor_erp) plus x-api-key bound to an Access Scope",
        "/<instance>/api/v2/odata/<Company>/Erp.BO.<Name>Svc/<EntitySet> or /BaqSvc/<BAQID>/Data", "$top/$skip", "None published", "None native (BPM / Epicor Functions)", 2, 0, Doc);

    public IReadOnlyList<UnverifiedItem> Unverified(string edition) =>
    [
        new("Header and IdP", "The x-api-key header name and the IdP client-credentials flow come from partner/community material and an Epicor video; confirm on EpicWeb.", Doc),
        new("Services", "PartSvc, VendorSvc, JobEntrySvc, POSvc, ResourceGroupSvc, EmpBasicSvc entity sets and fields are not confirmed (CustomerSvc is cited); check /<instance>/api/help and $metadata.", Doc),
        new("Delta", "SysRevID or a BAQ change-date filter is not confirmed: the adapter reads in full and skips unchanged rows by hash. A BAQ with a change-date filter is the recommended read path.", Doc),
        new("Errors / limits / sandbox", "ErrorMessage/ErrorType/CorrelationId error body, rate limits, and Pilot environments are not confirmed.", Doc),
    ];

    public IReadOnlyDictionary<string, string> DefaultMap(string edition, ErpEntity entity) => S.TryGetValue(entity, out var s) ? s.Map : new();

    public static string? ParseError(HttpResponseMessage r, string body)
    {
        try { var n = JsonNode.Parse(body); return n?["ErrorMessage"]?.ToString() ?? n?["error"]?["message"]?.ToString(); }
        catch (JsonException) { return null; }
    }

    async Task<System.Net.Http.Headers.AuthenticationHeaderValue> AuthAsync(ErpCallContext c, CancellationToken ct)
    {
        if (c.Opt("auth", "basic") == "idp")
        {
            var t = await c.Tokens.GetAsync(c.Connection.Id, "epicor", async () => ErpWire.Token(await c.Http.JsonAsync(() => ErpWire.Form(c.Opt("tokenUrl", "https://login.epicor.com/connect/token"),
                [new("grant_type", "client_credentials"), new("client_id", c.Connection.ClientId ?? ""), new("client_secret", c.RequireSecret("client secret")), new("scope", "epicor_erp")]), ct)), c.Now);
            return ErpWire.Bearer(t);
        }
        return ErpWire.Basic(c.Connection.Username ?? throw new ErpException("Set the integration user."), c.RequireSecret("integration user password"));
    }

    string Url(ErpCallContext c, ErpEntity e, ErpCursor cur, int top)
    {
        var d = S[e];
        var company = c.Connection.Company ?? throw new ErpException("Set the Epicor company.");
        var skip = int.TryParse(cur.PageToken, out var s) ? s : 0;
        var baq = c.Opt("baq." + e);
        var path = baq.Length > 0 ? $"BaqSvc/{baq}/Data" : $"{d.Service}/{d.Set}";
        return $"{c.Base}/api/v2/odata/{company}/{path}?$top={top}&$skip={skip}";
    }

    public async Task<string> TestAsync(ErpCallContext c, CancellationToken ct)
    {
        var p = await ReadPage(c, ErpEntity.Item, new(null, null), 1, ct);
        return $"Authenticated with API key; Parts returned {p.Records.Count} row(s).";
    }

    public Task<ErpPage> ReadAsync(ErpCallContext c, ErpEntity entity, ErpCursor cursor, CancellationToken ct) => ReadPage(c, entity, cursor, Math.Clamp(c.PageSize <= 0 ? 200 : c.PageSize, 1, 5000), ct);

    async Task<ErpPage> ReadPage(ErpCallContext c, ErpEntity entity, ErpCursor cursor, int top, CancellationToken ct)
    {
        this.RequireRead(c.Connection.Edition, entity);
        c.Http.ErrorParser = ParseError;
        var apiKey = c.SecondarySecret ?? throw new ErpException("Set the API key (API Key Maintenance) on the connection.");
        var auth = await AuthAsync(c, ct);
        var n = await c.Http.JsonAsync(() => ErpWire.Get(Url(c, entity, cursor, top), auth, ("x-api-key", apiKey)), ct);
        var rows = (n["value"] as JsonArray ?? []).OfType<JsonNode>().Select(x => c.Record(this, entity, x, S[entity].Key, null, "SysRevID")).ToList();
        var skip = (int.TryParse(cursor.PageToken, out var s) ? s : 0) + rows.Count;
        return new ErpPage(rows, rows.Count == top ? skip.ToString() : null);
    }
}
