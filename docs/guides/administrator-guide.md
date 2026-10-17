# Administrator guide

All configuration is data. Nothing in the application depends on a particular role name.

## Organization, sites and departments (Admin)

- **Organization settings**: application name, organization name, logo.
- **Sites**: each site has a time zone used for due dates and overdue calculations.
- **Departments**: belong to a site; used to scope permissions and route work.

## Users and roles

- A **role** is a named set of permissions. Grant a role to a user organization-wide, for one site, or for one
  department at a site.
- Permission checks run on the server for every request; changing a user's roles or disabling them takes effect on
  their next request.
- Accounts lock after five wrong passwords. Unlock from the user's page. Sessions can be revoked there too.
- Periodic access reviews are run from Governance › Access reviews.

## Workflow designer (Platform › Workflow designer, needs *admin.organization*)

Approval routes are defined per record type, optionally overridden per site.

- **Stages** run one after another. **Steps** in the same stage run in parallel.
- Each step names the permission a signer must hold, the signature meaning, optional MFA and skill requirements.
- A step may have a **condition** (field, operator, value) so it applies only to matching records, for example
  `severity = Critical`. Every route must have at least one unconditional step.
- **Escalation**: after a number of hours in a stage, users holding the escalation permission are notified once.
- **Preview** shows which steps would apply to an existing record before you save.
- Saving creates a new route version. Approvals already in progress finish on the version they started with.

## Form designer (Platform › Form designer, needs *forms.edit*)

- Form templates are controlled documents; publishing a template follows the document approval route.
- Fields support types, units, limits, patterns, calculated values, cross-field checks, conditional visibility and
  conditional requirement.
- The **component library** holds reusable field groups (for example *Gauge identification*). A component is
  published (needs *forms.approve*) before use; inserting it copies that published version into the template, so later
  library changes do not alter released forms. Create a new component version to change it.
- **Preview** renders the form and evaluates it against sample values before you submit it for approval.

## Business rules (Platform › Business rules, needs *platform.admin*)

| Rule kind | Effect |
|---|---|
| Validation | Blocks submission for approval when the condition matches, with your message |
| Routing | On submission, notifies holders of a permission and can set the due date |
| Escalation | Notifies when matching records stay open past a number of hours |
| Automation | Reacts to platform events (record created, status changed and so on) by notifying |

Use **Test** to evaluate a rule against an existing record. The firing log shows every time a rule acted.

## Data governance (Platform › Data governance)

- Each master data domain has a steward, an owner and a classification (Public, Internal, Confidential, Restricted).
- **Quality rules** (required, unique, pattern, allowed values) are measured with **Measure data quality**; the trend
  is shown per domain. A rule marked *blocking* stops change requests that would break it.
- Changes to master data are requested, approved on the *Master data change* route, then applied automatically.

## Retention, continuity and recovery

- Retention policies: Governance › Retention.
- Continuity exercises, including disaster-recovery tests: Governance › Business continuity.
- Backups, restore tests and recovery objectives: Platform › Recovery center. See the [recovery guide](recovery-guide.md).

## Background jobs

The worker runs: document release (hourly), due reminders (daily), alerts, approval escalations and report schedules
(every few minutes), workforce updates (daily) and data exports. Job status is shown on Platform › System health.
