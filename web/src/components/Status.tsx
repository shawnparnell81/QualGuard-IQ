import { Chip } from '@mui/material';

const colors: Record<string, 'default' | 'success' | 'warning' | 'error' | 'info' | 'primary' | 'secondary'> = {
  Draft: 'default', InReview: 'warning', Approved: 'info', Effective: 'success', Active: 'success', Superseded: 'default', Obsolete: 'error',
  Open: 'warning', InProgress: 'info', Completed: 'primary', Verified: 'success', Cancelled: 'default', Pending: 'warning', Rejected: 'error',
};
const labels: Record<string, string> = { InReview: 'In review', InProgress: 'In progress' };

export const StatusChip = ({ status }: { status: string }) => (
  <Chip size="small" variant={status === 'Effective' || status === 'Active' ? 'filled' : 'outlined'} color={colors[status] ?? 'default'} label={labels[status] ?? status} />
);

export const docStatus = ['Draft', 'Active', 'Obsolete'];
export const revStatus = ['Draft', 'InReview', 'Approved', 'Effective', 'Superseded', 'Obsolete'];
export const actionStatus = ['Open', 'InProgress', 'Completed', 'Verified', 'Cancelled'];
export const approvalStatus = ['Pending', 'Approved', 'Rejected', 'Cancelled'];
export const fmtDate = (d?: string | null) => (d ? new Date(d.length === 10 ? d + 'T00:00:00' : d).toLocaleDateString() : '—');
export const fmtDateTime = (d?: string | null) => (d ? new Date(d).toLocaleString() : '—');
