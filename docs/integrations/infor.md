# Infor connector research (ION API Gateway; M3; SyteLine/CSI)

## ION API Gateway (CloudSuite, all products)
- In Infor OS: **API Gateway > Authorized Apps > +**, Type **Backend Service**, Save, **Download Credentials**, enable **Create Service Account**, pick the user, and optionally choose scopes. That produces a `.ionapi` file. https://docs.infor.com/inforos/latest/en-us/useradminlib_cloud/etl_dl_ag/oda1623328668051.html
- `.ionapi` fields: `ti` tenant, `ci` client id, `cs` client secret, `pu` + `ot` = token URL, `iu` gateway base, `saak`/`sask` service account keys. The ci/cs/pu/ot meanings are confirmed at https://docs.infor.com/inforos/latest/en-us/useradminlib_cloud/apigatewayag_cloud/wxo1761727799964.html; `iu`/`ti` are **UNVERIFIED**.
- Grants: the classic backend method is resource owner (`grant_type=password`, username=saak, password=sask, client_id=ci, client_secret=cs). https://github.com/infor-cloud/ion-api-sdk and https://docs.infor.com/inforos/2025.x/en-us/useradminlib_cloud/apigatewayag_cloud/ionapi_2025.x_apigatewayag_cloud_en-us.pdf. Current docs also document **client_credentials** with ci/cs at the same token URL: https://docs.infor.com/inforos/latest/en-us/useradminlib_cloud/apigatewayag_cloud/wxo1761727799964.html. Support both; prefer client_credentials when the tenant allows it.
- Call `<iu>/<ti>/<product>/<path>` with Bearer. Pattern is **UNVERIFIED**.
- Events: Infor ION supports BOD (Business Object Document) publishing via ION Connect. That's event-driven, but it needs ION connection points configured by the customer (**UNVERIFIED** doc). Data Lake/Compass is an alternative bulk read (**UNVERIFIED**).
- Rate limits: gateway throttling per tenant (**UNVERIFIED** numbers).

## M3
- MI programs over REST: on-prem `/m3api-rest/execute/<Program>/<Transaction>?CONO=...`, multi-tenant via ION `/<TENANT>/M3/m3api-rest/v2/execute/<Program>/<Transaction>`. Sources: https://learninform3.wordpress.com/2024/10/12/execute-m3-apis-through-postman-in-multi-tenant/ (community) and Infor ION API suite doc https://docs.infor.com/m3csche/latest/en-us/cschelib/industry_content_chemicals/vmw1731577228531.html. Path is **UNVERIFIED** officially.
- Programs: MMS200MI items (Get, GetItmBasic, LstByNumber), CRS610MI customers, CRS620MI suppliers, PDS001MI product structure (BOM) / PDS002MI operations, PDS010MI work centers (**UNVERIFIED**), PMS100MI manufacturing orders, PPS200MI purchase orders, MMS235MI lots (**UNVERIFIED**). MMS200MI is cited at https://docs.infor.com/m3csdee/latest/en-us/csdeelib/industry_content_distribution-essentials/apf1721326592696.html. The rest are **UNVERIFIED**.
- Paging: `maxrecs` param; delta via change-date `LMDT` fields (**UNVERIFIED**).

## SyteLine / CloudSuite Industrial (CSI) – Mongoose IDO REST v2
- Load: `GET /IDORequestService/ido/load/{ido}?properties=&filter=&orderby=&recordCap=&bookmark=`. https://docs.infor.com/mg/latest/en-us/mongooseolh/mgiiea/dwn1576796415221.html
- Auth: Mongoose security token in `Authorization` from the token endpoint (`/IDORequestService/ido/token/{config}/{user}/{pwd}`, **UNVERIFIED** path). Through ION, use Bearer plus header `X-Infor-MongooseConfig`. https://docs.infor.com/mg/latest/en-us/mongooseolh/mgiiea/uis1573179054690.html
- Per-IDO swagger: `/IDORequestService/ido/dynamic/api-docs-collection/{IDOName}`. https://docs.infor.com/mg/2026.10/en-us/mongooseolh/mgiiea/ook1576736580874.html
- IDOs: SLItems, SLCustomers, SLVendors, SLJobs/SLJobRoutes, SLWcs, SLEmployees, SLPos/SLPoitems, SLLots, SLSerials, SLCurrMatls (BOM): all **UNVERIFIED**, so confirm with the dynamic swagger. Delta: filter on `RecordDate` (**UNVERIFIED**). Paging via bookmark (cited load doc).
- Sandbox: CloudSuite tenants have TST/TRN environments (**UNVERIFIED**). Writes: read-only default.
