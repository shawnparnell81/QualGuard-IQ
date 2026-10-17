# Oracle connector research (Fusion Cloud SCM; EBS)

## Fusion Cloud SCM REST
### Auth
- Supported: Basic over HTTPS, SAML bearer, JWT bearer, and OAuth 2.0 tokens from the Fusion IAM (IDCS/OCI IAM) domain. https://docs.oracle.com/en/cloud/saas/applications-common/26c/farca/Quick_Start.html, https://docs.oracle.com/en/cloud/saas/supply-chain-and-manufacturing/26c/fasrp/Use_JWT_Token_for_Authorization.html
- Recommended M2M: create a **Confidential Application** in the Fusion IAM domain, enable Client Credentials (or JWT assertion), and add the Fusion Applications resource scope. Then `POST <IDCS>/oauth2/v1/token` and send the Bearer token. https://docs.oracle.com/en/cloud/saas/applications-common/26d/farca/configure_oauth.html
- The integration user needs Fusion job/duty roles for each resource (e.g. Item read, Work Order view, Procurement). Exact role codes are **UNVERIFIED**, so use the "Required roles" section on each endpoint page.

### Base URL and entities
- `https://<host>/fscmRestApi/resources/11.13.18.05/<resource>`. https://docs.oracle.com/en/cloud/saas/supply-chain-and-manufacturing/26c/fasrp/op-workorders-get.html. The exact pod host pattern is **UNVERIFIED** (customer-specific, shown in the customer's Fusion URL).
- The resources below are confirmed in the official SCM REST endpoint index (743 resources, 26C): https://docs.oracle.com/en/cloud/saas/supply-chain-and-manufacturing/26c/fasrp/rest-endpoints.html
| QMS | Resource |
|---|---|
| Items, revisions | `itemsV2` (revisions are a child; child name **UNVERIFIED** until the item page is read) https://docs.oracle.com/en/cloud/saas/supply-chain-and-manufacturing/26c/fasrp/api-product-lifecycle-management-items-version-2.html |
| BOMs | `itemStructures` |
| Work centers / routings | `workCenters`, `workDefinitions`, `productionResources` |
| Work orders | `workOrders` |
| Receiving | `purchaseOrdersForReceiving` (finder `findByOrganization`), `receivingReceiptRequests`, `receivingReceiptTransactionRequests`, `receipts`, `receiptLots`, `purchaseOrderReceiptSerials` |
| Lots / serials | `inventoryItemLots`, `inventoryItemSerialNumbers` |
| Quality (useful) | `qualityIssues`, `qualityActions`, `qualityCorrectiveAndPreventiveActions`, `inspectionPlans`, `inspectionResults`, `inspectionCharacteristics` |
- Suppliers and POs: `suppliers`, `purchaseOrders` (Procurement REST index). https://docs.oracle.com/en/cloud/saas/procurement/26c/fapra/rest-endpoints.html
- Employees: HCM `/hcmRestApi/resources/11.13.18.05/publicWorkers` (or `workers`). https://docs.oracle.com/en/cloud/saas/human-resources/farws/op-publicworkers-get.html
- Customers: Fusion CX/TCA resource (e.g. `accounts`, `hubOrganizations`). **UNVERIFIED** (not in the SCM or Procurement indexes; check the Sales/Service REST index for the customer's licensed pillar).
- Integration user roles: per endpoint "required roles". Exact role codes are **UNVERIFIED** (they depend on the customer's role design; check per resource page during setup).

### Paging, delta, limits, errors, events
- Collections return `items`, `count`, `hasMore`, `limit`, `offset`, `totalResults`. The default page is 25 (`limit` sets page size). Rows include `LastUpdateDate`. https://docs.oracle.com/en/cloud/saas/human-resources/farws/Navigate_a_Collection.html. Max limit 500: **UNVERIFIED** (not found on an official page).
- Delta: `q=LastUpdateDate > '...'` where the resource lists LastUpdateDate as queryable. Per resource, **UNVERIFIED**.
- Rate limits: none published (**UNVERIFIED**, no official page found). Errors: HTTP status plus body; exact format **UNVERIFIED**.
- Events: Fusion business events go out through Oracle Integration adapters (**UNVERIFIED**, no page fetched).
- Sandbox: customers get test/stage pods. No public sandbox (**UNVERIFIED**).
- Writes: read-only default. Receipt creation is possible but approval-gated.

## E-Business Suite (EBS)
- Relevant only for on-prem customers. Integration goes through **Oracle E-Business Suite Integrated SOA Gateway** (REST services deployed from the Integration Repository) or DB views. **UNVERIFIED**, no doc fetched. Use the generic Database/REST adapter after customer discovery.
