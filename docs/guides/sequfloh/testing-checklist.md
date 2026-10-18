# SEQUFLOH testing checklist

Automated tests run against local test databases only.

- Backend: `dotnet test` (API and service tests in `tests/QualGuardIQ.Tests/Sequfloh*Tests.cs`; OpenAPI contract in `OpenApiTests`).
- Browser: `cd web && E2E_CHROME=/usr/bin/google-chrome npx playwright test` (API on :5081, web on :5174, database `qgiq_e2e`; SEQUFLOH specs `e2e42` to `e2e49`).

Backend test classes: Foundation (F), Parts (P), Instruction (I), Tracking (T), Execution (X), Dashboard (D), Report (R).

| Area (Prompt 2 section 29) | Item | Covered by |
| --- | --- | --- |
| Module access | Permitted user, user without access, direct routes, menu visibility | F `Module_access_uses_the_existing_session_and_permissions`; e2e42 (all three tests) |
| | Permission-protected actions | Every backend class checks 403 for missing permissions |
| | Current user propagation, site and department context | F module access test; e2e42 shell test; R `Assignment_notifies_search_respects_site_and_prints_are_audited` |
| Settings and reference data | Settings changes, audit | F `Settings_are_validated_audited_and_can_switch_the_module_off`; e2e42 settings test |
| | Dropdown changes, seeding, reset confirmation | F `Reference_lists_are_seeded_editable_reorderable_and_never_deleted`; e2e42 |
| | Numbering | F `Numbering_patterns_and_revision_formats_are_configurable` |
| Parts | Shared Part Master and updates | P `Part_master_is_shared_and_core_edits_need_master_data_permission`; e2e43 |
| | Parts List creation and revision | P `Parts_list_lifecycle_is_numbered_controlled_and_revised_with_snapshots`; e2e43 |
| | Operation templates | P `Operation_steps_are_validated_numbered_duplicated_and_reordered`; e2e43 |
| Work instructions | Five types and their designers | I `Five_instruction_types_open_their_own_designer_and_numbering` |
| | Step actions, FAI characteristics, repair parts | I repair/FAI tests; e2e44 (repair and FAI tests) |
| | Review, approval, rejection, release, revision, superseding, obsolescence, archive, restore | I `Lifecycle_is_enforced_recorded_and_released_records_change_only_by_revision`; e2e44 validation test |
| | Print | I `Each_instruction_type_prints_a_controlled_pdf_and_printing_is_audited`; e2e45 |
| | Export | R `Reports_export_the_full_filtered_result_with_a_title_block_and_the_export_is_audited`; e2e49 |
| Quality | Dashboard, review queue, comments, requested changes, approval, rejection | D `Dashboards_and_review_queues_follow_permissions_and_status`; I lifecycle test; e2e48 |
| | Result verification | T validation and FAI tests (independent Quality approval, result reversal) |
| Engineering | Dashboard, development queue, validation review, conclusions, revision development, release recommendation | D both tests; T FAI test (Engineering review); e2e48 |
| Tracking | Validation, FAI, Repair, Production | T all four tests; e2e46 |
| | Required fields, results, evidence, signoffs, approvals | T validation, FAI and repair tests; e2e46 |
| Live execution | Dispatch, start, pause, refresh while paused, resume, hold, refresh while active, completion | e2e47; T production test; X `Live_views_follow_the_stored_state_and_simultaneous_resume_applies_once` |
| | Failure, rejection, reassignment, cancellation | T validation, FAI and production tests |
| | Duplicate transitions, simultaneous actions, persisted timers | T production test (operation ID, state version); X (parallel resume) |
| Reports | Filters, sorting, pagination, Excel export, audit queries | R report test; e2e49 |
| | Print layouts | I print test; R test (tracking record and audit summary prints) |
| Security | Unauthorized reads and writes, protected settings, approvals and releases | F, I, T and R tests (403 and out-of-site refusals) |
| | Append-only task events and audit events | T production test; audit and task-event tables have no update or delete endpoints |
| Data integrity | Number concurrency | Shared `NumberingTests` (atomic allocation); numbers allocated in the saving transaction |
| | Released revision preservation, missing references | I lifecycle test; T validation test (record keeps its revision); P tests |
| | Unsafe-text escaping | e2e48 (markup in a development field is shown as text and never runs) |
| | State refresh, loading, empty and error states | e2e47 (refresh), e2e42 to e2e49 page checks |

## Manual checks before release

- Open each SEQUFLOH page at desktop, tablet (768 px) and phone (390 px) widths: the shell sidebar collapses, tables scroll inside their card, dialogs fit the screen.
- Tab through a designer, a tracking record and the Active Station: every control is reachable and shows the focus ring.
- Confirm a cancel, reject, obsolete, deactivate, remove step, remove part, discard changes, override, restore and reset: each asks first.

Latest full run (2026-10-18, S9): backend 507/507, browser 119/119.
