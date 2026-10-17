# IQMS / DELMIAworks connector research

- Integration options:
  1. **DELMIAworks WebAPI** (separately licensed module): a .NET SOA layer exposing REST (JSON/XML), shipped as an SDK with entity DLLs, help files and C# samples. https://www.solidworks.com/product/delmiaworks/manufacturing-erp/web-api/
  2. **GraphQL API SDK** alongside REST. REST areas include Inventory, Manufacturing, Receiving, QA, CRM, WMS, Workforce. https://www.3ds.com/assets/invest/2022-03/delmiaworks-webapi-graphql-rest-api-datasheet.pdf
  3. EDI module, and the customer's Oracle DB (direct read-only DB views). The DB option is **UNVERIFIED** as officially supported.
- **Gotcha:** there's no public API portal. The endpoint contract ships in the licensed SDK help file, so field and endpoint names can't be confirmed publicly. Every entity mapping is **UNVERIFIED** until the customer supplies the SDK docs for their version.
- Auth: session login with ERP user, password, database alias, then a token sent on later calls. The IQMS SDK sample uses `IQMSServiceClient(BASE_URL, user, pwd, pwd, db).Authenticate()`. Base example `http://<server>:8080/WebAPI`. Endpoints `/CRM/CustomerCentral/Customers/` and `/CRM/CustomerCentral/UpdateCustomer/`. Source: IQMS whitepaper hosted by a reseller, https://manufacturingerp.co.uk/wp-content/uploads/Growing-Your-Manufacturing-Business-with-Application-Programming-Interfaces-API-Whitepaper.pdf (older IQMS-era material, **UNVERIFIED** for current releases). `POST /User/Login` returning AuthToken is also **UNVERIFIED**.
- Paging, delta, rate limits, errors, webhooks, sandbox: all **UNVERIFIED** (not public). On-prem/HMS, so the customer's test DB is the sandbox.
- Recommendation: build this adapter last, against customer-provided SDK docs. Use CSV exchange or DB views as the fallback. Read-only.
