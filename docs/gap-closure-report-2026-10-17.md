# QualGuard IQ: gap-closure report, 17 October 2026

This report covers the 28 gaps from the overnight report (`docs/requirements/gap-closure-prompt.md`), the repository-wide sweep that followed, and the full regression run. All work is committed locally in `/workspace/mqms`; nothing has been pushed.

## Summary

- **Backend tests:** 267/267 passed (final run, 3 min 26 s).
- **Browser tests:** 72/72 passed (25 spec files, final run 7.6 min).
- **Production builds:** the web build (`npm run build`), and Release publishes of the API and the worker, all succeed.
- **Fixed now:** gaps 1, 2, 3, 4, 5, 6, 7, 8, 10, 11, 12, 15, 16, 17, 18, 19, 20, 21, 22 and 23.
- **Framework complete, but live use waits on something outside the code:** gaps 9, 13, 14, 24, 25, 26, 27 and 28. Sections 2 to 7 explain each one. None of them is labelled completed.
- **Baseline before the gap work:** 167/167 backend and 42/42 browser tests. Every original test still passes and none was weakened or removed.

## Gap details

Each entry lists the original gap, the fix, the commits, the migration, the tests and their result, the screenshots, any remaining limitation, any external dependency, and the verification status. Commit hashes refer to the local repository. To see the files changed for a gap, run `git show --stat <commit>`.

### Gap 1: Layered audit site picker

- **Original gap:** The LPA template dialog asked for a raw site ID.
- **Fix implemented:** Shared SitePicker (searchable by name or code, keyboard navigation, loading and empty states) fed by GET /api/organization/sites/accessible?permission=, which returns only active sites in the caller's scope for that permission. The server already refused other sites and archived sites; tests now prove it.
- **Commits and files changed:** 25e76c0 (36 files changed).
- **Migration:** None (API and UI only)
- **Tests added:** GapClosureSitePickerTests (403 other site, 400 archived site, 403 moving a template); E2E 14 site picker
- **Test result:** passing in the final full run.
- **Screenshots:** [168](screenshots/gap-closure/168-gap01-lpa-site-picker.png)
- **Remaining limitation:** None
- **External dependency:** None
- **Verification status:** Verified

### Gap 2: Cost of quality site picker

- **Original gap:** The CoQ entry dialog asked for a raw site ID.
- **Fix implemented:** Same shared SitePicker (no duplicate component); site names shown, IDs stored, server-side site authorization unchanged and tested; historical entries untouched.
- **Commits and files changed:** 25e76c0 (36 files changed).
- **Migration:** None
- **Tests added:** GapClosureSitePickerTests; E2E 14 (CoQ part)
- **Test result:** passing in the final full run.
- **Screenshots:** [169](screenshots/gap-closure/169-gap02-coq-site-picker.png)
- **Remaining limitation:** None
- **External dependency:** None
- **Verification status:** Verified

### Gap 3: PFMEA bulk import

- **Original gap:** PFMEA lines could only be typed in one by one.
- **Fix implemented:** Shared tabular import engine (CSV and .xlsx through ClosedXML, 10 MB / 5,000-row limits, auto-mapping with explicit overrides, Excel and CSV templates). Dry run by default, row-level validation reusing the editor's rules, S/O/D checks, RPN and AP always recalculated, duplicate detection against the file and existing lines, all-or-nothing commit (or allow-partial), optional controlled new revision in the same transaction, rejected-row CSV export in the original column order, import history and an audit entry with the file's SHA-256. Released and pending revisions are never written.
- **Commits and files changed:** 5526a61 (17 files changed).
- **Migration:** PfmeaBulkImport
- **Tests added:** GapClosurePfmeaImportTests (3); E2E 14 PFMEA import
- **Test result:** passing in the final full run.
- **Screenshots:** [170](screenshots/gap-closure/170-gap03-pfmea-import.png)
- **Remaining limitation:** None
- **External dependency:** None
- **Verification status:** Verified

### Gap 4: Control plan bulk import

- **Original gap:** Control plan lines could only be entered by hand, and several required columns did not exist.
- **Fix implemented:** Reuses the gap 3 engine. New line fields: operation, process characteristic, tolerance, inspection equipment, owner, work instruction. Links to PFMEA items (by failure mode), inspection plans (by number) and controlled work instructions (obsolete refused). Same revision-control and rollback rules.
- **Commits and files changed:** f2331cd (12 files changed).
- **Migration:** ControlPlanBulkImport
- **Tests added:** GapClosureControlPlanImportTests (3); E2E 14 control plan import
- **Test result:** passing in the final full run.
- **Screenshots:** [171](screenshots/gap-closure/171-gap04-control-plan-import.png)
- **Remaining limitation:** None
- **External dependency:** None
- **Verification status:** Verified

### Gap 5: Portal browser tests

- **Original gap:** Customer and supplier portals had API tests only.
- **Fix implemented:** Portal pages got their own addresses; another party's record shows a not-shared notice (API 404); the other portal's areas show a not-available notice (API 403); SCAR responses lock after submission. Browser tests cover customer, supplier and staff sign-out.
- **Commits and files changed:** dbc4590 (5 files changed).
- **Migration:** None
- **Tests added:** GapClosurePortalTests (API boundaries); E2E 15 (3)
- **Test result:** passing in the final full run.
- **Screenshots:** [172](screenshots/gap-closure/172-gap05-customer-portal-complaint.png), [173](screenshots/gap-closure/173-gap05-customer-portal-not-shared.png), [174](screenshots/gap-closure/174-gap05-supplier-portal-audits.png), [175](screenshots/gap-closure/175-gap05-supplier-portal-scar.png)
- **Remaining limitation:** None
- **External dependency:** None
- **Verification status:** Verified

### Gap 6: Bias t-test

- **Original gap:** The bias study used a small table of approximate critical values.
- **Fix implemented:** MathNet.Numerics Student t: degrees of freedom, configured confidence (0.80 to 0.999), standard error, critical t, p-value, confidence interval and calculation version. Frozen results are never rewritten: recalculation adds a version; the original stays of record. Result versions table on the study page.
- **Commits and files changed:** 9313bfc (14 files changed).
- **Migration:** MsaCalculationVersions
- **Tests added:** GapClosureBiasTests (8, checked against SciPy references); E2E 26 bias study (added in the final pass)
- **Test result:** passing in the final full run.
- **Screenshots:** [176](screenshots/gap-closure/176-gap06-bias-study-result-versions.png)
- **Remaining limitation:** None
- **External dependency:** None
- **Verification status:** Verified

### Gap 7: Scrap and rework denominators

- **Original gap:** Rates silently divided by quantity inspected when no production volume existed.
- **Fix implemented:** Configurable per-site and global denominator order (MES, ERP, produced, work orders completed, approved manual volume; inspected only when configured). The source is stored and shown; fallbacks are labelled; no volume means no value instead of a divide-by-zero. Manual volumes need a second person's approval. Calculation version rate/2.
- **Commits and files changed:** 7cc9ffd (12 files changed).
- **Migration:** RateDenominators
- **Tests added:** GapClosureDenominatorTests; E2E 14 denominator and volumes
- **Test result:** passing in the final full run.
- **Screenshots:** [177](screenshots/gap-closure/177-gap07-denominator-production-volumes.png)
- **Remaining limitation:** None
- **External dependency:** None
- **Verification status:** Verified

### Gap 8: MES device authentication

- **Original gap:** MES events were posted with user tokens.
- **Fix implemented:** Registered MES device clients with scopes, sites, endpoint list, expiry (max 2 years), hashed secrets shown once, rotation with a grace period, permanent revocation, last used, failure logging, rate-limit logging and audit. User tokens keep working until Mes:RequireDeviceClients is switched on.
- **Commits and files changed:** 95da12d (25 files changed).
- **Migration:** MesDeviceAuth
- **Tests added:** GapClosureMesDeviceAuthTests (5); E2E 14 MES devices
- **Test result:** passing in the final full run.
- **Screenshots:** [178](screenshots/gap-closure/178-gap08-mes-devices-active.png), [179](screenshots/gap-closure/179-gap08-mes-devices-revoked.png)
- **Remaining limitation:** None
- **External dependency:** None
- **Verification status:** Verified

### Gap 9: Dynamics BC manufacturing extension adapter

- **Original gap:** No adapter for Business Central manufacturing data.
- **Fix implemented:** New BcManufacturing adapter (generic BC adapter untouched) with Web Services and Custom API editions; configurable entity sets, keys, filters and delta field; connection test probes every listed entity and explains each failure; production orders, lines, machine centers, lots and serials sync; routings, ledgers and journals are review-only. Disabled until a passing test. Required BC permissions and assumptions documented in docs/integrations/dynamics365-bc-manufacturing.md.
- **Commits and files changed:** e5bdefc (12 files changed).
- **Migration:** None (enum values stored as integers)
- **Tests added:** GapClosureBcManufacturingTests (3); ErpAdapterTests, GapClosureErpValidationTests, ManufacturingConnectivityTests; E2E 26 validation tab (added in the final pass)
- **Test result:** passing in the final full run.
- **Screenshots:** [180](screenshots/gap-closure/180-gap09-bc-manufacturing-validation.png)
- **Remaining limitation:** Fixture tests only. Field names, published pages, keys and the extension remain Unverified.
- **External dependency:** A customer Business Central tenant
- **Verification status:** Requires live vendor validation

### Gap 10: Mobile offline picking

- **Original gap:** Picking needed a live connection.
- **Fix implemented:** Assigned pick work downloads per user (12-hour expiry, wiped on sign-out); on-device location, lot and serial checks; device timestamp and request id; queue syncs on reconnect; replays post once; conflicts for cancelled or completed shipments, wrong location, lot or serial, quantity, holds and inventory changed meanwhile; custody entries marked offline.
- **Commits and files changed:** c5a92bf (18 files changed).
- **Migration:** OfflinePicking
- **Tests added:** GapClosureOfflinePickingTests (now 5 with the concurrency tests from the sweep); E2E 17 (2)
- **Test result:** passing in the final full run.
- **Screenshots:** [181](screenshots/gap-closure/181-gap10-offline-picking.png)
- **Remaining limitation:** None
- **External dependency:** None
- **Verification status:** Verified

### Gap 11: Mobile offline shipping verification

- **Original gap:** Verification needed a live connection.
- **Fix implemented:** Assigned verifications download per user; carton, pallet, lot and serial scans offline; duplicates, wrong lots and over-counts stopped; offline results leave the shipment Pending validation; release always re-checks holds, revision, status, staged stock and customer requirements online; ship requires Validated.
- **Commits and files changed:** 1619f2a (17 files changed).
- **Migration:** OfflineShippingVerification
- **Tests added:** GapClosureOfflineVerificationTests (now 3); E2E 18 (2)
- **Test result:** passing in the final full run.
- **Screenshots:** [182](screenshots/gap-closure/182-gap11-offline-verification.png)
- **Remaining limitation:** None
- **External dependency:** None
- **Verification status:** Verified

### Gap 12: PO-based mobile receiving

- **Original gap:** Mobile receiving was blind only.
- **Fix implemented:** Scan the PO, choose the line, scan the item; part, revision, open quantity, over-receipt tolerance, partial-receipt policy, duplicate serials and serial counts checked by one shared routine used by every receipt path; per-part receiving requirements trigger inspection, AQL, holds and labels. Blind receiving needs its own permission, enforced on the server.
- **Commits and files changed:** 3806916 (21 files changed).
- **Migration:** PoReceiving
- **Tests added:** GapClosurePoReceivingTests (2); E2E 16 (2); E2E 9; InventoryWarehouseTests
- **Test result:** passing in the final full run.
- **Screenshots:** [183](screenshots/gap-closure/183-gap12-po-receiving-scan-station.png), [184](screenshots/gap-closure/184-gap12-po-receiving.png)
- **Remaining limitation:** None
- **External dependency:** None
- **Verification status:** Verified

### Gap 13: Carrier provider framework

- **Original gap:** No carrier integration.
- **Fix implemented:** Per-site carrier connections (Fixture, Generic REST, LTL; UPS, FedEx and DHL slots), encrypted secrets, sandbox/production, configuration fingerprint, enable only after a passing test and (production) a second person's approval. Address validation, rating, service selection, idempotent label creation, tracking job, void with reason, retries and a log of every call.
- **Commits and files changed:** 49194ae (17 files changed); acd541d (9 files changed).
- **Migration:** CarrierIntegration
- **Tests added:** GapClosureCarrierTests (5); E2E 20
- **Test result:** passing in the final full run.
- **Screenshots:** [185](screenshots/gap-closure/185-gap13-carrier-connection.png)
- **Remaining limitation:** UPS, FedEx and DHL slots only test the OAuth token; their shipment operations are not implemented and cannot be enabled.
- **External dependency:** Carrier developer accounts and certification
- **Verification status:** Partially fixed; requires live vendor validation

### Gap 14: Label printing and print routing

- **Original gap:** No automatic labels.
- **Fix implemented:** Printers per site (Virtual test driver, raw TCP ZPL, HTTPS relay, manual only), routes by label type, location, workstation and user, automatic labels after the transaction commits (receipt, lot, serial, carton, pallet, ship, hold, quarantine, scrap), queue with retry and back-off, reprint with reason, PDF fallback and ZPL view. A print failure never rolls back the transaction.
- **Commits and files changed:** e1d4cc3 (17 files changed); 750dd96 (12 files changed).
- **Migration:** LabelPrinting
- **Tests added:** GapClosureLabelPrintingTests (4); E2E 19
- **Test result:** passing in the final full run.
- **Screenshots:** [186](screenshots/gap-closure/186-gap14-label-printing.png)
- **Remaining limitation:** No physical printer was available; raw TCP and relay drivers are tested against local fixtures only.
- **External dependency:** Physical label printers
- **Verification status:** Framework verified; blocked by unavailable hardware for live printing

### Gap 15: BOL, packing list and manifest template designer

- **Original gap:** Shipping documents had one fixed layout.
- **Fix implemented:** Version-controlled templates per customer, carrier or site with draft, preview, approval by a second person, effective dates and selection rules; 18 section types including barcodes and QR codes, page X of Y, logo; every issued document keeps its data snapshot, PDF and SHA-256 so it can be reproduced.
- **Commits and files changed:** 8129f7e (15 files changed); 77b93ba (9 files changed).
- **Migration:** ShippingDocumentTemplates
- **Tests added:** GapClosureShippingDocumentTests (2); E2E 21; InventoryWarehouseTests
- **Test result:** passing in the final full run.
- **Screenshots:** [187](screenshots/gap-closure/187-gap15-shipping-document-template.png)
- **Remaining limitation:** Customer-specific templates are for each customer to design.
- **External dependency:** None
- **Verification status:** Verified

### Gap 16: Mobile queue banner

- **Original gap:** The offline banner did not show the sync states.
- **Fix implemented:** States saved locally, pending, synchronizing, synchronized, failed and conflict; items removed only after a 2xx; retry and discard; state survives refresh; one tab sends (Web Locks).
- **Commits and files changed:** 25e76c0 (36 files changed).
- **Migration:** None
- **Tests added:** E2E 9 offline test (pending to synchronized and conflict)
- **Test result:** passing in the final full run.
- **Screenshots:** [188](screenshots/gap-closure/188-gap16-offline-queue-banner.png)
- **Remaining limitation:** None
- **External dependency:** None
- **Verification status:** Verified

### Gap 17: Retention filter enforcement

- **Original gap:** Product family and regulatory category filters were stored but ignored.
- **Fix implemented:** Retention engine applies record type, module, product family, regulatory category, site, department, customer, supplier and confidentiality; priority, then specificity, then conflict (longest retention, disposal blocked); legal holds override; dry run (no disposal); policy versions reproducible.
- **Commits and files changed:** 87bf225 (21 files changed).
- **Migration:** RetentionFilters
- **Tests added:** GapClosureRetentionTests (3); E2E 14 retention dry run
- **Test result:** passing in the final full run.
- **Screenshots:** [189](screenshots/gap-closure/189-gap17-retention-dry-run.png)
- **Remaining limitation:** None
- **External dependency:** None
- **Verification status:** Verified

### Gap 18: Document library type seeding

- **Original gap:** Document types were free text.
- **Fix implemented:** Idempotent seed of the 24 required types plus types already in use; customer types adopted, not duplicated; rename carries to documents; retired types not offered for new documents.
- **Commits and files changed:** 25e76c0 (36 files changed).
- **Migration:** DocumentTypeLibrary
- **Tests added:** GapClosureDocumentTypeTests (clean and populated databases); E2E 14 document types
- **Test result:** passing in the final full run.
- **Screenshots:** [190](screenshots/gap-closure/190-gap18-document-type-library.png)
- **Remaining limitation:** None
- **External dependency:** None
- **Verification status:** Verified

### Gap 19: Board report scheduler

- **Original gap:** Board reports could not be scheduled.
- **Fix implemented:** One distribution engine for saved and board reports: time zones (DST handled), recipient role groups, permission re-check on every run, run as the schedule owner, frozen board editions approved by a second person, retry with back-off, failure notification, history, pause, resume and manual runs.
- **Commits and files changed:** 7856099 (16 files changed); 8c8b2a8 (9 files changed).
- **Migration:** ReportDistribution
- **Tests added:** GapClosureReportDistributionTests (5, shared with 22); E2E 22
- **Test result:** passing in the final full run.
- **Screenshots:** [191](screenshots/gap-closure/191-gap19-report-schedule-history.png)
- **Remaining limitation:** None
- **External dependency:** None
- **Verification status:** Verified

### Gap 20: Friendly workflow designer labels

- **Original gap:** The designer showed identifiers such as ChangeRequest and Fai.
- **Fix implemented:** Display labels with configurable overrides and an acronym-aware fallback, used in tabs, lists, selectors, dry-run text and validation messages; identifiers unchanged.
- **Commits and files changed:** 25e76c0 (36 files changed).
- **Migration:** None
- **Tests added:** GapClosureLabelTests (11); E2E 14 labels
- **Test result:** passing in the final full run.
- **Screenshots:** [192](screenshots/gap-closure/192-gap20-friendly-record-type-labels.png)
- **Remaining limitation:** None
- **External dependency:** None
- **Verification status:** Verified

### Gap 21: Content library search limit

- **Original gap:** The library loaded the newest 1,000 files and filtered them in memory.
- **Fix implemented:** Server-side paged search with permission scope in SQL, filters (kind, text, dates, content type, owner, site, department, classification, retention), stable sorts, totals, trigram indexes.
- **Commits and files changed:** 6397437 (14 files changed).
- **Migration:** ContentLibrarySearch
- **Tests added:** GapClosureContentLibraryTests (2, 1,250 files); E2E 14 paging past 1,000 files
- **Test result:** passing in the final full run.
- **Screenshots:** [193](screenshots/gap-closure/193-gap21-content-library-paging.png)
- **Remaining limitation:** None
- **External dependency:** None
- **Verification status:** Verified

### Gap 22: Scheduled report delivery modes

- **Original gap:** Only links could be delivered.
- **Fix implemented:** Secure link stays the default; PDF, Excel and CSV attachments need explicit policy enablement, report permission, recipient checks, allowed email domains, size limit, expiry; confidential reports are never attached by default; attachments sent only over TLS.
- **Commits and files changed:** 7856099 (16 files changed); 8c8b2a8 (9 files changed).
- **Migration:** ReportDistribution (shared)
- **Tests added:** GapClosureReportDistributionTests attachment test (each mode, policy off, confidential, domain, size, retention, fallback); E2E 22 delivery policy
- **Test result:** passing in the final full run.
- **Screenshots:** [194](screenshots/gap-closure/194-gap22-report-delivery-policy.png)
- **Remaining limitation:** None
- **External dependency:** None
- **Verification status:** Verified

### Gap 23: Video conversion

- **Original gap:** Uploaded videos were served as uploaded only.
- **Fix implemented:** Playback copies (MP4 H.264/AAC, optional WebM) made by a worker job through an ffmpeg adapter; the original is kept; output is probed before it is marked Ready; retries, reuse of identical content, status and errors in the player, Download original. Without ffmpeg the copies wait with the configuration requirement and nothing is marked converted.
- **Commits and files changed:** 54fabef (15 files changed); 5007186 (5 files changed).
- **Migration:** VideoRenditions
- **Tests added:** GapClosureVideoConversionTests (4, three use the local ffmpeg); E2E 25, E2E 13
- **Test result:** passing in the final full run.
- **Screenshots:** [195](screenshots/gap-closure/195-gap23-video-conversion-status.png)
- **Remaining limitation:** Thumbnails are not generated.
- **External dependency:** None (ffmpeg is installed in the Docker images from the final pass)
- **Verification status:** Verified

### Gap 24: Compliance mapping verification workflow

- **Original gap:** Pack clause lists had no verification state.
- **Fix implemented:** Versioned mappings with states Draft, Unverified, Under review, Verified, Superseded and Rejected, source, edition, publisher, reference, verifier, dates and append-only history; a second person verifies; shipped content changes start a new Unverified version; coverage pages carry a no-guarantee disclaimer. The PPAP matrix ships Verified against two published reproductions.
- **Commits and files changed:** 94e1f41 (21 files changed).
- **Migration:** ComplianceMappingVerification
- **Tests added:** GapClosureComplianceMappingTests (2); E2E 14 verification states; E2E 10 PPAP matrix
- **Test result:** passing in the final full run.
- **Screenshots:** [196](screenshots/gap-closure/196-gap24-compliance-mapping-verification.png)
- **Remaining limitation:** All pack clause lists, including IATF 16949 and AS9100, are Unverified.
- **External dependency:** Licensed copies of the standards
- **Verification status:** Workflow verified; content blocked by licensed source material

### Gap 25: ERP connector live-validation status

- **Original gap:** Connectors had no record of live validation.
- **Fix implemented:** Connection, authentication and read gates recorded per configuration fingerprint; write always Not applicable (read-only adapters); production approval by a second person with key assumptions confirmed; guided checklist for all 12 systems; environment field; history.
- **Commits and files changed:** 2af739c (15 files changed).
- **Migration:** ErpConnectorValidation
- **Tests added:** GapClosureErpValidationTests (4); E2E 14 live-validation tab
- **Test result:** passing in the final full run.
- **Screenshots:** [197](screenshots/gap-closure/197-gap25-erp-live-validation.png)
- **Remaining limitation:** No connector has been validated against a live customer system. NetSuite transaction types, SAP entity names, Oracle pod host, Plex and DELMIAworks behaviour stay Unverified.
- **External dependency:** Customer ERP systems and credentials
- **Verification status:** Framework verified; requires live vendor validation

### Gap 26: Extension licensing key readiness

- **Original gap:** Licence verification used a single configured key.
- **Fix implemented:** QGL2 ES256 licences with key id, public-key registration (private keys refused), rotation with multi-key verification, retire and revoke, revocation list, customer and deployment scope, development keys refused on production, offline signing CLI that refuses without a production key reference. Custody procedure in docs/guides/license-key-custody.md.
- **Commits and files changed:** ddf49a8 (19 files changed).
- **Migration:** ExtensionLicenseKeys
- **Tests added:** GapClosureLicensingTests (4); E2E 14 marketplace keys
- **Test result:** passing in the final full run.
- **Screenshots:** [198](screenshots/gap-closure/198-gap26-licence-verification-keys.png)
- **Remaining limitation:** No production key exists in the repository or on the box.
- **External dependency:** Shawn's production signing key (offline)
- **Verification status:** Framework verified; blocked by production key ownership

### Gap 27: SMS and push provider readiness

- **Original gap:** Only webhook notification channels existed.
- **Fix implemented:** Provider framework: generic HTTPS gateway and sandbox fixture; templates; self-service opt-in and opt-out; delivery job with rate limit, retries and failure reasons; signed delivery receipts; providers disabled until a passing test and (production) a second person's approval. Webhook channels unchanged.
- **Commits and files changed:** 58f12eb (15 files changed); a23c197 (8 files changed).
- **Migration:** MessagingProviders
- **Tests added:** GapClosureMessagingTests (7); E2E 23
- **Test result:** passing in the final full run.
- **Screenshots:** [199](screenshots/gap-closure/199-gap27-sms-push-providers.png)
- **Remaining limitation:** Twilio, FCM and APNs are non-sendable slots; no live delivery has been made.
- **External dependency:** SMS gateway and push accounts
- **Verification status:** Partially fixed; blocked by external credentials

### Gap 28: Generation provider boundary

- **Original gap:** Generated drafts had no provider boundary.
- **Fix implemented:** Rule-based behaviour stays the default and the fallback. Optional provider boundary with modes (rule-based only, external disabled, approved functions), fixture, external HTTPS and local providers, per-function policy, permission- and site-scoped sources, confidentiality filtering, injection-phrase and secret redaction, citation checks, request metadata log with retention, and drafts that the requester reviews and that are never applied automatically.
- **Commits and files changed:** 6a3ba70 (13 files changed); 3d4c1ab (9 files changed).
- **Migration:** GenerationBoundary
- **Tests added:** GapClosureGenerationBoundaryTests (5); E2E 24
- **Test result:** passing in the final full run.
- **Screenshots:** [200](screenshots/gap-closure/200-gap28-generated-draft-on-ncr.png), [201](screenshots/gap-closure/201-gap28-generated-request-log.png)
- **Remaining limitation:** No external generation service was contacted.
- **External dependency:** An external generation provider account, if Shawn wants one
- **Verification status:** Framework verified; blocked by external credentials

## 1. Fixed now

Gaps 1, 2, 3, 4, 5, 6, 7, 8, 10, 11, 12, 15, 16, 17, 18, 19, 20, 21, 22 and 23. Each one is implemented, migrated where needed and tested, and its tests pass.

Gap 23 depends on ffmpeg being installed. It was validated on the box with ffmpeg 7.1 against a 2-second fixture, and the API and worker Docker images now install ffmpeg. Video thumbnails are not generated; that was outside the gap.

## 2. Partially fixed

- **Gap 13, carriers.** The Fixture, Generic REST and LTL adapters are complete. The UPS, FedEx and DHL slots only test the OAuth token. Their shipment operations are not implemented and the slots cannot be enabled.
- **Gap 27, SMS and push.** The generic HTTPS gateway and the sandbox fixture are complete. Twilio, Firebase Cloud Messaging and Apple Push are non-sendable slots.

## 3. Blocked by external credentials

- **Gap 27:** an SMS gateway account and push credentials (FCM, APNs) are needed before any live delivery.
- **Gap 28:** an external generation provider account, if one is wanted. Rule-based drafts work without one, and no external service was contacted.
- **Gap 13:** carrier developer accounts, which are also needed for section 7.

## 4. Blocked by licensed or authoritative source material

- **Gap 24:** the verification workflow is complete. All compliance pack clause lists, including IATF 16949 and AS9100, stay **Unverified** until someone with the licensed standards reviews them in the workflow. The PPAP matrix was verified against two published secondary reproductions, not a licensed AIAG copy.

## 5. Blocked by production key or ownership decision

- **Gap 26:** licence verification, key registration, rotation and revocation are complete. The production signing key is Shawn's offline input. It was not generated, and it is not in the repository, the box or any log. Signing refuses to run without it. The procedure is in `docs/guides/license-key-custody.md`.

## 6. Blocked by unavailable physical hardware

- **Gap 14:** print routing, queueing, retries, reprints and the PDF fallback are complete. No physical label printer was available, so the raw TCP (ZPL) and HTTPS relay drivers were tested against local fixtures only, and the Virtual driver is labelled test-only.

## 7. Requires live vendor validation

- **Gap 9, Business Central manufacturing:** tested against recorded fixtures only. Published pages, field names, keys and the extension remain Unverified.
- **Gap 25, ERP live validation:** no connector has been validated against a live customer system. NetSuite transaction types, SAP entity names, the Oracle pod host, and Plex and DELMIAworks behaviour stay visibly Unverified.
- **Gap 13, carriers:** UPS, FedEx and DHL need to be built and certified against each carrier's sandbox.

## Repository-wide sweep

The searches covered TODO, FIXME, placeholder, "not implemented", "coming soon", mock or sample only, skipped tests, empty handlers, hard-coded IDs and values, raw foreign-key fields, disconnected scheduler jobs and client-side-only authorization. The only "not implemented" text left is the honest labelling on the vendor slots (gaps 13 and 27). The only conditional skip is the ffmpeg check in the video tests, and ffmpeg is installed on the box, so those tests ran. Every worker job is registered in the worker. `docs/api/openapi.yaml` matches the code, which `OpenApiTests` checks.

These problems were found and fixed in the sweep:

| Commit | Problem found | Fix |
|---|---|---|
| 59d0f0c | Chips were nested inside text elements, which is invalid markup. The assigned-picker and site selects dropped their value while their options loaded. | Valid markup, and the selects keep their value while loading. E2E 1 now picks the exact document type option. |
| c4fe286 | Table-cell inputs, check boxes, switches and scan-station fields had no accessible name. | Accessible names added. `npm test` now runs the browser suite. |
| 4f477ed | Migrations were not tested as a set. | Migration tests check that the model matches the migrations, that they apply in order on an empty database, and that the latest one rolls back and forward. |
| 20a3fff | Two offline replays of the same pick confirmation or verification scan arriving at the same moment could both post. | The request id is re-checked under the row lock, and ledger postings serialize on the request id. Concurrency tests added. |
| db5296a | Two concurrent replays of an offline receipt could book two receipts. | They now book one receipt. Test added. |
| e21d568 | The shop-floor work order select lost a preselected order while the list loaded. | The preselected order is kept. |
| 324aa81, c10e128 | Copying or cloning an FAI with no request body failed. Some service dependencies were unused, the generation request log used its own clock, and there were build warnings. | Copying with an empty body keeps the type, unused dependencies are removed, the log uses the shared created time, and the warnings are cleared. |
| 178b5b5 | The Docker images had no ffmpeg, so the Docker setup could never make playback copies. The compose health check for the API used `wget`, which the .NET runtime image does not include, so the worker and web containers could wait for ever on an API that never reported healthy. | The API and worker images install ffmpeg (and the API image installs wget), and compose sets `Video__FfmpegPath`. Not run here, because Docker is not available on the box. It needs checking during the Docker one-command verification. |
| fad9539, 3f5444c | The ERP live-validation tab showed internal test class names (for example `GapClosureBcManufacturingTests`) to administrators. | Coverage is described in plain words. Two assertions that checked for the old text now check for the new text, with the same strength. Browser tests added for the bias study (gap 6) and the BC manufacturing validation tab (gap 9), which had no browser coverage. |
| 3a5eafd | On a 412 px phone the top bar pushed pages to 551 px wide, so every phone page scrolled sideways. | Below 900 px the search box becomes an icon that opens the command palette, which also searches records, and modules move into the menu. E2E 9 and E2E 16 now check that phone pages are not wider than the screen. The check was confirmed to fail on the old top bar. |
| 0b93d7c | At 1440 px, detail pages with a side panel (shipment, receipt, location and others) grew to 1540 px when a table was wide. | The main column can shrink, and the issued-documents table scrolls inside its panel. E2E 21 checks the page width. |

## Regression and acceptance testing

| # | Check | Result |
|---|---|---|
| 1 | All migrations on a clean test database | Passed: `MigrationTests` applies every migration to an empty database, and the latest one rolls back and forward. |
| 2 | Migrations against a populated database | Passed. A copy of the database left by the full browser run (11 NCRs, 10 shipments, 1,107 files, 12 receipts, 1,168 audit entries, among others) was rolled back through all 21 gap-closure migrations to `PpapMatrixCorrection` and then migrated forward to `VideoRenditions` with `dotnet ef database update`. No rows were lost. The backfills ran: version 1 of the existing retention policy, and verification states for 10 shipments. The API then started on that database without errors, seeded the document type library (37 types, including types already in use) and the compliance mapping records (9), and reported ready. GapClosureDocumentTypeTests separately covers a clean and a populated database. |
| 3 | Every backend test | 267/267 passed (final run, 3 min 26 s) |
| 4 | Every browser test | 72/72 passed (25 spec files, final run 7.6 min) |
| 5 | Portal browser tests | Passed: E2E 15 (3) and GapClosurePortalTests. |
| 6 | Import tests | Passed: GapClosurePfmeaImportTests (3), GapClosureControlPlanImportTests (3), and the E2E 14 import tests. |
| 7 | Mobile offline sync tests | Passed: E2E 9, E2E 16, E2E 17 and E2E 18; GapClosureOfflinePickingTests (5), GapClosureOfflineVerificationTests (3) and GapClosurePoReceivingTests (2). |
| 8 | Retention-policy tests | Passed: GapClosureRetentionTests (3) and the E2E 14 dry run. |
| 9 | Scheduled-report tests | Passed: GapClosureReportDistributionTests (5), E2E 22, ReportTests and PlatformTests. |
| 10 | Carrier-adapter fixture tests | Passed: GapClosureCarrierTests (5) and E2E 20. |
| 11 | MES device-auth tests | Passed: GapClosureMesDeviceAuthTests (5) and E2E 14. |
| 12 | Connector-validation state tests | Passed: GapClosureErpValidationTests (4), GapClosureBcManufacturingTests (3), ErpAdapterTests (18), E2E 14 and E2E 26. |
| 13 | Licence-signature tests | Passed: GapClosureLicensingTests (4). |
| 14 | Content search with more than 1,000 files | Passed: GapClosureContentLibraryTests (1,250 files) and E2E 14 (1,100 files, paging past 1,000). |
| 15 | PDF rendering tests | Passed: GapClosureShippingDocumentTests (deterministic bytes, page numbering, barcodes, reproduction), the GapClosureReportDistributionTests attachment test, and the GapClosureLabelPrintingTests PDF fallback. |
| 16 | Authorization-boundary tests | Passed: RbacTests (8), GapClosureSitePickerTests, GapClosurePortalTests, the MES device scope and site tests, and the permission checks in every gap test. |
| 17 | Concurrency tests | Passed: concurrent replays of picks, moves, receipts and verification scans (GapClosureOfflinePickingTests and GapClosureOfflineVerificationTests). |
| 18 | Complete production builds | Passed: `npm run build`, and `dotnet publish -c Release` for the API and the worker. |

### Backend tests by class (final run)

```
AccessResolverTests 4
AccountTests 1
ActionPriorityTableTests 10
AdvancedQualityTests 6
AuditTrailTests 4
AuthTests 9
AzureBlobStorageTests 2
ChangeTests 1
ComplaintTests 1
DocumentControlTests 6
EngineeringTests 8
EnterpriseTests 8
ErpAdapterTests 18
FaiAqlTests 4
GapClosureBcManufacturingTests 3
GapClosureBiasTests 8
GapClosureCarrierTests 5
GapClosureComplianceMappingTests 2
GapClosureContentLibraryTests 2
GapClosureControlPlanImportTests 3
GapClosureDenominatorTests 1
GapClosureDocumentTypeTests 1
GapClosureErpValidationTests 4
GapClosureGenerationBoundaryTests 5
GapClosureLabelPrintingTests 4
GapClosureLabelTests 11
GapClosureLicensingTests 4
GapClosureMesDeviceAuthTests 5
GapClosureMessagingTests 7
GapClosureOfflinePickingTests 5
GapClosureOfflineVerificationTests 3
GapClosurePfmeaImportTests 3
GapClosurePoReceivingTests 2
GapClosurePortalTests 1
GapClosureReportDistributionTests 5
GapClosureRetentionTests 3
GapClosureShippingDocumentTests 2
GapClosureSitePickerTests 1
GapClosureVideoConversionTests 4
InspectionTests 2
InventoryWarehouseTests 10
MaintenanceTests 2
MalwareScanTests 4
ManufacturingConnectivityTests 8
MasterDataTests 2
MigrationTests 3
NcrTraceabilityTests 2
NumberingTests 3
ObjectiveReviewTests 9
OpenApiTests 3
PlatformDesignerTests 7
PlatformTests 5
PpapMatrixTests 3
QualityModuleTests 5
RbacTests 8
ReminderJobTests 1
ReportTests 2
RiskTests 2
SharedEngineTests 6
SupplierQualityScenarioTests 1
TagTests 1
TimeZoneTests 2
TOTAL 267 FAILED 0

```

## Honest limits

- No live ERP, carrier, SMS, push, label printer or external generation accounts were used. Every such integration is tested against fixtures, starts disabled and stays labelled Unverified until a live test passes.
- The production licence signing key is Shawn's offline input. It is not generated or stored anywhere in this work.
- The IATF 16949 and AS9100 pack clause lists (and every other pack's list) are unverified. They were compiled without the licensed standard text.
- Video thumbnails are not generated. Playback copies are made only where ffmpeg is installed.
- The Docker image changes from the sweep (ffmpeg and wget) have not been built here, because Docker is not available on the box.
- Bulk material PPAP is not modelled as its own submission type. Its checklist row is shown for reference only.
