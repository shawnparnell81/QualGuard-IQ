# QualGuard IQ – Master Technical Implementation Prompt: Enterprise Form Templates, High-Fidelity Office Imports, Document Conversion, Quality Content Migration, and Advanced QMS Functionality
(Received from Shawn 2026-10-17 9:17 AM ET. Shawn's instruction: "use this prompt ONLY IF ITS THE SAME PROMPT, IF ITS NOT THE SAME THEN DO BOTH AND MAKE THE FORMS LOOK PROFESSIONAL, DETAILED AND PRECISE." It is not the same as prompt-forms-import.md, so both are built together, with this one as the detailed technical spec.)

MASTER TECHNICAL IMPLEMENTATION PROMPT
QUALGUARD IQ ENTERPRISE FORM TEMPLATES, HIGH-FIDELITY OFFICE IMPORTS, DOCUMENT CONVERSION, QUALITY CONTENT MIGRATION, AND ADVANCED QMS FUNCTIONALITY

ROLE

Act as the principal software architect and lead implementation engineer responsible for expanding the existing QualGuard IQ platform.

Perform the combined responsibilities of:

1. Principal Software Architect
2. Senior ASP.NET Core Engineer
3. Senior React and TypeScript Engineer
4. Senior PostgreSQL Database Architect
5. Senior Document Processing Engineer
6. Senior Microsoft Office File-Format Engineer
7. Senior Quality Manager
8. Senior Manufacturing Quality Engineer
9. Senior Document Control Manager
10. Senior APQP and PPAP Specialist
11. Senior Calibration and Metrology Specialist
12. Senior Application Security Engineer
13. Senior Software Validation Engineer
14. Senior User Experience Designer
15. Senior Migration and Data Conversion Engineer

PRIMARY OBJECTIVE

Expand the existing QualGuard IQ application with:

• Enterprise-grade quality form templates
• High-fidelity Word import
• High-fidelity Excel import
• CSV data import
• PDF and scanned-form intake
• Imported-form conversion
• Formula preservation
• Formatting preservation
• Controlled template management
• Master-list management
• Quality record migration
• Enterprise QMS capabilities not already implemented
• Original user experience and visual design

The completed implementation must allow a quality manager, engineer, auditor, calibration coordinator, supplier quality engineer, document controller, or manufacturing employee to import existing real-world quality documents without destroying their layout, calculations, formulas, worksheets, tables, or historical meaning.

IMPORTANT INTELLECTUAL-PROPERTY REQUIREMENTS

Research competing QMS products only to understand general capability categories and common business needs.

Do not:

• Copy another vendor’s source code
• Copy another vendor’s screen layout
• Copy another vendor’s colors or branding
• Copy another vendor’s navigation structure
• Copy another vendor’s proprietary terminology where avoidable
• Reproduce proprietary form templates
• Reproduce copyrighted standards
• Reproduce licensed APQP, PPAP, FAI, audit, or compliance forms
• Copy proprietary workflow definitions
• Scrape or duplicate another vendor’s template library
• claim that QualGuard IQ is endorsed by another vendor or standards publisher

Create:

• Original screen designs
• Original user flows
• Original layouts
• Original templates
• Organization-authored field names
• Configurable requirement mappings
• User-supplied template conversion
• Licensed-content reference fields
• Original reports and dashboards

Use common quality-management concepts, public terminology, user-owned files, organization-authored templates, and configuration-driven mappings.

EXISTING APPLICATION CONSTRAINTS

Continue in the existing QualGuard IQ repository.

Do not create a new application.

Do not replace the established architecture.

Do not duplicate:

• Authentication
• Authorization
• Workflow engine
• Approval engine
• Audit engine
• File engine
• Notification engine
• Search engine
• Reporting engine
• Dashboard engine
• Retention engine
• Record relationship engine
• Form designer
• Document control
• Product records
• Supplier records
• Customer records
• NCR
• CAPA
• APQP
• PPAP
• FAI
• AQL
• Calibration
• Training
• Audit
• Risk
• Change control
• Inspection
• Management review

Extend these shared services.

Preserve the current QualGuard IQ visual identity while creating a new, original enterprise template and import experience.

FIRST ACTION

Before writing code:

1. Inspect the current repository.
2. Locate the existing form designer.
3. Locate the existing file-storage service.
4. Locate document revision logic.
5. Locate record revision logic.
6. Locate import/export utilities.
7. Locate PDF-generation services.
8. Locate background-job infrastructure.
9. Locate attachment malware-scanning boundaries.
10. Locate authorization policies.
11. Locate retention and archiving logic.
12. Locate audit-event infrastructure.
13. Locate current Office-file processing libraries.
14. Locate current tests and test fixtures.
15. Identify every module that currently accepts file attachments or imported data.
16. Identify duplicated or incomplete import code.
17. Preserve all working behavior.
18. Create an architecture decision record for the new import and template system.

Do not provide a planning-only response.

After inspection, immediately begin implementation.

==================================================
SECTION 1
TARGET ARCHITECTURE
==================================================

Implement the following shared services:

1. Template Catalog Service
2. Template Versioning Service
3. Template Rendering Service
4. Import Intake Service
5. File Inspection Service
6. Office Document Parser
7. Spreadsheet Parser
8. CSV Parser
9. PDF Intake Service
10. OCR Adapter
11. Layout Analysis Service
12. Formula Analysis Service
13. Field Detection Service
14. Form Conversion Service
15. Data Mapping Service
16. Conversion Validation Service
17. Conversion Warning Service
18. Import Approval Service
19. Migration History Service
20. Original File Preservation Service
21. Derived File Service
22. Content Preview Service
23. Template Package Service
24. Quality Record Import Service
25. Duplicate Detection Service
26. Batch Import Service
27. Import Rollback Service
28. Import Audit Service
29. Import Reporting Service
30. Document Fidelity Comparison Service

Use background processing for:

• Large Word imports
• Large Excel imports
• PDF extraction
• OCR
• Preview generation
• Formula analysis
• Conversion
• Batch record creation
• Search indexing
• Thumbnail generation
• Fidelity comparison
• Export package creation

Do not process large files synchronously inside a normal HTTP request.

IMPORT JOB STATES

Support:

• Uploaded
• Security Scan Pending
• Security Scan Failed
• Inspecting
• Parsing
• Mapping Required
• Preview Ready
• Validation Failed
• Awaiting Approval
• Importing
• Imported
• Partially Imported
• Completed With Warnings
• Failed
• Canceled
• Rolled Back
• Archived

Every state transition must be audited.

==================================================
SECTION 2
CORE DATABASE MODEL
==================================================

Create or extend relational entities for:

TEMPLATE CATALOG

• Template ID
• Template Number
• Template Name
• Template Category
• Module
• Industry Pack
• Organization
• Site
• Department
• Owner
• Status
• Current Revision
• Effective Revision
• Description
• Tags
• Confidentiality
• Retention Rule
• Created By
• Created Date
• Updated By
• Updated Date

TEMPLATE REVISION

• Revision ID
• Template ID
• Revision Number
• Revision Status
• Source Type
• Source File ID
• Native File ID
• Preview File ID
• Normalized Representation
• Schema Version
• Rendering Version
• Change Summary
• Change Reason
• Effective Date
• Review Date
• Superseded Date
• Created By
• Approved By
• Approval Date
• Version Token

FORM DEFINITION

• Form Definition ID
• Template Revision ID
• Form Schema
• Layout Schema
• Validation Schema
• Formula Schema
• Workflow Configuration
• Print Configuration
• Search Configuration
• Reporting Configuration

FORM SECTION

• Section ID
• Parent Section ID
• Sequence
• Title
• Instructions
• Layout
• Repeating Status
• Conditional Rule

FORM FIELD

• Field ID
• Section ID
• Source Field ID
• Field Name
• Display Label
• Field Type
• Data Type
• Required Status
• Default Value
• Validation Rule
• Conditional Rule
• Formula
• Format
• Source Location
• Mapping Confidence
• Manual Review Status

FORM TABLE

• Table ID
• Section ID
• Table Name
• Header Rows
• Data Rows
• Repeating Status
• Column Definitions
• Formula Rules
• Subtotal Rules
• Total Rules

IMPORT JOB

• Import Job ID
• Source File ID
• Original File Name
• File Type
• File Size
• File Hash
• Import Mode
• Target Module
• Target Template
• Status
• Progress
• Submitted By
• Submitted Date
• Started Date
• Completed Date
• Warning Count
• Error Count
• Background Job ID
• Version Token

IMPORT MAPPING

• Mapping ID
• Import Job ID
• Source Location
• Source Name
• Source Type
• Target Field
• Target Type
• Transformation Rule
• Mapping Confidence
• User Confirmed
• Default Value
• Required Value
• Validation Status

IMPORT ISSUE

• Issue ID
• Import Job ID
• Source Location
• Severity
• Issue Code
• Description
• Suggested Resolution
• User Resolution
• Resolved By
• Resolved Date

FORMULA DEFINITION

• Formula ID
• Import Job ID
• Workbook
• Worksheet
• Cell
• Original Formula
• Normalized Formula
• Supported Status
• Evaluation Strategy
• Dependency List
• Cached Result
• Warning
• Manual Review Status

COMPLETED FORM

• Completed Form ID
• Template Revision ID
• Record Number
• Record Status
• Owner
• Site
• Department
• Workflow Instance
• Created By
• Created Date
• Submitted Date
• Approved Date
• Closed Date
• Retention Rule

COMPLETED FORM VALUE

• Completed Form ID
• Field ID
• Value Type
• Text Value
• Numeric Value
• Date Value
• Boolean Value
• JSON Value
• File ID

IMPORT HISTORY

• Import Event ID
• Import Job ID
• Event
• Actor
• Event Date
• Previous Value
• New Value
• Message
• Correlation ID

Use:

• Foreign keys
• Unique constraints
• Check constraints
• Appropriate indexes
• Optimistic concurrency
• Transactional import
• Controlled deletion
• Archived history
• Immutable approved revisions

==================================================
SECTION 3
ENTERPRISE TEMPLATE CATALOG
==================================================

Create a dedicated Enterprise Templates module.

NAVIGATION

Provide:

• Template Library
• My Templates
• Organization Templates
• Site Templates
• Industry Packs
• Imported Templates
• Draft Templates
• Templates Under Review
• Effective Templates
• Retired Templates
• Import Jobs
• Conversion Warnings
• Template Usage
• Migration History

LIST FUNCTIONS

Support:

• Search
• Filters
• Sorting
• Pagination
• Saved Views
• Column Selection
• Favorites
• Ownership
• Site
• Department
• Status
• Category
• Revision
• Industry Pack
• Last Used
• Effective Date
• Review Date

TEMPLATE ACTIONS

Support:

• Create
• Import
• Clone
• Revise
• Submit
• Review
• Approve
• Reject
• Return
• Publish
• Retire
• Restore
• Download Native File
• Download Normalized File
• Preview
• Compare Revisions
• Export Package
• Assign Workflow
• Assign Retention
• Assign Permissions
• View Usage
• View Audit History

Approved template revisions must be immutable.

Completed forms must remain linked to the exact revision used.

==================================================
SECTION 4
TEMPLATE CATEGORIES
==================================================

Create original, configurable, detailed template definitions for the following categories.

Do not create small demonstration forms.

Each template must include:

• Purpose
• Scope
• Instructions
• Record identification
• Ownership
• Site
• Department
• Product
• Part
• Supplier
• Customer
• Dates
• Status
• Workflow
• Attachments
• Signatures
• Related records
• Audit history
• Retention metadata
• Print layout
• Export layout

NONCONFORMANCE TEMPLATES

Include:

• Internal Nonconformance Report
• Supplier Nonconformance Report
• Customer Nonconformance Report
• Material Nonconformance
• Production Nonconformance
• Process Nonconformance
• Documentation Nonconformance
• Audit Finding
• Containment Record
• Material Segregation Record
• Disposition Authorization
• Rework Authorization
• Repair Authorization
• Use-As-Is or Concession Request
• Scrap Authorization
• Return-to-Supplier Authorization

NCR sections must support:

• Detection
• Requirement
• Actual condition
• Evidence
• Quantity affected
• Traceability
• Containment
• Investigation
• Risk
• Disposition
• Approvals
• Verification
• Closure
• Cost

CAPA TEMPLATES

Include:

• CAPA Request
• CAPA Investigation
• Correction Record
• Containment Plan
• Root Cause Analysis
• Corrective Action Plan
• Preventive Action Plan
• Implementation Verification
• Effectiveness Review
• CAPA Closure

ROOT CAUSE TEMPLATES

Include:

• Five Whys
• Fishbone Analysis
• Is and Is Not Analysis
• Fault Tree Reference
• Cause and Effect Matrix
• Escape Point Analysis
• Barrier Analysis
• Eight Discipline Problem Solving Package

APQP TEMPLATES

Create configurable, organization-authored templates for:

• Project Charter
• APQP Timing Plan
• Responsibility Matrix
• Feasibility Review
• Customer Requirement Review
• Design Input Review
• Design Output Review
• Design Verification Plan
• Design Validation Plan
• Product Assurance Plan
• Process Flow
• Special Characteristics List
• PFMEA
• Control Plan
• Packaging Evaluation
• Measurement System Plan
• Capability Study Plan
• Production Trial Plan
• Launch Readiness Review
• Safe Launch Plan
• Lessons Learned
• Gate Review
• Deliverables Checklist

PPAP TEMPLATES

Create configurable submission-package templates for:

• Submission Cover Sheet
• Submission Element Checklist
• Design Record Index
• Engineering Change Record
• Customer Engineering Approval
• DFMEA Reference
• Process Flow Reference
• PFMEA Reference
• Control Plan Reference
• Measurement System Study
• Dimensional Results
• Material Test Results
• Performance Test Results
• Initial Process Study
• Qualified Laboratory Documentation
• Appearance Approval
• Sample Product Record
• Master Sample Record
• Checking Aid Record
• Customer-Specific Requirement Record
• Part Submission Warrant
• Rejection and Resubmission Record

Do not reproduce proprietary manuals or licensed forms.

Allow the organization to configure its own element names, required fields, submission levels, customer requirements, and approval rules.

FAI TEMPLATES

Include:

• FAI Cover Record
• Part and Drawing Accountability
• Balloon Characteristic Register
• Material and Process Accountability
• Characteristic Results
• Equipment Used
• Nonconforming Characteristic Record
• Partial FAI Record
• Delta FAI Record
• FAI Approval
• FAI Closure

AQL AND INSPECTION TEMPLATES

Include:

• Receiving Inspection
• In-Process Inspection
• Final Inspection
• First-Piece Inspection
• Dock Audit
• Packaging Inspection
• Sample Inspection
• Lot Acceptance Inspection
• Skip-Lot Review
• Inspection Plan
• Characteristic Results
• Defect Summary
• Lot Disposition

CALIBRATION TEMPLATES

Include:

• Calibration Master List
• Measurement Equipment Register
• Calibration Schedule
• External Calibration Request
• Internal Calibration Record
• Calibration Certificate Summary
• Calibration Standards Record
• Calibration Label Record
• Out-of-Tolerance Investigation
• Impact Assessment
• Equipment Repair Record
• Equipment Retirement Record
• Calibration Extension Request
• Limited Use Authorization
• Bias Study
• Stability Study
• Linearity Study
• Variable Gage R&R
• Attribute Gage R&R
• Measurement System Analysis Review

DOCUMENT CONTROL TEMPLATES

Include:

• Master Document List
• Master Record List
• Master Drawing List
• Master Specification List
• Master Form List
• Master Procedure List
• Master Work Instruction List
• External Document Register
• Obsolete Document Register
• Document Change Request
• Document Review Record
• Controlled Copy Register
• Document Distribution List
• Document Acknowledgment Register
• Document Retention Schedule

AUDIT TEMPLATES

Include:

• Annual Audit Program
• Audit Plan
• Audit Agenda
• Audit Checklist
• Audit Evidence Log
• Audit Interview Record
• Audit Finding
• Audit Report
• Corrective Action Follow-Up
• Audit Closure
• Supplier Audit
• Process Audit
• Product Audit
• System Audit
• Layered Process Audit
• Compliance Assessment

TRAINING TEMPLATES

Include:

• Training Matrix
• Competency Matrix
• Skills Matrix
• Position Training Requirements
• Training Assignment
• Training Attendance
• Training Assessment
• On-the-Job Qualification
• Supervisor Verification
• Certification Register
• Qualification Register
• Retraining Record
• Training Effectiveness Review

SUPPLIER TEMPLATES

Include:

• Approved Supplier List
• Supplier Qualification
• Supplier Questionnaire
• Supplier Risk Assessment
• Supplier Audit
• Supplier Scorecard
• Supplier Performance Review
• Supplier Development Plan
• Supplier Corrective Action Request
• Supplier Change Notification
• Supplier Deviation Request
• Supplier Certification Register
• Supplier Reapproval

CUSTOMER QUALITY TEMPLATES

Include:

• Complaint Intake
• Return Authorization
• Returned Product Evaluation
• Customer Investigation
• Customer Response
• Warranty Evaluation
• Field Failure
• Recall Assessment
• Customer-Specific Requirement Register
• Customer Satisfaction Review

RISK AND CHANGE TEMPLATES

Include:

• Risk Register
• Opportunity Register
• Product Risk Assessment
• Process Risk Assessment
• Supplier Risk Assessment
• Change Request
• Change Impact Assessment
• Change Risk Assessment
• Implementation Plan
• Post-Implementation Review
• Temporary Deviation
• Planned Deviation
• Emergency Change

MANAGEMENT REVIEW TEMPLATES

Include:

• Agenda
• Attendance
• Input Package
• KPI Review
• Audit Review
• Complaint Review
• Supplier Review
• Risk Review
• Resource Review
• Decisions
• Action Register
• Minutes
• Approval

==================================================
SECTION 5
WORD IMPORT TECHNICAL REQUIREMENTS
==================================================

Support:

• DOCX as the primary editable Word format
• DOC through a controlled conversion adapter when supported

Do not claim perfect fidelity for unsupported legacy content.

PRESERVE

• Paragraphs
• Headings
• Styles
• Fonts when licensed and available
• Font size
• Bold
• Italic
• Underline
• Colors
• Highlighting
• Alignment
• Indentation
• Bullets
• Numbered lists
• Multilevel lists
• Tables
• Cell widths
• Row heights
• Borders
• Shading
• Merged cells
• Header rows
• Headers
• Footers
• Page numbers
• Page breaks
• Section breaks
• Portrait sections
• Landscape sections
• Images
• Captions
• Hyperlinks
• Bookmarks
• Content controls
• Checkboxes
• Signature lines as nonexecuting placeholders
• Footnotes where supported
• Endnotes where supported

PRESERVATION MODEL

Store:

1. Original Word file
2. Original file hash
3. Extracted content model
4. Layout model
5. Generated preview
6. Conversion warnings
7. Normalized form definition
8. Rendering version
9. Import tool version

WORD IMPORT MODES

Mode A: Controlled Native Document

• Preserve original file
• Preview original
• Route through document control
• Maintain revisions
• Do not convert it into a form

Mode B: Fillable Document Record

• Identify content controls
• Identify placeholders
• Map editable regions
• Preserve noneditable layout
• Save entered values separately from the original template

Mode C: Electronic Form Conversion

• Analyze tables and placeholders
• Detect candidate fields
• Suggest mappings
• Allow manual correction
• Generate QualGuard IQ form schema
• Retain the original document as evidence

Mode D: Data Extraction

• Extract named content controls
• Extract table rows
• Map values to existing QMS records
• Require preview and approval before import

WORD IMPORT SECURITY

• Block macros from executing
• Treat embedded objects as untrusted
• Extract images safely
• Sanitize hyperlinks
• Validate file signatures
• Enforce file-size limits
• Scan uploaded files
• Prevent external-resource retrieval without authorization
• Never execute embedded scripts
• Record security findings

WORD FIDELITY REVIEW

Create a split-screen comparison:

• Original preview
• Imported or normalized preview
• Differences highlighted
• Missing fonts
• Changed dimensions
• Unsupported elements
• Reflow warnings
• Manual approval

Do not mark the conversion approved until an authorized user accepts the fidelity report.

==================================================
SECTION 6
EXCEL IMPORT TECHNICAL REQUIREMENTS
==================================================

Support:

• XLSX
• XLS through an adapter
• XLSM without executing macros
• CSV
• Multiple worksheets

PRESERVE

• Workbook
• Worksheet names
• Worksheet order
• Hidden worksheet metadata
• Cell values
• Cell data types
• Formulas
• Cached formula results
• Named ranges
• Tables
• Auto filters
• Freeze panes
• Merged cells
• Cell borders
• Fill colors
• Font styles
• Number formats
• Date formats
• Currency formats
• Percentage formats
• Text wrapping
• Alignment
• Row heights
• Column widths
• Conditional formatting
• Data validation
• Hyperlinks
• Comments or notes
• Images
• Print areas
• Print titles
• Page orientation
• Page margins
• Page breaks
• Headers
• Footers
• Charts as preserved workbook objects or derived previews
• Pivot tables as preserved workbook objects

FORMULA REQUIREMENTS

Classify formulas as:

• Supported for server evaluation
• Supported for browser evaluation
• Preserved but not evaluated
• Unsupported
• External reference
• Macro-dependent
• Volatile
• Requires manual review

Do not silently replace formulas with values.

Store:

• Original formula
• Cached result
• Formula dependencies
• Workbook calculation mode
• External references
• Unsupported functions
• Formula warning
• Evaluation version

FORMULA SECURITY

Do not execute:

• Macros
• VBA
• Shell commands
• External links
• Dynamic data connections
• Embedded executables
• Untrusted add-ins

Detect and flag:

• DDE references
• External workbook references
• Hidden macros
• Suspicious hyperlinks
• Formula injection
• Unsupported add-ins

EXCEL IMPORT MODES

Mode A: Preserve Workbook

• Store and control the native workbook
• Preserve formatting and formulas
• Provide secured preview
• Maintain revision control

Mode B: Import Spreadsheet Data

• Select worksheet
• Detect header row
• Map columns
• Validate rows
• Preview changes
• Import records transactionally

Mode C: Convert Spreadsheet to Electronic Form

• Preserve visual grid
• Detect input cells
• Detect protected cells
• Detect formulas
• Detect dropdowns
• Detect validation
• Convert selected regions into form sections and tables

Mode D: Master List Migration

Convert structured worksheets into:

• Calibration master lists
• Document master lists
• Drawing lists
• Approved supplier lists
• Training matrices
• Skills matrices
• Risk registers
• Inspection plans
• PFMEA records
• Control plans
• APQP timing plans
• PPAP submission lists
• Equipment lists
• Product lists
• Part lists

Mode E: Transactional Data Import

Import:

• Inspection results
• Calibration history
• Training history
• NCR history
• CAPA history
• Audit findings
• Supplier performance
• Complaint history

Require:

• Unique-key mapping
• Duplicate strategy
• Record ownership
• Site mapping
• Department mapping
• User mapping
• Status mapping
• Date mapping
• Attachment mapping
• Import audit record

EXCEL PREVIEW

Provide:

• Workbook tree
• Worksheet tabs
• Grid preview
• Formula indicators
• Validation indicators
• Named-range indicators
• Import-region selection
• Column mapping
• Error highlighting
• Warning highlighting
• Row counts
• Valid-row count
• Invalid-row count
• Duplicate count

EXCEL ROUND TRIP

For supported electronic-form templates:

• Export a generated workbook
• Allow controlled offline completion
• Reimport the workbook
• Verify template ID
• Verify template revision
• Verify workbook signature or integrity hash
• Reject altered protected structure
• Import allowed input cells only
• Recalculate supported formulas
• Preserve the submitted workbook as evidence
• Prevent duplicate submission

==================================================
SECTION 7
CSV IMPORT TECHNICAL REQUIREMENTS
==================================================

Support:

• UTF-8
• Common delimiter detection
• Configurable delimiters
• Quoted values
• Escaped values
• Large files
• Streaming parsing
• Header detection
• Headerless files
• Date-format configuration
• Decimal-separator configuration
• Empty-value handling

CSV IMPORT WORKFLOW

1. Upload
2. Security inspection
3. Encoding detection
4. Delimiter detection
5. Header-row selection
6. Data-type inference
7. Target-module selection
8. Column mapping
9. Transformation rules
10. Validation
11. Duplicate detection
12. Preview
13. Dry run
14. Approval
15. Transactional import
16. Import summary
17. Error-row export
18. Audit history

Never allow spreadsheet formulas in CSV exports to create formula-injection risk.

Escape dangerous leading characters when exporting values intended for spreadsheet applications.

==================================================
SECTION 8
PDF AND SCANNED FORM INTAKE
==================================================

Support:

• Native PDF
• Fillable PDF
• Scanned PDF
• Image-based forms
• Multi-page documents

PDF MODES

Mode A: Controlled PDF Document

• Preserve exact PDF
• Store checksum
• Preview
• Apply access control
• Apply revision control

Mode B: Fillable PDF Intake

• Detect form fields
• Extract field names
• Extract values
• Map to QualGuard IQ fields
• Preserve completed PDF

Mode C: OCR-Assisted Form Conversion

• Render pages
• Run OCR through an adapter
• Detect labels
• Detect tables
• Detect checkboxes
• Detect signature areas
• Detect repeating rows
• Present confidence scores
• Require manual confirmation

Mode D: Data Extraction

• Select pages
• Select tables
• Map extracted columns
• Validate rows
• Import selected data

OCR REQUIREMENTS

• Provider-neutral adapter
• Disabled until configured
• Confidence score
• Page-level status
• Field-level status
• Manual correction
• Original image preservation
• Extracted text preservation
• Processing audit
• No unsupported accuracy claims

==================================================
SECTION 9
FORM CONVERSION ENGINE
==================================================

Build a conversion pipeline that transforms detected document structures into QualGuard IQ form definitions.

CONVERSION STEPS

1. Inspect source file
2. Extract document structure
3. Detect pages or worksheets
4. Detect sections
5. Detect labels
6. Detect fields
7. Detect tables
8. Detect formulas
9. Detect validations
10. Detect signatures
11. Detect approval blocks
12. Detect instructions
13. Detect conditional areas
14. Generate mapping suggestions
15. Score confidence
16. Present review interface
17. Allow corrections
18. Generate normalized schema
19. Generate preview
20. Compare fidelity
21. Validate
22. Submit for approval
23. Publish controlled template

FIELD DETECTION

Detect candidates for:

• Text
• Long text
• Integer
• Decimal
• Currency
• Percentage
• Date
• Date and time
• Time
• Checkbox
• Radio button
• Single select
• Multiple select
• User
• Department
• Site
• Product
• Part
• Supplier
• Customer
• Equipment
• Record relationship
• Attachment
• Image
• Signature
• Repeating row
• Calculated field

Do not automatically publish detected fields.

Require user confirmation for low-confidence mappings.

CONVERSION CONFIDENCE

Use:

• High
• Medium
• Low
• Unmapped

Show confidence at:

• Document level
• Page level
• Section level
• Field level
• Formula level
• Table level

==================================================
SECTION 10
HIGH-FIDELITY RENDERING
==================================================

Create separate representations for:

1. Native source
2. Normalized structure
3. Interactive form
4. Printable output
5. PDF archive output

Do not force one representation to perform every function.

INTERACTIVE VIEW

Optimize for:

• Data entry
• Accessibility
• Responsive layout
• Validation
• Workflow
• Search
• Relationships

PRINT VIEW

Optimize for:

• Source-faithful arrangement
• Page orientation
• Repeating table headers
• Page numbering
• Controlled-copy markings
• Approval blocks
• Signatures
• Organization branding
• Template revision
• Generated date
• Generated by
• Confidentiality
• Record number

Allow the user to choose:

• Original-look output
• QualGuard IQ standardized output

==================================================
SECTION 11
IMPORT MAPPING WORKBENCH
==================================================

Build a step-by-step mapping workbench.

STEP 1: FILE REVIEW

Show:

• File name
• File type
• Size
• File hash
• Security status
• Sheets or pages
• Detected content

STEP 2: IMPORT PURPOSE

Choose:

• Controlled document
• Controlled form template
• Master list
• Historical records
• Transactional records
• Attachment package
• Reference content

STEP 3: TARGET

Choose:

• QMS module
• Record type
• Existing template
• New template
• Existing revision
• New revision

STEP 4: STRUCTURE

Map:

• Pages
• Worksheets
• Tables
• Sections
• Headers
• Footer information
• Repeating regions

STEP 5: FIELDS

Map:

• Source
• Target
• Data type
• Required status
• Transformation
• Default
• Validation

STEP 6: RELATIONSHIPS

Map:

• Site
• Department
• User
• Supplier
• Customer
• Product
• Part
• Equipment
• Existing record

STEP 7: VALIDATION

Display:

• Errors
• Warnings
• Duplicates
• Unsupported content
• Missing references
• Invalid formulas
• Unauthorized mappings

STEP 8: PREVIEW

Show:

• Before
• After
• Changes
• Records to create
• Records to update
• Records to skip
• Records in error

STEP 9: APPROVAL

Require permission-aware approval for controlled imports.

STEP 10: EXECUTION

Execute transactionally.

STEP 11: RESULTS

Display:

• Imported
• Updated
• Skipped
• Failed
• Warnings
• Rollback status
• Audit record
• Downloadable result package

==================================================
SECTION 12
MASTER LIST MANAGEMENT
==================================================

Do not treat every master list as a generic spreadsheet.

Build governed list types for:

• Calibration equipment
• Controlled documents
• Engineering drawings
• Specifications
• Forms
• Suppliers
• Customers
• Employees
• Training requirements
• Certifications
• Products
• Parts
• Inspection plans
• Risks
• Quality objectives
• Audit schedules
• Customer requirements
• Supplier certifications

MASTER LIST FUNCTIONS

Support:

• Configurable columns
• Typed values
• Relationships
• Validation
• Revision control where applicable
• Workflow approval
• Bulk import
• Bulk update
• Export
• Saved views
• Audit history
• Duplicate prevention
• Record ownership
• Retention
• Search
• Reporting
• Effective dates
• Inactive status

MASTER DOCUMENT LIST

Include:

• Document number
• Title
• Category
• Department
• Site
• Owner
• Revision
• Status
• Effective date
• Review date
• Expiration date
• Distribution
• Training requirement
• Retention rule
• Native file
• Controlled PDF
• Related process

CALIBRATION MASTER LIST

Include:

• Equipment ID
• Asset ID
• Equipment name
• Type
• Manufacturer
• Model
• Serial number
• Site
• Department
• Location
• Owner
• Range
• Resolution
• Accuracy
• Acceptance criteria
• Frequency
• Last calibration
• Next due date
• Provider
• Status
• Certificate
• Out-of-tolerance status
• Limited-use status
• Retired status

==================================================
SECTION 13
ENTERPRISE FEATURES TO ADD OR VERIFY
==================================================

Inspect QualGuard IQ before adding anything.

Do not duplicate existing functions.

If missing or incomplete, implement:

• Planned deviation management
• Unplanned deviation management
• Laboratory investigation
• Product release management
• Batch or lot release
• Delegation and temporary responsibility
• External-partner collaboration
• Supplier change notification
• Customer-specific requirement register
• Regulatory intelligence register
• Quality event intake
• Quality event triage
• Quality event classification
• Cross-module investigation
• Recurrence detection
• Quality readiness scoring
• Audit readiness scoring
• CAPA effectiveness analytics
• Quality data lake or analytical store
• Mobile offline quality-event intake
• Periodic access review
• Controlled master data
• Release management
• Product quality review
• Annual quality review
• Lab sample tracking where configured
• Environmental monitoring integration boundary
• Safety and EHS integration boundary
• Complaint intake automation boundary
• Quality agreement management
• Contract manufacturer collaboration
• Approved laboratory management
• Stability-program configuration
• Recall simulation or mock-recall exercises
• Business continuity quality exercises
• Inspection skip-lot transitions
• Dynamic supplier-risk monitoring
• Quality hold and release board
• Quality event severity matrix
• Recurrence and systemic-issue detection
• Quality process cycle-time analytics
• Bottleneck analytics
• Record-aging analytics
• SLA management
• Delegated approval
• Escalation calendars
• Holiday calendars
• Regional time-zone rules

Every added capability must use shared QualGuard IQ services.

==================================================
SECTION 14
API REQUIREMENTS
==================================================

Create versioned APIs for:

• Template catalog
• Template revisions
• Template packages
• Import jobs
• Import mappings
• Import issues
• Import previews
• Word imports
• Excel imports
• CSV imports
• PDF imports
• Conversion jobs
• Form schemas
• Formula definitions
• Master lists
• Migration results
• Fidelity comparisons
• Import approvals
• Import rollback
• Template export
• Template installation

API RULES

• Enforce authentication
• Enforce authorization
• Enforce site boundaries
• Enforce department boundaries
• Enforce confidentiality
• Validate request size
• Stream large uploads
• Support idempotency
• Support optimistic concurrency
• Return structured errors
• Use correlation IDs
• Audit significant operations
• Rate-limit expensive processing
• Never return storage credentials
• Never expose private file paths

Generate updated OpenAPI documentation.

==================================================
SECTION 15
SECURITY REQUIREMENTS
==================================================

Validate all uploads by:

• File extension
• File signature
• MIME type
• Size
• Content structure
• Malware-scanning adapter
• Archive depth
• Decompression size
• Embedded-object policy

Protect against:

• Zip bombs
• Path traversal
• Macro execution
• Formula injection
• External-link retrieval
• Embedded executable content
• XXE
• SSRF through linked resources
• Malformed Office packages
• Malicious PDFs
• Oversized XML
• Unauthorized file access
• Cross-site record access

Use temporary processing storage with:

• Restricted permissions
• Automatic cleanup
• Randomized paths
• No public access
• Size quotas
• Processing timeout

Do not log:

• File contents unnecessarily
• Credentials
• Tokens
• Private signing keys
• Sensitive extracted values

==================================================
SECTION 16
PERMISSION MODEL
==================================================

Create or reuse permissions for:

• View templates
• Create templates
• Import templates
• Revise templates
• Approve templates
• Publish templates
• Retire templates
• View native files
• Download native files
• Convert documents
• Review conversion
• Approve conversion
• Import records
• Update records through import
• Roll back import
• View import history
• View conversion warnings
• Configure import mappings
• Install industry templates
• Export template packages
• Manage master lists
• View confidential content

Enforce permissions on the server.

Frontend visibility is not authorization.

==================================================
SECTION 17
MIGRATION AND BACKWARD COMPATIBILITY
==================================================

Preserve:

• Existing forms
• Existing completed responses
• Existing document revisions
• Existing file IDs
• Existing record numbers
• Existing signatures
• Existing approvals
• Existing audit history
• Existing links
• Existing reports

Do not automatically convert existing forms.

Provide an opt-in migration wizard.

For every migrated template:

• Preserve original
• Create migration snapshot
• Create new revision
• Generate comparison
• Require approval
• Preserve rollback ability

Database migrations must:

• Be version controlled
• Be idempotent where appropriate
• Preserve data
• Include indexes
• Include constraints
• Run against clean and populated test databases

==================================================
SECTION 18
TEST FIXTURES
==================================================

Create original or synthetic test files.

Do not use copyrighted proprietary forms.

TEST WORD FILES

Include:

• Simple form
• Complex table form
• Multi-section form
• Header and footer
• Portrait and landscape sections
• Images
• Checkboxes
• Content controls
• Merged cells
• Numbered lists

TEST EXCEL FILES

Include:

• Multiple worksheets
• Formulas
• Named ranges
• Tables
• Data validation
• Conditional formatting
• Merged cells
• Protected cells
• Charts
• Pivot metadata
• Hidden worksheet
• External-reference warning
• Macro-enabled workbook without macro execution

TEST CSV FILES

Include:

• Standard comma delimiter
• Semicolon delimiter
• Quoted commas
• Dates
• Decimals
• Unicode
• Duplicate rows
• Invalid rows
• Large streaming file

TEST PDF FILES

Include:

• Native text PDF
• Fillable PDF
• Scanned image PDF
• Multi-page table
• Checkbox form
• Low-confidence OCR case

==================================================
SECTION 19
AUTOMATED TESTING
==================================================

Create:

• Unit tests
• Parser tests
• Formula tests
• Layout tests
• Security tests
• Import tests
• Mapping tests
• Authorization tests
• Revision tests
• Workflow tests
• Background-job tests
• File-storage tests
• Rollback tests
• API tests
• Browser tests
• Performance tests
• Accessibility tests

END-TO-END TEST 1: WORD FORM IMPORT

1. Upload complex DOCX.
2. Scan file.
3. Parse structure.
4. Generate preview.
5. Detect fields.
6. Correct mappings.
7. Compare fidelity.
8. Submit for approval.
9. Publish template.
10. Complete electronic form.
11. Generate PDF.
12. Confirm historical link to source revision.

END-TO-END TEST 2: EXCEL CALIBRATION MASTER LIST

1. Upload workbook.
2. Detect worksheets.
3. Select master-list worksheet.
4. Preserve original workbook.
5. Map equipment fields.
6. Validate dates.
7. Detect duplicate equipment.
8. Preview changes.
9. Dry run.
10. Import.
11. Verify calibration records.
12. Verify import audit.
13. Verify formulas remain in original controlled workbook.

END-TO-END TEST 3: EXCEL MASTER DOCUMENT LIST

1. Upload workbook.
2. Detect document rows.
3. Map document numbers and revisions.
4. Resolve departments and owners.
5. Identify duplicates.
6. Validate status.
7. Import transactionally.
8. Verify controlled records.
9. Verify rejected-row export.

END-TO-END TEST 4: PPAP PACKAGE

1. Create original organization-authored PPAP package.
2. Configure required elements.
3. Attach relevant QMS records.
4. Generate package index.
5. Route for review.
6. Approve package.
7. Export package.
8. Preserve source relationships.
9. Confirm no proprietary template content is embedded.

END-TO-END TEST 5: FORMULA PRESERVATION

1. Upload workbook containing formulas.
2. Detect formulas.
3. Classify functions.
4. Preserve original formulas.
5. Identify unsupported formulas.
6. Display warnings.
7. Convert supported cells.
8. Reimport controlled workbook.
9. Validate input cells.
10. Prevent altered protected structure.

END-TO-END TEST 6: SECURITY

1. Upload malformed Office package.
2. Upload suspicious macro workbook.
3. Upload file with external references.
4. Upload oversized archive.
5. Attempt unauthorized file access.
6. Attempt cross-site import.
7. Confirm all are rejected or safely quarantined.
8. Confirm security logging.

==================================================
SECTION 20
PERFORMANCE REQUIREMENTS
==================================================

Implement:

• Streaming uploads
• Chunked processing where appropriate
• Server-side pagination
• Background processing
• Processing timeouts
• Memory limits
• File-size limits
• Import row limits
• Batch operations
• Database bulk insert where safe
• Transaction chunking where atomicity permits
• Progress reporting
• Cancelation
• Retry
• Failed-job recovery

Do not load entire large workbooks or CSV files into memory when streaming processing is available.

Test:

• Large workbook
• Large CSV
• Complex DOCX
• Multi-page PDF
• Concurrent imports
• Import cancellation
• Job retry
• Database rollback

==================================================
SECTION 21
USER EXPERIENCE
==================================================

Create an original QualGuard IQ import experience.

Do not imitate competitor interfaces.

Provide:

• Import Center
• Drag-and-drop upload
• Recent imports
• Job status
• Progress indicator
• Conversion warnings
• Mapping workbench
• Before-and-after preview
• Formula inspector
• Structure tree
• Page or worksheet selector
• Error navigation
• Approval queue
• Import history
• Rollback action
• Help panel
• Accessibility
• Keyboard navigation
• Responsive design

Users must always know:

• What is being imported
• Where it will be imported
• What will be preserved
• What will change
• What is unsupported
• Which records will be created
• Which records will be updated
• Which records will be skipped
• Whether approval is required
• Whether rollback is available

==================================================
SECTION 22
DOCUMENTATION
==================================================

Create:

• Import Center User Guide
• Word Import Guide
• Excel Import Guide
• CSV Import Guide
• PDF Intake Guide
• Formula Compatibility Guide
• Security Guide
• Mapping Guide
• Template Administrator Guide
• Template Package Guide
• Master List Migration Guide
• Rollback Guide
• Troubleshooting Guide
• API Guide
• Architecture Decision Record
• Supported Feature Matrix
• Known Limitations

Document unsupported content clearly.

Do not claim perfect fidelity for every Office feature.

==================================================
DEFINITION OF DONE
==================================================

This implementation is complete only when:

• Original files are preserved
• Word formatting is retained where supported
• Excel formulas are preserved
• Unsupported formulas are identified
• CSV mapping works
• PDF intake works
• OCR adapter boundary exists
• Templates are revision controlled
• Completed forms remain linked to revisions
• Imports are permission controlled
• Imports are auditable
• Imports are transactional
• Rollback works where supported
• Mapping previews work
• Fidelity comparison works
• Master lists can be migrated
• Detailed enterprise templates exist
• Template packages can be installed
• APIs are documented
• Security controls work
• Performance limits are enforced
• Automated tests pass
• Browser tests pass
• Existing tests continue to pass
• No proprietary competitor content is copied
• No copyrighted standard text is reproduced
• No placeholder import claims remain

==================================================
FINAL DELIVERABLES
==================================================

Provide:

• Working source code
• Database migrations
• Template schemas
• Import APIs
• Updated OpenAPI contract
• Template catalogs
• Original seed templates
• Synthetic test fixtures
• Import test results
• Security test results
• Performance test results
• Mapping documentation
• Formula compatibility report
• Word compatibility report
• Excel compatibility report
• PDF compatibility report
• Migration guide
• Administrator guide
• User guide
• Architecture documentation
• Known limitations
• Requirements traceability matrix
• Final acceptance report

The requirements traceability matrix must connect:

Requirement
→ Service
→ Database entity
→ API endpoint
→ User interface
→ Permission
→ Background job
→ Audit event
→ Automated test
→ Implementation status

==================================================
FINAL EXECUTION INSTRUCTION
==================================================

Begin by inspecting the existing QualGuard IQ implementation.

Reuse all existing shared platform services.

Build the Enterprise Template Library, Import Center, Office-document processing pipeline, formula-preservation system, high-fidelity rendering architecture, master-list migration tools, controlled template packages, original detailed form templates, and any verified missing enterprise QMS capabilities.

Do not stop with a plan.

Do not create demonstration pages.

Do not create superficial forms.

Do not flatten complex documents unnecessarily.

Do not discard formulas or formatting silently.

Do not copy competitor designs or proprietary templates.

Implement the features, database migrations, APIs, security controls, import workflows, previews, approvals, tests, documentation, and acceptance validation required to make this functionality production ready.
