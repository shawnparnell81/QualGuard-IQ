# SAP connector research (S/4HANA Cloud, S/4HANA on-prem, Business One)

**UNVERIFIED** = not confirmed in official SAP docs this round.

## S/4HANA Cloud (Public Edition)
### Auth / setup
- Each API is enabled by a **Communication Scenario**, activated through **Communication Arrangements**. You also create a Communication System and a Communication User, which use Basic or OAuth 2.0/X.509 inbound auth. Example: API_PRODUCT_SRV ↔ SAP_COM_0009, Business Partner/Customer/Supplier ↔ SAP_COM_0008. https://api.sap.com/products/SAPS4HANACloud/apis/packages (scenario IDs per API on each API page). The 0009/0008 mappings are also shown at https://community.sap.com/t5/enterprise-resource-planning-blog-posts-by-sap/odata-service-of-business-partner-in-sap-s-4hana-cloud/ba-p/14278613 (SAP-authored blog).
- Scenario IDs for BOM, production order, PO, material documents, work center and batch: **UNVERIFIED**. Read them off each API page on api.sap.com before setup.
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
- Sandbox: api.sap.com offers a shared read-only API sandbox with an APIKey header for trying calls. Customers have Q/test tenants (3-system landscape): **UNVERIFIED**.

## S/4HANA on-prem / Private Cloud
- Same A2X OData services, activated in `/IWFND/MAINT_SERVICE` on the gateway. Auth: Basic, X.509 or SAML/OAuth by gateway config. Role: a technical user with the business catalog roles for each API. **UNVERIFIED** (no official page fetched).
- Base URL `https://<host>:<port>/sap/opu/odata/sap/<SERVICE>`. **UNVERIFIED**.

## SAP Business One Service Layer
- Session auth: `POST /b1s/v1/Login` (or `/b1s/v2/`) with `CompanyDB`, `UserName`, `Password`. Returns a `B1SESSION` cookie (plus `ROUTEID` when load balanced), sent on every call. https://help.sap.com/doc/fc2f5477516c404c8bf9ad1315a17238/10.0/en-US/Working_with_SAP_Business_One_Service_Layer.pdf
- Base `https://<server>:50000/b1s/v1/`, port 50000 by default (same PDF; port detail **UNVERIFIED**).
- Entities: `Items`, `BusinessPartners` (CardType cCustomer/cSupplier), `ProductTrees` (BOM), `ProductionOrders`, `PurchaseOrders`, `PurchaseDeliveryNotes` (goods receipt PO), `BatchNumberDetails`, `SerialNumberDetails`, `EmployeesInfo`, `Resources`. Items/PurchaseOrders/ProductionOrders are cited in the PDF. Others are **UNVERIFIED**.
- Paging: server page size default 20, use `Prefer: odata.maxpagesize=N` and `odata.nextLink`. Delta: `UpdateDate`/`UpdateTime` filter. Both **UNVERIFIED**.
- Sessions time out at 30 min by default: **UNVERIFIED**. No webhooks (**UNVERIFIED**). No vendor sandbox, so use a customer test company DB.
- Writes: read-only default.
