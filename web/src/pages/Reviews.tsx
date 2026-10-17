import { useEffect, useState } from 'react';
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Checkbox, FormControlLabel, IconButton, MenuItem, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import DeleteOutline from '@mui/icons-material/DeleteOutline';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { z } from 'zod';
import { del, get, post, put } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, fmtDate, fmtDateTime, localDate } from '../components/Status';
import { ActionsPanel, AttachmentsAndHistory } from '../components/RecordTabs';
import { Facts, Prose, SchemaForm, Section, StepDialog, useRunner, useUsers } from '../components/Kit';
import { mono } from '../theme';

export const reviewStatus = ['Planned', 'InProgress', 'PendingApproval', 'Approved', 'Cancelled'];
const mod = () => modules.find((m) => m.key === 'objectives')!;
const num = (x: { getValue: () => unknown }) => <span style={{ fontFamily: mono }}>{String(x.getValue())}</span>;

/** Section titles for the compiled input snapshot; unknown keys still render with their key. */
const sectionTitles: Record<string, string> = {
  previousActions: 'Actions from the previous review', objectives: 'Quality objectives and KPI performance', productConformity: 'Product conformity and NCRs', capa: 'CAPA performance',
  complaints: 'Customer complaints and satisfaction', audits: 'Audit results', suppliers: 'Supplier performance', calibration: 'Calibration performance', training: 'Training performance',
  actions: 'Action performance', risks: 'Risks and opportunities', changes: 'Changes affecting the QMS',
};
const label = (k: string) => k.replace(/([A-Z])/g, ' $1').replace(/^./, (c) => c.toUpperCase());

function Value({ v }: { v: unknown }): React.ReactNode {
  if (v === null || v === undefined) return '—';
  if (Array.isArray(v)) {
    if (v.length === 0) return <Typography component="span" color="text.secondary" fontSize={13}>None</Typography>;
    if (typeof v[0] !== 'object') return v.join(', ');
    const keys = Object.keys(v[0] as object);
    return <Table size="small"><TableHead><TableRow>{keys.map((k) => <TableCell key={k}>{label(k)}</TableCell>)}</TableRow></TableHead>
      <TableBody>{v.map((r, i) => <TableRow key={i}>{keys.map((k) => <TableCell key={k}><Value v={(r as Record<string, unknown>)[k]} /></TableCell>)}</TableRow>)}</TableBody></Table>;
  }
  if (typeof v === 'object') {
    const o = v as Record<string, unknown>;
    if ('value' in o && 'basis' in o) return <span><b style={{ fontFamily: mono }}>{String(o.value)}</b> <Typography component="span" color="text.secondary" fontSize={12.5}>({String(o.basis)})</Typography></span>;
    return <Box sx={{ display: 'grid', gridTemplateColumns: '200px 1fr', rowGap: 0.75, columnGap: 2 }}>{Object.entries(o).map(([k, x]) => [<Typography key={k + 'l'} variant="body2" color="text.secondary">{label(k)}</Typography>, <Box key={k + 'v'} sx={{ fontSize: 14 }}><Value v={x} /></Box>])}</Box>;
  }
  return String(v);
}

interface Row { id: string; number: string; title: string; scheduledOn: string; heldOn: string | null; status: number; decisions: number; openActions: number; ownerName: string; siteCode: string }
export function ReviewList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const view = p.get('view') ?? 'all';
  const list = useQuery({ queryKey: ['reviews', view], queryFn: () => get<{ items: Row[] }>(`/api/reviews?pageSize=200${view === 'all' ? '' : `&status=${view}`}`) });
  const cols: ColumnDef<Row, any>[] = [
    { header: 'Number', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' }, { header: 'Scheduled', accessorKey: 'scheduledOn', cell: (x) => fmtDate(x.getValue()) }, { header: 'Held', accessorKey: 'heldOn', cell: (x) => fmtDate(x.getValue()) },
    { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={reviewStatus[x.getValue()]} /> }, { header: 'Decisions', accessorKey: 'decisions' }, { header: 'Open actions', accessorKey: 'openActions' },
    { header: 'Chair', accessorKey: 'ownerName' }, { header: 'Site', accessorKey: 'siteCode' },
  ];
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Management review' }]} title="Management reviews"
      tabs={[{ label: 'All', value: 'all' }, { label: 'Planned', value: '0' }, { label: 'In progress', value: '1' }, { label: 'Pending approval', value: '2' }, { label: 'Approved', value: '3' }]} tab={view} onTab={(v) => setP(v === 'all' ? {} : { view: v })}
      actions={can('reviews.edit') ? <Button variant="contained" component={RouterLink} to="/reviews/new">Schedule review</Button> : undefined}>
      <DataTable data={list.data?.items ?? []} columns={cols} onRowClick={(r) => nav(`/reviews/${r.id}`)} empty="No management reviews in this view." />
    </ModuleWorkspace>
  );
}

const schema = z.object({ title: z.string().trim().min(1, 'Required').max(300), scheduledOn: z.string().min(10, 'Required'), periodStart: z.string().min(10, 'Required'), periodEnd: z.string().min(10, 'Required'),
  agenda: z.string().max(10000).optional(), siteId: z.string().uuid('Choose a site'), departmentId: z.string().optional(), ownerId: z.string().optional() });
export function ReviewNew() {
  const nav = useNavigate();
  const users = useUsers();
  const [people, setPeople] = useState<string[]>([]);
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Management review', to: '/reviews' }, { label: 'Schedule' }]} title="Schedule management review">
      <Paper sx={{ p: 2, mb: 2, maxWidth: 820 }}>
        <TextField select fullWidth label="Participants (invited when the review is scheduled)" SelectProps={{ multiple: true }} value={people} onChange={(e) => setPeople(typeof e.target.value === 'string' ? e.target.value.split(',') : e.target.value as string[])}>
          {(users.data ?? []).map((u) => <MenuItem key={u.id} value={u.id}>{u.displayName}</MenuItem>)}
        </TextField>
      </Paper>
      <SchemaForm schema={schema} submitLabel="Schedule" onCancel={() => nav('/reviews')}
        defaults={{ title: '', scheduledOn: localDate(14), periodStart: localDate(-90), periodEnd: localDate(), agenda: 'Status of actions from previous reviews\nChanges in external and internal issues\nQuality objectives and KPIs\nCustomer satisfaction and complaints\nProduct conformity and NCRs\nAudit results\nSupplier performance\nRisks and opportunities\nResource needs\nImprovement opportunities', siteId: '', departmentId: '', ownerId: '' }}
        fields={[{ name: 'title', label: 'Title', full: true }, { name: 'scheduledOn', label: 'Meeting date', type: 'date' }, { name: 'siteId', label: 'Site', type: 'site' }, { name: 'periodStart', label: 'Review period from', type: 'date' }, { name: 'periodEnd', label: 'to', type: 'date' },
          { name: 'agenda', label: 'Agenda', type: 'multiline' }, { name: 'departmentId', label: 'Department (optional)', type: 'department' }, { name: 'ownerId', label: 'Chair (defaults to you)', type: 'user' }]}
        onSubmit={async (v) => { const r = await post<{ id: string }>('/api/reviews', { ...v, agenda: v.agenda || null, departmentId: v.departmentId || null, ownerId: v.ownerId || null, participants: people }); nav(`/reviews/${r.id}`); }} />
    </ModuleWorkspace>
  );
}

interface Dto { id: string; number: string; title: string; siteId: string; departmentId: string | null; ownerId: string; scheduledOn: string; periodStart: string; periodEnd: string; agenda: string | null; qualityPolicy: string | null; inputsCompiledAt: string | null;
  resourceNeeds: string | null; improvementOpportunities: string | null; minutes: string | null; heldOn: string | null; status: number; approvedAt: string | null; rowVersion: number; inputs: Record<string, unknown> | null; discussion: Record<string, string> | null;
  ownerName: string; siteCode: string; participants: { userId: string; displayName: string; attended: boolean }[]; decisions: { id: string; topic: string; decision: string }[]; canEdit: boolean; allowed: string[] }

function MeetingForm({ d, onSaved }: { d: Dto; onSaved: () => void }) {
  const [f, setF] = useState({ heldOn: d.heldOn ?? localDate(), qualityPolicy: d.qualityPolicy ?? '', resourceNeeds: d.resourceNeeds ?? '', improvementOpportunities: d.improvementOpportunities ?? '', minutes: d.minutes ?? '' });
  const [disc, setDisc] = useState<Record<string, string>>(d.discussion ?? {});
  const [att, setAtt] = useState<string[]>(d.participants.filter((p) => p.attended).map((p) => p.userId));
  const { error, run } = useRunner(onSaved);
  useEffect(() => { setDisc(d.discussion ?? {}); }, [d.discussion]);
  const topics = Object.keys(d.inputs ?? {}).filter((k) => k !== 'period');
  return (
    <Stack gap={2}>
      {error && <Alert severity="error">{error}</Alert>}
      <Section title="Attendance">{d.participants.length === 0 ? <Alert severity="warning">No participants. Edit the review to add them.</Alert> :
        <Stack direction="row" flexWrap="wrap" gap={1}>{d.participants.map((p) => <FormControlLabel key={p.userId} label={p.displayName}
          control={<Checkbox checked={att.includes(p.userId)} onChange={(e) => setAtt(e.target.checked ? [...att, p.userId] : att.filter((x) => x !== p.userId))} />} />)}</Stack>}</Section>
      <Section title="Discussion of inputs"><Stack gap={1.5}>{topics.map((t) => <TextField key={t} label={sectionTitles[t] ?? label(t)} multiline minRows={2} value={disc[t] ?? ''} onChange={(e) => setDisc({ ...disc, [t]: e.target.value })} />)}</Stack></Section>
      <Section title="Meeting record"><Stack gap={1.5}>
        <TextField type="date" label="Held on" value={f.heldOn} onChange={(e) => setF({ ...f, heldOn: e.target.value })} slotProps={{ inputLabel: { shrink: true } }} sx={{ width: 200 }} />
        <TextField label="Quality policy (suitability, changes)" multiline minRows={2} value={f.qualityPolicy} onChange={(e) => setF({ ...f, qualityPolicy: e.target.value })} />
        <TextField label="Resource needs" multiline minRows={2} value={f.resourceNeeds} onChange={(e) => setF({ ...f, resourceNeeds: e.target.value })} />
        <TextField label="Improvement opportunities" multiline minRows={2} value={f.improvementOpportunities} onChange={(e) => setF({ ...f, improvementOpportunities: e.target.value })} />
        <TextField label="Minutes" multiline minRows={5} value={f.minutes} onChange={(e) => setF({ ...f, minutes: e.target.value })} />
        <Box><Button variant="contained" onClick={() => run(() => put(`/api/reviews/${d.id}/meeting`, { ...f, qualityPolicy: f.qualityPolicy || null, resourceNeeds: f.resourceNeeds || null, improvementOpportunities: f.improvementOpportunities || null, discussion: disc, attended: att, rowVersion: d.rowVersion }))}>Save meeting record</Button></Box>
      </Stack></Section>
    </Stack>
  );
}

export function ReviewDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const [tab, setTab] = useState('inputs');
  const [dlg, setDlg] = useState<string | null>(null);
  const q = useQuery({ queryKey: ['review', id], queryFn: () => get<Dto>(`/api/reviews/${id}`) });
  const refresh = () => qc.invalidateQueries({ queryKey: ['review', id] });
  const { error, run } = useRunner(refresh);
  const d = q.data;
  if (!d) return <ModuleWorkspace module={mod()} crumbs={[{ label: 'Management review', to: '/reviews' }]} title="Loading">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const a = (k: string) => d.allowed.includes(k);
  const sections = Object.entries(d.inputs ?? {}).filter(([k]) => k !== 'period');
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Management review', to: '/reviews' }, { label: d.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono, whiteSpace: 'nowrap' }}>{d.number}</span><span>{d.title}</span><StatusChip status={reviewStatus[d.status]} /></Stack>}
      tabs={[{ label: 'Inputs', value: 'inputs' }, { label: 'Meeting', value: 'meeting' }, { label: `Decisions (${d.decisions.length})`, value: 'decisions' }, { label: 'Actions', value: 'actions' }, { label: 'Files & history', value: 'more' }]} tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1} flexWrap="wrap">
        {a('compile') && <Button variant={d.inputs ? 'outlined' : 'contained'} onClick={() => run(() => post(`/api/reviews/${d.id}/compile`))}>{d.inputs ? 'Recompile inputs' : 'Compile inputs from records'}</Button>}
        {a('submit') && <Button variant="contained" onClick={() => run(() => post(`/api/reviews/${d.id}/submit`))}>Submit for approval</Button>}
        {a('cancel') && <Button color="error" onClick={() => setDlg('cancel')}>Cancel</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {d.status === 3 && <Alert severity="success" sx={{ mb: 2 }}>Approved {fmtDateTime(d.approvedAt)}. This record and its input snapshot are locked; actions continue to be tracked.</Alert>}
      {d.status === 2 && <Alert severity="info" sx={{ mb: 2 }}>Waiting for approval of the meeting record.</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: '270px 1fr', gap: 3 }}>
        <Facts items={[['Meeting date', fmtDate(d.scheduledOn)], ['Held', fmtDate(d.heldOn)], ['Period', `${fmtDate(d.periodStart)} – ${fmtDate(d.periodEnd)}`], ['Chair', d.ownerName], ['Site', d.siteCode],
          ['Inputs compiled', fmtDateTime(d.inputsCompiledAt)], ['Participants', d.participants.map((p) => `${p.displayName}${p.attended ? ' ✓' : ''}`).join(', ') || '—']]} />
        <Box sx={{ minWidth: 0 }}>
          {tab === 'inputs' && (d.inputs ? <Stack gap={2}>
            <Typography variant="body2" color="text.secondary">Compiled from system records for {d.siteCode}, {fmtDate(d.periodStart)} – {fmtDate(d.periodEnd)}. The snapshot is stored with the review so it does not change when records change later.</Typography>
            {d.agenda && <Section title="Agenda"><Prose label="" text={d.agenda} /></Section>}
            {sections.map(([k, v]) => <Section key={k} title={sectionTitles[k] ?? label(k)}><Value v={v} />{d.discussion?.[k] && <Box sx={{ mt: 1.5 }}><Prose label="Discussion" text={d.discussion[k]} /></Box>}</Section>)}
          </Stack> : <Alert severity="info">Inputs have not been compiled. Compile them from records before the meeting.</Alert>)}
          {tab === 'meeting' && (a('record') ? <MeetingForm d={d} onSaved={refresh} /> : <Stack gap={2}>
            <Section title="Meeting record"><Prose label="Quality policy" text={d.qualityPolicy} /><Prose label="Resource needs" text={d.resourceNeeds} /><Prose label="Improvement opportunities" text={d.improvementOpportunities} /><Prose label="Minutes" text={d.minutes} /></Section>
            {!d.inputs && <Alert severity="info">Compile inputs to start the meeting record.</Alert>}</Stack>)}
          {tab === 'decisions' && <Section title="Decisions" action={a('decide') ? <Button size="small" onClick={() => setDlg('decide')}>Add decision</Button> : undefined}>
            {d.decisions.length === 0 ? <Typography color="text.secondary" fontSize={14}>No decisions recorded.</Typography> :
              <Table size="small"><TableHead><TableRow><TableCell>Topic</TableCell><TableCell>Decision</TableCell><TableCell /></TableRow></TableHead><TableBody>{d.decisions.map((x) => <TableRow key={x.id}>
                <TableCell sx={{ width: 220 }}>{x.topic}</TableCell><TableCell sx={{ whiteSpace: 'pre-wrap' }}>{x.decision}</TableCell>
                <TableCell sx={{ width: 50 }}>{a('decide') && <IconButton aria-label="Remove decision" size="small" onClick={() => run(() => del(`/api/reviews/${d.id}/decisions/${x.id}`))}><DeleteOutline fontSize="small" /></IconButton>}</TableCell></TableRow>)}</TableBody></Table>}
            <Typography variant="body2" color="text.secondary" sx={{ mt: 1.5 }}>Raise the resulting actions with owners and due dates on the Actions tab. Overdue actions are escalated to whoever raised them.</Typography>
          </Section>}
          {tab === 'actions' && <ActionsPanel type="ManagementReview" id={d.id} canEdit={a('actions')} canVerifyAs={d.canEdit} onChanged={refresh} />}
          {tab === 'more' && <AttachmentsAndHistory type="ManagementReview" id={d.id} canEdit={d.canEdit && d.status < 2} />}
        </Box>
      </Box>
      <StepDialog open={dlg === 'decide'} title="Record decision" confirm="Add" onClose={() => setDlg(null)}
        inputs={[{ name: 'topic', label: 'Topic', type: 'select', initial: 'Improvement', options: ['Improvement', 'Changes to the QMS', 'Resource needs', 'Quality objectives', 'Quality policy', 'Risks and opportunities', 'Customer', 'Supplier', 'Other'].map((t) => [t, t] as [string, string]) },
          { name: 'decision', label: 'Decision', type: 'multiline' }]} onConfirm={(v) => run(() => post(`/api/reviews/${d.id}/decisions`, v))} />
      <StepDialog open={dlg === 'cancel'} title={`Cancel ${d.number}`} confirm="Cancel review" danger onClose={() => setDlg(null)} inputs={[{ name: 'text', label: 'Reason', type: 'multiline' }]} onConfirm={(v) => run(() => post(`/api/reviews/${d.id}/cancel`, v))} />
    </ModuleWorkspace>
  );
}
