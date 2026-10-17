# Plex (Rockwell Automation) connector research

- Auth: static subscription key in header `X-Plex-Connect-Api-Key`. There's no OAuth on Connect. A missing, invalid or expired key returns 401. https://developers.plex.com/getstarted
- Setup: activate **Plex IAM**. Your Plex Champion files a support case to get Developer Portal accounts. Then at developers.plex.com **My Apps > Create App**, select API Products, and you get the Consumer Key/Secret. https://developers.plex.com/getstarted, https://developers.plex.com/best-practices. (Step order taken from the getstarted page sections. The fetch returned only section headers, so the step wording is **UNVERIFIED**.)
- Headers: `X-Plex-Connect-Tenant-Id` for multi-PCN access, plus a Customer Id header (named on the getstarted page). https://developers.plex.com/getstarted
- Base: `https://connect.plex.com/{collection}/{version}/{resource}`. Test: `https://test.connect.plex.com`, and its key must differ from production. Per getstarted "URL structure". Test-tier detail is from https://www.fibric.io/integrations/c/cn-plex (third party, **UNVERIFIED**).
- Products/collections: `mdm/v1/parts`, `mdm/v1/suppliers`, `mdm/v1/customers`, `mdm/v1/employees`, `mdm/v1/operations`, `production/v1/scheduling/jobs`, `purchasing/v1/purchase-orders`, `inventory/v1/inventory-tracking/containers` (lot/serial = containers), `mdm/v1/parts/{id}/bom` (**UNVERIFIED** every path). Discover them in the portal API products after the app is subscribed.
- Paging/dates: "List Query String Parameters" and UTC dates are documented on getstarted. Exact params are **UNVERIFIED**.
- Rate limit: 200 calls/min reported at https://github.com/just-shane/plex-api/blob/master/Plex_API_Reference.md (community, **UNVERIFIED**).
- Errors: 400 malformed/validation, 401, 403, 404, 500. https://developers.plex.com/getstarted
- Events: none documented in Connect (**UNVERIFIED**). Legacy alternatives: Plex Data Source web services (SOAP, `ExecuteDataSource`) and Plex Smart Forms/EDI (**UNVERIFIED**).
- Sandbox: test.connect.plex.com tier (above). Writes: read-only default.

## Verification status (pass 2, 2026-10-16)
developers.plex.com is a client-rendered app. Plain fetches return only section headings, and product/API pages need a portal account (Plex IAM via a support case). The confirmed items are the auth header, 401/403/404/500 meanings and section names. The remaining items need a portal login.
