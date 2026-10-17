using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Messaging;

public enum MessagingChannel { Sms = 0, Push = 1 }
/// <summary>Fixture is for automated tests and sandboxes only. GenericHttp posts to the customer's own gateway. The vendor slots only test credentials and cannot send.</summary>
public enum MessagingAdapterKind { Fixture = 0, GenericHttp = 1, TwilioSlot = 10, FcmSlot = 11, ApnsSlot = 12 }
public enum MessagingEnvironment { Sandbox = 0, Production = 1 }

/// <summary>
/// An SMS or push provider. Created disabled; enabling needs a passing connection test of the current configuration (and, for production,
/// approval by a second person with integrations.approve). Secrets are a reference to deployment configuration ("config:Section:Key")
/// or a write-only value encrypted at rest; neither is ever returned or written to the audit trail.
/// </summary>
public class MessagingProvider : Entity, IAudited
{
    public string Name { get; set; } = "";
    public MessagingChannel Channel { get; set; }
    public MessagingAdapterKind Adapter { get; set; }
    public MessagingEnvironment Environment { get; set; }
    /// <summary>HTTPS gateway endpoint (GenericHttp).</summary>
    public string? Endpoint { get; set; }
    /// <summary>Sender number, sender id or app id (not secret).</summary>
    public string? Sender { get; set; }
    /// <summary>"config:Section:Key" read from deployment configuration at send time, or "stored" when an encrypted value is kept.</summary>
    public string? SecretReference { get; set; }
    public string? EncryptedSecret { get; set; }
    /// <summary>Shared token the gateway sends back on delivery-status callbacks (encrypted).</summary>
    public string? EncryptedCallbackToken { get; set; }
    public bool Enabled { get; set; }
    public int RateLimitPerMinute { get; set; } = 30;
    public int MaxAttempts { get; set; } = 3;
    /// <summary>Only users who opted in for this channel receive messages (always true for SMS).</summary>
    public bool RequireOptIn { get; set; } = true;
    /// <summary>Only notifications whose title contains one of these words are sent (empty = all).</summary>
    public List<string> Keywords { get; set; } = [];
    public Guid? TemplateId { get; set; }
    public string ConfigFingerprint { get; set; } = "";
    public DateTimeOffset? LastTestAt { get; set; }
    public bool? LastTestOk { get; set; }
    public string? LastTestMessage { get; set; }
    public string? LastTestFingerprint { get; set; }
    public Guid? LastTestById { get; set; }
    public Guid? ProductionApprovedById { get; set; }
    public DateTimeOffset? ProductionApprovedAt { get; set; }
    /// <summary>Highest notification id already considered for delivery.</summary>
    public long LastNotificationId { get; set; }
    public string AuditLabel => $"messaging provider {Name}";
}

/// <summary>Message text with placeholders {title}, {body}, {link}, {app}. SMS bodies are cut to MaxLength.</summary>
public class MessageTemplate : Entity, IAudited
{
    public string Name { get; set; } = "";
    public MessagingChannel Channel { get; set; }
    public string Text { get; set; } = "{title}";
    public string? PushTitle { get; set; }
    public int MaxLength { get; set; } = 320;
    public string AuditLabel => $"message template {Name}";
}

/// <summary>A user's own opt-in for a channel: a phone number (E.164) or a device push token. Opt-out keeps the record.</summary>
public class MessagingSubscription : Entity, IAudited
{
    public Guid UserId { get; set; }
    public MessagingChannel Channel { get; set; }
    public string Address { get; set; } = "";
    public string? Label { get; set; }
    public DateTimeOffset OptedInAt { get; set; }
    public DateTimeOffset? OptedOutAt { get; set; }
    public string AuditLabel => $"{Channel} opt-in {Label ?? MaskedAddress}";
    public string MaskedAddress => Address.Length <= 4 ? "****" : new string('*', Math.Min(8, Address.Length - 4)) + Address[^4..];
}

public enum DeliveryState { Queued = 0, Sent = 1, Delivered = 2, Failed = 3, Skipped = 4 }

/// <summary>One message to one address (append-style log; status and attempts are updated in place).</summary>
public class MessageDelivery
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ProviderId { get; set; }
    public long? NotificationId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? SubscriptionId { get; set; }
    public string MaskedAddress { get; set; } = "";
    public string Text { get; set; } = "";
    public DeliveryState State { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? FailureReason { get; set; }
    public string? ProviderMessageId { get; set; }
    public bool IsTest { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
}
