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
