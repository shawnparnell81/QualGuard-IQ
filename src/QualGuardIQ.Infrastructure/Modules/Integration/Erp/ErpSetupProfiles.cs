using System.Text.Json.Nodes;
using QualGuardIQ.Domain.Integration;

namespace QualGuardIQ.Infrastructure.Modules.Integration.Erp;

/// <summary>
/// One input on an ERP setup screen. Target says where the value is kept: baseUrl, account, company, clientId, username, option:&lt;name&gt;,
/// secret, secondarySecret or secret:&lt;name&gt; (secrets are write-only and encrypted). Kind: text, url, number, select, secret, file (a secret
/// pasted or loaded from a file, such as a PEM key or an .ionapi file). ShowWhen hides a field unless option=value.
/// </summary>
public sealed record SetupField(string Key, string Label, string Kind, string Target, bool Required, string Group, string? Help = null, string? Placeholder = null,
    IReadOnlyList<string>? Choices = null, string? Default = null, string? ShowWhen = null)
{
    public bool IsSecret => Target is "secret" or "secondarySecret" || Target.StartsWith("secret:", StringComparison.Ordinal);
}

/// <summary>The complete setup screen for one ERP edition: what the customer's administrator must prepare, then each field.</summary>
public sealed record SetupProfile(ErpSystem System, string Edition, string Title, IReadOnlyList<string> Prepare, IReadOnlyList<SetupField> Fields, IReadOnlyList<string> RotatingSecrets);

/// <summary>
/// Setup screens per ERP. Every field matches what the vendor's own documentation asks for (docs/integrations/&lt;vendor&gt;.md); the adapters read
/// the same slots, so a connection that passes <see cref="Missing"/> has everything its adapter needs to try a test.
/// </summary>
public static class ErpSetupProfiles
{
    const string Conn = "Connection", Auth = "Authentication", End = "Endpoints", Adv = "Advanced";

    static SetupField T(string key, string label, string target, bool req, string group, string? help = null, string? ph = null, string? def = null, string? when = null) => new(key, label, "text", target, req, group, help, ph, null, def, when);
    static SetupField U(string key, string label, string target, bool req, string group, string? help = null, string? ph = null, string? when = null) => new(key, label, "url", target, req, group, help, ph, null, null, when);
    static SetupField S(string key, string label, string target, bool req, string? help = null, string? when = null) => new(key, label, "secret", target, req, Auth, help, null, null, null, when);
    static SetupField Fl(string key, string label, string target, bool req, string? help = null, string? when = null) => new(key, label, "file", target, req, Auth, help, null, null, null, when);
    static SetupField C(string key, string label, string target, string group, string def, string? help, params string[] choices) => new(key, label, "select", target, true, group, help, null, choices, def);

    static readonly SetupField PageSize = new("pageSize", "Rows per page", "number", "option:pageSize", false, Adv, "Leave blank to use the vendor's largest page.");

    static readonly List<SetupProfile> All =
    [
        new(ErpSystem.NetSuite, NetSuiteAdapter.SuiteQl, "NetSuite: OAuth 2.0 machine-to-machine (certificate)",
            ["Enable SuiteTalk REST Web Services and OAuth 2.0 (Setup > Company > Enable Features > SuiteCloud).",
             "Create an integration record with Client Credentials (Machine to Machine) Grant and the REST Web Services scope; copy the client id.",
             "Upload the public certificate in OAuth 2.0 Client Credentials Setup, mapped to the integration user and role; copy the certificate id."],
            [
                T("account", "Account id", "account", true, Conn, "Setup > Company > Company Information. Sandboxes end in _SB1, _SB2 and so on.", "1234567 or 1234567_SB1"),
                T("clientId", "Client id (integration record)", "clientId", true, Auth),
                T("certificateId", "Certificate id", "option:certificateId", true, Auth, "From the OAuth 2.0 Client Credentials Setup mapping."),
                C("algorithm", "Signing algorithm", "option:algorithm", Auth, "PS256", "NetSuite does not accept RS256.", "PS256", "PS384", "PS512", "ES256", "ES384", "ES512"),
                Fl("privateKey", "Certificate private key (PEM)", "secret", true, "Paste or load the PEM private key that matches the uploaded certificate."),
                T("scope", "Scope", "option:scope", false, Adv, null, null, "rest_webservices"),
                U("baseUrl", "SuiteTalk REST URL", "baseUrl", false, End, "Leave blank to use https://<account>.suitetalk.api.netsuite.com.", "https://1234567.suitetalk.api.netsuite.com"),
                PageSize,
            ], []),
        new(ErpSystem.NetSuite, NetSuiteAdapter.Tba, "NetSuite: token-based authentication (TBA)",
            ["Enable Token-Based Authentication and SuiteTalk REST Web Services (Setup > Company > Enable Features > SuiteCloud > Manage Authentication).",
             "Create an integration record with Token-Based Authentication ticked; copy the consumer key and consumer secret (shown once).",
             "Give the integration role the User Access Tokens and REST Web Services permissions, then create an access token for the integration user and role; copy the token id and token secret (shown once).",
             "To read through a RESTlet instead of SuiteQL, deploy the RESTlet and copy the script and deployment ids or the external URL.",
             "NetSuite stops accepting new token-based integration records from release 2027.1; for a new integration use the OAuth 2.0 machine-to-machine edition."],
            [
                T("account", "Account id (realm)", "account", true, Conn, "Setup > Company > Company Information. Sandboxes end in _SB1. Used as the OAuth realm and to build the URLs.", "1234567_SB1"),
                T("consumerKey", "Consumer key", "clientId", true, Auth, "From the integration record."),
                S("consumerSecret", "Consumer secret", "secret:consumerSecret", true),
                S("tokenId", "Token id", "secret:tokenId", true, "From the access token for the integration user and role."),
                S("tokenSecret", "Token secret", "secret:tokenSecret", true),
                new("signature", "Signature method", "select", "option:signatureMethod", true, Auth, "NetSuite requires HMAC-SHA256 for token-based authentication.", null, ["HMAC-SHA256"], "HMAC-SHA256"),
                C("readVia", "Read records through", "option:readVia", End, "suiteql", "SuiteQL on SuiteTalk REST needs no script. A RESTlet uses your own deployed script.", "suiteql", "restlet"),
                U("baseUrl", "SuiteTalk REST URL", "baseUrl", false, End, "Leave blank to use https://<account>.suitetalk.api.netsuite.com.", "https://1234567-sb1.suitetalk.api.netsuite.com"),
                U("restletUrl", "RESTlet external URL", "option:restletUrl", false, End, "Copy it from the script deployment, or fill the script and deployment ids instead.", "https://1234567-sb1.restlets.api.netsuite.com/app/site/hosting/restlet.nl?script=123&deploy=1", "readVia=restlet"),
                T("scriptId", "RESTlet script id", "option:scriptId", false, End, null, "customscript_qg_sync or 123", null, "readVia=restlet"),
                T("deployId", "RESTlet deployment id", "option:deployId", false, End, null, "1", "1", "readVia=restlet"),
                PageSize,
            ], []),
        new(ErpSystem.Sap, SapAdapter.S4Cloud, "SAP S/4HANA Cloud: communication arrangement",
            ["Create a communication system and user, then a communication arrangement for each API scenario (product, business partner, purchase order, production order).",
             "Use Basic with the communication user, or OAuth 2.0 client credentials if the arrangement is set up for OAuth."],
            [
                U("baseUrl", "API host", "baseUrl", true, Conn, "The -api host of the tenant.", "https://my300000-api.s4hana.cloud.sap"),
                C("auth", "Authentication", "option:auth", Auth, "basic", null, "basic", "oauth"),
                T("username", "Communication user", "username", false, Auth, null, null, null, "auth=basic"),
                S("password", "Communication user password", "secret", false, null, "auth=basic"),
                T("clientId", "OAuth client id", "clientId", false, Auth, null, null, null, "auth=oauth"),
                S("clientSecret", "OAuth client secret", "secret", false, null, "auth=oauth"),
                U("tokenUrl", "Token URL", "option:tokenUrl", false, Auth, "From the communication arrangement's OAuth 2.0 details.", "https://my300000.s4hana.cloud.sap/sap/bc/sec/oauth2/token", "auth=oauth"),
                T("poService", "Purchase order service", "option:poService", false, Adv, "Override the OData service name if the arrangement uses another."),
                PageSize,
            ], []),
        new(ErpSystem.Sap, SapAdapter.S4OnPrem, "SAP S/4HANA on-premise: Gateway technical user",
            ["Activate the A2X OData services in /IWFND/MAINT_SERVICE and create a technical user with the matching authorizations.", "Expose the Gateway over HTTPS to the QualGuard IQ server."],
            [
                U("baseUrl", "Gateway URL", "baseUrl", true, Conn, null, "https://s4.example.com:44300"),
                C("auth", "Authentication", "option:auth", Auth, "basic", null, "basic", "oauth"),
                T("username", "Technical user", "username", false, Auth, null, null, null, "auth=basic"),
                S("password", "Technical user password", "secret", false, null, "auth=basic"),
                T("clientId", "OAuth client id", "clientId", false, Auth, null, null, null, "auth=oauth"),
                S("clientSecret", "OAuth client secret", "secret", false, null, "auth=oauth"),
                U("tokenUrl", "Token URL", "option:tokenUrl", false, Auth, null, null, "auth=oauth"),
                PageSize,
            ], []),
        new(ErpSystem.Sap, SapAdapter.BusinessOne, "SAP Business One: Service Layer",
            ["Install the Service Layer and note its HTTPS address (usually port 50000).", "Create a B1 user for the integration with read access to items, business partners and documents."],
            [
                U("baseUrl", "Service Layer URL", "baseUrl", true, Conn, "Without /b1s/v1.", "https://b1server:50000"),
                T("companyDb", "Company database (CompanyDB)", "company", true, Conn, null, "SBODEMOUS"),
                T("username", "B1 user name", "username", true, Auth),
                S("password", "B1 password", "secret", true),
                C("version", "Service Layer version", "option:version", Adv, "v1", "v1 is OData V3, v2 is OData V4.", "v1", "v2"),
                PageSize,
            ], []),
        new(ErpSystem.Oracle, "FusionScm", "Oracle Fusion Cloud SCM: REST",
            ["Create a confidential application in the Fusion IAM domain with the client credentials grant and the Fusion Applications resource scope.",
             "Or use an integration user with Basic over HTTPS."],
            [
                U("baseUrl", "Fusion pod URL", "baseUrl", true, Conn, null, "https://abcd-test.fa.us2.oraclecloud.com"),
                C("auth", "Authentication", "option:auth", Auth, "oauth", null, "oauth", "basic"),
                T("clientId", "Client id", "clientId", false, Auth, null, null, null, "auth=oauth"),
                S("clientSecret", "Client secret", "secret", false, null, "auth=oauth"),
                U("tokenUrl", "IAM token URL", "option:tokenUrl", false, Auth, null, "https://idcs-xxxx.identity.oraclecloud.com/oauth2/v1/token", "auth=oauth"),
                T("scope", "Scope", "option:scope", false, Auth, "The Fusion Applications resource scope from the confidential application.", "urn:opc:resource:fa:instanceid=xxxxurn:opc:resource:consumer::all", null, "auth=oauth"),
                T("username", "Integration user", "username", false, Auth, null, null, null, "auth=basic"),
                S("password", "Integration user password", "secret", false, null, "auth=basic"),
                PageSize,
            ], []),
        new(ErpSystem.Dynamics, DynamicsAdapter.BusinessCentral, "Microsoft Dynamics 365 Business Central: API v2.0",
            ["Register an app in Microsoft Entra ID with the Dynamics 365 Business Central API.ReadWrite.All application permission and grant admin consent.",
             "In Business Central, open Microsoft Entra Applications, add the client id and give it the permission sets it needs."],
            [
                T("tenant", "Entra tenant id", "account", true, Conn, null, "00000000-0000-0000-0000-000000000000"),
                T("environment", "Environment name", "option:environment", true, Conn, null, "production", "production"),
                T("company", "Company id", "company", true, Conn, "GET /companies lists the ids.", "00000000-0000-0000-0000-000000000000"),
                T("clientId", "Application (client) id", "clientId", true, Auth),
                S("clientSecret", "Client secret", "secret", true),
                U("authority", "Sign-in authority", "option:authority", false, Adv, "Change only for sovereign clouds.", "https://login.microsoftonline.com"),
                U("baseUrl", "API host", "baseUrl", false, Adv, "Leave blank for https://api.businesscentral.dynamics.com."),
                PageSize,
            ], []),
        new(ErpSystem.Dynamics, DynamicsAdapter.FinanceSupplyChain, "Microsoft Dynamics 365 Finance & Supply Chain: OData",
            ["Register an app in Microsoft Entra ID and add its client id under System administration > Setup > Microsoft Entra applications with an integration user."],
            [
                U("baseUrl", "Environment URL", "baseUrl", true, Conn, null, "https://contoso.operations.dynamics.com"),
                T("tenant", "Entra tenant id", "account", true, Conn),
                T("clientId", "Application (client) id", "clientId", true, Auth),
                S("clientSecret", "Client secret", "secret", true),
                T("dataAreaId", "Legal entity (dataAreaId)", "option:dataAreaId", false, Conn, "Leave blank to read every legal entity the user can see.", "usmf"),
                U("authority", "Sign-in authority", "option:authority", false, Adv, null, "https://login.microsoftonline.com"),
                PageSize,
            ], []),
        new(ErpSystem.BcManufacturing, BcManufacturingAdapter.CustomApi, "Business Central manufacturing: extension API",
            ["Publish an extension with API pages for production orders, routings, work and machine centers and ledger entries.", "Register the Entra app as for Business Central."],
            [
                T("tenant", "Entra tenant id", "account", true, Conn),
                T("environment", "Environment name", "option:environment", true, Conn, null, null, "production"),
                T("company", "Company id", "company", true, Conn),
                T("clientId", "Application (client) id", "clientId", true, Auth),
                S("clientSecret", "Client secret", "secret", true),
                T("apiPublisher", "API publisher", "option:apiPublisher", true, End, null, "contoso"),
                T("apiGroup", "API group", "option:apiGroup", true, End, null, "manufacturing"),
                T("apiVersion", "API version", "option:apiVersion", false, End, null, null, "v1.0"),
                PageSize,
            ], []),
        new(ErpSystem.BcManufacturing, BcManufacturingAdapter.WebServices, "Business Central manufacturing: published web services",
            ["Publish the production order, routing, work center and ledger pages as OData web services (Web Services page)."],
            [
                T("tenant", "Entra tenant id", "account", true, Conn),
                T("environment", "Environment name", "option:environment", true, Conn, null, null, "production"),
                T("company", "Company name", "company", true, Conn, "Web services use the company name, not the id.", "CRONUS USA, Inc."),
                T("clientId", "Application (client) id", "clientId", true, Auth),
                S("clientSecret", "Client secret", "secret", true),
                PageSize,
            ], []),
        new(ErpSystem.Acumatica, "ContractRest", "Acumatica: contract-based REST",
            ["Register a connected application (System > Integration > Connected Applications, SM303010) with the Resource Owner Password flow; copy the client id and secret.",
             "Create an integration user with a role that can read stock items, customers, vendors, purchase orders and production orders."],
            [
                U("baseUrl", "Instance URL", "baseUrl", true, Conn, null, "https://contoso.acumatica.com"),
                T("clientId", "Client id", "clientId", true, Auth, "Looks like 00000000-0000-0000-0000-000000000000@Company."),
                S("clientSecret", "Client secret", "secondarySecret", true),
                T("username", "Integration user", "username", true, Auth),
                S("password", "Integration user password", "secret", true),
                T("defaultVersion", "Default endpoint version", "option:defaultVersion", false, End, null, null, "25.200.001"),
                T("manufacturingVersion", "MANUFACTURING endpoint version", "option:manufacturingVersion", false, End, null, null, "25.100.001"),
                PageSize,
            ], []),
        new(ErpSystem.Infor, InforAdapter.M3, "Infor M3 (CloudSuite): ION API",
            ["In ION API, create a Backend Service authorized app and download its .ionapi credentials file.", "Give the service account access to the M3 MI programs in use (MMS200MI, CRS610MI, CRS620MI, PPS200MI, PMS100MI)."],
            [
                Fl("ionapi", ".ionapi credentials file", "secret", true, "Load the downloaded file; it holds the tenant, client id and secret and token URLs."),
                C("grant", "Grant type", "option:grant", Auth, "client_credentials", "Use password only when the file includes a service account (saak / sask).", "client_credentials", "password"),
                T("cono", "M3 company (CONO)", "option:cono", false, Conn, null, "100"),
                PageSize,
            ], []),
        new(ErpSystem.Infor, InforAdapter.Mongoose, "Infor SyteLine / CSI (Mongoose IDOs)",
            ["Through ION: download a Backend Service .ionapi file. Direct: an IDO request service user."],
            [
                T("config", "Mongoose configuration name", "company", true, Conn, null, "CSI_DALS"),
                C("mode", "Connect", "option:mode", Auth, "ion", null, "ion", "direct"),
                Fl("ionapi", ".ionapi credentials file", "secret", false, null, "mode=ion"),
                T("ionProductPath", "ION product path", "option:ionProductPath", false, End, null, null, "CSI", "mode=ion"),
                U("baseUrl", "Mongoose server URL", "baseUrl", false, Conn, null, "https://csi.example.com", "mode=direct"),
                T("username", "IDO user", "username", false, Auth, null, null, null, "mode=direct"),
                S("password", "IDO user password", "secret", false, null, "mode=direct"),
                PageSize,
            ], []),
        new(ErpSystem.Epicor, "KineticRestV2", "Epicor Kinetic: REST v2",
            ["Create an API key in API Key Maintenance bound to an Access Scope that lists the services in use.", "Use a Kinetic integration user (Basic), or an Epicor Identity Provider client for client credentials."],
            [
                U("baseUrl", "Instance URL", "baseUrl", true, Conn, "Server and instance, without /api.", "https://kinetic.example.com/KineticLive"),
                T("company", "Company id", "company", true, Conn, null, "EPIC06"),
                S("apiKey", "API key (x-api-key)", "secondarySecret", true),
                C("auth", "User authentication", "option:auth", Auth, "basic", null, "basic", "idp"),
                T("username", "Integration user", "username", false, Auth, null, null, null, "auth=basic"),
                S("password", "Integration user password", "secret", false, null, "auth=basic"),
                T("clientId", "Identity Provider client id", "clientId", false, Auth, null, null, null, "auth=idp"),
                S("clientSecret", "Identity Provider client secret", "secret", false, null, "auth=idp"),
                U("tokenUrl", "Identity Provider token URL", "option:tokenUrl", false, Auth, null, "https://login.epicor.com/connect/token", "auth=idp"),
                PageSize,
            ], []),
        new(ErpSystem.Plex, "Connect", "Plex Smart Manufacturing: Plex Connect",
            ["Request a Plex Connect API key from the developer portal for the customer's tenant (production and test keys differ)."],
            [
                U("baseUrl", "Plex Connect host", "baseUrl", false, Conn, "https://connect.plex.com for production, https://test.connect.plex.com for test.", "https://connect.plex.com"),
                T("tenant", "Tenant id (X-Plex-Connect-Tenant-Id)", "account", false, Conn, "Needed when the key reaches more than one tenant."),
                S("apiKey", "API key (X-Plex-Connect-Api-Key)", "secret", true),
                PageSize,
            ], []),
        new(ErpSystem.DelmiaWorks, "WebApi", "DELMIAworks (IQMS): WebAPI",
            ["License and install the DELMIAworks WebAPI; create an integration user.", "Paths depend on the installed WebAPI version: copy them from the SDK documentation."],
            [
                U("baseUrl", "WebAPI URL", "baseUrl", true, Conn, null, "https://iqms.example.com/WebAPI"),
                T("company", "Company / EPLANT", "company", false, Conn),
                T("username", "Integration user", "username", true, Auth),
                S("password", "Password", "secret", true),
                T("loginPath", "Login path", "option:loginPath", false, End, null, null, "/User/Login"),
                T("tokenHeader", "Token header name", "option:tokenHeader", false, End, null, null, "AuthToken"),
                PageSize,
            ], []),
        new(ErpSystem.QuickBooks, "Online", "QuickBooks Online: OAuth 2.0",
            ["Create an app on the Intuit Developer portal with the com.intuit.quickbooks.accounting scope; copy the client id and secret (production keys need Intuit's app review).",
             "Connect the app to the company once (OAuth playground or the app's sign-in) and copy the refresh token and the company id (realm id)."],
            [
                T("realmId", "Company id (realm id)", "account", true, Conn, "Shown when the app is connected; also under Settings > Account and settings > Billing & subscription.", "9130350000000000"),
                T("clientId", "Client id", "clientId", true, Auth),
                S("clientSecret", "Client secret", "secret:clientSecret", true),
                S("refreshToken", "Refresh token", "secret:refreshToken", true, "Kept encrypted and replaced automatically each time Intuit issues a new one."),
                U("baseUrl", "API host", "baseUrl", false, End, "Leave blank: sandbox connections use sandbox-quickbooks.api.intuit.com, production ones quickbooks.api.intuit.com."),
                T("minorVersion", "Minor version", "option:minorVersion", false, Adv, null, null, "75"),
                PageSize,
            ], ["refreshToken"]),
        new(ErpSystem.SageIntacct, "XmlGateway", "Sage Intacct: XML Web Services",
            ["Get a Web Services sender id and password (a Web Services developer license).",
             "In the company, add the sender id under Company > Setup > Company > Security > Web Services authorizations.",
             "Create a Web Services user with read permissions for items, customers, vendors, employees and purchasing."],
            [
                T("senderId", "Sender id", "option:senderId", true, Auth),
                S("senderPassword", "Sender password", "secret:senderPassword", true),
                T("companyId", "Company id", "company", true, Conn),
                T("entityId", "Entity id", "option:entityId", false, Conn, "Multi-entity companies only; leave blank for the top level."),
                T("userId", "Web Services user id", "username", true, Auth),
                S("userPassword", "Web Services user password", "secret:userPassword", true),
                U("baseUrl", "Gateway URL", "baseUrl", false, End, "Leave blank for https://api.intacct.com/ia/xml/xmlgw.phtml."),
                PageSize,
            ], []),
        new(ErpSystem.CustomRest, "Rest", "Other ERP or MES: REST / OData",
            ["Get the base URL, an API key, bearer token or service user, and the path of each list you want to read."],
            [
                U("baseUrl", "Base URL", "baseUrl", true, Conn),
                C("auth", "Authentication", "option:auth", Auth, "bearer", null, "bearer", "apikey", "basic"),
                T("apiKeyHeader", "API key header", "option:apiKeyHeader", false, Auth, null, null, "X-Api-Key", "auth=apikey"),
                T("username", "User", "username", false, Auth, null, null, null, "auth=basic"),
                S("secret", "Token, API key or password", "secret", true),
                T("urlItem", "Items path", "option:url.Item", false, End, null, "/api/items"),
                T("itemsItem", "Items JSON path", "option:items.Item", false, End, "Where the row array sits in the response.", "data"),
                T("nextItem", "Next page JSON path", "option:next.Item", false, End, null, "links.next"),
                PageSize,
            ], []),
        new(ErpSystem.CsvExchange, "Csv", "CSV exchange",
            ["Export a CSV with a header row from the ERP; upload it on the connection's mapping."],
            [], []),
    ];

    public static SetupProfile? For(ErpSystem system, string edition) => All.FirstOrDefault(p => p.System == system && p.Edition == edition);
    public static IReadOnlyList<SetupProfile> Profiles => All;

    /// <summary>Whether a field applies, given the connection's options (ShowWhen "key=value").</summary>
    public static bool Applies(SetupField f, JsonObject options)
    {
        if (f.ShowWhen is null) return true;
        var i = f.ShowWhen.IndexOf('=');
        var key = f.ShowWhen[..i]; var want = f.ShowWhen[(i + 1)..];
        var field = All.SelectMany(p => p.Fields).FirstOrDefault(x => x.Target == "option:" + key);
        var have = options[key]?.ToString() is { Length: > 0 } v ? v : field?.Default ?? "";
        return have == want;
    }

    /// <summary>Labels of required fields that are empty on the connection (secrets count as set when stored).</summary>
    public static List<string> Missing(ErpConnection c, JsonObject options, ISet<string> secretNames)
    {
        var p = For(c.System, c.Edition);
        if (p is null) return [];
        var missing = new List<string>();
        foreach (var f in p.Fields.Where(f => f.Required && Applies(f, options)))
        {
            var set = f.Target switch
            {
                "baseUrl" => !string.IsNullOrWhiteSpace(c.BaseUrl),
                "account" => !string.IsNullOrWhiteSpace(c.Account),
                "company" => !string.IsNullOrWhiteSpace(c.Company),
                "clientId" => !string.IsNullOrWhiteSpace(c.ClientId),
                "username" => !string.IsNullOrWhiteSpace(c.Username),
                "secret" => c.EncryptedSecret is not null,
                "secondarySecret" => c.EncryptedSecondarySecret is not null,
                var t when t.StartsWith("secret:") => secretNames.Contains(t[7..]),
                var t when t.StartsWith("option:") => options[t[7..]]?.ToString() is { Length: > 0 } || f.Default is not null,
                _ => true,
            };
            if (!set) missing.Add(f.Label);
        }
        return missing;
    }
}
