using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Logistics;

public sealed record CarrierAddress(string? Name, string? Line1, string? Line2, string? City, string? Region, string? PostalCode, string? Country, string? Phone, bool Residential);
public sealed record CarrierPackage(int Number, Guid? ContainerId, string? Reference, decimal WeightKg, decimal? LengthCm, decimal? WidthCm, decimal? HeightCm);
public sealed record AddressCheck(bool Valid, CarrierAddress? Normalized, List<string> Messages, bool? Residential);
public sealed record RateQuote(string ServiceCode, string ServiceName, decimal Charge, string Currency, int? TransitDays, DateOnly? EstimatedDelivery);
public sealed record CarrierShipmentRequest(string Reference, string ServiceCode, CarrierAddress Shipper, CarrierAddress ShipTo, List<CarrierPackage> Packages, string? FreightClass, string? Instructions, string IdempotencyKey);
public sealed record CreatedPackage(int Number, string TrackingNumber, string Format, byte[]? Label);
public sealed record CreatedShipment(string CarrierShipmentId, string MasterTrackingNumber, decimal? Charge, string? Currency, List<CreatedPackage> Packages);
public sealed record CarrierLabelDocument(string Format, byte[] Data);
public sealed record TrackingUpdate(DateTimeOffset OccurredAt, string Code, string Description, string? Location, bool Delivered);
public sealed record CarrierServiceOption(string Code, string Name);

/// <summary>What an adapter can do. Operations it does not support return a clear "not supported" result instead of a guess.</summary>
public sealed record CarrierCapabilities(string Provider, string Description, bool AddressValidation, bool Rating, bool ShipmentCreation, bool LabelRetrieval, bool Cancellation, bool Tracking,
    bool SandboxOnly, bool RequiresBaseUrl, bool RequiresSecret, List<string> LabelFormats, List<CarrierServiceOption> Services, List<string> Unverified, string Status)
{
    public bool AnyShipmentOperation => AddressValidation || Rating || ShipmentCreation || Tracking;
}

/// <summary>Adapter call context: the connection, its decrypted secret (never logged) and the parsed options.</summary>
public sealed record CarrierContext(CarrierConnection Connection, string? Secret, JsonObject Options, HttpClient Http, int Attempt)
{
    public string? Opt(string key) => Options.TryGetPropertyValue(key, out var v) && v is JsonValue jv && jv.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;
}

/// <summary>A failed carrier call. Retryable failures (network, timeout, 429, 5xx) are tried again; the others are returned to the user at once.</summary>
public sealed class CarrierFailure(string code, string message, bool retryable, int? httpStatus = null) : Exception(message)
{
    public string Code => code; public bool Retryable => retryable; public int? HttpStatus => httpStatus;
}

public sealed class CarrierNotSupported(string message) : Exception(message);

public interface ICarrierAdapter
{
    CarrierProvider Provider { get; }
    CarrierCapabilities Capabilities(CarrierConnection c);
    Task<string> TestAsync(CarrierContext c, CancellationToken ct);
    Task<AddressCheck> ValidateAddressAsync(CarrierContext c, CarrierAddress a, CancellationToken ct);
    Task<List<RateQuote>> RateAsync(CarrierContext c, CarrierAddress from, CarrierAddress to, List<CarrierPackage> packages, string? freightClass, CancellationToken ct);
    Task<CreatedShipment> CreateShipmentAsync(CarrierContext c, CarrierShipmentRequest r, CancellationToken ct);
    Task<CarrierLabelDocument> LabelAsync(CarrierContext c, string carrierShipmentId, string trackingNumber, CancellationToken ct);
    Task CancelAsync(CarrierContext c, string carrierShipmentId, CancellationToken ct);
    Task<List<TrackingUpdate>> TrackAsync(CarrierContext c, string trackingNumber, CancellationToken ct);
}

/// <summary>Supplies the HTTP handler for carrier adapters. Tests swap in recorded fixtures; production uses one pooled handler without cookies.</summary>
public sealed class CarrierHttpHandlerProvider
{
    static readonly SocketsHttpHandler Shared = new() { UseCookies = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5), AutomaticDecompression = DecompressionMethods.All };
    public Func<HttpMessageHandler>? Override { get; set; }
    /// <summary>Delay before retry attempt n (1-based). Tests set it to zero.</summary>
    public Func<int, TimeSpan> RetryDelay { get; set; } = n => TimeSpan.FromMilliseconds(500 * n);
    public HttpClient Create() => Override is { } f ? new HttpClient(f(), disposeHandler: true) { Timeout = TimeSpan.FromSeconds(30) } : new HttpClient(Shared, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(30) };
}

/// <summary>
/// Built-in simulated carrier for automated tests, training and demonstrations. Sandbox only: it never ships anything and every label
/// it makes is marked "SANDBOX - NOT A SHIPPING LABEL". Behaviour is deterministic: postal code 00000 is undeliverable, a client id of
/// "fail-auth" fails authentication, option "failNext" = "transient" fails the first attempt of each call (to exercise retry),
/// option "holdDelivery" = "true" keeps tracking short of delivery.
/// </summary>
public sealed class FixtureCarrierAdapter : ICarrierAdapter
{
    public CarrierProvider Provider => CarrierProvider.Fixture;
    static readonly List<CarrierServiceOption> Services = [new("FX-GROUND", "Fixture Ground"), new("FX-2DAY", "Fixture 2nd Day"), new("FX-OVERNIGHT", "Fixture Overnight")];

    public CarrierCapabilities Capabilities(CarrierConnection c) => new("Fixture", "Simulated carrier for tests and training. Nothing is shipped; labels are marked as sandbox samples.",
        true, true, true, true, true, true, SandboxOnly: true, RequiresBaseUrl: false, RequiresSecret: false, ["PDF"], Services, [], "Fixture (simulated, sandbox only)");

    static void Transient(CarrierContext c) { if (c.Opt("failNext") == "transient" && c.Attempt == 1) throw new CarrierFailure("timeout", "The fixture carrier timed out (simulated transient failure).", retryable: true); }

    public Task<string> TestAsync(CarrierContext c, CancellationToken ct)
    {
        if (string.Equals(c.Connection.ClientId, "fail-auth", StringComparison.OrdinalIgnoreCase)) throw new CarrierFailure("auth", "Authentication failed: the fixture carrier rejected the client id.", retryable: false, 401);
        return Task.FromResult("Fixture carrier reachable (simulated sandbox).");
    }

    public Task<AddressCheck> ValidateAddressAsync(CarrierContext c, CarrierAddress a, CancellationToken ct)
    {
        Transient(c);
        var msgs = new List<string>();
        if (string.IsNullOrWhiteSpace(a.Line1)) msgs.Add("Street address is missing.");
        if (string.IsNullOrWhiteSpace(a.City)) msgs.Add("City is missing.");
        if (string.IsNullOrWhiteSpace(a.PostalCode)) msgs.Add("Postal code is missing.");
        if (a.PostalCode?.Trim() == "00000") msgs.Add("The postal code is not deliverable.");
        if (msgs.Count > 0) return Task.FromResult(new AddressCheck(false, null, msgs, null));
        static string? T(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        var norm = new CarrierAddress(T(a.Name), T(a.Line1)!.ToUpperInvariant(), T(a.Line2)?.ToUpperInvariant(), T(a.City)!.ToUpperInvariant(), T(a.Region)?.ToUpperInvariant(), a.PostalCode!.Trim().ToUpperInvariant(),
            (T(a.Country) ?? "US").ToUpperInvariant(), T(a.Phone), a.Residential);
        if (norm != a) msgs.Add("Address standardized by the carrier.");
        return Task.FromResult(new AddressCheck(true, norm, msgs, a.Residential));
    }

    public Task<List<RateQuote>> RateAsync(CarrierContext c, CarrierAddress from, CarrierAddress to, List<CarrierPackage> packages, string? freightClass, CancellationToken ct)
    {
        Transient(c);
        var baseCharge = Charge(packages, to.Residential);
        DateOnly D(int days) => DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(days);
        return Task.FromResult(new List<RateQuote>
        {
            new("FX-GROUND", "Fixture Ground", Math.Round(baseCharge, 2), "USD", 5, D(5)),
            new("FX-2DAY", "Fixture 2nd Day", Math.Round(baseCharge * 2.1m, 2), "USD", 2, D(2)),
            new("FX-OVERNIGHT", "Fixture Overnight", Math.Round(baseCharge * 3.4m, 2), "USD", 1, D(1)),
        });
    }

    /// <summary>Simulated ground charge: base 8.50 + 1.25 per kg + 0.75 per package + 4.10 residential surcharge.</summary>
    public static decimal Charge(List<CarrierPackage> packages, bool residential) => 8.50m + packages.Sum(p => 1.25m * Math.Max(p.WeightKg, 0.1m) + 0.75m) + (residential ? 4.10m : 0);

    static string Tracking(string key, int n) => "FX" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{key}:{n}")))[..14];

    public Task<CreatedShipment> CreateShipmentAsync(CarrierContext c, CarrierShipmentRequest r, CancellationToken ct)
    {
        Transient(c);
        if (Services.All(s => s.Code != r.ServiceCode)) throw new CarrierFailure("service", $"Service {r.ServiceCode} is not offered by the fixture carrier.", retryable: false, 400);
        var pk = r.Packages.Select(p => new CreatedPackage(p.Number, Tracking(r.IdempotencyKey, p.Number), "PDF", LabelPdf(r.Reference, Tracking(r.IdempotencyKey, p.Number), r.ServiceCode, r.ShipTo, p, r.Packages.Count))).ToList();
        var factor = r.ServiceCode switch { "FX-2DAY" => 2.1m, "FX-OVERNIGHT" => 3.4m, _ => 1m };
        var charge = Math.Round(Charge(r.Packages, r.ShipTo.Residential) * factor, 2);
        return Task.FromResult(new CreatedShipment("FXS-" + Tracking(r.IdempotencyKey, 0)[2..], pk[0].TrackingNumber, charge, "USD", pk));
    }

    static byte[] LabelPdf(string reference, string tracking, string service, CarrierAddress to, CarrierPackage p, int count) =>
        new SimplePdf("SANDBOX - NOT A SHIPPING LABEL").Title("SANDBOX - NOT A SHIPPING LABEL")
            .Field("Carrier", "Fixture carrier (simulated)").Field("Service", service).Field("Tracking", tracking).Field("Package", $"{p.Number} of {count}")
            .Field("Ship to", string.Join(", ", new[] { to.Name, to.Line1, to.Line2, to.City, to.Region, to.PostalCode, to.Country }.Where(x => !string.IsNullOrWhiteSpace(x))))
            .Field("Reference", reference).Field("Weight", $"{p.WeightKg:0.###} kg").ToBytes();

    public Task<CarrierLabelDocument> LabelAsync(CarrierContext c, string carrierShipmentId, string trackingNumber, CancellationToken ct) =>
        Task.FromResult(new CarrierLabelDocument("PDF", new SimplePdf("SANDBOX - NOT A SHIPPING LABEL").Title("SANDBOX - NOT A SHIPPING LABEL").Field("Tracking", trackingNumber).Field("Carrier shipment", carrierShipmentId).ToBytes()));

    public Task CancelAsync(CarrierContext c, string carrierShipmentId, CancellationToken ct)
    {
        Transient(c);
        if (c.Opt("refuseCancel") == "true") throw new CarrierFailure("cancel", "The carrier refused the cancellation: the package was already picked up (simulated).", retryable: false, 409);
        return Task.CompletedTask;
    }

    public Task<List<TrackingUpdate>> TrackAsync(CarrierContext c, string trackingNumber, CancellationToken ct)
    {
        Transient(c);
        var t0 = new DateTimeOffset(2026, 1, 5, 14, 0, 0, TimeSpan.Zero);
        var list = new List<TrackingUpdate> { new(t0, "LABEL", "Shipping label created", "Origin facility", false), new(t0.AddHours(4), "PICKUP", "Picked up", "Origin facility", false), new(t0.AddHours(20), "TRANSIT", "In transit", "Hub", false) };
        if (c.Opt("holdDelivery") != "true") list.Add(new(t0.AddDays(2), "DELIVERED", "Delivered", "Destination", true));
        return Task.FromResult(list);
    }
}

/// <summary>
/// Generic REST carrier adapter: speaks the documented QualGuard carrier JSON contract (docs/setup/carrier-connections.md) to a
/// customer-run endpoint, usually integration middleware that fronts a carrier or a freight broker. HTTPS only (http on the same
/// machine for local testing). Auth: bearer token (default) or an API-key header (options authStyle = "header", headerName).
/// Paths can be overridden in options (paths.test, paths.validateAddress, paths.rates, paths.shipments, paths.label, paths.cancel, paths.track).
/// Used for LTL as well: freight class and handling units are sent with every rate and shipment request.
/// </summary>
public sealed class RestCarrierAdapter(CarrierProvider provider) : ICarrierAdapter
{
    public CarrierProvider Provider => provider;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public CarrierCapabilities Capabilities(CarrierConnection c) => new(provider == CarrierProvider.Ltl ? "LTL (REST contract)" : "Generic REST",
        provider == CarrierProvider.Ltl ? "LTL freight through the QualGuard carrier contract (freight class, handling units, PRO number as tracking)." : "Any carrier or middleware that implements the QualGuard carrier contract.",
        true, true, true, true, true, true, SandboxOnly: false, RequiresBaseUrl: true, RequiresSecret: false, ["PDF", "ZPL"], [],
        ["The endpoint's behaviour is defined by the customer's middleware; confirm each operation with a sandbox account before production."], "Unverified until a connection test passes");

    public static Uri Validate(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var u) || !(u.Scheme == Uri.UriSchemeHttps || (u.Scheme == Uri.UriSchemeHttp && u.IsLoopback)))
            throw new CarrierFailure("config", "The endpoint must be an https:// URL (http:// only on the same machine).", retryable: false);
        return u;
    }

    static string Path(CarrierContext c, string key, string fallback) => c.Options["paths"] is JsonObject p && p[key] is JsonValue v && v.TryGetValue<string>(out var s) && s.StartsWith('/') ? s : fallback;

    async Task<JsonNode?> SendAsync(CarrierContext c, HttpMethod method, string path, object? body, string? idempotencyKey, CancellationToken ct)
    {
        var u = new Uri(Validate(c.Connection.BaseUrl).ToString().TrimEnd('/') + path);
        using var req = new HttpRequestMessage(method, u);
        if (body is not null) req.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(c.Secret))
        {
            if (c.Opt("authStyle") == "header") req.Headers.TryAddWithoutValidation(c.Opt("headerName") ?? "X-Api-Key", c.Secret);
            else req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", c.Secret);
        }
        if (c.Connection.AccountNumber is { Length: > 0 } acct) req.Headers.TryAddWithoutValidation("X-Carrier-Account", acct);
        req.Headers.TryAddWithoutValidation("X-Environment", c.Connection.Environment == CarrierEnvironment.Production ? "production" : "sandbox");
        if (idempotencyKey is not null) req.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        HttpResponseMessage res;
        try { res = await c.Http.SendAsync(req, ct); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            throw new CarrierFailure("network", $"The carrier endpoint at {u.Host} could not be reached ({(e is TaskCanceledException ? "timed out" : e.Message)}).", retryable: true);
        }
        using (res)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            var code = (int)res.StatusCode;
            if (!res.IsSuccessStatusCode)
            {
                string? detail = null;
                try { var n = JsonNode.Parse(text); detail = n?["message"]?.GetValue<string>() ?? n?["error"]?.GetValue<string>(); } catch (JsonException) { }
                var kind = code is 401 or 403 ? "auth" : code == 429 ? "rate_limited" : code >= 500 ? "carrier_unavailable" : "rejected";
                throw new CarrierFailure(kind, $"The carrier returned HTTP {code}{(detail is null ? "" : ": " + (detail.Length > 300 ? detail[..300] : detail))}.", retryable: code == 429 || code >= 500, code);
            }
            if (text.Length == 0) return null;
            try { return JsonNode.Parse(text); }
            catch (JsonException) { throw new CarrierFailure("bad_response", "The carrier response was not valid JSON.", retryable: false, code); }
        }
    }

    static string Req(JsonNode? n, string field) => n?[field]?.GetValue<string>() is { Length: > 0 } s ? s : throw new CarrierFailure("bad_response", $"The carrier response has no {field}.", retryable: false);
    static object Addr(CarrierAddress a) => new { name = a.Name, line1 = a.Line1, line2 = a.Line2, city = a.City, region = a.Region, postalCode = a.PostalCode, country = a.Country, phone = a.Phone, residential = a.Residential };
    static CarrierAddress? ParseAddr(JsonNode? n) => n is null ? null : new(n["name"]?.GetValue<string>(), n["line1"]?.GetValue<string>(), n["line2"]?.GetValue<string>(), n["city"]?.GetValue<string>(), n["region"]?.GetValue<string>(),
        n["postalCode"]?.GetValue<string>(), n["country"]?.GetValue<string>(), n["phone"]?.GetValue<string>(), n["residential"]?.GetValue<bool>() ?? false);
    static object Pkgs(List<CarrierPackage> p) => p.Select(x => new { number = x.Number, reference = x.Reference, weightKg = x.WeightKg, lengthCm = x.LengthCm, widthCm = x.WidthCm, heightCm = x.HeightCm }).ToList();

    public async Task<string> TestAsync(CarrierContext c, CancellationToken ct)
    {
        var n = await SendAsync(c, HttpMethod.Get, Path(c, "test", "/v1/ping"), null, null, ct);
        return n?["message"]?.GetValue<string>() is { } m ? $"Endpoint answered: {(m.Length > 200 ? m[..200] : m)}" : "Endpoint answered.";
    }

    public async Task<AddressCheck> ValidateAddressAsync(CarrierContext c, CarrierAddress a, CancellationToken ct)
    {
        var n = await SendAsync(c, HttpMethod.Post, Path(c, "validateAddress", "/v1/addresses/validate"), new { address = Addr(a) }, null, ct);
        var msgs = n?["messages"] is JsonArray arr ? arr.Select(x => x?.GetValue<string>() ?? "").Where(x => x.Length > 0).ToList() : [];
        return new AddressCheck(n?["valid"]?.GetValue<bool>() ?? false, ParseAddr(n?["normalized"]), msgs, n?["residential"]?.GetValue<bool>());
    }

    public async Task<List<RateQuote>> RateAsync(CarrierContext c, CarrierAddress from, CarrierAddress to, List<CarrierPackage> packages, string? freightClass, CancellationToken ct)
    {
        var n = await SendAsync(c, HttpMethod.Post, Path(c, "rates", "/v1/rates"), new { shipper = Addr(from), shipTo = Addr(to), packages = Pkgs(packages), freightClass }, null, ct);
        if (n?["rates"] is not JsonArray rates) throw new CarrierFailure("bad_response", "The carrier response has no rates.", retryable: false);
        return rates.Where(r => r is not null).Select(r => new RateQuote(Req(r, "serviceCode"), r!["serviceName"]?.GetValue<string>() ?? Req(r, "serviceCode"),
            r["charge"]?.GetValue<decimal>() ?? throw new CarrierFailure("bad_response", "A rate has no charge.", retryable: false), r["currency"]?.GetValue<string>() ?? "USD",
            r["transitDays"]?.GetValue<int>(), r["estimatedDelivery"]?.GetValue<string>() is { } d && DateOnly.TryParse(d, CultureInfo.InvariantCulture, out var dd) ? dd : null)).ToList();
    }

    public async Task<CreatedShipment> CreateShipmentAsync(CarrierContext c, CarrierShipmentRequest r, CancellationToken ct)
    {
        var n = await SendAsync(c, HttpMethod.Post, Path(c, "shipments", "/v1/shipments"), new
        {
            reference = r.Reference, serviceCode = r.ServiceCode, shipper = Addr(r.Shipper), shipTo = Addr(r.ShipTo), packages = Pkgs(r.Packages), freightClass = r.FreightClass, instructions = r.Instructions,
            labelFormat = c.Opt("labelFormat") ?? "PDF",
        }, r.IdempotencyKey, ct);
        var id = Req(n, "shipmentId");
        var pk = n?["packages"] is JsonArray arr ? arr.Where(x => x is not null).Select(x => new CreatedPackage(x!["number"]?.GetValue<int>() ?? 1, Req(x, "trackingNumber"), x["labelFormat"]?.GetValue<string>() ?? "PDF",
            x["label"]?.GetValue<string>() is { Length: > 0 } b64 ? Convert.FromBase64String(b64) : null)).ToList() : [];
        if (pk.Count == 0) throw new CarrierFailure("bad_response", "The carrier response has no packages.", retryable: false);
        return new CreatedShipment(id, n?["trackingNumber"]?.GetValue<string>() ?? pk[0].TrackingNumber, n?["charge"]?.GetValue<decimal>(), n?["currency"]?.GetValue<string>(), pk);
    }

    public async Task<CarrierLabelDocument> LabelAsync(CarrierContext c, string carrierShipmentId, string trackingNumber, CancellationToken ct)
    {
        var n = await SendAsync(c, HttpMethod.Get, Path(c, "label", "/v1/shipments/{id}/labels/{tracking}").Replace("{id}", Uri.EscapeDataString(carrierShipmentId)).Replace("{tracking}", Uri.EscapeDataString(trackingNumber)), null, null, ct);
        return new CarrierLabelDocument(n?["format"]?.GetValue<string>() ?? "PDF", Convert.FromBase64String(Req(n, "label")));
    }

    public Task CancelAsync(CarrierContext c, string carrierShipmentId, CancellationToken ct) =>
        SendAsync(c, HttpMethod.Post, Path(c, "cancel", "/v1/shipments/{id}/cancel").Replace("{id}", Uri.EscapeDataString(carrierShipmentId)), new { }, null, ct);

    public async Task<List<TrackingUpdate>> TrackAsync(CarrierContext c, string trackingNumber, CancellationToken ct)
    {
        var n = await SendAsync(c, HttpMethod.Get, Path(c, "track", "/v1/tracking/{tracking}").Replace("{tracking}", Uri.EscapeDataString(trackingNumber)), null, null, ct);
        if (n?["events"] is not JsonArray ev) return [];
        return ev.Where(e => e is not null).Select(e => new TrackingUpdate(DateTimeOffset.Parse(Req(e, "occurredAt"), CultureInfo.InvariantCulture), Req(e, "code"), e!["description"]?.GetValue<string>() ?? Req(e, "code"),
            e["location"]?.GetValue<string>(), e["delivered"]?.GetValue<bool>() ?? false)).ToList();
    }
}

/// <summary>
/// Provider slot for a parcel carrier's own API (UPS, FedEx, DHL). It stores the account and tests authentication against the OAuth
/// token endpoint the connection names (options tokenUrl; the defaults are the carriers' published sandbox/production token URLs and are
/// flagged unverified). Shipment operations are not implemented: they need to be built and certified against the carrier's sandbox with
/// a real developer account, so the connection cannot be enabled for shipping yet. No carrier behaviour is assumed beyond the token call.
/// </summary>
public sealed class CarrierApiSlotAdapter(CarrierProvider provider) : ICarrierAdapter
{
    public CarrierProvider Provider => provider;

    static string? DefaultTokenUrl(CarrierProvider p, CarrierEnvironment env) => (p, env) switch
    {
        (CarrierProvider.Ups, CarrierEnvironment.Sandbox) => "https://wwwcie.ups.com/security/v1/oauth/token",
        (CarrierProvider.Ups, _) => "https://onlinetools.ups.com/security/v1/oauth/token",
        (CarrierProvider.FedEx, CarrierEnvironment.Sandbox) => "https://apis-sandbox.fedex.com/oauth/token",
        (CarrierProvider.FedEx, _) => "https://apis.fedex.com/oauth/token",
        _ => null, // DHL has several APIs (Express, eCommerce, Freight) with different auth; the customer names the one their account uses
    };

    public CarrierCapabilities Capabilities(CarrierConnection c) => new(provider.ToString(),
        $"{provider} account slot: stores the account and tests authentication. Address validation, rating, labels, cancellation and tracking through {provider}'s own API are not built yet; use the generic REST adapter through integration middleware meanwhile.",
        false, false, false, false, false, false, SandboxOnly: false, RequiresBaseUrl: false, RequiresSecret: true, [], [],
        [$"Token endpoint {(DefaultTokenUrl(provider, c.Environment) ?? "(not set; enter options.tokenUrl)")} is taken from {provider}'s developer documentation and has not been tested with a live account.",
         $"Shipment operations through the {provider} API are not implemented and have not been certified with {provider}."],
        "Provider slot: authentication test only; shipping operations not available");

    public async Task<string> TestAsync(CarrierContext c, CancellationToken ct)
    {
        var url = c.Opt("tokenUrl") ?? DefaultTokenUrl(provider, c.Connection.Environment) ?? throw new CarrierFailure("config", $"Enter the {provider} token URL in options.tokenUrl.", retryable: false);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || !(u.Scheme == Uri.UriSchemeHttps || (u.Scheme == Uri.UriSchemeHttp && u.IsLoopback))) throw new CarrierFailure("config", "The token URL must be https://.", retryable: false);
        if (string.IsNullOrEmpty(c.Connection.ClientId) || string.IsNullOrEmpty(c.Secret)) throw new CarrierFailure("auth", "Enter the client id and client secret from the carrier's developer portal.", retryable: false);
        using var req = new HttpRequestMessage(HttpMethod.Post, u);
        var form = new Dictionary<string, string> { ["grant_type"] = "client_credentials" };
        if (c.Opt("authStyle") == "form") { form["client_id"] = c.Connection.ClientId!; form["client_secret"] = c.Secret!; }
        else req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{c.Connection.ClientId}:{c.Secret}")));
        req.Content = new FormUrlEncodedContent(form);
        HttpResponseMessage res;
        try { res = await c.Http.SendAsync(req, ct); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            throw new CarrierFailure("network", $"{provider} could not be reached at {u.Host}.", retryable: true);
        }
        using (res)
        {
            var code = (int)res.StatusCode;
            if (!res.IsSuccessStatusCode) throw new CarrierFailure(code is 400 or 401 or 403 ? "auth" : "carrier_unavailable", $"{provider} refused the credentials: HTTP {code}.", retryable: code >= 500 || code == 429, code);
            var text = await res.Content.ReadAsStringAsync(ct);
            try { if (JsonNode.Parse(text)?["access_token"]?.GetValue<string>() is { Length: > 0 }) return $"{provider} issued an access token (authentication only; shipping operations are not available through this adapter)."; }
            catch (JsonException) { }
            throw new CarrierFailure("bad_response", $"{provider} answered without an access token.", retryable: false, code);
        }
    }

    CarrierNotSupported No(string op) => new($"{op} through the {provider} API is not available yet; use a generic REST connection through integration middleware.");
    public Task<AddressCheck> ValidateAddressAsync(CarrierContext c, CarrierAddress a, CancellationToken ct) => throw No("Address validation");
    public Task<List<RateQuote>> RateAsync(CarrierContext c, CarrierAddress from, CarrierAddress to, List<CarrierPackage> packages, string? freightClass, CancellationToken ct) => throw No("Rating");
    public Task<CreatedShipment> CreateShipmentAsync(CarrierContext c, CarrierShipmentRequest r, CancellationToken ct) => throw No("Shipment creation");
    public Task<CarrierLabelDocument> LabelAsync(CarrierContext c, string carrierShipmentId, string trackingNumber, CancellationToken ct) => throw No("Label retrieval");
    public Task CancelAsync(CarrierContext c, string carrierShipmentId, CancellationToken ct) => throw No("Cancellation");
    public Task<List<TrackingUpdate>> TrackAsync(CarrierContext c, string trackingNumber, CancellationToken ct) => throw No("Tracking");
}
