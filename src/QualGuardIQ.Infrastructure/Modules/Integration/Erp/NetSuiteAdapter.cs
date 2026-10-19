using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using QualGuardIQ.Domain.Integration;

namespace QualGuardIQ.Infrastructure.Modules.Integration.Erp;

/// <summary>
/// NetSuite, two ways in:
/// SuiteQL edition: OAuth 2.0 client credentials (M2M) with a certificate-signed JWT assertion, then SuiteQL over REST.
/// TokenBasedAuth edition: token-based authentication (OAuth 1.0 with HMAC-SHA256 signatures built from the integration record's consumer key and
/// secret plus an access token id and secret), reading through SuiteQL on SuiteTalk REST or through a customer-deployed RESTlet.
/// Every rule here follows docs/integrations/netsuite.md.
/// </summary>
public sealed class NetSuiteAdapter : IErpAdapter
{
    public const string SuiteQl = "SuiteQL", Tba = "TokenBasedAuth";
    public ErpSystem System => ErpSystem.NetSuite;
    public IReadOnlyList<string> Editions => [SuiteQl, Tba];
    /// <summary>Test hook: nonce source for token-based signatures.</summary>
    public static Func<string> Nonce { get; set; } = () => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
    const string Doc = "docs/integrations/netsuite.md";
    public static readonly string[] AllowedAlgorithms = ["PS256", "PS384", "PS512", "ES256", "ES384", "ES512"];
    const int SuiteQlMaxLimit = 1000;

    public ErpCapabilities Capabilities(string edition) => new(edition,
        [ErpEntity.Item, ErpEntity.Customer, ErpEntity.Supplier, ErpEntity.Employee, ErpEntity.PurchaseOrder, ErpEntity.ProductionOrder], [], true,
        edition == Tba
            ? "Token-based authentication: OAuth 1.0 Authorization header signed with HMAC-SHA256, key = consumer secret & token secret, realm = account id; tokens do not expire until revoked"
            : "OAuth 2.0 client credentials (M2M): JWT assertion signed with the mapped certificate (PS256/PS384/PS512/ES256/ES384/ES512, kid = certificate id); token valid 60 min, no refresh token",
        edition == Tba ? "POST /services/rest/query/v1/suiteql (Prefer: transient), or POST to the RESTlet deployment URL (script and deploy ids)" : "POST /services/rest/query/v1/suiteql with Prefer: transient", "SuiteQL limit ≤ 1000, keyset paging by id (offset kept at 0, so the 100,000-row offset cap never applies)",
        "Account concurrency shared with SOAP/REST/RESTlets (5 for Standard and dev accounts, 15 Premium, 20 Enterprise/Ultimate, +10 per SuiteCloud Plus); HTTP 429 when exceeded",
        "None used: polling SuiteQL is the source of truth", 5, 0, Doc);

    public IReadOnlyList<UnverifiedItem> Unverified(string edition) => edition == Tba ? [.. Common(), new("RESTlet contract", "The RESTlet is the customer's own script. It must accept {entity, since, afterId, limit} and answer {items, hasMore} as described in docs/integrations/netsuite.md.", Doc)] : Common();

    static List<UnverifiedItem> Common() =>
    [
        new("Delta field", "Whether lastmodifieddate exists on item, customer, vendor and employee tables (documented on transaction only). Check the Records Catalog in the sandbox.", Doc),
        new("Record ids", "Exact REST record ids for lot/serial item variants and itemRevision (REST API Browser not readable). Check /services/rest/record/v1/metadata-catalog.", Doc),
        new("Work centers", "No standalone work-center record in the supported list; routings reference them by id.", Doc),
        new("Transaction types", "SuiteQL transaction type codes used for POs ('PurchOrd') and work orders ('WorkOrd') and column names (tranid, entity, trandate, quantity, duedate) need confirming in the Records Catalog.", Doc),
        new("Response envelope", "SuiteQL response fields items / hasMore are used for paging; confirm against a sandbox response.", Doc),
        new("Outbound events", "\"No native outbound webhooks\" is not stated in the docs either way; the adapter polls.", Doc),
        new("Governance", "GET /services/rest/system/v1/governanceLimits needs an administrator login, so the concurrency cap is configured on the connection (default 5).", Doc),
        new("Sandbox", "M2M certificate mapping and access tokens are not copied to sandbox / Release Preview and are cleared by a sandbox refresh; redo them after each refresh.", Doc),
    ];

    static readonly Dictionary<ErpEntity, (string Sql, Dictionary<string, string> Map)> Queries = new()
    {
        [ErpEntity.Item] = ("SELECT id, itemid, displayname, description, itemtype, lastmodifieddate FROM item", new() { ["number"] = "itemid", ["name"] = "displayname", ["description"] = "description", ["type"] = "itemtype" }),
        [ErpEntity.Customer] = ("SELECT id, entityid, companyname, email, lastmodifieddate FROM customer", new() { ["code"] = "entityid", ["name"] = "companyname", ["email"] = "email" }),
        [ErpEntity.Supplier] = ("SELECT id, entityid, companyname, email, lastmodifieddate FROM vendor", new() { ["code"] = "entityid", ["name"] = "companyname", ["email"] = "email" }),
        [ErpEntity.Employee] = ("SELECT id, entityid, firstname, lastname, lastmodifieddate FROM employee", new() { ["number"] = "entityid", ["firstName"] = "firstname", ["lastName"] = "lastname" }),
        [ErpEntity.PurchaseOrder] = ("SELECT t.id, t.tranid, BUILTIN.DF(t.entity) AS vendor, t.trandate, t.lastmodifieddate FROM transaction t WHERE t.type = 'PurchOrd'",
            new() { ["number"] = "tranid", ["supplierCode"] = "vendor", ["orderedOn"] = "trandate" }),
        [ErpEntity.ProductionOrder] = ("SELECT t.id, t.tranid, BUILTIN.DF(t.assemblyitem) AS item, t.quantity, t.enddate, t.status, t.lastmodifieddate FROM transaction t WHERE t.type = 'WorkOrd'",
            new() { ["number"] = "tranid", ["item"] = "item", ["quantity"] = "quantity", ["dueOn"] = "enddate", ["status"] = "status" }),
    };

    public IReadOnlyDictionary<string, string> DefaultMap(string edition, ErpEntity entity) => Queries.TryGetValue(entity, out var q) ? q.Map : new();

    /// <summary>"123456_SB1" → "123456-sb1" (the documented sandbox host form).</summary>
    public static string AccountHost(string account) => account.Trim().ToLowerInvariant().Replace('_', '-');

    public static string Root(ErpCallContext c) =>
        string.IsNullOrWhiteSpace(c.Connection.BaseUrl) ? $"https://{AccountHost(c.Connection.Account ?? throw new ErpException("Set the NetSuite account id."))}.suitetalk.api.netsuite.com" : c.Base;

    public static string TokenUrl(ErpCallContext c) => Root(c) + "/services/rest/auth/oauth2/v1/token";

    /// <summary>Builds the client assertion: header typ/alg/kid, payload iss/scope/aud/iat/exp (exp under 60 minutes).</summary>
    public static string Assertion(ErpCallContext c)
    {
        var alg = c.Opt("algorithm", "PS256").ToUpperInvariant();
        if (!AllowedAlgorithms.Contains(alg)) throw new ErpException($"NetSuite accepts {string.Join(", ", AllowedAlgorithms)} for the client assertion (RS256 is not listed).");
        var kid = c.Opt("certificateId");
        if (kid.Length == 0) throw new ErpException("Set the certificate id (from the OAuth 2.0 Client Credentials Setup mapping).");
        var clientId = c.Connection.ClientId ?? throw new ErpException("Set the client id of the integration record.");
        var now = c.Now.ToUnixTimeSeconds();
        var header = new JsonObject { ["typ"] = "JWT", ["alg"] = alg, ["kid"] = kid };
        var payload = new JsonObject { ["iss"] = clientId, ["scope"] = c.Opt("scope", "rest_webservices"), ["aud"] = TokenUrl(c), ["iat"] = now, ["exp"] = now + 3000 };
        var input = B64(Encoding.UTF8.GetBytes(header.ToJsonString())) + "." + B64(Encoding.UTF8.GetBytes(payload.ToJsonString()));
        var data = Encoding.ASCII.GetBytes(input);
        var hash = alg[2..] switch { "384" => HashAlgorithmName.SHA384, "512" => HashAlgorithmName.SHA512, _ => HashAlgorithmName.SHA256 };
        var pem = c.RequireSecret("certificate private key (PEM)");
        byte[] sig;
        if (alg.StartsWith("PS"))
        {
            using var rsa = RSA.Create(); rsa.ImportFromPem(pem);
            sig = rsa.SignData(data, hash, RSASignaturePadding.Pss);
        }
        else
        {
            using var ec = ECDsa.Create(); ec.ImportFromPem(pem);
            sig = ec.SignData(data, hash, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        return input + "." + B64(sig);
    }

    static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    async Task<string> TokenAsync(ErpCallContext c, CancellationToken ct) => await c.Tokens.GetAsync(c.Connection.Id, "ns", async () =>
    {
        var n = await c.Http.JsonAsync(() => ErpWire.Form(TokenUrl(c), [
            new("grant_type", "client_credentials"),
            new("client_assertion_type", "urn:ietf:params:oauth:client-assertion-type:jwt-bearer"),
            new("client_assertion", Assertion(c))]), ct);
        return ErpWire.Token(n);
    }, c.Now);

    public static string? ParseError(HttpResponseMessage r, string body)
    {
        try
        {
            var n = JsonNode.Parse(body);
            var details = n?["o:errorDetails"] as JsonArray;
            var first = details?.FirstOrDefault();
            return first is null ? n?["title"]?.ToString() : $"{first["o:errorCode"]}: {first["detail"]}";
        }
        catch (JsonException) { return null; }
    }

    public async Task<string> TestAsync(ErpCallContext c, CancellationToken ct)
    {
        c.Http.ErrorParser = ParseError;
        if (c.Connection.Edition == Tba)
        {
            if (UsesRestlet(c))
            {
                var r = await RestletAsync(c, new JsonObject { ["entity"] = "Item", ["limit"] = 1 }, ct);
                return $"Token-based signature accepted; the RESTlet returned {(r["items"] as JsonArray)?.Count ?? 0} row(s).";
            }
            var t = await QueryAsync(c, "SELECT id FROM item WHERE ROWNUM <= 1", 1, ct);
            return $"Token-based signature accepted; SuiteQL returned {t.Items.Count} row(s).";
        }
        await TokenAsync(c, ct);
        var page = await QueryAsync(c, "SELECT id FROM item WHERE ROWNUM <= 1", 1, ct);
        return $"Token issued; SuiteQL returned {page.Items.Count} row(s).";
    }

    // ---------------------------------------------------------------- token-based authentication (OAuth 1.0, HMAC-SHA256)
    static bool UsesRestlet(ErpCallContext c) => c.Opt("readVia", "suiteql") == "restlet";

    /// <summary>NetSuite realm: the account id in upper case with an underscore ("123456_SB1").</summary>
    public static string Realm(string account) => account.Trim().ToUpperInvariant().Replace('-', '_');

    /// <summary>RFC 3986 percent-encoding as OAuth 1.0 requires (unreserved characters only are left as is).</summary>
    public static string Pct(string s)
    {
        var sb = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(s))
        {
            var ch = (char)b;
            if (ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.' or '_' or '~') sb.Append(ch);
            else sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Builds the OAuth 1.0 Authorization header for one request. The signature base string is METHOD &amp; encoded base URL &amp; encoded sorted
    /// parameters (oauth_* plus the query string); the HMAC-SHA256 key is the encoded consumer secret and token secret joined with &amp;.
    /// </summary>
    public static string TbaHeader(ErpCallContext c, HttpMethod method, string url, string? nonce = null, long? timestamp = null)
    {
        var account = c.Connection.Account is { Length: > 0 } a ? a : throw new ErpException("Set the NetSuite account id.");
        var consumerKey = c.Connection.ClientId is { Length: > 0 } k ? k : throw new ErpException("Set the consumer key from the integration record.");
        var consumerSecret = c.RequireNamed("consumerSecret", "consumer secret", c.Secret);
        var tokenId = c.RequireNamed("tokenId", "token id");
        var tokenSecret = c.RequireNamed("tokenSecret", "token secret", c.SecondarySecret);
        var u = new Uri(url);
        var oauth = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["oauth_consumer_key"] = consumerKey, ["oauth_nonce"] = nonce ?? Nonce(), ["oauth_signature_method"] = "HMAC-SHA256",
            ["oauth_timestamp"] = (timestamp ?? c.Now.ToUnixTimeSeconds()).ToString(global::System.Globalization.CultureInfo.InvariantCulture), ["oauth_token"] = tokenId, ["oauth_version"] = "1.0",
        };
        var all = oauth.Select(p => (K: Pct(p.Key), V: Pct(p.Value))).ToList();
        foreach (var part in u.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = part.IndexOf('=');
            var key = Uri.UnescapeDataString(i < 0 ? part : part[..i]); var val = i < 0 ? "" : Uri.UnescapeDataString(part[(i + 1)..]);
            all.Add((Pct(key), Pct(val)));
        }
        var normalized = string.Join("&", all.OrderBy(p => p.K, StringComparer.Ordinal).ThenBy(p => p.V, StringComparer.Ordinal).Select(p => p.K + "=" + p.V));
        var baseUrl = $"{u.Scheme.ToLowerInvariant()}://{u.Host.ToLowerInvariant()}{(u.IsDefaultPort ? "" : ":" + u.Port)}{u.AbsolutePath}";
        var baseString = method.Method.ToUpperInvariant() + "&" + Pct(baseUrl) + "&" + Pct(normalized);
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(Pct(consumerSecret) + "&" + Pct(tokenSecret)));
        var sig = Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(baseString)));
        return $"OAuth realm=\"{Realm(account)}\", " + string.Join(", ", oauth.Select(p => $"{p.Key}=\"{Pct(p.Value)}\"")) + $", oauth_signature=\"{Pct(sig)}\"";
    }

    /// <summary>RESTlet deployment URL: the full external URL, or built from the account id with the script and deploy ids.</summary>
    public static string RestletUrl(ErpCallContext c)
    {
        if (c.Opt("restletUrl") is { Length: > 0 } full) return full;
        var script = c.Opt("scriptId"); var deploy = c.Opt("deployId", "1");
        if (script.Length == 0) throw new ErpException("Set the RESTlet script id (or the full external URL from the script deployment).");
        var host = $"https://{AccountHost(c.Connection.Account ?? throw new ErpException("Set the NetSuite account id."))}.restlets.api.netsuite.com";
        return $"{host}/app/site/hosting/restlet.nl?script={Uri.EscapeDataString(script)}&deploy={Uri.EscapeDataString(deploy)}";
    }

    static async Task<JsonNode> RestletAsync(ErpCallContext c, JsonObject body, CancellationToken ct)
    {
        var url = RestletUrl(c);
        return await c.Http.JsonAsync(() =>
        {
            var m = ErpWire.PostJson(url, body);
            m.Headers.TryAddWithoutValidation("Authorization", TbaHeader(c, HttpMethod.Post, url));
            return m;
        }, ct);
    }

    public static string Sql(ErpEntity entity, ErpCursor cursor)
    {
        var (sql, _) = Queries[entity];
        var where = new List<string>();
        if (cursor.Watermark is { } w) where.Add($"lastmodifieddate > TO_DATE('{w.UtcDateTime:yyyy-MM-dd HH:mm:ss}', 'YYYY-MM-DD HH24:MI:SS')");
        if (long.TryParse(cursor.PageToken, out var lastId)) where.Add($"id > {lastId}");
        var prefix = sql.Contains(" t WHERE ") ? "t." : "";
        var cond = string.Join(" AND ", where.Select(x => prefix + x));
        var joined = cond.Length == 0 ? sql : sql.Contains(" WHERE ") ? $"{sql} AND {cond}" : $"{sql} WHERE {cond}";
        return $"{joined} ORDER BY {prefix}id";
    }

    async Task<(JsonArray Items, bool HasMore)> QueryAsync(ErpCallContext c, string sql, int limit, CancellationToken ct)
    {
        var url = $"{Root(c)}/services/rest/query/v1/suiteql?limit={limit}&offset=0";
        JsonNode n;
        if (c.Connection.Edition == Tba)
            n = await c.Http.JsonAsync(() =>
            {
                var m = ErpWire.PostJson(url, new { q = sql }, null, ("Prefer", "transient"));
                m.Headers.TryAddWithoutValidation("Authorization", TbaHeader(c, HttpMethod.Post, url));
                return m;
            }, ct);
        else
        {
            var token = await TokenAsync(c, ct);
            n = await c.Http.JsonAsync(() => ErpWire.PostJson(url, new { q = sql }, ErpWire.Bearer(token), ("Prefer", "transient")), ct);
        }
        return (n["items"] as JsonArray ?? [], n["hasMore"]?.GetValue<bool>() ?? false);
    }

    public async Task<ErpPage> ReadAsync(ErpCallContext c, ErpEntity entity, ErpCursor cursor, CancellationToken ct)
    {
        this.RequireRead(c.Connection.Edition, entity);
        c.Http.ErrorParser = ParseError;
        var limit = Math.Clamp(c.PageSize <= 0 ? SuiteQlMaxLimit : c.PageSize, 1, SuiteQlMaxLimit);
        JsonArray items; bool hasMore;
        if (c.Connection.Edition == Tba && UsesRestlet(c))
        {
            var body = new JsonObject { ["entity"] = entity.ToString(), ["limit"] = limit, ["afterId"] = cursor.PageToken, ["since"] = cursor.Watermark is { } w ? ErpWire.Iso(w) : null };
            var r = await RestletAsync(c, body, ct);
            (items, hasMore) = (r["items"] as JsonArray ?? [], r["hasMore"]?.GetValue<bool>() ?? false);
        }
        else (items, hasMore) = await QueryAsync(c, Sql(entity, cursor), limit, ct);
        var records = items.OfType<JsonNode>().Select(row => c.Record(this, entity, row, "id", "lastmodifieddate")).ToList();
        foreach (var r in records.Where(r => entity == ErpEntity.Employee))
            r.Fields["name"] = $"{r.Fields["firstName"]} {r.Fields["lastName"]}".Trim();
        return new ErpPage(records, hasMore && records.Count > 0 ? records[^1].ExternalId : null);
    }
}
