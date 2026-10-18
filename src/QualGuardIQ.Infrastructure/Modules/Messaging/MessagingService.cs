using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Messaging;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Integration;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Messaging;

public sealed record ProviderInput(string Name, MessagingChannel Channel, MessagingAdapterKind Adapter, MessagingEnvironment Environment, string? Endpoint, string? Sender, string? SecretReference,
    int RateLimitPerMinute = 30, int MaxAttempts = 3, bool RequireOptIn = true, List<string>? Keywords = null, Guid? TemplateId = null);
/// <summary>Write-only: null leaves the stored value unchanged, empty string clears it.</summary>
public sealed record ProviderSecretInput(string? Secret, string? CallbackToken);
public sealed record TemplateInput(string Name, MessagingChannel Channel, string Text, string? PushTitle, int MaxLength = 320);
public sealed record SubscriptionInput(MessagingChannel Channel, string Address, string? Label);
public sealed record StatusCallback(string MessageId, string Status, string? Error);
public sealed record ProviderRow(Guid Id, string Name, MessagingChannel Channel, MessagingAdapterKind Adapter, string AdapterDescription, bool CanSend, MessagingEnvironment Environment, string? Endpoint, string? Sender,
    string? SecretReference, bool SecretStored, bool CallbackTokenSet, bool Enabled, int RateLimitPerMinute, int MaxAttempts, bool RequireOptIn, List<string> Keywords, Guid? TemplateId,
    DateTimeOffset? LastTestAt, bool? LastTestOk, string? LastTestMessage, bool TestCurrent, DateTimeOffset? ProductionApprovedAt, bool CanEnable, string? EnableBlocker);
public sealed record SubscriptionRow(Guid Id, MessagingChannel Channel, string Address, string? Label, DateTimeOffset OptedInAt, DateTimeOffset? OptedOutAt);
public sealed record DeliveryRow(Guid Id, Guid ProviderId, long? NotificationId, Guid? UserId, string MaskedAddress, string Text, DeliveryState State, int Attempts, DateTimeOffset? NextAttemptAt, string? FailureReason,
    string? ProviderMessageId, bool IsTest, DateTimeOffset CreatedAt, DateTimeOffset? SentAt, DateTimeOffset? DeliveredAt);

/// <summary>Supplies the HTTP handler for messaging adapters; tests replace it.</summary>
public sealed class MessagingHttpProvider(Security.OutboundGuard guard)
{
    readonly SocketsHttpHandler Shared = guard.Handler(h => { h.UseCookies = false; h.PooledConnectionLifetime = TimeSpan.FromMinutes(5); });
    public Func<HttpMessageHandler>? Override { get; set; }
    public HttpClient Create() => Override is { } f ? new HttpClient(f(), true) { Timeout = TimeSpan.FromSeconds(30) } : new HttpClient(Shared, false) { Timeout = TimeSpan.FromSeconds(30) };
}

/// <summary>
/// SMS and push provider readiness alongside the generic webhook notification channels (which are unchanged): provider registration with
/// secret references, connection test bound to the configuration, templates, user opt-in, queued delivery with retry, rate limits, status
/// callbacks and a delivery log. No provider sends until it is tested and enabled; nothing claims delivery without a provider acceptance.
/// </summary>
public sealed partial class MessagingService(AppDbContext db, ModuleSupport m, SecretProtector secrets, IEnumerable<IMessagingAdapter> adapters, MessagingHttpProvider http, IConfiguration config, IClock clock,
    ILogger<MessagingService> log)
{
    public static readonly TimeSpan[] Backoff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1)];
    IMessagingAdapter Adapter(MessagingAdapterKind k) => adapters.FirstOrDefault(a => a.Kind == k) ?? throw new AppException($"No adapter for {k}.");
    async Task AdminAsync(CancellationToken ct) { if (!await m.CanAnyAsync(Permissions.ApiAdmin, ct)) throw new ForbiddenException("Managing SMS and push providers needs api.admin."); }

    [GeneratedRegex(@"^\+[1-9]\d{6,14}$")] private static partial Regex E164();
    [GeneratedRegex(@"^config:[A-Za-z0-9_.:-]{1,190}$")] private static partial Regex ConfigRef();

    public static string Fingerprint(MessagingProvider p) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\u001f", p.Channel, p.Adapter, p.Environment, p.Endpoint, p.Sender, p.SecretReference, p.EncryptedSecret))));

    // ---------- providers ----------
    public async Task<List<ProviderRow>> ProvidersAsync(CancellationToken ct) { await AdminAsync(ct); return (await db.MessagingProviders.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct)).Select(Row).ToList(); }
    public async Task<ProviderRow> ProviderAsync(Guid id, CancellationToken ct) { await AdminAsync(ct); return Row(await db.MessagingProviders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Provider")); }

    ProviderRow Row(MessagingProvider p)
    {
        var a = Adapter(p.Adapter);
        var current = p.LastTestOk == true && p.LastTestFingerprint == p.ConfigFingerprint;
        var blocker = !a.CanSend ? "This adapter is a slot only and cannot send." : !current ? "Run a passing connection test of the current configuration." : p.Environment == MessagingEnvironment.Production && p.ProductionApprovedAt is null ? "A production provider needs approval by a second person with integrations.approve." : null;
        return new(p.Id, p.Name, p.Channel, p.Adapter, a.Description, a.CanSend, p.Environment, p.Endpoint, p.Sender, p.SecretReference, p.EncryptedSecret is not null, p.EncryptedCallbackToken is not null, p.Enabled, p.RateLimitPerMinute, p.MaxAttempts,
            p.RequireOptIn, p.Keywords, p.TemplateId, p.LastTestAt, p.LastTestOk, p.LastTestMessage, current, p.ProductionApprovedAt, blocker is null, blocker);
    }

    public async Task<Guid> SaveProviderAsync(Guid? id, ProviderInput r, CancellationToken ct)
    {
        await AdminAsync(ct);
        ModuleSupport.Required(r.Name, "Name", 200);
        if (r.Adapter == MessagingAdapterKind.Fixture && r.Environment == MessagingEnvironment.Production) throw new AppException("The fixture provider is for tests and sandboxes only.");
        if (r.Adapter is MessagingAdapterKind.TwilioSlot && r.Channel != MessagingChannel.Sms || r.Adapter is MessagingAdapterKind.FcmSlot or MessagingAdapterKind.ApnsSlot && r.Channel != MessagingChannel.Push)
            throw new AppException("That adapter does not serve this channel.");
        if (r.Adapter == MessagingAdapterKind.GenericHttp && !(Uri.TryCreate(r.Endpoint, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.IsLoopback))) throw new AppException("The gateway endpoint must be an HTTPS URL.");
        if (r.SecretReference is { Length: > 0 } sr && sr != "stored" && !ConfigRef().IsMatch(sr)) throw new AppException("A secret reference is config:Section:Key (read from deployment configuration) or leave it blank and store the secret.");
        if (r.RateLimitPerMinute is < 1 or > 6000) throw new AppException("Rate limit must be 1–6000 messages per minute.");
        if (r.MaxAttempts is < 1 or > 6) throw new AppException("Attempts must be 1–6.");
        if (r.TemplateId is { } t && !await db.MessageTemplates.AnyAsync(x => x.Id == t && x.Channel == r.Channel, ct)) throw new AppException("Choose a template for the same channel.");
        var p = id is { } pid ? await db.MessagingProviders.FirstOrDefaultAsync(x => x.Id == pid, ct) ?? throw new NotFoundException("Provider") : new MessagingProvider { Enabled = false };
        if (await db.MessagingProviders.AnyAsync(x => x.Name == r.Name.Trim() && x.Id != p.Id, ct)) throw new ConflictException("Another provider has this name.");
        p.Name = r.Name.Trim(); p.Channel = r.Channel; p.Adapter = r.Adapter; p.Environment = r.Environment; p.Endpoint = string.IsNullOrWhiteSpace(r.Endpoint) ? null : r.Endpoint.Trim(); p.Sender = string.IsNullOrWhiteSpace(r.Sender) ? null : r.Sender.Trim();
        p.SecretReference = string.IsNullOrWhiteSpace(r.SecretReference) ? (p.EncryptedSecret is null ? null : "stored") : r.SecretReference.Trim();
        p.RateLimitPerMinute = r.RateLimitPerMinute; p.MaxAttempts = r.MaxAttempts; p.RequireOptIn = r.Channel == MessagingChannel.Sms || r.RequireOptIn; p.Keywords = (r.Keywords ?? []).Select(k => k.Trim()).Where(k => k.Length > 0).Distinct().ToList(); p.TemplateId = r.TemplateId;
        Reconfigured(p);
        if (id is null)
        {
            db.MessagingProviders.Add(p);
            p.LastNotificationId = await db.Notifications.MaxAsync(n => (long?)n.Id, ct) ?? 0; // never back-fill old notifications
        }
        await db.SaveChangesAsync(ct);
        return p.Id;
    }

    void Reconfigured(MessagingProvider p)
    {
        var fp = Fingerprint(p);
        if (fp == p.ConfigFingerprint) return;
        p.ConfigFingerprint = fp;
        if (p.Enabled) { p.Enabled = false; p.LastTestMessage = "Configuration changed: the provider was disabled; run the connection test again."; }
        p.ProductionApprovedAt = null; p.ProductionApprovedById = null;
    }

    public async Task SetSecretsAsync(Guid id, ProviderSecretInput r, CancellationToken ct)
    {
        await AdminAsync(ct);
        var p = await db.MessagingProviders.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Provider");
        if (r.Secret is not null)
        {
            p.EncryptedSecret = r.Secret.Length == 0 ? null : secrets.Protect(r.Secret);
            if (r.Secret.Length > 0) p.SecretReference = "stored"; else if (p.SecretReference == "stored") p.SecretReference = null;
        }
        if (r.CallbackToken is not null)
        {
            if (r.CallbackToken.Length is > 0 and < 24) throw new AppException("Use a callback token of at least 24 characters.");
            p.EncryptedCallbackToken = r.CallbackToken.Length == 0 ? null : secrets.Protect(r.CallbackToken);
        }
        Reconfigured(p);
        await db.SaveChangesAsync(ct);
    }

    string? ResolveSecret(MessagingProvider p) => p.SecretReference switch
    {
        null => null,
        "stored" => secrets.Unprotect(p.EncryptedSecret),
        var s when s.StartsWith("config:", StringComparison.Ordinal) => config[s["config:".Length..]] is { Length: > 0 } v ? v : null,
        _ => null,
    };

    public async Task<(bool Ok, string Message)> TestAsync(Guid id, CancellationToken ct)
    {
        await AdminAsync(ct);
        var p = await db.MessagingProviders.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Provider");
        using var client = http.Create();
        bool ok; string msg;
        try { msg = await Adapter(p.Adapter).TestAsync(new MessagingContext(p, ResolveSecret(p), client), ct); ok = true; }
        catch (Exception e) when (e is MessagingException or HttpRequestException or TaskCanceledException or CryptographicException) { msg = e.Message; ok = false; }
        p.LastTestAt = clock.UtcNow; p.LastTestOk = ok; p.LastTestMessage = msg.Length > 1000 ? msg[..1000] : msg; p.LastTestFingerprint = p.ConfigFingerprint; p.LastTestById = m.UserId;
        if (!ok && p.Enabled) p.Enabled = false;
        await db.SaveChangesAsync(ct);
        return (ok, msg);
    }

    public async Task SetEnabledAsync(Guid id, bool enabled, CancellationToken ct)
    {
        await AdminAsync(ct);
        var p = await db.MessagingProviders.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Provider");
        if (enabled && Row(p).EnableBlocker is { } why) throw new AppException(why);
        p.Enabled = enabled;
        await db.SaveChangesAsync(ct);
    }

    public async Task ApproveProductionAsync(Guid id, CancellationToken ct)
    {
        if (!await m.CanAnyAsync(Permissions.IntegrationsApprove, ct)) throw new ForbiddenException("Approving a production provider needs integrations.approve.");
        var p = await db.MessagingProviders.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Provider");
        if (p.Environment != MessagingEnvironment.Production) throw new AppException("Only production providers need approval.");
        if (p.LastTestOk != true || p.LastTestFingerprint != p.ConfigFingerprint) throw new AppException("Approve after a passing test of the current configuration.");
        if (p.LastTestById == m.UserId) throw new ForbiddenException("The person who ran the test cannot approve the provider.");
        p.ProductionApprovedAt = clock.UtcNow; p.ProductionApprovedById = m.UserId;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Sends one test message to an address the administrator gives (recorded in the delivery log as a test).</summary>
    public async Task<DeliveryRow> SendTestAsync(Guid id, string address, CancellationToken ct)
    {
        await AdminAsync(ct);
        var p = await db.MessagingProviders.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Provider");
        if (p.LastTestOk != true || p.LastTestFingerprint != p.ConfigFingerprint) throw new AppException("Run a passing connection test first.");
        ValidateAddress(p.Channel, address);
        var d = new MessageDelivery { ProviderId = p.Id, MaskedAddress = Mask(address), Text = "QualGuard IQ test message", IsTest = true, CreatedAt = clock.UtcNow };
        db.MessageDeliveries.Add(d);
        await SendOneAsync(p, d, address, null, ct);
        await db.SaveChangesAsync(ct);
        return Delivery(d);
    }

    // ---------- templates ----------
    public async Task<List<MessageTemplate>> TemplatesAsync(CancellationToken ct) { await AdminAsync(ct); return await db.MessageTemplates.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct); }
    public async Task<Guid> SaveTemplateAsync(Guid? id, TemplateInput r, CancellationToken ct)
    {
        await AdminAsync(ct);
        ModuleSupport.Required(r.Name, "Name", 200); ModuleSupport.Required(r.Text, "Text", 2000);
        if (r.MaxLength is < 20 or > 2000) throw new AppException("Maximum length must be 20–2000 characters.");
        var unknown = Regex.Matches(r.Text + " " + r.PushTitle, @"\{([a-z]+)\}").Select(x => x.Groups[1].Value).Distinct().Except(["title", "body", "link", "app"]).ToList();
        if (unknown.Count > 0) throw new AppException($"Unknown placeholder(s): {string.Join(", ", unknown.Select(u => "{" + u + "}"))}. Use {{title}}, {{body}}, {{link}}, {{app}}.");
        var t = id is { } tid ? await db.MessageTemplates.FirstOrDefaultAsync(x => x.Id == tid, ct) ?? throw new NotFoundException("Template") : new MessageTemplate();
        t.Name = r.Name.Trim(); t.Channel = r.Channel; t.Text = r.Text.Trim(); t.PushTitle = string.IsNullOrWhiteSpace(r.PushTitle) ? null : r.PushTitle.Trim(); t.MaxLength = r.MaxLength;
        if (id is null) db.MessageTemplates.Add(t);
        await db.SaveChangesAsync(ct);
        return t.Id;
    }

    public static string Render(string template, int max, string app, string title, string? body, string? link)
    {
        var s = template.Replace("{app}", app).Replace("{title}", title).Replace("{body}", body ?? "").Replace("{link}", link ?? "");
        s = Regex.Replace(s, @"[ \t]+", " ").Trim();
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }

    // ---------- opt-in (each user manages their own) ----------
    static void ValidateAddress(MessagingChannel ch, string address)
    {
        if (ch == MessagingChannel.Sms && !E164().IsMatch(address)) throw new AppException("Enter the phone number in international format, e.g. +15551234567.");
        if (ch == MessagingChannel.Push && (address.Length is < 16 or > 500 || address.Any(char.IsWhiteSpace))) throw new AppException("The device token is not valid.");
    }
    public static string Mask(string a) => a.Length <= 4 ? "****" : new string('*', Math.Min(8, a.Length - 4)) + a[^4..];

    public async Task<List<SubscriptionRow>> MySubscriptionsAsync(CancellationToken ct) { var uid = m.UserId; return await db.MessagingSubscriptions.AsNoTracking().Where(x => x.UserId == uid).OrderBy(x => x.Channel).Select(x => new SubscriptionRow(x.Id, x.Channel, x.Address, x.Label, x.OptedInAt, x.OptedOutAt)).ToListAsync(ct); }

    public async Task<Guid> OptInAsync(SubscriptionInput r, CancellationToken ct)
    {
        var address = r.Address.Trim();
        ValidateAddress(r.Channel, address);
        var uid = m.UserId;
        var s = await db.MessagingSubscriptions.FirstOrDefaultAsync(x => x.UserId == uid && x.Channel == r.Channel && x.Address == address, ct);
        if (s is null) { s = new MessagingSubscription { UserId = uid, Channel = r.Channel, Address = address }; db.MessagingSubscriptions.Add(s); }
        s.Label = string.IsNullOrWhiteSpace(r.Label) ? null : r.Label.Trim()[..Math.Min(100, r.Label.Trim().Length)]; s.OptedInAt = clock.UtcNow; s.OptedOutAt = null;
        await db.SaveChangesAsync(ct);
        return s.Id;
    }

    public async Task OptOutAsync(Guid id, CancellationToken ct)
    {
        var uid = m.UserId;
        var s = await db.MessagingSubscriptions.FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid, ct) ?? throw new NotFoundException("Subscription");
        s.OptedOutAt ??= clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    // ---------- delivery ----------
    public async Task<List<DeliveryRow>> DeliveriesAsync(Guid? providerId, DeliveryState? state, CancellationToken ct)
    {
        await AdminAsync(ct);
        var q = db.MessageDeliveries.AsNoTracking();
        if (providerId is { } p) q = q.Where(x => x.ProviderId == p);
        if (state is { } s) q = q.Where(x => x.State == s);
        return (await q.OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct)).Select(Delivery).ToList();
    }

    static DeliveryRow Delivery(MessageDelivery d) => new(d.Id, d.ProviderId, d.NotificationId, d.UserId, d.MaskedAddress, d.Text, d.State, d.Attempts, d.NextAttemptAt, d.FailureReason, d.ProviderMessageId, d.IsTest, d.CreatedAt, d.SentAt, d.DeliveredAt);

    /// <summary>Background: queue messages for new notifications on every enabled provider, then send what is due within each provider's rate limit.</summary>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var providers = await db.MessagingProviders.Where(p => p.Enabled).ToListAsync(ct);
        var app = await db.OrganizationSettings.Select(s => s.ApplicationName).FirstOrDefaultAsync(ct) ?? "QualGuard IQ";
        var baseUrl = (config["App:BaseUrl"] ?? "").TrimEnd('/');
        var sent = 0;
        foreach (var p in providers)
        {
            if (Row(p).EnableBlocker is { } why) { p.Enabled = false; p.LastTestMessage = "Disabled automatically: " + why; continue; }
            var template = p.TemplateId is { } tid ? await db.MessageTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tid, ct) : null;
            var notes = await db.Notifications.AsNoTracking().Where(n => n.Id > p.LastNotificationId).OrderBy(n => n.Id).Take(500).ToListAsync(ct);
            if (notes.Count > 0)
            {
                var userIds = notes.Select(n => n.UserId).Distinct().ToList();
                var subs = await db.MessagingSubscriptions.AsNoTracking().Where(s => userIds.Contains(s.UserId) && s.Channel == p.Channel && s.OptedOutAt == null).ToListAsync(ct);
                var active = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id) && u.IsActive).Select(u => u.Id).ToListAsync(ct);
                foreach (var n in notes)
                {
                    if (!active.Contains(n.UserId)) continue;
                    if (p.Keywords.Count > 0 && !p.Keywords.Any(k => n.Title.Contains(k, StringComparison.OrdinalIgnoreCase))) continue;
                    foreach (var s in subs.Where(s => s.UserId == n.UserId))
                    {
                        var text = Render(template?.Text ?? "{app}: {title} {link}", template?.MaxLength ?? (p.Channel == MessagingChannel.Sms ? 320 : 1000), app, n.Title, n.Body, n.Link is null ? null : baseUrl + n.Link);
                        db.MessageDeliveries.Add(new MessageDelivery { ProviderId = p.Id, NotificationId = n.Id, UserId = n.UserId, SubscriptionId = s.Id, MaskedAddress = Mask(s.Address), Text = text, CreatedAt = now, State = DeliveryState.Queued });
                    }
                }
                p.LastNotificationId = notes[^1].Id;
                await db.SaveChangesAsync(ct);
            }

            // rate limit: messages accepted by this provider in the last minute
            var budget = p.RateLimitPerMinute - await db.MessageDeliveries.CountAsync(d => d.ProviderId == p.Id && d.SentAt > now.AddMinutes(-1), ct);
            if (budget <= 0) continue;
            var due = await db.MessageDeliveries.Where(d => d.ProviderId == p.Id && d.State == DeliveryState.Queued && (d.NextAttemptAt == null || d.NextAttemptAt <= now)).OrderBy(d => d.CreatedAt).Take(budget).ToListAsync(ct);
            foreach (var d in due)
            {
                var sub = d.SubscriptionId is { } sid ? await db.MessagingSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sid, ct) : null;
                if (sub is null || sub.OptedOutAt is not null) { d.State = DeliveryState.Skipped; d.FailureReason = "The recipient opted out before sending."; continue; }
                if (await SendOneAsync(p, d, sub.Address, template?.PushTitle is { } pt ? Render(pt, 200, app, "", null, null) : null, ct)) sent++;
            }
            await db.SaveChangesAsync(ct);
        }
        await db.SaveChangesAsync(ct);
        return sent;
    }

    async Task<bool> SendOneAsync(MessagingProvider p, MessageDelivery d, string address, string? title, CancellationToken ct)
    {
        d.Attempts++;
        SendOutcome r;
        using (var client = http.Create())
        {
            try { r = await Adapter(p.Adapter).SendAsync(new MessagingContext(p, ResolveSecret(p), client), new OutgoingMessage(d.Id, address, title, d.Text), ct); }
            catch (CryptographicException) { r = new SendOutcome(false, false, false, null, "The stored secret cannot be decrypted; set it again."); }
        }
        if (r.Accepted)
        {
            d.State = r.Delivered ? DeliveryState.Delivered : DeliveryState.Sent; d.SentAt = clock.UtcNow; d.DeliveredAt = r.Delivered ? d.SentAt : null; d.ProviderMessageId = r.ProviderMessageId; d.FailureReason = null; d.NextAttemptAt = null;
            return true;
        }
        d.FailureReason = r.Error?.Length > 1000 ? r.Error[..1000] : r.Error;
        if (r.Retry && d.Attempts < p.MaxAttempts) { d.State = DeliveryState.Queued; d.NextAttemptAt = clock.UtcNow + Backoff[Math.Min(d.Attempts - 1, Backoff.Length - 1)]; }
        else { d.State = DeliveryState.Failed; d.NextAttemptAt = null; log.LogWarning("Message {Id} via {Provider} failed: {Error}", d.Id, p.Name, d.FailureReason); }
        return false;
    }

    /// <summary>Delivery receipt from the gateway (anonymous endpoint, authenticated by the provider's callback token).</summary>
    public async Task CallbackAsync(Guid providerId, string? token, StatusCallback r, CancellationToken ct)
    {
        var p = await db.MessagingProviders.FirstOrDefaultAsync(x => x.Id == providerId, ct);
        var expected = p?.EncryptedCallbackToken is { } enc ? secrets.Unprotect(enc) : null;
        if (expected is null || token is null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(expected))) throw new NotFoundException("Provider");
        var d = await db.MessageDeliveries.FirstOrDefaultAsync(x => x.ProviderId == providerId && x.ProviderMessageId == r.MessageId, ct) ?? throw new NotFoundException("Message");
        switch (r.Status.ToLowerInvariant())
        {
            case "delivered": d.State = DeliveryState.Delivered; d.DeliveredAt = clock.UtcNow; break;
            case "failed" or "undelivered": d.State = DeliveryState.Failed; d.FailureReason = string.IsNullOrWhiteSpace(r.Error) ? "The provider reported the message as undelivered." : r.Error[..Math.Min(1000, r.Error.Length)]; break;
            case "sent" or "queued": break;
            default: throw new AppException("Status must be sent, delivered, failed or undelivered.");
        }
        await db.SaveChangesAsync(ct);
    }
}
