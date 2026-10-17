using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Quality;

/// <summary>
/// Which adapter talks to the carrier. Fixture is a built-in simulated carrier for tests and training (sandbox only). GenericRest posts the
/// documented QualGuard carrier JSON contract to a customer-run endpoint (integration middleware). Ups, FedEx and Dhl are provider slots:
/// they record the account and test authentication against the published OAuth/credential endpoint, but shipment operations are not
/// implemented until they are built and certified against the carrier's sandbox. Ltl uses the REST contract with freight fields.
/// </summary>
public enum CarrierProvider { Fixture = 0, GenericRest = 1, Ups = 2, FedEx = 3, Dhl = 4, Ltl = 5 }
public enum CarrierEnvironment { Sandbox = 0, Production = 1 }
/// <summary>Unverified until a connection test passes for the current configuration; a configuration change makes it Unverified again.</summary>
public enum CarrierVerification { Unverified = 0, Verified = 1, Failed = 2 }
public enum CarrierOperation { ConnectionTest = 0, AddressValidation = 1, Rate = 2, CreateShipment = 3, Label = 4, Cancel = 5, Track = 6 }
public enum CarrierCallResult { Succeeded = 0, Failed = 1, NotSupported = 2 }
public enum CarrierLabelStatus { Active = 0, Voided = 1, VoidFailed = 2 }

/// <summary>
/// A carrier account at one site: provider, environment (sandbox by default), endpoint, account and encrypted credentials, the shipper
/// address the account is registered to, and the activation state. Disabled by default; enabling needs a passing connection test for the
/// current configuration, and a production connection also needs approval by a second person.
/// </summary>
public class CarrierConnection : Entity, IAudited, ISiteScoped
{
    public Guid CarrierId { get; set; }
    public string Name { get; set; } = "";
    public CarrierProvider Provider { get; set; }
    public CarrierEnvironment Environment { get; set; }
    public string? BaseUrl { get; set; }
    public string? AccountNumber { get; set; }
    public string? ClientId { get; set; }
    /// <summary>Client secret, API key or bearer token (encrypted). Never returned by the API.</summary>
    public string? SecretCiphertext { get; set; }
    /// <summary>Adapter options (JSON): REST paths, auth style, default service, label format.</summary>
    public string OptionsJson { get; set; } = "{}";
    public string? ShipperName { get; set; }
    public string? ShipperLine1 { get; set; }
    public string? ShipperLine2 { get; set; }
    public string? ShipperCity { get; set; }
    public string? ShipperRegion { get; set; }
    public string? ShipperPostalCode { get; set; }
    public string? ShipperCountry { get; set; }
    public string? ShipperPhone { get; set; }
    public bool Enabled { get; set; }
    public CarrierVerification Verification { get; set; }
    /// <summary>Configuration fingerprint the verification applies to.</summary>
    public string? VerifiedFingerprint { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public Guid? VerifiedById { get; set; }
    public DateTimeOffset? LastTestAt { get; set; }
    public string? LastTestMessage { get; set; }
    /// <summary>Production approval (second person, integrations.approve), for the fingerprint it was given on.</summary>
    public string? ApprovedFingerprint { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid? ApprovedById { get; set; }
    public string? ApprovalNotes { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"carrier connection {Name}";
}

/// <summary>Every call to a carrier adapter (append-only): operation, attempt, result, HTTP status, error, duration, the shipment and who asked.</summary>
public class CarrierRequestLog : Entity
{
    public Guid ConnectionId { get; set; }
    public Guid? ShipmentId { get; set; }
    public CarrierOperation Operation { get; set; }
    public CarrierEnvironment Environment { get; set; }
    public int Attempt { get; set; }
    public CarrierCallResult Result { get; set; }
    public int? HttpStatus { get; set; }
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
    public int DurationMs { get; set; }
    public string? IdempotencyKey { get; set; }
    public Guid? ById { get; set; }
    public DateTimeOffset At { get; set; }
    public string? ConfigFingerprint { get; set; }
}

/// <summary>A label (and tracking number) created at the carrier for a shipment package. The label image is kept as returned.</summary>
public class CarrierLabel : Entity, IAudited
{
    public Guid ShipmentId { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid? ContainerId { get; set; }
    public int PackageNumber { get; set; }
    public string CarrierShipmentId { get; set; } = "";
    public string TrackingNumber { get; set; } = "";
    public string ServiceCode { get; set; } = "";
    public string Format { get; set; } = "PDF";
    public byte[] Data { get; set; } = [];
    public decimal? Charge { get; set; }
    public string? Currency { get; set; }
    public CarrierLabelStatus Status { get; set; }
    public DateTimeOffset CreatedOn { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public string? VoidMessage { get; set; }
    public string AuditLabel => $"carrier label {TrackingNumber}";
}

/// <summary>A tracking event reported by the carrier (deduplicated on shipment, tracking number, time and code).</summary>
public class CarrierTrackingEvent : Entity
{
    public Guid ShipmentId { get; set; }
    public string TrackingNumber { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Location { get; set; }
    public bool Delivered { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
