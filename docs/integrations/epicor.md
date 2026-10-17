# Epicor Kinetic connector research (REST v2)

Note: Epicor's detailed REST technical reference is on EpicWeb (login required). Most specifics below come from Epicor's product page plus community or partner sources, so they're marked **UNVERIFIED** until checked against the EpicWeb "REST Technical Reference" for the customer's version.

## Auth
- Two credentials on every v2 call: user authentication (Basic with a Kinetic user, or a Bearer token from Epicor Identity Provider/Azure AD) **plus** an API key in `x-api-key`. Keys are made in **API Key Maintenance** and can be bound to an **Access Scope** that limits services, BAQs and functions. Official product page confirms API keys and access scopes: https://www.epicor.com/en-us/products/enterprise-resource-planning-erp/kinetic/tools-and-technology/open-rest-api/. Header name and steps: https://docs.lumina-erp.com/epicor-kinetic/rest-v2-api/ (partner, **UNVERIFIED** officially).
- M2M via Epicor Identity: client credentials at `https://login.epicor.com/connect/token`, scope `epicor_erp`. Source is community (https://www.epiusers.help/t/client-credentials-auth-flow/127726) and Epicor video (https://erpvideo.epicor.com/detail/video/6374121778112/accessing-kinetic-erp-rest-using-idp). **UNVERIFIED** in written docs.
- Integration user: a dedicated Kinetic user with security groups for the services needed, plus an Access Scope on the key. **UNVERIFIED**.

## Base URL and entities
- `https://<server>/<instance>/api/v2/odata/<Company>/Erp.BO.<Name>Svc/<EntitySet>`, OData v4, `$metadata` per service. Help: `/<instance>/api/help`. Sources: lumina page above; https://www.epiusers.help/t/woking-with-api-ver2/87658. OData v4 itself is confirmed on the Epicor product page.
| QMS | Service | Status |
|---|---|---|
| Parts, revisions | `Erp.BO.PartSvc` (Parts, PartRevs) | **UNVERIFIED** |
| BOM | `Erp.BO.EngWorkBenchSvc` / BOM via PartRev `PartMtls`, `PartOprs` | **UNVERIFIED** |
| Customers / Suppliers | `Erp.BO.CustomerSvc` (cited above), `Erp.BO.VendorSvc` | Customer cited; Vendor **UNVERIFIED** |
| Work centers (resource groups) | `Erp.BO.ResourceGroupSvc` | **UNVERIFIED** |
| Employees | `Erp.BO.EmpBasicSvc` | **UNVERIFIED** |
| Jobs / routings | `Erp.BO.JobEntrySvc` (JobHeads, JobOpers, JobMtls) | **UNVERIFIED** |
| POs / receipts | `Erp.BO.POSvc`, `Erp.BO.ReceiptSvc` | **UNVERIFIED** |
| Lots / serials | `Erp.BO.LotSelectUpdateSvc`, `Erp.BO.SerialNoSvc` | **UNVERIFIED** |
- Recommended read path: a customer-built **BAQ** exposed at `/api/v2/odata/<Company>/BaqSvc/<BAQID>/Data`. **UNVERIFIED**.

## Paging, delta, limits, errors, events, sandbox
- `$top/$skip/$filter/$select/$expand` (lumina page). Delta via `SysRevID` or a BAQ filter on change date: **UNVERIFIED**.
- Rate limits: none published (**UNVERIFIED**). Errors: JSON `ErrorMessage`, `ErrorType`, `CorrelationId`: **UNVERIFIED**.
- Events: no native webhooks. Outbound needs BPM/Epicor Functions or Epicor Automation Studio (**UNVERIFIED**).
- Sandbox: cloud customers get Pilot/Third environments (**UNVERIFIED**).
- Writes: read-only default.
