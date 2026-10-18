# B12 enterprise features: verification before building (FI phase 6, TECH section 13)

Rule (TECH 13): inspect first, do not duplicate, build only what is missing or incomplete, and use the shared services
(numbering, audit trail, approval routes, signer verification, record links, attachments, notifications, site calendar, permissions).

Status: **Existing** = already covered by a working module (location given); **Extended** = existing module completed in B12;
**Built** = added in B12 (Quality operations).

## FI phase 6

| Capability | Status | Where |
| --- | --- | --- |
| Enterprise risk program: strategic, operational, quality, supplier, product risk | Existing | Risks (api/risks, register, matrix, reviews, residual); category list configurable in risk settings |
| Deviation management: planned, unplanned, temporary | Built | Quality operations: deviations (kind, effective window, quantity limit, approval, expiry) |
| Investigation management: cross-module, escalations, evidence | Built | Quality operations: investigations (linked records, escalation level, evidence attachments) |
| Complaint trending, risk scoring, correlation | Existing / Extended | Complaints trends (api/complaints/trends); correlation and recurrence added through recurrence detection |
| Quality cost analytics: COPQ, prevention, appraisal, internal and external failure | Existing | COQ (api/coq, CoqCategory with the four classes) |
| Quality data lake: historical trending, cross-module analysis, predictive | Existing | Quality metric facts + insights (api/insights), intelligence (api/intelligence) |
| Knowledge repository: lessons learned, best practices, root cause and corrective action libraries | Existing | Knowledge (api/knowledge, KnowledgeKind) |
| Supplier collaboration: shared action plans, evidence, audits, approvals | Existing | Portal (api/portal), supplier development, SCAR |
| Customer collaboration: complaint collaboration, corrective action review, product quality reporting | Existing | Portal (api/portal) customer side |
| Requirement management: registry, traceability, verification, validation | Existing | Engineering requirements (ProductRequirement, revisions, design reviews) |
| Digital quality command center: global risk and NCR dashboards, health and readiness scoring | Existing / Built | Enterprise rollup and war room; quality and audit readiness scores built in B12 |

## TECH section 13

| Capability | Status | Where |
| --- | --- | --- |
| Planned deviation management | Built | Deviations, kind Planned |
| Unplanned deviation management | Built | Deviations, kind Unplanned |
| Laboratory investigation | Built | Investigations, kind Laboratory (sample, method, specification, result, out-of-specification flag) |
| Product release management | Built | Release decisions, kind Product |
| Batch or lot release | Built | Release decisions, kind Batch/Lot, blocked by open holds, nonconformances and deviations on the lot |
| Delegation and temporary responsibility | Built | Delegations (period, scope), honoured by approvals |
| External-partner collaboration | Existing | Portal |
| Supplier change notification | Built | Registers, kind Supplier change notification (received, evaluation, decision) |
| Customer-specific requirement register | Existing | Compliance obligations with source Customer (api/compliance) |
| Regulatory intelligence register | Built | Registers, kind Regulatory intelligence |
| Quality event intake | Built | Quality events (web, mobile offline with client reference, integration boundary) |
| Quality event triage | Built | Quality events triage step |
| Quality event classification | Built | Quality events classification and severity |
| Cross-module investigation | Built | Investigations, kind Cross-module |
| Recurrence detection | Built | Recurrence analysis across quality events, nonconformances and complaints |
| Quality readiness scoring | Built | Readiness scores |
| Audit readiness scoring | Built | Readiness scores |
| CAPA effectiveness analytics | Built | Analytics |
| Quality data lake or analytical store | Existing | Quality metric facts |
| Mobile offline quality-event intake | Built | Quality events accept a client reference so a queued offline submission is stored once |
| Periodic access review | Existing | Access reviews (governance) |
| Controlled master data | Existing | Master data changes with approval (data governance) |
| Release management | Existing | Deployment releases (governance) |
| Product quality review | Built | Quality reviews, kind Product |
| Annual quality review | Built | Quality reviews, kind Annual |
| Lab sample tracking where configured | Built | Registers, kind Laboratory sample (enabled per organization) |
| Environmental monitoring integration boundary | Built | Quality event intake with source Environmental monitoring (API client) |
| Safety and EHS integration boundary | Built | Quality event intake with source Safety and EHS (API client) |
| Complaint intake automation boundary | Built | Quality event intake through an API client; a classified event with a customer escalates to a new customer complaint carrying its facts (title, description, lot, part, product, supplier, client reference, severity) |
| Quality agreement management | Built | Registers, kind Quality agreement |
| Contract manufacturer collaboration | Built | Registers, kind Contract manufacturer, plus the portal |
| Approved laboratory management | Built | Registers, kind Approved laboratory |
| Stability-program configuration | Built | Registers, kind Stability program |
| Recall simulation or mock-recall exercises | Extended | Recalls gain an exercise flag and trace-completeness timing; the recall pages offer the exercise option and show the exercise result (trace time, parties, quantity, containment) |
| Business continuity quality exercises | Existing | Continuity exercises (governance) |
| Inspection skip-lot transitions | Built | Skip-lot rules and states per supplier and part |
| Dynamic supplier-risk monitoring | Built | Supplier risk monitor computed from performance, nonconformances, corrective actions and certificates |
| Quality hold and release board | Built | Board combining active holds and pending release decisions |
| Quality event severity matrix | Built | Configurable likelihood by impact matrix |
| Recurrence and systemic-issue detection | Built | Recurrence analysis with systemic threshold |
| Quality process cycle-time analytics | Built | Analytics from status history |
| Bottleneck analytics | Built | Analytics: average time in each status |
| Record-aging analytics | Built | Analytics: open records by age band |
| SLA management | Built | Service level policies per module and status |
| Delegated approval | Built | Approvals accept the delegate during an active delegation |
| Escalation calendars | Built | Service level policies use a business calendar for escalation timing |
| Holiday calendars | Built | Business calendars with holidays per site |
| Regional time-zone rules | Existing | Site time zone via the site calendar |
