# Prompt 2 addendum: FAI and AQL Management Module (Shawn, 2026-10-16 9:51 PM ET). Content verbatim; bullet lists joined with semicolons.

Build a complete First Article Inspection and Acceptance Quality Limit management system.

FIRST ARTICLE INSPECTION (FAI)
Support: Full FAI; Partial FAI; Delta FAI; Engineering Change FAI; Process Change FAI; Supplier Change FAI; Tooling Change FAI; Material Change FAI; Requalification FAI.
Support creation from: New Product; New Part; New Revision; Drawing Revision; Engineering Change; Customer Requirement; Supplier Requirement.
FAI Records must contain: FAI Number; Product; Part Number; Revision; Drawing Number; Drawing Revision; Specification; Process; Supplier; Customer; Lot; Batch; Serial Number; Production Date; Inspector; Reviewer; Approver; Status; Completion Date.

BALLOONED DRAWING MANAGEMENT
Support: Ballooned Drawings; Characteristic Numbering; Characteristic Mapping; Drawing Characteristics; Special Characteristics; Key Characteristics; Critical Characteristics; Significant Characteristics.
For every balloon: Balloon Number; Characteristic Description; Drawing Reference; Dimension; Tolerance; Units; Requirement Source; Acceptance Criteria; Inspection Method.
Support upload and storage of: PDF Drawings; Engineering Drawings; Ballooned Drawings; Inspection Sheets.

CHARACTERISTIC ACCOUNTABILITY
Track every drawing characteristic. For each characteristic: Characteristic Number; Description; Nominal Value; Upper Tolerance; Lower Tolerance; Measured Value; Pass/Fail Result; Inspector; Equipment Used; Calibration Status; Evidence; Notes.
Support: 100 Percent Accountability; Missing Characteristic Detection; Incomplete Inspection Detection.
Do not allow FAI approval when characteristics remain incomplete.

FAI APPROVAL WORKFLOW
Support: Draft; Inspection In Progress; Review; Returned For Correction; Approval Pending; Approved; Rejected; Closed.
Require configurable approvals from: Quality; Engineering; Manufacturing; Customer when required.

FAI RELATIONSHIPS
Relate FAI records to: Products; Parts; Drawings; Specifications; Inspection Plans; Change Requests; NCRs; CAPAs; Suppliers; Customers. Allow direct navigation between related records.

AQL MANAGEMENT
Build a complete Acceptance Quality Limit sampling system. Support standards-based sampling plans.
Support: Single Sampling; Double Sampling; Multiple Sampling; Continuous Sampling; Reduced Inspection; Normal Inspection; Tightened Inspection.
Support configurable plans. Do not hard-code any single standard.
Allow administrators to configure: Sample Size Tables; Acceptance Numbers; Rejection Numbers; Inspection Levels.

AQL TABLE MANAGEMENT
Support: Lot Size; Sample Size; Inspection Level; AQL Level; Acceptance Quantity; Rejection Quantity. Support configurable values. Allow future table revisions without application changes.

AQL INSPECTION RECORDS
Support: Inspection Number; Product; Part Number; Revision; Supplier; Customer; Lot Size; Sample Size; Inspection Level; AQL Level; Defects Found; Critical Defects; Major Defects; Minor Defects; Acceptance Number; Rejection Number; Pass/Fail Result.
Automatically determine acceptance status using the configured sampling plan.

DEFECT CLASSIFICATION
Support: Critical Defect; Major Defect; Minor Defect. Allow configurable defect coding.
Support linkage to: NCRs; CAPAs; Supplier Corrective Actions; Complaints.

SUPPLIER RECEIVING INSPECTION
Support AQL-based receiving inspection. Track: Supplier; Purchase Order; Lot; Shipment; Sampling Plan; Inspection Results; Acceptance History.
Automatically calculate: Supplier Defect Rate; Supplier Acceptance Rate; Supplier Rejection Rate.

INSPECTION PLAN INTEGRATION
Integrate FAI and AQL with: Inspection Plans; Receiving Inspection; In Process Inspection; Final Inspection; Supplier Quality; Product Quality; NCR; CAPA; Reporting.

REPORTING
Create dashboards for: Open FAIs; Overdue FAIs; Approved FAIs; Failed FAIs; First Pass Yield; Characteristic Pass Rate; Supplier AQL Performance; Lot Acceptance Rate; Lot Rejection Rate; Defect Trends; Defect Categories; Supplier Defect Trends.

WORKFLOW RULES
Do not allow: FAI approval with missing characteristics; FAI approval with unresolved failures; AQL acceptance when rejection criteria are met; Closure without approvals; Closure without audit history.

AUDIT REQUIREMENTS
Audit: Characteristic Changes; Measured Values; Approvals; Rejections; Sampling Decisions; Acceptance Decisions; Drawing Revisions; Inspector Changes; Review Actions. All actions must be fully traceable.

TESTING
Validate: 1. New Part FAI 2. Drawing Revision FAI 3. Ballooned Drawing Characteristic Mapping 4. Complete Characteristic Inspection 5. Failed Characteristic Workflow 6. AQL Pass Scenario 7. AQL Reject Scenario 8. Supplier Receiving Inspection 9. FAI to NCR Workflow 10. Supplier AQL Trending

The FAI and AQL module must be fully integrated into the existing Quality Management System and use the shared workflow, approval, audit, notification, reporting, attachment, and permission engines.
