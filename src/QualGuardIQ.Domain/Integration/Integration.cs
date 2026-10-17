using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Integration;

/// <summary>ERP products with an adapter. Editions select the vendor API family inside one adapter (see docs/integrations).</summary>
public enum ErpSystem { NetSuite = 0, Sap = 1, Oracle = 2, Dynamics = 3, Acumatica = 4, Infor = 5, Epicor = 6, Plex = 7, DelmiaWorks = 8, CustomRest = 20, CsvExchange = 21 }

/// <summary>Canonical entities exchanged with ERP and MES systems; vendor names never reach the domain.</summary>
public enum ErpEntity { Item = 0, ItemRevision = 1, Bom = 2, Customer = 3, Supplier = 4, WorkCenter = 5, Employee = 6, ProductionOrder = 7, Routing = 8, PurchaseOrder = 9, Receipt = 10, Lot = 11, Serial = 12, InventoryBalance = 13 }

public enum SyncDirection { Inbound = 0, Outbound = 1, TwoWay = 2 }
/// <summary>Auto applies valid inbound changes; ApprovalRequired routes each change through the approval engine.</summary>
public enum SyncMode { Disabled = 0, Auto = 1, ApprovalRequired = 2 }
public enum ConflictPolicy { ErpWins = 0, QmsWins = 1, Manual = 2 }

/// <summary>
/// One configured connection to an ERP or MES. Non-secret settings only: the secret (client secret, private key,
/// API key or password) is encrypted with ASP.NET Core Data Protection, is never returned by the API, and every change is audited.
/// New connections are disabled and read-only.
/// </summary>
public class ErpConnection : Entity, IAudited, ISiteScoped
{
    public string Name { get; set; } = "";
    public ErpSystem System { get; set; }
    /// <summary>Adapter variant, e.g. S4HanaCloud / S4HanaOnPrem / BusinessOne, BusinessCentral / FinanceSupplyChain, M3 / Mongoose.</summary>
    public string Edition { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    /// <summary>NetSuite account id, BC/Entra tenant id, ION tenant (ti), Plex tenant id.</summary>
    public string? Account { get; set; }
    /// <summary>BC environment + company, Epicor company, SAP B1 CompanyDB, Mongoose config, Acumatica endpoint version.</summary>
    public string? Company { get; set; }
    public string? ClientId { get; set; }
    /// <summary>Username for ROPC/session logins (integration user, never a person).</summary>
    public string? Username { get; set; }
    /// <summary>JSON of adapter options that are not secret (token URL, certificate id, scope, API version, page size).</summary>
    public string Options { get; set; } = "{}";
    public string? EncryptedSecret { get; set; }
    public DateTimeOffset? SecretSetAt { get; set; }
    /// <summary>Second secret where a vendor needs two (Epicor x-api-key with a user password; ION service account key).</summary>
    public string? EncryptedSecondarySecret { get; set; }
    public bool Enabled { get; set; }
    /// <summary>Outbound writes are refused unless this is set and the adapter supports writes; even then they go through approval.</summary>
    public bool AllowWrites { get; set; }
    public int MaxConcurrency { get; set; } = 1;
    public int RequestsPerMinute { get; set; } = 60;
    /// <summary>When set, this ERP is the inventory system of record for the site and local inventory runs in read-only mirror mode.</summary>
    public bool InventorySystemOfRecord { get; set; }
    /// <summary>Generated from the adapter's unverified-items list plus administrator notes.</summary>
    public string SetupNotes { get; set; } = "";
    public string? AdminNotes { get; set; }
    public DateTimeOffset? LastTestAt { get; set; }
    public bool? LastTestOk { get; set; }
    public string? LastTestMessage { get; set; }
    /// <summary>Which customer environment the connection points at. Production connections need a production approval before they can be enabled.</summary>
    public ErpEnvironment Environment { get; set; } = ErpEnvironment.Sandbox;
    public Guid OwnerId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"ERP connection {Name}";
}

/// <summary>Per connection and entity: direction, mode, schedule, field map, validation and conflict policy.</summary>
public class ErpEntityMapping : Entity, IAudited
{
    public Guid ConnectionId { get; set; }
    public ErpEntity Entity { get; set; }
    public SyncDirection Direction { get; set; }
    public SyncMode Mode { get; set; } = SyncMode.ApprovalRequired;
    public ConflictPolicy Conflict { get; set; } = ConflictPolicy.Manual;
    /// <summary>Cron expression (UTC) for the scheduled sync.</summary>
    public string Cron { get; set; } = "0 * * * *";
    public DateTimeOffset? NextRunAt { get; set; }
    /// <summary>JSON object canonical field -> vendor field path (overrides the adapter default).</summary>
    public string FieldMap { get; set; } = "{}";
    /// <summary>JSON array of rules: {"field":"number","required":true,"pattern":"^[A-Z0-9-]+$","max":50}.</summary>
    public string ValidationRules { get; set; } = "[]";
    public int Version { get; set; } = 1;
    public string AuditLabel => $"ERP mapping {Entity}";
}

/// <summary>Delta watermark per connection and entity. Advances only after a page commits.</summary>
public class ErpSyncCursor : Entity
{
    public Guid ConnectionId { get; set; }
    public ErpEntity Entity { get; set; }
    public DateTimeOffset? Watermark { get; set; }
    public string? PageToken { get; set; }
}

/// <summary>External id -> local record. Unique per connection, entity and external id, so re-sync updates instead of duplicating.</summary>
public class ErpExternalLink : Entity
{
    public Guid ConnectionId { get; set; }
    public ErpEntity Entity { get; set; }
    public string ExternalId { get; set; } = "";
    public string LocalType { get; set; } = "";
    public Guid LocalId { get; set; }
    public string PayloadHash { get; set; } = "";
    public string? ExternalVersion { get; set; }
    public DateTimeOffset LastSyncedAt { get; set; }
}

public enum SyncRunStatus { Running = 0, Succeeded = 1, Failed = 2, PartiallySucceeded = 3 }

public class ErpSyncRun : Entity, IAudited
{
    public Guid ConnectionId { get; set; }
    public ErpEntity Entity { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public SyncRunStatus Status { get; set; }
    public int Read { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int Queued { get; set; }
    public int Rejected { get; set; }
    public int Requests { get; set; }
    public string? Error { get; set; }
    public string Trigger { get; set; } = "Schedule";
    public string AuditLabel => $"ERP sync {Entity} {StartedAt:yyyy-MM-dd HH:mm}";
}

public enum ChangeQueueStatus { Pending = 0, PendingApproval = 1, Applied = 2, Rejected = 3, Failed = 4, Superseded = 5 }

/// <summary>An inbound (or outbound) change waiting for review: before/after, validation results, approval.</summary>
public class ErpChange : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public Guid ConnectionId { get; set; }
    public ErpEntity Entity { get; set; }
    public SyncDirection Direction { get; set; }
    public string ExternalId { get; set; } = "";
    public string Action { get; set; } = "Create";
    public string? Before { get; set; }
    public string After { get; set; } = "{}";
    public string PayloadHash { get; set; } = "";
    public string? ValidationErrors { get; set; }
    public ChangeQueueStatus Status { get; set; }
    public Guid? RunId { get; set; }
    public string? LocalType { get; set; }
    public Guid? LocalId { get; set; }
    public string? DecisionNote { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"ERP change {Number}";
}

/// <summary>Inbound MES event, kept for idempotency (unique per source and event id) and replay.</summary>
public class MesEvent : Entity
{
    public string Source { get; set; } = "";
    public string EventId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? Error { get; set; }
    public Guid SiteId { get; set; }
    /// <summary>API client id of the sender (null when sent with a user session or user bearer token).</summary>
    public string? ClientId { get; set; }
    /// <summary>MesDevice, ApiClient or UserToken.</summary>
    public string AuthMethod { get; set; } = "UserToken";
}

public enum ErpEnvironment { Sandbox = 0, Test = 1, Production = 2 }
public enum ErpValidationGate { Connection = 1, Authentication = 2, Read = 3, Write = 4, ProductionApproval = 5, Assumption = 6 }
public enum ErpValidationResult { Passed = 0, Failed = 1, NotApplicable = 2, Confirmed = 3, Rejected = 4, Revoked = 5 }

/// <summary>
/// One validation result for an ERP connection (append-only): a gate result from a connection test, a person's confirmation of an
/// unverified vendor assumption, or a production approval. Results count only for the configuration fingerprint they were recorded
/// against, so changing the URL, edition, options, secrets or environment puts every gate back to "not run".
/// </summary>
public class ErpValidationEvent : Entity, IAudited, IAppendOnly
{
    public Guid ConnectionId { get; set; }
    public ErpValidationGate Gate { get; set; }
    public ErpValidationResult Result { get; set; }
    /// <summary>Assumption topic (Gate = Assumption).</summary>
    public string? Topic { get; set; }
    public ErpEnvironment Environment { get; set; }
    public string ConfigFingerprint { get; set; } = "";
    public Guid? ById { get; set; }
    public string ByName { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public string? Notes { get; set; }
    public string AuditLabel => $"ERP validation {Gate} {Result}";
}
