using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using QualGuardIQ.Domain.Messaging;

namespace QualGuardIQ.Infrastructure.Modules.Messaging;

public sealed record MessagingContext(MessagingProvider Provider, string? Secret, HttpClient Http);
public sealed record OutgoingMessage(Guid DeliveryId, string Address, string? Title, string Text);
/// <summary>Accepted = the provider took the message (Sent, or Delivered when it says so); Retry = transient failure; otherwise permanent failure.</summary>
public sealed record SendOutcome(bool Accepted, bool Delivered, bool Retry, string? ProviderMessageId, string? Error);

public interface IMessagingAdapter
{
    MessagingAdapterKind Kind { get; }
    /// <summary>False for vendor slots that only check credentials: they can never be enabled or send.</summary>
    bool CanSend { get; }
    string Description { get; }
    Task<string> TestAsync(MessagingContext c, CancellationToken ct);
    Task<SendOutcome> SendAsync(MessagingContext c, OutgoingMessage m, CancellationToken ct);
}

public sealed class MessagingException(string message) : Exception(message);

/// <summary>In-memory provider for automated tests and sandboxes. Addresses ending in "0000" fail permanently, "9999" fail transiently once, then succeed.</summary>
public sealed class FixtureMessagingAdapter : IMessagingAdapter
{
    public static readonly ConcurrentDictionary<Guid, ConcurrentQueue<OutgoingMessage>> Sent = new();
    static readonly ConcurrentDictionary<string, int> Transient = new();
    public MessagingAdapterKind Kind => MessagingAdapterKind.Fixture;
    public bool CanSend => true;
    public string Description => "Test fixture: records messages in memory, never contacts a network. Sandbox only.";
    public static IReadOnlyList<OutgoingMessage> Received(Guid providerId) => Sent.TryGetValue(providerId, out var q) ? [.. q] : [];

    public Task<string> TestAsync(MessagingContext c, CancellationToken ct)
    {
        if (c.Provider.Environment == MessagingEnvironment.Production) throw new MessagingException("The fixture provider is for tests and sandboxes; it cannot be used in production.");
        return Task.FromResult("Fixture provider ready (no network).");
    }

    public Task<SendOutcome> SendAsync(MessagingContext c, OutgoingMessage m, CancellationToken ct)
    {
        if (m.Address.EndsWith("0000", StringComparison.Ordinal)) return Task.FromResult(new SendOutcome(false, false, false, null, "Unknown subscriber (fixture)"));
        if (m.Address.EndsWith("9999", StringComparison.Ordinal) && Transient.AddOrUpdate(m.DeliveryId.ToString(), 1, (_, n) => n + 1) == 1)
            return Task.FromResult(new SendOutcome(false, false, true, null, "Gateway timeout (fixture)"));
        Sent.GetOrAdd(c.Provider.Id, _ => new()).Enqueue(m);
        return Task.FromResult(new SendOutcome(true, false, false, "fx-" + m.DeliveryId.ToString("N")[..12], null));
    }
}

/// <summary>
/// The customer's own HTTPS gateway. Send: POST {endpoint} with bearer secret and {"id","channel","to","from","title","text"};
/// a 2xx answer with optional {"messageId","status":"sent|delivered"} is accepted, 429/5xx/network errors are retried, other 4xx fail.
/// Test: GET {endpoint}/health (or the endpoint itself) with the bearer secret must answer 2xx. Delivery receipts come back through the
/// status callback endpoint with the provider's callback token.
/// </summary>
public sealed class GenericHttpMessagingAdapter : IMessagingAdapter
{
    public MessagingAdapterKind Kind => MessagingAdapterKind.GenericHttp;
    public bool CanSend => true;
    public string Description => "Your own HTTPS SMS or push gateway (contract in docs/setup/sms-push-providers.md).";

    static HttpRequestMessage Req(HttpMethod method, string url, string? secret, object? body = null)
    {
        var r = new HttpRequestMessage(method, url);
        if (!string.IsNullOrEmpty(secret)) r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        if (body is not null) r.Content = JsonContent.Create(body);
        return r;
    }

    public async Task<string> TestAsync(MessagingContext c, CancellationToken ct)
    {
        var url = c.Provider.Endpoint ?? throw new MessagingException("Set the gateway endpoint.");
        if (string.IsNullOrEmpty(c.Secret)) throw new MessagingException("The gateway secret is not available (check the secret reference).");
        using var r = await c.Http.SendAsync(Req(HttpMethod.Get, url.TrimEnd('/') + "/health", c.Secret), ct);
        if (!r.IsSuccessStatusCode) throw new MessagingException($"Gateway health check answered {(int)r.StatusCode} {r.ReasonPhrase}.");
        return $"Gateway answered {(int)r.StatusCode}.";
    }

    public async Task<SendOutcome> SendAsync(MessagingContext c, OutgoingMessage m, CancellationToken ct)
    {
        try
        {
            using var r = await c.Http.SendAsync(Req(HttpMethod.Post, c.Provider.Endpoint!, c.Secret, new { id = m.DeliveryId, channel = c.Provider.Channel.ToString().ToLowerInvariant(), to = m.Address, from = c.Provider.Sender, title = m.Title, text = m.Text }), ct);
            var body = await r.Content.ReadAsStringAsync(ct);
            if (r.IsSuccessStatusCode)
            {
                JsonNode? n = null; try { n = body.Length > 0 ? JsonNode.Parse(body) : null; } catch (System.Text.Json.JsonException) { }
                return new SendOutcome(true, string.Equals(n?["status"]?.ToString(), "delivered", StringComparison.OrdinalIgnoreCase), false, n?["messageId"]?.ToString(), null);
            }
            var retry = (int)r.StatusCode is 429 or >= 500;
            return new SendOutcome(false, false, retry, null, $"{(int)r.StatusCode} {r.ReasonPhrase}: {(body.Length > 200 ? body[..200] : body)}");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return new SendOutcome(false, false, true, null, "Network error: " + e.Message); }
    }
}

/// <summary>Twilio, Firebase Cloud Messaging and Apple Push slots: registered so a deployment can record its account, but no vendor API is implemented in this build. They cannot be enabled.</summary>
public sealed class VendorSlotMessagingAdapter(MessagingAdapterKind kind) : IMessagingAdapter
{
    public MessagingAdapterKind Kind => kind;
    public bool CanSend => false;
    public string Description => $"{kind switch { MessagingAdapterKind.TwilioSlot => "Twilio SMS", MessagingAdapterKind.FcmSlot => "Firebase Cloud Messaging", _ => "Apple Push Notification service" }}: not implemented in this build (no live account was available to validate it). Use a Generic HTTP gateway.";
    public Task<string> TestAsync(MessagingContext c, CancellationToken ct) => throw new MessagingException(Description);
    public Task<SendOutcome> SendAsync(MessagingContext c, OutgoingMessage m, CancellationToken ct) => Task.FromResult(new SendOutcome(false, false, false, null, Description));
}
