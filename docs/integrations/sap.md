# SAP connector research (S/4HANA Cloud, S/4HANA on-prem, Business One)

**UNVERIFIED** = not confirmed in official SAP docs this round.

## S/4HANA Cloud (Public Edition)
### Auth / setup
- Each API is enabled by a **Communication Scenario**, activated through **Communication Arrangements**. You also create a Communication System and a Communication User, which use Basic or OAuth 2.0/X.509 inbound auth. Example: API_PRODUCT_SRV ↔ SAP_COM_0009, Business Partner/Customer/Supplier ↔ SAP_COM_0008. https://api.sap.com/products/SAPS4HANACloud/apis/packages (scenario IDs per API on each API page). The 0009/0008 mappings are also shown at https://community.sap.com/t5/enterprise-resource-planning-blog-posts-by-sap/odata-service-of-business-partner-in-sap-s-4hana-cloud/ba-p/14278613 (SAP-authored blog).
- Scenario IDs reported in SAP Cloud SDK references and SAP-authored material:
  - BOM `API_BILL_OF_MATERIAL_SRV;v=0002` uses SAP_COM_0105.
  - Production order `API_PRODUCTION_ORDER_2_SRV` uses SAP_COM_0104.
  - PO `API_PURCHASEORDER_PROCESS_SRV` uses SAP_COM_0053 (deprecated in favor of OData V4 `API_PURCHASEORDER_2`).
  - Material document `API_MATERIAL_DOCUMENT_SRV` uses SAP_COM_0108 (GR: `GoodsMovementCode` 01, `GoodsMovementType` 101, `GoodsMovementRefDocType` B).
  - Batch `API_BATCH_SRV` uses SAP_COM_0337.
  - Inspection lot `API_INSPECTIONLOT_SRV` uses SAP_COM_0318.
  - All still **UNVERIFIED**. api.sap.com API pages and spec downloads redirect to an SAP login (checked 2026-10-16), and the SAP-hosted SDK pages wouldn't load from here. Confirm on api.sap.com with an SAP ID before setup. Work center scenario: **UNVERIFIED** (same reason).
- Base URL `https://<tenant>-api.s4hana.ondemand.com/sap/opu/odata/sap/<SERVICE>` (OData V2) or `/sap/opu/odata4/sap/...` (V4). Cited at https://community.sap.com/t5/technology-blog-posts-by-members/api-consumption-s-4-hana-cloud-public-edition-and-s-4-hana-cloud-private/ba-p/14162598 (community, not official, so the URL pattern is **UNVERIFIED** in official docs).

### Entities (OData V2 services on api.sap.com)
| QMS | Service / entity set | Status |
|---|---|---|
| Products, revisions | `API_PRODUCT_SRV` / `A_Product`, `A_ProductPlant`. Revision levels via `API_CHANGEMASTER` / engineering change | product service cited above; change master **UNVERIFIED** |
| BOMs | `API_BILL_OF_MATERIAL_SRV;v=0002` / `MaterialBOM`, `MaterialBOMItem` | **UNVERIFIED** |
| Customers / Suppliers | `API_BUSINESS_PARTNER` / `A_Customer`, `A_Supplier`, `A_BusinessPartner` | service and scenario cited above; entity names **UNVERIFIED** |
| Work centers | `API_WORK_CENTERS` / `A_WorkCenters` | **UNVERIFIED** |
| Employees | Workforce Person APIs (often owned by SuccessFactors) | **UNVERIFIED** |
| Production orders / routings | `API_PRODUCTION_ORDER_2_SRV` / `A_ProductionOrder_2`, `A_ProductionOrderOperation_2`; routings `API_PRODUCTION_ROUTING` | **UNVERIFIED** |
| Purchase orders | `API_PURCHASEORDER_PROCESS_SRV` / `A_PurchaseOrder`, `A_PurchaseOrderItem` (OData V4 successor exists) | **UNVERIFIED** |
| Receipts | `API_MATERIAL_DOCUMENT_SRV` / `A_MaterialDocumentHeader`, `A_MaterialDocumentItem` (movement type 101) | **UNVERIFIED** |
| Batches / serials | `API_BATCH_SRV`; serial numbers via the material document item serial entity | **UNVERIFIED** |
| Quality (useful) | `API_INSPECTIONLOT_SRV` | **UNVERIFIED** |

### Paging, delta, limits, errors, events, sandbox
- Standard OData `$top/$skip/$filter/$select/$expand`, with `LastChangeDateTime` filters on most A_ entities: **UNVERIFIED** per entity.
- Rate limits: none published that I found, so **UNVERIFIED**.
- Errors: OData V2 JSON `{"error":{"code","message":{"value"}, "innererror":{...}}}`: **UNVERIFIED** citation.
- Events: SAP Event Mesh / Business Events (e.g. `sap.s4.beh.product.v1.Product.Changed.v1`) on api.sap.com under "Events": **UNVERIFIED**. They need Event Mesh entitlement.
- Sandbox: api.sap.com "Try out" sandbox needs an SAP login (the hub redirects to SAP ID auth), plus the customer's Q/test tenant. Details are **UNVERIFIED** (login-gated).

## S/4HANA on-prem / Private Cloud
- Same A2X OData services, activated in `/IWFND/MAINT_SERVICE` on the gateway. Auth: Basic, X.509 or SAML/OAuth by gateway config. Role: a technical user with the business catalog roles for each API. **UNVERIFIED** (no official page fetched).
- Base URL `https://<host>:<port>/sap/opu/odata/sap/<SERVICE>`. **UNVERIFIED**.

## SAP Business One Service Layer
Source for this section: SAP's official guide "Working with SAP Business One Service Layer" (document version 1.29, 2026-07-27): https://help.sap.com/doc/fc2f5477516c404c8bf9ad1315a17238/10.0/en-US/Working_with_SAP_Business_One_Service_Layer.pdf
- **Session auth:** `POST /b1s/v1/Login` (or `/b1s/v2/`) with `CompanyDB`, `UserName`, `Password`. It returns a `B1SESSION` cookie (required on every request) and a `ROUTEID` cookie (optional, keeps load-balancer stickiness). The response includes `SessionTimeout: 30` (minutes, configurable).
- **Base URL:** `https://<server>:50000/b1s/v1/` (v1 is OData V3) or `/b1s/v2/` (OData V4).
- **Paging:**
  - The default page size is 20; the guide's example is `odata.nextLink: /b1s/v1/Orders?$skip=20`.
  - Override with the header `Prefer: odata.maxpagesize=N` or the server option `PageSize` in `conf/b1s.conf`. 0 turns paging off.
  - V3 returns `odata.nextLink`; V4 returns `@odata.nextLink`.
- **Filtering:** standard `$filter`, e.g. `BusinessPartners?$filter=CardType eq 'cCustomer'`. `$crossjoin` is supported.
- **Errors:** `{"error":{"code":<int>,"message":{"lang":"en-us","value":"..."}}}`. For example, 401 with code 301 means the session is invalid or timed out, so re-login.
- **Webhooks (new, guide v1.28, 2026-01-07):** EventSubscription and EventNotifications plus Webhook Configuration/Messenger, with retry. A newer B1 version is required; check which versions support it with the customer.
- **Entities:** `Items`, `BusinessPartners`, `ProductionOrders`, `PurchaseOrders` are named in the guide.
  - `ProductTrees` (BOM), `PurchaseDeliveryNotes` (GRPO), `BatchNumberDetails`, `SerialNumberDetails`, `EmployeesInfo`, `Resources`: **UNVERIFIED**. The guide refers to the separate Service Layer API reference / `$metadata`, so read `GET /b1s/v1/$metadata` on a customer server.
  - Delta via `UpdateDate`/`UpdateTime`: **UNVERIFIED** (same reason).
- **Sandbox:** no vendor sandbox. Use a customer test company DB.
- **Writes:** read-only default.
