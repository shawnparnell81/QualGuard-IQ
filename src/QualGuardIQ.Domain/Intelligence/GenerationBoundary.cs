using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Intelligence;

/// <summary>
/// RuleBasedOnly: only the built-in deterministic features run (default). ExternalDisabled: providers may be configured and tested but nothing
/// is sent. ApprovedFunctions: enabled functions may use an enabled provider; everything else stays rule-based.
/// </summary>
public enum GenerationMode { RuleBasedOnly = 0, ExternalDisabled = 1, ApprovedFunctions = 2 }
/// <summary>Fixture: deterministic in-process generator for tests and sandboxes (refused in a production host). ExternalHttp: a hosted service over HTTPS. LocalHttp: a model server inside the customer network (HTTP allowed on loopback/private addresses).</summary>
public enum GenerationProviderKind { Fixture = 0, ExternalHttp = 1, LocalHttp = 2 }
public enum GenerationOutcome { Generated = 0, RuleBased = 1, FellBack = 2, Blocked = 3 }
public enum GenerationReview { Pending = 0, Accepted = 1, Discarded = 2, NotRequired = 3 }

/// <summary>Organization-wide switch for generated content (single row). Disabled by default.</summary>
public class GenerationSettings : Entity, IAudited
{
    public GenerationMode Mode { get; set; } = GenerationMode.RuleBasedOnly;
    public int TimeoutSeconds { get; set; } = 20;
    /// <summary>Keep the redacted prompt and the response text with the request log (otherwise only hashes and metadata are kept).</summary>
    public bool StoreContent { get; set; }
    /// <summary>Days the stored prompt/response text is kept before it is purged (metadata stays for MetadataRetentionDays).</summary>
    public int ContentRetentionDays { get; set; } = 30;
    public int MetadataRetentionDays { get; set; } = 365;
    /// <summary>Extra redaction patterns (regular expressions) applied to every prompt in addition to emails and phone numbers.</summary>
    public List<string> RedactionPatterns { get; set; } = [];
    public bool RedactEmails { get; set; } = true;
    public bool RedactPhoneNumbers { get; set; } = true;
    public int MaxSourceRecords { get; set; } = 8;
    public string AuditLabel => "generated content settings";
}

public class GenerationProvider : Entity, IAudited
{
    public string Name { get; set; } = "";
    public GenerationProviderKind Kind { get; set; }
    public string? Endpoint { get; set; }
    public string? Model { get; set; }
    /// <summary>"config:Section:Key" or "stored" (EncryptedSecret). Never sent inside a prompt.</summary>
    public string? SecretReference { get; set; }
    public string? EncryptedSecret { get; set; }
    public bool Enabled { get; set; }
    public string ConfigFingerprint { get; set; } = "";
    public DateTimeOffset? LastTestAt { get; set; }
    public bool? LastTestOk { get; set; }
    public string? LastTestMessage { get; set; }
    public string? LastTestFingerprint { get; set; }
    public Guid? LastTestById { get; set; }
    /// <summary>External providers need approval by a second person with integrations.approve before they can be enabled.</summary>
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid? ApprovedById { get; set; }
    public string AuditLabel => $"generation provider {Name}";
}

/// <summary>Per-function enablement. Functions without a row, or disabled, stay rule-based.</summary>
public class GenerationFunctionPolicy : Entity, IAudited
{
    public string Function { get; set; } = "";
    public bool Enabled { get; set; }
    public Guid? ProviderId { get; set; }
    /// <summary>Records classified Confidential may be sent to this function's provider (Restricted records never are).</summary>
    public bool AllowConfidential { get; set; }
    public string AuditLabel => $"generated content function {Function}";
}

/// <summary>Append-only log of every generation request (including rule-based answers and fallbacks).</summary>
public class GenerationRequestLog : Entity
{
    public string Function { get; set; } = "";
    public Guid UserId { get; set; }
    public string? RecordType { get; set; }
    public Guid? RecordId { get; set; }
    public Guid? ProviderId { get; set; }
    public string? ProviderName { get; set; }
    public string? Model { get; set; }
    public GenerationOutcome Outcome { get; set; }
    public string? FallbackReason { get; set; }
    public int DurationMs { get; set; }
    public int SourceCount { get; set; }
    public int ExcludedSources { get; set; }
    public List<string> SourceRefs { get; set; } = [];
    public int Redactions { get; set; }
    public int InjectionFlags { get; set; }
    public int PromptChars { get; set; }
    public int ResponseChars { get; set; }
    public string? PromptHash { get; set; }
    public string? ResponseHash { get; set; }
    public string? PromptText { get; set; }
    public string? ResponseText { get; set; }
    public DateTimeOffset? ContentPurgedAt { get; set; }
    public GenerationReview Review { get; set; }
    public Guid? ReviewedById { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}
