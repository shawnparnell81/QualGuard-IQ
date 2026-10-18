namespace QualGuardIQ.Domain.Platform;

/// <summary>
/// A state-changing API request sent with an Idempotency-Key header and the response it produced. A retry with the same key and the same
/// request gets the stored response back instead of running again; the same key with a different request is refused. Kept for 24 hours.
/// </summary>
public class IdempotencyRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>The signed-in user or API client the key belongs to; keys never cross callers.</summary>
    public string Caller { get; set; } = "";
    public string Key { get; set; } = "";
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    /// <summary>SHA-256 of the method, path, query, content type and body (or body length for uploads).</summary>
    public string Fingerprint { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Null while the first request is still running.</summary>
    public DateTimeOffset? CompletedAt { get; set; }
    public int? StatusCode { get; set; }
    public string? ContentType { get; set; }
    public byte[]? Body { get; set; }
}
