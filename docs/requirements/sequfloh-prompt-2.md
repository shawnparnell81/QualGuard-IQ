MASTER BUILD PROMPT 2 OF 2
ADD SEQUFLOH TO QUALGUARD IQ
PERSONNEL FOLDERS, TRACKING, LIVE EXECUTION, REPORTS, SECURITY, AND FINAL INTEGRATION

Continue modifying the same QualGuard IQ repository completed under Prompt 1.

The complete current QualGuard IQ and SEQUFLOH source will be pasted directly after this prompt.

Do not rebuild QualGuard IQ or SEQUFLOH.

Preserve all completed Prompt 1 functionality.

SEQUFLOH remains a separate module inside QualGuard IQ.

QualGuard IQ remains the parent QMS.

==================================================
1. CRITICAL LOGIN AND USER REQUIREMENT
==================================================

Do not create or modify a separate SEQUFLOH login.

Authentication, user identity, session management, password handling, and account access are controlled by the existing QualGuard IQ login.

SEQUFLOH must consume the current QualGuard IQ user and authorization context.

Do not create:

- another sign-in page
- another registration page
- another password-reset screen
- another authentication listener unless required by the existing shared architecture
- another user collection
- a separate role system
- a separate session
- duplicate user administration

Use the QualGuard IQ user ID, display name, role, department, site, permissions, and other available profile information throughout SEQUFLOH records.

==================================================
2. PRESERVE THE CURRENT APPLICATION
==================================================

Do not:

- change the approved stack
- replace the application shell
- replace the database
- replace the audit system
- replace QualGuard IQ permissions
- rebuild completed modules
- rename completed files without necessity
- remove functioning QMS features
- add a parallel architecture
- install an unapproved package
- introduce a second QMS

Extend only what is necessary to complete SEQUFLOH.

==================================================
3. PRIMARY OBJECTIVE
==================================================

Complete:

- SEQUFLOH Home dashboard
- Quality Personnel folders
- Engineering Personnel folders
- Quality and Engineering review queues
- Validation Tracking
- FAI Tracking
- Repair Tracking
- Production Tracking
- Dispatch Queue
- Active Station
- persistent active tasks
- persistent paused tasks
- task events
- task timing
- concurrency protection
- operational reports
- Excel exports
- controlled printing
- complete audit views
- notifications
- module search
- recent items
- favorites
- database security requirements
- database indexes
- seed functions
- validation and error handling
- integration testing
- final acceptance testing
- deployment instructions

No required page may remain a placeholder.

No major button may remain decorative or nonfunctional.

==================================================
4. SEQUFLOH HOME DASHBOARD
==================================================

Create a permission-aware dashboard showing appropriate information such as:

- tasks awaiting dispatch
- active tasks
- paused tasks
- overdue tasks
- validation records in progress
- FAI records in progress
- repair records in progress
- production tasks in progress
- failed records
- pending reviews
- pending approvals
- recently released instructions
- recently revised instructions
- assigned work
- recent activity
- quick actions

Only show data the current QualGuard IQ user is authorized to view.

Use existing QualGuard IQ dashboard patterns where available.

==================================================
5. QUALITY PERSONNEL AREA
==================================================

Create:

- Quality Dashboard
- Validation Work Instructions
- Repair Work Instructions
- Quality Review Queue

Quality Dashboard must show:

- validation instructions pending review
- repair instructions pending review
- approvals awaiting Quality
- failed validations
- failed FAI records
- failed repair tests
- overdue quality actions
- recently released instructions
- recently revised instructions

Authorized Quality users must be able to:

- review instructions
- enter review comments
- return documents for changes
- approve when permitted
- reject with a required reason
- verify validation results
- verify FAI results
- verify repair results
- review evidence references
- inspect complete revision histories
- run status reports
- export permitted records
- manage assigned Quality actions

Do not expose Quality-only actions to unauthorized users.

Use the existing QualGuard IQ permissions extended by Prompt 1.

==================================================
6. ENGINEERING PERSONNEL AREA
==================================================

Create:

- Engineering Dashboard
- Validation
- Development
- Engineering Review Queue

ENGINEERING VALIDATION:

- validation work instructions
- validation plans
- open validation activities
- validation results
- failed validation records
- records requiring Engineering review
- released validation instructions
- revision history

ENGINEERING DEVELOPMENT:

- work instructions in development
- draft repair instructions
- draft validation instructions
- Engineering notes
- development status
- assigned engineer
- target completion date
- related part
- related project
- review status
- approval status

Authorized Engineering users must be able to:

- develop work instructions
- edit drafts
- create revisions
- submit for review
- respond to requested changes
- review validation results
- enter Engineering conclusions
- recommend release
- manage development records

Do not expose Engineering-only functions to unauthorized users.

==================================================
7. OPERATIONAL TRACKING RECORDS
==================================================

Create tracking records for:

- validation
- FAI
- repair
- production

Common fields:

- tracking record number
- tracking type
- related work instruction
- work instruction revision
- part number
- part description
- serial number
- lot number
- work order
- repair order
- project
- assigned user
- assigned department
- site
- status
- priority
- planned start
- actual start
- due date
- actual completion
- elapsed time
- paused time
- hold time
- minimum benchmark
- maximum benchmark
- variance
- variance reason
- result
- approval status
- evidence references
- technician comments
- Quality comments
- Engineering comments
- required signoffs
- created by
- created date
- updated by
- updated date

Statuses:

- Not Started
- Assigned
- In Progress
- Paused
- On Hold
- Pending Inspection
- Pending Review
- Completed
- Failed
- Rejected
- Canceled

Store the exact released Work Instruction revision selected for execution.

A later revision must not silently change historical tracking records.

==================================================
8. VALIDATION TRACKING
==================================================

Implement:

- create validation record
- select released Validation Work Instruction
- preserve selected revision
- select part
- identify samples
- record controlled conditions
- assign personnel
- record planned and actual dates
- load validation steps
- record step-level results
- record evidence references
- record deviations
- record nonconformance references
- calculate overall result
- submit for Quality review
- submit for Engineering review when required
- approve or reject results
- preserve result history

Results:

- Pending
- Pass
- Fail
- Conditional

Do not allow completion when required steps, results, evidence, comments, or signoffs are incomplete.

==================================================
9. FAI TRACKING
==================================================

Implement:

- create FAI record
- select released FAI Work Instruction
- preserve the selected revision
- copy applicable characteristics
- record characteristic results
- pass, fail, or not applicable
- evidence references
- nonconformance references
- material verification
- special-process verification
- certificate review
- drawing-balloon references
- dimensional results
- visual results
- functional results
- Quality review
- Engineering review when required
- final disposition
- complete history

Do not allow completion when required characteristics are incomplete.

==================================================
10. REPAIR TRACKING
==================================================

Implement:

- create repair record
- select released Repair Work Instruction
- preserve selected revision
- select repairable part
- capture serial and lot references
- capture customer-return reference
- capture repair-order reference
- record reported condition
- record observed condition
- copy repair description
- copy operation steps
- copy instruction parts list
- record issued quantities
- record used quantities
- record returned quantities
- record scrap quantities
- record operation-level results
- technician acknowledgements
- inspector verification
- repair-test results
- post-repair inspection
- evidence references
- final disposition
- failure handling
- rejection handling
- Quality approval when required

Validate material quantities.

Prevent completion when:

- mandatory operations are incomplete
- used quantities are invalid
- required tests are incomplete
- required signoffs are missing
- final disposition is missing

==================================================
11. PRODUCTION TRACKING
==================================================

Implement production tasks that can:

- select a part
- select a released Work Instruction
- preserve the selected revision
- capture work order
- select operation
- set priority
- assign user
- assign department
- assign site
- establish benchmark range
- dispatch
- start
- pause
- resume
- hold
- complete
- fail
- reject
- reassign
- cancel with reason

Record complete timing, state, result, signoff, evidence, and audit information.

==================================================
12. LIVE EXECUTION ENGINE
==================================================

Create or complete:

DISPATCH QUEUE:

- tasks awaiting start
- assignment
- priority
- due date
- part
- operation
- related instruction
- revision
- permission-controlled reassignment

ACTIVE STATION:

- active task
- tracking number
- Work Instruction number
- Work Instruction revision
- part
- operation
- assigned user
- status
- elapsed time
- target range
- priority
- quality checkpoints
- next required action
- technician comments
- result entry
- evidence references
- required signoffs

PAUSED TASKS:

- paused tasks
- pause reason
- paused timestamp
- accumulated paused duration
- permission-controlled resume

COMPLETED TASKS:

- completed
- failed
- rejected
- canceled
- result
- duration
- variance
- completion user and date

MY ASSIGNED WORK:

- current user’s assigned tasks
- status filtering
- priority filtering
- due-date filtering

OVERDUE WORK:

- configured overdue thresholds
- status
- assigned user
- department
- due date
- overdue duration

WORK HISTORY:

- completed execution history
- task events
- search
- filters
- sorting
- pagination

==================================================
13. TASK ACTIONS
==================================================

Support:

- dispatch
- assign
- start
- pause
- resume
- place on hold
- restart when authorized
- complete
- fail
- reject
- reassign
- cancel with reason
- record variance
- record result
- save evidence references
- capture comments
- capture required signoffs

Every action must:

- verify permission
- reload or validate current database state
- verify the transition is legal
- prevent duplicate submission
- disable controls while processing
- update the record
- append a task event
- create a QualGuard IQ audit event
- display success or error feedback

==================================================
14. PERSISTENT TASK TIMING
==================================================

Do not use an in-memory JavaScript timer as the record of truth.

Persist:

- actualStart
- actualCompletion
- currentState
- currentStateStartedAt
- accumulatedActiveDuration
- accumulatedPausedDuration
- accumulatedHoldDuration
- activeOperator
- state version
- last transition ID
- last transition timestamp

Use database timestamps to calculate elapsed time.

A browser interval may update the visible clock, but displayed time must be calculated from persisted timestamps and accumulated durations.

Refresh, logout, browser close, and reopening the application must not destroy the queue or reset task timing.

==================================================
15. CONCURRENCY AND DUPLICATE PROTECTION
==================================================

Use transactions or guarded state updates supported by the existing QualGuard IQ stack.

Prevent, as far as practical:

- duplicate task completion
- simultaneous resume by multiple users
- starting an already active task
- completing a canceled task
- resuming a completed task
- appending the same event twice
- conflicting reassignment
- stale state overwrites
- repeated form submissions
- duplicate number assignment

Use unique operation or transition IDs where practical.

Validate the stored state immediately before committing a transition.

==================================================
16. TASK EVENTS
==================================================

Create append-only events for:

- dispatch
- assignment
- start
- pause
- resume
- hold
- restart
- completion
- failure
- rejection
- reassignment
- cancellation
- inspection submission
- review submission
- approval
- authorized result reversal
- administrative override

Fields:

- event ID
- task ID
- tracking number
- event type
- previous status
- new status
- timestamp
- user ID
- user display name
- assigned user before
- assigned user after
- reason
- comment
- elapsed-duration snapshot
- paused-duration snapshot
- hold-duration snapshot
- transition operation ID

Task events and QualGuard IQ audit events are separate records. Create both where required.

==================================================
17. TRACKING FOLDERS
==================================================

Implement:

- Validation Tracking
- FAI Tracking
- Repair Tracking
- Production Tracking
- Open Activities
- Completed Activities
- On-Hold Activities
- Rejected Activities

Every major table must provide:

- search
- status filter
- type filter
- owner filter
- assigned-user filter
- part filter
- department filter
- site filter where applicable
- date filter
- priority filter
- result filter
- sorting
- pagination
- clear filters
- loading state
- empty state
- error state

==================================================
18. SEQUFLOH REPORTS
==================================================

Implement:

- Task Performance
- Validation Status
- FAI Status
- Repair Status
- Work Instruction Status
- User Activity
- Time Variance
- Export Center

TASK PERFORMANCE:

- task counts by status
- assigned users
- part
- operation
- planned dates
- actual dates
- benchmark ranges
- elapsed time
- variance
- completed, failed, rejected, and canceled results

VALIDATION STATUS:

- record number
- instruction and revision
- part
- owner
- planned date
- due date
- result
- review status
- approval status
- deviations

FAI STATUS:

- FAI number
- instruction and revision
- part
- FAI type
- inspection status
- characteristic completion
- result
- Quality approval
- Engineering approval

REPAIR STATUS:

- repair number
- instruction and revision
- part
- category
- technician
- status
- result
- test status
- inspection status
- disposition

WORK INSTRUCTION STATUS:

- instruction number
- type
- title
- revision
- status
- owner
- reviewer
- approver
- effective date
- last update

USER ACTIVITY:

- user
- role
- module
- action
- timestamp
- record reference
- summary

TIME VARIANCE:

- task
- type
- part
- operation
- minimum benchmark
- maximum benchmark
- elapsed time
- variance
- reason
- result

Do not invent metrics that cannot be calculated from stored records.

==================================================
19. EXCEL EXPORT
==================================================

Use the existing QualGuard IQ spreadsheet export service.

If the current application uses SheetJS XLSX 0.18.5, continue using that same version.

Export:

- filtered Work Instruction lists
- Validation Tracking
- FAI Tracking
- Repair Tracking
- Production Tracking
- Part Master
- Parts Lists
- task logs
- time variances
- audit trail
- user activity
- approval status

Include:

- report title
- organization
- generated date
- generated by
- active filters
- column headings
- formatted values
- worksheet name
- configurable filename

Export the full filtered result set, not only the visible page, unless the interface explicitly offers a current-page export.

Audit exports through QualGuard IQ.

==================================================
20. PRINTING
==================================================

Complete printable layouts for:

- Validation Work Instruction
- FAI Work Instruction
- Repair Test Instruction
- Repair Work Instruction
- Tracking Record
- Audit Record Summary

Use the existing QualGuard IQ browser-print method.

Hide navigation and action controls during printing.

Include:

- organization
- QualGuard IQ identification where appropriate
- SEQUFLOH module identification
- document title
- document number
- revision
- status
- effective date
- applicable part
- detailed content
- approvals
- revision history
- controlled-copy notice

Do not introduce another PDF system.

==================================================
21. AUDIT TRAIL
==================================================

Integrate these filtered SEQUFLOH views into the existing QualGuard IQ audit system:

- System Audit Events
- Work Instruction History
- User Access History
- Record Change History
- Export History

Audit:

- module access
- settings changes
- reference-data changes
- parts changes where performed through SEQUFLOH
- Parts List changes
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
- task dispatch
- start
- pause
- resume
- hold
- completion
- failure
- reassignment
- cancellation
- export
- print
- administrative override

Provide:

- search
- filters
- sorting
- pagination
- loading states
- empty states
- error states
- detail view
- printable summary

Do not permit normal users to edit or delete audit events.

==================================================
22. NOTIFICATIONS
==================================================

Integrate SEQUFLOH notifications into the existing QualGuard IQ notification system.

Include:

- assigned work
- pending review
- pending approval
- requested changes
- overdue work
- failed records requiring attention
- rejected records
- released instructions
- superseded instructions
- approaching due dates where configured

Notifications must honor module permissions and record access.

Do not create a conflicting second notification system.

==================================================
23. SEARCH, RECENT ITEMS, AND FAVORITES
==================================================

Integrate SEQUFLOH with the current QualGuard IQ search experience.

Search permitted records across:

- Work Instructions
- parts
- Parts Lists
- tracking records
- tasks
- users when authorized

Do not expose unauthorized results.

RECENT ITEMS:

Track recently opened SEQUFLOH records through the existing QualGuard IQ recent-items system when available.

FAVORITES:

Allow users to pin permitted SEQUFLOH records and navigation destinations through the existing preference system when available.

If no shared service exists, implement a SEQUFLOH-scoped preference structure linked to the current QualGuard IQ user ID.

==================================================
24. SECURITY INTEGRATION
==================================================

Extend the existing QualGuard IQ database security rules for SEQUFLOH.

Security must address:

- authenticated QualGuard IQ access
- active user profiles
- module access
- permission-based reads
- permission-based writes
- ownership where applicable
- protected settings
- protected permissions
- protected numbering sequences
- protected reference-data administration
- append-only audit events
- append-only task events
- restricted Work Instruction review
- restricted approval
- restricted release
- restricted revision creation
- restricted obsolescence
- restricted task transitions
- protected Quality functions
- protected Engineering functions
- protected exports where practical

Do not rely only on client-side controls.

Do not create a new authentication system.

Do not place confidential credentials in database records.

Where a complex workflow cannot be completely validated in security rules, use the strongest practical rule supported by the current architecture and document the limitation accurately.

==================================================
25. DATABASE INDEXES
==================================================

Provide only indexes required by implemented queries.

Include applicable indexes for:

- Work Instruction type and status
- Work Instruction owner and status
- Work Instruction updated date
- validation result and status
- FAI result and status
- repair result and status
- task assigned user and status
- task department and status
- task site and status
- task status and due date
- task type and status
- audit module and timestamp
- audit action and timestamp
- audit user and timestamp
- part active status and part number
- Parts List parent part and status
- approval status and assigned reviewer

Merge these with existing QualGuard IQ index configuration.

Do not overwrite unrelated indexes.

==================================================
26. SAFE INITIAL SEEDING
==================================================

Create idempotent seed functions for SEQUFLOH-specific data:

- module registration
- permissions
- role-permission suggestions
- settings
- numbering settings
- lifecycle statuses
- priorities
- validation types
- validation results
- FAI types
- inspection results
- repair types
- repair categories
- repair descriptions
- repair dispositions
- failure reasons
- hold reasons
- rejection reasons
- cancellation reasons
- units
- qualifications
- operation categories
- notification settings
- export settings

Do not create Authentication users.

Do not replace existing QualGuard IQ roles.

Do not overwrite administrator-modified values without an explicit authorized reset action and confirmation.

==================================================
27. ERROR HANDLING AND DATA INTEGRITY
==================================================

Every database operation must provide appropriate error handling.

Show understandable messages without exposing sensitive technical details.

Prevent:

- duplicate submissions
- blank required records
- invalid status transitions
- direct editing of released records
- unauthorized actions
- completion without required fields
- approval without required review
- deletion of retained history
- invalid revisions
- missing part references
- incomplete repair parts
- Repair Work Instruction release without steps
- Validation Work Instruction release without acceptance criteria
- FAI Work Instruction release without characteristics
- tracking completion without mandatory results
- task completion without signoffs
- duplicate numbering
- stale task updates
- duplicate completion
- unsafe database content rendering

Use confirmation dialogs for:

- cancellation
- rejection
- obsolescence
- deactivation
- removing steps
- removing parts
- discarding unsaved changes
- administrative override
- restoring archived records
- resetting seeded values

Use deactivation, superseding, obsolescence, or archival instead of destructive deletion.

==================================================
28. RESPONSIVE AND ACCESSIBLE DESIGN
==================================================

Match the existing QualGuard IQ design.

Review:

- desktop layouts
- tablet layouts
- mobile layouts
- sidebar behavior
- module navigation
- table overflow
- responsive form grids
- modal sizing
- keyboard navigation
- visible focus indicators
- semantic labels
- action-button states
- active states
- disabled states
- status badges
- loading states
- empty states
- error states
- success states
- confirmation dialogs

Do not introduce another styling system.

==================================================
29. COMPLETE INTEGRATION TESTING
==================================================

Test the complete QualGuard IQ and SEQUFLOH integration.

MODULE ACCESS:

- permitted user
- user without SEQUFLOH access
- direct route or view access
- menu visibility
- permission-protected actions
- current QualGuard IQ user propagation
- site and department context

SETTINGS AND REFERENCE DATA:

- settings changes
- dropdown changes
- seeding
- reset confirmation
- audit creation

PARTS:

- shared Part Master
- Part Master updates
- Parts List creation
- Parts List revision
- operation templates
- related records

WORK INSTRUCTIONS:

- Validation
- Validation / FAI
- FAI
- Repair Test
- Repair Work Instruction
- step actions
- FAI characteristics
- repair parts
- lifecycle transitions
- review
- approval
- rejection
- release
- revision
- superseding
- obsolescence
- archive
- restore
- print
- export

QUALITY:

- Quality dashboard
- review queue
- review comments
- requested changes
- approval
- rejection
- result verification

ENGINEERING:

- Engineering dashboard
- development queue
- validation review
- Engineering conclusions
- revision development
- release recommendation

TRACKING:

- Validation
- FAI
- Repair
- Production
- required-field validation
- result validation
- evidence references
- signoffs
- approvals

LIVE EXECUTION:

- dispatch
- start
- pause
- refresh while paused
- resume
- hold
- refresh while active
- completion
- failure
- rejection
- reassignment
- cancellation
- duplicate-transition protection
- simultaneous-action protection
- persisted timer calculations

REPORTS:

- filters
- sorting
- pagination
- Excel exports
- print layouts
- audit queries

SECURITY:

- unauthorized reads
- unauthorized writes
- protected settings
- protected approvals
- protected releases
- append-only task events
- append-only audit events

DATA INTEGRITY:

- number concurrency
- released revision preservation
- unsafe-text escaping
- missing references
- state refresh
- loading states
- empty states
- error states

==================================================
30. FINAL ACCEPTANCE CRITERIA
==================================================

The SEQUFLOH module is complete only when:

- it is integrated inside QualGuard IQ
- QualGuard IQ remains the parent QMS
- SEQUFLOH retains its name
- no separate SEQUFLOH login exists
- no duplicate user system exists
- the current QualGuard IQ session is reused
- the current role and permission system is extended
- existing modules are preserved
- no parallel architecture is introduced
- no unapproved technology is introduced
- SEQUFLOH module access works
- settings are functional
- dropdowns are administrable
- shared Part Master integration works
- Parts Lists work
- reusable operation steps work
- numbering works
- Work Instructions work
- released records are protected
- revision control preserves history
- Validation and Repair folders are separate
- Validation instructions are complete
- FAI instructions are complete
- Repair Test instructions are complete
- Repair Work Instructions are complete
- Quality folders work
- Engineering folders work
- active tasks persist
- paused tasks persist
- task timing uses stored timestamps
- concurrency checks exist
- Validation Tracking works
- FAI Tracking works
- Repair Tracking works
- Production Tracking works
- audit events are created
- reports work
- Excel exports work
- printing works
- security rules are extended
- required indexes are included
- errors are handled
- unsafe text is escaped
- search and filters work
- no required page is a placeholder
- no major control is decorative

==================================================
31. COMPLIANCE WORDING
==================================================

Do not claim that SEQUFLOH or QualGuard IQ is automatically ISO-certified, IATF-certified, or compliant merely because features exist.

Use accurate wording:

“Designed to support controlled records, traceability, workflow execution, and audit-history requirements.”

State that organizational compliance depends on:

- configuration
- validation
- procedures
- security
- deployment
- training
- implementation
- organizational use

==================================================
32. FINAL OUTPUT REQUIREMENTS
==================================================

First output:

1. Integration findings
2. Files being created or modified
3. Final QualGuard IQ and SEQUFLOH module structure
4. Final SEQUFLOH data model
5. Final permission matrix
6. Work Instruction lifecycle matrix
7. Task transition matrix
8. Timing and concurrency rules
9. Complete code

Provide complete content for every created or modified file.

Do not provide:

- pseudocode
- abbreviated examples
- placeholder modules
- partial snippets
- “continue here” markers
- skipped functions
- duplicate sign-in code
- a new technology stack
- an unfinished skeleton

If multiple responses are required:

- divide them into numbered consecutive parts
- identify the files in each part
- provide complete files
- avoid repeating completed files
- continue exactly from the prior part
- never stop in the middle of a function, object, class, HTML element, database rule, configuration object, or file

At completion provide:

- final folder structure
- final database structure
- final security rules or equivalent changes
- final index configuration
- seed instructions
- configuration instructions
- setup instructions
- deployment instructions
- testing checklist
- final acceptance checklist
- list of any necessary manual QualGuard IQ administrator steps

Begin by examining the complete current QualGuard IQ and SEQUFLOH source code below.

PASTE THE COMPLETE CURRENT QUALGUARD IQ SOURCE CODE DIRECTLY BELOW THIS LINE:
