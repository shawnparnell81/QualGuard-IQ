# QuickBooks Online connector research

Official Intuit sources only. **UNVERIFIED** marks anything not confirmed there.

## Auth: OAuth 2.0 authorization code
- Create an app at https://developer.intuit.com with the `com.intuit.quickbooks.accounting` scope. Development keys work against sandbox companies; production keys need Intuit's app assessment. https://developer.intuit.com/app/developer/qbo/docs/develop/authentication-and-authorization/oauth-2.0
- The company administrator connects the app once (authorization code flow), which returns an access token, a refresh token and the company id (`realmId`).
- **Token endpoint:** `POST https://oauth.platform.intuit.com/oauth2/v1/tokens/bearer`, Basic auth with client id and secret, form `grant_type=refresh_token&refresh_token=...`. Access tokens last one hour. Refresh tokens last 100 days and Intuit may return a new one on any refresh, so the connector always stores the newest (encrypted). https://developer.intuit.com/app/developer/qbo/docs/develop/authentication-and-authorization/faq
- The rotated refresh token is not part of the validation fingerprint, so a routine rotation does not reset the connection's gates.

## Base URL and API
- Production `https://quickbooks.api.intuit.com`, sandbox `https://sandbox-quickbooks.api.intuit.com`. The connector picks one from the connection's environment.
- Query: `GET /v3/company/{realmId}/query?query=SELECT * FROM Vendor WHERE MetaData.LastUpdatedTime > '...' ORDERBY Id STARTPOSITION 1 MAXRESULTS 1000&minorversion=75`. https://developer.intuit.com/app/developer/qbo/docs/learn/explore-the-quickbooks-online-api/data-queries
- Minor versions 1 to 74 were retired on 1 August 2025; 75 is the base. https://blogs.intuit.com/2025/01/21/changes-to-our-accounting-api-that-may-impact-your-application/

## Entities
| QMS entity | QuickBooks entity | Key fields |
|---|---|---|
| Item | Item | Id, Sku, Name, Description, Type, SyncToken, MetaData.LastUpdatedTime |
| Customer | Customer | Id, DisplayName, CompanyName, PrimaryEmailAddr |
| Supplier | Vendor | Id, DisplayName, CompanyName, PrimaryEmailAddr |
| Employee | Employee | Id, EmployeeNumber, DisplayName |
| Purchase order | PurchaseOrder | Id, DocNumber, VendorRef, TxnDate, Line[].ItemBasedExpenseLineDetail |

QuickBooks Online has no work orders, routings or BOMs.

## Limits and errors
- 500 requests per minute per realm and 10 concurrent requests; 429 when exceeded. https://developer.intuit.com/app/developer/qbo/docs/learn/rest-api-features#limits-and-throttles
- Errors come back as `{"Fault":{"Error":[{"Message","Detail","code"}],"type"}}`.

## UNVERIFIED
- Sku is only filled when SKU tracking is on; map `number` to `Name` otherwise.
