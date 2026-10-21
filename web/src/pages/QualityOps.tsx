import { setupOptions } from '../components/SetupLink';
import { useState } from 'react';
import type { ReactNode } from 'react';
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { z } from 'zod';
import { del, get, post, put } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, fmtDate, fmtDateTime, localDate } from '../components/Status';
import { ApprovalsPanel, AttachmentsAndHistory } from '../components/RecordTabs';
import { Facts, Prose, SchemaForm, Section, StepDialog, useLookup, useRunner, useUsers } from '../components/Kit';
import { SitePicker } from '../components/SitePicker';
import { RecordPicker } from '../components/RecordPicker';
import type { PickedRecord } from '../components/RecordPicker';
import { mono } from '../theme';

const mod = () => modules.find((m) => m.key === 'qualityops')!;
const num = (x: { getValue: () => unknown }) => <span style={{ fontFamily: mono }}>{String(x.getValue() ?? '')}</span>;
const nul = (v?: string | null) => (v ? v : null);
const numOrNull = (v?: string | number | null) => (v === '' || v === undefined || v === null ? null : Number(v));
const Q = '/api/quality-ops';
const base = '/quality-ops';
type Crumb = { label: string; to?: string };
const Page = ({ crumbs, title, children, actions, tabs, tab, onTab }: { crumbs: Crumb[]; title: ReactNode; children?: ReactNode; actions?: ReactNode; tabs?: { label: string; value: string }[]; tab?: string; onTab?: (v: string) => void }) =>
  <ModuleWorkspace module={mod()} crumbs={crumbs} title={title} actions={actions} tabs={tabs} tab={tab} onTab={onTab}>{children}</ModuleWorkspace>;
const Loading = ({ crumbs, error }: { crumbs: Crumb[]; error: unknown }) => <Page crumbs={crumbs} title="Loading">{error ? <Alert severity="error">{(error as Error).message}</Alert> : null}</Page>;
const Title = ({ number, title, status }: { number: string; title: string; status: string }) =>
  <Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono, whiteSpace: 'nowrap' }}>{number}</span><span>{title}</span><StatusChip status={status} /></Stack>;
function Filter({ value, onApply, placeholder }: { value: string; onApply: (v: string) => void; placeholder: string }) {
  const [q, setQ] = useState(value);
  return <Stack direction="row" gap={1} component="form" sx={{ mb: 2 }} onSubmit={(e) => { e.preventDefault(); onApply(q); }}>
    <TextField size="small" placeholder={placeholder} value={q} onChange={(e) => setQ(e.target.value)} sx={{ width: 360 }} /><Button type="submit" variant="outlined">Filter</Button></Stack>;
}

// ---------------------------------------------------------------- settings
interface Level { value: number; label: string }
interface Severity { name: string; color: string; responseHours: number; escalate: boolean; order: number }
export interface OpsSettings { likelihood: Level[]; impact: Level[]; severities: Severity[]; cells: { likelihood: number; impact: number; severity: string }[]; categories: string[]; classifications: string[];
  recurrenceWindowDays: number; systemicThreshold: number; labSampleTracking: boolean; rowVersion: number }
const useSettings = () => useQuery({ queryKey: ['qops-settings'], queryFn: () => get<OpsSettings>(`${Q}/settings`) });
export function SeverityChip({ settings, name }: { settings?: OpsSettings; name: string | null }) {
  if (!name) return <span>—</span>;
  const s = settings?.severities.find((x) => x.name === name);
  return <Box component="span" sx={{ px: 1, py: 0.2, borderRadius: 1, fontSize: 12.5, fontWeight: 600, color: '#fff', bgcolor: s?.color ?? 'grey.600', whiteSpace: 'nowrap' }}>{name}</Box>;
}

export function QualityOpsSettingsPage() {
  const q = useSettings();
  const qc = useQueryClient();
  const [draft, setDraft] = useState<OpsSettings | null>(null);
  const { error, run } = useRunner(() => { setDraft(null); qc.invalidateQueries({ queryKey: ['qops-settings'] }); });
  const s = draft ?? q.data;
  const crumbs = [{ label: 'Settings' }];
  if (!s) return <Loading crumbs={crumbs} error={q.error} />;
  const upd = (f: (c: OpsSettings) => void) => { const c = structuredClone(s); f(c); setDraft(c); };
  return (
    <Page crumbs={crumbs} title="Event severity and intake settings"
      actions={<Stack direction="row" gap={1}>{draft && <Button onClick={() => setDraft(null)}>Discard</Button>}<Button variant="contained" disabled={!draft} onClick={() => run(() => put(`${Q}/settings`, s))}>Save settings</Button></Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <Section title="Severity matrix (likelihood × impact)">
        <Box sx={{ display: 'grid', gridTemplateColumns: `150px repeat(${s.impact.length}, 120px)`, gap: 0.5 }}>
          {[...s.likelihood].reverse().map((l) => [
            <Box key={'l' + l.value} sx={{ fontSize: 13, display: 'flex', alignItems: 'center', justifyContent: 'flex-end', pr: 1, color: 'text.secondary' }}>{l.value} · {l.label}</Box>,
            ...s.impact.map((i) => {
              const cell = s.cells.find((c) => c.likelihood === l.value && c.impact === i.value);
              const sev = s.severities.find((x) => x.name === cell?.severity);
              return <TextField key={`${l.value}-${i.value}`} select size="small" value={cell?.severity ?? ''} slotProps={{ htmlInput: { 'aria-label': `Likelihood ${l.value} impact ${i.value}` } }}
                sx={{ '& .MuiInputBase-root': { bgcolor: sev?.color, color: '#fff', fontWeight: 600 } }}
                onChange={(e) => upd((c) => { const x = c.cells.find((y) => y.likelihood === l.value && y.impact === i.value); if (x) x.severity = e.target.value; else c.cells.push({ likelihood: l.value, impact: i.value, severity: e.target.value }); })}>
                {s.severities.map((v) => <MenuItem key={v.name} value={v.name}>{v.name}</MenuItem>)}</TextField>;
            }),
          ])}
          <Box />{s.impact.map((i) => <Box key={'i' + i.value} sx={{ fontSize: 12, textAlign: 'center', color: 'text.secondary', pt: 0.5 }}>{i.value} · {i.label}</Box>)}
        </Box>
        <Typography variant="caption" color="text.secondary">Rows: likelihood. Columns: impact.</Typography>
      </Section>
      <Box sx={{ mt: 2 }}><Section title="Severity levels">
        <Table size="small"><TableHead><TableRow><TableCell>Name</TableCell><TableCell>Color</TableCell><TableCell>Respond within (hours)</TableCell><TableCell>Notify approvers</TableCell></TableRow></TableHead>
          <TableBody>{s.severities.map((v, k) => <TableRow key={v.name}>
            <TableCell>{v.name}</TableCell>
            <TableCell><input type="color" aria-label={`Color for ${v.name}`} value={v.color} onChange={(e) => upd((c) => { c.severities[k].color = e.target.value; })} /></TableCell>
            <TableCell><TextField size="small" type="number" value={v.responseHours} sx={{ width: 110 }} slotProps={{ htmlInput: { 'aria-label': `Response hours for ${v.name}` } }} onChange={(e) => upd((c) => { c.severities[k].responseHours = Number(e.target.value); })} /></TableCell>
            <TableCell><Checkbox checked={v.escalate} slotProps={{ input: { 'aria-label': `Notify approvers for ${v.name}` } }} onChange={(e) => upd((c) => { c.severities[k].escalate = e.target.checked; })} /></TableCell>
          </TableRow>)}</TableBody></Table>
      </Section></Box>
      <Box sx={{ mt: 2, display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: '1fr 1fr' }, gap: 2 }}>
        <Section title="Categories"><TextField fullWidth multiline minRows={5} label="One per line" value={s.categories.join('\n')} onChange={(e) => upd((c) => { c.categories = e.target.value.split('\n'); })} onBlur={() => upd((c) => { c.categories = c.categories.map((x) => x.trim()).filter(Boolean); })} /></Section>
        <Section title="Classifications"><TextField fullWidth multiline minRows={5} label="One per line" value={s.classifications.join('\n')} onChange={(e) => upd((c) => { c.classifications = e.target.value.split('\n'); })} onBlur={() => upd((c) => { c.classifications = c.classifications.map((x) => x.trim()).filter(Boolean); })} /></Section>
      </Box>
      <Box sx={{ mt: 2 }}><Section title="Recurrence and laboratory">
        <Stack direction="row" gap={2} alignItems="center" flexWrap="wrap">
          <TextField size="small" type="number" label="Recurrence window (days)" value={s.recurrenceWindowDays} onChange={(e) => upd((c) => { c.recurrenceWindowDays = Number(e.target.value); })} />
          <TextField size="small" type="number" label="Systemic after (occurrences)" value={s.systemicThreshold} onChange={(e) => upd((c) => { c.systemicThreshold = Number(e.target.value); })} />
          <FormControlLabel control={<Checkbox checked={s.labSampleTracking} onChange={(e) => upd((c) => { c.labSampleTracking = e.target.checked; })} />} label="Track laboratory samples" />
        </Stack>
      </Section></Box>
    </Page>
  );
}

// ---------------------------------------------------------------- quality events
export const eventStatus = ['New', 'InTriage', 'Classified', 'Escalated', 'Closed', 'Rejected'];
export const eventSource = ['Manual', 'Portal', 'Integration', 'Api', 'ShopFloor', 'Mobile'];
interface EventRow { id: string; number: string; title: string; source: number; category: string | null; classification: string | null; severity: string | null; status: number; occurredAt: string; respondBy: string | null; lotNumber: string | null; escalatedToNumber: string | null; partNumber: string | null; reportedBy: string }

export function EventList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const s = useSettings().data;
  const [p, setP] = useSearchParams();
  const view = p.get('view') ?? 'open';
  const [applied, setApplied] = useState('');
  const filter = view === 'open' ? '&openOnly=true' : view === 'all' ? '' : `&status=${view}`;
  const list = useQuery({ queryKey: ['qevents', view, applied], queryFn: () => get<{ items: EventRow[] }>(`${Q}/events?pageSize=300${filter}${applied ? `&q=${encodeURIComponent(applied)}` : ''}`) });
  const now = new Date().toISOString();
  const cols: ColumnDef<EventRow, any>[] = [
    { header: 'Number', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' }, { header: 'Source', accessorKey: 'source', cell: (x) => eventSource[x.getValue()] },
    { header: 'Classification', accessorKey: 'classification', cell: (x) => x.getValue() ?? '—' }, { header: 'Severity', accessorKey: 'severity', cell: (x) => <SeverityChip settings={s} name={x.getValue()} /> },
    { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={eventStatus[x.getValue()]} /> },
    { header: 'Respond by', accessorKey: 'respondBy', cell: (x) => <Box component="span" sx={{ color: x.getValue() && x.getValue() < now && x.row.original.status < 3 ? 'error.main' : undefined }}>{fmtDateTime(x.getValue())}</Box> },
    { header: 'Part / lot', accessorKey: 'partNumber', cell: (x) => [x.getValue(), x.row.original.lotNumber].filter(Boolean).join(' / ') || '—' },
    { header: 'Escalated to', accessorKey: 'escalatedToNumber', cell: num }, { header: 'Reported by', accessorKey: 'reportedBy' },
  ];
  return (
    <Page crumbs={[{ label: 'Events' }]} title="Quality events"
      tabs={[{ label: 'Open', value: 'open' }, { label: 'New', value: '0' }, { label: 'In triage', value: '1' }, { label: 'Classified', value: '2' }, { label: 'Escalated', value: '3' }, { label: 'Closed', value: '4' }, { label: 'All', value: 'all' }]}
      tab={view} onTab={(v) => setP(v === 'open' ? {} : { view: v })}
      actions={can('qualityops.edit') || can('qualityevents.report') ? <Button variant="contained" component={RouterLink} to={`${base}/events/new`}>Report event</Button> : undefined}>
      <Filter value={applied} onApply={setApplied} placeholder="Number, title, category, lot" />
      <DataTable data={list.data?.items ?? []} columns={cols} onRowClick={(r) => nav(`${base}/events/${r.id}`)} empty="No quality events in this view." />
    </Page>
  );
}

export function EventNew() {
  const nav = useNavigate();
  const intake = useQuery({ queryKey: ['qops-intake'], queryFn: () => get<{ categories: string[] }>(`${Q}/settings/intake`) });
  const crumbs = [{ label: 'Events', to: `${base}/events` }, { label: 'Report' }];
  if (!intake.data) return <Loading crumbs={crumbs} error={intake.error} />;
  const schema = z.object({ title: z.string().trim().min(1, 'Required').max(300), description: z.string().trim().min(1, 'Required').max(10000), category: z.string().optional(), occurredAt: z.string().optional(),
    location: z.string().max(200).optional(), process: z.string().max(200).optional(), partId: z.string().optional(), supplierId: z.string().optional(), customerId: z.string().optional(), lotNumber: z.string().max(100).optional(),
    siteId: z.string().uuid('Choose a site'), departmentId: z.string().optional() });
  return (
    <Page crumbs={crumbs} title="Report a quality event">
      <SchemaForm schema={schema} submitLabel="Report event" onCancel={() => nav(`${base}/events`)}
        defaults={{ title: '', description: '', category: '', occurredAt: '', location: '', process: '', partId: '', supplierId: '', customerId: '', lotNumber: '', siteId: '', departmentId: '' }}
        fields={[{ name: 'title', label: 'What happened', full: true }, { name: 'description', label: 'Description', type: 'multiline' },
          { name: 'category', label: 'Category', type: 'select', options: [['', 'Not sure'], ...intake.data.categories.map((c) => [c, c] as [string, string])] }, { name: 'occurredAt', label: 'When (leave empty for now)', type: 'date' },
          { name: 'location', label: 'Location' }, { name: 'process', label: 'Process' }, { name: 'partId', label: 'Part (optional)', type: 'lookup', lookup: 'part' }, { name: 'lotNumber', label: 'Lot or batch' },
          { name: 'supplierId', label: 'Supplier (optional)', type: 'lookup', lookup: 'supplier' }, { name: 'customerId', label: 'Customer (optional)', type: 'lookup', lookup: 'customer' },
          { name: 'siteId', label: 'Site', type: 'site' }, { name: 'departmentId', label: 'Department (optional)', type: 'department' }]}
        onSubmit={async (v) => {
          const r = await post<{ id: string }>(`${Q}/events`, { ...v, category: nul(v.category), occurredAt: v.occurredAt ? new Date(v.occurredAt + 'T12:00:00').toISOString() : null, location: nul(v.location), process: nul(v.process),
            partId: nul(v.partId), supplierId: nul(v.supplierId), customerId: nul(v.customerId), lotNumber: nul(v.lotNumber), departmentId: nul(v.departmentId) });
          nav(`${base}/events/${r.id}`);
        }} />
    </Page>
  );
}

interface EventDto { event: { id: string; number: string; title: string; description: string; source: number; category: string | null; classification: string | null; likelihood: number | null; impact: number | null; severity: string | null;
  status: number; occurredAt: string; respondBy: string | null; location: string | null; process: string | null; lotNumber: string | null; triageNotes: string | null; closureNotes: string | null; escalatedToType: string | null; escalatedToId: string | null;
  escalatedToNumber: string | null; externalSystem: string | null; clientReference: string | null; siteId: string };
  reportedBy: string; triagedBy: string; partNumber: string | null; supplier: string | null; customer: string | null; settings: OpsSettings; actions: Record<string, boolean>; url: string }

export function EventDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const nav = useNavigate();
  const [dlg, setDlg] = useState<string | null>(null);
  const q = useQuery({ queryKey: ['qevent', id], queryFn: () => get<EventDto>(`${Q}/events/${id}`) });
  const { error, run } = useRunner(() => qc.invalidateQueries({ queryKey: ['qevent', id] }));
  const crumbs = [{ label: 'Events', to: `${base}/events` }];
  const d = q.data;
  if (!d) return <Loading crumbs={crumbs} error={q.error} />;
  const e = d.event; const s = d.settings; const a = d.actions;
  const lv = (l: Level[]) => l.map((x) => [x.value, `${x.value} · ${x.label}`] as [number, string]);
  const target = e.escalatedToType === 'Deviation' ? `${base}/deviations/${e.escalatedToId}` : e.escalatedToType === 'QualityInvestigation' ? `${base}/investigations/${e.escalatedToId}` : e.escalatedToId ? `/records/${e.escalatedToType}/${e.escalatedToId}` : null;
  return (
    <Page crumbs={[...crumbs, { label: e.number }]} title={<Title number={e.number} title={e.title} status={eventStatus[e.status]} />}
      actions={<Stack direction="row" gap={1} flexWrap="wrap">
        {a.triage && <Button variant="contained" onClick={() => run(() => post(`${Q}/events/${e.id}/triage`))}>Start triage</Button>}
        {a.classify && <Button variant={e.status === 1 ? 'contained' : 'outlined'} onClick={() => setDlg('classify')}>Classify and rate</Button>}
        {a.escalate && <Button variant="contained" onClick={() => setDlg('escalate')}>Escalate</Button>}
        {a.close && <Button onClick={() => setDlg('close')}>Close</Button>}
        {a.reject && <Button color="error" onClick={() => setDlg('reject')}>Reject</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {e.respondBy && e.status < 3 && e.respondBy < new Date().toISOString() && <Alert severity="error" sx={{ mb: 2 }}>The response time for this severity passed at {fmtDateTime(e.respondBy)}.</Alert>}
      {e.escalatedToNumber && target && <Alert severity="info" sx={{ mb: 2 }}>Escalated to <RouterLink to={target}>{e.escalatedToNumber}</RouterLink>.</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: '290px minmax(0, 1fr)' }, gap: 3 }}>
        <Facts items={[['Source', eventSource[e.source] + (e.externalSystem ? ` (${e.externalSystem})` : '')], ['Reference', e.clientReference ?? '—'], ['Occurred', fmtDateTime(e.occurredAt)], ['Reported by', d.reportedBy], ['Category', e.category ?? '—'],
          ['Classification', e.classification ?? '—'], ['Severity', <SeverityChip settings={s} name={e.severity} />], ['Respond by', fmtDateTime(e.respondBy)], ['Part', d.partNumber ?? '—'], ['Lot', e.lotNumber ?? '—'],
          ['Supplier', d.supplier ?? '—'], ['Customer', d.customer ?? '—'], ['Location', e.location ?? '—'], ['Process', e.process ?? '—'], ['Triaged by', d.triagedBy || '—']]} />
        <Stack gap={2} sx={{ minWidth: 0 }}>
          <Section title="Description"><Prose label="Description" text={e.description} /><Prose label="Triage notes" text={e.triageNotes} /><Prose label="Closure" text={e.closureNotes} /></Section>
          <AttachmentsAndHistory type="QualityEvent" id={e.id} canEdit={a.classify || a.close} />
        </Stack>
      </Box>
      <StepDialog open={dlg === 'classify'} title="Classify and rate" intro="Severity and the response time come from the severity matrix." confirm="Save" onClose={() => setDlg(null)}
        inputs={[{ name: 'category', label: 'Category', type: 'select', options: s.categories.map((c) => [c, c]), initial: e.category ?? '' }, { name: 'classification', label: 'Classification', type: 'select', options: s.classifications.map((c) => [c, c]), initial: e.classification ?? '' },
          { name: 'likelihood', label: 'Likelihood', type: 'select', options: lv(s.likelihood), initial: e.likelihood ? String(e.likelihood) : '' }, { name: 'impact', label: 'Impact', type: 'select', options: lv(s.impact), initial: e.impact ? String(e.impact) : '' },
          { name: 'notes', label: 'Triage notes', type: 'multiline', initial: e.triageNotes ?? '' }]}
        onConfirm={async (v) => { await post(`${Q}/events/${e.id}/classify`, { category: nul(v.category), classification: v.classification, likelihood: Number(v.likelihood || 0), impact: Number(v.impact || 0), notes: nul(v.notes) }); qc.invalidateQueries({ queryKey: ['qevent', id] }); }} />
      <EscalateDialog open={dlg === 'escalate'} eventId={e.id} title={e.title} onClose={() => setDlg(null)} onDone={(t) => { qc.invalidateQueries({ queryKey: ['qevent', id] }); if (t.type === 'Deviation') nav(`${base}/deviations/${t.id}`); else if (t.type === 'QualityInvestigation') nav(`${base}/investigations/${t.id}`); else if (t.type === 'Complaint') nav(`/complaints/${t.id}`); }} />
      <StepDialog open={dlg === 'close'} title="Close event" confirm="Close event" onClose={() => setDlg(null)} inputs={[{ name: 'notes', label: 'Closure notes', type: 'multiline' }]}
        onConfirm={async (v) => { await post(`${Q}/events/${e.id}/close`, { notes: v.notes }); qc.invalidateQueries({ queryKey: ['qevent', id] }); }} />
      <StepDialog open={dlg === 'reject'} title="Reject event" intro="Use when the report is not a quality event (duplicate, out of scope)." confirm="Reject" danger onClose={() => setDlg(null)} inputs={[{ name: 'reason', label: 'Reason', type: 'multiline' }]}
        onConfirm={async (v) => { await post(`${Q}/events/${e.id}/reject`, { reason: v.reason }); qc.invalidateQueries({ queryKey: ['qevent', id] }); }} />
    </Page>
  );
}

function EscalateDialog({ open, eventId, title, onClose, onDone }: { open: boolean; eventId: string; title: string; onClose: () => void; onDone: (t: { type: string; id: string }) => void }) {
  const [target, setTarget] = useState('deviation');
  const [t, setT] = useState(title);
  const [owner, setOwner] = useState('');
  const [rec, setRec] = useState({ type: '', id: '' });
  const [err, setErr] = useState<string | null>(null);
  const users = useUsers();
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Escalate event</DialogTitle>
      <DialogContent>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>Create a deviation, an investigation or a customer complaint from this event, or link it to an existing record in another module.</Typography>
        {err && <Alert severity="error" sx={{ mb: 2 }}>{err}</Alert>}
        <Stack gap={2} sx={{ pt: 1 }}>
          <TextField select label="Escalate to" value={target} onChange={(e) => setTarget(e.target.value)}>
            <MenuItem value="deviation">New deviation</MenuItem><MenuItem value="investigation">New investigation</MenuItem><MenuItem value="complaint">New customer complaint</MenuItem><MenuItem value="record">Existing record</MenuItem></TextField>
          {target !== 'record' && <TextField label="Title" value={t} onChange={(e) => setT(e.target.value)} />}
          {target !== 'record' && <TextField select label="Owner" value={owner} onChange={(e) => setOwner(e.target.value)}><MenuItem value="">Me</MenuItem>{(users.data ?? []).map((u) => <MenuItem key={u.id} value={u.id}>{u.displayName}</MenuItem>)}{setupOptions('user', users.data)}</TextField>}
          {target === 'record' && <RecordPicker label="Find the NCR, CAPA, complaint or other record" onChange={(r) => setRec({ type: r?.type ?? '', id: r?.id ?? '' })} />}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" onClick={async () => {
          try {
            const r = await post<{ type: string; id: string }>(`${Q}/events/${eventId}/escalate`, target === 'record' ? { target, recordType: rec.type, recordId: rec.id } : { target, title: nul(t), ownerId: nul(owner) });
            onClose(); onDone(r);
          } catch (e) { setErr((e as Error).message); }
        }}>Escalate</Button>
      </DialogActions>
    </Dialog>
  );
}

// ---------------------------------------------------------------- deviations
export const deviationStatus = ['Draft', 'PendingApproval', 'Approved', 'Rejected', 'Closed', 'Cancelled'];
export const deviationKind = ['Planned', 'Unplanned', 'Temporary'];
interface DeviationRow { id: string; number: string; kind: number; title: string; status: number; effectiveFrom: string | null; effectiveTo: string | null; quantityLimit: number | null; quantityUsed: number; partNumber: string | null; owner: string }

export function DeviationList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const view = p.get('view') ?? 'active';
  const [applied, setApplied] = useState('');
  const filter = view === 'active' ? '&inForce=true' : view === 'all' ? '' : `&status=${view}`;
  const list = useQuery({ queryKey: ['deviations', view, applied], queryFn: () => get<{ items: DeviationRow[] }>(`${Q}/deviations?pageSize=300${filter}${applied ? `&q=${encodeURIComponent(applied)}` : ''}`) });
  const cols: ColumnDef<DeviationRow, any>[] = [
    { header: 'Number', accessorKey: 'number', cell: num }, { header: 'Type', accessorKey: 'kind', cell: (x) => deviationKind[x.getValue()] }, { header: 'Title', accessorKey: 'title' },
    { header: 'Part', accessorKey: 'partNumber', cell: (x) => x.getValue() ?? '—' }, { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={deviationStatus[x.getValue()]} /> },
    { header: 'Window', accessorKey: 'effectiveFrom', cell: (x) => x.getValue() ? `${fmtDate(x.getValue())} – ${fmtDate(x.row.original.effectiveTo)}` : '—' },
    { header: 'Used / limit', accessorKey: 'quantityUsed', cell: (x) => x.row.original.quantityLimit != null ? `${x.getValue()} / ${x.row.original.quantityLimit}` : String(x.getValue()) }, { header: 'Owner', accessorKey: 'owner' },
  ];
  return (
    <Page crumbs={[{ label: 'Deviations' }]} title="Deviations"
      tabs={[{ label: 'In force', value: 'active' }, { label: 'Draft', value: '0' }, { label: 'Pending approval', value: '1' }, { label: 'Approved', value: '2' }, { label: 'Closed', value: '4' }, { label: 'All', value: 'all' }]}
      tab={view} onTab={(v) => setP(v === 'active' ? {} : { view: v })}
      actions={can('qualityops.edit') ? <Button variant="contained" component={RouterLink} to={`${base}/deviations/new`}>New deviation</Button> : undefined}>
      <Filter value={applied} onApply={setApplied} placeholder="Number, title, lots" />
      <DataTable data={list.data?.items ?? []} columns={cols} onRowClick={(r) => nav(`${base}/deviations/${r.id}`)} empty="No deviations in this view." />
    </Page>
  );
}

const deviationSchema = z.object({ kind: z.coerce.number().int(), title: z.string().trim().min(1, 'Required').max(300), requirement: z.string().trim().min(1, 'Required').max(4000), deviationDescription: z.string().trim().min(1, 'Required').max(10000),
  justification: z.string().trim().min(1, 'Required').max(10000), riskAssessment: z.string().max(10000).optional(), containmentActions: z.string().max(10000).optional(), partId: z.string().optional(), productId: z.string().optional(),
  supplierId: z.string().optional(), customerId: z.string().optional(), affectedLots: z.string().max(2000).optional(), quantityLimit: z.union([z.literal(''), z.coerce.number().positive('Must be greater than zero')]).optional(),
  effectiveFrom: z.string().optional(), effectiveTo: z.string().optional(), customerApprovalRequired: z.coerce.number().int(), customerApprovalReference: z.string().max(200).optional(),
  siteId: z.string().uuid('Choose a site'), departmentId: z.string().optional(), ownerId: z.string().optional() });
type DeviationForm = z.output<typeof deviationSchema>;
const deviationFields = [{ name: 'kind', label: 'Type', type: 'select', options: deviationKind.map((k, i) => [i, k]) }, { name: 'title', label: 'Title', full: true },
  { name: 'requirement', label: 'Requirement deviated from', type: 'multiline' }, { name: 'deviationDescription', label: 'Deviation', type: 'multiline' }, { name: 'justification', label: 'Justification', type: 'multiline' },
  { name: 'riskAssessment', label: 'Risk assessment', type: 'multiline' }, { name: 'containmentActions', label: 'Containment actions (unplanned)', type: 'multiline' },
  { name: 'scope', label: 'Scope', type: 'heading' }, { name: 'partId', label: 'Part', type: 'lookup', lookup: 'part' }, { name: 'productId', label: 'Product', type: 'lookup', lookup: 'product' },
  { name: 'supplierId', label: 'Supplier', type: 'lookup', lookup: 'supplier' }, { name: 'customerId', label: 'Customer', type: 'lookup', lookup: 'customer' }, { name: 'affectedLots', label: 'Affected lots' },
  { name: 'quantityLimit', label: 'Quantity limit', type: 'number' }, { name: 'effectiveFrom', label: 'Effective from', type: 'date' }, { name: 'effectiveTo', label: 'Effective to', type: 'date' },
  { name: 'customerApprovalRequired', label: 'Customer approval required', type: 'select', options: [[0, 'No'], [1, 'Yes']] }, { name: 'customerApprovalReference', label: 'Customer approval reference' },
  { name: 'siteId', label: 'Site', type: 'site' }, { name: 'departmentId', label: 'Department (optional)', type: 'department' }, { name: 'ownerId', label: 'Owner (defaults to you)', type: 'user' }] as never;
const deviationBody = (v: DeviationForm) => ({ ...v, riskAssessment: nul(v.riskAssessment), containmentActions: nul(v.containmentActions), partId: nul(v.partId), productId: nul(v.productId), supplierId: nul(v.supplierId), customerId: nul(v.customerId),
  affectedLots: nul(v.affectedLots), quantityLimit: numOrNull(v.quantityLimit), effectiveFrom: nul(v.effectiveFrom), effectiveTo: nul(v.effectiveTo), customerApprovalRequired: v.customerApprovalRequired === 1,
  customerApprovalReference: nul(v.customerApprovalReference), departmentId: nul(v.departmentId), ownerId: nul(v.ownerId) });

export function DeviationNew() {
  const nav = useNavigate();
  return (
    <Page crumbs={[{ label: 'Deviations', to: `${base}/deviations` }, { label: 'New' }]} title="New deviation">
      <SchemaForm schema={deviationSchema} submitLabel="Create draft" onCancel={() => nav(`${base}/deviations`)} fields={deviationFields}
        defaults={{ kind: 0, title: '', requirement: '', deviationDescription: '', justification: '', riskAssessment: '', containmentActions: '', partId: '', productId: '', supplierId: '', customerId: '', affectedLots: '', quantityLimit: '',
          effectiveFrom: localDate(), effectiveTo: localDate(30), customerApprovalRequired: 0, customerApprovalReference: '', siteId: '', departmentId: '', ownerId: '' }}
        onSubmit={async (v) => { const r = await post<{ id: string }>(`${Q}/deviations`, deviationBody(v)); nav(`${base}/deviations/${r.id}`); }} />
    </Page>
  );
}

interface DeviationDto { deviation: { id: string; number: string; kind: number; title: string; requirement: string; deviationDescription: string; justification: string; riskAssessment: string | null; containmentActions: string | null;
  partId: string | null; productId: string | null; supplierId: string | null; customerId: string | null; affectedLots: string | null; quantityLimit: number | null; quantityUsed: number; effectiveFrom: string | null; effectiveTo: string | null;
  customerApprovalRequired: boolean; customerApprovalReference: string | null; status: number; closureNotes: string | null; siteId: string; departmentId: string | null; ownerId: string; rowVersion: number };
  owner: string; inForce: boolean; partNumber: string | null; actions: Record<string, boolean> }

export function DeviationDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const [dlg, setDlg] = useState<string | null>(null);
  const [tab, setTab] = useState('details');
  const q = useQuery({ queryKey: ['deviation', id], queryFn: () => get<DeviationDto>(`${Q}/deviations/${id}`) });
  const refresh = () => qc.invalidateQueries({ queryKey: ['deviation', id] });
  const { error, run } = useRunner(refresh);
  const crumbs = [{ label: 'Deviations', to: `${base}/deviations` }];
  const d = q.data;
  if (!d) return <Loading crumbs={crumbs} error={q.error} />;
  const x = d.deviation; const a = d.actions;
  if (dlg === 'edit') return (
    <Page crumbs={[...crumbs, { label: x.number, to: `${base}/deviations/${x.id}` }, { label: 'Edit' }]} title={`Edit ${x.number}`}>
      <SchemaForm schema={deviationSchema} submitLabel="Save" onCancel={() => setDlg(null)} fields={deviationFields}
        defaults={{ kind: x.kind, title: x.title, requirement: x.requirement, deviationDescription: x.deviationDescription, justification: x.justification, riskAssessment: x.riskAssessment ?? '', containmentActions: x.containmentActions ?? '',
          partId: x.partId ?? '', productId: x.productId ?? '', supplierId: x.supplierId ?? '', customerId: x.customerId ?? '', affectedLots: x.affectedLots ?? '', quantityLimit: x.quantityLimit ?? '', effectiveFrom: x.effectiveFrom ?? '', effectiveTo: x.effectiveTo ?? '',
          customerApprovalRequired: x.customerApprovalRequired ? 1 : 0, customerApprovalReference: x.customerApprovalReference ?? '', siteId: x.siteId, departmentId: x.departmentId ?? '', ownerId: x.ownerId }}
        onSubmit={async (v) => { await put(`${Q}/deviations/${x.id}`, { ...deviationBody(v), rowVersion: x.rowVersion }); setDlg(null); refresh(); }} />
    </Page>);
  return (
    <Page crumbs={[...crumbs, { label: x.number }]} title={<Title number={x.number} title={x.title} status={deviationStatus[x.status]} />}
      tabs={[{ label: 'Details', value: 'details' }, { label: 'Approval', value: 'approval' }, { label: 'Files & history', value: 'more' }]} tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1} flexWrap="wrap">
        {a.edit && <Button variant="outlined" onClick={() => setDlg('edit')}>Edit</Button>}
        {a.submit && <Button variant="contained" onClick={() => run(() => post(`${Q}/deviations/${x.id}/submit`))}>Submit for approval</Button>}
        {a.use && <Button variant="contained" onClick={() => setDlg('use')}>Record use</Button>}
        {a.close && <Button onClick={() => setDlg('close')}>Close</Button>}
        {a.cancel && <Button color="error" onClick={() => setDlg('cancel')}>Cancel</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {x.status === 2 && <Alert severity={d.inForce ? 'success' : 'warning'} sx={{ mb: 2 }}>{d.inForce ? 'This deviation is in force.' : 'Approved but not in force (outside its window or its quantity is used).'}</Alert>}
      {x.status === 1 && <Alert severity="info" sx={{ mb: 2 }}>Waiting for approval (see Approvals in My work).</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: '290px minmax(0, 1fr)' }, gap: 3 }}>
        <Facts items={[['Type', deviationKind[x.kind]], ['Owner', d.owner], ['Part', d.partNumber ?? '—'], ['Affected lots', x.affectedLots ?? '—'], ['Window', x.effectiveFrom ? `${fmtDate(x.effectiveFrom)} – ${fmtDate(x.effectiveTo)}` : '—'],
          ['Quantity used', x.quantityLimit != null ? `${x.quantityUsed} of ${x.quantityLimit}` : String(x.quantityUsed)], ['Customer approval', x.customerApprovalRequired ? (x.customerApprovalReference ?? 'Required, not recorded') : 'Not required']]} />
        <Box sx={{ minWidth: 0 }}>
          {tab === 'details' && <Section title="Deviation"><Prose label="Requirement" text={x.requirement} /><Prose label="Deviation" text={x.deviationDescription} /><Prose label="Justification" text={x.justification} />
            <Prose label="Risk assessment" text={x.riskAssessment} /><Prose label="Containment actions" text={x.containmentActions} /><Prose label="Closure" text={x.closureNotes} /></Section>}
          {tab === 'approval' && <ApprovalsPanel type="Deviation" id={x.id} />}
          {tab === 'more' && <AttachmentsAndHistory type="Deviation" id={x.id} canEdit={a.edit || a.use} />}
        </Box>
      </Box>
      <StepDialog open={dlg === 'use'} title="Record use" confirm="Record" onClose={() => setDlg(null)} inputs={[{ name: 'quantity', label: 'Quantity' }, { name: 'lot', label: 'Lot' }]}
        onConfirm={async (v) => { await post(`${Q}/deviations/${x.id}/use`, { quantity: Number(v.quantity), lot: nul(v.lot) }); refresh(); }} />
      <StepDialog open={dlg === 'close'} title="Close deviation" confirm="Close" onClose={() => setDlg(null)} inputs={[{ name: 'notes', label: 'Closure notes', type: 'multiline' }]}
        onConfirm={async (v) => { await post(`${Q}/deviations/${x.id}/close`, { notes: v.notes }); refresh(); }} />
      <StepDialog open={dlg === 'cancel'} title="Cancel deviation" confirm="Cancel deviation" danger onClose={() => setDlg(null)} inputs={[{ name: 'reason', label: 'Reason', type: 'multiline' }]}
        onConfirm={async (v) => { await post(`${Q}/deviations/${x.id}/cancel`, { reason: v.reason }); refresh(); }} />
    </Page>
  );
}

// ---------------------------------------------------------------- investigations
export const investigationStatus = ['Open', 'InProgress', 'Concluded', 'Closed', 'Cancelled'];
export const investigationKind = ['Cross-module', 'Laboratory', 'Escalation'];
interface InvRow { id: string; number: string; kind: number; title: string; status: number; escalationLevel: number; dueOn: string | null; outOfSpecification: boolean | null; lead: string }

export function InvestigationList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const view = p.get('view') ?? 'all';
  const [applied, setApplied] = useState('');
  const list = useQuery({ queryKey: ['investigations', view, applied], queryFn: () => get<{ items: InvRow[] }>(`${Q}/investigations?pageSize=300${view === 'all' ? '' : `&status=${view}`}${applied ? `&q=${encodeURIComponent(applied)}` : ''}`) });
  const today = localDate();
  const cols: ColumnDef<InvRow, any>[] = [
    { header: 'Number', accessorKey: 'number', cell: num }, { header: 'Type', accessorKey: 'kind', cell: (x) => investigationKind[x.getValue()] }, { header: 'Title', accessorKey: 'title' },
    { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={investigationStatus[x.getValue()]} /> }, { header: 'Level', accessorKey: 'escalationLevel' },
    { header: 'Result', accessorKey: 'outOfSpecification', cell: (x) => x.getValue() == null ? '—' : x.getValue() ? <StatusChip status="Out of specification" /> : 'Within specification' },
    { header: 'Due', accessorKey: 'dueOn', cell: (x) => <Box component="span" sx={{ color: x.getValue() && x.getValue() < today && x.row.original.status < 2 ? 'error.main' : undefined }}>{fmtDate(x.getValue())}</Box> }, { header: 'Lead', accessorKey: 'lead' },
  ];
  return (
    <Page crumbs={[{ label: 'Investigations' }]} title="Investigations"
      tabs={[{ label: 'All', value: 'all' }, { label: 'Open', value: '0' }, { label: 'In progress', value: '1' }, { label: 'Concluded', value: '2' }, { label: 'Closed', value: '3' }]} tab={view} onTab={(v) => setP(v === 'all' ? {} : { view: v })}
      actions={can('qualityops.edit') ? <Button variant="contained" component={RouterLink} to={`${base}/investigations/new`}>New investigation</Button> : undefined}>
      <Filter value={applied} onApply={setApplied} placeholder="Number, title, sample" />
      <DataTable data={list.data?.items ?? []} columns={cols} onRowClick={(r) => nav(`${base}/investigations/${r.id}`)} empty="No investigations in this view." />
    </Page>
  );
}

export function InvestigationNew() {
  const nav = useNavigate();
  const schema = z.object({ kind: z.coerce.number().int(), title: z.string().trim().min(1, 'Required').max(300), scope: z.string().trim().min(1, 'Required').max(10000), leadId: z.string().optional(), dueOn: z.string().optional(),
    siteId: z.string().uuid('Choose a site'), departmentId: z.string().optional() });
  return (
    <Page crumbs={[{ label: 'Investigations', to: `${base}/investigations` }, { label: 'New' }]} title="New investigation">
      <SchemaForm schema={schema} submitLabel="Open investigation" onCancel={() => nav(`${base}/investigations`)} defaults={{ kind: 0, title: '', scope: '', leadId: '', dueOn: localDate(30), siteId: '', departmentId: '' }}
        fields={[{ name: 'kind', label: 'Type', type: 'select', options: investigationKind.map((k, i) => [i, k] as [number, string]) }, { name: 'title', label: 'Title', full: true }, { name: 'scope', label: 'Scope', type: 'multiline' },
          { name: 'leadId', label: 'Lead (defaults to you)', type: 'user' }, { name: 'dueOn', label: 'Due', type: 'date' }, { name: 'siteId', label: 'Site', type: 'site' }, { name: 'departmentId', label: 'Department (optional)', type: 'department' }]}
        onSubmit={async (v) => { const r = await post<{ id: string }>(`${Q}/investigations`, { ...v, leadId: nul(v.leadId), dueOn: nul(v.dueOn), departmentId: nul(v.departmentId) }); nav(`${base}/investigations/${r.id}`); }} />
    </Page>
  );
}

interface InvDto { investigation: { id: string; number: string; kind: number; title: string; scope: string; status: number; escalationLevel: number; dueOn: string | null; hypotheses: string | null; findings: string | null; rootCause: string | null;
  conclusion: string | null; sampleReference: string | null; testMethod: string | null; specification: string | null; lowerLimit: number | null; upperLimit: number | null; result: number | null; resultUnit: string | null; outOfSpecification: boolean | null;
  laboratoryErrorFound: boolean | null; rowVersion: number };
  lead: string; linked: { type: string; id: string; number: string; title: string; status: string; url: string; linkType: string }[]; actions: Record<string, boolean> }

export function InvestigationDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const [dlg, setDlg] = useState<string | null>(null);
  const q = useQuery({ queryKey: ['investigation', id], queryFn: () => get<InvDto>(`${Q}/investigations/${id}`) });
  const refresh = () => qc.invalidateQueries({ queryKey: ['investigation', id] });
  const crumbs = [{ label: 'Investigations', to: `${base}/investigations` }];
  const d = q.data;
  if (!d) return <Loading crumbs={crumbs} error={q.error} />;
  const x = d.investigation; const a = d.actions; const lab = x.kind === 1;
  const s = (v: unknown) => (v == null ? '' : String(v));
  return (
    <Page crumbs={[...crumbs, { label: x.number }]} title={<Title number={x.number} title={x.title} status={investigationStatus[x.status]} />}
      actions={<Stack direction="row" gap={1} flexWrap="wrap">
        {a.work && <Button variant="contained" onClick={() => setDlg('work')}>Record work</Button>}
        {a.link && <Button variant="outlined" onClick={() => setDlg('link')}>Link record</Button>}
        {a.escalate && <Button variant="outlined" color="warning" onClick={() => setDlg('escalate')}>Escalate</Button>}
        {a.conclude && <Button onClick={() => setDlg('conclude')}>Conclude</Button>}
        {a.close && <Button variant="contained" onClick={() => setDlg('close')}>Close</Button>}
        {a.cancel && <Button color="error" onClick={() => setDlg('cancel')}>Cancel</Button>}
      </Stack>}>
      {x.outOfSpecification && <Alert severity="error" sx={{ mb: 2 }}>Out of specification: {x.result} {x.resultUnit ?? ''} against {x.lowerLimit ?? '—'} to {x.upperLimit ?? '—'}.</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: '290px minmax(0, 1fr)' }, gap: 3 }}>
        <Facts items={[['Type', investigationKind[x.kind]], ['Lead', d.lead], ['Due', fmtDate(x.dueOn)], ['Escalation level', String(x.escalationLevel)],
          ...(lab ? [['Sample', x.sampleReference ?? '—'], ['Method', x.testMethod ?? '—'], ['Specification', x.specification ?? '—'], ['Limits', `${x.lowerLimit ?? '—'} to ${x.upperLimit ?? '—'}`], ['Result', x.result != null ? `${x.result} ${x.resultUnit ?? ''}` : '—'],
            ['Laboratory error', x.laboratoryErrorFound == null ? '—' : x.laboratoryErrorFound ? 'Yes' : 'No']] as [string, string][] : [])]} />
        <Stack gap={2} sx={{ minWidth: 0 }}>
          <Section title="Investigation"><Prose label="Scope" text={x.scope} /><Prose label="Hypotheses" text={x.hypotheses} /><Prose label="Findings" text={x.findings} /><Prose label="Root cause" text={x.rootCause} /><Prose label="Conclusion" text={x.conclusion} /></Section>
          <Section title={`Linked records (${d.linked.length})`}>{d.linked.length === 0 ? <Typography color="text.secondary" fontSize={14}>Link NCRs, CAPAs, complaints, events or any other record.</Typography> :
            <Table size="small"><TableHead><TableRow><TableCell>Record</TableCell><TableCell>Title</TableCell><TableCell>Status</TableCell><TableCell>Link</TableCell></TableRow></TableHead>
              <TableBody>{d.linked.map((l) => <TableRow key={l.type + l.id}><TableCell><RouterLink to={l.url}>{l.number}</RouterLink></TableCell><TableCell>{l.title}</TableCell><TableCell>{l.status}</TableCell><TableCell>{l.linkType}</TableCell></TableRow>)}</TableBody></Table>}</Section>
          <AttachmentsAndHistory type="QualityInvestigation" id={x.id} canEdit={a.link} />
        </Stack>
      </Box>
      <StepDialog open={dlg === 'work'} title="Record investigation work" confirm="Save" onClose={() => setDlg(null)}
        inputs={[{ name: 'hypotheses', label: 'Hypotheses', type: 'multiline', initial: s(x.hypotheses) }, { name: 'findings', label: 'Findings', type: 'multiline', initial: s(x.findings) }, { name: 'rootCause', label: 'Root cause', type: 'multiline', initial: s(x.rootCause) },
          ...(lab ? [{ name: 'sampleReference', label: 'Sample reference', initial: s(x.sampleReference) }, { name: 'testMethod', label: 'Test method', initial: s(x.testMethod) }, { name: 'specification', label: 'Specification', initial: s(x.specification) },
            { name: 'lowerLimit', label: 'Lower limit', initial: s(x.lowerLimit) }, { name: 'upperLimit', label: 'Upper limit', initial: s(x.upperLimit) }, { name: 'result', label: 'Result', initial: s(x.result) }, { name: 'resultUnit', label: 'Unit', initial: s(x.resultUnit) },
            { name: 'laboratoryErrorFound', label: 'Laboratory error found', type: 'select' as const, options: [['', 'Not yet assessed'], ['true', 'Yes'], ['false', 'No']] as [string, string][], initial: x.laboratoryErrorFound == null ? '' : String(x.laboratoryErrorFound) }] : []),
          { name: 'dueOn', label: 'Due', type: 'date', initial: s(x.dueOn) }]}
        onConfirm={async (v) => {
          await put(`${Q}/investigations/${x.id}/work`, { hypotheses: nul(v.hypotheses), findings: nul(v.findings), rootCause: nul(v.rootCause), sampleReference: nul(v.sampleReference), testMethod: nul(v.testMethod), specification: nul(v.specification),
            lowerLimit: numOrNull(v.lowerLimit), upperLimit: numOrNull(v.upperLimit), result: numOrNull(v.result), resultUnit: nul(v.resultUnit), laboratoryErrorFound: v.laboratoryErrorFound ? v.laboratoryErrorFound === 'true' : null, dueOn: nul(v.dueOn), rowVersion: x.rowVersion });
          refresh();
        }} />
      <LinkDialog open={dlg === 'link'} onClose={() => setDlg(null)} onLink={async (r) => { await post(`${Q}/investigations/${x.id}/links`, { recordType: r.type, recordId: r.id }); refresh(); }} />
      <StepDialog open={dlg === 'escalate'} title="Escalate investigation" intro="Raises the level and notifies the approvers at the site." confirm="Escalate" onClose={() => setDlg(null)} inputs={[{ name: 'reason', label: 'Reason', type: 'multiline' }]}
        onConfirm={async (v) => { await post(`${Q}/investigations/${x.id}/escalate`, { reason: v.reason }); refresh(); }} />
      <StepDialog open={dlg === 'conclude'} title="Conclude investigation" confirm="Conclude" onClose={() => setDlg(null)} inputs={[{ name: 'notes', label: 'Conclusion', type: 'multiline' }]}
        onConfirm={async (v) => { await post(`${Q}/investigations/${x.id}/conclude`, { notes: v.notes }); refresh(); }} />
      <StepDialog open={dlg === 'close'} title="Close investigation" intro="Closing confirms the conclusion." confirm="Close" onClose={() => setDlg(null)} inputs={[]}
        onConfirm={async () => { await post(`${Q}/investigations/${x.id}/close`); refresh(); }} />
      <StepDialog open={dlg === 'cancel'} title="Cancel investigation" confirm="Cancel investigation" danger onClose={() => setDlg(null)} inputs={[{ name: 'reason', label: 'Reason', type: 'multiline' }]}
        onConfirm={async (v) => { await post(`${Q}/investigations/${x.id}/cancel`, { reason: v.reason }); refresh(); }} />
    </Page>
  );
}

function LinkDialog({ open, onClose, onLink }: { open: boolean; onClose: () => void; onLink: (r: PickedRecord) => Promise<void> }) {
  const [rec, setRec] = useState<PickedRecord | null>(null);
  const [err, setErr] = useState<string | null>(null);
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm" TransitionProps={{ onEnter: () => { setRec(null); setErr(null); } }}>
      <DialogTitle>Link a record</DialogTitle>
      <DialogContent>{err && <Alert severity="error" sx={{ mb: 2 }}>{err}</Alert>}<Box sx={{ pt: 1 }}><RecordPicker label="Find a record by number or title" onChange={setRec} /></Box></DialogContent>
      <DialogActions><Button onClick={onClose}>Cancel</Button><Button variant="contained" disabled={!rec} onClick={async () => { try { await onLink(rec!); onClose(); } catch (e) { setErr((e as Error).message); } }}>Link</Button></DialogActions>
    </Dialog>
  );
}

// ---------------------------------------------------------------- release
export const releaseStatus = ['Pending', 'Released', 'Rejected', 'Cancelled'];
export const releaseKind = ['Product', 'Batch'];
interface Check { key: string; label: string; passed: boolean; detail: string }

export function ReleaseBoard() {
  const nav = useNavigate();
  const { can } = useAuth();
  const q = useQuery({ queryKey: ['release-board'], queryFn: () => get<{ holds: { id: string; number: string; reason: string; partNumber: string | null; lotNumber: string | null; createdAt: string }[];
    releases: { id: string; number: string; kind: number; lotNumber: string | null; quantity: number | null; partNumber: string | null; ready: boolean; blockers: string[] }[] }>(`${Q}/releases/board`) });
  return (
    <Page crumbs={[{ label: 'Hold and release board' }]} title="Quality hold and release board"
      actions={<Stack direction="row" gap={1}><Button variant="outlined" component={RouterLink} to={`${base}/releases/all`}>All decisions</Button>{can('qualityops.edit') && <Button variant="contained" component={RouterLink} to={`${base}/releases/new`}>Request release</Button>}</Stack>}>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: '1fr 1fr' }, gap: 2 }}>
        <Section title={`Waiting for release (${q.data?.releases.length ?? 0})`}>
          {(q.data?.releases ?? []).length === 0 ? <Typography color="text.secondary" fontSize={14}>Nothing waiting.</Typography> : <Stack gap={1}>{q.data!.releases.map((r) =>
            <Paper key={r.id} variant="outlined" sx={{ p: 1.5, cursor: 'pointer', borderLeft: 4, borderLeftColor: r.ready ? 'success.main' : 'error.main' }} onClick={() => nav(`${base}/releases/${r.id}`)}>
              <Stack direction="row" justifyContent="space-between"><span style={{ fontFamily: mono }}>{r.number}</span><StatusChip status={r.ready ? 'Ready' : 'Blocked'} /></Stack>
              <Typography fontSize={14}>{releaseKind[r.kind]} {[r.partNumber, r.lotNumber].filter(Boolean).join(' / ')}{r.quantity != null ? ` · ${r.quantity}` : ''}</Typography>
              {!r.ready && <Typography fontSize={12.5} color="error.main">{r.blockers.join(' · ')}</Typography>}
            </Paper>)}</Stack>}
        </Section>
        <Section title={`Active holds (${q.data?.holds.length ?? 0})`}>
          {(q.data?.holds ?? []).length === 0 ? <Typography color="text.secondary" fontSize={14}>No active holds you can see.</Typography> :
            <Table size="small"><TableHead><TableRow><TableCell>Hold</TableCell><TableCell>Part / lot</TableCell><TableCell>Reason</TableCell><TableCell>Since</TableCell></TableRow></TableHead>
              <TableBody>{q.data!.holds.map((h) => <TableRow key={h.id}><TableCell sx={{ fontFamily: mono }}>{h.number}</TableCell><TableCell>{[h.partNumber, h.lotNumber].filter(Boolean).join(' / ') || '—'}</TableCell><TableCell>{h.reason}</TableCell><TableCell>{fmtDate(h.createdAt)}</TableCell></TableRow>)}</TableBody></Table>}
        </Section>
      </Box>
    </Page>
  );
}

interface ReleaseRow { id: string; number: string; kind: number; status: number; lotNumber: string | null; quantity: number | null; decidedByName: string | null; decidedAt: string | null; partNumber: string | null }
export function ReleaseList() {
  const nav = useNavigate();
  const list = useQuery({ queryKey: ['releases'], queryFn: () => get<{ items: ReleaseRow[] }>(`${Q}/releases?pageSize=300`) });
  const cols: ColumnDef<ReleaseRow, any>[] = [{ header: 'Number', accessorKey: 'number', cell: num }, { header: 'Type', accessorKey: 'kind', cell: (x) => releaseKind[x.getValue()] }, { header: 'Part', accessorKey: 'partNumber', cell: (x) => x.getValue() ?? '—' },
    { header: 'Lot', accessorKey: 'lotNumber', cell: (x) => x.getValue() ?? '—' }, { header: 'Quantity', accessorKey: 'quantity', cell: (x) => x.getValue() ?? '—' }, { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={releaseStatus[x.getValue()]} /> },
    { header: 'Decided by', accessorKey: 'decidedByName', cell: (x) => x.getValue() ?? '—' }, { header: 'Decided', accessorKey: 'decidedAt', cell: (x) => fmtDateTime(x.getValue()) }];
  return <Page crumbs={[{ label: 'Release', to: `${base}/releases` }, { label: 'All decisions' }]} title="Release decisions">
    <DataTable data={list.data?.items ?? []} columns={cols} onRowClick={(r) => nav(`${base}/releases/${r.id}`)} empty="No release decisions yet." /></Page>;
}

export function ReleaseNew() {
  const nav = useNavigate();
  const schema = z.object({ kind: z.coerce.number().int(), partId: z.string().optional(), productId: z.string().optional(), lotNumber: z.string().max(100).optional(), quantity: z.union([z.literal(''), z.coerce.number().positive()]).optional(),
    reviewNotes: z.string().max(4000).optional(), siteId: z.string().uuid('Choose a site'), departmentId: z.string().optional() });
  return (
    <Page crumbs={[{ label: 'Release', to: `${base}/releases` }, { label: 'Request' }]} title="Request a release decision">
      <SchemaForm schema={schema} submitLabel="Request release" onCancel={() => nav(`${base}/releases`)} defaults={{ kind: 1, partId: '', productId: '', lotNumber: '', quantity: '', reviewNotes: '', siteId: '', departmentId: '' }}
        fields={[{ name: 'kind', label: 'Type', type: 'select', options: releaseKind.map((k, i) => [i, k] as [number, string]) }, { name: 'partId', label: 'Part', type: 'lookup', lookup: 'part' }, { name: 'productId', label: 'Product', type: 'lookup', lookup: 'product' },
          { name: 'lotNumber', label: 'Lot or batch' }, { name: 'quantity', label: 'Quantity', type: 'number' }, { name: 'reviewNotes', label: 'Review notes', type: 'multiline' }, { name: 'siteId', label: 'Site', type: 'site' }, { name: 'departmentId', label: 'Department (optional)', type: 'department' }]}
        onSubmit={async (v) => { const r = await post<{ id: string }>(`${Q}/releases`, { ...v, partId: nul(v.partId), productId: nul(v.productId), lotNumber: nul(v.lotNumber), quantity: numOrNull(v.quantity), reviewNotes: nul(v.reviewNotes), departmentId: nul(v.departmentId) }); nav(`${base}/releases/${r.id}`); }} />
    </Page>
  );
}

interface ReleaseDto { release: { id: string; number: string; kind: number; status: number; lotNumber: string | null; quantity: number | null; reviewNotes: string | null; decidedByName: string | null; decidedAt: string | null; decisionMeaning: string | null; decisionComment: string | null; signatureHash: string | null };
  requestedBy: string; checks: Check[]; signatureValid: boolean | null; partNumber: string | null; actions: Record<string, boolean> }
export function ReleaseDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const [dlg, setDlg] = useState<string | null>(null);
  const q = useQuery({ queryKey: ['release', id], queryFn: () => get<ReleaseDto>(`${Q}/releases/${id}`) });
  const refresh = () => { qc.invalidateQueries({ queryKey: ['release', id] }); qc.invalidateQueries({ queryKey: ['release-board'] }); };
  const crumbs = [{ label: 'Release', to: `${base}/releases` }];
  const d = q.data;
  if (!d) return <Loading crumbs={crumbs} error={q.error} />;
  const x = d.release; const a = d.actions;
  return (
    <Page crumbs={[...crumbs, { label: x.number }]} title={<Title number={x.number} title={`${releaseKind[x.kind]} release ${[d.partNumber, x.lotNumber].filter(Boolean).join(' / ')}`} status={releaseStatus[x.status]} />}
      actions={<Stack direction="row" gap={1}>
        {a.release && <Button variant="contained" onClick={() => setDlg('release')}>Release</Button>}
        {a.reject && <Button color="error" variant="outlined" onClick={() => setDlg('reject')}>Reject</Button>}
        {a.cancel && <Button onClick={() => setDlg('cancel')}>Cancel request</Button>}
      </Stack>}>
      {x.status === 0 && d.checks.some((c) => !c.passed) && <Alert severity="error" sx={{ mb: 2 }}>Release is blocked until every check passes.</Alert>}
      {x.signatureHash && <Alert severity={d.signatureValid ? 'success' : 'error'} sx={{ mb: 2 }}>{x.decisionMeaning} by {x.decidedByName} on {fmtDateTime(x.decidedAt)}. Signature {d.signatureValid ? 'verified' : 'does not match the record'}.</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: '290px minmax(0, 1fr)' }, gap: 3 }}>
        <Facts items={[['Type', releaseKind[x.kind]], ['Part', d.partNumber ?? '—'], ['Lot', x.lotNumber ?? '—'], ['Quantity', x.quantity != null ? String(x.quantity) : '—'], ['Requested by', d.requestedBy], ['Decision comment', x.decisionComment ?? '—']]} />
        <Stack gap={2} sx={{ minWidth: 0 }}>
          <Section title={x.status === 0 ? 'Release checks (evaluated now)' : 'Release checks (at decision)'}>
            <Table size="small"><TableBody>{d.checks.map((c) => <TableRow key={c.key}><TableCell sx={{ width: 32 }}><Box sx={{ width: 12, height: 12, borderRadius: 6, bgcolor: c.passed ? 'success.main' : 'error.main' }} /></TableCell><TableCell>{c.label}</TableCell><TableCell>{c.detail}</TableCell></TableRow>)}</TableBody></Table>
          </Section>
          <Section title="Review notes"><Prose label="Notes" text={x.reviewNotes} /></Section>
          <AttachmentsAndHistory type="ReleaseDecision" id={x.id} canEdit={a.cancel} />
        </Stack>
      </Box>
      {(['release', 'reject'] as const).map((k) => <StepDialog key={k} open={dlg === k} title={k === 'release' ? 'Release with signature' : 'Reject release'} danger={k === 'reject'} confirm={k === 'release' ? 'Sign and release' : 'Sign and reject'}
        intro="Your password is your electronic signature for this decision." onClose={() => setDlg(null)}
        inputs={[{ name: 'password', label: 'Password', type: 'password' }, { name: 'mfaCode', label: 'Authenticator code (if asked)' }, { name: 'comment', label: k === 'reject' ? 'Reason' : 'Comment', type: 'multiline' }]}
        onConfirm={async (v) => { await post(`${Q}/releases/${x.id}/decision`, { release: k === 'release', password: v.password, mfaCode: nul(v.mfaCode), comment: nul(v.comment) }); refresh(); }} />)}
      <StepDialog open={dlg === 'cancel'} title="Cancel release request" confirm="Cancel request" danger onClose={() => setDlg(null)} inputs={[{ name: 'reason', label: 'Reason', type: 'multiline' }]}
        onConfirm={async (v) => { await post(`${Q}/releases/${x.id}/cancel`, { reason: v.reason }); refresh(); }} />
    </Page>
  );
}

// ---------------------------------------------------------------- delegations
interface Delegation { id: string; delegatorId: string; delegator: string; delegateId: string; delegate: string; startsAt: string; endsAt: string; recordType: string | null; reason: string; revoked: boolean; active: boolean }
export function DelegationsPage() {
  const qc = useQueryClient();
  const { can, me } = useAuth();
  const [all, setAll] = useState(false);
  const [open, setOpen] = useState(false);
  const q = useQuery({ queryKey: ['delegations', all], queryFn: () => get<Delegation[]>(`${Q}/delegations?all=${all}`) });
  const { error, run } = useRunner(() => qc.invalidateQueries({ queryKey: ['delegations'] }));
  const admin = can('admin.users');
  const types: [string, string][] = [['', 'All approvals'], ['Deviation', 'Deviations'], ['QualityReview', 'Quality reviews'], ['Document', 'Documents'], ['Ncr', 'Nonconformances'], ['Capa', 'CAPAs'], ['ChangeRequest', 'Changes']];
  return (
    <Page crumbs={[{ label: 'Approval delegation' }]} title="Approval delegation"
      actions={<Stack direction="row" gap={1}>{admin && <FormControlLabel control={<Checkbox checked={all} onChange={(e) => setAll(e.target.checked)} />} label="Everyone" />}<Button variant="contained" onClick={() => setOpen(true)}>Delegate approvals</Button></Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <Alert severity="info" sx={{ mb: 2 }}>While a delegation is active the delegate can sign the approval steps the delegator could sign. Each signature records that it was made on the delegator's behalf.</Alert>
      <Table size="small"><TableHead><TableRow><TableCell>Delegator</TableCell><TableCell>Delegate</TableCell><TableCell>Records</TableCell><TableCell>From</TableCell><TableCell>Until</TableCell><TableCell>Reason</TableCell><TableCell>Status</TableCell><TableCell /></TableRow></TableHead>
        <TableBody>{(q.data ?? []).map((d) => <TableRow key={d.id}><TableCell>{d.delegator}</TableCell><TableCell>{d.delegate}</TableCell><TableCell>{types.find((t) => t[0] === (d.recordType ?? ''))?.[1] ?? d.recordType}</TableCell><TableCell>{fmtDateTime(d.startsAt)}</TableCell><TableCell>{fmtDateTime(d.endsAt)}</TableCell>
          <TableCell>{d.reason}</TableCell><TableCell><StatusChip status={d.revoked ? 'Revoked' : d.active ? 'Active' : 'Inactive'} /></TableCell>
          <TableCell>{!d.revoked && (d.delegatorId === me?.id || admin) && <Button size="small" onClick={() => run(() => post(`${Q}/delegations/${d.id}/revoke`))}>Revoke</Button>}</TableCell></TableRow>)}
          {(q.data ?? []).length === 0 && <TableRow><TableCell colSpan={8} sx={{ color: 'text.secondary' }}>No delegations.</TableCell></TableRow>}</TableBody></Table>
      <StepDialog open={open} title="Delegate approvals" confirm="Delegate" onClose={() => setOpen(false)}
        inputs={[{ name: 'delegateId', label: 'Delegate', type: 'users', helper: 'Choose one person' }, { name: 'startsAt', label: 'From', type: 'date', initial: localDate() }, { name: 'endsAt', label: 'Until', type: 'date', initial: localDate(14) },
          { name: 'recordType', label: 'Records', type: 'select', options: types, initial: '' }, { name: 'reason', label: 'Reason', type: 'multiline' }]}
        onConfirm={async (v) => {
          const ids = v.delegateId.split(',').filter(Boolean);
          if (ids.length !== 1) throw new Error('Choose one delegate.');
          const start = v.startsAt === localDate() ? new Date() : new Date(v.startsAt + 'T00:00:00');
          await post(`${Q}/delegations`, { delegateId: ids[0], startsAt: start.toISOString(), endsAt: new Date(v.endsAt + 'T23:59:59').toISOString(), recordType: nul(v.recordType), reason: v.reason });
          qc.invalidateQueries({ queryKey: ['delegations'] });
        }} />
    </Page>
  );
}

// ---------------------------------------------------------------- registers
interface RegField { key: string; label: string; type: string; required: boolean; options: string[] | null }
interface RegDef { key: string; name: string; description: string; fields: RegField[]; statuses: string[]; transitions: Record<string, string[]>; closedStatuses: string[]; usesSupplier: boolean; usesCustomer: boolean; usesPart: boolean; count: number }
const useRegisters = () => useQuery({ queryKey: ['registers'], queryFn: () => get<RegDef[]>(`${Q}/registers`) });

export function RegistersHome() {
  const r = useRegisters();
  return (
    <Page crumbs={[{ label: 'Registers' }]} title="Quality registers">
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(300px, 1fr))', gap: 2 }}>
        {(r.data ?? []).map((d) => <Paper key={d.key} variant="outlined" component={RouterLink} to={`${base}/registers/${d.key}`} sx={{ p: 2, textDecoration: 'none', color: 'inherit', '&:hover': { borderColor: 'primary.main' } }}>
          <Stack direction="row" justifyContent="space-between"><Typography fontWeight={600}>{d.name}</Typography><Typography sx={{ fontFamily: mono }}>{d.count}</Typography></Stack>
          <Typography fontSize={13.5} color="text.secondary">{d.description}</Typography></Paper>)}
      </Box>
    </Page>
  );
}

interface EntryRow { id: string; number: string; title: string; status: string; effectiveOn: string | null; reviewDueOn: string | null; expiresOn: string | null; owner: string; supplier: string | null; customer: string | null; partNumber: string | null }
export function RegisterList() {
  const { key } = useParams();
  const nav = useNavigate();
  const { can } = useAuth();
  const defs = useRegisters();
  const [p, setP] = useSearchParams();
  const view = p.get('view') ?? 'current';
  const [applied, setApplied] = useState('');
  const def = defs.data?.find((d) => d.key === key);
  const filter = view === 'expiring' ? '&expiring=true' : view === 'archived' ? '&archived=true' : '';
  const list = useQuery({ queryKey: ['register', key, view, applied], queryFn: () => get<{ items: EntryRow[] }>(`${Q}/registers/${key}?pageSize=300${filter}${applied ? `&q=${encodeURIComponent(applied)}` : ''}`), enabled: !!def });
  const crumbs = [{ label: 'Registers', to: `${base}/registers` }];
  if (!def) return <Loading crumbs={crumbs} error={defs.error ?? (defs.data ? new Error('This register is not available.') : null)} />;
  const today = localDate();
  const opt = (header: string, accessorKey: keyof EntryRow): ColumnDef<EntryRow, any> => ({ header, accessorKey, cell: (x) => x.getValue() ?? '—' });
  const due = (header: string, accessorKey: keyof EntryRow): ColumnDef<EntryRow, any> => ({ header, accessorKey, cell: (x) => <Box component="span" sx={{ color: x.getValue() && x.getValue() < today ? 'error.main' : undefined }}>{fmtDate(x.getValue())}</Box> });
  const cols: ColumnDef<EntryRow, any>[] = [{ header: 'Number', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' },
    ...(def.usesSupplier ? [opt('Supplier', 'supplier')] : []), ...(def.usesCustomer ? [opt('Customer', 'customer')] : []), ...(def.usesPart ? [opt('Part', 'partNumber')] : []),
    { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={x.getValue()} /> }, due('Review due', 'reviewDueOn'), due('Expires', 'expiresOn'), { header: 'Owner', accessorKey: 'owner' }];
  return (
    <Page crumbs={[...crumbs, { label: def.name }]} title={def.name} tabs={[{ label: 'Current', value: 'current' }, { label: 'Due or expiring in 30 days', value: 'expiring' }, { label: 'Archived', value: 'archived' }]} tab={view} onTab={(v) => setP(v === 'current' ? {} : { view: v })}
      actions={can('qualityops.edit') ? <Button variant="contained" component={RouterLink} to={`${base}/registers/${key}/new`}>Add entry</Button> : undefined}>
      <Typography color="text.secondary" fontSize={14} sx={{ mb: 2 }}>{def.description}</Typography>
      <Filter value={applied} onApply={setApplied} placeholder="Number, title, summary" />
      <DataTable data={list.data?.items ?? []} columns={cols} onRowClick={(r) => nav(`${base}/registers/entries/${r.id}`)} empty="No entries in this view." />
    </Page>
  );
}

function DetailField({ f, value, onChange }: { f: RegField; value: unknown; onChange: (v: unknown) => void }) {
  const label = f.label + (f.required ? ' *' : '');
  if (f.type === 'checkbox') return <FormControlLabel control={<Checkbox checked={value === true} onChange={(e) => onChange(e.target.checked)} />} label={f.label} />;
  if (f.type === 'choice') return <TextField select label={label} value={(value as string) ?? ''} onChange={(e) => onChange(e.target.value || null)}><MenuItem value="">—</MenuItem>{(f.options ?? []).map((o) => <MenuItem key={o} value={o}>{o}</MenuItem>)}</TextField>;
  if (f.type === 'number') return <TextField type="number" label={label} value={(value as number | null) ?? ''} onChange={(e) => onChange(e.target.value === '' ? null : Number(e.target.value))} />;
  if (f.type === 'date') return <TextField type="date" label={label} value={(value as string) ?? ''} slotProps={{ inputLabel: { shrink: true } }} onChange={(e) => onChange(e.target.value || null)} />;
  return <TextField label={label} multiline={f.type === 'textarea'} minRows={f.type === 'textarea' ? 3 : undefined} sx={f.type === 'textarea' ? { gridColumn: '1 / -1' } : undefined} value={(value as string) ?? ''} onChange={(e) => onChange(e.target.value || null)} />;
}

interface EntryForm { siteId: string; departmentId: string; title: string; summary: string; supplierId: string; customerId: string; partId: string; ownerId: string; effectiveOn: string; reviewDueOn: string; expiresOn: string; details: Record<string, unknown> }
function LookupSelect({ kind, label, value, onChange }: { kind: 'supplier' | 'customer' | 'part'; label: string; value: string; onChange: (v: string) => void }) {
  const q = useLookup(kind);
  return <TextField select label={label} value={value} onChange={(e) => onChange(e.target.value)}><MenuItem value="">None</MenuItem>
    {(q.data ?? []).map((r) => <MenuItem key={r.id} value={r.id}>{r.partNumber ?? r.code ?? r.number ?? ''} — {r.name ?? r.legalName ?? ''}</MenuItem>)}{setupOptions(kind, q.data)}</TextField>;
}
function EntryEditor({ def, initial, submitLabel, onSubmit, onCancel }: { def: RegDef; initial: EntryForm; submitLabel: string; onSubmit: (v: EntryForm) => Promise<void>; onCancel: () => void }) {
  const [v, setV] = useState<EntryForm>(initial);
  const [err, setErr] = useState<string | null>(null);
  const users = useUsers();
  const set = (k: keyof EntryForm, x: string) => setV({ ...v, [k]: x });
  return (
    <Paper sx={{ p: 3, maxWidth: 820, display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: '1fr 1fr' }, gap: 2 }}>
      {err && <Alert severity="error" sx={{ gridColumn: '1 / -1' }}>{err}</Alert>}
      <TextField label="Title *" value={v.title} onChange={(e) => set('title', e.target.value)} sx={{ gridColumn: '1 / -1' }} />
      <TextField label="Summary" multiline minRows={2} value={v.summary} onChange={(e) => set('summary', e.target.value)} sx={{ gridColumn: '1 / -1' }} />
      {def.usesSupplier && <LookupSelect kind="supplier" label="Supplier" value={v.supplierId} onChange={(x) => set('supplierId', x)} />}
      {def.usesCustomer && <LookupSelect kind="customer" label="Customer" value={v.customerId} onChange={(x) => set('customerId', x)} />}
      {def.usesPart && <LookupSelect kind="part" label="Part" value={v.partId} onChange={(x) => set('partId', x)} />}
      <Typography variant="overline" color="text.secondary" sx={{ gridColumn: '1 / -1' }}>Details</Typography>
      {def.fields.map((f) => <DetailField key={f.key} f={f} value={v.details[f.key]} onChange={(x) => setV({ ...v, details: { ...v.details, [f.key]: x } })} />)}
      <Typography variant="overline" color="text.secondary" sx={{ gridColumn: '1 / -1' }}>Dates and ownership</Typography>
      <TextField type="date" label="Effective" value={v.effectiveOn} slotProps={{ inputLabel: { shrink: true } }} onChange={(e) => set('effectiveOn', e.target.value)} />
      <TextField type="date" label="Review due" value={v.reviewDueOn} slotProps={{ inputLabel: { shrink: true } }} onChange={(e) => set('reviewDueOn', e.target.value)} />
      <TextField type="date" label="Expires" value={v.expiresOn} slotProps={{ inputLabel: { shrink: true } }} onChange={(e) => set('expiresOn', e.target.value)} />
      <TextField select label="Owner" value={v.ownerId} onChange={(e) => set('ownerId', e.target.value)}><MenuItem value="">Me</MenuItem>{(users.data ?? []).map((u) => <MenuItem key={u.id} value={u.id}>{u.displayName}</MenuItem>)}{setupOptions('user', users.data)}</TextField>
      {!initial.siteId && <SitePicker permission="qualityops.edit" value={v.siteId} onChange={(id) => set('siteId', id ?? '')} />}
      <Stack direction="row" gap={1} sx={{ gridColumn: '1 / -1' }}>
        <Button variant="contained" onClick={async () => { setErr(null); try { await onSubmit(v); } catch (e) { setErr((e as Error).message); } }}>{submitLabel}</Button><Button onClick={onCancel}>Cancel</Button>
      </Stack>
    </Paper>
  );
}
const entryBody = (v: EntryForm) => ({ siteId: v.siteId, departmentId: nul(v.departmentId), title: v.title, summary: nul(v.summary), supplierId: nul(v.supplierId), customerId: nul(v.customerId), partId: nul(v.partId), ownerId: nul(v.ownerId),
  effectiveOn: nul(v.effectiveOn), reviewDueOn: nul(v.reviewDueOn), expiresOn: nul(v.expiresOn), details: Object.fromEntries(Object.entries(v.details).filter(([, x]) => x !== null && x !== '' && x !== undefined)) });

export function RegisterEntryNew() {
  const { key } = useParams();
  const nav = useNavigate();
  const def = useRegisters().data?.find((d) => d.key === key);
  const crumbs = [{ label: 'Registers', to: `${base}/registers` }];
  if (!def) return <Loading crumbs={crumbs} error={null} />;
  return (
    <Page crumbs={[...crumbs, { label: def.name, to: `${base}/registers/${key}` }, { label: 'New' }]} title={`Add to ${def.name.toLowerCase()}`}>
      <EntryEditor def={def} submitLabel="Add entry" onCancel={() => nav(`${base}/registers/${key}`)}
        initial={{ siteId: '', departmentId: '', title: '', summary: '', supplierId: '', customerId: '', partId: '', ownerId: '', effectiveOn: '', reviewDueOn: '', expiresOn: '', details: {} }}
        onSubmit={async (v) => { if (!v.siteId) throw new Error('Choose a site.'); const r = await post<{ id: string }>(`${Q}/registers/${key}`, entryBody(v)); nav(`${base}/registers/entries/${r.id}`); }} />
    </Page>
  );
}

interface EntryDto { entry: { id: string; number: string; title: string; summary: string | null; status: string; siteId: string; departmentId: string | null; supplierId: string | null; customerId: string | null; partId: string | null; ownerId: string;
  effectiveOn: string | null; reviewDueOn: string | null; expiresOn: string | null; archived: boolean; rowVersion: number };
  details: Record<string, unknown>; owner: string; register: { key: string; name: string; fields: RegField[]; usesSupplier: boolean; usesCustomer: boolean; usesPart: boolean }; next: string[]; actions: Record<string, boolean> }
export function RegisterEntryDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const defs = useRegisters();
  const [dlg, setDlg] = useState<string | null>(null);
  const q = useQuery({ queryKey: ['register-entry', id], queryFn: () => get<EntryDto>(`${Q}/registers/entries/${id}`) });
  const refresh = () => qc.invalidateQueries({ queryKey: ['register-entry', id] });
  const { error, run } = useRunner(refresh);
  const crumbs = [{ label: 'Registers', to: `${base}/registers` }];
  const d = q.data;
  if (!d) return <Loading crumbs={crumbs} error={q.error} />;
  const x = d.entry; const r = d.register; const a = d.actions;
  const def = defs.data?.find((k) => k.key === r.key);
  const shown = (f: RegField) => { const v = d.details[f.key]; return v === undefined || v === null || v === '' ? '—' : f.type === 'checkbox' ? (v ? 'Yes' : 'No') : f.type === 'date' ? fmtDate(String(v)) : String(v); };
  if (dlg === 'edit' && def) return (
    <Page crumbs={[...crumbs, { label: r.name, to: `${base}/registers/${r.key}` }, { label: x.number, to: `${base}/registers/entries/${x.id}` }, { label: 'Edit' }]} title={`Edit ${x.number}`}>
      <EntryEditor def={def} submitLabel="Save" onCancel={() => setDlg(null)}
        initial={{ siteId: x.siteId, departmentId: x.departmentId ?? '', title: x.title, summary: x.summary ?? '', supplierId: x.supplierId ?? '', customerId: x.customerId ?? '', partId: x.partId ?? '', ownerId: x.ownerId,
          effectiveOn: x.effectiveOn ?? '', reviewDueOn: x.reviewDueOn ?? '', expiresOn: x.expiresOn ?? '', details: d.details }}
        onSubmit={async (v) => { await put(`${Q}/registers/entries/${x.id}`, { ...entryBody(v), rowVersion: x.rowVersion }); setDlg(null); refresh(); }} />
    </Page>);
  return (
    <Page crumbs={[...crumbs, { label: r.name, to: `${base}/registers/${r.key}` }, { label: x.number }]} title={<Title number={x.number} title={x.title} status={x.archived ? 'Archived' : x.status} />}
      actions={<Stack direction="row" gap={1} flexWrap="wrap">
        {a.edit && <Button variant="outlined" onClick={() => setDlg('edit')}>Edit</Button>}
        {d.next.map((n) => <Button key={n} variant="contained" onClick={() => setDlg('to:' + n)}>{n}</Button>)}
        {a.archive && <Button onClick={() => run(() => post(`${Q}/registers/entries/${x.id}/archive`))}>Archive</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: '290px minmax(0, 1fr)' }, gap: 3 }}>
        <Facts items={[['Register', r.name], ['Owner', d.owner], ['Effective', fmtDate(x.effectiveOn)], ['Review due', fmtDate(x.reviewDueOn)], ['Expires', fmtDate(x.expiresOn)]]} />
        <Stack gap={2} sx={{ minWidth: 0 }}>
          <Section title="Details"><Prose label="Summary" text={x.summary} /><Facts items={r.fields.map((f) => [f.label, shown(f)] as [string, string])} /></Section>
          <AttachmentsAndHistory type="RegisterEntry" id={x.id} canEdit={a.edit} />
        </Stack>
      </Box>
      {d.next.map((n) => <StepDialog key={n} open={dlg === 'to:' + n} title={`Move to ${n}`} confirm={n} onClose={() => setDlg(null)} inputs={[{ name: 'comment', label: 'Comment', type: 'multiline' }]}
        onConfirm={async (v) => { await post(`${Q}/registers/entries/${x.id}/status`, { status: n, comment: nul(v.comment) }); refresh(); }} />)}
    </Page>
  );
}

// ---------------------------------------------------------------- service levels and calendars
interface Calendar { id: string; name: string; siteId: string | null; site: string | null; workingDays: string; workdayStart: string; workdayEnd: string; isDefault: boolean; rowVersion: number; holidays: { date: string; name: string }[] }
interface Policy { id: string; name: string; recordType: string; status: string | null; targetHours: number; warningPercent: number; businessHoursOnly: boolean; calendarId: string | null; escalationPermission: string | null; active: boolean; rowVersion: number }
interface SlRow { policyId: string; policy: string; module: string; recordId: string; number: string; status: string; since: string; dueAt: string; elapsedPercent: number; state: string }
const days = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
export function ServiceLevelsPage() {
  const qc = useQueryClient();
  const { can } = useAuth();
  const admin = can('admin.organization');
  const [dlg, setDlg] = useState<string | null>(null);
  const status = useQuery({ queryKey: ['sl-status'], queryFn: () => get<SlRow[]>(`${Q}/service-levels/status`) });
  const pol = useQuery({ queryKey: ['sl-policies'], queryFn: () => get<{ modules: { key: string; label: string; entity: string; statuses: string[] }[]; policies: Policy[] }>(`${Q}/service-levels`) });
  const cals = useQuery({ queryKey: ['calendars'], queryFn: () => get<Calendar[]>(`${Q}/calendars`) });
  const refresh = () => { qc.invalidateQueries({ queryKey: ['sl-status'] }); qc.invalidateQueries({ queryKey: ['sl-policies'] }); qc.invalidateQueries({ queryKey: ['calendars'] }); };
  const { error, run } = useRunner(refresh);
  const mods = pol.data?.modules ?? [];
  const modOf = (entity: string) => mods.find((m) => m.entity === entity);
  const rank = (s: string) => (s === 'Breached' ? 0 : s === 'Warning' ? 1 : 2);
  const cols: ColumnDef<SlRow, any>[] = [{ header: 'Record', accessorKey: 'number', cell: num }, { header: 'Module', accessorKey: 'module' }, { header: 'Status', accessorKey: 'status' }, { header: 'Service level', accessorKey: 'policy' },
    { header: 'In status since', accessorKey: 'since', cell: (x) => fmtDateTime(x.getValue()) }, { header: 'Due', accessorKey: 'dueAt', cell: (x) => fmtDateTime(x.getValue()) },
    { header: 'Elapsed', accessorKey: 'elapsedPercent', cell: (x) => `${Math.round(x.getValue())}%` }, { header: 'State', accessorKey: 'state', cell: (x) => <StatusChip status={x.getValue()} /> }];
  return (
    <Page crumbs={[{ label: 'Service levels' }]} title="Service levels and business calendars"
      actions={admin ? <Stack direction="row" gap={1}><Button variant="outlined" onClick={() => setDlg('calendar')}>Add calendar</Button><Button variant="contained" onClick={() => setDlg('policy')}>Add service level</Button></Stack> : undefined}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <Section title="Open records against their service levels"><DataTable data={[...(status.data ?? [])].sort((a, b) => rank(a.state) - rank(b.state) || b.elapsedPercent - a.elapsedPercent)} columns={cols} empty="No open records under a service level." /></Section>
      <Box sx={{ mt: 2, display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: '1fr 1fr' }, gap: 2 }}>
        <Section title="Service levels">
          <Table size="small"><TableHead><TableRow><TableCell>Name</TableCell><TableCell>Module / status</TableCell><TableCell>Target</TableCell><TableCell>Active</TableCell>{admin && <TableCell />}</TableRow></TableHead>
            <TableBody>{(pol.data?.policies ?? []).map((p) => <TableRow key={p.id}><TableCell>{p.name}</TableCell><TableCell>{modOf(p.recordType)?.label ?? p.recordType} / {p.status ?? 'any open status'}</TableCell>
              <TableCell>{p.targetHours} h{p.businessHoursOnly ? ' (working hours)' : ''}, warn at {p.warningPercent}%</TableCell><TableCell>{p.active ? 'Yes' : 'No'}</TableCell>
              {admin && <TableCell><Button size="small" onClick={() => run(() => put(`${Q}/service-levels/${p.id}`, { name: p.name, module: modOf(p.recordType)?.key, status: p.status, targetHours: p.targetHours, warningPercent: p.warningPercent, businessHoursOnly: p.businessHoursOnly, calendarId: p.calendarId, escalationPermission: p.escalationPermission, active: !p.active, rowVersion: p.rowVersion }))}>{p.active ? 'Pause' : 'Activate'}</Button></TableCell>}</TableRow>)}
              {(pol.data?.policies ?? []).length === 0 && <TableRow><TableCell colSpan={5} sx={{ color: 'text.secondary' }}>No service levels.</TableCell></TableRow>}</TableBody></Table>
        </Section>
        <Section title="Business calendars">
          <Table size="small"><TableHead><TableRow><TableCell>Name</TableCell><TableCell>Site</TableCell><TableCell>Working time</TableCell><TableCell>Holidays</TableCell>{admin && <TableCell />}</TableRow></TableHead>
            <TableBody>{(cals.data ?? []).map((c) => <TableRow key={c.id}><TableCell>{c.name}{c.isDefault ? ' (default)' : ''}</TableCell><TableCell>{c.site ?? 'All sites'}</TableCell>
              <TableCell>{c.workingDays.split(',').map((x) => days[Number(x)]).join(' ')} {c.workdayStart.slice(0, 5)}–{c.workdayEnd.slice(0, 5)}</TableCell><TableCell>{c.holidays.map((h) => `${fmtDate(h.date)} ${h.name}`).join(', ') || '—'}</TableCell>
              {admin && <TableCell><Button size="small" color="error" onClick={() => run(() => del(`${Q}/calendars/${c.id}`))}>Delete</Button></TableCell>}</TableRow>)}
              {(cals.data ?? []).length === 0 && <TableRow><TableCell colSpan={5} sx={{ color: 'text.secondary' }}>No calendars; elapsed time is counted around the clock.</TableCell></TableRow>}</TableBody></Table>
        </Section>
      </Box>
      <StepDialog open={dlg === 'policy'} title="Add service level" confirm="Add" onClose={() => setDlg(null)}
        inputs={[{ name: 'name', label: 'Name' }, { name: 'module', label: 'Module', type: 'select', options: mods.map((m) => [m.key, m.label]) }, { name: 'status', label: 'Status (empty for any open status)' },
          { name: 'targetHours', label: 'Target (hours)', initial: '24' }, { name: 'warningPercent', label: 'Warn at (% of target)', initial: '75' }, { name: 'businessHoursOnly', label: 'Count working hours only', type: 'checkbox', initial: 'true' },
          { name: 'calendarId', label: 'Calendar', type: 'select', options: [['', 'Default calendar'], ...(cals.data ?? []).map((c) => [c.id, c.name] as [string, string])] }]}
        onConfirm={async (v) => { await post(`${Q}/service-levels`, { name: v.name, module: v.module, status: nul(v.status), targetHours: Number(v.targetHours), warningPercent: Number(v.warningPercent), businessHoursOnly: v.businessHoursOnly === 'true', calendarId: nul(v.calendarId), active: true }); refresh(); }} />
      <StepDialog open={dlg === 'calendar'} title="Add business calendar" confirm="Add" onClose={() => setDlg(null)}
        inputs={[{ name: 'name', label: 'Name' }, { name: 'siteId', label: 'Site (empty for all sites)', type: 'site', permission: 'admin.organization' }, { name: 'workingDays', label: 'Working days', type: 'multiselect', options: days.map((x, i) => [i, x]), initial: '1,2,3,4,5' },
          { name: 'workdayStart', label: 'Day starts (HH:MM)', initial: '08:00' }, { name: 'workdayEnd', label: 'Day ends (HH:MM)', initial: '17:00' }, { name: 'isDefault', label: 'Default calendar', type: 'checkbox' },
          { name: 'holidays', label: 'Holidays (one per line: YYYY-MM-DD name)', type: 'multiline' }]}
        onConfirm={async (v) => {
          const holidays = v.holidays.split('\n').map((l) => l.trim()).filter(Boolean).map((l) => { const [date, ...rest] = l.split(' '); return { date, name: rest.join(' ') || 'Holiday' }; });
          await post(`${Q}/calendars`, { name: v.name, siteId: nul(v.siteId), workingDays: v.workingDays.split(',').filter(Boolean).map(Number), workdayStart: v.workdayStart, workdayEnd: v.workdayEnd, isDefault: v.isDefault === 'true', holidays }); refresh();
        }} />
    </Page>
  );
}

// ---------------------------------------------------------------- skip-lot
export const skipLotState = ['Normal', 'Qualifying', 'Skipping', 'Disqualified'];
interface SkipRule { id: string; siteId: string; partId: string; partNumber: string; supplierId: string | null; supplier: string | null; qualifyAfterLots: number; inspectEvery: number; requalifyAfterLots: number; active: boolean; state: number; consecutiveAccepted: number; lotsSinceInspection: number; stateChangedAt: string | null; rowVersion: number }
interface SkipEvent { id: string; occurredAt: string; lotNumber: string | null; inspected: boolean; accepted: boolean | null; fromState: number; toState: number; note: string }
export function SkipLotPage() {
  const qc = useQueryClient();
  const { can } = useAuth();
  const [dlg, setDlg] = useState<string | null>(null);
  const [hist, setHist] = useState<string | null>(null);
  const rules = useQuery({ queryKey: ['skiplot'], queryFn: () => get<SkipRule[]>(`${Q}/skip-lot`) });
  const history = useQuery({ queryKey: ['skiplot-h', hist], queryFn: () => get<SkipEvent[]>(`${Q}/skip-lot/${hist}/history`), enabled: !!hist });
  const refresh = () => { qc.invalidateQueries({ queryKey: ['skiplot'] }); qc.invalidateQueries({ queryKey: ['skiplot-h'] }); };
  const parts = useLookup('part');
  const suppliers = useLookup('supplier');
  const edit = can('inspection.edit');
  const rule = rules.data?.find((r) => dlg === 'lot:' + r.id);
  return (
    <Page crumbs={[{ label: 'Skip-lot inspection' }]} title="Skip-lot inspection" actions={edit ? <Button variant="contained" onClick={() => setDlg('new')}>Add rule</Button> : undefined}>
      <Alert severity="info" sx={{ mb: 2 }}>A part (and supplier) qualifies after the set number of consecutive accepted lots; then one lot in every N is inspected. A rejected lot while skipping disqualifies it until it requalifies.</Alert>
      <Table size="small"><TableHead><TableRow><TableCell>Part</TableCell><TableCell>Supplier</TableCell><TableCell>Rule</TableCell><TableCell>State</TableCell><TableCell>Accepted in a row</TableCell><TableCell>Lots since inspection</TableCell><TableCell /></TableRow></TableHead>
        <TableBody>{(rules.data ?? []).map((r) => <TableRow key={r.id} selected={hist === r.id}>
          <TableCell sx={{ fontFamily: mono }}>{r.partNumber}</TableCell><TableCell>{r.supplier ?? 'Any'}</TableCell><TableCell>Qualify after {r.qualifyAfterLots}, inspect 1 in {r.inspectEvery}, requalify after {r.requalifyAfterLots}{r.active ? '' : ' (paused)'}</TableCell>
          <TableCell><StatusChip status={skipLotState[r.state]} /></TableCell><TableCell>{r.consecutiveAccepted}</TableCell><TableCell>{r.lotsSinceInspection}</TableCell>
          <TableCell><Stack direction="row" gap={1}><Button size="small" onClick={() => setHist(r.id)}>History</Button>{edit && r.active && <Button size="small" variant="outlined" onClick={() => setDlg('lot:' + r.id)}>Record lot</Button>}</Stack></TableCell></TableRow>)}
          {(rules.data ?? []).length === 0 && <TableRow><TableCell colSpan={7} sx={{ color: 'text.secondary' }}>No skip-lot rules.</TableCell></TableRow>}</TableBody></Table>
      {hist && <Box sx={{ mt: 2 }}><Section title="History"><Table size="small"><TableHead><TableRow><TableCell>When</TableCell><TableCell>Lot</TableCell><TableCell>Inspected</TableCell><TableCell>Result</TableCell><TableCell>State</TableCell><TableCell>Note</TableCell></TableRow></TableHead>
        <TableBody>{(history.data ?? []).map((h) => <TableRow key={h.id}><TableCell>{fmtDateTime(h.occurredAt)}</TableCell><TableCell>{h.lotNumber ?? '—'}</TableCell><TableCell>{h.inspected ? 'Yes' : 'Skipped'}</TableCell><TableCell>{h.accepted == null ? '—' : h.accepted ? 'Accepted' : 'Rejected'}</TableCell>
          <TableCell>{skipLotState[h.fromState]} → {skipLotState[h.toState]}</TableCell><TableCell>{h.note}</TableCell></TableRow>)}</TableBody></Table></Section></Box>}
      <StepDialog open={dlg === 'new'} title="Add skip-lot rule" confirm="Add rule" onClose={() => setDlg(null)}
        inputs={[{ name: 'siteId', label: 'Site', type: 'site', permission: 'inspection.edit' }, { name: 'partId', label: 'Part', type: 'select', setup: 'part', options: (parts.data ?? []).map((p) => [p.id, `${p.partNumber} — ${p.name}`]) },
          { name: 'supplierId', label: 'Supplier', type: 'select', setup: 'supplier', options: [['', 'Any supplier'], ...(suppliers.data ?? []).map((s) => [s.id, `${s.code} — ${s.legalName}`] as [string, string])] },
          { name: 'qualifyAfterLots', label: 'Qualify after (consecutive accepted lots)', initial: '5' }, { name: 'inspectEvery', label: 'Then inspect one lot in', initial: '4' }, { name: 'requalifyAfterLots', label: 'Requalify after (accepted lots)', initial: '10' }]}
        onConfirm={async (v) => { await post(`${Q}/skip-lot`, { siteId: v.siteId, partId: v.partId, supplierId: nul(v.supplierId), qualifyAfterLots: Number(v.qualifyAfterLots), inspectEvery: Number(v.inspectEvery), requalifyAfterLots: Number(v.requalifyAfterLots), active: true }); refresh(); }} />
      {rule && <SkipLotDialog rule={rule} onClose={() => setDlg(null)} onDone={refresh} />}
    </Page>
  );
}
function SkipLotDialog({ rule, onClose, onDone }: { rule: SkipRule; onClose: () => void; onDone: () => void }) {
  const decision = useQuery({ queryKey: ['skiplot-d', rule.id, rule.rowVersion], queryFn: () => get<{ inspect: boolean; state: number; reason: string }>(`${Q}/skip-lot/decision?siteId=${rule.siteId}&partId=${rule.partId}${rule.supplierId ? `&supplierId=${rule.supplierId}` : ''}`) });
  const d = decision.data;
  if (!d) return null;
  return <StepDialog open title={`Record lot for ${rule.partNumber}`} intro={d.inspect ? `Inspect this lot. ${d.reason}` : `This lot may be skipped. ${d.reason}`} confirm="Record" onClose={onClose}
    inputs={[{ name: 'lot', label: 'Lot' }, { name: 'result', label: 'Result', type: 'select', options: [...(!d.inspect ? [['skip', 'Skipped (not inspected)'] as [string, string]] : []), ['accept', 'Inspected and accepted'], ['reject', 'Inspected and rejected']], initial: d.inspect ? 'accept' : 'skip' }]}
    onConfirm={async (v) => { await post(`${Q}/skip-lot/lots`, { siteId: rule.siteId, partId: rule.partId, supplierId: rule.supplierId, lot: nul(v.lot), inspected: v.result !== 'skip', accepted: v.result === 'skip' ? null : v.result === 'accept' }); onDone(); }} />;
}

// ---------------------------------------------------------------- quality reviews
export const reviewStatus = ['Draft', 'PendingApproval', 'Approved', 'Rejected'];
export const reviewKind = ['Product quality review', 'Annual quality review'];
interface ReviewRow { id: string; number: string; kind: number; title: string; status: number; periodFrom: string; periodTo: string; metricsAt: string; partNumber: string | null; product: string | null }
export function ReviewList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const list = useQuery({ queryKey: ['qreviews'], queryFn: () => get<{ items: ReviewRow[] }>(`${Q}/reviews?pageSize=300`) });
  const cols: ColumnDef<ReviewRow, any>[] = [{ header: 'Number', accessorKey: 'number', cell: num }, { header: 'Type', accessorKey: 'kind', cell: (x) => reviewKind[x.getValue()] }, { header: 'Title', accessorKey: 'title' },
    { header: 'Scope', accessorKey: 'product', cell: (x) => x.getValue() ?? x.row.original.partNumber ?? 'Site' }, { header: 'Period', accessorKey: 'periodFrom', cell: (x) => `${fmtDate(x.getValue())} – ${fmtDate(x.row.original.periodTo)}` },
    { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={reviewStatus[x.getValue()]} /> }];
  return <Page crumbs={[{ label: 'Quality reviews' }]} title="Product and annual quality reviews"
    actions={can('qualityops.edit') ? <Button variant="contained" component={RouterLink} to={`${base}/reviews/new`}>New review</Button> : undefined}>
    <DataTable data={list.data?.items ?? []} columns={cols} onRowClick={(r) => nav(`${base}/reviews/${r.id}`)} empty="No quality reviews yet." /></Page>;
}
export function ReviewNew() {
  const nav = useNavigate();
  const schema = z.object({ kind: z.coerce.number().int(), title: z.string().trim().min(1, 'Required').max(300), productId: z.string().optional(), partId: z.string().optional(), periodFrom: z.string().min(1, 'Required'), periodTo: z.string().min(1, 'Required'),
    ownerId: z.string().optional(), siteId: z.string().uuid('Choose a site'), departmentId: z.string().optional() });
  return (
    <Page crumbs={[{ label: 'Quality reviews', to: `${base}/reviews` }, { label: 'New' }]} title="New quality review">
      <SchemaForm schema={schema} submitLabel="Create review" onCancel={() => nav(`${base}/reviews`)} defaults={{ kind: 0, title: '', productId: '', partId: '', periodFrom: localDate(-365), periodTo: localDate(), ownerId: '', siteId: '', departmentId: '' }}
        fields={[{ name: 'kind', label: 'Type', type: 'select', options: reviewKind.map((k, i) => [i, k] as [number, string]) }, { name: 'title', label: 'Title', full: true }, { name: 'productId', label: 'Product', type: 'lookup', lookup: 'product' }, { name: 'partId', label: 'Part', type: 'lookup', lookup: 'part' },
          { name: 'periodFrom', label: 'Period from', type: 'date' }, { name: 'periodTo', label: 'Period to', type: 'date' }, { name: 'ownerId', label: 'Owner (defaults to you)', type: 'user' }, { name: 'siteId', label: 'Site', type: 'site' }, { name: 'departmentId', label: 'Department (optional)', type: 'department' }]}
        onSubmit={async (v) => { const r = await post<{ id: string }>(`${Q}/reviews`, { ...v, productId: nul(v.productId), partId: nul(v.partId), ownerId: nul(v.ownerId), departmentId: nul(v.departmentId) }); nav(`${base}/reviews/${r.id}`); }} />
    </Page>
  );
}
interface ReviewDto { review: { id: string; number: string; kind: number; title: string; status: number; periodFrom: string; periodTo: string; metricsAt: string; conclusions: string | null; actions: string | null; rowVersion: number }; metrics: Record<string, unknown>; owner: string; actions: Record<string, boolean> }
const label = (k: string) => k.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^./, (c) => c.toUpperCase());
function MetricValue({ v }: { v: unknown }) {
  if (v === null || v === undefined) return <>—</>;
  if (Array.isArray(v)) return v.length === 0 ? <>None</> : typeof v[0] !== 'object' ? <>{v.join(', ')}</> : <Table size="small"><TableHead><TableRow>{Object.keys(v[0] as object).map((k) => <TableCell key={k}>{label(k)}</TableCell>)}</TableRow></TableHead>
    <TableBody>{v.map((r, i) => <TableRow key={i}>{Object.values(r as object).map((c, j) => <TableCell key={j}>{typeof c === 'object' && c !== null ? JSON.stringify(c) : String(c ?? '—')}</TableCell>)}</TableRow>)}</TableBody></Table>;
  if (typeof v === 'object') return <Facts items={Object.entries(v as object).map(([k, x]) => [label(k), typeof x === 'object' && x !== null ? <MetricValue v={x} /> : String(x ?? '—')] as [string, ReactNode])} />;
  return <>{String(v)}</>;
}
export function ReviewDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const [dlg, setDlg] = useState<string | null>(null);
  const [tab, setTab] = useState('metrics');
  const q = useQuery({ queryKey: ['qreview', id], queryFn: () => get<ReviewDto>(`${Q}/reviews/${id}`) });
  const refresh = () => qc.invalidateQueries({ queryKey: ['qreview', id] });
  const { error, run } = useRunner(refresh);
  const crumbs = [{ label: 'Quality reviews', to: `${base}/reviews` }];
  const d = q.data;
  if (!d) return <Loading crumbs={crumbs} error={q.error} />;
  const x = d.review; const a = d.actions;
  return (
    <Page crumbs={[...crumbs, { label: x.number }]} title={<Title number={x.number} title={x.title} status={reviewStatus[x.status]} />}
      tabs={[{ label: 'Metrics', value: 'metrics' }, { label: 'Conclusions', value: 'text' }, { label: 'Approval', value: 'approval' }, { label: 'Files & history', value: 'more' }]} tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1}>
        {a.refresh && <Button variant="outlined" onClick={() => run(() => post(`${Q}/reviews/${x.id}/refresh`))}>Refresh metrics</Button>}
        {a.edit && <Button variant="outlined" onClick={() => setDlg('text')}>Write conclusions</Button>}
        {a.submit && <Button variant="contained" onClick={() => run(() => post(`${Q}/reviews/${x.id}/submit`))}>Submit for approval</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: '290px minmax(0, 1fr)' }, gap: 3 }}>
        <Facts items={[['Type', reviewKind[x.kind]], ['Owner', d.owner], ['Period', `${fmtDate(x.periodFrom)} – ${fmtDate(x.periodTo)}`], ['Metrics as of', fmtDateTime(x.metricsAt)]]} />
        <Box sx={{ minWidth: 0 }}>
          {tab === 'metrics' && <Stack gap={2}>{Object.entries(d.metrics).map(([k, v]) => <Section key={k} title={label(k)}><MetricValue v={v} /></Section>)}</Stack>}
          {tab === 'text' && <Section title="Review"><Prose label="Conclusions" text={x.conclusions} /><Prose label="Actions" text={x.actions} /></Section>}
          {tab === 'approval' && <ApprovalsPanel type="QualityReview" id={x.id} />}
          {tab === 'more' && <AttachmentsAndHistory type="QualityReview" id={x.id} canEdit={a.edit} />}
        </Box>
      </Box>
      <StepDialog open={dlg === 'text'} title="Conclusions and actions" confirm="Save" onClose={() => setDlg(null)}
        inputs={[{ name: 'conclusions', label: 'Conclusions', type: 'multiline', initial: x.conclusions ?? '' }, { name: 'actions', label: 'Actions', type: 'multiline', initial: x.actions ?? '' }]}
        onConfirm={async (v) => { await put(`${Q}/reviews/${x.id}/text`, { conclusions: nul(v.conclusions), actions: nul(v.actions), rowVersion: x.rowVersion }); refresh(); }} />
    </Page>
  );
}

// ---------------------------------------------------------------- analytics
interface Readiness { kind: string; score: number; band: string; factors: { key: string; label: string; total: number; late: number; score: number; weight: number }[] }
export function QualityAnalyticsPage() {
  const { can } = useAuth();
  const mods = useQuery({ queryKey: ['qa-mods'], queryFn: () => get<{ key: string; label: string }[]>(`${Q}/analytics/modules`) });
  const [m, setM] = useState('ncr');
  const [from, setFrom] = useState(localDate(-90));
  const [to, setTo] = useState(localDate());
  const range = `from=${from}&to=${to}`;
  const aging = useQuery({ queryKey: ['qa-aging', m], queryFn: () => get<{ band: string; count: number }[]>(`${Q}/analytics/aging?module=${m}`) });
  const bottle = useQuery({ queryKey: ['qa-bottle', m, range], queryFn: () => get<{ status: string; transitions: number; averageHours: number; maxHours: number; currentlyIn: number }[]>(`${Q}/analytics/bottlenecks?module=${m}&${range}`) });
  const cycles = useQuery({ queryKey: ['qa-cycle', range], queryFn: () => get<{ module: string; closed: number; averageDays: number; medianDays: number; p90Days: number }[]>(`${Q}/analytics/cycle-times?${range}`) });
  const ready = useQuery({ queryKey: ['qa-ready'], queryFn: () => get<Readiness[]>(`${Q}/analytics/readiness`) });
  const rec = useQuery({ queryKey: ['qa-rec'], queryFn: () => get<{ key: string; part: string; category: string; count: number; systemic: boolean; records: string[] }[]>(`${Q}/analytics/recurrence`) });
  const capa = useQuery({ queryKey: ['qa-capa', range], queryFn: () => get<Record<string, unknown>>(`${Q}/analytics/capa-effectiveness?${range}`), enabled: can('capa.view') });
  const sup = useQuery({ queryKey: ['qa-sup'], queryFn: () => get<{ supplierId: string; code: string; name: string; status: string; score: number; band: string; signals: string[] }[]>(`${Q}/analytics/supplier-risk`), enabled: can('suppliers.view') });
  const max = Math.max(1, ...(aging.data ?? []).map((b) => b.count));
  const h = (n: number) => (n < 48 ? `${n.toFixed(1)} h` : `${(n / 24).toFixed(1)} d`);
  return (
    <Page crumbs={[{ label: 'Analytics' }]} title="Quality analytics"
      actions={<Stack direction="row" gap={1}><TextField size="small" type="date" label="From" value={from} slotProps={{ inputLabel: { shrink: true } }} onChange={(e) => setFrom(e.target.value)} /><TextField size="small" type="date" label="To" value={to} slotProps={{ inputLabel: { shrink: true } }} onChange={(e) => setTo(e.target.value)} /></Stack>}>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: 'repeat(2, 1fr)' }, gap: 2, mb: 2 }}>
        {(ready.data ?? []).map((r) => <Section key={r.kind} title={/readiness$/i.test(r.kind) ? r.kind : `${r.kind} readiness`}>
          <Stack direction="row" alignItems="baseline" gap={1}><Typography sx={{ fontSize: 36, fontWeight: 700, fontFamily: mono, color: r.factors.length === 0 ? 'text.secondary' : r.score >= 85 ? 'success.main' : r.score >= 65 ? 'warning.main' : 'error.main' }}>{r.factors.length === 0 ? '–' : Math.round(r.score)}</Typography><Typography color="text.secondary">{r.band}</Typography></Stack>
          <Table size="small"><TableBody>{r.factors.map((f) => <TableRow key={f.key}><TableCell>{f.label}</TableCell><TableCell>{f.late} late of {f.total}</TableCell><TableCell sx={{ fontFamily: mono }}>{Math.round(f.score)}</TableCell></TableRow>)}</TableBody></Table>
        </Section>)}
      </Box>
      <Section title="Cycle time (closed in the period)">
        <Table size="small"><TableHead><TableRow><TableCell>Module</TableCell><TableCell>Closed</TableCell><TableCell>Average</TableCell><TableCell>Median</TableCell><TableCell>90th percentile</TableCell></TableRow></TableHead>
          <TableBody>{(cycles.data ?? []).map((c) => <TableRow key={c.module}><TableCell>{c.module}</TableCell><TableCell>{c.closed}</TableCell><TableCell>{c.averageDays.toFixed(1)} d</TableCell><TableCell>{c.medianDays.toFixed(1)} d</TableCell><TableCell>{c.p90Days.toFixed(1)} d</TableCell></TableRow>)}</TableBody></Table>
      </Section>
      <Box sx={{ mt: 2 }}><Section title="Aging and bottlenecks" action={<TextField select size="small" label="Module" value={m} onChange={(e) => setM(e.target.value)} sx={{ minWidth: 200 }}>{(mods.data ?? []).map((x) => <MenuItem key={x.key} value={x.key}>{x.label}</MenuItem>)}</TextField>}>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: 'minmax(0, 1fr) minmax(0, 1.4fr)' }, gap: 3 }}>
          <Stack gap={0.75}>{(aging.data ?? []).map((b) => <Stack key={b.band} direction="row" gap={1} alignItems="center"><Box sx={{ width: 90, fontSize: 13, color: 'text.secondary' }}>{b.band}</Box>
            <Box sx={{ height: 18, width: `${(b.count / max) * 100}%`, minWidth: 2, bgcolor: 'primary.main', borderRadius: 0.5 }} /><Box sx={{ fontFamily: mono, fontSize: 13 }}>{b.count}</Box></Stack>)}</Stack>
          <Table size="small"><TableHead><TableRow><TableCell>Status</TableCell><TableCell>Passed through</TableCell><TableCell>Average time in status</TableCell><TableCell>Longest</TableCell><TableCell>In it now</TableCell></TableRow></TableHead>
            <TableBody>{(bottle.data ?? []).map((b) => <TableRow key={b.status}><TableCell>{b.status}</TableCell><TableCell>{b.transitions}</TableCell><TableCell>{h(b.averageHours)}</TableCell><TableCell>{h(b.maxHours)}</TableCell><TableCell>{b.currentlyIn}</TableCell></TableRow>)}</TableBody></Table>
        </Box>
      </Section></Box>
      <Box sx={{ mt: 2, display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: '1fr 1fr' }, gap: 2 }}>
        <Section title="Recurrence (events and complaints)">{(rec.data ?? []).length === 0 ? <Typography color="text.secondary" fontSize={14}>No recurring groups in the window.</Typography> :
          <Table size="small"><TableHead><TableRow><TableCell>Part</TableCell><TableCell>Category</TableCell><TableCell>Count</TableCell><TableCell>Records</TableCell></TableRow></TableHead>
            <TableBody>{rec.data!.map((r) => <TableRow key={r.key}><TableCell>{r.part}</TableCell><TableCell>{r.category}</TableCell><TableCell>{r.count}{r.systemic && <> <StatusChip status="Systemic" /></>}</TableCell><TableCell sx={{ fontFamily: mono, fontSize: 12 }}>{r.records.join(', ')}</TableCell></TableRow>)}</TableBody></Table>}</Section>
        {can('capa.view') && <Section title="CAPA effectiveness"><MetricValue v={capa.data} /></Section>}
      </Box>
      {can('suppliers.view') && <Box sx={{ mt: 2 }}><Section title="Supplier risk">
        <Table size="small"><TableHead><TableRow><TableCell>Supplier</TableCell><TableCell>Status</TableCell><TableCell>Score</TableCell><TableCell>Signals</TableCell></TableRow></TableHead>
          <TableBody>{(sup.data ?? []).slice(0, 25).map((s) => <TableRow key={s.supplierId}><TableCell>{s.code} — {s.name}</TableCell><TableCell>{s.status}</TableCell><TableCell><StatusChip status={`${Math.round(s.score)} ${s.band}`} /></TableCell><TableCell>{s.signals.join(' · ') || '—'}</TableCell></TableRow>)}</TableBody></Table>
      </Section></Box>}
    </Page>
  );
}

// ---------------------------------------------------------------- home
export function QualityOpsHome() {
  const { can } = useAuth();
  const nav = useNavigate();
  const s = useSettings().data;
  const ev = useQuery({ queryKey: ['qevents', 'open', ''], queryFn: () => get<{ items: EventRow[] }>(`${Q}/events?pageSize=300&openOnly=true`) });
  const dev = useQuery({ queryKey: ['deviations', '1', ''], queryFn: () => get<{ items: DeviationRow[] }>(`${Q}/deviations?pageSize=300&status=1`) });
  const board = useQuery({ queryKey: ['release-board'], queryFn: () => get<{ holds: unknown[]; releases: { ready: boolean }[] }>(`${Q}/releases/board`) });
  const sl = useQuery({ queryKey: ['sl-status'], queryFn: () => get<SlRow[]>(`${Q}/service-levels/status`) });
  const now = new Date().toISOString();
  const tiles: [string, number | undefined, string, boolean][] = [
    ['Open quality events', ev.data?.items.length, `${base}/events`, false], ['Past response time', ev.data?.items.filter((e) => e.respondBy && e.respondBy < now && e.status < 3).length, `${base}/events`, true],
    ['Deviations waiting for approval', dev.data?.items.length, `${base}/deviations?view=1`, false], ['Releases waiting', board.data?.releases.length, `${base}/releases`, false],
    ['Active holds', board.data?.holds.length, `${base}/releases`, false], ['Service levels breached', sl.data?.filter((r) => r.state === 'Breached').length, `${base}/service-levels`, true]];
  const evCols: ColumnDef<EventRow, any>[] = [{ header: 'Number', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' }, { header: 'Severity', accessorKey: 'severity', cell: (x) => <SeverityChip settings={s} name={x.getValue()} /> },
    { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={eventStatus[x.getValue()]} /> }, { header: 'Occurred', accessorKey: 'occurredAt', cell: (x) => fmtDateTime(x.getValue()) }];
  return (
    <Page crumbs={[{ label: 'Overview' }]} title="Quality operations"
      actions={can('qualityops.edit') || can('qualityevents.report') ? <Button variant="contained" component={RouterLink} to={`${base}/events/new`}>Report event</Button> : undefined}>
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: 2, mb: 3 }}>
        {tiles.map(([t, n, to, bad]) => <Paper key={t} variant="outlined" component={RouterLink} to={to} sx={{ p: 2, textDecoration: 'none', color: 'inherit', '&:hover': { borderColor: 'primary.main' } }}>
          <Typography fontSize={13} color="text.secondary">{t}</Typography><Typography sx={{ fontSize: 30, fontWeight: 700, fontFamily: mono, color: bad && n ? 'error.main' : undefined }}>{n ?? '…'}</Typography></Paper>)}
      </Box>
      <Section title="Newest open events"><DataTable data={(ev.data?.items ?? []).slice(0, 10)} columns={evCols} onRowClick={(r) => nav(`${base}/events/${r.id}`)} empty="No open events." /></Section>
    </Page>
  );
}
