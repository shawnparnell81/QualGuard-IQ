# Setting up ERP and MES connections

This guide covers the Integrations module (Admin → Integrations). The vendor research behind each adapter is in `docs/integrations/<vendor>.md`. That folder is reference material and is not edited here.

## How a connection works

- **New connections are off.** A connection starts disabled and read-only. Nothing syncs until an administrator with `integrations.admin` tests it and turns it on.
- **Secrets can only be written.** Client secrets, private keys, API keys and passwords are encrypted at rest. The API and UI only show whether a secret is set and when. To rotate one, use **Secrets → Replace**.
- **Changes go to a queue first.** A sync never edits a record directly. Each new or changed external row becomes a *pending change* (Create or Update, with before and after values). Someone with `integrations.approve` submits it, and it goes through the normal approval route ("ErpChange"). An approval applies the change and writes an audit entry (who, what, when, description).
- **Duplicates are skipped.** Rows are matched by external id. If there is no external id link yet, they are matched by business key (part number, supplier code, customer code and so on). A row whose content hash has not changed since the last run is counted as *unchanged*.
- **Delta reads.** Where the vendor documents a modified-date filter, the adapter reads from the last watermark minus a 2-minute overlap. Otherwise it reads in full and relies on the hash check.
- **Schedule.** The worker runs `erp-sync` every minute. Each mapping has its own cron schedule, so a connection only syncs when its mapping is due.
- **Throttling.** HTTP 429 and 503 honour `Retry-After`, with capped exponential back-off. The connection's *max concurrency* setting limits parallel calls.
- **Revision changes.** When a part's revision changes through a sync, a part revision is recorded. The connection owner also gets a "FAI required" notification.
- **Read only.** No adapter writes to the ERP. Outbound mappings are refused.

## Common steps

1. Create a dedicated integration user in the ERP with read-only roles.
2. In QualGuard IQ, go to **Integrations → New connection**. Pick the vendor and edition, then enter the base URL, client id and options. Save.
3. Open **Secrets** and enter the secret the vendor issued.
4. Click **Test**. This signs in and reads one small page. If it fails, the error shows the vendor's message.
5. Review the field maps on the **Mappings** tab. Defaults come from the adapter, and items flagged UNVERIFIED are shown next to them.
6. Run each mapping once by hand. Check the **Change queue**, approve a sample, and then turn the connection on.

## Per vendor

| Vendor | Edition(s) | Auth | Key options |
|---|---|---|---|
| Oracle NetSuite | SuiteQL | OAuth 2.0 client credentials (M2M) with a JWT assertion signed **PS256** (RS256 is rejected). Upload the certificate under *OAuth 2.0 Client Credentials (M2M) Setup*. The private key goes in Secrets. | Account = NetSuite account id; options `certificateId`, `scope` (default `rest_webservices`), `algorithm` |
| SAP | S4Cloud, S4OnPrem, BusinessOne | S/4: communication user (Username + Secret, Basic) or OAuth (`auth=oauth`, `tokenUrl`). B1: Service Layer `/Login` session cookies, re-login on expiry. | Company = B1 CompanyDB; options `poService` (S/4), `version` (B1, default v1) |
| Oracle Fusion SCM | FusionScm | OAuth client credentials through IDCS (`tokenUrl`, `scope`), or Basic (`auth=basic`) | options `auth`, `tokenUrl`, `scope` |
| Microsoft Dynamics 365 | BusinessCentral, FinanceSupplyChain | Entra ID client credentials (ClientId + Secret) | Account = Entra tenant id; Company = BC company id; options `authority`, `environment` (BC, default production), `dataAreaId` (F&SCM) |
| Acumatica | ContractRest | OAuth 2.0 (connected application) | options `defaultVersion`, `manufacturingVersion` |
| Infor | M3, Mongoose | ION API OAuth from the `.ionapi` file values (`grant`, default client credentials); Mongoose also supports `mode=direct` | Company = Mongoose configuration; options `cono` (M3), `ionProductPath` (Mongoose, default CSI) |
| Epicor Kinetic | KineticRestV2 | API key plus Basic (Username + Secret), or IdP token (`auth=idp`, `tokenUrl`) | Company = Epicor company; options `baq.<Entity>` for BAQ reads |
| Plex Smart Manufacturing | Connect | `X-Plex-Connect-Api-Key` (Secret) and `X-Plex-Connect-Tenant-Id` | Account = tenant id; options `query.<Entity>`, `idField` |
| DELMIAworks (IQMS) | WebApi | Session login (`loginPath`, token header `tokenHeader`) | Username + Secret (password); Company = database alias; options `tokenMinutes` (default 20), `path.<Entity>`, `idField.<Entity>` |
| Generic REST | Rest | `auth` = bearer, basic or apikey (`apiKeyHeader`) | options `url.<Entity>`, `items.<Entity>`, `id.<Entity>`, `modified.<Entity>`, `next.<Entity>`, `since.<Entity>`, `testEntity` |
| CSV exchange | Csv | none (upload) | `id.<Entity>` |

## Live validation (connection → Live validation tab)

No connector in this build has been validated against a live customer system. Every adapter is covered by fixture tests
(recorded vendor responses), which prove the adapter's wire handling, not that it works against a given tenant. Live
validation is recorded per connection:

| Gate | How it is recorded | Required to enable |
|---|---|---|
| Fixture tested | Static: the automated fixture tests that cover the adapter | — |
| Connection | Test connection reached the host | Yes (not for CSV) |
| Authentication | Test connection obtained a token or session | Yes (not for CSV) |
| Read | Test connection read one page (CSV: first accepted upload) | Yes (not for CSV) |
| Write | Not applicable: all adapters are read-only | — |
| Production approval | A second person with *integrations.approve* (not the tester) approves | Production environment only |

- Each result records time, who, environment (Sandbox, Test, Production) and notes, and counts only for the
  configuration it was recorded against. Changing the URL, edition, account, company, client, user, options, secrets or
  environment disables the connection and puts every gate back to "not run"; the history keeps the old results.
- Every UNVERIFIED item listed below is shown on the tab as Unverified until a person records Confirmed or Rejected with
  notes, which is only allowed after a passing read test for the current configuration. The following key items block
  production approval until confirmed: NetSuite transaction codes; SAP entity and field names; Oracle Fusion pod host;
  Plex paths, fields/paging and Customer Id header; DELMIAworks contract and login.
- The tab lists supported and unsupported entities and a guided checklist for each system (SAP, Oracle, Epicor, Infor,
  Microsoft Dynamics, NetSuite, Acumatica, Plex, DELMIAworks, Generic REST, CSV).
- Connections that were enabled before this change stay enabled, but their gates show "not run" until tested again.

## UNVERIFIED items (confirm with each customer before go-live)

The live list is on each connection's **Setup notes** panel, and on `GET /api/integrations/adapters`. Summary:

**NetSuite**
- `lastmodifieddate` on item, customer, vendor and employee.
- REST ids for lot/serial items and item revisions.
- SuiteQL transaction type codes (`PurchOrd`, `WorkOrd`) and column names.
- The `items`/`hasMore` envelope.
- Governance limits (needs an administrator login).
- M2M certificate mapping is cleared by a sandbox refresh.

**SAP S/4HANA**
- Communication scenarios other than 0009 and 0008.
- Entity and field names (check each `$metadata`).
- The base URL pattern.
- Delta beyond `A_Product`.
- The OData V2 error shape.
- Rate limits.
- PO V2/V4 service choice.

**SAP Business One**
- Entity and field names come from common usage (confirm in `$metadata`).
- No delta filter.
- No vendor sandbox.

**Oracle Fusion**
- Pod host.
- Integration role codes.
- Row field names.
- The `itemsV2` revisions child.
- Customers are out of scope (CX pillar).
- The delta `q` filter.
- Max page size.
- Basic client auth at the token endpoint.
- Error format, limits and sandbox.

**Dynamics 365 Business Central**
- The standard API has no manufacturing or item-tracking data. A companion AL API extension is needed and is not shipped yet.
- Purchase order fields.
- No receipts delta.

**Dynamics 365 Finance & SCM**
- Only `ReleasedProductsV2` is confirmed.
- Delta needs change tracking.
- Error format and sandbox steps.

**Acumatica**
- Entity and field lists other than the cited examples.
- The `{value}` wrapper on every entity.
- Endpoint versions.
- Error body and sandbox.

**Infor M3**
- The ION execute path.
- MI programs other than MMS200MI.
- The `results[0].records` shape.
- `maxrecs` and LMDT.
- Limits and sandbox.

**Infor Mongoose**
- IDO names and properties.
- The Items/Bookmark/MoreRowsExist response.
- `RecordDate` delta.
- The ION product path.

**Epicor**
- The `x-api-key` header and IdP flow.
- Service names other than CustomerSvc.
- Delta (a BAQ is recommended).
- Error body, limits and Pilot.

**Plex**
- Every collection path.
- Response fields and paging.
- The Customer Id header name.
- The rate limit (community figure of 200/min; the default is 100/min).
- Portal onboarding steps.

**DELMIAworks**
- The whole contract is in the licensed SDK help. Only the Customers path is cited.
- The login and token header.
- Paging, delta, limits, errors and sandbox.
- The fallback is CSV exchange or read-only database views.

## MES and shop floor

- **Event intake.** MES systems post events to `POST /api/mes/events` with a unique `eventId`. A repeated id returns `duplicate: true` and is not applied again.
- **Event kinds.** WorkOrder, Production, Downtime, Inspection, Parameter and Nonconformance.
- **Authentication.** Register each MES sender under Governance › API & channels › MES devices. A device has a unique client id (`qgm_…`), a 256-bit secret shown once at registration or rotation (stored only as a SHA-256 hash, never retrievable), one or more permission scopes (for example `production.edit`, plus `ncr.edit` for Nonconformance events), one or more sites, an endpoint list (default `POST /api/mes/events`), a rate limit and an expiry date within two years. It sends `X-Api-Key: {clientId}.{secret}` or exchanges the id and secret at `POST /api/oauth/token` (client credentials). The device acts as its service user, narrowed to its scopes and sites: a scope never adds a permission the service user lacks. Each event records the sending client and `AuthMethod` (MesDevice, ApiClient or UserToken).
- **Rotation, revocation and failures.** Rotate with an optional grace period (up to 7 days) during which the old secret still works. Disable is reversible; revoke is permanent and stops the key and any issued access token at once. Unknown, wrong-secret, expired, disabled and revoked attempts are written to the security log and to the device's last-failure fields; requests outside the scopes, sites or endpoints (403) and rate-limit refusals (429, logged at most once a minute) are logged too.
- **Migration from user tokens.** Until `Mes:RequireDeviceClients` is `true`, user sessions and user bearer tokens with `production.edit` may still post events (counted on the MES devices tab as legacy events). Set it to `true` once every sender is registered; user tokens are then refused with 403.
- **SPC.** An inspection entry that names an SPC chart adds a sample. A rule violation raises an NCR automatically and links it to the work order.

## Alerts and outbound endpoints

- **Alert rules.** These live under **Alerts** and need `alerts.admin`. The worker evaluates them every 15 minutes, and a rule raises once per record until the condition clears.
- **Integration points.** SMS gateway, Teams webhook and data-lake endpoints must use https. Their signing secret is write-only. Each POST carries an `X-QualGuard-Signature` HMAC-SHA256 header over the body.
- **Exports.** Data-lake exports run every 10 minutes and write NDJSON to object storage under `exports/<dataset>/`.
