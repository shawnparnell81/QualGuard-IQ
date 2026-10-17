using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Integration;
using QualGuardIQ.Infrastructure.Modules.Inventory;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Logistics;

public sealed record CarrierConnectionInput(Guid CarrierId, string Name, CarrierProvider Provider, CarrierEnvironment Environment, string? BaseUrl, string? AccountNumber, string? ClientId,
    string? OptionsJson, CarrierAddress? Shipper, Guid SiteId, string? Secret = null, bool ClearSecret = false);
public sealed record CarrierConnectionRow(Guid Id, Guid CarrierId, string Carrier, string Name, CarrierProvider Provider, CarrierEnvironment Environment, string? BaseUrl, string? AccountNumber, string? ClientId,
    bool HasSecret, string OptionsJson, CarrierAddress Shipper, bool Enabled, CarrierVerification Verification, DateTimeOffset? VerifiedAt, string? VerifiedBy, DateTimeOffset? LastTestAt, string? LastTestMessage,
    bool ProductionApproved, DateTimeOffset? ApprovedAt, string? ApprovedBy, string? ApprovalNotes, Guid SiteId, string Site, CarrierCapabilities Capabilities, List<string> Blockers);
public sealed record CarrierApprovalInput(string Notes);
public sealed record CarrierEnableInput(bool Enabled);
public sealed record ShipToInput(CarrierAddress Address);
public sealed record CarrierChoice(Guid ConnectionId);
public sealed record ServiceChoice(Guid ConnectionId, string ServiceCode);
public sealed record CreateLabelsInput(string? Instructions);
public sealed record VoidLabelsInput(string Reason);
public sealed record AddressResult(bool Valid, CarrierAddress? Normalized, List<string> Messages, bool Applied);
public sealed record LabelRow(Guid Id, int PackageNumber, Guid? ContainerId, string? Container, string CarrierShipmentId, string TrackingNumber, string ServiceCode, string Format, int Bytes, decimal? Charge, string? Currency,
    CarrierLabelStatus Status, DateTimeOffset CreatedOn, DateTimeOffset? VoidedAt, string? VoidMessage);
public sealed record RequestLogRow(Guid Id, CarrierOperation Operation, CarrierEnvironment Environment, int Attempt, CarrierCallResult Result, int? HttpStatus, string? ErrorCode, string? Message, int DurationMs, DateTimeOffset At, string? By, string? Connection);
public sealed record TrackingRow(DateTimeOffset OccurredAt, string TrackingNumber, string Code, string Description, string? Location, bool Delivered);
public sealed record ShipmentCarrierView(CarrierAddress Address, DateTimeOffset? AddressValidatedAt, string? AddressValidationNote, Guid? ConnectionId, string? Connection, CarrierEnvironment? Environment,
    string? ServiceCode, string? ServiceLevel, decimal? QuotedCharge, string? QuotedCurrency, List<LabelRow> Labels, List<TrackingRow> Tracking, List<RequestLogRow> Requests, List<CarrierPackage> Packages);
public sealed record VoidResult(int Voided, int Failed, List<string> Messages);
public sealed record TrackResult(int NewEvents, bool Delivered, List<TrackingRow> Events);

/// <summary>
/// Carrier provider framework: carrier accounts per site (sandbox by default, disabled by default, encrypted credentials), adapters with
/// capability metadata, connection tests that verify the exact configuration, production approval by a second person, and the shipment
/// operations: address validation, rating and service selection, shipment and label creation (idempotent), label retrieval, cancellation
/// (void) and tracking updates. Every adapter call is logged with its attempt, result and duration; transient failures are retried.
/// No carrier account or credential is built in, and nothing claims a live carrier result that did not come from a configured connection.
/// </summary>
public sealed class CarrierIntegrationService(AppDbContext db, ModuleSupport m, IEnumerable<ICarrierAdapter> adapters, SecretProtector secrets, CarrierHttpHandlerProvider http, ILogger<CarrierIntegrationService> log)
{
    public const int MaxAttempts = 3;
    public const string Disclaimer = "A passing test proves only that this configuration answered; it is not a carrier certification. Sandbox results are not shipments.";

    ICarrierAdapter Adapter(CarrierProvider p) => adapters.FirstOrDefault(a => a.Provider == p) ?? throw new AppException($"No adapter for {p}.");
    public List<CarrierCapabilities> Catalog() => Enum.GetValues<CarrierProvider>().Select(p => Adapter(p).Capabilities(new CarrierConnection { Provider = p })).ToList();

    public static string Fingerprint(CarrierConnection c) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\u001f",
        c.Provider, c.Environment, c.BaseUrl, c.AccountNumber, c.ClientId, c.SecretCiphertext, CanonicalOptions(c.OptionsJson), c.CarrierId,
        c.ShipperName, c.ShipperLine1, c.ShipperLine2, c.ShipperCity, c.ShipperRegion, c.ShipperPostalCode, c.ShipperCountry, c.ShipperPhone))));

    static string CanonicalOptions(string? json)
    {
        static JsonNode? Sort(JsonNode? n) => n switch
        {
            JsonObject o => new JsonObject(o.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => KeyValuePair.Create(kv.Key, Sort(kv.Value?.DeepClone())))),
            JsonArray a => new JsonArray(a.Select(x => Sort(x?.DeepClone())).ToArray()),
            _ => n?.DeepClone(),
        };
        try { return Sort(JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json))?.ToJsonString() ?? "{}"; } catch (JsonException) { return json ?? ""; }
    }

    static CarrierAddress Shipper(CarrierConnection c) => new(c.ShipperName, c.ShipperLine1, c.ShipperLine2, c.ShipperCity, c.ShipperRegion, c.ShipperPostalCode, c.ShipperCountry, c.ShipperPhone, false);
    static CarrierAddress ShipTo(Shipment s) => new(s.ShipToName, s.ShipToLine1, s.ShipToLine2, s.ShipToCity, s.ShipToRegion, s.ShipToPostalCode, s.ShipToCountry, s.ShipToPhone, s.ShipToResidential);

    /// <summary>Why the connection cannot be enabled for the current configuration (empty = it can).</summary>
    List<string> Blockers(CarrierConnection c, Carrier? carrier)
    {
        var b = new List<string>();
        var cap = Adapter(c.Provider).Capabilities(c);
        var fp = Fingerprint(c);
        if (!cap.AnyShipmentOperation) b.Add($"{cap.Provider} is an account slot: shipping operations are not available through it yet.");
        if (c.Verification != CarrierVerification.Verified || c.VerifiedFingerprint != fp) b.Add("Run a connection test that passes for the current configuration.");
        if (c.Environment == CarrierEnvironment.Production && c.ApprovedFingerprint != fp) b.Add("A production connection needs approval by a second person with integrations.approve.");
        if (carrier is { Active: false }) b.Add($"Carrier {carrier.Code} is inactive.");
        if (string.IsNullOrWhiteSpace(c.ShipperLine1) || string.IsNullOrWhiteSpace(c.ShipperCity) || string.IsNullOrWhiteSpace(c.ShipperPostalCode) || string.IsNullOrWhiteSpace(c.ShipperCountry))
            b.Add("Enter the shipper address the account is registered to.");
        return b;
    }

    // ------------------------------------------------------------------ connections
    public async Task<List<CarrierConnectionRow>> ConnectionsAsync(Guid? siteId, CancellationToken ct)
    {
        var shp = await m.TryScopeAsync(db.CarrierConnections.AsNoTracking(), Permissions.ShippingView, ct);
        var integ = await m.TryScopeAsync(db.CarrierConnections.AsNoTracking(), Permissions.IntegrationsView, ct);
        if (shp is null && integ is null) throw new ForbiddenException();
        var q = shp is not null && integ is not null ? shp.Union(integ) : (shp ?? integ)!;
        if (siteId is { } s) q = q.Where(x => x.SiteId == s);
        var list = await q.OrderBy(x => x.Name).ToListAsync(ct);
        var carriers = await db.Carriers.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        var sites = await db.Sites.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var userIds = list.SelectMany(x => new[] { x.VerifiedById, x.ApprovedById }).OfType<Guid>().Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return list.Select(c => Row(c, carriers.GetValueOrDefault(c.CarrierId), sites.GetValueOrDefault(c.SiteId) ?? "", users)).ToList();
    }

    CarrierConnectionRow Row(CarrierConnection c, Carrier? carrier, string site, Dictionary<Guid, string> users)
    {
        var fp = Fingerprint(c);
        var ver = c.VerifiedFingerprint == fp ? c.Verification : CarrierVerification.Unverified;
        return new(c.Id, c.CarrierId, carrier?.Name ?? "", c.Name, c.Provider, c.Environment, c.BaseUrl, c.AccountNumber, c.ClientId, c.SecretCiphertext is not null, c.OptionsJson, Shipper(c), c.Enabled, ver,
            ver == CarrierVerification.Verified ? c.VerifiedAt : null, ver == CarrierVerification.Verified && c.VerifiedById is { } v ? users.GetValueOrDefault(v) : null, c.LastTestAt, c.LastTestMessage,
            c.Environment == CarrierEnvironment.Production && c.ApprovedFingerprint == fp, c.ApprovedFingerprint == fp ? c.ApprovedAt : null, c.ApprovedFingerprint == fp && c.ApprovedById is { } a ? users.GetValueOrDefault(a) : null,
            c.ApprovedFingerprint == fp ? c.ApprovalNotes : null, c.SiteId, site, Adapter(c.Provider).Capabilities(c), Blockers(c, carrier));
    }

    public async Task<CarrierConnectionRow> ConnectionAsync(Guid id, CancellationToken ct)
    {
        var c = await db.CarrierConnections.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Carrier connection");
        if (!await m.CanAsync(Permissions.ShippingView, c.SiteId, c.DepartmentId, ct) && !await m.CanAsync(Permissions.IntegrationsView, c.SiteId, c.DepartmentId, ct)) throw new NotFoundException("Carrier connection");
        var ids = new[] { c.VerifiedById, c.ApprovedById }.OfType<Guid>().ToList();
        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return Row(c, await db.Carriers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == c.CarrierId, ct), await db.Sites.AsNoTracking().Where(x => x.Id == c.SiteId).Select(x => x.Name).FirstAsync(ct), users);
    }

    public async Task<CarrierConnection> SaveConnectionAsync(Guid? id, CarrierConnectionInput r, CancellationToken ct)
    {
        ModuleSupport.Required(r.Name, "Connection name", 200);
        var carrier = await db.Carriers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.CarrierId, ct) ?? throw new AppException("Select the carrier.");
        if (r.Provider == CarrierProvider.Fixture && r.Environment == CarrierEnvironment.Production) throw new AppException("The fixture carrier is simulated and can only be used in sandbox mode.");
        var cap = Adapter(r.Provider).Capabilities(new CarrierConnection { Provider = r.Provider, Environment = r.Environment });
        if (cap.RequiresBaseUrl) try { RestCarrierAdapter.Validate(r.BaseUrl); } catch (CarrierFailure e) { throw new AppException(e.Message); }
        if (r.BaseUrl?.Length > 500 || r.AccountNumber?.Length > 100 || r.ClientId?.Length > 200 || r.Secret?.Length > 4000) throw new AppException("A connection field is too long.");
        JsonObject opts;
        try { opts = JsonNode.Parse(string.IsNullOrWhiteSpace(r.OptionsJson) ? "{}" : r.OptionsJson) as JsonObject ?? throw new AppException("Options must be a JSON object."); }
        catch (JsonException) { throw new AppException("Options must be valid JSON."); }
        foreach (var k in opts.Select(kv => kv.Key))
            if (k.Contains("secret", StringComparison.OrdinalIgnoreCase) || k.Contains("password", StringComparison.OrdinalIgnoreCase) || (k.Contains("token", StringComparison.OrdinalIgnoreCase) && k != "tokenUrl"))
                throw new AppException($"Option \"{k}\" looks like a credential; enter credentials in the secret field so they are encrypted.");
        var sh = r.Shipper;
        if (sh?.Country is { Length: > 0 } cc && cc.Trim().Length != 2) throw new AppException("Use the two-letter country code for the shipper address.");
        CarrierConnection c;
        if (id is { } cid)
        {
            c = await db.CarrierConnections.FirstOrDefaultAsync(x => x.Id == cid, ct) ?? throw new NotFoundException("Carrier connection");
            await m.RequireAsync(Permissions.IntegrationsEdit, c.SiteId, c.DepartmentId, ct);
            if (r.SiteId != c.SiteId) throw new AppException("A carrier connection cannot move to another site; add it at the other site.");
        }
        else
        {
            await m.ValidateLocationAsync(r.SiteId, null, ct);
            await m.RequireAsync(Permissions.IntegrationsEdit, r.SiteId, null, ct);
            c = new CarrierConnection { SiteId = r.SiteId, Enabled = false, Verification = CarrierVerification.Unverified };
            db.CarrierConnections.Add(c);
        }
        var before = id is null ? null : Fingerprint(c);
        static string? T(string? s, int max = 200) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length > max ? throw new AppException("An address field is too long.") : s.Trim();
        c.CarrierId = carrier.Id; c.Name = r.Name.Trim(); c.Provider = r.Provider; c.Environment = r.Environment; c.BaseUrl = T(r.BaseUrl, 500); c.AccountNumber = T(r.AccountNumber, 100); c.ClientId = T(r.ClientId);
        c.OptionsJson = opts.ToJsonString();
        c.ShipperName = T(sh?.Name); c.ShipperLine1 = T(sh?.Line1); c.ShipperLine2 = T(sh?.Line2); c.ShipperCity = T(sh?.City); c.ShipperRegion = T(sh?.Region); c.ShipperPostalCode = T(sh?.PostalCode, 20);
        c.ShipperCountry = T(sh?.Country, 2)?.ToUpperInvariant(); c.ShipperPhone = T(sh?.Phone, 40);
        if (!string.IsNullOrEmpty(r.Secret)) c.SecretCiphertext = secrets.Protect(r.Secret);
        else if (r.ClearSecret) c.SecretCiphertext = null;
        // any configuration change makes the earlier test and approval stop counting, and switches the connection off
        if (before is not null && before != Fingerprint(c)) { c.Enabled = false; c.Verification = CarrierVerification.Unverified; }
        await db.SaveChangesAsync(ct);
        return c;
    }

    async Task<CarrierConnection> EditConnectionAsync(Guid id, string permission, CancellationToken ct)
    {
        var c = await db.CarrierConnections.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Carrier connection");
        await m.RequireAsync(permission, c.SiteId, c.DepartmentId, ct);
        return c;
    }

    /// <summary>Connection test against the configured endpoint. Records the result for this exact configuration.</summary>
    public async Task<CarrierConnectionRow> TestAsync(Guid id, CancellationToken ct)
    {
        var c = await EditConnectionAsync(id, Permissions.IntegrationsEdit, ct);
        string message; var ok = false;
        try { message = await CallAsync(c, CarrierOperation.ConnectionTest, null, null, (ctx, t) => Adapter(c.Provider).TestAsync(ctx, t), ct); ok = true; }
        catch (CarrierCallFailed e) { message = e.Message; }
        c.LastTestAt = m.Now; c.LastTestMessage = message.Length > 1000 ? message[..1000] : message;
        c.Verification = ok ? CarrierVerification.Verified : CarrierVerification.Failed; c.VerifiedFingerprint = Fingerprint(c);
        c.VerifiedAt = ok ? m.Now : null; c.VerifiedById = ok ? m.UserId : null;
        if (!ok) c.Enabled = false;
        await db.SaveChangesAsync(ct);
        return await ConnectionAsync(id, ct);
    }

    public async Task<CarrierConnectionRow> ApproveProductionAsync(Guid id, CarrierApprovalInput r, CancellationToken ct)
    {
        var c = await EditConnectionAsync(id, Permissions.IntegrationsApprove, ct);
        if (c.Environment != CarrierEnvironment.Production) throw new ConflictException("Only production connections need approval; sandbox connections can be enabled after a passing test.");
        if (string.IsNullOrWhiteSpace(r.Notes) || r.Notes.Trim().Length < 5) throw new AppException("Record what you reviewed (at least 5 characters).");
        if (r.Notes.Length > 2000) throw new AppException("The notes are too long.");
        var fp = Fingerprint(c);
        if (c.Verification != CarrierVerification.Verified || c.VerifiedFingerprint != fp) throw new ConflictException("The current configuration has no passing connection test.");
        if (c.VerifiedById == m.UserId) throw new ForbiddenException("The person who ran the connection test cannot also approve it for production.");
        c.ApprovedFingerprint = fp; c.ApprovedAt = m.Now; c.ApprovedById = m.UserId; c.ApprovalNotes = r.Notes.Trim();
        await db.SaveChangesAsync(ct);
        return await ConnectionAsync(id, ct);
    }

    public async Task<CarrierConnectionRow> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct)
    {
        var c = await EditConnectionAsync(id, Permissions.IntegrationsEdit, ct);
        if (enabled)
        {
            var blockers = Blockers(c, await db.Carriers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == c.CarrierId, ct));
            if (blockers.Count > 0) throw new ConflictException("The connection cannot be enabled: " + string.Join(" ", blockers));
        }
        c.Enabled = enabled;
        await db.SaveChangesAsync(ct);
        return await ConnectionAsync(id, ct);
    }

    // ------------------------------------------------------------------ calls
    /// <summary>A carrier call failed after its retries; the log rows are saved. Status 502 (the carrier, not the request, failed) unless the carrier rejected the request.</summary>
    public sealed class CarrierCallFailed(string message, int status) : AppException(message, status);

    async Task<T> CallAsync<T>(CarrierConnection c, CarrierOperation op, Guid? shipmentId, string? idempotencyKey, Func<CarrierContext, CancellationToken, Task<T>> f, CancellationToken ct)
    {
        string? secret;
        try { secret = secrets.Unprotect(c.SecretCiphertext); }
        catch (CryptographicException) { secret = null; Log(c, op, shipmentId, 1, CarrierCallResult.Failed, null, "config", "The stored credential cannot be decrypted; enter it again.", 0, idempotencyKey); await db.SaveChangesAsync(ct); throw new CarrierCallFailed("The stored credential cannot be decrypted; enter it again.", 409); }
        var opts = (JsonNode.Parse(string.IsNullOrWhiteSpace(c.OptionsJson) ? "{}" : c.OptionsJson) as JsonObject) ?? [];
        using var client = http.Create();
        for (var attempt = 1; ; attempt++)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var res = await f(new CarrierContext(c, secret, opts, client, attempt), ct);
                Log(c, op, shipmentId, attempt, CarrierCallResult.Succeeded, null, null, null, (int)sw.ElapsedMilliseconds, idempotencyKey);
                await db.SaveChangesAsync(ct);
                return res;
            }
            catch (CarrierNotSupported e)
            {
                Log(c, op, shipmentId, attempt, CarrierCallResult.NotSupported, null, "not_supported", e.Message, (int)sw.ElapsedMilliseconds, idempotencyKey);
                await db.SaveChangesAsync(ct);
                throw new CarrierCallFailed(e.Message, 409);
            }
            catch (CarrierFailure e)
            {
                var msg = e.Message.Length > 2000 ? e.Message[..2000] : e.Message;
                Log(c, op, shipmentId, attempt, CarrierCallResult.Failed, e.HttpStatus, e.Code, msg, (int)sw.ElapsedMilliseconds, idempotencyKey);
                await db.SaveChangesAsync(ct);
                if (e.Retryable && attempt < MaxAttempts) { await Task.Delay(http.RetryDelay(attempt), ct); continue; }
                log.LogWarning("Carrier {Operation} on connection {Connection} failed after {Attempts} attempt(s): {Code}", op, c.Id, attempt, e.Code);
                throw new CarrierCallFailed($"{OpLabel(op)} failed{(attempt > 1 ? $" after {attempt} attempts" : "")}: {msg}", e.Code is "rejected" or "service" or "config" or "cancel" ? 409 : 502);
            }
        }
    }

    static string OpLabel(CarrierOperation op) => op switch
    {
        CarrierOperation.ConnectionTest => "Connection test", CarrierOperation.AddressValidation => "Address validation", CarrierOperation.Rate => "Rate request", CarrierOperation.CreateShipment => "Shipment creation",
        CarrierOperation.Label => "Label retrieval", CarrierOperation.Cancel => "Cancellation", _ => "Tracking",
    };

    void Log(CarrierConnection c, CarrierOperation op, Guid? shipmentId, int attempt, CarrierCallResult result, int? status, string? code, string? message, int ms, string? key) =>
        db.CarrierRequestLogs.Add(new CarrierRequestLog
        {
            ConnectionId = c.Id, ShipmentId = shipmentId, Operation = op, Environment = c.Environment, Attempt = attempt, Result = result, HttpStatus = status, ErrorCode = code, Message = message, DurationMs = ms,
            IdempotencyKey = key, ById = CurrentUser, At = m.Now, ConfigFingerprint = Fingerprint(c),
        });

    Guid? CurrentUser { get { try { return m.UserId; } catch (ForbiddenException) { return null; } } }

    // ------------------------------------------------------------------ shipments
    async Task<Shipment> EditShipmentAsync(Guid id, CancellationToken ct, params ShipmentStatus[] allowed)
    {
        await m.EditAsync(ShippingService.ShipmentType, id, ct);
        var s = await db.Shipments.FirstAsync(x => x.Id == id, ct);
        if (allowed.Length > 0 && !allowed.Contains(s.Status)) throw new ConflictException($"Not allowed while the shipment is {s.Status}.");
        return s;
    }

    async Task<CarrierConnection> UsableAsync(Guid connectionId, Shipment s, CancellationToken ct)
    {
        var c = await db.CarrierConnections.FirstOrDefaultAsync(x => x.Id == connectionId, ct) ?? throw new AppException("Select a carrier connection.");
        if (c.SiteId != s.SiteId) throw new AppException("The carrier connection belongs to another site.");
        if (!c.Enabled) throw new ConflictException($"Carrier connection {c.Name} is disabled.");
        // a configuration changed underneath an enabled connection (should not happen through the API) still blocks use
        var blockers = Blockers(c, await db.Carriers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == c.CarrierId, ct));
        if (blockers.Count > 0) throw new ConflictException("The carrier connection cannot be used: " + string.Join(" ", blockers));
        return c;
    }

    static bool Open(ShipmentStatus st) => st is not (ShipmentStatus.Shipped or ShipmentStatus.Delivered or ShipmentStatus.Cancelled);

    public async Task SaveShipToAsync(Guid id, ShipToInput r, CancellationToken ct)
    {
        var s = await EditShipmentAsync(id, ct);
        if (!Open(s.Status)) throw new ConflictException($"The address cannot change once the shipment is {s.Status}.");
        if (await db.CarrierLabels.AnyAsync(x => x.ShipmentId == id && x.Status == CarrierLabelStatus.Active, ct)) throw new ConflictException("Void the carrier labels before changing the address.");
        var a = r.Address ?? throw new AppException("Enter the address.");
        static string? T(string? v, int max) => string.IsNullOrWhiteSpace(v) ? null : v.Trim().Length > max ? throw new AppException("An address field is too long.") : v.Trim();
        if (a.Country is { Length: > 0 } c && c.Trim().Length != 2) throw new AppException("Use the two-letter country code (for example US, CA, DE).");
        s.ShipToName = T(a.Name, 200); s.ShipToLine1 = T(a.Line1, 200); s.ShipToLine2 = T(a.Line2, 200); s.ShipToCity = T(a.City, 100); s.ShipToRegion = T(a.Region, 100);
        s.ShipToPostalCode = T(a.PostalCode, 20)?.ToUpperInvariant(); s.ShipToCountry = T(a.Country, 2)?.ToUpperInvariant(); s.ShipToPhone = T(a.Phone, 40); s.ShipToResidential = a.Residential;
        s.AddressValidatedAt = null; s.AddressValidationNote = null; s.QuotedCharge = null; s.QuotedCurrency = null;
        await db.SaveChangesAsync(ct);
    }

    static void RequireAddress(Shipment s)
    {
        if (string.IsNullOrWhiteSpace(s.ShipToLine1) || string.IsNullOrWhiteSpace(s.ShipToCity) || string.IsNullOrWhiteSpace(s.ShipToPostalCode) || string.IsNullOrWhiteSpace(s.ShipToCountry))
            throw new AppException("Enter the delivery address (street, city, postal code and country) first.");
    }

    public async Task<AddressResult> ValidateAddressAsync(Guid id, CarrierChoice r, CancellationToken ct)
    {
        var s = await EditShipmentAsync(id, ct);
        if (!Open(s.Status)) throw new ConflictException($"Not allowed while the shipment is {s.Status}.");
        RequireAddress(s);
        var c = await UsableAsync(r.ConnectionId, s, ct);
        var res = await CallAsync(c, CarrierOperation.AddressValidation, s.Id, null, (ctx, t) => Adapter(c.Provider).ValidateAddressAsync(ctx, ShipTo(s), t), ct);
        var note = string.Join(" ", res.Messages);
        var applied = false;
        if (res.Valid)
        {
            if (res.Normalized is { } n)
            {
                applied = n with { Residential = res.Residential ?? n.Residential } != ShipTo(s);
                s.ShipToName = n.Name ?? s.ShipToName; s.ShipToLine1 = n.Line1 ?? s.ShipToLine1; s.ShipToLine2 = n.Line2; s.ShipToCity = n.City ?? s.ShipToCity; s.ShipToRegion = n.Region ?? s.ShipToRegion;
                s.ShipToPostalCode = n.PostalCode ?? s.ShipToPostalCode; s.ShipToCountry = n.Country ?? s.ShipToCountry; s.ShipToPhone = n.Phone ?? s.ShipToPhone;
            }
            if (res.Residential is { } resi) s.ShipToResidential = resi;
            s.AddressValidatedAt = m.Now;
        }
        else s.AddressValidatedAt = null;
        s.AddressValidationNote = $"{(res.Valid ? "Valid" : "Not valid")} ({c.Name}){(note.Length > 0 ? ": " + note : ".")}" is var x && x.Length > 1000 ? x[..1000] : x;
        await db.SaveChangesAsync(ct);
        return new AddressResult(res.Valid, res.Normalized, res.Messages, applied);
    }

    /// <summary>Packages to rate and label: the top-level cartons, pallets and containers with their weight (own or the sum of what they hold) and dimensions.</summary>
    public async Task<List<CarrierPackage>> PackagesAsync(Shipment s, CancellationToken ct)
    {
        var conts = await db.ShipmentContainers.AsNoTracking().Where(x => x.ShipmentId == s.Id).OrderBy(x => x.Number).ToListAsync(ct);
        decimal W(ShipmentContainer c) => c.Weight ?? conts.Where(x => x.ParentId == c.Id).Sum(W);
        var top = conts.Where(x => x.ParentId == null).ToList();
        if (top.Count == 0) return s.GrossWeight is { } g && g > 0 ? [new CarrierPackage(1, null, s.Number, g, null, null, null)] : [];
        return top.Select((c, i) => new CarrierPackage(i + 1, c.Id, c.Number, W(c), c.LengthCm, c.WidthCm, c.HeightCm)).ToList();
    }

    async Task<List<CarrierPackage>> RequirePackagesAsync(Shipment s, CancellationToken ct)
    {
        var p = await PackagesAsync(s, ct);
        if (p.Count == 0) throw new AppException("Pack the shipment (or enter its gross weight) before rating it.");
        if (p.Any(x => x.WeightKg <= 0)) throw new AppException($"Enter the weight of {string.Join(", ", p.Where(x => x.WeightKg <= 0).Select(x => x.Reference))}.");
        return p;
    }

    public async Task<List<RateQuote>> RatesAsync(Guid id, CarrierChoice r, CancellationToken ct)
    {
        var s = await EditShipmentAsync(id, ct);
        if (!Open(s.Status)) throw new ConflictException($"Not allowed while the shipment is {s.Status}.");
        RequireAddress(s);
        var c = await UsableAsync(r.ConnectionId, s, ct);
        var pk = await RequirePackagesAsync(s, ct);
        var ctxClass = (JsonNode.Parse(c.OptionsJson) as JsonObject)?["freightClass"]?.ToString();
        return await CallAsync(c, CarrierOperation.Rate, s.Id, null, (ctx, t) => Adapter(c.Provider).RateAsync(ctx, Shipper(c), ShipTo(s), pk, ctxClass, t), ct);
    }

    /// <summary>Selects a service from a fresh quote (the charge comes from the carrier, not from the browser).</summary>
    public async Task<RateQuote> SelectServiceAsync(Guid id, ServiceChoice r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.ServiceCode)) throw new AppException("Select the service.");
        if (await db.CarrierLabels.AnyAsync(x => x.ShipmentId == id && x.Status == CarrierLabelStatus.Active, ct)) throw new ConflictException("Void the carrier labels before changing the service.");
        var quotes = await RatesAsync(id, new CarrierChoice(r.ConnectionId), ct);
        var q = quotes.FirstOrDefault(x => x.ServiceCode == r.ServiceCode) ?? throw new ConflictException($"The carrier no longer offers {r.ServiceCode} for this shipment.");
        var s = await db.Shipments.FirstAsync(x => x.Id == id, ct);
        var c = await db.CarrierConnections.AsNoTracking().FirstAsync(x => x.Id == r.ConnectionId, ct);
        s.CarrierConnectionId = c.Id; s.CarrierId = c.CarrierId; s.CarrierServiceCode = q.ServiceCode; s.ServiceLevel = q.ServiceName.Length > 100 ? q.ServiceName[..100] : q.ServiceName;
        s.QuotedCharge = q.Charge; s.QuotedCurrency = q.Currency.Length > 3 ? q.Currency[..3] : q.Currency;
        await db.SaveChangesAsync(ct);
        return q;
    }

    /// <summary>
    /// Creates the carrier shipment and its labels. Idempotent: active labels are returned as they are, and a retried request reuses the same
    /// idempotency key so the carrier can recognise it. Only packed or verified shipments with a selected service and (where the carrier
    /// validates addresses) a validated address.
    /// </summary>
    public async Task<List<LabelRow>> CreateLabelsAsync(Guid id, CreateLabelsInput? r, CancellationToken ct)
    {
        await EditShipmentAsync(id, ct, ShipmentStatus.Packed, ShipmentStatus.Verified);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM qms.shipments WHERE \"Id\" = {id} FOR UPDATE", ct);
        var s = await db.Shipments.FirstAsync(x => x.Id == id, ct);
        await db.Entry(s).ReloadAsync(ct);
        if (await db.CarrierLabels.AnyAsync(x => x.ShipmentId == id && x.Status == CarrierLabelStatus.Active, ct)) { await tx.CommitAsync(ct); return await LabelsAsync(id, ct); }
        if (s.CarrierConnectionId is not { } cid || s.CarrierServiceCode is null) throw new AppException("Rate the shipment and select a carrier service first.");
        var c = await UsableAsync(cid, s, ct);
        var cap = Adapter(c.Provider).Capabilities(c);
        if (!cap.ShipmentCreation) throw new ConflictException($"{cap.Provider} cannot create shipments.");
        RequireAddress(s);
        if (cap.AddressValidation && s.AddressValidatedAt is null) throw new ConflictException("Validate the delivery address with the carrier first.");
        var pk = await RequirePackagesAsync(s, ct);
        var batch = await db.CarrierLabels.Where(x => x.ShipmentId == id).Select(x => x.CarrierShipmentId).Distinct().CountAsync(ct);
        var key = $"{s.Id:N}-{batch + 1}";
        var freight = (JsonNode.Parse(c.OptionsJson) as JsonObject)?["freightClass"]?.ToString();
        CreatedShipment created;
        try
        {
            created = await CallAsync(c, CarrierOperation.CreateShipment, s.Id, key, (ctx, t) => Adapter(c.Provider).CreateShipmentAsync(ctx,
                new CarrierShipmentRequest(s.Number, s.CarrierServiceCode, Shipper(c), ShipTo(s), pk, freight, r?.Instructions is { Length: > 0 } i ? (i.Length > 500 ? i[..500] : i) : null, key), t), ct);
        }
        catch (CarrierCallFailed) { await tx.CommitAsync(ct); throw; } // keep the request log; nothing else changed
        var rows = new List<CarrierLabel>();
        foreach (var p in created.Packages)
        {
            var data = p.Label;
            if (data is null or { Length: 0 } && cap.LabelRetrieval)
            {
                try { data = (await CallAsync(c, CarrierOperation.Label, s.Id, null, (ctx, t) => Adapter(c.Provider).LabelAsync(ctx, created.CarrierShipmentId, p.TrackingNumber, t), ct)).Data; }
                catch (CarrierCallFailed) { data = null; } // the shipment exists at the carrier; the label can be fetched again from the label list
            }
            var pkg = pk.FirstOrDefault(x => x.Number == p.Number);
            rows.Add(new CarrierLabel
            {
                ShipmentId = s.Id, ConnectionId = c.Id, ContainerId = pkg?.ContainerId, PackageNumber = p.Number, CarrierShipmentId = created.CarrierShipmentId, TrackingNumber = p.TrackingNumber, ServiceCode = s.CarrierServiceCode,
                Format = p.Format.Length > 10 ? p.Format[..10] : p.Format, Data = data ?? [], Charge = p.Number == created.Packages[0].Number ? created.Charge : null, Currency = created.Currency, Status = CarrierLabelStatus.Active, CreatedOn = m.Now,
            });
        }
        db.CarrierLabels.AddRange(rows);
        s.TrackingNumber = created.MasterTrackingNumber; s.CarrierId = c.CarrierId;
        if (created.Charge is { } ch) { s.QuotedCharge = ch; s.QuotedCurrency = created.Currency; }
        db.CustodyEvents.Add(new CustodyEvent { RefType = ShippingService.ShipmentType, RefId = s.Id, Action = $"Carrier labels created ({c.Name}{(c.Environment == CarrierEnvironment.Sandbox ? ", sandbox" : "")})", ById = m.UserId, At = m.Now, LocationId = s.StagingLocationId, Notes = created.MasterTrackingNumber });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await LabelsAsync(id, ct);
    }

    async Task<List<LabelRow>> LabelsAsync(Guid shipmentId, CancellationToken ct)
    {
        var conts = await db.ShipmentContainers.AsNoTracking().Where(x => x.ShipmentId == shipmentId).ToDictionaryAsync(x => x.Id, x => x.Number, ct);
        return (await db.CarrierLabels.AsNoTracking().Where(x => x.ShipmentId == shipmentId).OrderBy(x => x.CreatedOn).ThenBy(x => x.PackageNumber)
                .Select(x => new { x.Id, x.PackageNumber, x.ContainerId, x.CarrierShipmentId, x.TrackingNumber, x.ServiceCode, x.Format, Bytes = x.Data.Length, x.Charge, x.Currency, x.Status, x.CreatedOn, x.VoidedAt, x.VoidMessage }).ToListAsync(ct))
            .Select(x => new LabelRow(x.Id, x.PackageNumber, x.ContainerId, x.ContainerId is { } k ? conts.GetValueOrDefault(k) : null, x.CarrierShipmentId, x.TrackingNumber, x.ServiceCode, x.Format, x.Bytes, x.Charge, x.Currency, x.Status, x.CreatedOn, x.VoidedAt, x.VoidMessage)).ToList();
    }

    /// <summary>The label as returned by the carrier; fetched again from the carrier when it was not stored.</summary>
    public async Task<(byte[] Data, string ContentType, string FileName)> LabelFileAsync(Guid labelId, CancellationToken ct)
    {
        var l = await db.CarrierLabels.FirstOrDefaultAsync(x => x.Id == labelId, ct) ?? throw new NotFoundException("Carrier label");
        await m.ViewAsync(ShippingService.ShipmentType, l.ShipmentId, ct);
        if (l.Data.Length == 0)
        {
            var s = await db.Shipments.AsNoTracking().FirstAsync(x => x.Id == l.ShipmentId, ct);
            await m.EditAsync(ShippingService.ShipmentType, s.Id, ct);
            var c = await db.CarrierConnections.FirstAsync(x => x.Id == l.ConnectionId, ct);
            var doc = await CallAsync(c, CarrierOperation.Label, s.Id, null, (ctx, t) => Adapter(c.Provider).LabelAsync(ctx, l.CarrierShipmentId, l.TrackingNumber, t), ct);
            l.Data = doc.Data; l.Format = doc.Format.Length > 10 ? doc.Format[..10] : doc.Format;
            await db.SaveChangesAsync(ct);
        }
        var zpl = l.Format.Equals("ZPL", StringComparison.OrdinalIgnoreCase);
        return (l.Data, zpl ? "application/vnd.zebra-zpl" : "application/pdf", $"label-{l.TrackingNumber}.{(zpl ? "zpl" : "pdf")}");
    }

    /// <summary>Voids the shipment's active labels at the carrier. A void the carrier refuses stays visible (VoidFailed) and can be tried again.</summary>
    public async Task<VoidResult> VoidLabelsAsync(Guid id, VoidLabelsInput r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Trim().Length < 3) throw new AppException("Give the reason for voiding the labels.");
        var s = await EditShipmentAsync(id, ct);
        if (s.Status is ShipmentStatus.Shipped or ShipmentStatus.Delivered) throw new ConflictException("The shipment was handed to the carrier; contact the carrier to intercept it.");
        var labels = await db.CarrierLabels.Where(x => x.ShipmentId == id && (x.Status == CarrierLabelStatus.Active || x.Status == CarrierLabelStatus.VoidFailed)).ToListAsync(ct);
        if (labels.Count == 0) throw new ConflictException("There are no carrier labels to void.");
        int ok = 0, failed = 0; var msgs = new List<string>();
        foreach (var g in labels.GroupBy(x => (x.ConnectionId, x.CarrierShipmentId)))
        {
            var c = await db.CarrierConnections.FirstAsync(x => x.Id == g.Key.ConnectionId, ct);
            try
            {
                await CallAsync(c, CarrierOperation.Cancel, s.Id, null, async (ctx, t) => { await Adapter(c.Provider).CancelAsync(ctx, g.Key.CarrierShipmentId, t); return true; }, ct);
                foreach (var l in g) { l.Status = CarrierLabelStatus.Voided; l.VoidedAt = m.Now; l.VoidMessage = r.Reason.Trim().Length > 1000 ? r.Reason.Trim()[..1000] : r.Reason.Trim(); ok++; }
            }
            catch (CarrierCallFailed e)
            {
                foreach (var l in g) { l.Status = CarrierLabelStatus.VoidFailed; l.VoidMessage = e.Message.Length > 1000 ? e.Message[..1000] : e.Message; failed++; }
                msgs.Add(e.Message);
            }
        }
        if (failed == 0) { s.TrackingNumber = null; s.CarrierServiceCode = null; s.QuotedCharge = null; s.QuotedCurrency = null; } // every label was voided: rate and select again before new labels
        db.CustodyEvents.Add(new CustodyEvent { RefType = ShippingService.ShipmentType, RefId = s.Id, Action = failed == 0 ? "Carrier labels voided" : "Carrier label void failed", ById = m.UserId, At = m.Now, LocationId = s.StagingLocationId, Notes = r.Reason.Trim() });
        await db.SaveChangesAsync(ct);
        return new VoidResult(ok, failed, msgs);
    }

    /// <summary>After a shipment is cancelled: voids its active carrier labels. Never throws; a refused void stays visible on the shipment as VoidFailed.</summary>
    public async Task VoidOnCancelAsync(Guid id, string? reason, CancellationToken ct)
    {
        if (!await db.CarrierLabels.AnyAsync(x => x.ShipmentId == id && x.Status == CarrierLabelStatus.Active, ct)) return;
        try { await VoidLabelsAsync(id, new VoidLabelsInput("Shipment cancelled" + (string.IsNullOrWhiteSpace(reason) ? "" : ": " + reason.Trim())), ct); }
        catch (AppException e) { log.LogWarning("Voiding carrier labels for cancelled shipment {Shipment} failed: {Message}", id, e.Message); db.ChangeTracker.Clear(); }
    }

    /// <summary>Asks the carrier for tracking events, stores new ones (deduplicated) and marks the shipment delivered when the carrier reports delivery.</summary>
    public async Task<TrackResult> TrackAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(ShippingService.ShipmentType, id, ct);
        return await TrackCoreAsync(id, ct);
    }

    async Task<TrackResult> TrackCoreAsync(Guid id, CancellationToken ct)
    {
        var s = await db.Shipments.FirstAsync(x => x.Id == id, ct);
        var labels = await db.CarrierLabels.AsNoTracking().Where(x => x.ShipmentId == id && x.Status == CarrierLabelStatus.Active).ToListAsync(ct);
        if (labels.Count == 0) throw new ConflictException("The shipment has no active carrier labels to track.");
        var added = 0; var delivered = false;
        foreach (var l in labels.GroupBy(x => (x.ConnectionId, x.TrackingNumber)).Select(g => g.First()))
        {
            var c = await db.CarrierConnections.FirstAsync(x => x.Id == l.ConnectionId, ct);
            if (!Adapter(c.Provider).Capabilities(c).Tracking) continue;
            var events = await CallAsync(c, CarrierOperation.Track, s.Id, null, (ctx, t) => Adapter(c.Provider).TrackAsync(ctx, l.TrackingNumber, t), ct);
            var known = await db.CarrierTrackingEvents.AsNoTracking().Where(x => x.ShipmentId == id && x.TrackingNumber == l.TrackingNumber).Select(x => new { x.OccurredAt, x.Code }).ToListAsync(ct);
            foreach (var e in events.Where(e => !known.Any(k => k.OccurredAt == e.OccurredAt && k.Code == e.Code)).GroupBy(e => (e.OccurredAt, e.Code)).Select(g => g.First()))
            {
                db.CarrierTrackingEvents.Add(new CarrierTrackingEvent { ShipmentId = id, TrackingNumber = l.TrackingNumber, OccurredAt = e.OccurredAt, Code = Cut(e.Code, 60), Description = Cut(e.Description, 500), Location = e.Location is null ? null : Cut(e.Location, 200), Delivered = e.Delivered, ReceivedAt = m.Now });
                added++;
            }
            if (events.FirstOrDefault(e => e.Delivered) is { } d && s.Status == ShipmentStatus.Shipped)
            {
                var zone = await m.ZoneAsync(s.SiteId, ct);
                var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(d.OccurredAt, zone).DateTime);
                if (s.ShippedAt is { } sa && day < DateOnly.FromDateTime(sa.UtcDateTime).AddDays(-1)) day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(m.Now, zone).DateTime); // carrier clock before our ship date: use today
                s.Status = ShipmentStatus.Delivered; s.DeliveredOn = day; delivered = true;
            }
        }
        await db.SaveChangesAsync(ct);
        return new TrackResult(added, delivered, await TrackingAsync(id, ct));
    }

    static string Cut(string s, int n) => s.Length > n ? s[..n] : s;

    Task<List<TrackingRow>> TrackingAsync(Guid id, CancellationToken ct) => db.CarrierTrackingEvents.AsNoTracking().Where(x => x.ShipmentId == id).OrderByDescending(x => x.OccurredAt)
        .Select(x => new TrackingRow(x.OccurredAt, x.TrackingNumber, x.Code, x.Description, x.Location, x.Delivered)).ToListAsync(ct);

    /// <summary>Background: refreshes tracking for shipped shipments with active labels on enabled connections (last 60 days, 200 per run). Failures are logged, never thrown.</summary>
    public async Task<int> RefreshTrackingAsync(CancellationToken ct)
    {
        var since = m.Now.AddDays(-60);
        var ids = await db.Shipments.AsNoTracking().Where(s => s.Status == ShipmentStatus.Shipped && s.ShippedAt >= since
                && db.CarrierLabels.Any(l => l.ShipmentId == s.Id && l.Status == CarrierLabelStatus.Active && db.CarrierConnections.Any(c => c.Id == l.ConnectionId && c.Enabled)))
            .OrderBy(s => s.ShippedAt).Select(s => s.Id).Take(200).ToListAsync(ct);
        var n = 0;
        foreach (var id in ids)
        {
            try { if ((await TrackCoreAsync(id, ct)).Delivered) n++; }
            catch (AppException e) { log.LogInformation("Tracking refresh for shipment {Shipment} failed: {Message}", id, e.Message); db.ChangeTracker.Clear(); }
        }
        return n;
    }

    public async Task<ShipmentCarrierView> ViewAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(ShippingService.ShipmentType, id, ct);
        var s = await db.Shipments.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var c = s.CarrierConnectionId is { } cid ? await db.CarrierConnections.AsNoTracking().FirstOrDefaultAsync(x => x.Id == cid, ct) : null;
        var logs = await (from l in db.CarrierRequestLogs.AsNoTracking()
                          where l.ShipmentId == id
                          orderby l.At descending, l.Attempt descending
                          select new RequestLogRow(l.Id, l.Operation, l.Environment, l.Attempt, l.Result, l.HttpStatus, l.ErrorCode, l.Message, l.DurationMs, l.At,
                              db.Users.Where(u => u.Id == l.ById).Select(u => u.DisplayName).FirstOrDefault(), db.CarrierConnections.Where(x => x.Id == l.ConnectionId).Select(x => x.Name).FirstOrDefault())).Take(100).ToListAsync(ct);
        return new ShipmentCarrierView(ShipTo(s), s.AddressValidatedAt, s.AddressValidationNote, c?.Id, c?.Name, c?.Environment, s.CarrierServiceCode, s.ServiceLevel, s.QuotedCharge, s.QuotedCurrency,
            await LabelsAsync(id, ct), await TrackingAsync(id, ct), logs, await PackagesAsync(s, ct));
    }

    /// <summary>Connection request log (newest first, paged).</summary>
    public async Task<List<RequestLogRow>> ConnectionLogAsync(Guid id, int take, CancellationToken ct)
    {
        await ConnectionAsync(id, ct);
        return await (from l in db.CarrierRequestLogs.AsNoTracking()
                      where l.ConnectionId == id
                      orderby l.At descending, l.Attempt descending
                      select new RequestLogRow(l.Id, l.Operation, l.Environment, l.Attempt, l.Result, l.HttpStatus, l.ErrorCode, l.Message, l.DurationMs, l.At,
                          db.Users.Where(u => u.Id == l.ById).Select(u => u.DisplayName).FirstOrDefault(), null)).Take(Math.Clamp(take, 1, 500)).ToListAsync(ct);
    }
}
