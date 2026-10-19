# Sage Intacct connector research

Official Sage Intacct developer documentation only.

## Auth: Web Services sender plus company user
- Every request is `POST https://api.intacct.com/ia/xml/xmlgw.phtml` with `Content-Type: application/xml`. GET is blocked. https://developer.intacct.com/web-services/
- The `control` element carries the Web Services **sender id and password** (from a Web Services developer license). The sender id must be added to the company's Web Services authorizations (Company > Setup > Company > Security), or every call fails with XL03000006. https://developer.intacct.com/web-services/requests/, https://developer.intacct.com/support/faq/
- The `operation/authentication/login` element carries the **company id, Web Services user id and password**, plus `locationid` for an entity in a multi-entity company.
- `uniqueid=false` is used because the connector only reads.

## Reads
- `readByQuery` with `object`, `fields`, `query` and `pagesize` (up to 1000), then `readMore` with the `resultId` until `numremaining="0"`. https://developer.intacct.com/web-services/queries/
- Delta: `WHENMODIFIED > 'MM/DD/YYYY HH:MM:SS'`.

| QMS entity | Object | Key fields |
|---|---|---|
| Item | ITEM | RECORDNO, ITEMID, NAME, ITEMTYPE |
| Customer | CUSTOMER | RECORDNO, CUSTOMERID, NAME |
| Supplier | VENDOR | RECORDNO, VENDORID, NAME |
| Employee | EMPLOYEE | RECORDNO, EMPLOYEEID, PERSONALINFO.CONTACTNAME |
| Purchase order | PODOCUMENT | RECORDNO, DOCNO, CUSTVENDID, WHENCREATED |

## Errors
- `control/status` failure: the sender was rejected. `operation/authentication/status` failure: the user or company was rejected. Each carries `errormessage/error` with `errorno`, `description` and `description2`; the connector reports the number and the second description.

## UNVERIFIED
- PODOCUMENT covers every purchasing transaction type; filter by `DOCPARID` for purchase orders in the customer's company.
- The company's date format may differ from the default MM/DD/YYYY.
- Sage 100 and Sage 300 have no hosted public API; use CSV exchange or a generic REST connection to the customer's own API layer.
