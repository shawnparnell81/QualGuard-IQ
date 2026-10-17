import { Chip } from '@mui/material';

const colors: Record<string, 'default' | 'success' | 'warning' | 'error' | 'info' | 'primary' | 'secondary'> = {
  Draft: 'default', InReview: 'warning', Approved: 'info', Effective: 'success', Active: 'success', Superseded: 'default', Obsolete: 'error',
  Open: 'warning', Contained: 'info', DispositionPending: 'warning', DispositionApproved: 'info', Closed: 'success', Investigation: 'info', Implementation: 'info', EffectivenessCheck: 'warning',
  Planned: 'default', Reporting: 'warning', ActionPlanned: 'info', Submitted: 'warning', Assigned: 'warning', Quarantined: 'error', OutOfService: 'error', Retired: 'default',
  Pass: 'success', PassAfterAdjustment: 'success', OutOfTolerance: 'error', Fail: 'error', Minor: 'default', Major: 'warning', Critical: 'error', InProgress: 'info', Completed: 'primary', Verified: 'success', Cancelled: 'default', Pending: 'warning', Rejected: 'error',
};
const labels: Record<string, string> = { InReview: 'In review', InProgress: 'In progress', DispositionPending: 'Disposition pending', DispositionApproved: 'Disposition approved',
  EffectivenessCheck: 'Effectiveness check', ActionPlanned: 'Action planned', OutOfService: 'Out of service', PassAfterAdjustment: 'Pass after adjustment', OutOfTolerance: 'Out of tolerance' };

export const StatusChip = ({ status }: { status: string }) => (
  <Chip size="small" variant={status === 'Effective' || status === 'Active' ? 'filled' : 'outlined'} color={colors[status] ?? 'default'} label={labels[status] ?? status} />
);

export const docStatus = ['Draft', 'Active', 'Obsolete'];
export const revStatus = ['Draft', 'InReview', 'Approved', 'Effective', 'Superseded', 'Obsolete'];
export const actionStatus = ['Open', 'InProgress', 'Completed', 'Verified', 'Cancelled'];
export const approvalStatus = ['Pending', 'Approved', 'Rejected', 'Cancelled'];
export const fmtDate = (d?: string | null) => (d ? new Date(d.length === 10 ? d + 'T00:00:00' : d).toLocaleDateString() : '—');
export const fmtDateTime = (d?: string | null) => (d ? new Date(d).toLocaleString() : '—');
export const formStatus = ['Draft', 'Submitted', 'Approved', 'Rejected'];
export const ncrStatus = ['Open', 'Contained', 'DispositionPending', 'DispositionApproved', 'Closed', 'Cancelled'];
export const severity = ['Minor', 'Major', 'Critical'];
export const dispositions = ['None', 'Use as is', 'Rework', 'Repair', 'Scrap', 'Return to supplier', 'Regrade'];
export const capaStatus = ['Open', 'Investigation', 'Implementation', 'EffectivenessCheck', 'Closed', 'Cancelled'];
export const auditStatus = ['Planned', 'InProgress', 'Reporting', 'Closed', 'Cancelled'];
export const checklistResult = ['Not assessed', 'Conforming', 'Nonconforming', 'Observation', 'Not applicable'];
export const findingType = ['Major nonconformity', 'Minor nonconformity', 'Observation', 'Opportunity for improvement'];
export const findingStatus = ['Open', 'ActionPlanned', 'Verified', 'Closed'];
export const assignmentStatus = ['Assigned', 'Completed', 'Superseded', 'Cancelled'];
export const equipmentStatus = ['Active', 'Quarantined', 'OutOfService', 'Retired'];
export const calResult = ['Pass', 'PassAfterAdjustment', 'OutOfTolerance', 'Fail'];
export const impactStatus = ['Open', 'Completed'];
/** Local calendar date (YYYY-MM-DD) offset by whole days; the server re-checks against the site's time zone. */
export const localDate = (addDays = 0) => { const d = new Date(); d.setDate(d.getDate() + addDays); return d.toLocaleDateString('en-CA'); };
