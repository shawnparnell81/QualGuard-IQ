using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using QualGuardIQ.Domain.Integration;

namespace QualGuardIQ.Infrastructure.Modules.Integration.Erp;

/// <summary>
/// Sage Intacct XML Web Services. Every request is an XML POST to the gateway carrying two sets of credentials: the Web Services sender id and
/// password (control element, must be authorized in the company's Security tab) and a Web Services user's company id, user id and password
/// (operation element; entity id optional for multi-entity companies). readByQuery returns the first page and a result id; readMore pages on.
/// See docs/integrations/sage-intacct.md.
/// </summary>
public sealed class SageIntacctAdapter : IErpAdapter
{
    public ErpSystem System => ErpSystem.SageIntacct;
    public IReadOnlyList<string> Editions => ["XmlGateway"];
    const string Doc = "docs/integrations/sage-intacct.md";
    public const string Gateway = "https://api.intacct.com/ia/xml/xmlgw.phtml";
    /// <summary>Test hook: request control ids.</summary>
    public static Func<string> ControlId { get; set; } = () => Guid.NewGuid().ToString();

    static readonly Dictionary<ErpEntity, (string Object, string Id, string Fields, Dictionary<string, string> Map)> E = new()
    {
        [ErpEntity.Item] = ("ITEM", "RECORDNO", "RECORDNO,ITEMID,NAME,EXTENDED_DESCRIPTION,ITEMTYPE,STATUS,WHENMODIFIED", new() { ["number"] = "ITEMID", ["name"] = "NAME", ["description"] = "EXTENDED_DESCRIPTION", ["type"] = "ITEMTYPE" }),
        [ErpEntity.Customer] = ("CUSTOMER", "RECORDNO", "RECORDNO,CUSTOMERID,NAME,STATUS,WHENMODIFIED", new() { ["code"] = "CUSTOMERID", ["name"] = "NAME" }),
        [ErpEntity.Supplier] = ("VENDOR", "RECORDNO", "RECORDNO,VENDORID,NAME,STATUS,WHENMODIFIED", new() { ["code"] = "VENDORID", ["name"] = "NAME" }),
        [ErpEntity.Employee] = ("EMPLOYEE", "RECORDNO", "RECORDNO,EMPLOYEEID,PERSONALINFO.CONTACTNAME,STATUS,WHENMODIFIED", new() { ["number"] = "EMPLOYEEID", ["name"] = "PERSONALINFO.CONTACTNAME" }),
        [ErpEntity.PurchaseOrder] = ("PODOCUMENT", "RECORDNO", "RECORDNO,DOCNO,DOCID,CUSTVENDID,WHENCREATED,STATE,WHENMODIFIED", new() { ["number"] = "DOCNO", ["supplierCode"] = "CUSTVENDID", ["orderedOn"] = "WHENCREATED" }),
    };

    public ErpCapabilities Capabilities(string edition) => new(edition, [.. E.Keys], [], true,
        "Web Services sender id and password (control) plus company id, Web Services user id and password (operation); optional entity id",
        "POST XML to https://api.intacct.com/ia/xml/xmlgw.phtml (readByQuery, readMore)", "pagesize up to 1000 then readMore with the result id until numremaining = 0",
        "Concurrent request limit depends on the subscription's performance tier; requests over it are queued or refused",
        "None used: polling WHENMODIFIED is the source of truth", 2, 0, Doc);

    public IReadOnlyList<UnverifiedItem> Unverified(string edition) =>
    [
        new("Purchase orders", "PODOCUMENT returns every purchasing transaction type; filter on DOCPARID (the transaction definition, for example 'Purchase Order') in the customer's company.", Doc),
        new("Manufacturing", "Sage Intacct has no work orders or routings; production data has to come from another system.", Doc),
        new("Date format", "readByQuery date literals use the company's date format; the adapter sends MM/DD/YYYY HH:MM:SS, the default. Confirm in Company > Setup > Configuration.", Doc),
    ];

    public IReadOnlyDictionary<string, string> DefaultMap(string edition, ErpEntity entity) => E.TryGetValue(entity, out var e) ? e.Map : new();

    public static string Request(ErpCallContext c, XElement function)
    {
        var senderId = c.Opt("senderId"); if (senderId.Length == 0) throw new ErpException("Set the Web Services sender id.");
        var login = new XElement("login",
            new XElement("userid", c.Connection.Username is { Length: > 0 } u ? u : throw new ErpException("Set the Web Services user id.")),
            new XElement("companyid", c.Connection.Company is { Length: > 0 } co ? co : throw new ErpException("Set the company id.")),
            new XElement("password", c.RequireNamed("userPassword", "Web Services user password", c.Secret)));
        if (c.Opt("entityId") is { Length: > 0 } ent) login.Add(new XElement("locationid", ent));
        var doc = new XElement("request",
            new XElement("control",
                new XElement("senderid", senderId), new XElement("password", c.RequireNamed("senderPassword", "sender password", c.SecondarySecret)),
                new XElement("controlid", ControlId()), new XElement("uniqueid", "false"), new XElement("dtdversion", "3.0"), new XElement("includewhitespace", "false")),
            new XElement("operation", new XElement("authentication", login), new XElement("content", function)));
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + doc.ToString(SaveOptions.DisableFormatting);
    }

    static XElement Fn(XElement body) => new("function", new XAttribute("controlid", ControlId()), body);

    public static string IntacctDate(DateTimeOffset d) => d.UtcDateTime.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture);

    async Task<XElement> CallAsync(ErpCallContext c, XElement function, CancellationToken ct)
    {
        var url = c.Connection.BaseUrl is { Length: > 0 } ? c.Base : Gateway;
        var xml = Request(c, function);
        var (_, body) = await c.Http.SendAsync(() => new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(xml, Encoding.UTF8, "application/xml") }, ct);
        XElement root;
        try { root = XElement.Parse(body); }
        catch (XmlException) { throw new ErpException("The response was not XML: " + ErpHttp.Trim(body)) { Summary = ErpTestStatus.Unreadable }; }
        string Err(XElement? scope) => scope?.Descendants("error").FirstOrDefault() is { } e
            ? $"{e.Element("errorno")?.Value}: {e.Element("description2")?.Value ?? e.Element("description")?.Value}".Trim() : "request failed";
        if (root.Element("control")?.Element("status")?.Value != "success") throw new ErpException("Sender rejected: " + Err(root)) { Summary = "Authentication failed (Web Services sender rejected)" };
        var op = root.Element("operation");
        if (op?.Element("authentication")?.Element("status")?.Value != "success") throw new ErpException("Sign-in rejected: " + Err(op)) { Summary = "Authentication failed (user sign-in rejected)" };
        var result = op.Element("result") ?? throw new ErpException("The response has no result.");
        if (result.Element("status")?.Value != "success") throw new ErpException(Err(result)) { Summary = ErpTestStatus.Rejected };
        return result.Element("data") ?? new XElement("data");
    }

    /// <summary>Turns one returned object element into JSON (nested elements become nested objects; dotted paths reach them).</summary>
    public static JsonObject ToJson(XElement x)
    {
        var o = new JsonObject();
        foreach (var child in x.Elements())
            o[child.Name.LocalName] = child.HasElements ? ToJson(child) : JsonValue.Create(child.Value);
        return o;
    }

    public async Task<string> TestAsync(ErpCallContext c, CancellationToken ct)
    {
        var data = await CallAsync(c, Fn(new XElement("readByQuery", new XElement("object", "VENDOR"), new XElement("fields", "RECORDNO"), new XElement("query"), new XElement("pagesize", 1))), ct);
        return $"Sender and user accepted; {data.Attribute("totalcount")?.Value ?? "0"} vendor(s) visible.";
    }

    public async Task<ErpPage> ReadAsync(ErpCallContext c, ErpEntity entity, ErpCursor cursor, CancellationToken ct)
    {
        this.RequireRead(c.Connection.Edition, entity);
        var d = E[entity];
        var size = Math.Clamp(c.PageSize <= 0 ? 1000 : c.PageSize, 1, 1000);
        var fn = cursor.PageToken is { Length: > 0 } rid
            ? new XElement("readMore", new XElement("resultId", rid))
            : new XElement("readByQuery", new XElement("object", d.Object), new XElement("fields", d.Fields),
                new XElement("query", cursor.Watermark is { } w ? $"WHENMODIFIED > '{IntacctDate(w)}'" : ""), new XElement("pagesize", size));
        var data = await CallAsync(c, Fn(fn), ct);
        var rows = data.Elements().Select(x =>
        {
            var j = ToJson(x);
            // WHENMODIFIED comes back as MM/DD/YYYY HH:MM:SS in GMT
            if (j["WHENMODIFIED"]?.ToString() is { Length: > 0 } wm && DateTime.TryParseExact(wm, "MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
                j["WHENMODIFIED"] = ErpWire.Iso(new DateTimeOffset(dt, TimeSpan.Zero));
            return c.Record(this, entity, j, d.Id, "WHENMODIFIED");
        }).ToList();
        var remaining = int.TryParse(data.Attribute("numremaining")?.Value, out var n) ? n : 0;
        return new ErpPage(rows, remaining > 0 ? data.Attribute("resultId")?.Value : null);
    }
}
