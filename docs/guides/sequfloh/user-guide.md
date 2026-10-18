# SEQUFLOH user guide

Open SEQUFLOH from the QualGuard IQ module list. The menu shows only what your permissions allow.

## SEQUFLOH Home

The dashboard shows open, overdue, paused, on-hold and completed work, pending reviews and approvals, your assigned work and recent activity. Each tile opens the matching list. Quick links, recent items and favorites are on the right; star a record to add it to favorites. Use **Search** to find instructions, parts, parts lists, records, tasks and users.

![SEQUFLOH Home](../../screenshots/sequfloh/s1-home.png)

## Parts and parts lists

Part Master lists the shared parts with the SEQUFLOH fields. Parts Lists hold the items for an assembly or repair; approve a list and create a new revision when it changes. Reusable Operation Steps can be inserted into work instructions.

![Part Master](../../screenshots/sequfloh/s2-part-master.png)
![Parts list](../../screenshots/sequfloh/s2-parts-list.png)

## Work instructions

Create a validation, FAI, repair test or repair work instruction from its folder. The designer has the header, steps, characteristics (FAI) and parts (repair). Save as you go, then **Submit for review**. Reviewers return it for changes or submit it for approval; approvers approve or reject; document control releases it. A released instruction is read-only: use **New revision** to change it. The lifecycle history shows every action.

![Validation designer](../../screenshots/sequfloh/s3-validation-designer.png)
![FAI characteristics](../../screenshots/sequfloh/s3-fai-characteristics.png)
![Lifecycle history](../../screenshots/sequfloh/s3-lifecycle-history.png)

## Tracking records

Create a validation, FAI, repair or production record from a released work instruction. The record copies the instruction revision, so later revisions do not change it. Record step results, characteristic measurements, parts used, signoffs and comments, then submit it for review or complete it.

![New tracking record](../../screenshots/sequfloh/s5-tracking-new.png)
![Tracking record](../../screenshots/sequfloh/s5-tracking-record.png)
![Task events](../../screenshots/sequfloh/s5-tracking-events.png)

Use **Print** on a record for its PDF.

## Live execution

Supervisors dispatch work from the **Dispatch Queue**. Technicians open **My Assigned Work** or **Active Station** and use Start, Pause (with a reason), Resume, Hold, Submit and Complete. Elapsed time is kept by the server, so it is the same after a refresh or on another device. If someone else changed the task first, you are asked to reload. Paused, Completed, Overdue and Work History views list the matching tasks.

![Dispatch queue](../../screenshots/sequfloh/s6-dispatch-queue.png)
![Active station](../../screenshots/sequfloh/s6-active-station.png)
![Paused tasks](../../screenshots/sequfloh/s6-paused-tasks.png)
![Work history](../../screenshots/sequfloh/s6-work-history.png)

## Quality and Engineering

The Quality dashboard and Quality Review Queue show work waiting for Quality review or approval. The Engineering dashboard, Validation and Development pages show engineering instructions and their development records (assigned engineer, target date, project, notes, status and release recommendation).

![Quality dashboard](../../screenshots/sequfloh/s7-quality-dashboard.png)
![Quality review queue](../../screenshots/sequfloh/s7-review-queue.png)
![Engineering dashboard](../../screenshots/sequfloh/s7-engineering-dashboard.png)
![Development](../../screenshots/sequfloh/s7-development.png)

## Reports, export and audit

Reports list task performance, status by type, user activity and time variance with filters. **Export to Excel** downloads the filtered rows with the report title, filters, date and user. The Export Center offers every export you are allowed to run. Audit Trail shows system events, instruction history, access history, record changes and exports; open an entry for its details or print a summary.

![Task performance](../../screenshots/sequfloh/s8-task-performance.png)
![Export Center](../../screenshots/sequfloh/s8-export-center.png)
![Export history](../../screenshots/sequfloh/s8-audit-exports.png)
![Search](../../screenshots/sequfloh/s8-search.png)

## Notifications

SEQUFLOH notifications appear in the QualGuard IQ notification bell (and by email when your preferences say so): instructions sent to you for review or approval, returned, rejected or released; tasks assigned to you, waiting for your review, failed, rejected, overdue or nearly due.

## Administration

See [setup and administration](setup-and-administration.md).
