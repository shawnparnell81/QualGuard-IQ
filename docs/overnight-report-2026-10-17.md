# QualGuard IQ: overnight report, Saturday 17 October 2026

Good morning, Shawn. This is what got done overnight, prompt by prompt, in the run order we agreed. Everything is committed in the local git repository at `/workspace/mqms`. Nothing has been pushed to GitHub.

Screenshots are in `/workspace/mqms-screenshots`, numbered in build order. Number ranges for each prompt are listed below.

## Where things stand

| Prompt | Status | Backend tests | Browser tests | Screenshots |
|---|---|---|---|---|
| 2 + FAI/AQL addendum (gate) | Done | 87/87 at the gate | 14/14 after gap fixes | 53–62 |
| 4: Advanced quality | Done | 93/93 | 14/14 | 63–75 |
| 5: Integrations, shop floor, traceability | Done | 129/129 | 18/18 | 76–96 |
| 10: Warehouse, shipping, scanning | Done | 139/139 | 22/22 | 97–116 |
| 6: Enterprise governance | Done | 147/147 | 28/28 | 117–126 |
| 7: Engineering and intelligence | Done | 155/155 | 35/35 | 127–142 |
| 8: Platform services | Done | 167/167 | 42/42 | 143–167 |

Each prompt's tests were run on top of the previous ones, so the counts grow as you go down the table. The final full run this morning passed 167 of 167 backend tests and 42 of 42 browser tests. Tests run only against local test databases.

QualGuard IQ keeps its own look throughout. There is a teal top module menu on the home page, and each module has its own side menu and tabs. No prior product layouts, colours, fonts or components are used. Every prompt's checkpoint entry has a note on how its screens differ from prior product.

---

## Prompt 2 + FAI/AQL addendum (the gate)

**What was built**
- First Article Inspection (FAI) and AQL sampling, built to match your reference OpenAPI contract. The contract test now reports zero gaps.
- A First Article & AQL workspace with:
  - FAI list, new and detail pages: characteristics table, balloon board, signatures, drawings and revisions, files and history.
  - AQL lots with a sample preview and threshold cards.
  - Sampling plans, supplier trends and a dashboard.
- Approval decisions ask for the user's password again, so e-signatures re-authenticate.
- Bearer-token sign-in for API clients, alongside the normal browser sign-in.
- The OpenAPI spec is generated from the code, and a test fails if the committed copy is out of date.
- Gap fixes after the gate:
  - Excel export works.
  - Link due dates are saved.
  - The requested approver and due date are saved on approval requests.
  - Approvers can return an FAI using their approval permission, with no new roles.
  - The FAI product is filled in from the selected part.

**Tests:** all 10 FAI/AQL scenarios pass. Backend 87/87 at the gate. Browser tests 14/14 after the gap fixes.

**Flagged / unverified:** none specific to this prompt.

**Known gaps:** none open. All five gaps found at the gate were fixed.

**Screenshots:** 53–62.

---

## Prompt 4: Advanced quality

**What was built**
- New top-menu modules:
  - **Process Planning:** revision-controlled process flow, PFMEA and control plan, with RPN and action priority.
  - **SPC & MSA:** seven chart types, Western Electric rules, Cp/Cpk/Pp/Ppk and Gage R&R.
  - **Layered Audits.**
  - **Cost of Quality.**
  - **Insights:** production and executive metrics that click through to the records behind them, plus a relationship map.
- Supplier development scorecards.
- Customer and supplier portals in their own slim shell. Portal users can see only their own data.
- 10 new reports with CSV, Excel and PDF export.

**Tests:** 6 of 6 scenarios. Backend 93/93. Browser tests 14/14.

**Flagged / unverified**
- The PFMEA action priority table was first entered from memory. It was later checked against the published AIAG-VDA table and **all 25 rule rows match**, so this flag is closed.
- The bias t-test critical values are approximations.
- Scrap and rework rates use inspected quantity as the denominator, because there is no production-volume source yet.

**Known gaps**
- Layered-audit template and cost-of-quality dialogs take a raw site ID instead of a site picker.
- The PFMEA and control plan grids are edit-in-place only, with no bulk import.
- The portal screens have no browser test yet.

**Screenshots:** 63–75.

---

## Prompt 5: Integrations, shop floor, traceability, field quality

**What was built**
- **All 9 ERP connectors**, plus a generic REST connector and a CSV connector. Every connector ships **turned off**. A connector can only be turned on after its connection test passes, so customers decide after testing.
- New top-menu modules:
  - **Shop Floor:** live monitor, work and purchase orders, and a touch entry form for operators with a scan box.
  - **Traceability:** lots, serials, genealogy lanes, and labels in Code 128, Code 39, Data Matrix and QR. Scanning works with keyboard-wedge scanners and phone or tablet cameras.
  - **Field Quality:** recalls, returns (RMA) and warranty.
  - **Integrations:** connection cards, write-only secrets, field mappings, and a change queue with field-by-field differences.
- Insights gained a KPI library, a quality assistant, investigation, trends and risk, and datasets.
- Setup guide: `docs/setup/erp-connections.md`.

**Tests:** 6 of 6 scenarios. Backend 129/129, including 18 connector fixture tests. Browser tests 18/18. An earlier note said 21; the correct figure at that point was 18.

**Flagged / unverified**
- Every connector is built from the vendor documentation and tested against sample responses shaped like that documentation. **None has been tested against a live ERP system.**
- Plex and DELMIAworks are the least certain.
- Each connection's **Setup notes** panel lists the items that are not yet confirmed, as does the "UNVERIFIED items" section of the setup guide. Examples:
  - NetSuite transaction codes.
  - SAP entity names.
  - The Oracle Fusion pod host.

**Known gaps**
- The assistant is rule-based and uses no external text-generation service.
- MES push uses a user token rather than a device key.
- The Dynamics Business Central manufacturing extension is not shipped.

**Screenshots:** 76–96.

---

## Prompt 10: Inventory, warehouse, shipping, barcode and scanning

**What was built**
- **Warehouse module:**
  - An append-only inventory ledger that every page reads from.
  - A location tree with fill bars and printable labels.
  - Receiving linked to inspection, AQL, NCR and holds.
  - Holds, with e-signed release.
  - MRB, returns to vendor with a custody trail, and cycle and physical counts with freeze and variance approval.
  - Transfers, lot split and merge, and WIP.
- **Shipping & Logistics module:**
  - Pick, pack (nested packing), scan verification and ship.
  - Packing list and bill of lading PDFs.
  - Carriers and logistics reports.
- **Mobile scan station:**
  - A phone-sized, one-column screen for receive, inspect, move, issue, ship and trace.
  - Receive, move and issue keep working offline and post when the connection comes back. Nothing posts twice.
- ERP mirror mode for sites where the ERP is the inventory system of record.

**Tests:** 6 of 6 scenarios. Backend 139/139. Browser tests 22/22, including the offline-then-reconnect test and the frozen-count test.

**Flagged / unverified:** none new. The action priority table check described under Prompt 4 was done during this stretch.

**Known gaps**
- Pick and verify need a connection. Only receive, move and issue work offline.
- Receiving at the scan station is a blind receipt. Receiving against a PO is on the desktop page.
- There are no carrier rate or label APIs. Tracking is a link template.
- Labels are not auto-printed.
- There is no template editor for the BOL or packing list.
- The "saved on this device" banner stays up after the queue posts.

**Screenshots:** 97–116.

---

## Prompt 6: Enterprise governance

**What was built**
- **Enterprise:** plant scorecards, a war room, the org tree, corporate standards and annual audit programs.
- **Compliance:** compliance packs, obligations and coverage, and the validation lifecycle. Packs carry clause numbers and short titles only, with no standard text.
- **Launch:** APQP projects with e-signed gates, a PPAP element card wall, and capability.
- **Governance:** Part 11 readiness, retention, business continuity and API access.
- Training gained a skills matrix and curricula.
- An audit finding can now raise a CAPA directly.

**Tests:** 6 of 6 scenarios. Backend 147/147. Browser tests 28/28.

**Bugs found and fixed**
- A training service loop that could hang requests.
- Skill codes are now unique per site.
- Gate reviewers no longer need edit rights.
- The war room is now site-scoped.

**Flagged / unverified**
- The PPAP level matrix and the IATF 16949 and AS9100 clause lists were entered from memory and need checking.
- FDA 820 uses the new QMSR sections.
- SMS and push notifications go through generic HTTPS webhooks.

**Known gaps**
- Retention filters by product family and regulatory category are stored but not applied yet.
- Document library types are not seeded.
- The dynamic form gap noted here was closed in Prompt 8.

**Screenshots:** 117–126.

---

## Prompt 7: Engineering, intelligence and knowledge

**What was built**
- **Engineering:**
  - Requirements register and traceability matrix.
  - A design-review gate board.
  - EBOM and MBOM quality with history, and variants.
  - DFMEA uses the same FMEA engine.
- **Quality OS:** command center, factory view, a 5×5 enterprise risk heat map, KPI framework, digital twin (from real SPC data only) and board reports.
- **Knowledge:** card library, a similar-issue finder, 5 Why and fishbone, playbooks, process chain and maturity ratings.
- **Advisor:** ask and explain, a document assistant and audit preparation. It drafts but never approves. Management-review drafts need a person to approve them.
- **Governance** gained access reviews and deployments.

**Tests:** 6 of 6 scenarios. Backend 155/155. Browser tests 35/35.

**Bugs found and fixed**
- Number allocation outside a transaction.
- A part could not have both an EBOM and an MBOM line.
- The similar-issue finder returned nothing on small data sets.

**Flagged / unverified**
- The advisor is rule-based. No outside AI service is used.
- Supplier-risk factors with no data are left out rather than guessed.
- A new NCR never counts as overdue, because its due date is set later in the workflow.

**Known gaps**
- The document assistant works on pasted text only.
- Board-report distribution by schedule was waiting on Prompt 8's scheduler, which now exists. Hooking the two together is still open.

**Screenshots:** 127–142.

---

## Prompt 8: Platform services

**What was built**

The Platform module covers:
- My workspace, activity stream, calendar, resources and genealogy.
- Report and dashboard designers.
- **Workflow designer:** parallel stages, conditional steps (decision trees) and escalation, built on the existing approval engine.
- **Form designer:** show/require-when rules, validation checks, a versioned component library and a live preview.
- **Business rule engine:** validation, routing, escalation and automation rules, written as WHEN … THEN … sentences, with a dry run and a firing log.
- **Data governance and master data:** stewards, data quality rules with compliance tracking, and governed changes that go through approval before they touch the master record.
- **Recovery center:**
  - A backup ledger, restore-test results, and disaster scenarios with recovery time and recovery point targets.
  - Backup, verify and restore scripts. A local trial run passed, a tampered file failed, and restore refuses to run without `--confirm`.
  - User, administrator, deployment, recovery, API and architecture guides in `docs/guides`.
- **Extensions marketplace:** add-ons and industry packs with dependency checks and signed licence keys.
- **Content library:** video on the existing file engine, with seeking, file classification and retention.
- Webhooks (outbound only, HMAC-signed), system health, security operations and the module registry.

**Tests:** backend 167/167. Browser tests 42/42, including 4 new ones this morning for the designers, recovery center, marketplace and video.

**Fixed this morning:** the page breadcrumb no longer repeats the module name ("Platform / Platform").

**Flagged / unverified**
- Scheduled reports send a link, not the data.
- **There is no licence signing key yet.** One must be created and configured before extensions are sold, and the private key kept offline.
- Restore checks are recorded from the script result, because the app cannot read backup files.
- There is no video conversion. MP4 or WebM is recommended, because MOV plays only in some browsers.

**Known gaps**
- The workflow designer's tabs show internal names (for example "ChangeRequest", "Fai") instead of friendly labels.
- The content library searches the 1,000 most recent matching files.

**Screenshots**
- 143–151 and 152–158: from the browser tests.
- 159–167: the new Platform pages on the dev system.

---

## What's next

1. **Prompt 3: first hardening pass.** Not started yet, per the plan.
2. **Prompt 9: final certification**, combined with Prompt 3 where they overlap. **Prompt 9's text is incomplete. Please send the rest** before it starts. As agreed, the 5,000-user load test will be partly simulated and projected, and labelled that way.

## Things that need your input

- Send the rest of Prompt 9.
- Say when to push to `shawnparnell81/QualGuard-IQ`. Nothing has been pushed.
- Confirm or correct the from-memory items: the PPAP level matrix and the IATF/AS9100 clause lists.
- Decide who will hold the extension licence signing key.
- Arrange a live test for each ERP connector, or decide which ones a customer will test first. All stay off until then.
