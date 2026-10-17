import { useEffect, useState } from 'react';
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Checkbox, FormControlLabel, IconButton, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import DeleteOutline from '@mui/icons-material/DeleteOutline';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { z } from 'zod';
import { get, post, put } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, fmtDate, fmtDateTime, localDate } from '../components/Status';
import { ActionsPanel, AttachmentsAndHistory } from '../components/RecordTabs';
import { Facts, Prose, SchemaForm, Section, StepDialog, useRunner } from '../components/Kit';
import { mono } from '../theme';

export const riskStatus = ['Open', 'Mitigating', 'PendingAcceptance', 'Monitoring', 'Closed', 'Cancelled'];
const kinds = ['Risk', 'Opportunity'];
const mod = () => modules.find((m) => m.key === 'risks')!;
const num = (x: { getValue: () => unknown }) => <span style={{ fontFamily: mono }}>{String(x.getValue())}</span>;

interface Level { value: number; label: string }
interface Band { minScore: number; label: string; color: string; requiresAcceptance: boolean; escalate: boolean }
export interface RiskSettings { probability: Level[]; severity: Level[]; detectability: Level[]; useDetectability: boolean; bands: Band[]; categories: string[]; rowVersion: number }
const useSettings = () => useQuery({ queryKey: ['risk-settings'], queryFn: () => get<RiskSettings>('/api/risks/settings') });
const bandOf = (s: RiskSettings | undefined, label: string | null) => s?.bands.find((b) => b.label === label);
const bandFor = (s: RiskSettings, score: number) => [...s.bands].sort((a, b) => b.minScore - a.minScore).find((b) => score >= b.minScore);

export function BandChip({ settings, label, score }: { settings?: RiskSettings; label: string | null; score?: number | null }) {
  if (!label) return <span>—</span>;
  const b = bandOf(settings, label);
  return <Box component="span" sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.75, px: 1, py: 0.2, borderRadius: 1, fontSize: 12.5, fontWeight: 600, color: '#fff', bgcolor: b?.color ?? 'grey.600', whiteSpace: 'nowrap' }}>
    {score != null && <span style={{ fontFamily: mono }}>{score}</span>}{label}</Box>;
}

interface Row { id: string; number: string; kind: number; category: string; title: string; status: number; initialScore: number; initialBand: string; residualScore: number | null; residualBand: string | null; reviewDueOn: string; escalated: boolean; ownerName: string }

export function RiskList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const settings = useSettings();
  const [p, setP] = useSearchParams();
  const view = p.get('view') ?? 'open';
  const [q, setQ] = useState('');
  const [applied, setApplied] = useState('');
  const filter = view === 'open' ? '&openOnly=true' : view === 'review' ? '&reviewDue=true' : view === 'opportunities' ? '&kind=1&openOnly=true' : view === 'all' ? '' : `&status=${view}`;
  const list = useQuery({ queryKey: ['risks', view, applied], queryFn: () => get<{ items: Row[] }>(`/api/risks?pageSize=300${filter}${applied ? `&q=${encodeURIComponent(applied)}` : ''}`) });
  const today = localDate();
  const s = settings.data;
  const cols: ColumnDef<Row, any>[] = [
    { header: 'Number', accessorKey: 'number', cell: num }, { header: 'Type', accessorKey: 'kind', cell: (x) => kinds[x.getValue()] }, { header: 'Category', accessorKey: 'category' }, { header: 'Title', accessorKey: 'title' },
    { header: 'Initial', accessorKey: 'initialScore', cell: (x) => <BandChip settings={s} label={x.row.original.initialBand} score={x.getValue()} /> },
    { header: 'Residual', accessorKey: 'residualScore', cell: (x) => <BandChip settings={s} label={x.row.original.residualBand} score={x.getValue()} /> },
    { header: 'Status', accessorKey: 'status', cell: (x) => <Stack direction="row" gap={0.5}><StatusChip status={riskStatus[x.getValue()]} />{x.row.original.escalated && <StatusChip status="Escalated" />}</Stack> },
    { header: 'Review due', accessorKey: 'reviewDueOn', cell: (x) => <Box component="span" sx={{ color: x.getValue() < today && x.row.original.status < 4 ? 'error.main' : undefined }}>{fmtDate(x.getValue())}</Box> },
    { header: 'Owner', accessorKey: 'ownerName' },
  ];
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Risks' }]} title="Risk and opportunity register"
      tabs={[{ label: 'Open', value: 'open' }, { label: 'Pending acceptance', value: '2' }, { label: 'Reviews due', value: 'review' }, { label: 'Opportunities', value: 'opportunities' }, { label: 'Closed', value: '4' }, { label: 'All', value: 'all' }]}
      tab={view} onTab={(v) => setP(v === 'open' ? {} : { view: v })}
      actions={can('risks.edit') ? <Button variant="contained" component={RouterLink} to="/risks/new">Add risk or opportunity</Button> : undefined}>
      <Stack direction="row" gap={1} component="form" sx={{ mb: 2 }} onSubmit={(e) => { e.preventDefault(); setApplied(q); }}>
        <TextField size="small" placeholder="Number, title, category, process" value={q} onChange={(e) => setQ(e.target.value)} sx={{ width: 360 }} />
        <Button type="submit" variant="outlined">Filter</Button>
      </Stack>
      <DataTable data={list.data?.items ?? []} columns={cols} onRowClick={(r) => nav(`/risks/${r.id}`)} empty="No risks in this view." />
    </ModuleWorkspace>
  );
}

/** Probability × severity heat map of open risks, colored by the configured bands. */
export function RiskMatrix() {
  const [residual, setResidual] = useState(true);
  const q = useQuery({ queryKey: ['risk-matrix', residual], queryFn: () => get<{ settings: RiskSettings; cells: { probability: number; severity: number; count: number }[] }>(`/api/risks/matrix?residual=${residual}`) });
  const s = q.data?.settings;
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Risks', to: '/risks' }, { label: 'Heat map' }]} title="Risk heat map"
      actions={<FormControlLabel control={<Checkbox checked={residual} onChange={(e) => setResidual(e.target.checked)} />} label="Use residual rating where assessed" />}>
      {s && <Paper sx={{ p: 3, display: 'inline-block' }}>
        <Box sx={{ display: 'grid', gridTemplateColumns: `140px repeat(${s.severity.length}, 96px)`, gap: 0.5 }}>
          {[...s.probability].reverse().map((p) => [
            <Box key={'l' + p.value} sx={{ fontSize: 13, display: 'flex', alignItems: 'center', justifyContent: 'flex-end', pr: 1, color: 'text.secondary' }}>{p.value} · {p.label}</Box>,
            ...s.severity.map((sv) => {
              const c = q.data!.cells.find((x) => x.probability === p.value && x.severity === sv.value)?.count ?? 0;
              const band = bandFor(s, p.value * sv.value);
              return <Box key={`${p.value}-${sv.value}`} title={`${band?.label} (${p.value * sv.value})`} sx={{ height: 64, borderRadius: 1, bgcolor: band?.color, opacity: c ? 1 : 0.35, color: '#fff', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 20, fontWeight: 700, fontFamily: mono }}>{c || ''}</Box>;
            }),
          ])}
          <Box />
          {s.severity.map((sv) => <Box key={'s' + sv.value} sx={{ fontSize: 12, textAlign: 'center', color: 'text.secondary', pt: 0.5 }}>{sv.value} · {sv.label}</Box>)}
        </Box>
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1.5 }}>Rows: probability. Columns: severity. Numbers are open risks in each cell{s.useDetectability ? '; scores also multiply detectability, so cell color shows probability × severity only' : ''}.</Typography>
        <Stack direction="row" gap={1} sx={{ mt: 2 }}>{s.bands.map((b) => <BandChip key={b.label} settings={s} label={b.label} score={b.minScore} />)}</Stack>
      </Paper>}
    </ModuleWorkspace>
  );
}

const nul = (v?: string) => (v ? v : null);

export function RiskNew() {
  const nav = useNavigate();
  const s = useSettings().data;
  if (!s) return <ModuleWorkspace module={mod()} crumbs={[{ label: 'Risks', to: '/risks' }, { label: 'New' }]} title="Add risk or opportunity">{null}</ModuleWorkspace>;
  const lv = (l: Level[]) => l.map((x) => [x.value, `${x.value} · ${x.label}`] as [number, string]);
  const schema = z.object({
    kind: z.coerce.number().int(), category: z.string().min(1, 'Required'), source: z.string().max(200).optional(), title: z.string().trim().min(1, 'Required').max(300), description: z.string().trim().min(1, 'Required').max(10000),
    cause: z.string().max(4000).optional(), consequence: z.string().max(4000).optional(), process: z.string().max(200).optional(), productId: z.string().optional(),
    probability: z.coerce.number().int().min(1, 'Required'), severity: z.coerce.number().int().min(1, 'Required'), detectability: z.union([z.literal(''), z.coerce.number().int()]).optional(),
    existingControls: z.string().max(4000).optional(), mitigationPlan: z.string().max(4000).optional(), reviewIntervalDays: z.coerce.number().int().min(1).max(1825),
    siteId: z.string().uuid('Choose a site'), departmentId: z.string().optional(), ownerId: z.string().optional(),
  });
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Risks', to: '/risks' }, { label: 'New' }]} title="Add risk or opportunity">
      <SchemaForm schema={schema} submitLabel="Add to register" onCancel={() => nav('/risks')}
        defaults={{ kind: 0, category: '', source: '', title: '', description: '', cause: '', consequence: '', process: '', productId: '', probability: 0, severity: 0, detectability: '', existingControls: '', mitigationPlan: '', reviewIntervalDays: 180, siteId: '', departmentId: '', ownerId: '' }}
        fields={[{ name: 'kind', label: 'Type', type: 'select', options: kinds.map((k, i) => [i, k] as [number, string]) }, { name: 'category', label: 'Category', type: 'select', options: s.categories.map((c) => [c, c] as [string, string]) },
          { name: 'title', label: 'Title', full: true }, { name: 'description', label: 'Description', type: 'multiline' }, { name: 'source', label: 'Source (audit, FMEA, complaint, review…)' }, { name: 'process', label: 'Process' },
          { name: 'cause', label: 'Cause', type: 'multiline' }, { name: 'consequence', label: 'Consequence', type: 'multiline' }, { name: 'productId', label: 'Product (optional)', type: 'lookup', lookup: 'product' },
          { name: 'rating' as never, label: 'Initial rating', type: 'heading' }, { name: 'probability', label: 'Probability', type: 'select', options: lv(s.probability) }, { name: 'severity', label: 'Severity', type: 'select', options: lv(s.severity) },
          ...(s.useDetectability ? [{ name: 'detectability' as const, label: 'Detectability', type: 'select' as const, options: lv(s.detectability) }] : []),
          { name: 'existingControls', label: 'Existing controls', type: 'multiline' }, { name: 'mitigationPlan', label: 'Mitigation plan', type: 'multiline' },
          { name: 'reviewIntervalDays', label: 'Review interval (days)', type: 'number' }, { name: 'siteId', label: 'Site', type: 'site' }, { name: 'departmentId', label: 'Department (optional)', type: 'department' }, { name: 'ownerId', label: 'Owner (defaults to you)', type: 'user' }]}
        onSubmit={async (v) => {
          const r = await post<{ id: string }>('/api/risks', { ...v, source: nul(v.source), cause: nul(v.cause), consequence: nul(v.consequence), process: nul(v.process), productId: nul(v.productId), departmentId: nul(v.departmentId), ownerId: nul(v.ownerId),
            detectability: v.detectability === '' || v.detectability === undefined ? null : v.detectability, existingControls: nul(v.existingControls), mitigationPlan: nul(v.mitigationPlan) });
          nav(`/risks/${r.id}`);
        }} />
    </ModuleWorkspace>
  );
}

interface Dto { id: string; number: string; kind: number; category: string; source: string | null; title: string; description: string; cause: string | null; consequence: string | null; process: string | null; productId: string | null; productName: string | null;
  siteId: string; departmentId: string | null; ownerId: string; probability: number; severity: number; detectability: number | null; initialScore: number; initialBand: string; existingControls: string | null; mitigationPlan: string | null;
  residualProbability: number | null; residualSeverity: number | null; residualDetectability: number | null; residualScore: number | null; residualBand: string | null; acceptanceCriteria: string | null; acceptedAt: string | null; acceptedByName: string | null;
  reviewDueOn: string; reviewIntervalDays: number; lastReviewedOn: string | null; monitoring: string | null; escalated: boolean; status: number; closureNotes: string | null; closedAt: string | null; rowVersion: number;
  ownerName: string; siteCode: string; settings: RiskSettings; allowed: string[];
  reviews: { id: string; reviewedOn: string; score: number; band: string; notes: string; stillEffective: boolean; reviewedByName: string }[] }

export function RiskDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const [tab, setTab] = useState('assessment');
  const [dlg, setDlg] = useState<string | null>(null);
  const q = useQuery({ queryKey: ['risk', id], queryFn: () => get<Dto>(`/api/risks/${id}`) });
  const refresh = () => qc.invalidateQueries({ queryKey: ['risk', id] });
  const { error, run } = useRunner(refresh);
  const d = q.data;
  if (!d) return <ModuleWorkspace module={mod()} crumbs={[{ label: 'Risks', to: '/risks' }]} title="Loading">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const a = (k: string) => d.allowed.includes(k);
  const s = d.settings;
  const lbl = (l: Level[], v: number | null) => v == null ? '—' : `${v} · ${l.find((x) => x.value === v)?.label ?? ''}`;
  const lv = (l: Level[]) => l.map((x) => [String(x.value), `${x.value} · ${x.label}`] as [string, string]);
  const ratingInputs = (p: number | null, sv: number | null, dt: number | null) => [
    { name: 'probability', label: 'Probability', type: 'select' as const, options: lv(s.probability), initial: p ? String(p) : '' },
    { name: 'severity', label: 'Severity', type: 'select' as const, options: lv(s.severity), initial: sv ? String(sv) : '' },
    ...(s.useDetectability ? [{ name: 'detectability', label: 'Detectability', type: 'select' as const, options: lv(s.detectability), initial: dt ? String(dt) : '' }] : []),
  ];
  const n = (v: string | undefined) => (v ? Number(v) : null);
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Risks', to: '/risks' }, { label: d.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono, whiteSpace: 'nowrap' }}>{d.number}</span><span>{d.title}</span><StatusChip status={riskStatus[d.status]} /></Stack>}
      tabs={[{ label: 'Assessment', value: 'assessment' }, { label: `Reviews (${d.reviews.length})`, value: 'reviews' }, { label: 'Mitigation actions', value: 'actions' }, { label: 'Files & history', value: 'more' }]} tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1} flexWrap="wrap">
        {a('edit') && <Button variant="outlined" onClick={() => setDlg('edit')}>Edit controls and plan</Button>}
        {a('mitigate') && <Button variant="contained" onClick={() => run(() => post(`/api/risks/${d.id}/mitigate`))}>Start mitigation</Button>}
        {a('residual') && <Button variant={d.status === 1 ? 'contained' : 'outlined'} onClick={() => setDlg('residual')}>Assess residual risk</Button>}
        {a('review') && <Button variant="outlined" onClick={() => setDlg('review')}>Record review</Button>}
        {a('close') && <Button onClick={() => setDlg('close')}>Close</Button>}
        {a('reopen') && <Button variant="outlined" color="warning" onClick={() => setDlg('reopen')}>Reopen</Button>}
        {a('cancel') && <Button color="error" onClick={() => setDlg('cancel')}>Cancel</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {d.escalated && d.status < 4 && <Alert severity="error" sx={{ mb: 2 }}>Escalated: the rating falls in a band that requires management attention.</Alert>}
      {d.status === 2 && <Alert severity="info" sx={{ mb: 2 }}>The residual risk is waiting for acceptance approval (see Approvals in My work).</Alert>}
      {d.status < 4 && d.reviewDueOn < localDate() && <Alert severity="warning" sx={{ mb: 2 }}>The periodic review was due {fmtDate(d.reviewDueOn)}.</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: '290px 1fr', gap: 3 }}>
        <Facts items={[['Type', kinds[d.kind]], ['Category', d.category], ['Source', d.source ?? '—'], ['Process', d.process ?? '—'], ['Product', d.productName ?? '—'], ['Owner', d.ownerName], ['Site', d.siteCode],
          ['Initial', <BandChip settings={s} label={d.initialBand} score={d.initialScore} />], ['Residual', <BandChip settings={s} label={d.residualBand} score={d.residualScore} />],
          ['Accepted', d.acceptedAt ? `${d.acceptedByName ?? ''} ${fmtDateTime(d.acceptedAt)}` : '—'], ['Review every', `${d.reviewIntervalDays} days`], ['Last review', fmtDate(d.lastReviewedOn)], ['Next review', fmtDate(d.reviewDueOn)]]} />
        <Box sx={{ minWidth: 0 }}>
          {tab === 'assessment' && <Stack gap={2}>
            <Section title="Description"><Prose label="Description" text={d.description} /><Prose label="Cause" text={d.cause} /><Prose label="Consequence" text={d.consequence} /></Section>
            <Section title="Initial assessment">
              <Facts items={[['Probability', lbl(s.probability, d.probability)], ['Severity', lbl(s.severity, d.severity)], ...(s.useDetectability ? [['Detectability', lbl(s.detectability, d.detectability)] as [string, string]] : []), ['Score', <BandChip settings={s} label={d.initialBand} score={d.initialScore} />]]} />
              <Prose label="Existing controls" text={d.existingControls} /><Prose label="Mitigation plan" text={d.mitigationPlan} /><Prose label="Monitoring" text={d.monitoring} />
              {d.status === 0 && !d.mitigationPlan && <Alert severity="info">Use “Edit controls and plan” to add a mitigation plan before starting mitigation.</Alert>}
            </Section>
            <Section title="Residual assessment">{d.residualScore == null ? <Alert severity="info">Assess residual risk after the mitigation actions are complete.</Alert> : <>
              <Facts items={[['Probability', lbl(s.probability, d.residualProbability)], ['Severity', lbl(s.severity, d.residualSeverity)], ...(s.useDetectability ? [['Detectability', lbl(s.detectability, d.residualDetectability)] as [string, string]] : []), ['Score', <BandChip settings={s} label={d.residualBand} score={d.residualScore} />]]} />
              <Prose label="Acceptance criteria" text={d.acceptanceCriteria} /></>}</Section>
            {d.closureNotes && <Section title="Closure"><Prose label={`Closed ${fmtDateTime(d.closedAt)}`} text={d.closureNotes} /></Section>}
          </Stack>}
          {tab === 'reviews' && <Section title="Periodic reviews">{d.reviews.length === 0 ? <Typography color="text.secondary" fontSize={14}>No reviews yet.</Typography> :
            <Table size="small"><TableHead><TableRow><TableCell>Date</TableCell><TableCell>Reviewer</TableCell><TableCell>Rating</TableCell><TableCell>Controls effective</TableCell><TableCell>Notes</TableCell></TableRow></TableHead>
              <TableBody>{d.reviews.map((r) => <TableRow key={r.id}><TableCell>{fmtDate(r.reviewedOn)}</TableCell><TableCell>{r.reviewedByName}</TableCell><TableCell><BandChip settings={s} label={r.band} score={r.score} /></TableCell><TableCell>{r.stillEffective ? 'Yes' : 'No'}</TableCell><TableCell sx={{ whiteSpace: 'pre-wrap' }}>{r.notes}</TableCell></TableRow>)}</TableBody></Table>}</Section>}
          {tab === 'actions' && <ActionsPanel type="Risk" id={d.id} canEdit={a('actions')} canVerifyAs={a('edit')} onChanged={refresh} />}
          {tab === 'more' && <AttachmentsAndHistory type="Risk" id={d.id} canEdit={a('edit')} />}
        </Box>
      </Box>
      <StepDialog open={dlg === 'residual'} title="Residual risk" intro="Rate the risk with the mitigation in place. Bands that require acceptance go to an approver." confirm="Save" onClose={() => setDlg(null)}
        inputs={[...ratingInputs(d.residualProbability, d.residualSeverity, d.residualDetectability), { name: 'acceptanceCriteria', label: 'Acceptance criteria', type: 'multiline', initial: d.acceptanceCriteria ?? '' }]}
        onConfirm={(v) => run(() => post(`/api/risks/${d.id}/residual`, { probability: n(v.probability), severity: n(v.severity), detectability: n(v.detectability), acceptanceCriteria: v.acceptanceCriteria }))} />
      <StepDialog open={dlg === 'review'} title="Periodic review" intro="Leave the rating blank to keep the current rating." confirm="Record review" onClose={() => setDlg(null)}
        inputs={[{ name: 'notes', label: 'Review notes (monitoring results, changes)', type: 'multiline' }, { name: 'stillEffective', label: 'Controls are still effective', type: 'checkbox', initial: 'true' }, ...ratingInputs(null, null, null)]}
        onConfirm={(v) => run(() => post(`/api/risks/${d.id}/reviews`, { notes: v.notes, stillEffective: v.stillEffective === 'true', probability: n(v.probability), severity: n(v.severity), detectability: n(v.detectability) }))} />
      <StepDialog open={dlg === 'edit'} title="Controls, mitigation and monitoring" confirm="Save" onClose={() => setDlg(null)}
        inputs={[{ name: 'existingControls', label: 'Existing controls', type: 'multiline', initial: d.existingControls ?? '' }, { name: 'mitigationPlan', label: 'Mitigation plan', type: 'multiline', initial: d.mitigationPlan ?? '' },
          { name: 'monitoring', label: 'Monitoring', type: 'multiline', initial: d.monitoring ?? '' }, { name: 'reviewIntervalDays', label: 'Review interval (days)', initial: String(d.reviewIntervalDays) }]}
        onConfirm={(v) => run(() => put(`/api/risks/${d.id}`, { kind: d.kind, category: d.category, source: d.source, title: d.title, description: d.description, cause: d.cause, consequence: d.consequence, process: d.process, productId: d.productId,
          siteId: d.siteId, departmentId: d.departmentId, ownerId: d.ownerId, probability: d.probability, severity: d.severity, detectability: d.detectability, acceptanceCriteria: d.acceptanceCriteria,
          existingControls: v.existingControls || null, mitigationPlan: v.mitigationPlan || null, monitoring: v.monitoring || null, reviewIntervalDays: Number(v.reviewIntervalDays), rowVersion: d.rowVersion }))} />
      <StepDialog open={dlg === 'close'} title={`Close ${d.number}`} confirm="Close" onClose={() => setDlg(null)} inputs={[{ name: 'text', label: 'Closure notes', type: 'multiline' }]} onConfirm={(v) => run(() => post(`/api/risks/${d.id}/close`, v))} />
      <StepDialog open={dlg === 'reopen'} title={`Reopen ${d.number}`} confirm="Reopen" onClose={() => setDlg(null)} inputs={[{ name: 'text', label: 'Reason', type: 'multiline' }]} onConfirm={(v) => run(() => post(`/api/risks/${d.id}/reopen`, v))} />
      <StepDialog open={dlg === 'cancel'} title={`Cancel ${d.number}`} confirm="Cancel risk" danger onClose={() => setDlg(null)} inputs={[{ name: 'text', label: 'Reason', type: 'multiline' }]} onConfirm={(v) => run(() => post(`/api/risks/${d.id}/cancel`, v))} />
    </ModuleWorkspace>
  );
}

/** Administrator editor for the risk matrix: scales, detectability, bands (colors, acceptance, escalation) and categories. */
export function RiskSettingsPage() {
  const qc = useQueryClient();
  const q = useSettings();
  const [s, setS] = useState<RiskSettings | null>(null);
  const [saved, setSaved] = useState(false);
  const { error, run } = useRunner(() => { qc.invalidateQueries({ queryKey: ['risk-settings'] }); setSaved(true); });
  useEffect(() => { if (q.data) setS(structuredClone(q.data)); }, [q.data]);
  if (!s) return <ModuleWorkspace module={mod()} crumbs={[{ label: 'Risks', to: '/risks' }, { label: 'Matrix settings' }]} title="Risk matrix settings">{null}</ModuleWorkspace>;
  const upd = (f: (x: RiskSettings) => void) => { const c = structuredClone(s); f(c); setS(c); setSaved(false); };
  const Scale = ({ k, title }: { k: 'probability' | 'severity' | 'detectability'; title: string }) => (
    <Section title={title} action={<Button size="small" onClick={() => upd((c) => { c[k].push({ value: (c[k].at(-1)?.value ?? 0) + 1, label: '' }); })}>Add level</Button>}>
      {s[k].map((l, i) => <Stack key={i} direction="row" gap={1} sx={{ mb: 1 }}>
        <TextField size="small" type="number" label="Value" value={l.value} sx={{ width: 90 }} onChange={(e) => upd((c) => { c[k][i].value = Number(e.target.value); })} />
        <TextField size="small" label="Label" value={l.label} sx={{ flex: 1 }} onChange={(e) => upd((c) => { c[k][i].label = e.target.value; })} />
        <IconButton aria-label="Remove level" onClick={() => upd((c) => { c[k].splice(i, 1); })}><DeleteOutline fontSize="small" /></IconButton>
      </Stack>)}
    </Section>);
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Risks', to: '/risks' }, { label: 'Matrix settings' }]} title="Risk matrix settings"
      actions={<Button variant="contained" onClick={() => run(() => put('/api/risks/settings', s))}>Save settings</Button>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {saved && <Alert severity="success" sx={{ mb: 2 }}>Saved. New ratings use these settings; existing ratings keep their recorded score until re-rated.</Alert>}
      <Alert severity="info" sx={{ mb: 2 }}>Score = probability × severity{s.useDetectability ? ' × detectability' : ''}. The band is the one with the highest minimum score that the score reaches.</Alert>
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 2, mb: 2 }}>
        <Scale k="probability" title="Probability scale" /><Scale k="severity" title="Severity scale" />
        <Box><FormControlLabel control={<Checkbox checked={s.useDetectability} onChange={(e) => upd((c) => { c.useDetectability = e.target.checked; })} />} label="Use detectability (RPN style)" />{s.useDetectability && <Scale k="detectability" title="Detectability scale" />}</Box>
      </Box>
      <Section title="Score bands" action={<Button size="small" onClick={() => upd((c) => { c.bands.push({ minScore: (c.bands.at(-1)?.minScore ?? 0) + 5, label: '', color: '#607d8b', requiresAcceptance: false, escalate: false }); })}>Add band</Button>}>
        <Table size="small"><TableHead><TableRow><TableCell>Minimum score</TableCell><TableCell>Label</TableCell><TableCell>Color</TableCell><TableCell>Residual needs acceptance</TableCell><TableCell>Escalate</TableCell><TableCell /></TableRow></TableHead>
          <TableBody>{s.bands.map((b, i) => <TableRow key={i}>
            <TableCell><TextField size="small" type="number" value={b.minScore} sx={{ width: 90 }} slotProps={{ htmlInput: { 'aria-label': `Band ${i + 1} minimum score` } }} onChange={(e) => upd((c) => { c.bands[i].minScore = Number(e.target.value); })} /></TableCell>
            <TableCell><TextField size="small" value={b.label} slotProps={{ htmlInput: { 'aria-label': `Band ${i + 1} label` } }} onChange={(e) => upd((c) => { c.bands[i].label = e.target.value; })} /></TableCell>
            <TableCell><Stack direction="row" gap={1} alignItems="center"><input type="color" aria-label={`Color for ${b.label}`} value={b.color} onChange={(e) => upd((c) => { c.bands[i].color = e.target.value; })} /><span style={{ fontFamily: mono, fontSize: 12 }}>{b.color}</span></Stack></TableCell>
            <TableCell><Checkbox slotProps={{ input: { 'aria-label': `Band ${i + 1} residual needs acceptance` } }} checked={b.requiresAcceptance} onChange={(e) => upd((c) => { c.bands[i].requiresAcceptance = e.target.checked; })} /></TableCell>
            <TableCell><Checkbox slotProps={{ input: { 'aria-label': `Band ${i + 1} escalate` } }} checked={b.escalate} onChange={(e) => upd((c) => { c.bands[i].escalate = e.target.checked; })} /></TableCell>
            <TableCell><IconButton aria-label="Remove band" onClick={() => upd((c) => { c.bands.splice(i, 1); })}><DeleteOutline fontSize="small" /></IconButton></TableCell>
          </TableRow>)}</TableBody></Table>
      </Section>
      <Box sx={{ mt: 2 }}><Section title="Categories">
        <TextField fullWidth multiline minRows={4} label="One category per line" value={s.categories.join('\n')} onChange={(e) => upd((c) => { c.categories = e.target.value.split('\n'); })}
          onBlur={() => upd((c) => { c.categories = c.categories.map((x) => x.trim()).filter(Boolean); })} />
      </Section></Box>
    </ModuleWorkspace>
  );
}
