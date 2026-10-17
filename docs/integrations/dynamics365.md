# Microsoft Dynamics 365 connector research

**UNVERIFIED** = not confirmed in official Microsoft Learn docs. The reason is given each time.

## Business Central (API v2.0)
- **Auth:** Entra ID OAuth 2.0 client credentials, scope `https://api.businesscentral.dynamics.com/.default`.
  - Register an Entra app and grant the **API.ReadWrite.All** application permission (Dynamics 365 Business Central), with admin consent.
  - In BC, open the **Microsoft Entra Applications** page, add the client ID, set State=Enabled, and assign least-privilege permission sets.
  - https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/administration/automation-apis-using-s2s-authentication
- **Base URL:** `https://api.businesscentral.dynamics.com/v2.0/{tenantId}/{environment}/api/v2.0/companies({companyId})/<entitySet>`. APIs are on by default online. Use the **API Overview** page in BC to list what's published. https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/api-reference/v2.0/
- **Standard v2.0 resources** (complete list from the official reference TOC, https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/api-reference/v2.0/toc.json): `items`, `itemVariants`, `itemCategories`, `itemLedgerEntries`, `customers`, `vendors`, `employees`, `locations`, `purchaseOrders`/`purchaseOrderLines`, `purchaseReceipts`/`purchaseReceiptLines`, `unitsOfMeasure`, `subscriptions`, and others.
- **Fields** (resource pages, e.g. https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/api-reference/v2.0/resources/dynamics_item):
  - item: `id`, `number`, `displayName`, `type`, `itemCategoryCode`, `blocked`, `gtin`, `inventory`, `baseUnitOfMeasureCode`, `unitCost`, `lastModifiedDateTime`
  - vendor: `id`, `number`, `displayName`, address fields, `email`, `blocked`, `lastModifiedDateTime` (https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/api-reference/v2.0/resources/dynamics_vendor)
  - itemLedgerEntry: `entryNumber`, `itemNumber`, `postingDate`, `entryType`, `sourceType`, `sourceNumber`, `documentType`, `documentNumber`, `quantity`, `lastModifiedDateTime` (https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/api-reference/v2.0/resources/dynamics_itemledgerentry)
  - purchaseReceiptLine: `documentId`, `sequence`, `lineType`, `lineObjectNumber`, `description`, `quantity`, `unitOfMeasureCode`, `expectedReceiptDate`. It has **no** lastModifiedDateTime. (https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/api-reference/v2.0/resources/dynamics_purchasereceiptline)
- **Gotcha (confirmed):** the standard v2.0 list has **no** production orders, BOMs (production BOMs), routings, work/machine centers, or lot/serial (item tracking) resources. `itemLedgerEntry` also has no lot/serial field. Same TOC and resource pages as above. Standard APIs can't be extended with fields; you copy the AL and make a custom API. https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/api-reference/v2.0/. **So manufacturing data needs a custom API extension (AL `PageType = API`) installed in the customer tenant. We'd ship it as a companion BC extension.**
- **Paging/delta:** `$filter=lastModifiedDateTime gt <ISO>`, pages via continuation tokens (`@odata.nextLink`), `$expand` for related entities. https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/api-reference/v2.0/dynamics-rate-limits
- **Limits:** HTTP 429 means back off. Requests longer than **10 minutes** return 504. Same URL. Per-user OData V4 limits:
  - 5 concurrent requests. Extra requests queue, and after 8 min in the queue return 503.
  - About 6000 requests per 5-minute window; over that returns 429.
  - Max page size 20,000 entities (over that returns 413).
  - Max `$batch` size 100 operations.
  - Max body size 350 MB.
  - https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/administration/operational-limits-online
  - Design: cap each BC connection at 5 parallel calls per service user.
- **Webhooks:** `POST .../api/v2.0/subscriptions` with `notificationUrl`, `resource`, `clientState`. You must echo `validationToken`. Subscriptions expire after 3 days, so renew with PATCH. https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/api-reference/v2.0/dynamics-subscriptions
- **Errors:** HTTP status plus an OData error object with `code` and `message` (e.g. `BadRequest_NotFound`, `Request_EntityChanged`, `Authentication_InvalidCredentials`). https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/webservices/dynamics-error-codes
- **Sandbox:** sandbox environments (`environmentType: Sandbox`) through the admin center / Admin Center API. https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/administration/administration-center-api_environments
- **Writes:** read-only default.

## Finance & Supply Chain Management (F&O)
- **Auth:** register an Entra app (client secret or cert), then client credentials with scope `https://<env>.operations.dynamics.com/.default`. In F&O, map the app at **System administration > Setup > Microsoft Entra applications** to a dedicated service user with security roles. https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/data-entities/services-home-page
- **OData root:** `https://<env>.operations.dynamics.com/data/<PublicCollectionName>`. Only entities marked IsPublic are exposed. The default is the user's company; `?cross-company=true` plus `$filter=dataAreaId eq 'USMF'` returns all. https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/data-entities/odata
- **Entities:**
  - `ReleasedProductsV2` (confirmed: https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/data-entities/entity-released-products-v2-releasedproductv2).
  - Manufacturing entities confirmed by name in the Common Data Model reference: `ProdProductionOrderHeaderEntity`, `RouteHeaderEntity`, `RouteOperationEntity`, `ProdProductionOrderRouteOperationEntity`, `BOMBillOfMaterialsHeaderEntity` (https://learn.microsoft.com/en-us/common-data-model/schema/core/operationscommon/entities/supplychain/productioncontrol/prodproductionorderheaderentity, https://learn.microsoft.com/en-us/common-data-model/schema/core/operationscommon/entities/supplychain/productioncontrol/routeoperationentity, https://learn.microsoft.com/en-us/common-data-model/schema/core/operationscommon/entities/supplychain/productinformationmanagement/bombillofmaterialsheaderentity).
  - Their **public collection names** (e.g. `ProductionOrderHeaders`, `BillOfMaterialsHeaders`), plus customers (`CustomersV3`), vendors (`VendorsV2`), workers, purchase orders (`PurchaseOrderHeadersV2`), product receipts, batches, serials and work-center resources: **UNVERIFIED** (these appear only in each tenant's `/data/$metadata`; read it from a customer or trial tenant).
- **Limits:** service protection plus resource-based throttling returns 429 with `Retry-After`. Map the app to High/Medium/Low at **System administration > Setup > Throttling priority mapping**. Retries get no priority. https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/data-entities/service-protection-api-limits, https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/data-entities/priority-based-throttling, https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/data-entities/service-protection-retry-operations
- **Delta:**
  - Enable SQL change tracking per entity (Data management > Data entities > Change tracking: primary table / entire entity / custom query). Without it, exports are full only. https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/data-entities/entity-change-track
  - Whether OData `$filter` on a modified-datetime field works depends on whether the entity exposes one (**UNVERIFIED**, per entity in `$metadata`).
- **Events:** Business events go to HTTPS, Azure Service Bus or Event Grid endpoints, set up at System administration > Setup > Business events. https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/business-events/home-page, https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/business-events/managing-business-event-endpoints
- **Errors:** OData JSON error. Exact shape is **UNVERIFIED** (no dedicated Learn page found).
- **Sandbox:** Tier-2+ sandbox environments through LCS / Power Platform admin center. **UNVERIFIED** (no page fetched this pass).
- **Writes:** read-only default.
