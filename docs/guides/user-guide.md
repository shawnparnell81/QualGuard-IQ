# User guide

## Finding your way

- The **teal bar** across the top lists the modules you have access to (Documents, Quality Events, Audits, Training,
  Equipment, Suppliers, Inspection and so on). Modules you have no permission for are not shown.
- Inside a module, the **side menu** lists its areas (for example *Nonconformances*, *CAPA*, *Dashboards*), and each
  record opens with **tabs** (details, actions, attachments, links, approvals, history).
- **My work** (Platform › My workspace) collects everything waiting for you: approvals, actions, training, reviews.
- Global search finds records by number, title and text across every module you can see.

Dates such as "due" and "overdue" follow the time zone of the record's site.

## Working a record

1. Create the record from the module's side menu. A number is assigned on save (for example `NCR-000123`).
2. Fill in the fields. Required fields are marked; some fields appear or become required only when another field has
   a certain value (for example a *Containment detail* field when *Containment required* is *Yes*).
3. Add attachments, comments, linked records and actions from the record's tabs.
4. Use the buttons at the top of the record to move it forward. A button appears only when you are allowed to take that
   step and the record is ready for it.

## Approvals and electronic signatures

- When a record is submitted, the approval route for that record type decides who signs. Routes can have several
  **stages**; all steps in a stage are open at the same time and the next stage opens when they are all signed.
- Some steps apply only to certain records (for example a *Quality manager* step only for Critical NCRs). The approval
  tab shows exactly which steps apply to the record.
- To sign, open the record, choose **Approve** or **Reject**, and re-enter your password (and MFA code where
  required). Your signature records your name, the date and time, and the meaning (for example *Approved*).
- You cannot approve a record you submitted when the route prevents self-approval.
- Steps that wait too long are escalated to the people named on the route; you will receive a notification.

## Controlled forms

Forms are filled in from Documents › Forms. Only effective form revisions can be filled. Calculated fields, checks
(for example *measured value must not exceed the upper limit*) and conditional sections are applied when you save;
if a check fails, the message tells you which field to correct.

## Master data change requests

If you hold *master data edit*, open Platform › Data governance, pick the domain (Part, Product, Supplier, Customer,
Site, Equipment) and choose **Request change** or **Propose fix** on a data quality violation. The change is applied only
after the data steward approves it; the master record's history shows the old and new values.

## Files, drawings and videos

- Attach files on a record's attachments tab. Choose a classification first: *Restricted* files can be opened only by
  people who may edit the record, and every opening of a *Confidential* or *Restricted* file is logged.
- **New version** replaces a file while keeping every earlier version; the version history shows each one with its
  checksum.
- Videos (MP4, M4V, WebM, MOV; up to 1 GB by default) play directly in the browser. MP4 (H.264) and WebM play
  everywhere; MOV plays only in some browsers.
- Documents › **Content library** lists every drawing, document, image, video and attachment you can see across all
  records, with search, version history, retention and classification.
- Files under retention cannot be removed before their retention date.

## Notifications

Notifications appear under the bell icon and, when email is configured, by email. They link straight to the record.

## MSA bias study calculation (calculation version bias-t/2)
- The bias is the mean of the readings minus the reference value. Significance is a two-sided one-sample Student t-test with n − 1 degrees of freedom at the study's confidence level (default 95%, configurable 80%–99.9% when the study is created).
- The result shows the inputs (n, mean, reference, standard deviation), the standard error, the t statistic, the exact critical t, the p-value and the confidence interval for the bias. Quantiles and p-values come from MathNet.Numerics; they are checked in the test suite against values computed independently with SciPy.
- Fewer than 2 readings: no result. Fewer than 10: the result carries a note (AIAG recommends at least 10). All readings identical: t is undefined; any non-zero bias is treated as significant.
- Results frozen before this version (calculation "msa/1", which used a five-step approximation of the critical t) are kept unchanged as the result of record. "Recalculate" adds a new result version beside the original; it never changes what was approved.

## Record retention policies (filters, priority, dry run)
Governance → Retention. A policy can be narrowed by record type, module, product family (code or name), regulatory category (set on the product family), site, department, customer, supplier and confidentiality; empty filters match everything, filled filters must all match.
- **Which policy applies:** the lowest priority number wins; at equal priority the policy with more filters wins. If two equally ranked policies disagree, the record is flagged as a conflict, the longest retention is kept and the action becomes *Review*; disposal cannot be approved until the conflict is resolved (change a priority).
- **Legal hold** always overrides: held records are never offered for disposal.
- **Dry run** (tab) evaluates up to 5,000 records at a time and explains for each record which policies matched and why one governs it. Nothing is changed or deleted; each run is logged (security log event `RetentionDryRun`).
- **Versions:** every saved change to a policy's criteria creates a new version. *Reproduce* on a version re-runs that version's criteria, and every decision records the policy version it was taken under.
- **Confidentiality** of a record is set with `PUT /api/governance/retention/classification` (records without a classification are Internal).
- The record's initial file retention date (content library) uses the same resolution.

## Content library search and paging
Documents → Content library now searches on the server with no 1,000-file limit: 50 files per page with Previous/Next, a total count, sort (newest, oldest, name, largest; ties broken by id so pages never overlap) and filters for kind, classification, retention state and date added. The API (`GET /api/files/search`) also filters by content type, uploader, site, department and owning record type. Visibility follows the owning record's view permission, site and department, applied in the database from values copied onto each file at upload; files uploaded before this change are back-filled at startup. Every file shown is still re-checked against its record; if a record's access changed since upload, the page says some files are hidden and the total may be slightly high.

## Importing PFMEA lines from a spreadsheet

Open a PFMEA and choose **Import lines**. Download the Excel template (it has a Help sheet describing every column) or use your own
.xlsx or .csv file. Old .xls files must be saved as .xlsx first.

1. Choose the file and whether to **add** the rows to the existing lines or **replace** all lines of the draft.
2. **Preview** runs a dry run: nothing is written. Columns are matched by name (for example "Potential failure mode", "Sev", "Occ",
   "Det"); change any field's column in the mapping panel. Required fields: Step, Process function, Failure mode, Failure effect, S,
   Failure cause, O, D.
3. Each row is shown as Ready, Rejected (with the reasons) or Duplicate (same step, failure mode and cause as an earlier row or an
   existing line). Ratings must be whole numbers 1–10; a recommended action needs a responsible person (email or name of an active
   user) and a due date; revised ratings need a completion date; special characteristics must be one of your organization's codes
   (default CC, SC, KPC, KCC, HI, OS, YC, YS; set `Apqp:SpecialCharacteristicCodes` to change them); when the PFMEA is linked to a
   process flow, the step must be one of its steps.
4. RPN (S×O×D) and action priority are always recalculated. If the file has its own RPN or AP column, a different value is shown as a
   note and the calculated value is used. Formula cells are read as their last calculated value.
5. **Import** writes the ready rows in one transaction. While any row is rejected nothing is imported, unless you switch on
   "Import the valid rows and leave the rejected ones out". **Download rejected rows** gives a CSV of the rows that were left out with
   their errors, ready to fix and import again.

A released PFMEA never changes. Importing into it asks for a change reason and creates the next draft revision (with the existing lines
copied) and adds the imported lines to it in the same step; if the import is refused, no revision is created. Every import and preview
is listed under **Line imports** with the file name, mode, revision and counts, and committed imports are written to the audit trail
with the file's SHA-256 fingerprint.

## Importing control plan lines from a spreadsheet

Open a control plan and choose **Import lines**. It works like the PFMEA import above (template with a Help sheet, .xlsx or .csv,
column mapping, dry-run preview, add or replace, all-or-nothing unless you import only the valid rows, rejected-row download,
released plans revised in the same step, history and audit).

- Required fields: Step, Process step, Measurement method, Sample size, Frequency, Reaction plan, and a **product characteristic** or a
  **process characteristic** (or both). A line with a product characteristic is a Product line and keeps the process characteristic
  next to it; a line with only a process characteristic is a Process line.
- Optional fields: Operation, Machine / tool, Special characteristic (your organization's codes), Specification, Tolerance, Inspection
  equipment (gauge), Control method, Owner (email or name of an active user).
- **Related PFMEA item** links the line to a line of the plan's linked PFMEA by its failure mode. If two PFMEA lines share the failure
  mode, write it as `step: failure mode` (for example `20: Bore oversize`). The plan must be linked to a PFMEA first.
- **Related inspection plan** is the inspection plan number; **Related work instruction** is the controlled document number (obsolete
  documents are refused).
- A row is a Duplicate when the plan (or an earlier row) already has the same step and characteristic.

The new columns (operation, process characteristic, tolerance, inspection equipment, owner) can also be edited in the plan's line editor.

## Customer and supplier portals

Portal accounts are created by a portal administrator (**Admin → Portal accounts**) for one customer or one supplier. A portal account
signs in on the normal sign-in page and sees only the portal of its kind:

- **Customer portal**: the customer's own complaints (number, your reference, dates, status), our response and root cause once the
  response has been sent, the linked corrective actions (number, title and status only, also only after the response), messages and
  evidence files; and a twelve-month quality report.
- **Supplier portal**: the supplier's own corrective action requests (SCARs) and nonconformances, the SCAR response form while the SCAR
  is waiting for the supplier (issued, or returned after a rejected answer), messages and evidence files, the supplier's scorecard and
  recent inspected lots, and audits linked to the supplier once they are in reporting or closed, with their findings.

Every portal page has its own address (`/portal/complaints/<id>`, `/portal/scars/<id>`, `/portal/scorecard`, …) so links can be
bookmarked. A link to another party's record shows "This record was not found, or it is not shared with your organization", and the
API answers 404 for it, so its existence is not revealed. Portal accounts cannot call any staff API (403). Evidence uploaded by one
contact is visible to the other contacts of the same customer or supplier; files uploaded by staff are not shown in the portal.
**Sign out** ends the session and returns to the sign-in page; a deactivated account loses access immediately.

## Receiving against a purchase order at the scan station

**Warehouse → Scan station → Receive PO** walks a receiver through one PO line at a time on a phone or handheld:

1. Scan the receiving dock location.
2. Scan the PO label (`QG:PO:<number>`) or type the PO number. The open lines show the open quantity, the maximum with the
   over-receipt tolerance, and what receiving must do for the part (inspection, AQL, receiving hold, serial per unit).
3. Select the line, then scan the item. A part that is not the part of the line is refused at once.
4. Check the revision (prefilled from the PO line), enter the supplier lot or heat, scan serial numbers (a serial scanned twice is
   refused; with serials the quantity is the number of serials), and the quantity.
5. **Check** runs the same server checks the receipt applies: part, revision (must be a revision of the part and match the PO),
   open quantity and tolerance, partial receipt, duplicate serials (in the scan, on the receipt and already in stock) and the part's
   receiving requirement. Errors block the receipt; warnings (partial receipt, over the ordered quantity within tolerance) are shown and
   the button reads **Receive with warnings**.
6. After posting, the result shows the lot, whether the line is accepted to stock or awaiting inspection, the inspection and AQL
   records and the receiving hold, and a **Labels** button that opens the printable lot and serial labels.

If the supplier is restricted (for example suspended), the scan station stops and the receipt is made on the desktop receiving page,
where an authorized override with a reason is recorded. Without a connection the receipt is queued on the device and checked by the
server when it is sent.

**Blind receipts** (no PO, delivery or ASN) are a separate flow with its own permission, `inventory.receive_blind`. Receivers without it
do not see the flow, cannot choose the Blind type on the desktop, and the API refuses the receipt (403). On upgrade the permission was
granted to every role that already had `inventory.edit`, so nobody lost access; remove it from roles that should receive against POs only.

**Receiving rules** (Warehouse → Receiving → Receiving rules, edited with `inventory.approve`):

- *Tolerances* per site (or the organization default): over-receipt up to the larger of a percentage and a number of units above the
  ordered quantity, on the receipt that completes the line; and whether partial receipts are allowed (otherwise a line is received in full).
- *Part requirements* (optionally for one supplier; a supplier-specific requirement wins): receiving inspection required (it cannot be
  skipped on any channel), the inspection plan (otherwise the part's effective receiving plan), AQL sampling plan, inspection level and AQL,
  a receiving hold with its reason (blocks issue and shipment until released through the hold approval), serial number per unit, and lot
  labels per line. Retired requirements stay linked to the receipt lines that applied them.

## Picking on a device, with or without a connection

A shipment's pick list is assigned to one picker on the shipment's **Pick** tab (**Assigned picker**; only people who may edit shipments
at the shipment's site are offered). While a picker is assigned, only that person can confirm the picks, at the desk or on a device.

**Warehouse → Scan station → Pick** (needs `shipping.edit`):

1. **Download my picks** while connected. Only open picks assigned to you, on shipments that are still being picked and that you can
   see, are downloaded: location, part, revision, lot or serial, and quantity. Nothing else about the shipment is kept on the device.
2. Choose a shipment and follow the next pick: scan the location, scan the lot or serial label, check the quantity and **Confirm pick**.
   A wrong location, part, lot or serial is refused on the device at once; a label the device cannot read (for example a supplier GS1 label)
   is accepted with a note and checked by the server.
3. Online, the pick posts at once. Offline, it is saved on the device with a request id and the device time, shown under *Saved locally*,
   and sent when the connection returns. A pick saved on the device is not offered again.

When the queued picks reach the server they are checked again. A replay of a pick that was already accepted (same request id) returns the
original result and posts nothing twice. A pick is refused as a **conflict** and shown under *Conflict requires review*, with the reason,
when meanwhile the shipment was cancelled or completed, the pick was confirmed by someone else, the pick list was reassigned or
unassigned, the lot or part was put on hold, the location was frozen for a count, or the allocated stock is no longer there. Put the
material back and mark the conflict reviewed.

Every confirmation, accepted or refused, is listed on the shipment's Pick tab with the picker, the device time, the server time, whether it
came from an offline device and the refusal code. Each accepted pick also adds a custody entry and an inventory ledger movement.

Device storage: the download is kept per person, expires after 12 hours (refresh it before picking again), and is wiped when you sign out.
On a shared device each person only sees and sends their own saved scans; another person's saved scans wait until they sign in again.
A device clock more than 10 minutes ahead of the server is refused, so correct the device time if that message appears.

## Verifying shipments on a device, with or without a connection

A shipment's verification is assigned on its **Verify** tab (**Assigned verifier**; only people who may edit shipments at the shipment's
site are offered). While someone is assigned, only that person can record verification scans.

**Warehouse → Scan station → Verify shipment** (needs `shipping.edit`):

1. **Download my verifications** while connected. Packed shipments assigned to you are downloaded with their lines (part, revision,
   required lot, quantity) and packing tree (pallets, cartons, the lots and serials in each). Nothing else about the shipment is kept.
2. Choose a shipment and scan cartons, pallets (a pallet counts everything on it), lot labels (enter a quantity for a loose partial lot)
   or serial labels. A duplicate scan, a carton already counted inside a scanned pallet, a lot or serial that was not packed, the wrong
   product, the wrong revision or more than ordered is refused on the device at once.
3. Online, each scan posts at once. Offline, it is saved on the device with a request id and the device time and sent when the
   connection returns; a replay is never counted twice.

Offline scans only verify contents. When every line is complete the shipment shows **Pending validation**: it stays Packed and cannot
ship. Once connected, choose **Confirm release conditions online**. The server checks, at that moment, the shipment status, that every line
is verified, that the packed stock is still staged, that no hold covers the material, that the packed revisions match the lines, and that
the customer is active and required lots are shipped. If any check fails the shipment stays pending validation and the failed checks are
listed. When all pass the shipment becomes Verified. Shipping re-runs the same checks, so a hold placed after verification still stops it.

Scans made online that complete the contents confirm the release conditions straight away. Scans that reach the server after the
shipment was cancelled, shipped or reassigned are refused as conflicts and listed under *Conflict requires review*. **Restart
verification** stops the scans so far from counting (they stay in the log) and starts again.

Every scan, accepted or refused, is listed on the shipment's Verify tab with the person, device time, server time, the offline flag and the
reason for a refusal; the online confirmation adds a custody entry. Cancelling a shipment that has staged (picked) stock asks for the
location to return it to, and the stock goes back there as available.

Device storage: as for picking, the download is per person, expires after 12 hours and is wiped at sign-out.

## Label printing (printers, routing, print queue)

**Warehouse → Label printing** lists the print queue, printers and routing of a site. Configuring printers and routes needs
`inventory.approve` at the site; printing, retrying, reprinting and cancelling need `inventory.edit` or `shipping.edit`; viewing needs
`inventory.view` or `shipping.view`.

**Printers.** Each printer belongs to one site and has a connection, a label language, a resolution (152, 203, 300 or 600 dpi) and a
label size in millimetres:

- *Network printer (raw TCP)*: a ZPL printer on the network, addressed as `host` or `host:port` (port 9100 by default).
- *Print relay (HTTPS)*: an HTTPS endpoint on the plant network that accepts the label (ZPL or PDF) and passes it to a printer. An optional
  bearer token is stored encrypted and never shown again; plain HTTP is accepted only for a relay on the same machine.
- *PDF download only*: nothing is sent; labels are prepared as PDFs to print by hand.
- *Virtual (test)*: accepts labels without printing; use it to try routing before printers are connected.

**Print test label** sends a label and records the printer status (Online, Offline, Error) and the last error. The printers list shows how
many jobs are waiting or failed on each printer.

**Routing.** A route sends one label type (receiving, inventory, bin, lot, serial, carton, pallet, shipping, hold, quarantine, scrap) to a
printer, for the whole site or narrowed to a warehouse, zone or bin, a workstation or a user. The most specific active route wins: a
user's own route, then a workstation, then the deepest warehouse, zone or bin above the label's location, then the site default.
*Which printer would be used?* shows the route that applies. Each route sets the copies, whether it prints automatically, and the mode:
*Print immediately* or *Hold in queue* (automatic labels wait in the queue until someone releases them).

**Workstation.** On **This workstation**, give the device the name used in routes (for example `DOCK-3`). The name stays on that
device and is sent with every request.

**Automatic labels.** When a route prints automatically, labels are printed after the transaction is saved: receipt, lot and serial
labels when a receipt is posted, carton and pallet labels when a container is packed, the shipping label when a shipment ships, a hold
label when a hold is placed, a quarantine label when a receiving line is rejected or quarantined, and a scrap label when stock is scrapped.
A printer that is offline or fails never undoes or blocks the transaction: the job is marked Failed with the reason and retried in the
background after 1, 2, 5 and 15 minutes (five attempts). A receipt sent again by the offline queue does not print its labels twice.

**Record pages.** The Labels tab (receipt, hold), the Label tab (location), the Pack tab (each carton and pallet) and the Ship &
documents tab (shipment) show every label of the record with its status, printer, copies, who asked for it and the last error, plus
**Preview** and **Print** buttons. Print uses the routed printer unless you pick one; with no printer the label is prepared as a PDF.
After a scan-station receipt the result shows the label status.

**Print queue actions.** *Retry* sends a failed job again; *Release* sends a held job; *Reprint* prints the label again with exactly the
original content and needs a reason, which is recorded on the job and in the audit trail; *PDF* downloads the label at the printer's
label size (4 × 6 in when there is no printer); *Cancel* stops a job that has not printed. A disabled printer is not retried.
