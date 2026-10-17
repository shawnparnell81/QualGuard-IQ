# Business Central manufacturing adapter

This adapter reads Business Central manufacturing data. It is separate from the generic Dynamics 365 connector (`dynamics365.md`), which reads only the standard API v2.0 and is not changed by it.

**Status: no live validation.** The adapter is covered by recorded-response tests only (`GapClosureBcManufacturingTests`). It has not been run against a customer tenant. Every entity set, key and field name below is a configurable default, flagged **UNVERIFIED** until the connection test and a person confirm it against the customer's system.

## Why a separate adapter

Business Central's standard API v2.0 has no production orders, production order lines, routings, work centers, machine centers, capacity ledger entries, output or consumption journals, and no lot or serial fields on item ledger entries (see `dynamics365.md`). Manufacturing data is therefore read from one of two sources, chosen per connection as the **edition**:

| Edition | Source | Needs |
|---|---|---|
| `WebServices` | Standard BC pages published as OData V4 web services: `/v2.0/{tenant}/{environment}/ODataV4/Company('{company name}')/{service name}` | An administrator publishes each page on the **Web Services** page in BC. No extension. |
| `CustomApi` | API pages from a manufacturing extension installed in the tenant: `/v2.0/{tenant}/{environment}/api/{publisher}/{group}/{version}/companies({company id})/{entitySet}` | An extension (AL `PageType = API`) that exposes the entities. Nothing assumes such an extension exists: `apiPublisher` and `apiGroup` must be entered, and the test fails until each page answers. |

## Connection fields

- **Account:** Entra tenant id. **Client id / secret:** Entra application (client credentials, scope `https://api.businesscentral.dynamics.com/.default`).
- **Company:** company **name** for `WebServices`, company **id** for `CustomApi`.
- **Base URL:** blank for BC online (`https://api.businesscentral.dynamics.com`).

## Options (all optional unless stated)

| Option | Meaning | Default |
|---|---|---|
| `entities` | Array of entities this connection reads. Others are refused. | `ProductionOrder`, `ProductionOrderLine`, `WorkCenter`, `MachineCenter` |
| `environment` | BC environment name | `production` (use your sandbox name first) |
| `apiPublisher`, `apiGroup`, `apiVersion` | Extension API route (`CustomApi`, publisher and group required) | version `v1.0` |
| `entitySet.<Entity>` | Entity set or web service name | see below |
| `idField.<Entity>` | Key field(s), comma separated for composite keys | see below |
| `filter.<Entity>` | OData `$filter` added to every read; `""` removes the default | released orders only; lots/serials only rows with a lot/serial |
| `modifiedField` | Field for delta reads; `""` turns delta off | `lastModifiedDateTime` (`CustomApi`), none (`WebServices`) |
| `pageSize` | Rows per page (max 20,000) | 1,000 |

Field maps per entity are set on each entity's mapping (key = canonical field, value = BC field path), overriding the defaults below.

## Entities and default mapping (UNVERIFIED)

| Entity | WebServices default (key) | CustomApi default (key) | Canonical fields | Applied to QMS |
|---|---|---|---|---|
| ProductionOrder (released) | `ReleasedProductionOrders` (`No`) | `productionOrders` (`id`), filter `status eq 'Released'` | number, item, quantity, dueOn, status, location | Work order (created/updated, status mapped) |
| ProductionOrderLine | `ReleasedProdOrderLines` (`Prod_Order_No,Line_No`) | `productionOrderLines` (`id`) | order, lineNumber, item, quantity, finishedQuantity, remainingQuantity, scrapPercent | Work order completed quantity = sum of finished quantity over the order's lines; scrapped quantity when a `scrapQuantity` field is mapped |
| Routing | `ProdOrderRouting` (`Prod_Order_No,Routing_Reference_No,Operation_No`) | `productionOrderRoutingLines` | order, operation, type, resource, setupTime, runTime, status | Review only |
| WorkCenter | `WorkCenters` (`No`) | `workCenters` | code, name | Work center at the connection's site |
| MachineCenter | `MachineCenters` (`No`) | `machineCenters` | code, name, workCenter | Work center at the connection's site |
| ItemLedgerEntry | `ItemLedgerEntries` (`Entry_No`) | `itemLedgerEntries` (`entryNumber`) | entryNumber, item, type, quantity, lot, serial, order, postedOn | Review only |
| CapacityLedgerEntry | `CapacityLedgerEntries` (`Entry_No`) | `capacityLedgerEntries` | entryNumber, order, operation, workCenter, outputQuantity, scrapQuantity, postedOn | Review only |
| OutputJournalLine | `OutputJournal` (`Journal_Template_Name,Journal_Batch_Name,Line_No`) | `outputJournalLines` | order, operation, item, outputQuantity, scrapQuantity, postingDate | Review only |
| ConsumptionJournalLine | `ConsumptionJournal` (same key) | `consumptionJournalLines` | order, item, quantity, lot, serial, postingDate | Review only |
| Lot | `ItemLedgerEntries`, filter `Lot_No ne ''` | `itemLedgerEntries`, filter `lotNumber ne ''` | number, item, expiresOn | Lot (part must exist) |
| Serial | `ItemLedgerEntries`, filter `Serial_No ne ''` | `itemLedgerEntries`, filter `serialNumber ne ''` | number, item, lot | Serial unit (part must exist) |

"Review only" rows are read into the change queue so they can be inspected and reconciled, but posted ERP history is never written into QMS records. Their changes are marked with the reason.

Web service page names and OData field names come from the page and its control names, which differ between BC versions and customizations. Publish the pages under the default names or set `entitySet.<Entity>` to your names.

## Connection test diagnostics

**Test connection** gets a token, then reads one row from every entity in `entities` and reports per entity:

- **OK, all mapped fields present**, or **OK, unverified fields (not in the sample row): canonical←bcField, …**. Correct those field maps, or accept them if the field is simply blank in that row.
- **FAILED, not published** (404 on `WebServices`) or **not found: extension API page not installed or publisher/group/version differ** (404 on `CustomApi`).
- **FAILED, the Entra application lacks a permission set that reads this table** (401/403).
- **FAILED, the filter or a field name is not valid for this page** (400).
- A missing key field fails the entity.

The test passes only when authentication works and every listed entity answers with its key. Connections are created **disabled** and can be enabled only after a passing test of the current configuration; changing the configuration disables the connection again (same validation gates as every ERP connection).

## Required Business Central permissions (assumption)

Grant the Entra application (Microsoft Entra Applications page) a permission set with **read** access to: Production Order, Prod. Order Line, Prod. Order Routing Line, Work Center, Machine Center, Item Ledger Entry, Capacity Ledger Entry, Item Journal Line (output and consumption journals), Item, and Lot/Serial No. Information if mapped. The adapter never writes. Confirm the exact permission set names in the customer's BC version.

## Assumptions to confirm (shown on the connection's validation page)

- **Extension** (`CustomApi`): the extension, its publisher, group, version and entity set names.
- **Published pages** (`WebServices`): service names.
- **Field names:** every mapped field.
- **Keys:** composite keys of lines and journals against `$metadata`.
