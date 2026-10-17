# Setting up ERP and MES connections

This guide covers the Integrations module (Admin → Integrations). The vendor research behind each adapter is in `docs/integrations/<vendor>.md`. That folder is reference material and is not edited here.

## How a connection works

- **New connections are off.** A connection starts disabled and read-only. Nothing syncs until an administrator with `integrations.admin` tests it and turns it on.
- **Secrets can only be written.** Client secrets, private keys, API keys and passwords are encrypted at rest. The API and UI only show whether a secret is set and when. To rotate one, use **Secrets → Replace**.
- **Changes go to a queue first.** A sync never edits a record directly. Each new or changed external row becomes a *pending change* (Create or Update, with before and after values). Someone with `integrations.approve` submits it, and it goes through the normal approval route ("ErpChange"). An approval applies the change and writes an audit entry (who, what, when, description).
- **Duplicates are skipped.** Rows are matched by external id. If there is no external id link yet, they are matched by business key (part number, supplier code, customer code and so on). A row whose content hash has not changed since the last run is counted as *unchanged*.
- **Delta reads.** Where the vendor documents a modified-date filter, the adapter reads from the last watermark minus a 2-minute overlap. Otherwise it reads in full and relies on the hash check.
- **Schedule.** The worker runs `erp-sync` every minute. Each mapping has its own interval in minutes, so a connection only syncs when its mapping is due.
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
| Oracle NetSuite | SuiteQL | OAuth 2.0 client credentials (M2M) with a JWT assertion signed **PS256** (RS256 is rejected). Upload the certificate under *OAuth 2.0 Client Credentials (M2M) Setup*. | `accountId`, `certificateId`, `scope` (default `rest_webservices`), `algorithm` |
| SAP | S4Cloud, S4OnPrem, BusinessOne | S/4: a communication user with Basic auth, or OAuth (`auth=oauth`, `tokenUrl`). B1: Service Layer `/Login` session cookies, which re-login when the session expires. | `poService`, `version` (B1, default v1), `companyDb` |
| Oracle Fusion SCM | FusionScm | OAuth client credentials through IDCS (`tokenUrl`, `scope`), or Basic (`auth=basic`) | `tokenUrl`, `scope` |
| Microsoft Dynamics 365 | BusinessCentral, FinanceSupplyChain | Entra ID client credentials (`authority`, tenant) | `tenantId`, `environment` (BC), `companyId` (BC), `dataAreaId` (F&SCM) |
| Acumatica | ContractRest | OAuth 2.0 (connected application) | `defaultVersion`, `manufacturingVersion`, `company` |
| Infor | M3, Mongoose | ION API OAuth. The `.ionapi` values are `ti`, `iu`, `pu`, `ot`, `saak`/`sask`. `grant` defaults to client credentials. Mongoose can use `mode=direct`. | `cono` (M3), `ionProductPath`, `config` (Mongoose) |
| Epicor Kinetic | KineticRestV2 | API key header plus Basic auth, or an IdP token (`auth=idp`, `tokenUrl`) | `company`, `baq.<Entity>` for BAQ reads |
| Plex Smart Manufacturing | Connect | `X-Plex-Connect-Api-Key` (plus secret) and `X-Plex-Connect-Tenant-Id` | `tenantId`, `query.<Entity>`, `idField` |
| DELMIAworks (IQMS) | WebApi | Session login (`loginPath`, `tokenHeader`) | `path.<Entity>`, `idField.<Entity>` |
| Generic REST | Rest | bearer, basic or API key | `url.<Entity>`, `items.<Entity>`, `id.<Entity>`, `modified.<Entity>`, `next.<Entity>`, `since.<Entity>` |
| CSV exchange | Csv | none (upload) | `id.<Entity>` |

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
- **Authentication.** The caller is a QualGuard IQ user with `production.edit`. Use a dedicated service account. API keys for machines are not implemented yet.
- **SPC.** An inspection entry that names an SPC chart adds a sample. A rule violation raises an NCR automatically and links it to the work order.

## Alerts and outbound endpoints

- **Alert rules.** These live under **Alerts** and need `alerts.admin`. The worker evaluates them every 15 minutes, and a rule raises once per record until the condition clears.
- **Integration points.** SMS gateway, Teams webhook and data-lake endpoints must use https. Their signing secret is write-only. Each POST carries an `X-QualGuard-Signature` HMAC-SHA256 header over the body.
- **Exports.** Data-lake exports run every 10 minutes and write NDJSON to object storage under `exports/<dataset>/`.
