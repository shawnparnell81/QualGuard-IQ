# SEQUFLOH setup and administration

## Deployment

SEQUFLOH ships inside QualGuard IQ. Nothing extra has to be installed or configured.

1. Deploy the application as usual. On startup the API applies the EF Core migrations (`Sequfloh*` migrations in `src/QualGuardIQ.Infrastructure/Persistence/Migrations`), which create the SEQUFLOH tables and indexes in schema `qms`.
2. The seeder then runs once per database (`SequflohFoundationService.SeedAsync`). It creates the settings row with default values and the seeded dropdown values. It never overwrites values an administrator has changed, and it adds no sample records.
3. The existing Hangfire reminder job (daily, 06:15) sends SEQUFLOH overdue and approaching-due reminders.

The module uses the shared services already configured for QualGuard IQ: sign-in, organizations, sites, departments, users, roles and permissions, audit trail, notifications and email, search, attachments (MinIO), Excel export and the PDF renderer.

## Manual administration steps

Before people can use the module, an administrator with `sequfloh.manage_module_access` (or the QualGuard IQ administrator):

1. **Turn the module on.** SEQUFLOH Settings › General Settings: module enabled, display name, landing page, records per page, default site and department, recent items and favorites.
2. **Grant permissions.** SEQUFLOH Administration › Module Access, or Administration › Roles. Add the `sequfloh.*` permissions to the roles that should have them, organization-wide or for selected sites and departments. Roles and Permissions shows the suggested permission sets (see [permissions and lifecycles](permissions-and-lifecycles.md)); they are only suggestions until applied to a role.
3. **Numbering.** Numbering Settings: prefix, padding and pattern for each work instruction type, tracking record type, parts list and operation step (on the shared QualGuard IQ numbering service), and the revision format (letters A, B, C or two digits 00, 01, 02).
4. **Settings sections.** Work Instruction, Validation, FAI, Repair and Tracking settings set the review and approval steps, self-approval prevention, required signoffs and evidence, effective dates, revision reasons, benchmarks, variance thresholds and variance reasons. Display, Export and Security settings set dashboard cards, default views, status colors, report titles and columns, file names, and whether exports require `sequfloh.export_records`.
5. **Notification settings.** Turn each notification kind on or off: assigned work, pending review, pending approval, changes requested, overdue work, failed records, rejected records, released instructions, superseded instructions and approaching due dates. The overdue and approaching-due hours are in Tracking Settings.
6. **Dropdowns.** SEQUFLOH Administration › Reference Data and Dropdown Management: add, edit, reorder or deactivate values. Reset to seeded values asks for confirmation and is audited.
7. **Parts.** SEQUFLOH uses the shared Part Master. Add the SEQUFLOH fields (product family, assembly type, repairable, serialized, lot controlled, drawing, specification) where they are needed, then build parts lists.

Every settings, dropdown and access change is recorded in the audit trail.

## Configuration reference

| Settings section | Items |
| --- | --- |
| General | Module enabled, display name, description, landing page, records per page, default site and department, recent items limit, favorites |
| Numbering | Prefix, padding and pattern per record type; revision format |
| Work Instruction | Quality review, Engineering review and approval required, extra review permissions, effective date and revision reason required, training acknowledgement, obsolete retention years, print footer, approval statement, prevent self-approval |
| Tracking | Variance warning and critical percent, default minimum and maximum minutes, overdue and approaching-due hours, technician and inspector signoff, variance reason required |
| Validation | Evidence, Quality review, Engineering review and approval required |
| FAI | Quality and Engineering approval required, evidence required for failures |
| Repair | Quality approval, inspector verification and repair test required |
| Display | Compact tables, dashboard cards, default tracking filter and folder view, status colors |
| Export | File name pattern, date format, organization heading, included columns and report titles |
| Notification | Each notification kind on or off |
| Security | Reason required for override, exports require permission, suggested permission sets |
