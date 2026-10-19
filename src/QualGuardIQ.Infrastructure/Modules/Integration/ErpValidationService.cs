using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Integration;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Integration.Erp;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Integration;

public sealed record ChecklistStep(string Step, string Text, ErpValidationGate? Gate);
public sealed record AssumptionInput(string Topic, ErpValidationResult Result, string Notes);
public sealed record ProductionApprovalInput(string Notes);

/// <summary>
/// Live-validation state of ERP connections, on top of the connector framework. Gates (connection, authentication, read, write,
/// production approval) and confirmations of each adapter's unverified vendor assumptions are recorded as append-only events
/// against the connection's configuration fingerprint. A connection can be enabled only when its required gates pass for the
/// current configuration. Nothing here contacts a vendor: gate results come from the connection test the administrator runs.
/// </summary>
public sealed class ErpValidationService(AppDbContext db, ModuleSupport m, ErpAdapterRegistry adapters)
{
    /// <summary>
    /// What the automated fixture tests (recorded vendor responses, no live system) cover for each adapter in this build, in words an
    /// administrator can read. The tests are ErpAdapterTests (per vendor), GapClosureBcManufacturingTests, ManufacturingConnectivityTests
    /// and the EnterpriseTests CSV import.
    /// </summary>
    public static readonly IReadOnlyDictionary<ErpSystem, string> FixtureTests = new Dictionary<ErpSystem, string>
    {
        [ErpSystem.Sap] = "Recorded SAP responses: S/4HANA OData V2 reads with paging; Business One sign-in with session cookies.",
        [ErpSystem.Oracle] = "Recorded Oracle Fusion responses: client-credentials token and limit/offset paging.",
        [ErpSystem.Epicor] = "Recorded Epicor Kinetic responses: API key with Basic authentication and skip paging.",
        [ErpSystem.Infor] = "Recorded Infor responses: M3 reads through ION; Mongoose IDO loads.",
        [ErpSystem.Dynamics] = "Recorded Dynamics 365 Business Central responses: Entra token and next-link paging.",
        [ErpSystem.NetSuite] = "Recorded NetSuite responses: PS256 signed assertion (RS256 refused) and error details reported; token-based sign-in with a known HMAC-SHA256 signature through SuiteQL and the RESTlet.",
        [ErpSystem.Acumatica] = "Recorded Acumatica responses: password grant with the api scope and value-wrapped fields.",
        [ErpSystem.Plex] = "Recorded Plex responses: Connect API key and tenant headers.",
        [ErpSystem.DelmiaWorks] = "Recorded DELMIAworks responses: sign-in and the documented customers path.",
        [ErpSystem.BcManufacturing] = "Recorded Business Central manufacturing responses: extension API and web services pages, connection diagnostics, sync of production orders, order lines, machine centers and serials.",
        [ErpSystem.QuickBooks] = "Recorded QuickBooks Online responses: refresh-token sign-in keeping the rotated refresh token, start-position paging and fault messages.",
        [ErpSystem.SageIntacct] = "Recorded Sage Intacct responses: sender and user credentials, readMore paging and an unauthorized sender reported in plain words.",
        [ErpSystem.CustomRest] = "Recorded generic REST responses through the connector framework.",
        [ErpSystem.CsvExchange] = "CSV parsing with quotes, commas and line breaks; CSV import of master data.",
    };

    /// <summary>Assumptions that stay visibly unverified until a person confirms them after a passing live read (Gap 25 list).</summary>
    public static readonly IReadOnlyDictionary<ErpSystem, string[]> KeyAssumptions = new Dictionary<ErpSystem, string[]>
    {
        [ErpSystem.NetSuite] = ["Transaction types"],
        [ErpSystem.Sap] = ["Entities", "Field names", "Entity and field names"],
        [ErpSystem.Oracle] = ["Pod host"],
        [ErpSystem.Plex] = ["Paths", "Fields and paging", "Customer Id header"],
        [ErpSystem.DelmiaWorks] = ["Whole contract", "Login"],
        [ErpSystem.BcManufacturing] = ["Extension", "Published pages", "Field names", "Keys"],
    };

    static readonly ChecklistStep[] Common =
    [
        new("Environment", "Point the connection at the customer's sandbox or test company first and set Environment accordingly. Production comes last.", null),
        new("Integration user", "Use a dedicated integration user or client (never a person's login) with read-only permissions for the entities you map.", null),
        new("Secrets", "Enter secrets in the connection's write-only secret fields. They are encrypted at rest and never shown again.", null),
        new("Connection", "Run Test connection. The host must answer over HTTPS (connection gate).", ErpValidationGate.Connection),
        new("Authentication", "The test must obtain a token or session with the configured credentials (authentication gate).", ErpValidationGate.Authentication),
        new("Read", "The test must read one page of the test entity (read gate). Compare a few rows with the ERP screen.", ErpValidationGate.Read),
        new("Assumptions", "Confirm or reject each unverified item below against the customer's system, with notes on what you checked.", ErpValidationGate.Assumption),
        new("Write", "Not applicable: every adapter in this build is read-only.", ErpValidationGate.Write),
        new("Production approval", "For a production connection, a second person with integrations.approve reviews the results and approves before it is enabled.", ErpValidationGate.ProductionApproval),
    ];

    static readonly Dictionary<ErpSystem, ChecklistStep[]> Specific = new()
    {
        [ErpSystem.Sap] = [new("SAP communication arrangement", "S/4HANA Cloud: create the communication arrangement for each cited scenario and use the URL it shows. Business One: use a test company database (SAP has no vendor sandbox).", null),
            new("SAP entity names", "Open $metadata for each service and confirm the entity set and field names the adapter uses (A_Product, A_Customer, ProductTrees …).", ErpValidationGate.Assumption)],
        [ErpSystem.Oracle] = [new("Oracle Fusion pod host", "Use the customer's own Fusion pod URL (https://<pod>.fa.<region>.oraclecloud.com) and confirm the IDCS token URL and scope.", ErpValidationGate.Assumption),
            new("Oracle roles", "Grant the integration user the job/duty roles listed under each REST endpoint's Required roles.", null)],
        [ErpSystem.Epicor] = [new("Epicor API key", "Create an API key scoped to the access scope for the services mapped, and the integration user's company and site.", null)],
        [ErpSystem.Infor] = [new("Infor ION", "Download the .ionapi file for a backend service account and paste its values; confirm the tenant (ti) and the M3 or Mongoose configuration.", null)],
        [ErpSystem.Dynamics] = [new("Dynamics app registration", "Register the Entra application, grant it in Business Central (Microsoft Entra applications) or Finance and Operations, and use a sandbox environment.", null),
            new("Dynamics manufacturing", "Business Central's standard API has no manufacturing entities; production data needs the BC manufacturing extension adapter or a custom API page.", null)],
        [ErpSystem.NetSuite] = [new("NetSuite certificate", "Map the PS256 certificate to the integration role in OAuth 2.0 Client Credentials (M2M) Setup; redo it after each sandbox refresh.", null),
            new("NetSuite transaction codes", "Run a SuiteQL query in the sandbox to confirm the purchase order and work order transaction type codes and column names.", ErpValidationGate.Assumption)],
        [ErpSystem.Acumatica] = [new("Acumatica connected application", "Create the connected application (resource owner password grant) and confirm the endpoint name and version.", null)],
        [ErpSystem.Plex] = [new("Plex developer portal", "Request Plex Connect API access through Plex support, then confirm each collection path and the tenant header against the portal.", ErpValidationGate.Assumption)],
        [ErpSystem.DelmiaWorks] = [new("DELMIAworks WebAPI", "Confirm the WebAPI is licensed, then set the login path, token header and every entity path from the licensed SDK help. Otherwise use a CSV exchange connection.", ErpValidationGate.Assumption)],
        [ErpSystem.BcManufacturing] = [new("Business Central manufacturing source", "Choose CustomApi only if a manufacturing extension with API pages is installed (enter its apiPublisher, apiGroup, apiVersion); otherwise choose WebServices and publish the standard pages on the Web Services page. Grant the Entra application read permission sets for production orders, routings, work and machine centers, item and capacity ledger entries and the output/consumption journals.", null),
            new("Business Central manufacturing mapping", "Run Test connection: it probes every entity in the entities option and lists unreachable pages and mapped fields missing from the sample row. Correct entitySet.<Entity>, idField.<Entity> and the field maps until the test passes, then confirm the assumptions.", ErpValidationGate.Assumption)],
        [ErpSystem.QuickBooks] = [new("QuickBooks Online app", "Create the app in the Intuit Developer portal with the com.intuit.quickbooks.accounting scope, authorize it once against a sandbox company and paste the realm ID and refresh token.", null)],
        [ErpSystem.SageIntacct] = [new("Sage Intacct Web Services", "Subscribe the company to Web Services, add the sender ID to the company's Web Services authorizations and use a dedicated Web Services user.", null)],
        [ErpSystem.CustomRest] = [new("Generic REST", "Set url.<Entity> and field maps from the customer's API documentation; confirm paging and the since parameter.", null)],
        [ErpSystem.CsvExchange] = [new("CSV", "Agree the column layout with the customer and upload a small sample file after enabling; the first accepted upload records the read gate.", ErpValidationGate.Read)],
    };

    public static IReadOnlyList<ChecklistStep> Checklist(ErpSystem s) => [.. Common.Take(3), .. Specific.GetValueOrDefault(s, []), .. Common.Skip(3)];

    /// <summary>Hash of everything that changes what a test proves: system, edition, endpoints, identities, options, secrets and environment.</summary>
    public static string Fingerprint(ErpConnection c) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\u001f",
        c.System, c.Edition, c.BaseUrl, c.Account, c.Company, c.ClientId, c.Username, Canonical(c.Options), c.EncryptedSecret, c.EncryptedSecondarySecret, c.Environment)
        + NamedSecrets(c))));

    /// <summary>Named secrets in the fingerprint, except ones the vendor rotates on its own (a refresh token), so rotation does not reset validation.</summary>
    static string NamedSecrets(ErpConnection c)
    {
        var bag = ErpSyncService.SecretBag(c);
        if (bag.Count == 0) return "";
        var rotating = Erp.ErpSetupProfiles.For(c.System, c.Edition)?.RotatingSecrets ?? [];
        return "\u001f" + string.Join("\u001f", bag.Where(kv => !rotating.Contains(kv.Key)).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value));
    }

    /// <summary>Options JSON with sorted keys and no whitespace, so the stored jsonb text and freshly serialized options hash the same.</summary>
    static string Canonical(string json)
    {
        static System.Text.Json.Nodes.JsonNode? Sort(System.Text.Json.Nodes.JsonNode? n) => n switch
        {
            System.Text.Json.Nodes.JsonObject o => new System.Text.Json.Nodes.JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, Sort(p.Value)))),
            System.Text.Json.Nodes.JsonArray a => new System.Text.Json.Nodes.JsonArray(a.Select(Sort).ToArray()),
            _ => n?.DeepClone(),
        };
        try { return Sort(System.Text.Json.Nodes.JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json))?.ToJsonString() ?? "{}"; }
        catch (System.Text.Json.JsonException) { return json; }
    }

    static bool NoHttp(ErpSystem s) => s == ErpSystem.CsvExchange;

    async Task<string> NameAsync(CancellationToken ct) => await db.Users.Where(u => u.Id == m.UserId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct) ?? "unknown";

    void Add(ErpConnection c, ErpValidationGate g, ErpValidationResult r, Guid? by, string byName, DateTimeOffset at, string? notes, string? topic = null) =>
        db.ErpValidationEvents.Add(new ErpValidationEvent { ConnectionId = c.Id, Gate = g, Result = r, Topic = topic, Environment = c.Environment, ConfigFingerprint = Fingerprint(c), ById = by, ByName = byName, At = at, Notes = notes });

    /// <summary>Records the gates a connection test proved or failed. Called by ErpSyncService.TestAsync (not saved here).</summary>
    public async Task RecordTestAsync(ErpConnection c, bool ok, Exception? error, string message, CancellationToken ct)
    {
        var by = m.UserId; var name = await NameAsync(ct); var at = m.Now;
        void G(ErpValidationGate g, ErpValidationResult r, string? n = null) => Add(c, g, r, by, name, at, n ?? message);
        if (NoHttp(c.System))
        {
            G(ErpValidationGate.Connection, ErpValidationResult.NotApplicable, "CSV exchange makes no network connection.");
            G(ErpValidationGate.Authentication, ErpValidationResult.NotApplicable, "CSV exchange has no credentials.");
        }
        else
        {
            // The first gate that failed; gates before it passed, gates after it were not reached (recorded as failed so an older pass no longer counts).
            ErpValidationGate? failed = ok ? null : error switch
            {
                HttpRequestException or TaskCanceledException => ErpValidationGate.Connection,
                ErpException { Status: 401 or 403 } => ErpValidationGate.Authentication,
                ErpException { Status: not null } or System.Text.Json.JsonException => ErpValidationGate.Read,
                _ => ErpValidationGate.Authentication,   // missing secret, no token in the response, decryption failure
            };
            var reachedHost = ok || error is ErpException { Status: not null } or System.Text.Json.JsonException;
            foreach (var g in new[] { ErpValidationGate.Connection, ErpValidationGate.Authentication, ErpValidationGate.Read })
                if (failed is null || g < failed)
                    G(g, reachedHost || failed is null ? ErpValidationResult.Passed : ErpValidationResult.Failed, failed is null ? null : reachedHost ? "Reached; a later step failed." : "Not attempted: " + message);
                else if (g == failed) G(g, ErpValidationResult.Failed);
                else G(g, ErpValidationResult.Failed, $"Not reached: the {failed.Value.ToString().ToLowerInvariant()} step failed.");
        }
        G(ErpValidationGate.Write, ErpValidationResult.NotApplicable, "Read-only adapter: no write path exists.");
    }

    /// <summary>First accepted CSV upload proves the read gate for a CSV exchange connection.</summary>
    public async Task RecordCsvReadAsync(ErpConnection c, int rows, CancellationToken ct)
    {
        var fp = Fingerprint(c);
        if (await db.ErpValidationEvents.AnyAsync(e => e.ConnectionId == c.Id && e.ConfigFingerprint == fp && e.Gate == ErpValidationGate.Read && e.Result == ErpValidationResult.Passed, ct)) return;
        Add(c, ErpValidationGate.Read, ErpValidationResult.Passed, m.UserId, await NameAsync(ct), m.Now, $"CSV upload accepted ({rows} row(s)).");
    }

    public sealed record GateRow(ErpValidationGate Gate, string Status, DateTimeOffset? At, string? By, ErpEnvironment? Environment, string? Notes, bool Required);
    public sealed record AssumptionRow(string Topic, string Detail, string Source, bool Key, string Status, DateTimeOffset? At, string? By, ErpEnvironment? Environment, string? Notes);
    public sealed record Status(Guid ConnectionId, ErpEnvironment Environment, string FixtureTested, IReadOnlyList<GateRow> Gates, IReadOnlyList<AssumptionRow> Assumptions,
        IReadOnlyList<ErpEntity> SupportedEntities, IReadOnlyList<ErpEntity> UnsupportedEntities, IReadOnlyList<ChecklistStep> Checklist, bool CanEnable, IReadOnlyList<string> Blockers,
        bool ProductionApproved, DateTimeOffset? LastTestAt, string? LastTestedBy, string Disclaimer);

    public const string Disclaimer = "Fixture tests use recorded vendor responses. They do not show that a connector works against a customer's system; only the gates below, run against that system, do.";

    public async Task<Status> StatusAsync(ErpConnection c, CancellationToken ct)
    {
        var fp = Fingerprint(c);
        var events = await db.ErpValidationEvents.AsNoTracking().Where(e => e.ConnectionId == c.Id && e.ConfigFingerprint == fp).OrderByDescending(e => e.At).ThenByDescending(e => e.CreatedAt).ToListAsync(ct);
        var a = adapters.Get(c.System);
        var cap = a.Capabilities(c.Edition);
        var noHttp = NoHttp(c.System);
        GateRow Row(ErpValidationGate g, bool required)
        {
            var e = events.FirstOrDefault(x => x.Gate == g);
            return new(g, e is null ? "Not run" : e.Result switch { ErpValidationResult.Passed => "Passed", ErpValidationResult.Failed => "Failed", ErpValidationResult.NotApplicable => "Not applicable", ErpValidationResult.Revoked => "Revoked", _ => e.Result.ToString() },
                e?.At, e?.ByName, e?.Environment, e?.Notes, required);
        }
        var prod = c.Environment == ErpEnvironment.Production;
        var gates = new List<GateRow> { Row(ErpValidationGate.Connection, !noHttp), Row(ErpValidationGate.Authentication, !noHttp), Row(ErpValidationGate.Read, !noHttp), Row(ErpValidationGate.Write, false), Row(ErpValidationGate.ProductionApproval, prod) };
        var keys = KeyAssumptions.GetValueOrDefault(c.System, []);
        var assumptions = a.Unverified(c.Edition).Select(u =>
        {
            var e = events.FirstOrDefault(x => x.Gate == ErpValidationGate.Assumption && x.Topic == u.Topic);
            var st = e?.Result switch { ErpValidationResult.Confirmed => "Confirmed", ErpValidationResult.Rejected => "Rejected", _ => "Unverified" };
            return new AssumptionRow(u.Topic, u.Detail, u.Source, keys.Contains(u.Topic), st, e?.At, e?.ByName, e?.Environment, e?.Notes);
        }).ToList();
        var blockers = new List<string>();
        foreach (var g in gates.Where(g => g.Required && g.Gate != ErpValidationGate.ProductionApproval && g.Status != "Passed")) blockers.Add($"{g.Gate} gate: {g.Status.ToLowerInvariant()} for the current configuration.");
        if (noHttp && events.All(x => !(x.Gate == ErpValidationGate.Connection && x.Result == ErpValidationResult.NotApplicable))) blockers.Add("Run Test connection once for the current configuration.");
        if (prod && gates.First(g => g.Gate == ErpValidationGate.ProductionApproval).Status != "Passed") blockers.Add("Production approval: a production connection needs approval by a second person with integrations.approve.");
        var lastTest = events.FirstOrDefault(x => x.Gate is ErpValidationGate.Connection or ErpValidationGate.Authentication or ErpValidationGate.Read);
        return new Status(c.Id, c.Environment, FixtureTests.GetValueOrDefault(c.System, "None"), gates, assumptions, cap.Read, Enum.GetValues<ErpEntity>().Except(cap.Read).ToList(), Checklist(c.System),
            blockers.Count == 0, blockers, prod && gates.First(g => g.Gate == ErpValidationGate.ProductionApproval).Status == "Passed", lastTest?.At, lastTest?.ByName, Disclaimer);
    }

    /// <summary>Throws unless the connection's required validation gates pass for its current configuration.</summary>
    public async Task RequireGateAsync(ErpConnection c, CancellationToken ct)
    {
        var s = await StatusAsync(c, ct);
        if (!s.CanEnable) throw new AppException("The connection cannot be enabled until its validation gate passes: " + string.Join(" ", s.Blockers));
    }

    /// <summary>A person records what they checked for one unverified vendor assumption. Needs a passing live read first, so it is never confirmed from fixtures.</summary>
    public async Task RecordAssumptionAsync(ErpConnection c, AssumptionInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.IntegrationsEdit, c.SiteId, c.DepartmentId, ct);
        var a = adapters.Get(c.System);
        if (!a.Unverified(c.Edition).Any(u => u.Topic == r.Topic)) throw new AppException($"'{r.Topic}' is not an unverified item for {c.System} ({c.Edition}).");
        if (r.Result is not (ErpValidationResult.Confirmed or ErpValidationResult.Rejected or ErpValidationResult.Revoked)) throw new AppException("Record Confirmed, Rejected or Revoked.");
        ModuleSupport.Required(r.Notes, "Notes (what you checked and where)", 4000);
        if (r.Result == ErpValidationResult.Confirmed && !NoHttp(c.System) && (await StatusAsync(c, ct)).Gates.First(g => g.Gate == ErpValidationGate.Read).Status != "Passed")
            throw new AppException("Confirm an assumption only after a passing read test against the customer's system for the current configuration.");
        Add(c, ErpValidationGate.Assumption, r.Result, m.UserId, await NameAsync(ct), m.Now, r.Notes.Trim(), r.Topic);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Production approval: integrations.approve at the connection's site, not the person who ran the passing test, all gates passed and no key assumption unverified or rejected.</summary>
    public async Task ApproveProductionAsync(ErpConnection c, ProductionApprovalInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.IntegrationsApprove, c.SiteId, c.DepartmentId, ct);
        if (c.Environment != ErpEnvironment.Production) throw new AppException("Only a connection whose environment is Production needs production approval.");
        ModuleSupport.Required(r.Notes, "Approval notes", 4000);
        var s = await StatusAsync(c, ct);
        var open = s.Blockers.Where(b => !b.StartsWith("Production approval", StringComparison.Ordinal)).ToList();
        if (open.Count > 0) throw new AppException("Approve only after the gates pass: " + string.Join(" ", open));
        var keyOpen = s.Assumptions.Where(x => x.Key && x.Status != "Confirmed").Select(x => x.Topic).ToList();
        if (keyOpen.Count > 0) throw new AppException("These key assumptions must be confirmed against the live system first: " + string.Join(", ", keyOpen) + ".");
        var fp = Fingerprint(c);
        var tester = await db.ErpValidationEvents.AsNoTracking().Where(e => e.ConnectionId == c.Id && e.ConfigFingerprint == fp && e.Gate == ErpValidationGate.Read && e.Result == ErpValidationResult.Passed)
            .OrderByDescending(e => e.At).Select(e => e.ById).FirstOrDefaultAsync(ct);
        if (tester == m.UserId) throw new ForbiddenException("The person who ran the passing test cannot approve the connection for production.");
        Add(c, ErpValidationGate.ProductionApproval, ErpValidationResult.Passed, m.UserId, await NameAsync(ct), m.Now, r.Notes.Trim());
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<ErpValidationEvent>> HistoryAsync(Guid connectionId, CancellationToken ct) =>
        await db.ErpValidationEvents.AsNoTracking().Where(e => e.ConnectionId == connectionId).OrderByDescending(e => e.At).Take(500).ToListAsync(ct);
}
