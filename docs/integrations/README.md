# ERP integration research and connector framework design

Per-vendor files: [sap](sap.md) · [oracle](oracle.md) · [epicor](epicor.md) · [infor](infor.md) · [dynamics365](dynamics365.md) · [netsuite](netsuite.md) · [acumatica](acumatica.md) · [plex](plex.md) · [iqms-delmiaworks](iqms-delmiaworks.md)

Rule: every claim cites a vendor URL. Anything marked **UNVERIFIED** has to be confirmed against official docs (or the customer's tenant metadata) before an adapter relies on it. No connector code exists yet.

## Comparison matrix
| ERP | M2M auth | Main API | Delta sync | Outbound events | Public docs | Sandbox |
|---|---|---|---|---|---|---|
| NetSuite | OAuth 2.0 client credentials, cert-signed JWT | REST record v1 + SuiteQL | SuiteQL `lastmodifieddate` | No native (SuiteScript only, UNVERIFIED) | Yes | Sandbox/RP, M2M setup per account |
| SAP S/4HANA Cloud | Comm. arrangement user (Basic / OAuth / X.509) | OData V2/V4 A2X | `LastChangeDateTime` (UNVERIFIED) | Business events via Event Mesh (UNVERIFIED) | Yes (api.sap.com) | Hub sandbox + Q tenant |
| SAP S/4 on-prem | Gateway technical user | Same OData | same | same / none | Yes | Customer QA |
| SAP Business One | Session login (B1SESSION cookie, 30-min timeout) | Service Layer OData (page 20, `odata.maxpagesize`) | `UpdateDate` (UNVERIFIED) | Webhooks/EventSubscription (new in 2026 guide) | Yes | Test company DB |
| Oracle Fusion SCM | IAM confidential app, client creds/JWT | fscmRestApi (page 25 default) | `q=LastUpdateDate>` (UNVERIFIED) | Via OIC (UNVERIFIED) | Yes | Test pod |
| Epicor Kinetic | Epicor IdP client creds + `x-api-key` (UNVERIFIED written) | REST v2 OData v4 / BAQ | BAQ filter (UNVERIFIED) | None native | Partly (EpicWeb login) | Pilot env |
| Infor CloudSuite | ION Backend Service `.ionapi` (client creds or resource owner) | ION gateway → M3 MI / Mongoose IDO | change-date fields (UNVERIFIED) | ION BODs | Yes | TST tenant |
| D365 Business Central | Entra client credentials | API v2.0 (no mfg entities; needs custom API ext) | `lastModifiedDateTime` | Webhooks (3-day subs) | Yes | Yes |
| D365 F&SCM | Entra client credentials | OData `/data` | SQL change tracking per entity | Business events (HTTPS/Service Bus/Event Grid) | Yes | Tier-2 |
| Acumatica | OAuth (ROPC for server, no client creds) | Contract REST + MANUFACTURING endpoint | `LastModifiedDateTime` | Push notifications | Yes | Partner tenant |
| Plex | Static `X-Plex-Connect-Api-Key` | Plex Connect REST | UNVERIFIED | None documented | Portal (login) | test.connect.plex.com |
| DELMIAworks | Session login → token | WebAPI REST / GraphQL (licensed) | UNVERIFIED | UNVERIFIED | No (SDK only) | Customer test DB |

## Proposed connector framework (design only)
Builds on existing QMS services (auth, audit engine, approval engine, notifications, Hangfire). No new workflow engine.

1. **Common adapter interface** `IErpConnector`: `SystemKind`, `TestConnectionAsync`, `GetCapabilities()` (entity types, delta support, write support, events), `ReadPageAsync(entityType, DeltaCursor, ct)` → `ErpPage<ExternalRecord>` (external id, version/ETag, lastModified, raw JSON, next cursor), and an optional `WriteAsync(ExternalChange)` gated by capability. A canonical entity set (Item, ItemRevision, Bom, Customer, Supplier, WorkCenter, Employee, ProductionOrder, Routing, PurchaseOrder, Receipt, Lot, Serial) keeps vendors out of domain code.
2. **Per-vendor adapters**, one project each: NetSuite (SuiteQL), S/4 OData, B1 Service Layer, Fusion REST, Kinetic v2, Infor ION (M3 + IDO sub-adapters), BC, F&SCM, Acumatica, Plex, DELMIAworks. Plus generic REST/OData/GraphQL, SOAP, Database (read-only views) and CSV adapters for custom ERPs. Shared building blocks: `ITokenProvider` implementations (client credentials, JWT-assertion cert, ROPC, session cookie, API key), paging strategies, and a Polly retry policy that honours 429/`Retry-After` plus per-connection concurrency caps.
3. **Credential storage**: a `ErpConnection` record holds non-secret config. Secrets (client secrets, private keys/certs, API keys) are encrypted with ASP.NET Core Data Protection (key ring persisted, optional Azure Key Vault provider), never returned by the API, write-only in the UI, rotation supported, and access is audited.
4. **Hangfire scheduling**: one recurring job per connection+entity (cron in config) that runs `SyncJob(connectionId, entityType)`, with `[DisableConcurrentExecution]` per key. Delta cursor (watermark + overlap window) persists in `ErpSyncCursor` and only advances after a page commits. Webhook/push receivers (BC, Acumatica, ION) only enqueue a delta job, so polling stays the source of truth.
5. **Idempotent upsert**: unique index `(ErpSystemId, ExternalEntityType, ExternalId)` on `ErpExternalLink` → local entity id. Store a hash of the mapped payload and skip unchanged rows. Use rowversion concurrency, so reprocessing a page is a no-op.
6. **Approval-required sync queue**: per mapping, choose mode `Auto`, `ApprovalRequired`, or `Disabled` (and direction `Inbound`/`Outbound`/`TwoWay`). Changes land in `ErpSyncQueueItem` (before/after diff, validation results) and go through the existing approval engine. **All outbound writes default to ApprovalRequired, and adapters ship read-only unless enabled per connection.**
7. **Mapping and validation config**: versioned JSON per connection+entity with field map (source path → canonical field), transforms (unit, date, enum maps), required fields, regex/range rules, and conflict policy (ERP-wins / QMS-wins / manual). Invalid records go to the queue as `Rejected` with reasons. They never get partially applied.
8. **Audit entries**: through the existing audit engine, log connection create/change, credential change, every run (counts, duration, errors), every applied create/update with external id and diff, and every approval decision. Raw payloads are retained per retention setting.
9. **Testing**: recorded-fixture contract tests per adapter built from documented sample payloads. A live "Test connection" checks auth and one read. Each UNVERIFIED item is a checklist entry to close against the customer's sandbox.

## Verification pass 2 (2026-10-16)
Per-vendor files were rechecked against official sources only. What's still open, and why, is in each file. Main corrections:
- SAP Business One has native webhooks (EventSubscription) as of its 2026 guide.
- NetSuite JWT `alg` must be PS256/PS384/PS512/ES256/ES384/ES512 (no RS256), and `governanceLimits` needs an admin login.
- The Business Central standard API has no manufacturing or lot entities (confirmed from the official resource list), and limits are 5 concurrent / about 6000 requests per 5 min per user.
