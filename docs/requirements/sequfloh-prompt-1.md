MASTER BUILD PROMPT 1 OF 2
ADD SEQUFLOH TO QUALGUARD IQ
MODULE FOUNDATION, CONFIGURATION, PARTS, AND CONTROLLED WORK INSTRUCTIONS

You are modifying and expanding an existing Quality Management System named:

QUALGUARD IQ

Add a new, fully integrated application module named:

SEQUFLOH
Enterprise Execution Engine

SEQUFLOH must operate as a separate functional module within QualGuard IQ, not as a separate standalone application.

The complete current QualGuard IQ source code will be provided directly after this prompt.

This is Prompt 1 of 2. Complete all requirements assigned to this prompt before beginning Prompt 2.

==================================================
1. CRITICAL INTEGRATION REQUIREMENT
==================================================

SEQUFLOH must be incorporated into the existing QualGuard IQ QMS.

Do not build a separate application shell, separate login page, separate account system, or separate user database.

The existing QualGuard IQ application remains the parent system.

SEQUFLOH must use and preserve:

- the existing QualGuard IQ login
- the existing authenticated user session
- the existing QualGuard IQ user profile
- the existing QualGuard IQ role model
- the existing QualGuard IQ permission model
- the existing organization and site context
- the existing application shell
- the existing sidebar or navigation system
- the existing header
- the existing responsive behavior
- the existing notification system
- the existing audit system
- the existing settings architecture
- the existing database connection
- the existing theme and visual language

Do not create:

- a SEQUFLOH sign-in page
- a second Firebase Authentication implementation
- a separate user-registration system
- a separate password-reset system
- a separate password-change workflow
- a separate session manager
- a separate identity provider
- a second application shell
- a separate top-level application
- a second QMS system
- duplicate organization or site records

Authentication is handled entirely by the existing QualGuard IQ login.

When a user opens SEQUFLOH, obtain the current authenticated user and authorization context from the existing QualGuard IQ application.

If the existing source uses an application service, global state, current-user object, auth listener, or session helper, reuse it.

Do not replace or duplicate it.

==================================================
2. NON-NEGOTIABLE PRESERVATION RULE
==================================================

Inspect the complete current QualGuard IQ repository before changing anything.

Preserve:

- current project architecture
- current technology stack
- current database
- current authentication
- current user administration
- current role and permission system
- current navigation structure
- current modules
- current routes or view controls
- current naming conventions
- current utilities
- current reusable components
- current styling system
- current responsive behavior
- current audit methods
- current export methods
- current notification methods
- current printing methods
- current application configuration

Do not rebuild QualGuard IQ.

Do not replace working modules.

Do not rename existing modules, collections, tables, services, routes, folders, or application objects unless required to resolve an actual conflict.

Do not move unrelated files.

Do not introduce a parallel architecture.

Extend the current system incrementally.

If existing QualGuard IQ services already provide a required capability, integrate with those services instead of creating duplicate functionality.

Examples include:

- users
- roles
- permissions
- parts
- suppliers
- departments
- sites
- document control
- approvals
- audit events
- notifications
- exports
- attachments
- numbering
- reference data
- reporting
- printing

==================================================
3. STACK RESTRICTION
==================================================

Use only technologies already present in the supplied QualGuard IQ source.

Do not introduce a new framework, database, authentication system, storage service, UI library, build system, or backend service.

If QualGuard IQ currently uses:

- HTML5
- Tailwind CSS
- vanilla JavaScript
- JavaScript ES modules
- native browser APIs
- Firebase
- Cloud Firestore
- SheetJS
- browser printing
- local browser state for temporary interface state

continue using those technologies exactly as implemented in the existing application.

Do not add or recommend:

- React unless already used by QualGuard IQ
- TypeScript unless already used by QualGuard IQ
- Vue
- Angular
- Svelte
- Next.js
- Nuxt
- Node.js unless already part of QualGuard IQ
- Express
- Vite unless already part of QualGuard IQ
- Webpack unless already part of QualGuard IQ
- npm packages not already approved in the repository
- Supabase
- SQL
- PostgreSQL
- MySQL
- MongoDB
- Bootstrap
- jQuery
- Material UI
- Shadcn
- additional authentication providers
- replacement databases
- replacement storage services
- PDF-generation libraries
- replacement spreadsheet libraries

The implementation must fit the existing QualGuard IQ architecture.

==================================================
4. SEQUFLOH MODULE IDENTITY
==================================================

Use the module name exactly as follows:

Display name:
SEQUFLOH

Subtitle:
Enterprise Execution Engine

Parent application:
QualGuard IQ

SEQUFLOH must be presented as a controlled module within QualGuard IQ.

Do not rename QualGuard IQ.

Do not rename SEQUFLOH.

Do not treat SEQUFLOH as the parent application.

Use a module namespace or equivalent consistent with the current repository to prevent naming conflicts.

Recommended logical module identifier:

sequfloh

Recommended module description:

“Integrated execution, tracking, validation, inspection, repair, production, and controlled work instruction management.”

Add appropriate SEQUFLOH module access to the existing QualGuard IQ navigation and permissions architecture.

==================================================
5. SOURCE INSPECTION AND REPAIR
==================================================

Before adding the module, inspect the supplied source for:

- broken HTML
- malformed script tags
- malformed imports
- invalid URLs
- encoded executable markup
- missing element IDs
- duplicate element IDs
- undefined functions
- null DOM references
- incomplete event listeners
- unsafe HTML rendering
- incomplete Firestore error handling
- authorization gaps
- timestamp defects
- navigation defects
- modal defects
- duplicate submission risks
- inconsistent collection names
- incompatible application-state assumptions

If older SEQUFLOH prototype code is also supplied, repair and adapt it to work inside QualGuard IQ.

The older source may contain encoded entities such as:

- &lt;
- &gt;
- &amp;
- &quot;

Restore executable markup where appropriate.

Do not overwrite the current QualGuard IQ configuration with configuration from an older SEQUFLOH prototype.

QualGuard IQ configuration is authoritative.

==================================================
6. MODULE ENTRY AND NAVIGATION
==================================================

Add SEQUFLOH as a main module entry within the existing QualGuard IQ navigation.

When opened, SEQUFLOH must appear inside the existing application workspace.

Do not create a separate full-screen application unless the existing QualGuard IQ architecture already uses full-screen module workspaces.

Create this SEQUFLOH navigation hierarchy:

SEQUFLOH HOME

LIVE EXECUTION
- Dispatch Queue
- Active Station
- Paused Tasks
- Completed Tasks
- My Assigned Work
- Overdue Work
- Work History

TRACKING
- Validation Tracking
- FAI Tracking
- Repair Tracking
- Production Tracking
- Open Activities
- Completed Activities
- On-Hold Activities
- Rejected Activities

WORK INSTRUCTIONS
- All Work Instructions
- Validation Work Instructions
- FAI Work Instructions
- Repair Test Instructions
- Repair Work Instructions
- Draft Instructions
- Instructions Pending Review
- Instructions Pending Approval
- Released Instructions
- Obsolete Instructions
- Templates

PARTS AND ITEMS
- Part Master
- Parts Lists
- Repair Parts
- Assemblies
- Reusable Operation Steps
- Revision History

QUALITY PERSONNEL
- Quality Dashboard
- Validation Work Instructions
- Repair Work Instructions
- Quality Review Queue

ENGINEERING PERSONNEL
- Engineering Dashboard
- Validation
- Development
- Engineering Review Queue

REPORTS
- Task Performance
- Validation Status
- FAI Status
- Repair Status
- Work Instruction Status
- User Activity
- Time Variance
- Export Center

AUDIT TRAIL
- System Audit Events
- Work Instruction History
- User Access History
- Record Change History
- Export History

SEQUFLOH ADMINISTRATION
- Module Access
- Roles and Permissions
- Reference Data
- Dropdown Management
- Application Configuration

SEQUFLOH SETTINGS
- General Settings
- Work Instruction Settings
- Validation Settings
- FAI Settings
- Repair Settings
- Tracking Settings
- Notification Settings
- Numbering Settings
- Display Preferences
- Export Settings
- Security Settings

If QualGuard IQ already has shared Administration, Settings, Audit Trail, Parts, Reports, or User Management modules, do not duplicate the parent modules.

Instead:

- add SEQUFLOH-specific tabs or subsections
- reuse shared records
- link to existing records
- add only SEQUFLOH-specific configuration
- preserve the current QualGuard IQ hierarchy

==================================================
7. APPLICATION WORKSPACE
==================================================

Within the current QualGuard IQ shell, the SEQUFLOH workspace must provide:

- module dashboard
- page title
- page description
- breadcrumbs
- current module indicator
- organization context
- site context where supported
- current user display through QualGuard IQ
- current role display through QualGuard IQ
- environment badge where supported
- module notifications
- contextual actions
- global or module search
- recent SEQUFLOH items
- pinned SEQUFLOH items
- responsive tables
- responsive forms
- loading states
- empty states
- error states
- success notifications
- confirmation dialogs
- unsaved-change warnings

Use existing QualGuard IQ components and utilities wherever available.

==================================================
8. QUALGUARD IQ USER AND PERMISSION INTEGRATION
==================================================

Do not create a second SEQUFLOH user-administration system.

Use existing QualGuard IQ users.

Extend the existing permission model with SEQUFLOH capabilities such as:

- sequfloh_access
- sequfloh_view_dashboard
- sequfloh_view_live_execution
- sequfloh_dispatch_task
- sequfloh_start_task
- sequfloh_pause_task
- sequfloh_resume_task
- sequfloh_hold_task
- sequfloh_complete_task
- sequfloh_fail_task
- sequfloh_reject_task
- sequfloh_reassign_task
- sequfloh_cancel_task
- sequfloh_create_work_instruction
- sequfloh_edit_draft_work_instruction
- sequfloh_submit_work_instruction
- sequfloh_review_work_instruction
- sequfloh_approve_work_instruction
- sequfloh_release_work_instruction
- sequfloh_revise_work_instruction
- sequfloh_obsolete_work_instruction
- sequfloh_archive_work_instruction
- sequfloh_restore_work_instruction
- sequfloh_create_validation_record
- sequfloh_create_fai_record
- sequfloh_create_repair_record
- sequfloh_create_production_task
- sequfloh_edit_parts
- sequfloh_manage_parts_lists
- sequfloh_manage_operation_steps
- sequfloh_view_quality_folder
- sequfloh_edit_quality_folder
- sequfloh_view_engineering_folder
- sequfloh_edit_engineering_folder
- sequfloh_view_reports
- sequfloh_export_records
- sequfloh_view_audit_trail
- sequfloh_manage_settings
- sequfloh_manage_dropdowns
- sequfloh_manage_module_access

Map these permissions to the existing QualGuard IQ roles wherever practical.

Do not replace existing role definitions.

Allow authorized administrators to add SEQUFLOH permissions to existing roles.

Every protected page, navigation item, action, button, and write operation must check the existing QualGuard IQ authorization context.

Hiding a button is not sufficient.

Validate permission again before every protected database write.

==================================================
9. SEQUFLOH SETTINGS
==================================================

Create SEQUFLOH settings inside the existing QualGuard IQ settings architecture.

GENERAL:

- module enabled
- module display name
- module description
- default landing page
- records per page
- default site
- default department
- recent-items limit
- favorites enabled

NUMBERING:

- work instruction prefix
- validation instruction prefix
- FAI instruction prefix
- repair test prefix
- repair work instruction prefix
- validation record prefix
- FAI record prefix
- repair record prefix
- production task prefix
- parts-list prefix
- sequence padding
- numbering pattern
- revision format

WORK INSTRUCTIONS:

- instruction types
- lifecycle statuses
- required reviews
- approval requirements
- required review roles
- effective-date requirements
- revision requirements
- training requirements
- obsolete-document retention
- print footer
- approval statement

TRACKING:

- activity statuses
- priority levels
- hold reasons
- rejection reasons
- cancellation reasons
- variance thresholds
- default minimum time
- default maximum time
- overdue thresholds
- required signoffs

VALIDATION:

- validation types
- result options
- status options
- evidence requirements
- review requirements
- approval requirements

FAI:

- FAI types
- inspection result options
- characteristic result options
- approval requirements
- rejection reasons

REPAIR:

- repair types
- repair categories
- repair descriptions
- repair dispositions
- repair-test results
- failure reasons
- technician qualification labels

DISPLAY:

- compact tables
- dashboard-card visibility
- default filters
- default folder view
- status-color mappings consistent with QualGuard IQ

EXPORT:

- filename patterns
- included columns
- export date format
- organization heading
- default report titles

Record setting changes through the existing QualGuard IQ audit service.

==================================================
10. REFERENCE DATA
==================================================

Add SEQUFLOH reference data to the existing QualGuard IQ reference-data architecture.

Support:

- work instruction types
- lifecycle statuses
- validation types
- validation results
- FAI types
- inspection results
- repair types
- repair categories
- repair descriptions
- repair dispositions
- repair failure reasons
- priorities
- hold reasons
- rejection reasons
- cancellation reasons
- units of measure
- qualification labels
- operation categories
- equipment types
- approval action types
- document types

Authorized users must be able to:

- create
- edit
- activate
- deactivate
- reorder
- mark defaults
- search
- filter
- review history

Seed values must remain editable. Do not permanently hard-code dropdown values.

Reuse existing QualGuard IQ reference records where the values already exist.

==================================================
11. PART MASTER INTEGRATION
==================================================

If QualGuard IQ already has a Part Master, extend and reuse it.

Do not create a competing Part Master.

Ensure SEQUFLOH can use:

- part number
- description
- revision
- product family
- item type
- assembly type
- unit of measure
- active status
- repairable status
- serialized status
- lot-controlled status
- drawing number
- specification
- created by
- created date
- updated by
- updated date

Provide:

- create where permitted
- view
- edit
- deactivate
- reactivate
- search
- filter
- sort
- pagination
- revision history
- related work instructions
- related validation records
- related FAI records
- related repair records
- related production records
- export integration

Do not destructively delete parts linked to retained records.

==================================================
12. PARTS LISTS
==================================================

Create or extend Parts List management.

Fields:

- list number
- title
- applicable parent part
- revision
- status
- description
- list type
- created by
- created date
- updated by
- updated date
- approved by
- approval date
- items
- revision history

List types:

- standard parts list
- repair kit
- assembly bill of materials
- repair parts list
- custom list

Each item:

- item number
- part number
- description
- quantity
- unit
- required or optional
- alternate part
- traceability requirement
- notes

Functions:

- create
- view
- edit
- duplicate
- revise
- deactivate
- reactivate
- search
- filter
- sort
- paginate
- add item
- edit item
- duplicate item
- reorder item
- remove item with confirmation
- copy from another list
- preserve revision history

==================================================
13. REUSABLE OPERATION STEPS
==================================================

Create reusable operation-step templates.

Fields:

- operation-step number
- title
- category
- detailed action
- default tools
- default parts
- specification
- target
- minimum
- maximum
- unit
- safety warning
- quality checkpoint
- expected result
- evidence requirement
- required role
- required qualification
- active status
- display order
- created and updated metadata

Functions:

- create
- edit
- duplicate
- activate
- deactivate
- reorder
- search
- filter
- view history

When copied into an instruction, the copied step becomes instruction-specific.

Editing an instruction-specific copy must not silently change the master template.

==================================================
14. CONTROLLED NUMBERING AND REVISION UTILITIES
==================================================

Create configurable numbering for:

- work instructions
- validation instructions
- FAI instructions
- repair test instructions
- repair work instructions
- validation records
- FAI records
- repair records
- production tasks
- parts lists

Support patterns such as:

PREFIX-YEAR-SEQUENCE

Do not hard-code the final format.

Use the existing QualGuard IQ numbering service if available.

Otherwise create a transaction-safe numbering service consistent with the existing database.

Protect against duplicate numbers using atomic database operations supported by the current stack.

Support configurable revisions:

- A, B, C
- 00, 01, 02

Never overwrite a previously released revision.

==================================================
15. COMPLETE WORK INSTRUCTION SYSTEM
==================================================

Create these instruction types:

1. Validation
2. Validation / FAI
3. FAI
4. Repair Test
5. Repair Work Instruction

The form must begin with:

WORK INSTRUCTION TYPE

The selected type must open the correct designer.

All fields must remain editable when:

- the current lifecycle status permits editing
- the current QualGuard IQ user has permission

Released records must be read-only.

Editing a released record must create a controlled revision.

==================================================
16. WORK INSTRUCTION LIFECYCLE
==================================================

Statuses:

- Draft
- In Development
- Pending Review
- Changes Requested
- Pending Approval
- Approved
- Released
- Superseded
- Obsolete
- Archived

Actions:

- Create
- Save Draft
- Edit
- Duplicate
- Submit for Review
- Return for Changes
- Submit for Approval
- Approve
- Reject
- Release
- Create Revision
- Supersede
- Mark Obsolete
- Archive
- Restore
- Print
- Export
- View History

Each transition must record:

- previous status
- new status
- action
- user
- date and time
- comment
- revision
- affected record ID

Require comments for:

- rejection
- return for changes
- superseding
- obsolescence
- approval reversal
- administrative override

Validate the latest database state before transitions.

Prevent invalid or duplicate transitions.

==================================================
17. COMMON WORK INSTRUCTION CONTENT
==================================================

Every instruction must support:

DOCUMENT CONTROL:

- internal record ID
- instruction number
- instruction type
- title
- revision
- status
- effective date
- superseded date
- obsolete date
- department
- site
- owner
- author
- reviewer
- approver
- created date
- updated date
- approved date
- released date

APPLICABILITY:

- part number
- part description
- product family
- assembly
- model
- variant
- process
- operation
- workstation
- location

PURPOSE AND SCOPE:

- purpose
- scope
- intended use
- exclusions
- process boundaries

RESPONSIBILITY:

- responsible department
- required role
- required qualification
- responsible personnel
- supervisor responsibility
- Quality responsibility
- Engineering responsibility

SAFETY:

- safety warnings
- hazards
- PPE
- lockout or isolation requirements
- handling precautions
- environmental precautions
- critical safety notes

TOOLS AND EQUIPMENT:

- required tools
- tool identification
- equipment
- software or test equipment
- calibration requirement
- calibration verification
- fixtures
- gauges
- consumables

MATERIALS AND PARTS:

- required materials
- required components
- replacement parts
- quantities
- units
- approved alternates
- lot-traceability requirement

PRECONDITIONS:

- prerequisite records
- required approvals
- receiving condition
- setup requirements
- environmental conditions
- initial inspection
- required documentation

PROCEDURE STEPS:

- sequence number
- step title
- detailed instruction
- specification
- target
- lower limit
- upper limit
- unit
- tool
- safety warning
- quality checkpoint
- expected result
- actual-result entry requirement
- evidence requirement
- visual-aid reference
- notes

ACCEPTANCE:

- acceptance criteria
- rejection criteria
- pass or fail rules
- required inspection
- required validation
- required test
- escalation instructions
- nonconformance instructions

RECORDS:

- records generated
- retention requirement
- result fields
- signoff requirements
- evidence references
- traceability requirements

APPROVAL:

- prepared by
- reviewed by
- approved by
- electronic acknowledgement
- approval date
- comments

Allow users to:

- add steps
- edit steps
- duplicate steps
- move steps up
- move steps down
- drag or otherwise reorder where supported
- delete steps with confirmation

Use explicit sequence numbers.

Validate sequence order before approval or release.

==================================================
18. VALIDATION WORK INSTRUCTION DESIGNER
==================================================

Create a dedicated Validation Work Instruction designer.

Include:

VALIDATION IDENTITY:

- validation instruction number
- title
- validation type
- related project
- related change
- related part
- related assembly
- requested by
- validation owner
- validation location
- planned date
- target completion date

OBJECTIVE:

- validation objective
- requirement being tested
- intended conclusion
- scope
- assumptions
- exclusions
- known risks

REFERENCES:

- drawing reference
- drawing revision
- specification reference
- Engineering document reference
- related work instruction
- test standard
- protocol reference
- change record reference

SAMPLES:

- sample quantity
- selection method
- sample source
- sample condition
- lot-number requirement
- serial-number requirement
- control-sample requirement
- comparison-sample requirement

EQUIPMENT:

- equipment name
- equipment ID
- calibration status
- calibration due date
- fixture
- software or firmware version
- environmental-monitoring equipment

CONDITIONS:

- temperature
- humidity
- voltage
- pressure
- load
- speed
- cycle count
- duration
- conditioning time
- other controlled conditions

EACH VALIDATION STEP:

- sequence number
- title
- detailed action
- required setup
- input condition
- characteristic evaluated
- specification
- target
- lower limit
- upper limit
- unit
- sample size
- equipment
- method
- evidence required
- expected result
- actual-result field
- pass or fail requirement
- deviation instructions
- responsible role
- required signoff

ACCEPTANCE:

- overall acceptance criteria
- individual characteristic rules
- allowed failures
- retest rules
- deviation rules
- approval conditions
- rejection conditions

RESULTS:

- result status
- actual result
- pass
- fail
- conditional acceptance
- deviation number
- nonconformance reference
- evidence
- technician comments
- Quality comments
- Engineering comments
- conclusion
- recommended action

APPROVAL:

- performed by
- reviewed by Quality
- reviewed by Engineering
- approved by
- dates
- comments
- final disposition

Do not allow release without steps and acceptance criteria.

==================================================
19. FAI WORK INSTRUCTION DESIGNER
==================================================

Create a dedicated FAI designer.

Fields:

- FAI instruction number
- FAI type
- full FAI
- partial FAI
- delta FAI
- reason for FAI
- part number
- part revision
- drawing number
- drawing revision
- purchase order
- manufacturing order
- supplier
- lot number
- serial number
- sample quantity
- inspection location
- inspector
- required completion date

Each characteristic:

- characteristic number
- drawing zone
- requirement
- specification
- target
- lower limit
- upper limit
- unit
- inspection method
- gauge or equipment
- equipment ID
- sample quantity
- actual result
- pass
- fail
- not applicable
- evidence reference
- notes
- nonconformance reference

Allow characteristics to be:

- added
- edited
- duplicated
- reordered
- moved up or down
- deleted with confirmation
- imported from existing application data
- copied from another revision

Include:

- material verification
- special-process verification
- certificate review
- drawing balloon reference
- dimensional inspection
- visual inspection
- functional inspection
- final disposition
- Quality approval
- Engineering approval when required

Do not allow release without valid characteristics.

==================================================
20. REPAIR TEST INSTRUCTION DESIGNER
==================================================

Create a dedicated Repair Test designer.

Fields:

- instruction number
- title
- applicable part
- applicable assembly
- repair category
- detected condition
- test purpose
- pre-test inspection
- test equipment
- equipment ID
- calibration requirement
- fixture
- setup conditions
- safety precautions
- test parameters
- expected results
- pass criteria
- fail criteria
- retest rules
- escalation instructions
- evidence requirements
- technician signoff
- Quality signoff when required

Each test step:

- sequence number
- title
- detailed action
- equipment
- parameter
- target
- minimum
- maximum
- unit
- expected result
- actual-result field
- pass or fail
- evidence requirement
- notes

==================================================
21. REPAIR WORK INSTRUCTION DESIGNER
==================================================

Create a separate Repair Work Instruction designer.

Do not combine it with the Validation designer.

Required controlled dropdowns:

1. Part being repaired
2. Repair description
3. Operation steps
4. Parts list

PART:

Populate from the shared QualGuard IQ Part Master.

Display:

- part number
- description
- current revision
- product family
- assembly classification
- active status

REPAIR DESCRIPTIONS:

Seed editable options such as:

- inspection and diagnosis
- disassembly
- cleaning
- component replacement
- adjustment
- rework
- electrical repair
- mechanical repair
- cosmetic repair
- leak correction
- connector repair
- fastener replacement
- functional restoration
- final testing
- other controlled repair

OPERATION STEPS:

Copy selected reusable operation-step data into the current instruction.

Editing the copied data must not alter the master template unless an authorized user explicitly updates the template.

PARTS LIST:

Allow selection of:

- existing parts list
- repair kit
- assembly bill of materials
- repair parts
- individually selected parts
- blank custom list

Copy selected list data into the instruction without linking edits back to the master record.

REPAIR DOCUMENT IDENTITY:

- instruction number
- title
- revision
- status
- repair category
- repair type
- department
- owner
- author
- reviewer
- approver

PART IDENTIFICATION:

- part being repaired
- part number
- description
- revision
- assembly
- product family
- serial-number requirement
- lot-number requirement
- customer-return reference
- repair-order reference

REPAIR CONDITION:

- reported problem
- observed condition
- failure description
- defect location
- defect category
- initial inspection
- visual-evidence references
- disposition before repair

REPAIR DESCRIPTION:

- controlled dropdown
- detailed description
- repair objective
- expected restored condition
- limitations
- prohibited actions

SAFETY:

- hazards
- PPE
- stored-energy precautions
- electrical precautions
- mechanical precautions
- chemical precautions
- handling requirements
- contamination controls

TOOLS AND EQUIPMENT:

- tools
- fixtures
- gauges
- test equipment
- equipment IDs
- calibration requirements
- consumables
- approved chemicals
- torque tools

PARTS LIST ROWS:

- item number
- replacement part number
- description
- quantity
- unit
- required or optional
- approved alternate
- lot number required
- serial number required
- issued quantity
- used quantity
- returned quantity
- scrap quantity

EACH REPAIR STEP:

- sequence number
- operation-step dropdown
- title
- detailed repair action
- tools required
- parts consumed
- quantity consumed
- specification
- torque value
- minimum
- maximum
- unit
- visual checkpoint
- quality checkpoint
- expected result
- evidence requirement
- warning
- technician-result field
- pass or fail
- technician acknowledgement
- inspector verification
- notes

POST-REPAIR:

- visual inspection
- dimensional inspection
- electrical test
- mechanical test
- functional test
- leak test
- load test
- cycle test
- final cleaning
- labeling
- packaging
- final disposition

ACCEPTANCE:

- restored condition
- inspection requirements
- test requirements
- allowable conditions
- rejection conditions
- rework rules
- escalation requirements
- nonconformance instructions

APPROVAL:

- performed by
- inspected by
- reviewed by
- approved by
- dates
- comments
- repair disposition

Do not release a Repair Work Instruction without operation steps or when required parts information is incomplete.

==================================================
22. SEPARATE VALIDATION AND REPAIR FOLDERS
==================================================

VALIDATION FOLDER COLUMNS:

- instruction number
- title
- part
- validation type
- revision
- status
- owner
- reviewer
- approver
- effective date
- last updated
- result status
- actions

Filters:

- Draft
- Pending Review
- Pending Approval
- Released
- Superseded
- Obsolete
- Pass
- Fail
- Conditional

REPAIR FOLDER COLUMNS:

- instruction number
- title
- part being repaired
- repair description
- repair category
- revision
- status
- owner
- reviewer
- approver
- effective date
- last updated
- actions

Filters:

- Draft
- Pending Review
- Pending Approval
- Released
- Superseded
- Obsolete
- Repair category
- Part number
- Repair type

Both folders must provide:

- search
- filtering
- sorting
- pagination
- clear filters
- new instruction
- open
- edit
- duplicate
- create revision
- print
- export integration
- history
- archive
- loading state
- empty state
- error state

All actions must be functional and permission-controlled.

==================================================
23. PRINTABLE WORK INSTRUCTIONS
==================================================

Use the existing QualGuard IQ printing method.

Create controlled printable layouts for:

- Validation Work Instruction
- FAI Work Instruction
- Repair Test Instruction
- Repair Work Instruction

Include:

- QualGuard IQ organization heading
- SEQUFLOH module identification
- document title
- document number
- revision
- status
- effective date
- part applicability
- detailed content
- numbered steps
- characteristics where applicable
- repair parts where applicable
- approvals
- revision history
- controlled-copy footer

Hide application navigation and controls when printing.

Do not add a PDF library.

==================================================
24. DATA MODEL
==================================================

Integrate SEQUFLOH records into the existing QualGuard IQ data architecture.

Use existing shared collections or tables where appropriate.

Where dedicated records are required, use clear SEQUFLOH-scoped names consistent with the repository, such as:

- sequfloh_settings
- sequfloh_numbering_sequences
- sequfloh_reference_data
- sequfloh_parts_lists
- sequfloh_operation_steps
- sequfloh_work_instructions
- sequfloh_work_instruction_revisions
- sequfloh_approval_actions
- sequfloh_notifications

Do not create duplicate shared-users, shared-parts, shared-suppliers, shared-sites, or shared-departments collections when QualGuard IQ already has them.

All applicable records must use the existing QualGuard IQ metadata conventions, including equivalents of:

- createdAt
- createdBy
- updatedAt
- updatedBy

Use server timestamps where supported.

Keep database IDs separate from controlled display numbers.

Do not store large base64 images as production records.

Use the existing QualGuard IQ attachment or document-reference capability if available.

If no approved attachment capability exists, use controlled text references or approved links. Do not introduce a new storage service.

==================================================
25. AUDIT INTEGRATION
==================================================

Use the existing QualGuard IQ audit system.

Add SEQUFLOH audit events for:

- module access
- settings changes
- reference-data changes
- parts-list changes
- operation-template changes
- instruction creation
- editing
- submission
- review
- approval
- rejection
- release
- revision
- superseding
- obsolescence
- archival
- restoration
- printing
- export request

Audit fields must follow the existing QualGuard IQ audit schema.

Include sufficient SEQUFLOH context:

- module
- record type
- record ID
- display number
- previous status
- new status
- previous revision
- new revision
- summary
- reason or comment
- changed fields when available

Do not create editable audit records.

==================================================
26. REQUIRED REUSABLE UTILITIES
==================================================

Use existing utilities where available.

Add only missing helpers for:

- safe DOM selection
- HTML escaping
- safe rendering
- form reading
- form population
- validation
- error summaries
- date formatting
- notifications
- modal handling
- confirmation handling
- permission checks
- audit logging
- table rendering
- filtering
- sorting
- pagination
- numbering
- revision creation
- loading states
- empty states
- duplicate-submission prevention
- unsaved-change warnings

Do not use unsafe HTML interpolation for untrusted database content.

==================================================
27. OUTPUT REQUIREMENTS
==================================================

Return a complete functional implementation for Prompt 1.

Do not provide:

- pseudocode
- abbreviated examples
- placeholder comments
- incomplete stubs
- decorative buttons
- skipped functions
- “continue here” markers
- framework conversion recommendations
- separate sign-in code
- duplicate user administration
- an unfinished skeleton

First output:

1. QualGuard IQ integration findings
2. Existing services and modules that will be reused
3. Files being created or modified
4. Updated module and folder structure
5. SEQUFLOH data model
6. SEQUFLOH permission matrix
7. Work instruction lifecycle matrix
8. Complete code beginning with the module integration

If the implementation requires multiple responses:

- divide it into numbered consecutive parts
- identify files in each part
- provide complete file contents
- avoid repeating completed files
- never stop in the middle of a function, object, class, HTML element, database rule, or file

At completion provide:

- configuration instructions
- initial reference-data instructions
- work instruction testing checklist
- integration checklist
- complete list of files ready for Prompt 2
