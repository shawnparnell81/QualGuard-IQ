using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using QualGuardIQ.Domain.Integration;

namespace QualGuardIQ.Infrastructure.Modules.Integration.Erp;

/// <summary>An item from the research that is not confirmed in official vendor docs. Shown in each connection's setup notes.</summary>
public sealed record UnverifiedItem(string Topic, string Detail, string Source);

/// <summary>What an adapter (edition) can do. Every adapter ships read-only; writes are listed only where the vendor documents them.</summary>
public sealed record ErpCapabilities(string Edition, IReadOnlyList<ErpEntity> Read, IReadOnlyList<ErpEntity> Write, bool Delta, string Auth, string Api, string Paging,
    string RateLimits, string Events, int DefaultConcurrency, int DefaultRequestsPerMinute, string Doc);

/// <summary>A vendor record mapped to canonical fields (see ErpFieldMap), plus the raw vendor JSON.</summary>
public sealed record ExternalRecord(string ExternalId, string? Version, DateTimeOffset? LastModified, JsonObject Fields, string Raw);

public sealed record ErpCursor(DateTimeOffset? Watermark, string? PageToken);
public sealed record ErpPage(IReadOnlyList<ExternalRecord> Records, string? NextPageToken);

public sealed class ErpException(string message, int? status = null) : Exception(message)
{
    public int? Status { get; } = status;
}

/// <summary>Everything an adapter needs for one call sequence. Secrets are already decrypted and never leave the adapter.</summary>
public sealed class ErpCallContext
{
    public required ErpConnection Connection { get; init; }
    public string? Secret { get; init; }
    public string? SecondarySecret { get; init; }
    /// <summary>Named secrets from the connection's setup profile, already decrypted.</summary>
    public IReadOnlyDictionary<string, string> Secrets { get; init; } = new Dictionary<string, string>();
    /// <summary>Secrets the vendor replaced during this call (for example a rotated refresh token). The caller encrypts and stores them.</summary>
    public Dictionary<string, string> Rotated { get; } = new();
    public required JsonObject Options { get; init; }
    public required ErpHttp Http { get; init; }
    public required ErpTokenCache Tokens { get; init; }
    public Dictionary<string, string> FieldMap { get; init; } = new();
    public int PageSize { get; init; }
    public DateTimeOffset Now { get; init; } = DateTimeOffset.UtcNow;

    public string Opt(string key, string fallback = "") => Options[key]?.GetValue<string>() is { Length: > 0 } v ? v : fallback;
    public int OptInt(string key, int fallback) => Options[key] is JsonValue v && v.TryGetValue<int>(out var i) ? i : int.TryParse(Opt(key), out var j) ? j : fallback;
    public string RequireSecret(string what) => string.IsNullOrEmpty(Secret) ? throw new ErpException($"Set the {what} on the connection first.") : Secret;
    public string Base => Connection.BaseUrl.TrimEnd('/');

    /// <summary>A named secret: the latest rotated value, then the stored value, then the fallback slot (older connections kept it in Secret / SecondarySecret).</summary>
    public string? SecretOf(string name, string? fallback = null) =>
        Rotated.TryGetValue(name, out var r) ? r : Secrets.TryGetValue(name, out var v) && v.Length > 0 ? v : string.IsNullOrEmpty(fallback) ? null : fallback;
    public string RequireNamed(string name, string label, string? fallback = null) => SecretOf(name, fallback) ?? throw new ErpException($"Set the {label} on the connection first.");
}

/// <summary>Access tokens and sessions per connection, reused until shortly before they expire (one session per connection).</summary>
public sealed class ErpTokenCache
{
    private readonly ConcurrentDictionary<string, (string Value, DateTimeOffset Expires)> _items = new();
    public async Task<string> GetAsync(Guid connectionId, string kind, Func<Task<(string Value, TimeSpan Lifetime)>> mint, DateTimeOffset now)
    {
        var key = connectionId + "|" + kind;
        if (_items.TryGetValue(key, out var t) && t.Expires > now) return t.Value;
        var (v, life) = await mint();
        // renew one minute (or 10%) before expiry
        var margin = TimeSpan.FromSeconds(Math.Min(60, life.TotalSeconds / 10));
        _items[key] = (v, now + life - margin);
        return v;
    }
    public void Invalidate(Guid connectionId, string kind) => _items.TryRemove(connectionId + "|" + kind, out _);
    public void InvalidateAll(Guid connectionId) { foreach (var k in _items.Keys.Where(k => k.StartsWith(connectionId.ToString()))) _items.TryRemove(k, out _); }
}

/// <summary>
/// HTTP for adapters: per-connection concurrency cap and per-minute budget, retry on 429/503 honouring Retry-After
/// (seconds or HTTP date), bounded backoff, and vendor error parsing. Counts requests for the run log.
/// </summary>
public sealed class ErpHttp(HttpClient http, Guid connectionId, int maxConcurrency, int requestsPerMinute, Func<HttpResponseMessage, string, string?>? errorParser = null)
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Gates = new();
    private static readonly ConcurrentDictionary<Guid, Queue<DateTimeOffset>> Windows = new();
    /// <summary>Test hook: replaces Task.Delay so retry waits do not slow tests.</summary>
    public static Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;
    public int Requests { get; private set; }
    public List<TimeSpan> Waits { get; } = [];
    public Func<HttpResponseMessage, string, string?>? ErrorParser { get; set; } = errorParser;
    public const int MaxAttempts = 4;

    public async Task<(HttpResponseMessage Response, string Body)> SendAsync(Func<HttpRequestMessage> make, CancellationToken ct, bool throwOnError = true)
    {
        var gate = Gates.GetOrAdd(connectionId, _ => new SemaphoreSlim(Math.Max(1, maxConcurrency)));
        for (var attempt = 1; ; attempt++)
        {
            await ThrottleAsync(ct);
            await gate.WaitAsync(ct);
            HttpResponseMessage r; string body;
            try
            {
                Requests++;
                r = await http.SendAsync(make(), ct);
                body = await r.Content.ReadAsStringAsync(ct);
            }
            finally { gate.Release(); }
            if ((r.StatusCode == HttpStatusCode.TooManyRequests || r.StatusCode == HttpStatusCode.ServiceUnavailable) && attempt < MaxAttempts)
            {
                var wait = RetryAfter(r) ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
                if (wait > TimeSpan.FromMinutes(5)) wait = TimeSpan.FromMinutes(5);
                Waits.Add(wait);
                await Delay(wait, ct);
                continue;
            }
            if (!r.IsSuccessStatusCode && throwOnError)
            {
                var detail = ErrorParser?.Invoke(r, body) ?? Trim(body);
                throw new ErpException($"{(int)r.StatusCode} {r.ReasonPhrase}: {detail}", (int)r.StatusCode);
            }
            return (r, body);
        }
    }

    public async Task<JsonNode> JsonAsync(Func<HttpRequestMessage> make, CancellationToken ct)
    {
        var (_, body) = await SendAsync(make, ct);
        try { return JsonNode.Parse(body) ?? throw new ErpException("Empty response."); }
        catch (JsonException) { throw new ErpException("The response was not JSON: " + Trim(body)); }
    }

    private async Task ThrottleAsync(CancellationToken ct)
    {
        if (requestsPerMinute <= 0) return;
        var q = Windows.GetOrAdd(connectionId, _ => new Queue<DateTimeOffset>());
        TimeSpan wait;
        lock (q)
        {
            var now = DateTimeOffset.UtcNow;
            while (q.Count > 0 && now - q.Peek() > TimeSpan.FromMinutes(1)) q.Dequeue();
            wait = q.Count >= requestsPerMinute ? TimeSpan.FromMinutes(1) - (now - q.Peek()) : TimeSpan.Zero;
            q.Enqueue(now + wait);
        }
        if (wait > TimeSpan.Zero) { Waits.Add(wait); await Delay(wait, ct); }
    }

    internal static TimeSpan? RetryAfter(HttpResponseMessage r)
    {
        var ra = r.Headers.RetryAfter;
        if (ra?.Delta is { } d) return d;
        if (ra?.Date is { } at) { var w = at - DateTimeOffset.UtcNow; return w < TimeSpan.Zero ? TimeSpan.Zero : w; }
        return null;
    }

    public static string Trim(string s) => s.Length > 300 ? s[..300] + "…" : s;
}

/// <summary>Helpers shared by adapters: form posts, basic auth, JSON paths and canonical field mapping.</summary>
public static class ErpWire
{
    public static HttpRequestMessage Form(string url, IEnumerable<KeyValuePair<string, string>> fields, AuthenticationHeaderValue? auth = null)
    {
        var m = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(fields) };
        if (auth is not null) m.Headers.Authorization = auth;
        m.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return m;
    }

    public static HttpRequestMessage Get(string url, AuthenticationHeaderValue? auth = null, params (string Name, string Value)[] headers)
    {
        var m = new HttpRequestMessage(HttpMethod.Get, url);
        if (auth is not null) m.Headers.Authorization = auth;
        m.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        foreach (var (n, v) in headers) m.Headers.TryAddWithoutValidation(n, v);
        return m;
    }

    public static HttpRequestMessage PostJson(string url, object body, AuthenticationHeaderValue? auth = null, params (string Name, string Value)[] headers)
    {
        var m = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        if (auth is not null) m.Headers.Authorization = auth;
        m.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        foreach (var (n, v) in headers) m.Headers.TryAddWithoutValidation(n, v);
        return m;
    }

    public static AuthenticationHeaderValue Basic(string user, string password) => new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password)));
    public static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    /// <summary>OAuth 2.0 token response: access_token + expires_in (seconds).</summary>
    public static (string Token, TimeSpan Life) Token(JsonNode n)
    {
        var t = n["access_token"]?.GetValue<string>() ?? throw new ErpException("The token response has no access_token.");
        var exp = n["expires_in"] is JsonValue v ? (v.TryGetValue<int>(out var i) ? i : int.TryParse(v.ToString(), out var j) ? j : 3600) : 3600;
        return (t, TimeSpan.FromSeconds(exp));
    }

    /// <summary>Reads a dotted path ("a.b.0.c"). A trailing ".value" unwraps Acumatica-style {"value": x} fields automatically when present.</summary>
    public static JsonNode? Path(JsonNode? n, string path)
    {
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (n is null) return null;
            n = n is JsonArray a && int.TryParse(part, out var i) ? (i < a.Count ? a[i] : null) : n is JsonObject o ? o[part] : null;
        }
        return n;
    }

    public static string? Str(JsonNode? n, string path)
    {
        var v = Path(n, path);
        return v switch { null => null, JsonValue x => x.ToString(), _ => v.ToJsonString() };
    }

    public static DateTimeOffset? Date(JsonNode? n, string path)
    {
        var s = Str(n, path);
        if (string.IsNullOrWhiteSpace(s)) return null;
        // OData V2 "/Date(1700000000000+0000)/"
        if (s.StartsWith("/Date(", StringComparison.Ordinal))
        {
            var ms = new string(s[6..].TakeWhile(c => char.IsDigit(c) || c == '-').ToArray());
            return long.TryParse(ms, out var l) ? DateTimeOffset.FromUnixTimeMilliseconds(l) : null;
        }
        return DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : null;
    }

    /// <summary>
    /// Builds canonical fields from a vendor row. Map values are vendor paths; "lines" names the child array and
    /// "lines.x" keys map child fields. The connection's field map overrides the adapter default per key.
    /// </summary>
    public static JsonObject Map(JsonNode row, IReadOnlyDictionary<string, string> defaults, IReadOnlyDictionary<string, string> overrides)
    {
        var map = new Dictionary<string, string>(defaults);
        foreach (var (k, v) in overrides) map[k] = v;
        var o = new JsonObject();
        foreach (var (k, path) in map.Where(m => !m.Key.StartsWith("lines", StringComparison.Ordinal)))
        {
            var v = Path(row, path);
            if (v is JsonObject wrapped && wrapped["value"] is { } inner) v = inner;   // {"value": x}
            o[k] = v?.DeepClone();
        }
        if (map.TryGetValue("lines", out var linesPath) && Path(row, linesPath) is JsonArray arr)
        {
            var lines = new JsonArray();
            foreach (var line in arr.OfType<JsonNode>())
            {
                var lo = new JsonObject();
                foreach (var (k, p) in map.Where(m => m.Key.StartsWith("lines.", StringComparison.Ordinal)))
                {
                    var v = Path(line, p);
                    if (v is JsonObject w && w["value"] is { } i) v = i;
                    lo[k[6..]] = v?.DeepClone();
                }
                lines.Add(lo);
            }
            o["lines"] = lines;
        }
        return o;
    }

    public static string Iso(DateTimeOffset d) => d.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
}

/// <summary>One ERP adapter. Read-only by contract: there is no write method; outbound changes are not implemented for any vendor yet.</summary>
public interface IErpAdapter
{
    ErpSystem System { get; }
    IReadOnlyList<string> Editions { get; }
    ErpCapabilities Capabilities(string edition);
    IReadOnlyList<UnverifiedItem> Unverified(string edition);
    /// <summary>Default canonical-field map per entity (vendor field paths).</summary>
    IReadOnlyDictionary<string, string> DefaultMap(string edition, ErpEntity entity);
    /// <summary>Authenticates and reads one small page; returns a short human-readable result.</summary>
    Task<string> TestAsync(ErpCallContext ctx, CancellationToken ct);
    Task<ErpPage> ReadAsync(ErpCallContext ctx, ErpEntity entity, ErpCursor cursor, CancellationToken ct);
}

public static class ErpAdapterExtensions
{
    public static void RequireRead(this IErpAdapter a, string edition, ErpEntity e)
    {
        if (!a.Capabilities(edition).Read.Contains(e))
            throw new ErpException($"{a.System} ({edition}) has no documented read for {e}. See the connection's setup notes.");
    }

    public static ExternalRecord Record(this ErpCallContext ctx, IErpAdapter a, ErpEntity e, JsonNode row, string idPath, string? modifiedPath, string? versionPath = null)
    {
        var id = ErpWire.Str(row, idPath);
        if (string.IsNullOrWhiteSpace(id)) throw new ErpException($"A {e} row has no id at '{idPath}'.");
        var fields = ErpWire.Map(row, a.DefaultMap(ctx.Connection.Edition, e), ctx.FieldMap);
        DateTimeOffset? mod = modifiedPath is null ? null : ErpWire.Date(row, modifiedPath) ?? ErpWire.Date(row, modifiedPath + ".value");
        return new ExternalRecord(id, versionPath is null ? null : ErpWire.Str(row, versionPath), mod, fields, row.ToJsonString());
    }
}
