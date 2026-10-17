import { useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Chip, LinearProgress, Paper, Stack, Table, TableBody, TableCell, TableRow, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { z } from 'zod';
import { get, post, put } from '../api';
import { useAuth } from '../auth';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, fmtDate } from '../components/Status';
import { Facts, SchemaForm, Section, StepDialog, useRunner } from '../components/Kit';
import { mono } from '../theme';
import { Loading, Tile, mod, num, rag } from './Enterprise';

/* eslint-disable @typescript-eslint/no-explicit-any */
const projectKinds = ['New product launch', 'Product transfer', 'Supplier transfer', 'Manufacturing transfer'];
const deliverableStatus = ['Open', 'Done', 'NotApplicable'];
const ppapStatus = ['Draft', 'Submitted', 'Approved', 'InterimApproved', 'Rejected'];
const ragColor: Record<string, string> = { Green: 'success.main', Yellow: 'warning.main', Red: 'error.main' };

export function ProjectList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const q = useQuery({ queryKey: ['launch-projects'], queryFn: () => get<any[]>('/api/launch/projects') });
  return (
    <ModuleWorkspace module={mod('launch')} crumbs={[{ label: 'Launch' }]} title="Launch and transfer projects"
      actions={can('apqp.edit') && <Button variant="contained" onClick={() => nav('/launch/new')}>New project</Button>}>
      <DataTable data={q.data ?? []} onRowClick={(r) => nav(`/launch/${r.id}`)} empty="No launch projects."
        columns={[{ header: 'Number', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' }, { header: 'Kind', accessorKey: 'kind', cell: (x) => projectKinds[x.getValue()] },
          { header: 'Phase', accessorKey: 'currentPhase', cell: (x) => `${x.getValue()} / 6` }, { header: 'Target launch', accessorKey: 'targetLaunchOn', cell: (x) => fmtDate(x.getValue()) }]} />
    </ModuleWorkspace>
  );
}

export function ProjectNew() {
  const nav = useNavigate();
  const schema = z.object({ title: z.string().min(1, 'Required'), kind: z.coerce.number(), siteId: z.string().min(1, 'Required'), productId: z.string().optional(), partId: z.string().optional(), customerId: z.string().optional(), supplierId: z.string().optional(), targetLaunchOn: z.string().optional() });
  return (
    <ModuleWorkspace module={mod('launch')} crumbs={[{ label: 'Launch', to: '/launch' }, { label: 'New' }]} title="New launch or transfer project">
      <SchemaForm schema={schema} defaults={{ title: '', kind: 0, siteId: '', productId: '', partId: '', customerId: '', supplierId: '', targetLaunchOn: '' }} submitLabel="Create project"
        fields={[{ name: 'title', label: 'Title', full: true }, { name: 'kind', label: 'Kind', type: 'select', options: projectKinds.map((k, i) => [i, k]) }, { name: 'siteId', label: 'Site', type: 'site' },
          { name: 'productId', label: 'Product', type: 'lookup', lookup: 'product' }, { name: 'partId', label: 'Part', type: 'lookup', lookup: 'part' }, { name: 'customerId', label: 'Customer', type: 'lookup', lookup: 'customer' },
          { name: 'supplierId', label: 'New supplier (supplier transfer)', type: 'lookup', lookup: 'supplier' }, { name: 'targetLaunchOn', label: 'Target launch', type: 'date' }]}
        onSubmit={async (v) => { const n = (x?: string) => x || null; const r = await post<{ id: string }>('/api/launch/projects', { ...v, productId: n(v.productId), partId: n(v.partId), customerId: n(v.customerId), supplierId: n(v.supplierId), targetLaunchOn: n(v.targetLaunchOn) }); nav(`/launch/${r.id}`); }} onCancel={() => nav(-1)} />
    </ModuleWorkspace>
  );
}

export function ProjectDetail() {
  const { id } = useParams();
  const nav = useNavigate();
  const qc = useQueryClient();
  const q = useQuery({ queryKey: ['launch-project', id], queryFn: () => get<any>(`/api/launch/projects/${id}`) });
  const [phase, setPhase] = useState<number | null>(null);
  const [gate, setGate] = useState(false);
  const r = useRunner(() => qc.invalidateQueries({ queryKey: ['launch-project', id] }));
  if (!q.data) return <Loading m="launch" crumbs={[{ label: 'Launch', to: '/launch' }]} q={q} />;
  const p = q.data.project; const rd = q.data.readiness;
  const ph = phase ?? p.currentPhase;
  const setStatus = (d: any, status: number) => r.run(() => put(`/api/launch/projects/${id}/deliverables/${d.id}`, { status, linkedType: null, linkedId: null }));
  return (
    <ModuleWorkspace module={mod('launch')} crumbs={[{ label: 'Launch', to: '/launch' }, { label: p.number }]} title={`${p.number} — ${p.title}`}
      actions={<Stack direction="row" gap={1}><Button onClick={() => nav(`/launch/ppap/new?project=${id}&part=${p.partId ?? ''}&site=${p.siteId}`)}>Start PPAP</Button><Button variant="contained" onClick={() => setGate(true)}>Phase {p.currentPhase} gate review</Button></Stack>}>
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'repeat(2, 1fr)', md: 'repeat(6, 1fr)' }, gap: 1, mb: 2 }}>
        {rd.byPhase.map((b: any) => (
          <Paper key={b.phase} data-testid="phase" onClick={() => setPhase(b.phase)} sx={{ p: 1.25, cursor: 'pointer', outline: b.phase === ph ? '2px solid' : 'none', outlineColor: 'primary.main', opacity: b.phase > p.currentPhase ? 0.6 : 1 }}>
            <Typography variant="caption" color="text.secondary">Phase {b.phase}{b.phase === p.currentPhase ? ' · current' : ''}</Typography>
            <Typography variant="body2" sx={{ fontWeight: 600, minHeight: 40 }}>{b.name}</Typography>
            <LinearProgress variant="determinate" value={Number(b.pct)} sx={{ my: 0.5 }} /><Typography variant="caption">{b.done}/{b.total}{b.overdue ? ` · ${b.overdue} overdue` : ''}</Typography>
          </Paper>))}
      </Box>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 280px' }, gap: 2 }}>
        <Section title={`Phase ${ph} deliverables`}>
          <Table size="small"><TableBody>{q.data.deliverables.filter((d: any) => d.phase === ph).map((d: any) => (
            <TableRow key={d.id}><TableCell>{d.name}</TableCell><TableCell>{d.linkedType ? <Chip size="small" label={d.linkedType} onClick={() => nav(`/records/${d.linkedType}/${d.linkedId}`)} /> : null}</TableCell>
              <TableCell><StatusChip status={deliverableStatus[d.status]} /></TableCell>
              <TableCell align="right">{d.status === 0 ? <><Button size="small" onClick={() => setStatus(d, 1)}>Done</Button><Button size="small" onClick={() => setStatus(d, 2)}>N/A</Button></>
                : <Button size="small" onClick={() => setStatus(d, 0)}>Reopen</Button>}</TableCell></TableRow>))}</TableBody></Table>
        </Section>
        <Stack gap={2}>
          <Paper sx={{ p: 2, borderLeft: 6, borderColor: ragColor[rd.rating] }}><Typography variant="caption" color="text.secondary">Launch readiness</Typography><Typography sx={{ fontSize: 28, fontFamily: mono, fontWeight: 700 }}>{rd.overallPct}%</Typography><Typography variant="body2">{rd.rating}</Typography></Paper>
          <Facts items={[['Kind', q.data.kind], ['Target launch', fmtDate(p.targetLaunchOn)], ['PPAP', q.data.ppaps.map((x: any) => `${x.number} (${x.status})`).join(', ') || '—'],
            ['Gate history', q.data.gates.map((g: any) => `Phase ${g.phase}: ${['Go', 'Conditional', 'No-go'][g.decision]} ${fmtDate(g.decidedAt)}`).join('\n') || '—']]} />
        </Stack>
      </Box>
      <StepDialog open={gate} onClose={() => setGate(false)} title={`Phase ${p.currentPhase} gate review`} confirm="Sign decision" intro="Go requires every deliverable of the phase to be done or not applicable. Signing re-enters your password."
        inputs={[{ name: 'decision', label: 'Decision', type: 'select', options: [[0, 'Go'], [1, 'Conditional go'], [2, 'No-go']], initial: '0' }, { name: 'notes', label: 'Notes', type: 'multiline' }, { name: 'password', label: 'Password', type: 'password' }]}
        onConfirm={async (v) => { await post(`/api/launch/projects/${id}/gate`, { decision: Number(v.decision), password: v.password, notes: v.notes || null }); qc.invalidateQueries({ queryKey: ['launch-project', id] }); }} />
    </ModuleWorkspace>
  );
}

export function PpapList() {
  const nav = useNavigate();
  const q = useQuery({ queryKey: ['ppaps'], queryFn: () => get<any[]>('/api/launch/ppap') });
  return (
    <ModuleWorkspace module={mod('launch')} crumbs={[{ label: 'Launch', to: '/launch' }, { label: 'PPAP' }]} title="PPAP submissions" actions={<Button variant="contained" onClick={() => nav('/launch/ppap/new')}>New submission</Button>}>
      <DataTable data={q.data ?? []} onRowClick={(r) => nav(`/launch/ppap/${r.id}`)} empty="No PPAP submissions."
        columns={[{ header: 'Number', accessorKey: 'number', cell: num }, { header: 'Level', accessorKey: 'level' }, { header: 'Reason', accessorKey: 'reason' }, { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={ppapStatus[x.getValue()]} /> }, { header: 'Submitted', accessorKey: 'submittedAt', cell: (x) => fmtDate(x.getValue()) }]} />
    </ModuleWorkspace>
  );
}

export function PpapNew() {
  const nav = useNavigate();
  const [p] = useSearchParams();
  const schema = z.object({ partId: z.string().min(1, 'Select the part'), customerId: z.string().optional(), level: z.coerce.number(), reason: z.string().min(1, 'Required'), siteId: z.string().min(1, 'Required'), partRevision: z.string().optional() });
  return (
    <ModuleWorkspace module={mod('launch')} crumbs={[{ label: 'PPAP', to: '/launch/ppap' }, { label: 'New' }]} title="New PPAP submission">
      <SchemaForm schema={schema} defaults={{ partId: p.get('part') ?? '', customerId: '', level: 3, reason: 'Initial submission', siteId: p.get('site') ?? '', partRevision: '' }} submitLabel="Create"
        fields={[{ name: 'partId', label: 'Part', type: 'lookup', lookup: 'part' }, { name: 'partRevision', label: 'Part revision' }, { name: 'customerId', label: 'Customer', type: 'lookup', lookup: 'customer' },
          { name: 'level', label: 'Submission level', type: 'select', options: [1, 2, 3, 4, 5].map((l) => [l, `Level ${l}`]) }, { name: 'reason', label: 'Reason for submission' }, { name: 'siteId', label: 'Site', type: 'site' }]}
        onSubmit={async (v) => { const r = await post<{ id: string }>('/api/launch/ppap', { ...v, customerId: v.customerId || null, projectId: p.get('project') || null }); nav(`/launch/ppap/${r.id}`); }} onCancel={() => nav(-1)} />
    </ModuleWorkspace>
  );
}

const reqLabel: Record<string, string> = { Submit: 'Submit to customer', Retain: 'Retain on file', None: 'Not required', SubmitOnRequest: 'Submit on request' };
const codeColor: Record<string, 'error' | 'default' | 'warning'> = { S: 'error', R: 'default', '*': 'warning' };

/** AIAG PPAP 4th Edition Table 4.2 codes: legend plus the full level matrix with the submission's level highlighted. */
function PpapMatrixPanel({ matrix, legend, level, verification }: { matrix: any[]; legend: any[]; level: number; verification?: any }) {
  const [open, setOpen] = useState(false);
  return (
    <Paper variant="outlined" sx={{ p: 1.5, mb: 1.5 }} data-testid="ppap-legend">
      <Stack direction="row" gap={2} alignItems="center" flexWrap="wrap">
        <Typography variant="caption" sx={{ fontWeight: 700 }}>Codes</Typography>
        {legend.map((l) => <Stack key={l.code} direction="row" gap={0.75} alignItems="center"><Chip size="small" label={l.code} color={codeColor[l.code]} sx={{ fontFamily: mono, minWidth: 32 }} /><Typography variant="caption">{l.meaning}</Typography></Stack>)}
        <Box sx={{ flex: 1 }} /><Chip size="small" variant="outlined" data-testid="ppap-matrix-verification" color={verification?.verified ? 'success' : 'warning'} title={verification?.label} label={verification?.verified ? 'Matrix verified' : 'Matrix not verified'} /><Button size="small" onClick={() => setOpen(!open)}>{open ? 'Hide level matrix' : 'Show level matrix'}</Button>
      </Stack>
      {open && <Table size="small" sx={{ mt: 1 }} data-testid="ppap-matrix">
        <TableBody>
          <TableRow><TableCell sx={{ fontWeight: 700 }}>Element</TableCell>{[1, 2, 3, 4, 5].map((n) => <TableCell key={n} align="center" sx={{ fontWeight: 700, bgcolor: n === level ? 'action.selected' : undefined }}>L{n}</TableCell>)}</TableRow>
          {matrix.map((m) => (
            <TableRow key={m.key}>
              <TableCell>{m.elementCode ?? '—'} · {m.name}{m.condition ? <Typography component="span" variant="caption" color="text.secondary"> ({m.condition.toLowerCase()})</Typography> : null}</TableCell>
              {m.levels.map((c: string, i: number) => <TableCell key={i} align="center" sx={{ fontFamily: mono, fontWeight: i + 1 === level ? 700 : 400, bgcolor: i + 1 === level ? 'action.selected' : undefined }}>{c}</TableCell>)}
            </TableRow>))}
        </TableBody>
      </Table>}
      {open && <Typography variant="caption" color="text.secondary">Source: AIAG PPAP 4th Edition, Table 4.2 Retention/Submission Requirements. The part submission warrant is always needed before submitting; level 5 is reviewed at your site.</Typography>}
    </Paper>
  );
}

export function PpapDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const q = useQuery({ queryKey: ['ppap', id], queryFn: () => get<any>(`/api/launch/ppap/${id}`) });
  const [el, setEl] = useState<any>(null);
  const [dec, setDec] = useState(false);
  const r = useRunner(() => qc.invalidateQueries({ queryKey: ['ppap', id] }));
  if (!q.data) return <Loading m="launch" crumbs={[{ label: 'PPAP', to: '/launch/ppap' }]} q={q} />;
  const s = q.data.submission; const draft = q.data.status === 'Draft' || q.data.status === 'Rejected';
  const ready = q.data.elements.filter((e: any) => e.status !== 'Missing').length;
  return (
    <ModuleWorkspace module={mod('launch')} crumbs={[{ label: 'PPAP', to: '/launch/ppap' }, { label: s.number }]} title={`${s.number} — Level ${s.level}`}
      actions={<Stack direction="row" gap={1} alignItems="center"><StatusChip status={q.data.status} />{draft && <Button variant="contained" onClick={() => r.run(() => post(`/api/launch/ppap/${id}/submit`))}>Submit to customer</Button>}
        {q.data.status === 'Submitted' && <Button variant="contained" onClick={() => setDec(true)}>Record customer decision</Button>}</Stack>}>
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      <LinearProgress variant="determinate" value={(100 * ready) / Math.max(1, q.data.elements.length)} sx={{ height: 8, borderRadius: 1, mb: 1 }} /><Typography variant="caption">{ready} of {q.data.elements.length} elements ready or not applicable</Typography>
      <Box sx={{ mt: 1.5 }}><PpapMatrixPanel matrix={q.data.matrix ?? []} legend={q.data.legend ?? []} level={s.level} verification={q.data.matrixVerification} /></Box>
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(230px, 1fr))', gap: 1.25 }}>
        {q.data.elements.map((e: any) => (
          <Paper key={e.code} data-testid="ppap-element" onClick={() => draft && setEl(e)} sx={{ p: 1.5, cursor: draft ? 'pointer' : undefined, borderLeft: 4, borderColor: e.status === 'Ready' ? 'success.main' : e.status === 'NotApplicable' ? 'grey.400' : e.requirement === 'Submit' ? 'error.main' : 'warning.main' }}>
            <Stack direction="row" gap={0.75} alignItems="center"><Chip size="small" label={e.requirementCode} color={codeColor[e.requirementCode]} sx={{ fontFamily: mono, minWidth: 32 }} data-testid="ppap-code" /><Typography variant="caption" color="text.secondary">{e.code} · {reqLabel[e.requirement] ?? e.requirement}</Typography></Stack>
            <Typography variant="body2" sx={{ fontWeight: 600 }}>{e.name}{e.condition ? <Typography component="span" variant="caption" color="text.secondary"> ({e.condition.toLowerCase()})</Typography> : null}</Typography>
            <Typography variant="caption">{e.status}{e.linkedType ? ` · ${e.linkedType}` : ''}</Typography>
          </Paper>))}
      </Box>
      <StepDialog open={!!el} onClose={() => setEl(null)} title={el ? `${el.code} ${el.name}` : ''} confirm="Save" intro="Describe the evidence."
        inputs={[{ name: 'status', label: 'Status', type: 'select', options: [[1, 'Ready'], [2, 'Not applicable'], [0, 'Missing']], initial: '1' }, { name: 'notes', label: 'Evidence / notes', type: 'multiline' }]}
        onConfirm={async (v) => { await put(`/api/launch/ppap/${id}/elements/${el.code}`, { status: Number(v.status), linkedType: null, linkedId: null, notes: v.notes || null }); qc.invalidateQueries({ queryKey: ['ppap', id] }); }} />
      <StepDialog open={dec} onClose={() => setDec(false)} title="Customer decision" confirm="Sign" intro="Approval completes the project's PPAP deliverable."
        inputs={[{ name: 'decision', label: 'Decision', type: 'select', options: [[2, 'Approved'], [3, 'Interim approval'], [4, 'Rejected']], initial: '2' }, { name: 'customer', label: 'Customer representative' }, { name: 'on', label: 'Decided on', type: 'date' }, { name: 'comments', label: 'Comments', type: 'multiline' }, { name: 'password', label: 'Your password', type: 'password' }]}
        onConfirm={async (v) => { await post(`/api/launch/ppap/${id}/customer-decision`, { decision: Number(v.decision), customerName: v.customer, decidedOn: v.on, comments: v.comments || null, password: v.password }); qc.invalidateQueries({ queryKey: ['ppap', id] }); }} />
    </ModuleWorkspace>
  );
}

export function CapabilityStudies() {
  const [values, setValues] = useState('25.000 25.004 24.998 25.002 24.996 25.001 25.003 24.999 25.000 25.002 24.997 25.001');
  const [lsl, setLsl] = useState('24.97'); const [usl, setUsl] = useState('25.03'); const [n, setN] = useState('1');
  const [res, setRes] = useState<any>(null); const [err, setErr] = useState<string | null>(null);
  const list = useQuery({ queryKey: ['cap-studies'], queryFn: () => get<any[]>('/api/launch/capability') });
  const calc = async () => {
    setErr(null);
    try { setRes(await post('/api/launch/capability/calculate', { title: 'calc', characteristic: 'calc', siteId: '00000000-0000-0000-0000-000000000000', lsl: lsl ? Number(lsl) : null, usl: usl ? Number(usl) : null, subgroupSize: Number(n), values: values.split(/[\s,;]+/).filter(Boolean).map(Number) })); } catch (e) { setErr((e as Error).message); }
  };
  const idx = (v: number | null) => (v == null ? '—' : Number(v).toFixed(2));
  return (
    <ModuleWorkspace module={mod('launch')} crumbs={[{ label: 'Launch', to: '/launch' }, { label: 'Capability' }]} title="Process capability">
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 320px' }, gap: 2, mb: 2 }}>
        <Section title="Calculator">
          <Stack direction="row" gap={1.5} sx={{ mb: 1.5 }}><TextField size="small" label="LSL" value={lsl} onChange={(e) => setLsl(e.target.value)} /><TextField size="small" label="USL" value={usl} onChange={(e) => setUsl(e.target.value)} /><TextField size="small" label="Subgroup size" value={n} onChange={(e) => setN(e.target.value)} /></Stack>
          <TextField fullWidth multiline minRows={4} label="Measurements" value={values} onChange={(e) => setValues(e.target.value)} />
          <Button sx={{ mt: 1.5 }} variant="contained" onClick={calc}>Calculate</Button>{err && <Alert severity="error" sx={{ mt: 1.5 }}>{err}</Alert>}
        </Section>
        <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 1.25, alignContent: 'start' }}>
          {res ? ([['Cp', res.cp], ['Cpk', res.cpk], ['Pp', res.pp], ['Ppk', res.ppk]] as [string, number | null][]).map(([l, v]) => <Tile key={l} label={l} value={idx(v)} color={v == null ? undefined : rag(v >= 1.67, v >= 1.33)} />) : <Typography variant="body2" color="text.secondary">Enter data and calculate.</Typography>}
        </Box>
      </Box>
      <DataTable data={list.data ?? []} empty="No saved studies." columns={[{ header: 'Number', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' }, { header: 'Characteristic', accessorKey: 'characteristic' }, { header: 'Cpk', accessorKey: 'cpk', cell: (x) => idx(x.getValue()) }, { header: 'Ppk', accessorKey: 'ppk', cell: (x) => idx(x.getValue()) }]} />
    </ModuleWorkspace>
  );
}
