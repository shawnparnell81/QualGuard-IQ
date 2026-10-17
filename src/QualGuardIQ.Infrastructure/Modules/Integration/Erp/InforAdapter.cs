using System.Text.Json;
using System.Text.Json.Nodes;
using QualGuardIQ.Domain.Integration;

namespace QualGuardIQ.Infrastructure.Modules.Integration.Erp;

/// <summary>
/// Infor CloudSuite through the ION API Gateway. The secret is the downloaded .ionapi file (ti, ci, cs, iu, pu, ot,
/// saak, sask). client_credentials is preferred; the classic resource-owner grant (saak/sask) is supported.
/// Editions: M3 (MI programs over m3api-rest) and Mongoose (SyteLine/CSI IDO REST: ido/load with bookmark paging).
/// See docs/integrations/infor.md.
/// </summary>
public sealed class InforAdapter : IErpAdapter
{
    public const string M3 = "M3", Mongoose = "Mongoose";
    public ErpSystem System => ErpSystem.Infor;
    public IReadOnlyList<string> Editions => [M3, Mongoose];
    const string Doc = "docs/integrations/infor.md";

    static readonly Dictionary<ErpEntity, (string Program, string Transaction, string Key, Dictionary<string, string> Map)> M3Programs = new()
    {
        [ErpEntity.Item] = ("MMS200MI", "LstByNumber", "ITNO", new() { ["number"] = "ITNO", ["name"] = "ITDS", ["uom"] = "UNMS" }),
        [ErpEntity.Customer] = ("CRS610MI", "LstByNumber", "CUNO", new() { ["code"] = "CUNO", ["name"] = "CUNM" }),
        [ErpEntity.Supplier] = ("CRS620MI", "LstSuppliers", "SUNO", new() { ["code"] = "SUNO", ["name"] = "SUNM" }),
        [ErpEntity.ProductionOrder] = ("PMS100MI", "LstMO", "MFNO", new() { ["number"] = "MFNO", ["item"] = "PRNO", ["quantity"] = "ORQA" }),
        [ErpEntity.PurchaseOrder] = ("PPS200MI", "LstPOHead", "PUNO", new() { ["number"] = "PUNO", ["supplierCode"] = "SUNO" }),
    };

    static readonly Dictionary<ErpEntity, (string Ido, string Key, string Properties, Dictionary<string, string> Map)> Idos = new()
    {
        [ErpEntity.Item] = ("SLItems", "Item", "Item,Description,UM,Revision,RecordDate", new() { ["number"] = "Item", ["name"] = "Description", ["uom"] = "UM", ["revision"] = "Revision" }),
        [ErpEntity.Customer] = ("SLCustomers", "CustNum", "CustNum,Name,RecordDate", new() { ["code"] = "CustNum", ["name"] = "Name" }),
        [ErpEntity.Supplier] = ("SLVendors", "VendNum", "VendNum,Name,RecordDate", new() { ["code"] = "VendNum", ["name"] = "Name" }),
        [ErpEntity.PurchaseOrder] = ("SLPos", "PoNum", "PoNum,VendNum,OrderDate,RecordDate", new() { ["number"] = "PoNum", ["supplierCode"] = "VendNum", ["orderedOn"] = "OrderDate" }),
        [ErpEntity.ProductionOrder] = ("SLJobs", "Job", "Job,Item,QtyReleased,RecordDate", new() { ["number"] = "Job", ["item"] = "Item", ["quantity"] = "QtyReleased" }),
        [ErpEntity.WorkCenter] = ("SLWcs", "Wc", "Wc,Description,RecordDate", new() { ["code"] = "Wc", ["name"] = "Description" }),
        [ErpEntity.Employee] = ("SLEmployees", "EmpNum", "EmpNum,Name,RecordDate", new() { ["number"] = "EmpNum", ["name"] = "Name" }),
        [ErpEntity.Lot] = ("SLLots", "Lot", "Lot,Item,RecordDate", new() { ["number"] = "Lot", ["item"] = "Item" }),
    };

    public ErpCapabilities Capabilities(string edition) => edition == Mongoose
        ? new(edition, [.. Idos.Keys], [], false, "ION Backend Service (.ionapi) bearer + X-Infor-MongooseConfig; or direct Mongoose token GET /IDORequestService/ido/token/{config} with username/password headers",
            "GET /IDORequestService/ido/load/{ido}?properties=&filter=&recordCap=&bookmark=", "recordCap + bookmark", "Gateway throttling per tenant (numbers not published)", "ION BODs (not used)", 2, 0, Doc)
        : new(edition, [.. M3Programs.Keys], [], false, "ION Backend Service (.ionapi): client_credentials (preferred) or password grant with saak/sask",
            "<iu>/<ti>/M3/m3api-rest/v2/execute/<Program>/<Transaction>", "maxrecs; next page starts after the last key", "Gateway throttling per tenant (numbers not published)", "ION BODs (not used)", 2, 0, Doc);

    public IReadOnlyList<UnverifiedItem> Unverified(string edition) => edition == Mongoose
        ?
        [
            new("IDO names", "SLItems, SLCustomers, SLVendors, SLPos, SLJobs, SLWcs, SLEmployees, SLLots and their properties: confirm with /IDORequestService/ido/dynamic/api-docs-collection/{IDO}.", Doc),
            new("Response fields", "Items / Bookmark / MoreRowsExist in the load response are assumed; check the per-IDO swagger.", Doc),
            new("Delta", "RecordDate filtering is not confirmed, so the adapter reads in full and skips unchanged rows by hash.", Doc),
            new("Product path", "The ION path for Mongoose (/<ti>/CSI/IDORequestService/…) is not confirmed; option ionProductPath sets it.", Doc),
        ]
        :
        [
            new("M3 path", "The m3api-rest v2 execute path through ION is from community and suite docs, not a dedicated official page.", Doc),
            new("Programs", "Only MMS200MI is cited. CRS610MI, CRS620MI, PMS100MI, PPS200MI transactions and fields (ITNO, CUNO, SUNO, MFNO, PUNO …) come from the tenant's MI catalog.", Doc),
            new("Response shape", "results[0].records[] is assumed for the v2 response.", Doc),
            new("Paging / delta", "maxrecs and LMDT change-date fields are not confirmed; the adapter pages by last key and skips unchanged rows by hash.", Doc),
            new("Rate limits / sandbox", "Gateway numbers and TST/TRN tenant details are not published.", Doc),
        ];

    public IReadOnlyDictionary<string, string> DefaultMap(string edition, ErpEntity entity) =>
        edition == Mongoose ? (Idos.TryGetValue(entity, out var i) ? i.Map : new()) : (M3Programs.TryGetValue(entity, out var m) ? m.Map : new());

    /// <summary>The .ionapi credentials file, as documented: ti, ci, cs, iu, pu, ot, saak, sask.</summary>
    public sealed record IonApi(string Ti, string Ci, string Cs, string Iu, string Pu, string Ot, string? Saak, string? Sask);

    public static IonApi Parse(string json)
    {
        var n = JsonNode.Parse(json) ?? throw new ErpException("The .ionapi file is not JSON.");
        string Req(string k) => n[k]?.GetValue<string>() ?? throw new ErpException($"The .ionapi file has no '{k}'.");
        return new(Req("ti"), Req("ci"), Req("cs"), Req("iu"), Req("pu"), Req("ot"), n["saak"]?.GetValue<string>(), n["sask"]?.GetValue<string>());
    }

    async Task<string> IonTokenAsync(ErpCallContext c, CancellationToken ct)
    {
        var ion = Parse(c.RequireSecret(".ionapi file"));
        var grant = c.Opt("grant", "client_credentials");
        return await c.Tokens.GetAsync(c.Connection.Id, "ion", async () =>
        {
            List<KeyValuePair<string, string>> f = grant == "password"
                ? [new("grant_type", "password"), new("username", ion.Saak ?? throw new ErpException("No saak in the .ionapi file.")), new("password", ion.Sask ?? ""), new("client_id", ion.Ci), new("client_secret", ion.Cs)]
                : [new("grant_type", "client_credentials"), new("client_id", ion.Ci), new("client_secret", ion.Cs)];
            return ErpWire.Token(await c.Http.JsonAsync(() => ErpWire.Form(ion.Pu.TrimEnd('/') + "/" + ion.Ot.TrimStart('/'), f), ct));
        }, c.Now);
    }

    async Task<(string Url, System.Net.Http.Headers.AuthenticationHeaderValue? Auth, (string, string)[] Headers)> MongooseAsync(ErpCallContext c, CancellationToken ct)
    {
        var config = c.Connection.Company ?? throw new ErpException("Set the Mongoose configuration name.");
        if (c.Opt("mode", "ion") == "direct")
        {
            var token = await c.Tokens.GetAsync(c.Connection.Id, "mg", async () =>
            {
                var n = await c.Http.JsonAsync(() => ErpWire.Get($"{c.Base}/IDORequestService/ido/token/{Uri.EscapeDataString(config)}", null,
                    ("username", c.Connection.Username ?? ""), ("password", c.RequireSecret("password"))), ct);
                return (n["Token"]?.GetValue<string>() ?? throw new ErpException("No Token in the Mongoose token response."), TimeSpan.FromMinutes(c.OptInt("tokenMinutes", 30)));
            }, c.Now);
            return ($"{c.Base}/IDORequestService", null, [("Authorization", token)]);
        }
        var ion = Parse(c.RequireSecret(".ionapi file"));
        var t = await IonTokenAsync(c, ct);
        return ($"{ion.Iu.TrimEnd('/')}/{ion.Ti}/{c.Opt("ionProductPath", "CSI")}/IDORequestService", ErpWire.Bearer(t), [("X-Infor-MongooseConfig", config)]);
    }

    public async Task<string> TestAsync(ErpCallContext c, CancellationToken ct)
    {
        var p = await ReadAsync(c, ErpEntity.Item, new(null, null), ct, 1);
        return $"Authenticated; {p.Records.Count} item row(s) read.";
    }

    public Task<ErpPage> ReadAsync(ErpCallContext c, ErpEntity entity, ErpCursor cursor, CancellationToken ct) => ReadAsync(c, entity, cursor, ct, 0);

    async Task<ErpPage> ReadAsync(ErpCallContext c, ErpEntity entity, ErpCursor cursor, CancellationToken ct, int size)
    {
        this.RequireRead(c.Connection.Edition, entity);
        var cap = size > 0 ? size : Math.Clamp(c.PageSize <= 0 ? 200 : c.PageSize, 1, 10000);
        if (c.Connection.Edition == Mongoose)
        {
            var d = Idos[entity];
            var (root, auth, headers) = await MongooseAsync(c, ct);
            var url = $"{root}/ido/load/{d.Ido}?properties={Uri.EscapeDataString(d.Properties)}&recordCap={cap}" + (cursor.PageToken is { } b ? "&bookmark=" + Uri.EscapeDataString(b) : "");
            var n = await c.Http.JsonAsync(() => ErpWire.Get(url, auth, headers), ct);
            if (n["Success"]?.GetValue<bool>() == false) throw new ErpException(n["Message"]?.ToString() ?? "IDO load failed.");
            var rows = (n["Items"] as JsonArray ?? []).OfType<JsonNode>().Select(x => c.Record(this, entity, x, d.Key, null)).ToList();
            return new ErpPage(rows, n["MoreRowsExist"]?.GetValue<bool>() == true ? n["Bookmark"]?.GetValue<string>() : null);
        }
        var m = M3Programs[entity];
        var ion = Parse(c.RequireSecret(".ionapi file"));
        var t = await IonTokenAsync(c, ct);
        var q = $"maxrecs={cap}" + (c.Opt("cono") is { Length: > 0 } cono ? "&CONO=" + cono : "") + (cursor.PageToken is { } last ? $"&{m.Key}={Uri.EscapeDataString(last)}" : "");
        var res = await c.Http.JsonAsync(() => ErpWire.Get($"{ion.Iu.TrimEnd('/')}/{ion.Ti}/M3/m3api-rest/v2/execute/{m.Program}/{m.Transaction}?{q}", ErpWire.Bearer(t)), ct);
        var recs = (res["results"]?[0]?["records"] as JsonArray ?? []).OfType<JsonNode>().ToList();
        // the starting key is inclusive in M3 list transactions: drop the row we already have
        if (cursor.PageToken is { } prev && recs.Count > 0 && ErpWire.Str(recs[0], m.Key) == prev) recs.RemoveAt(0);
        var list = recs.Select(x => c.Record(this, entity, x, m.Key, null)).ToList();
        return new ErpPage(list, list.Count > 0 && recs.Count + (cursor.PageToken is null ? 0 : 1) >= cap ? list[^1].ExternalId : null);
    }
}
