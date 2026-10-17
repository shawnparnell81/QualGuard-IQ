# Oracle connector research (Fusion Cloud SCM; EBS)

## Fusion Cloud SCM REST
### Auth
- Supported: Basic over HTTPS, SAML bearer, JWT bearer, and OAuth 2.0 tokens from the Fusion IAM (IDCS/OCI IAM) domain. https://docs.oracle.com/en/cloud/saas/applications-common/26c/farca/Quick_Start.html, https://docs.oracle.com/en/cloud/saas/supply-chain-and-manufacturing/26c/fasrp/Use_JWT_Token_for_Authorization.html
- Recommended M2M: create a **Confidential Application** in the Fusion IAM domain, enable Client Credentials (or JWT assertion), and add the Fusion Applications resource scope. Then `POST <IDCS>/oauth2/v1/token` and send the Bearer token. https://docs.oracle.com/en/cloud/saas/applications-common/26d/farca/configure_oauth.html
- The integration user needs Fusion job/duty roles for each resource (e.g. Item read, Work Order view, Procurement). Exact role codes are **UNVERIFIED**, so use the "Required roles" section on each endpoint page.

### Base URL and entities
- `https://<pod>.fa.<dc>.oraclecloud.com/fscmRestApi/resources/11.13.18.05/<resource>` (version string from endpoint pages, e.g. https://docs.oracle.com/en/cloud/saas/supply-chain-and-manufacturing/26c/fasrp/op-workorders-get.html). The host pattern is **UNVERIFIED**.
| QMS | Resource | Source |
|---|---|---|
| Items, revisions | `itemsV2`, child `ItemRevision` | https://docs.oracle.com/en/cloud/saas/supply-chain-and-manufacturing/26c/fasrp/api-product-lifecycle-management-items-version-2.html (child name **UNVERIFIED**) |
| BOMs | `itemStructures` | **UNVERIFIED** |
| Work orders, operations | `workOrders` (+ `WorkOrderOperation`) | https://docs.oracle.com/en/cloud/saas/supply-chain-and-manufacturing/26c/fasrp/op-workorders-get.html (child **UNVERIFIED**) |
| Work centers / routings | `workCenters`, `workDefinitions` | **UNVERIFIED** |
| Purchase orders | `purchaseOrders`; for receiving, `purchaseOrdersForReceiving` (finder `findByOrganization`) | https://docs.oracle.com/en/cloud/saas/supply-chain-and-manufacturing/26c/fasrp/op-purchaseordersforreceiving-get.html |
| Receipts | `receivingReceiptRequests` (create), `receivingReceiptTransactionRequests` | https://docs.oracle.com/en/cloud/saas/supply-chain-and-manufacturing/26b/fasrp/api-inventory-management-receiving-receipt-requests.html |
| Suppliers | `suppliers` | **UNVERIFIED** |
| Customers | `hubOrganizations` / `accounts` (CX/TCA) | **UNVERIFIED** |
| Employees | HCM `workers` (hcmRestApi) | **UNVERIFIED** |
| Lots/serials | `inventoryItemLots`, `inventoryItemSerialNumbers` | **UNVERIFIED** |
| Quality (useful) | Fusion Quality `qualityIssues`, `inspectionPlans` | **UNVERIFIED** |

### Paging, delta, limits, errors, events
- Query params `limit`, `offset`, `hasMore` in the response, `q=` filter (e.g. `q=LastUpdateDate > '2026-01-01T00:00:00'`), `onlyData=true`, `fields`, `finder`, `totalResults`. These are the Fusion REST framework conventions in the Quick Start: **UNVERIFIED** in detail. Default limit 25, max 500: **UNVERIFIED**.
- Rate limits: not published (**UNVERIFIED**). Errors: HTTP status with text/JSON body. Format **UNVERIFIED**.
- Events: Fusion business events go out through Oracle Integration Cloud adapters, not direct webhooks (**UNVERIFIED**).
- Sandbox: customers get test/stage pods. No public sandbox (**UNVERIFIED**).
- Writes: read-only default. Receipt creation is possible but approval-gated.

## E-Business Suite (EBS)
- Relevant only for on-prem customers. Integration goes through **Oracle E-Business Suite Integrated SOA Gateway** (REST services deployed from the Integration Repository) or DB views. **UNVERIFIED**, no doc fetched. Use the generic Database/REST adapter after customer discovery.
