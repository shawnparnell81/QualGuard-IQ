# Microsoft Dynamics 365 connector research

## Business Central (API v2.0)
- Auth: Entra ID OAuth 2.0 client credentials, scope `https://api.businesscentral.dynamics.com/.default`. Register the Entra app, grant the **API.ReadWrite.All** application permission (Dynamics 365 Business Central), admin consent, then in BC open the **Microsoft Entra Applications** page, add the client ID, set State=Enabled, and assign permission sets (use least privilege, e.g. read-only sets). https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/administration/automation-apis-using-s2s-authentication
- Base: `https://api.businesscentral.dynamics.com/v2.0/{tenantId}/{environment}/api/v2.0/companies({companyId})/<entity>`. https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/api-reference/v2.0/
- Standard entities: `items`, `customers`, `vendors`, `employees`, `purchaseOrders` (+`purchaseOrderLines`), `purchaseReceipts` (+lines). Per the API v2.0 reference index above. Individual entity pages not fetched, so field list is **UNVERIFIED**, though each has `id` (GUID) and `lastModifiedDateTime`.
- **Gotcha:** standard v2.0 has **no** production orders, BOMs, routings, work centers or item tracking (lots/serials) entities (**UNVERIFIED**, inferred from the reference index). These need a custom API page extension (AL `PageType = API`) installed in the tenant, or OData web services published from pages.
- Paging: server-driven `@odata.nextLink`. Filters: `$filter=lastModifiedDateTime gt 2026-01-01T00:00:00Z`. (**UNVERIFIED** link.)
- Limits: HTTP 429 on throttling, so use backoff. https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/api-reference/v2.0/dynamics-rate-limits (exact numbers, e.g. 6000 req/5 min and 5 concurrent per user, are **UNVERIFIED** from this fetch).
- Webhooks: yes. `POST .../api/v2.0/subscriptions` with notificationUrl, resource, clientState. You must echo `validationToken`. Subscriptions expire after 3 days, so renew with PATCH. https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/api-reference/v2.0/dynamics-subscriptions. Notifications only say a change happened, so the adapter still re-reads.
- Errors: OData JSON `{"error":{"code","message"}}` (**UNVERIFIED**).
- Sandbox: BC sandbox environments in the admin center (**UNVERIFIED** link).

## Finance & Supply Chain Management (F&O)
- Auth: Entra app (client credentials, scope `https://<env>.operations.dynamics.com/.default`). Register it in F&O at **System administration > Setup > Microsoft Entra applications**, mapped to a service user with roles. **UNVERIFIED** link.
- OData: `https://<env>.operations.dynamics.com/data/<PublicCollection>`, cross-company with `?cross-company=true` (**UNVERIFIED**).
- Entities: `ReleasedProductsV2` https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/data-entities/entity-released-products-v2-releasedproductv2. Others are **UNVERIFIED**: `CustomersV3`, `VendorsV2`, `BillOfMaterialsHeaders`/`BillOfMaterialsLines`, `ProductionOrderHeaders`, `RouteOperations`, `OperationsResources`, `Workers`, `PurchaseOrderHeadersV2`/`PurchaseOrderLinesV2`, `ProductReceiptHeaders`, `InventBatches`, `InventSerialNumbers`, `EngineeringChange` (engineering versions).
- Limits: service protection plus priority-based throttling returns 429 with `Retry-After`. Map the app to High/Medium/Low priority at **System administration > Setup > Throttling priority mapping**. https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/data-entities/service-protection-api-limits, https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/data-entities/priority-based-throttling, https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/data-entities/service-protection-retry-operations
- Bulk/delta: Data Management package API, change tracking on entities, Business Events (to Azure Service Bus/Event Grid/HTTPS), Dual-write/Dataverse. All **UNVERIFIED**.
- Sandbox: Tier-1/Tier-2 sandboxes via LCS/PPAC (**UNVERIFIED**).
- Writes: read-only default.
