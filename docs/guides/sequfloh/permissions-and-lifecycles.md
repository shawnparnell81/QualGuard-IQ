# SEQUFLOH permissions and lifecycles

## Permission matrix

SEQUFLOH permissions are part of the QualGuard IQ permission list and are granted to existing roles (Administration › Roles, or SEQUFLOH Administration › Module Access). Grants can be organization-wide or limited to sites and departments. Code checks permissions only, never role names.

The suggested sets are stored in Security settings as suggestions an administrator can apply to an existing role: **Op** Operator, **Te** Technician, **Su** Supervisor, **Qu** Quality, **En** Engineering, **DC** Document control. Module administrator gets every permission.

| Permission | Allows | Op | Te | Su | Qu | En | DC |
| --- | --- | :-: | :-: | :-: | :-: | :-: | :-: |
| `sequfloh.access` | Open SEQUFLOH, read work instructions, parts, parts lists and records within scope, search, recent items and favorites | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| `sequfloh.view_dashboard` | SEQUFLOH Home dashboard | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| `sequfloh.view_live_execution` | Live Execution views, tracking folders and records | ✓ | ✓ | ✓ | ✓ | | |
| `sequfloh.dispatch_task` | Dispatch, assign and restart tasks | | | ✓ | | | |
| `sequfloh.start_task` | Start a task; be assigned work | ✓ | ✓ | ✓ | | | |
| `sequfloh.pause_task` / `resume_task` | Pause with a reason; resume | ✓ | ✓ | ✓ | | | |
| `sequfloh.hold_task` | Place on hold with a reason | | ✓ | ✓ | | | |
| `sequfloh.complete_task` | Record results, sign, submit for inspection or review, complete | ✓ | ✓ | ✓ | | | |
| `sequfloh.fail_task` | Fail a task with a reason | | ✓ | ✓ | | | |
| `sequfloh.reject_task` | Reject a task with a reason | | | ✓ | ✓ | | |
| `sequfloh.reassign_task` | Reassign a task | | | ✓ | | | |
| `sequfloh.cancel_task` | Cancel a task with a reason | | | ✓ | | | |
| `sequfloh.create_work_instruction` | Create work instructions | | | | | ✓ | |
| `sequfloh.edit_draft_work_instruction` | Edit draft, in-development and returned instructions | | | | | ✓ | |
| `sequfloh.submit_work_instruction` | Submit for review | | | | | ✓ | |
| `sequfloh.review_work_instruction` | Review, return for changes, submit for approval | | | | ✓ | | |
| `sequfloh.approve_work_instruction` | Approve, reject, reverse approval | | | | ✓ | | |
| `sequfloh.release_work_instruction` | Release (supersedes the prior revision) | | | | | | ✓ |
| `sequfloh.revise_work_instruction` | Create a new revision of a released instruction | | | | | ✓ | |
| `sequfloh.obsolete_work_instruction` | Supersede or mark obsolete | | | | | | ✓ |
| `sequfloh.archive_work_instruction` / `restore_work_instruction` | Archive and restore | | | | | | ✓ |
| `sequfloh.create_validation_record` / `create_fai_record` / `create_repair_record` / `create_production_task` | Create tracking records of that type | | repair | all | validation, FAI | | |
| `sequfloh.edit_parts` | Edit Part Master fields through SEQUFLOH | | | | | ✓ | |
| `sequfloh.manage_parts_lists` | Create, edit, approve and revise parts lists | | | | | ✓ | |
| `sequfloh.manage_operation_steps` | Maintain reusable operation steps | | | | | ✓ | |
| `sequfloh.view_quality_folder` / `edit_quality_folder` | Quality area; Quality review, inspection approval and result reversal | | | | ✓ | | |
| `sequfloh.view_engineering_folder` / `edit_engineering_folder` | Engineering area; Engineering review and the development record | | | | | ✓ | |
| `sequfloh.view_reports` | Reports and the Export Center | | | ✓ | ✓ | ✓ | ✓ |
| `sequfloh.export_records` | Excel exports | | | ✓ | ✓ | | ✓ |
| `sequfloh.view_audit_trail` | Audit trail views, User Activity report, audit export and print | | | | ✓ | | ✓ |
| `sequfloh.manage_settings` | SEQUFLOH settings; administrative override of a task | | | | | | |
| `sequfloh.manage_dropdowns` | Reference data and dropdowns, including the confirmed reset to seeded values | | | | | | |
| `sequfloh.manage_module_access` | Module Access and permission suggestions | | | | | | |

## Work instruction lifecycle

| Action | From | To | Permission | Comment |
| --- | --- | --- | --- | --- |
| Start development | Draft | In Development | edit_draft_work_instruction | |
| Submit for review | Draft, In Development, Changes Requested | Pending Review | submit_work_instruction | Content rules must pass |
| Return for changes | Pending Review, Pending Approval | Changes Requested | review_work_instruction | Required |
| Submit for approval | Pending Review | Pending Approval | review_work_instruction (plus any extra review permissions in settings) | |
| Approve | Pending Approval | Approved | approve_work_instruction | Author cannot approve when self-approval is prevented |
| Reject | Pending Review, Pending Approval | Draft | approve_work_instruction | Required |
| Reverse approval | Approved | Pending Approval | approve_work_instruction | Required |
| Release | Approved | Released | release_work_instruction | Needs complete content (and an effective date when configured); the released prior revision becomes Superseded |
| Supersede | Released | Superseded | obsolete_work_instruction | Required |
| Mark obsolete | Released, Superseded, Approved | Obsolete | obsolete_work_instruction | Required |
| Archive | Draft, In Development, Changes Requested, Superseded, Obsolete | Archived | archive_work_instruction | |
| Restore | Archived | Status before archiving | restore_work_instruction | |
| New revision | Released | New Draft revision in the same family | revise_work_instruction | Reason required when configured; one revision in progress at a time; history kept |

Only Draft, In Development and Changes Requested instructions can be edited. Every action is written to the lifecycle history and the audit trail.

## Task transition matrix

| Action | From | To | Permission | Reason |
| --- | --- | --- | --- | --- |
| Dispatch | Not Started | Assigned | dispatch_task | |
| Assign | Not Started, Assigned | Assigned | dispatch_task | |
| Start | Not Started, Assigned | In Progress | start_task | |
| Pause | In Progress | Paused | pause_task | Required |
| Resume | Paused, On Hold | In Progress | resume_task | |
| Place on hold | Not Started, Assigned, In Progress, Paused, Pending Inspection | On Hold | hold_task | Required |
| Submit for inspection | In Progress | Pending Inspection | complete_task | Completion rules |
| Submit for review | In Progress, Pending Inspection | Pending Review | complete_task | Completion rules |
| Approve | Pending Review | Completed, or stays Pending Review until every required review is done | edit_quality_folder (Quality) / edit_engineering_folder (Engineering) | |
| Complete | In Progress, Pending Inspection | Completed | complete_task | Completion rules; variance reason when outside the benchmark |
| Fail | In Progress, Pending Inspection, Pending Review | Failed | fail_task | Required |
| Reject | In Progress, Pending Inspection, Pending Review | Rejected | reject_task | Required |
| Restart | Failed, Rejected | Assigned | dispatch_task | Required |
| Reassign | Not Started to Pending Inspection | Same status, new assignee | reassign_task | |
| Cancel | Not Started to Pending Review | Canceled | cancel_task | Required |
| Reverse result | Completed, Failed, Rejected | Pending Review | edit_quality_folder | Required |
| Administrative override | Any | Chosen status | manage_settings | Required |

Completion rules: required steps and characteristics have results, required signoffs are captured, required evidence is recorded, required parts have lot and serial numbers when used.

## Timing and concurrency rules

- **Stored timestamps.** Every transition reads the database clock (`clock_timestamp()`), adds the time since `CurrentStateStartedAt` to the active, paused or hold total for the state being left, and starts timing the next state. Elapsed time on screen is the stored total plus the time since the stored state start, so a refresh, another device or a restart shows the same time.
- **Benchmarks.** The minimum and maximum benchmark minutes come from the instruction (or the tracking defaults). At completion the variance is stored: negative below the minimum, positive above the maximum, zero inside. A variance reason is required when configured.
- **Row lock.** Each change opens a transaction and locks the record row (`SELECT … FOR UPDATE`), then reloads it, so simultaneous requests are applied one after the other against the latest state.
- **State version.** Every change carries the state version the user saw. A stale version is refused with 409 and the user is asked to reload; two people resuming the same paused task at once gives one success and one refusal.
- **Operation ID.** Every change carries a unique operation ID stored on the task event (unique index). Repeating the same request returns the stored result and changes nothing.
- **Allowed transitions only.** The action must be allowed from the current status; otherwise the request is refused.
