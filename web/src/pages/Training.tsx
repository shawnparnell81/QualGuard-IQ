import { setupOptions } from '../components/SetupLink';
import { useState } from 'react';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { Alert, Box, Button, Chip, MenuItem, Paper, Stack, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { z } from 'zod';
import { get, post } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, assignmentStatus, fmtDate, fmtDateTime } from '../components/Status';
import { AttachmentsAndHistory } from '../components/RecordTabs';
import { Facts, SchemaForm, StepDialog, useRunner, useUsers } from '../components/Kit';
import { mono } from '../theme';

const mod = () => modules.find((m) => m.key === 'training')!;
const num = (x: { getValue: () => unknown }) => <span style={{ fontFamily: mono }}>{String(x.getValue() ?? '—')}</span>;
const due = (r: { status: number; overdue?: boolean }) => r.overdue ? <Chip size="small" color="error" label="Overdue" /> : <StatusChip status={assignmentStatus[r.status]} />;

interface MyRow { id: string; courseId: string; courseNumber: string; courseTitle: string; documentId: string | null; documentRevision: string | null; dueOn: string; status: number; reason: string; completedAt: string | null; expiresOn: string | null; overdue: boolean }

export function MyTraining() {
  const qc = useQueryClient();
  const [signing, setSigning] = useState<MyRow | null>(null);
  const list = useQuery({ queryKey: ['mytraining'], queryFn: () => get<MyRow[]>('/api/training/my') });
  const cols: ColumnDef<MyRow, any>[] = [
    { header: 'Course', accessorKey: 'courseNumber', cell: num }, { header: 'Title', accessorKey: 'courseTitle' },
    { header: 'Document', accessorFn: (r) => r.documentId ? <RouterLink to={`/documents/${r.documentId}`}>Rev {r.documentRevision ?? '—'}</RouterLink> : '—', cell: (x) => x.getValue() },
    { header: 'Reason', accessorKey: 'reason' }, { header: 'Due', accessorKey: 'dueOn', cell: (x) => fmtDate(x.getValue()) },
    { header: 'Status', id: 'st', cell: (x) => due(x.row.original) }, { header: 'Completed', accessorKey: 'completedAt', cell: (x) => fmtDateTime(x.getValue()) },
    { header: 'Expires', accessorKey: 'expiresOn', cell: (x) => fmtDate(x.getValue()) },
    { header: '', id: 'sign', cell: (x) => x.row.original.status === 0 ? <Button size="small" variant="contained" onClick={(e) => { e.stopPropagation(); setSigning(x.row.original); }}>Sign off</Button> : null },
  ];
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'My training' }]} title="My training">
      <DataTable data={list.data ?? []} columns={cols} empty="No training assigned to you." />
      <StepDialog open={!!signing} title={`Sign off ${signing?.courseNumber ?? ''}`} intro="By signing you confirm that you have read and understood the training material. Your password is your electronic signature." confirm="Sign" onClose={() => setSigning(null)}
        inputs={[{ name: 'password', label: 'Password', type: 'password' }, { name: 'mfaCode', label: 'Authenticator code (if your account uses one)' }, { name: 'notes', label: 'Notes (optional)' }]}
        onConfirm={async (v) => { await post(`/api/training/assignments/${signing!.id}/complete`, { password: v.password, mfaCode: v.mfaCode?.trim() || null, notes: v.notes || null }); qc.invalidateQueries({ queryKey: ['mytraining'] }); }} />
    </ModuleWorkspace>
  );
}

interface CourseRow { id: string; number: string; title: string; dueDays: number; validityMonths: number | null; retrainOnRevision: boolean; documentNumber: string | null; siteCode: string; open: number; completed: number }
export function CourseList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const list = useQuery({ queryKey: ['courses'], queryFn: () => get<CourseRow[]>('/api/training/courses') });
  const cols: ColumnDef<CourseRow, any>[] = [
    { header: 'Course', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' }, { header: 'Document', accessorKey: 'documentNumber', cell: num },
    { header: 'Retrain on revision', accessorKey: 'retrainOnRevision', cell: (x) => x.getValue() ? 'Yes' : 'No' }, { header: 'Due within', accessorKey: 'dueDays', cell: (x) => `${x.getValue()} days` },
    { header: 'Valid for', accessorKey: 'validityMonths', cell: (x) => x.getValue() ? `${x.getValue()} months` : 'No expiry' },
    { header: 'Open', accessorKey: 'open' }, { header: 'Completed', accessorKey: 'completed' }, { header: 'Site', accessorKey: 'siteCode' },
  ];
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Courses' }]} title="Courses" actions={can('training.edit') ? <Button variant="contained" component={RouterLink} to="/training/courses/new">New course</Button> : undefined}>
      <DataTable data={list.data ?? []} columns={cols} onRowClick={(r) => nav(`/training/courses/${r.id}`)} empty="No courses yet." />
    </ModuleWorkspace>
  );
}

const courseSchema = z.object({
  title: z.string().trim().min(1, 'Required').max(300), description: z.string().max(4000).optional(), siteId: z.string().uuid('Choose a site'), departmentId: z.string().optional(),
  documentId: z.string().optional(), retrainOnRevision: z.enum(['true', 'false']), validityMonths: z.union([z.literal(''), z.coerce.number().int().positive()]).optional(), dueDays: z.coerce.number().int().min(1).max(365),
});
export function CourseNew() {
  const nav = useNavigate();
  const docs = useQuery({ queryKey: ['docs-for-courses'], queryFn: () => get<{ items: { id: string; number: string; title: string }[] }>('/api/documents?kind=0&pageSize=200') });
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Courses', to: '/training/courses' }, { label: 'New' }]} title="New course">
      <SchemaForm schema={courseSchema} submitLabel="Create course" onCancel={() => nav('/training/courses')}
        defaults={{ title: '', description: '', siteId: '', departmentId: '', documentId: '', retrainOnRevision: 'true', validityMonths: '', dueDays: 30 }}
        fields={[{ name: 'title', label: 'Title', full: true }, { name: 'description', label: 'Description', type: 'multiline' },
          { name: 'siteId', label: 'Site', type: 'site' }, { name: 'departmentId', label: 'Department (optional)', type: 'department' },
          { name: 'documentId', label: 'Controlled document (optional)', type: 'select', options: [['', 'None'], ...(docs.data?.items ?? []).map((d) => [d.id, `${d.number} — ${d.title}`] as [string, string])] },
          { name: 'retrainOnRevision', label: 'Retrain when a new revision is released', type: 'select', options: [['true', 'Yes'], ['false', 'No']] },
          { name: 'dueDays', label: 'Complete within (days)', type: 'number' }, { name: 'validityMonths', label: 'Valid for (months, blank = no expiry)', type: 'number' }]}
        onSubmit={async (v) => {
          const r = await post<{ id: string }>('/api/training/courses', { ...v, departmentId: v.departmentId || null, documentId: v.documentId || null, retrainOnRevision: v.retrainOnRevision === 'true', validityMonths: v.validityMonths === '' ? null : v.validityMonths });
          nav(`/training/courses/${r.id}`);
        }} />
    </ModuleWorkspace>
  );
}

interface CourseDto { id: string; number: string; title: string; description: string | null; documentNumber: string | null; retrainOnRevision: boolean; validityMonths: number | null; dueDays: number; canEdit: boolean;
  assignments: { id: string; userName: string; documentRevision: string | null; dueOn: string; status: number; reason: string; completedAt: string | null; expiresOn: string | null }[] }
export function CourseDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const users = useUsers();
  const [tab, setTab] = useState('assignments');
  const [picked, setPicked] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const q = useQuery({ queryKey: ['course', id], queryFn: () => get<CourseDto>(`/api/training/courses/${id}`) });
  const { error, run } = useRunner(() => qc.invalidateQueries({ queryKey: ['course', id] }));
  const c = q.data;
  if (!c) return <ModuleWorkspace module={mod()} crumbs={[{ label: 'Courses', to: '/training/courses' }]} title="Loading">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Courses', to: '/training/courses' }, { label: c.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{c.number}</span><span>{c.title}</span></Stack>}
      tabs={[{ label: 'Assignments', value: 'assignments' }, { label: 'Files & history', value: 'more' }]} tab={tab} onTab={setTab}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {msg && <Alert severity="success" sx={{ mb: 2 }} onClose={() => setMsg(null)}>{msg}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: '280px 1fr', gap: 3 }}>
        <Facts items={[['Document', c.documentNumber ?? '—'], ['Retrain on revision', c.retrainOnRevision ? 'Yes' : 'No'], ['Complete within', `${c.dueDays} days`], ['Valid for', c.validityMonths ? `${c.validityMonths} months` : 'No expiry'], ['Description', c.description ?? '—']]} />
        <Box sx={{ minWidth: 0 }}>
          {tab === 'assignments' && <Stack gap={2}>
            {c.canEdit && <Paper sx={{ p: 2 }}>
              <Typography variant="subtitle2" sx={{ mb: 1 }}>Assign people</Typography>
              <Stack direction="row" gap={1}>
                <TextField select size="small" label="People" sx={{ flex: 1 }} value={picked} slotProps={{ select: { multiple: true } }} onChange={(e) => setPicked(e.target.value as unknown as string[])}>
                  {(users.data ?? []).map((u) => <MenuItem key={u.id} value={u.id}>{u.displayName}</MenuItem>)}
                {setupOptions('user', users.data)}</TextField>
                <Button variant="contained" disabled={!picked.length} onClick={() => run(async () => { const r = await post<{ created: number }>(`/api/training/courses/${c.id}/assign`, { userIds: picked, dueOn: null }); setMsg(`${r.created} assignment(s) created. People with open training were skipped.`); setPicked([]); })}>Assign</Button>
              </Stack>
            </Paper>}
            <DataTable data={c.assignments} empty="No one is assigned yet." columns={[
              { header: 'Person', accessorKey: 'userName' }, { header: 'Revision', accessorKey: 'documentRevision', cell: (x) => (x.getValue() as string) ?? '—' }, { header: 'Reason', accessorKey: 'reason' },
              { header: 'Due', accessorKey: 'dueOn', cell: (x) => fmtDate(x.getValue() as string) }, { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={assignmentStatus[x.getValue() as number]} /> },
              { header: 'Completed', accessorKey: 'completedAt', cell: (x) => fmtDateTime(x.getValue() as string) }, { header: 'Expires', accessorKey: 'expiresOn', cell: (x) => fmtDate(x.getValue() as string) }]} />
          </Stack>}
          {tab === 'more' && <AttachmentsAndHistory type="TrainingCourse" id={c.id} canEdit={c.canEdit} />}
        </Box>
      </Box>
    </ModuleWorkspace>
  );
}

interface MatrixRow { id: string; userName: string; courseNumber: string; courseId: string; courseTitle: string; documentRevision: string | null; dueOn: string; status: number; expiresOn: string | null; overdue: boolean }
export function TrainingMatrix() {
  const nav = useNavigate();
  const [filter, setFilter] = useState('open');
  const list = useQuery({ queryKey: ['matrix'], queryFn: () => get<MatrixRow[]>('/api/training/matrix') });
  const rows = (list.data ?? []).filter((r) => filter === 'all' || (filter === 'open' ? r.status === 0 : filter === 'overdue' ? r.overdue : r.status === 1));
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Training status' }]} title="Training status"
      tabs={[{ label: 'Open', value: 'open' }, { label: 'Overdue', value: 'overdue' }, { label: 'Completed', value: 'done' }, { label: 'All', value: 'all' }]} tab={filter} onTab={setFilter}>
      <DataTable data={rows} onRowClick={(r) => nav(`/training/courses/${r.courseId}`)} empty="Nothing in this view." columns={[
        { header: 'Person', accessorKey: 'userName' }, { header: 'Course', accessorKey: 'courseNumber', cell: num }, { header: 'Title', accessorKey: 'courseTitle' },
        { header: 'Revision', accessorKey: 'documentRevision', cell: (x) => (x.getValue() as string) ?? '—' }, { header: 'Due', accessorKey: 'dueOn', cell: (x) => fmtDate(x.getValue() as string) },
        { header: 'Status', id: 'st', cell: (x) => due(x.row.original) }, { header: 'Expires', accessorKey: 'expiresOn', cell: (x) => fmtDate(x.getValue() as string) }]} />
    </ModuleWorkspace>
  );
}
