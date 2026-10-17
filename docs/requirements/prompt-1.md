MASTER BUILD PROMPT 1 OF 2
MANUFACTURING QMS FOUNDATION, ARCHITECTURE, DOCUMENT CONTROL, AND CORE QUALITY MODULES

Act as the principal architect and implementation team responsible for designing, building, integrating, securing, testing, and documenting a production-ready Quality Management System.

Perform the combined responsibilities of:

1. Principal Software Architect
2. Senior Full-Stack Software Engineer
3. Senior Database Architect
4. Senior Application Security Engineer
5. Senior DevOps Engineer
6. Senior Quality Manager
7. Senior Quality Engineer
8. Senior Manufacturing Engineer
9. Senior Document Control Manager
10. Senior Internal Quality Auditor
11. Senior Supplier Quality Engineer
12. Senior Calibration and Metrology Specialist
13. Senior User Experience Designer
14. Senior Software Validation Engineer
15. Senior IT Systems Administrator

PROJECT OBJECTIVE

Build a complete, fully functional Quality Management System for manufacturing organizations.

The system shall be structured around ISO 9001 principles and common manufacturing quality practices. It shall support implementation and maintenance of a Quality Management System, but it must not claim that using the application makes an organization certified, automatically compliant, or guaranteed to pass an audit.

This is a new, general-purpose manufacturing QMS.

Do not assume that it is based on any previous application.

Do not make it specific to the automotive aftermarket or any single manufacturing industry.

It must be configurable for organizations manufacturing components, assemblies, materials, industrial products, consumer products, equipment, or other manufactured goods.

The completed application must support:

- Quality planning
- Quality assurance
- Quality control
- Documented information
- Manufacturing process control
- Engineering records
- Product and part records
- Inspection and testing
- Nonconformance control
- Corrective action
- Internal auditing
- Supplier quality
- Customer quality
- Risk management
- Change control
- Training and competency
- Calibration and measurement equipment
- Management review
- Quality objectives
- Performance reporting
- Continual improvement

THIS MUST NOT BE:

- A prototype
- A demonstration
- A proof of concept
- A wireframe
- A static website
- A mock application
- A collection of disconnected pages
- A user-interface-only build
- A collection of placeholder modules
- A system dependent on fake data
- A plan without implementation

TECHNOLOGY STACK

If the existing repository already uses a sound, maintainable stack, inspect it and preserve it unless a change is necessary for security, data integrity, or completion.

If a new application must be initialized, use:

BACKEND

- ASP.NET Core 10 Web API
- C# 14
- Entity Framework Core 10
- Modular monolith architecture
- REST API
- OpenAPI documentation
- FluentValidation or an equivalent server-side validation framework
- ASP.NET Core authorization policies
- ASP.NET Core Identity or OpenID Connect compatible authentication

FRONTEND

- React
- TypeScript
- Vite
- TanStack Query
- React Hook Form
- Zod
- Material UI or another mature accessible component library
- TanStack Table
- Recharts

DATABASE

- PostgreSQL 18
- Relational schema
- Foreign keys
- Unique constraints
- Check constraints
- Transactional record changes
- Appropriate indexes
- Full-text search where useful
- Database migrations

FILES

Use S3-compatible object storage.

Support:

- Azure Blob Storage
- Amazon S3
- MinIO for local and self-hosted deployments

Store file metadata, revision information, security classification, permissions, checksums, and storage keys in PostgreSQL.

Do not store unrestricted files in publicly accessible folders.

BACKGROUND TASKS

Use Hangfire with PostgreSQL storage or an equivalent reliable background-job framework for:

- Notifications
- Reminders
- Escalations
- Document review notices
- Training expiration notices
- Calibration due notices
- Scheduled reports
- Controlled publication tasks
- File-processing tasks

DEPLOYMENT

Use:

- Docker containers
- Linux-compatible deployment
- Environment-based configuration
- Health checks
- Structured logging
- Automated tests
- Continuous integration
- Controlled database migrations
- Staging and production environments
- Backup and restoration documentation

ARCHITECTURE

Build a modular monolith.

Do not create a microservice for every QMS module.

Create clear modules sharing one transactional database and common foundational services.

Use architecture organized into:

- Domain
- Application
- Infrastructure
- API
- Background worker
- React frontend
- Automated tests

Separate:

- User interface
- API contracts
- Authorization
- Validation
- Business logic
- Workflow logic
- Database access
- File storage
- Notifications
- Reporting
- Infrastructure

FIRST ACTION: REPOSITORY INSPECTION

If a repository exists:

1. Inspect the complete repository.
2. Identify frontend, backend, database, authentication, authorization, API, file-storage, testing, and deployment technologies.
3. Identify all existing working functions.
4. Identify incomplete, duplicated, broken, mock, unused, and disconnected functions.
5. Preserve working behavior.
6. Follow existing conventions when sound.
7. Search the repository before creating components, tables, services, functions, endpoints, or utilities.
8. Do not rebuild working features merely to change coding style.
9. Do not remove or rename existing routes, tables, fields, or modules without implementing migrations and updating every reference.
10. Record important architecture decisions.

If no repository exists, initialize the application using the required stack and immediately begin implementation.

SHARED QMS ENGINES

Build the application around these reusable services:

1. Authentication Engine
2. Role and Permission Engine
3. Document Control Engine
4. Workflow Engine
5. Approval Engine
6. Electronic Acknowledgment Engine
7. Audit Trail Engine
8. Attachment Engine
9. Notification Engine
10. Escalation Engine
11. Record Relationship Engine
12. Comment Engine
13. Action and Task Engine
14. Search Engine
15. Reporting Engine
16. Retention and Archiving Engine
17. Record Numbering Engine
18. Configurable Form Engine
19. Custom Field Engine
20. Organization Configuration Engine

Modules must use these shared services.

Do not duplicate approvals, attachments, comments, users, departments, audit trails, notifications, or workflows independently in every module.

BRANDING AND ORGANIZATION CONFIGURATION

Allow authorized administrators to configure:

- Application name
- Application abbreviation
- Organization name
- Application logo
- Organization logo
- Favicon
- Primary color
- Secondary color
- Accent color
- Login identity
- Browser title
- Email sender
- Report header
- Report footer
- Default time zone
- Date format
- Time format
- Locale
- Record prefixes
- Sites
- Departments
- Locations
- Processes
- Product categories
- Confidentiality classifications

Do not hard-code the organization or application name throughout the source code.

ORGANIZATIONAL STRUCTURE

Support:

- Organization
- Multiple sites
- Departments
- Locations
- Teams
- Positions
- Employees
- Managers
- Process owners
- Document owners
- Quality roles
- Engineering roles
- Site-level access
- Department-level access

USER EXPERIENCE

Create:

- Responsive application shell
- Collapsible sidebar
- Logical menus and submenus
- Breadcrumbs
- Global search
- Module search
- Advanced filtering
- Sorting
- Pagination
- Saved views
- Configurable table columns
- Recently viewed records
- Favorites
- Assigned-to-me view
- Waiting-on-me view
- Pending-approval view
- Overdue-actions view
- Notifications
- Quality alerts
- Role-based dashboards
- Clear record status
- Priority indicators
- Due-date indicators
- Accessible forms
- Keyboard navigation
- Validation messages
- Loading states
- Empty states
- Error states
- Unauthorized states
- Not-found states
- Unsaved-change warnings
- Destructive-action confirmations

Every menu, button, dashboard item, report result, and notification must perform its intended action.

ROLE-BASED ACCESS CONTROL

Create configurable roles including:

- System Administrator
- Quality Administrator
- Quality Manager
- Quality Engineer
- Quality Inspector
- Quality Technician
- Internal Auditor
- Lead Auditor
- Document Control Administrator
- Document Author
- Document Reviewer
- Document Approver
- Engineering Manager
- Engineer
- Manufacturing Engineer
- Supplier Quality Engineer
- Calibration Coordinator
- Metrology Technician
- Training Coordinator
- Department Manager
- Supervisor
- Production Employee
- Maintenance User
- Purchasing User
- Customer Service User
- Executive Viewer
- General Employee
- Read-Only User
- External Supplier User when intentionally enabled

Permissions must support:

- Module access
- Record access
- Site access
- Department access
- Confidential-record access
- View
- Create
- Edit
- Delete
- Archive
- Restore
- Submit
- Review
- Approve
- Reject
- Return for correction
- Reassign
- Delegate
- Implement
- Verify
- Close
- Reopen
- Export
- Print
- Upload
- Download
- Configure
- View audit history
- View reports
- Manage users
- Manage permissions

Enforce authorization at the API and data-access layers.

Hiding interface controls is not sufficient authorization.

WORKFLOW ENGINE

Support configurable states such as:

- Draft
- Submitted
- Under review
- Returned for correction
- Approval pending
- Approved
- Rejected
- Scheduled
- Effective
- In progress
- On hold
- Implementation pending
- Verification pending
- Effectiveness review
- Completed
- Closed
- Canceled
- Reopened
- Superseded
- Obsolete
- Archived

Each workflow must support:

- Allowed transitions
- Required fields by state
- Required attachments
- Required comments
- Role-based transition permissions
- Sequential approvals
- Parallel approvals
- Named approvers
- Role-based approvers
- Department approvers
- Conditional routing
- Reassignment
- Delegation
- Due dates
- SLA targets
- Reminders
- Escalations
- Rejection reasons
- Immutable transition history
- Controlled reopening
- Audited administrative correction

AUDIT TRAIL

Record:

- Creation
- Field changes
- Previous values
- New values
- Status changes
- Workflow transitions
- Assignments
- Submissions
- Reviews
- Approvals
- Rejections
- Returns
- Acknowledgments
- Uploads
- Downloads where configured
- Controlled printing
- Exports
- Archiving
- Restoration
- Deletion
- Permission changes
- Configuration changes
- Administrative overrides

Each audit entry must identify:

- Actor
- Action
- Record
- Record revision
- Timestamp
- Previous value
- New value
- Reason when required
- Related workflow event

Ordinary users must not be able to modify or delete audit entries.

DOCUMENT CONTROL MODULE

Build a complete controlled repository for:

- Quality manuals
- Policies
- SOPs
- Procedures
- Work instructions
- Engineering drawings
- Manufacturing drawings
- Product specifications
- Process specifications
- Material specifications
- Customer specifications
- Supplier specifications
- Test methods
- Inspection methods
- Inspection plans
- Control plans
- Process flow diagrams
- Forms
- Checklists
- Technical data sheets
- Equipment manuals
- Maintenance instructions
- Calibration instructions
- Packaging instructions
- Labeling instructions
- Visual standards
- Training materials
- Audit checklists
- External documents
- Certificates
- Reference documents
- Controlled templates

Each controlled document must support:

- Document number
- Title
- Category
- Department
- Process
- Site
- Owner
- Author
- Reviewers
- Approvers
- Revision
- Status
- Draft date
- Approval date
- Effective date
- Review date
- Expiration date
- Revision reason
- Change summary
- Keywords
- Tags
- File
- File preview where supported
- Controlled download
- Controlled print
- Uncontrolled-copy marking
- Distribution list
- Required acknowledgment
- Training requirement
- Retention rule
- Confidentiality
- Relationships
- Complete history

Document lifecycle:

- Draft
- In review
- Returned for correction
- Approval pending
- Approved
- Scheduled for release
- Effective
- Review due
- Revision in progress
- Superseded
- Obsolete
- Archived
- Canceled

Approved and effective revisions must be immutable.

A new revision must not overwrite a previously approved revision.

The system must identify which version was effective on a selected date.

ENGINEERING DRAWINGS

Support:

- Drawing number
- Drawing title
- Drawing type
- Product
- Part
- Assembly
- Revision
- Sheet
- Scale
- Drawing size
- Material
- Finish
- Tolerance reference
- Special characteristics
- Designer
- Checker
- Engineering approver
- Quality approver
- Release date
- Native file
- Viewable file
- Related specifications
- Related inspection plans
- Related change requests
- Complete revision history

The QMS must control drawing metadata, files, revisions, approvals, release, and relationships.

Do not attempt to replace CAD authoring software.

CONTROLLED FORMS

Allow authorized users to:

- Upload form templates
- Create electronic forms
- Create sections
- Add field types
- Add tables
- Add repeating rows
- Add instructions
- Add required fields
- Add validation
- Add calculations
- Add conditional visibility
- Add signatures or approvals
- Add attachments
- Route completed forms
- Retain completed forms as records
- Export forms to PDF
- Search completed forms

Completed forms must remain tied to the exact template revision used.

Changing a template must not change historical completed records.

NONCONFORMANCE MODULE

Support nonconformances from:

- Receiving
- Production
- In-process inspection
- Final inspection
- Testing
- Packaging
- Shipping
- Customer complaints
- Suppliers
- Audits
- Calibration
- Documentation
- Process deviations
- Returns
- Employee observations

Support:

- NCR number
- Source
- Detection date
- Detection point
- Reporter
- Product
- Part
- Revision
- Process
- Operation
- Work order
- Purchase order
- Lot
- Batch
- Serial number
- Supplier
- Customer
- Quantities
- Defect code
- Requirement
- Actual condition
- Description
- Risk
- Evidence
- Containment
- Segregation
- Investigation
- Disposition
- Approvals
- Rework instructions
- Repair instructions
- Deviation or concession
- Scrap authorization
- Return to supplier
- Cost of poor quality
- Verification
- Closure
- Reopening
- Relationships
- Audit history

Support dispositions:

- Rework
- Repair
- Use as is
- Return to supplier
- Sort
- Re-inspect
- Re-test
- Scrap
- Deviation
- Concession
- Other configured disposition

Do not permit closure until affected quantities, required actions, approvals, and verification are complete.

CAPA MODULE

Support:

- Source
- Problem statement
- Scope
- Impact
- Risk
- Immediate correction
- Containment
- Investigation team
- Five Whys
- Fishbone analysis
- Is and Is Not analysis
- Root cause
- Contributing causes
- Escape point
- Corrective actions
- Preventive actions
- Owners
- Due dates
- Evidence
- Implementation verification
- Effectiveness criteria
- Effectiveness review
- Recurrence monitoring
- Approval
- Closure
- Reopening
- Related records

Do not permit closure until root cause, required actions, implementation verification, effectiveness evaluation, and approvals are complete.

AUDIT MANAGEMENT MODULE

Support:

- Internal audits
- System audits
- Process audits
- Product audits
- Manufacturing audits
- Department audits
- Supplier audits
- Documentation audits
- Layered process audits
- Custom audits

Include:

- Audit program
- Schedule
- Scope
- Criteria
- Objectives
- Lead auditor
- Audit team
- Auditee
- Auditor independence
- Revision-controlled checklists
- Evidence
- Interviews
- Samples
- Findings
- Observations
- Opportunities for improvement
- Finding classification
- Responses
- Root cause
- Corrective action
- Verification
- Audit report
- Closure
- History

TRAINING AND COMPETENCY MODULE

Support:

- Employee profiles
- Departments
- Positions
- Supervisors
- Required competencies
- Training requirements
- Course catalog
- Controlled-document training
- On-the-job training
- External training
- Assignments
- Due dates
- Assessments
- Scores
- Qualifications
- Certifications
- Expiration
- Supervisor verification
- Effectiveness evaluation
- Training history

Create training matrices by:

- Employee
- Position
- Department
- Site
- Course
- Document
- Competency
- Qualification

Support retraining when a controlled document is revised.

CALIBRATION MODULE

Support:

- Equipment ID
- Asset number
- Equipment type
- Manufacturer
- Model
- Serial number
- Site
- Department
- Location
- Custodian
- Measurement range
- Resolution
- Accuracy
- Acceptance criteria
- Calibration method
- Calibration frequency
- Last calibration
- Next due date
- Provider
- Standards used
- Traceability
- Environmental conditions
- As-found results
- As-left results
- In-tolerance status
- Out-of-tolerance status
- Limited-use status
- Certificates
- Repairs
- Labels
- Notifications
- History

For out-of-tolerance equipment require:

- Impact assessment
- Potentially affected period
- Affected products
- Affected inspections
- Affected tests
- Investigation
- Containment
- Disposition
- CAPA or NCR when required
- Approval
- Conclusion

SHARED RECORD RELATIONSHIPS

Implement reusable relationships including:

- Document to training
- Document to change
- Drawing to part
- Drawing to inspection
- NCR to CAPA
- Audit finding to CAPA
- Complaint to NCR
- Supplier to NCR
- Calibration failure to inspection
- Calibration failure to NCR
- Risk to action
- Product to drawing
- Product to inspection
- Equipment to calibration

Users must be able to navigate related records when authorized.

FILES AND ATTACHMENTS

Implement:

- Secure file storage
- File metadata
- File descriptions
- File categories
- Allowed types
- File-size limits
- Checksums
- Versioning when required
- Permission-controlled download
- Preview when supported
- Retention
- Archiving
- Audit history
- Malware-scanning integration point

Do not expose protected files through public URLs.

SECURITY REQUIREMENTS

Implement:

- Secure authentication
- Server-side authorization
- Least privilege
- Input validation
- Output encoding
- Injection prevention
- CSRF protection where applicable
- Secure sessions
- Rate limiting where appropriate
- Secure uploads
- Secret management
- Environment configuration
- Safe error handling
- Security logging
- Account disabling
- Session revocation
- Dependency review
- Backup planning
- Controlled deletion

Use OWASP ASVS Level 2 as the security verification target, but do not claim certification.

Separate security-event logs from QMS business audit trails.

DATABASE REQUIREMENTS

Use:

- Primary keys
- Foreign keys
- Unique constraints
- Check constraints
- Referential integrity
- Indexes
- Transactions
- Concurrency controls
- Created and updated metadata
- Soft deletion or controlled archiving
- Migrations
- Seed configuration
- Rollback considerations

Use shared entities for:

- Users
- Roles
- Permissions
- Sites
- Departments
- Products
- Parts
- Suppliers
- Customers
- Attachments
- Comments
- Actions
- Approvals
- Notifications
- Audit records
- Workflows
- Relationships
- Tags

Do not create incompatible duplicate models for every module.

PROMPT 1 TESTING

Create:

- Unit tests
- Integration tests
- Database tests
- API tests
- Authentication tests
- Authorization tests
- Permission-boundary tests
- Workflow tests
- Approval tests
- Audit-trail tests
- Revision-control tests
- Attachment tests
- Notification tests
- End-to-end tests

End-to-end tests must cover:

1. Create, review, approve, release, revise, supersede, and retrieve a controlled procedure.
2. Upload and approve an engineering drawing, then create a controlled revision without altering the original revision.
3. Create an NCR, contain it, approve disposition, connect a CAPA, verify effectiveness, and close both records.
4. Create an audit, complete a checklist, issue a finding, create corrective action, verify it, and close the audit.
5. Register calibrated equipment, record an out-of-tolerance condition, perform an impact assessment, and create related records.
6. Assign training from a controlled document and trigger retraining after revision.

PROMPT 1 DEFINITION OF DONE

Prompt 1 is complete only when:

- The application builds.
- Database migrations run.
- Authentication works.
- Server-side permissions work.
- Shared engines work.
- Document control works.
- Drawing control works.
- Controlled forms work.
- NCR works.
- CAPA works.
- Audit management works.
- Training works.
- Calibration works.
- Attachments are securely stored.
- Audit trails are recorded.
- Notifications open the correct records.
- Search works for completed modules.
- Tests pass.
- No placeholder functions remain in completed modules.

PULL REQUEST FOR PROMPT 1

Create one integrated pull request containing the foundation and Prompt 1 modules.

Do not create separate pull requests for each page or module.

CONTEXT CHECKPOINT

If execution must stop:

1. Stop only at a working checkpoint.
2. Preserve completed migrations.
3. List completed requirements.
4. List remaining Prompt 1 requirements.
5. List changed files.
6. List passing and failing tests.
7. List known defects.
8. State the exact next implementation action.
9. Do not start Prompt 2 until Prompt 1 foundations are working.

FINAL PROMPT 1 INSTRUCTION

Begin implementation now.

Do not respond only with a plan.

Do not create placeholders.

Do not claim Prompt 1 is complete until its migrations, permissions, workflows, modules, and tests function.
