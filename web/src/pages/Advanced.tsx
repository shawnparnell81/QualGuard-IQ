import { useMemo, useState } from 'react';
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Checkbox, FormControlLabel, MenuItem, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { z } from 'zod';
import { get, post, put } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, fmtDate, fmtDateTime, localDate } from '../components/Status';
import { Facts, Readout, SchemaForm, Section, StepDialog, useRunner, useUsers } from '../components/Kit';
import { mono, useChartColors } from '../theme';
import { ImportHistory, LineImportDialog } from '../components/LineImport';

const mod = (k: string) => modules.find((m) => m.key === k)!;
const num = (x: { getValue: () => unknown }) => <span style={{ fontFamily: mono }}>{String(x.getValue() ?? '')}</span>;
/** Blank optional fields become null so the API sees "not set" rather than an empty string. */
const clean = (v: Record<string, unknown>) => Object.fromEntries(Object.entries(v).map(([k, x]) => [k, x === '' ? null : x]));
const money = (n?: number | null) => (n == null ? '—' : n.toLocaleString(undefined, { style: 'currency', currency: 'USD', maximumFractionDigits: 0 }));
const pct = (n?: number | null) => (n == null ? '—' : `${n.toFixed(1)}%`);

// ---------------------------------------------------------------- APQP
export const apqpStatus = ['Draft', 'PendingApproval', 'Released', 'Superseded', 'Obsolete'];
const priorities = ['Low', 'Medium', 'High'];
const phases = ['Prototype', 'Pre-launch', 'Production', 'Supplier'];
type Kind = 'flows' | 'pfmea' | 'plans';
const kindLabel: Record<Kind, string> = { flows: 'Process flow', pfmea: 'PFMEA', plans: 'Control plan' };
interface ApqpDoc { id: string; number: string; revision: number; title: string; status: number; productId?: string; partId?: string; partRevision?: string; changeReason?: string; releasedAt?: string; siteId: string; phase?: number; pfmeaId?: string; processFlowId?: string }
interface ApqpRow { document: ApqpDoc; ownerName?: string; productName?: string; partNumber?: string }

export function ApqpList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const kind = (p.get('kind') as Kind) ?? 'plans';
  const all = p.get('all') === '1';
  const q = useQuery({ queryKey: ['apqp', kind, all], queryFn: () => get<ApqpRow[]>(`/api/apqp/${kind}?currentOnly=${!all}`) });
  const cols: ColumnDef<ApqpRow, any>[] = [
    { header: 'Number', accessorFn: (r) => r.document.number, cell: num }, { header: 'Rev', accessorFn: (r) => r.document.revision },
    { header: 'Title', accessorFn: (r) => r.document.title }, { header: 'Product', accessorFn: (r) => r.productName ?? '—' }, { header: 'Part', accessorFn: (r) => r.partNumber ?? '—' },
    { header: 'Status', accessorFn: (r) => r.document.status, cell: (x) => <StatusChip status={apqpStatus[x.getValue()]} /> },
    { header: 'Released', accessorFn: (r) => r.document.releasedAt, cell: (x) => fmtDate(x.getValue()) }, { header: 'Owner', accessorFn: (r) => r.ownerName ?? '' },
  ];
  return (
    <ModuleWorkspace module={mod('apqp')} crumbs={[{ label: 'Planning' }]} title="Process planning documents"
      tabs={[{ label: 'Control plans', value: 'plans' }, { label: 'PFMEA', value: 'pfmea' }, { label: 'Process flows', value: 'flows' }]} tab={kind} onTab={(v) => setP({ kind: v })}
      actions={<Stack direction="row" gap={1}>
        <FormControlLabel control={<Checkbox checked={all} onChange={(e) => setP(e.target.checked ? { kind, all: '1' } : { kind })} />} label="Include superseded" />
        {can('apqp.edit') && <Button variant="contained" component={RouterLink} to={`/apqp/new?kind=${kind}`}>New {kindLabel[kind].toLowerCase()}</Button>}</Stack>}>
      <DataTable data={q.data ?? []} columns={cols} onRowClick={(r) => nav(`/apqp/${kind}/${r.document.id}`)} empty={`No ${kindLabel[kind].toLowerCase()} documents yet.`} />
    </ModuleWorkspace>
  );
}

export function ApqpNew() {
  const nav = useNavigate();
  const [p] = useSearchParams();
  const kind = (p.get('kind') as Kind) ?? 'plans';
  const schema = z.object({ title: z.string().trim().min(1, 'Required').max(300), siteId: z.string().uuid('Choose a site'), departmentId: z.string().optional(), productId: z.string().optional(), partId: z.string().optional(),
    partRevision: z.string().max(20).optional(), ownerId: z.string().optional(), phase: z.coerce.number().int(), productFamily: z.string().max(200).optional() });
  return (
    <ModuleWorkspace module={mod('apqp')} crumbs={[{ label: 'Planning', to: `/apqp?kind=${kind}` }, { label: 'New' }]} title={`New ${kindLabel[kind].toLowerCase()}`}>
      <SchemaForm schema={schema} submitLabel="Create draft" onCancel={() => nav(`/apqp?kind=${kind}`)}
        defaults={{ title: '', siteId: '', departmentId: '', productId: '', partId: '', partRevision: '', ownerId: '', phase: 2, productFamily: '' }}
        fields={[{ name: 'title', label: 'Title', full: true }, { name: 'productId', label: 'Product', type: 'lookup', lookup: 'product' }, { name: 'partId', label: 'Part', type: 'lookup', lookup: 'part' },
          { name: 'partRevision', label: 'Part revision' }, ...(kind === 'plans' ? [{ name: 'phase' as const, label: 'Phase', type: 'select' as const, options: phases.map((x, i) => [i, x] as [number, string]) }, { name: 'productFamily' as const, label: 'Product family' }] : []),
          { name: 'siteId', label: 'Site', type: 'site' }, { name: 'departmentId', label: 'Department', type: 'department' }, { name: 'ownerId', label: 'Owner (blank = you)', type: 'user' }]}
        onSubmit={async (v) => { const r = await post<{ id: string }>(`/api/apqp/${kind}`, clean(v)); nav(`/apqp/${kind}/${r.id}`); }} />
    </ModuleWorkspace>
  );
}

type Line = Record<string, any>;
const lineCols: Record<Kind, [string, string, 'text' | 'number' | 'date' | 'user'][]> = {
  flows: [['sequence', 'Seq', 'number'], ['name', 'Step', 'text'], ['stepType', 'Type', 'text'], ['inputs', 'Inputs', 'text'], ['outputs', 'Outputs', 'text']],
  pfmea: [['stepSequence', 'Step', 'number'], ['processFunction', 'Function', 'text'], ['failureMode', 'Failure mode', 'text'], ['failureEffect', 'Effect', 'text'], ['failureCause', 'Cause', 'text'],
    ['preventionControls', 'Prevention', 'text'], ['detectionControls', 'Detection controls', 'text'], ['severity', 'S', 'number'], ['occurrence', 'O', 'number'], ['detection', 'D', 'number'], ['recommendedAction', 'Recommended action', 'text'], ['responsibleId', 'Responsible', 'user'], ['dueOn', 'Due', 'date']],
  plans: [['stepSequence', 'Step', 'number'], ['processStep', 'Process step', 'text'], ['operation', 'Operation', 'text'], ['machineOrTool', 'Machine / tool', 'text'], ['characteristic', 'Characteristic', 'text'], ['characteristicType', 'Product / Process', 'text'],
    ['processCharacteristic', 'Process characteristic', 'text'], ['specialCharacteristic', 'Special', 'text'], ['specification', 'Specification', 'text'], ['tolerance', 'Tolerance', 'text'], ['method', 'Method', 'text'], ['inspectionEquipment', 'Equipment', 'text'],
    ['sampleSize', 'Sample', 'text'], ['frequency', 'Frequency', 'text'], ['controlMethod', 'Control method', 'text'], ['reactionPlan', 'Reaction plan', 'text'], ['ownerId', 'Owner', 'user']],
};
const blank: Record<Kind, Line> = {
  flows: { sequence: 10, name: '', stepType: 'Operation', inputs: '', outputs: '' },
  pfmea: { stepSequence: 10, processFunction: '', failureMode: '', failureEffect: '', failureCause: '', preventionControls: '', detectionControls: '', severity: 5, occurrence: 5, detection: 5, recommendedAction: '', responsibleId: '', dueOn: '' },
  plans: { stepSequence: 10, processStep: '', operation: '', machineOrTool: '', characteristic: '', characteristicType: 'Product', processCharacteristic: '', specialCharacteristic: '', specification: '', tolerance: '', method: '', inspectionEquipment: '', sampleSize: '', frequency: '', controlMethod: '', reactionPlan: '', ownerId: '' },
};
const toInput = (kind: Kind, l: Line) => { const o: Line = {}; for (const [k] of lineCols[kind]) o[k] = l[k] === '' || l[k] === undefined ? null : l[k];
  if (kind === 'pfmea') Object.assign(o, { specialCharacteristic: l.specialCharacteristic ?? null, completedOn: l.completedOn ?? null, actionTaken: l.actionTaken ?? null, revisedSeverity: l.revisedSeverity ?? null, revisedOccurrence: l.revisedOccurrence ?? null, revisedDetection: l.revisedDetection ?? null });
  if (kind === 'flows') Object.assign(o, { ownerId: l.ownerId ?? null, workInstructionId: l.workInstructionId ?? null, inspectionPlanId: l.inspectionPlanId ?? null });
  if (kind === 'plans') Object.assign(o, { pfmeaLineId: l.pfmeaLineId ?? null, inspectionPlanId: l.inspectionPlanId ?? null, spcChartId: l.spcChartId ?? null, workInstructionId: l.workInstructionId ?? null });
  return o; };

/** Risk colour for a PFMEA action priority: these are QualGuard's own tones, not a copied palette. */
const prioTone = ['#5B7F6B', '#B98A2E', '#A4473B'];

export function ApqpDetail() {
  const { kind: k, id } = useParams();
  const kind = k as Kind;
  const nav = useNavigate();
  const qc = useQueryClient();
  const { can } = useAuth();
  const doc = useQuery({ queryKey: ['apqp-doc', kind, id], queryFn: () => get<ApqpRow>(`/api/apqp/${kind}/${id}`) });
  const lines = useQuery({ queryKey: ['apqp-lines', kind, id], queryFn: () => get<Line[]>(`/api/apqp/${kind}/${id}/lines`) });
  const hist = useQuery({ queryKey: ['apqp-hist', kind, id], queryFn: () => get<ApqpRow[]>(`/api/apqp/${kind}/${id}/history`) });
  const users = useUsers();
  const [draft, setDraft] = useState<Line[] | null>(null);
  const [dlg, setDlg] = useState<'revise' | 'obsolete' | null>(null);
  const [imp, setImp] = useState(false);
  const [imported, setImported] = useState<string | null>(null);
  const refresh = () => { qc.invalidateQueries({ queryKey: ['apqp-imports'] }); qc.invalidateQueries({ queryKey: ['apqp-doc', kind, id] }); qc.invalidateQueries({ queryKey: ['apqp-lines', kind, id] }); qc.invalidateQueries({ queryKey: ['apqp-hist', kind, id] }); setDraft(null); };
  const { error, busy, run } = useRunner(refresh);
  const d = doc.data?.document;
  if (!d) return <ModuleWorkspace module={mod('apqp')} crumbs={[{ label: 'Planning', to: '/apqp' }]} title="Loading…">{doc.error ? <Alert severity="error">{(doc.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const editable = d.status === 0 && can('apqp.edit');
  const rows = draft ?? lines.data ?? [];
  const cols = lineCols[kind];
  const setCell = (i: number, key: string, v: string, t: string) => setDraft(rows.map((r, j) => (j === i ? { ...r, [key]: t === 'number' ? Number(v) : v } : r)));
  return (
    <ModuleWorkspace module={mod('apqp')} crumbs={[{ label: 'Planning', to: `/apqp?kind=${kind}` }, { label: d.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{d.number}</span><span>rev {d.revision}</span><StatusChip status={apqpStatus[d.status]} /></Stack>}
      actions={<Stack direction="row" gap={1}>
        <Button component={RouterLink} to={`/insights/graph?type=${kind === 'flows' ? 'ProcessFlow' : kind === 'pfmea' ? 'Pfmea' : 'ControlPlan'}&id=${d.id}`}>Relationships</Button>
        {kind !== 'flows' && can('apqp.edit') && (d.status === 0 || d.status === 2) && <Button variant="outlined" disabled={draft !== null} onClick={() => setImp(true)}>Import lines</Button>}
        {editable && <Button variant="contained" disabled={busy || draft !== null} onClick={() => run(() => post(`/api/apqp/${kind}/${id}/submit`))}>Submit for release</Button>}
        {d.status === 2 && can('apqp.edit') && <Button variant="outlined" onClick={() => setDlg('revise')}>Start revision</Button>}
        {d.status === 2 && can('apqp.approve') && <Button color="error" onClick={() => setDlg('obsolete')}>Make obsolete</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {imported && <Alert severity="success" sx={{ mb: 2 }} onClose={() => setImported(null)} data-testid="import-done">{imported}</Alert>}
      <Facts items={[['Title', d.title], ['Product', doc.data?.productName ?? '—'], ['Part', `${doc.data?.partNumber ?? '—'}${d.partRevision ? ` rev ${d.partRevision}` : ''}`], ['Owner', doc.data?.ownerName ?? '—'],
        ...(kind === 'plans' ? [['Phase', phases[d.phase ?? 2]] as [string, string]] : []), ['Released', fmtDateTime(d.releasedAt)], ['Change reason', d.changeReason ?? '—']]} />
      <Section title={kind === 'flows' ? 'Process steps' : kind === 'pfmea' ? 'Failure modes' : 'Control characteristics'}
        action={editable ? <Stack direction="row" gap={1}>
          <Button size="small" onClick={() => setDraft([...rows, { ...blank[kind], [cols[0][0]]: (rows.length + 1) * 10 }])}>Add line</Button>
          {draft && <><Button size="small" onClick={() => setDraft(null)}>Discard</Button><Button size="small" variant="contained" disabled={busy} onClick={() => run(() => put(`/api/apqp/${kind}/${id}/lines`, draft.map((l) => toInput(kind, l))))}>Save lines</Button></>}
        </Stack> : undefined}>
        <Paper variant="outlined" sx={{ overflowX: 'auto' }}>
          <Table size="small">
            <TableHead><TableRow>{cols.map(([k2, l]) => <TableCell key={k2}>{l}</TableCell>)}{kind === 'pfmea' && <><TableCell>RPN</TableCell><TableCell>Priority</TableCell><TableCell>Revised</TableCell><TableCell /></>}{editable && <TableCell />}</TableRow></TableHead>
            <TableBody>
              {rows.map((r, i) => <TableRow key={r.id ?? `n${i}`}>
                {cols.map(([k2, l, t]) => <TableCell key={k2} sx={{ minWidth: t === 'number' ? 56 : 120 }}>
                  {t === 'user' ? (editable ? <TextField select size="small" variant="standard" value={r[k2] ?? ''} onChange={(e) => setCell(i, k2, e.target.value, t)} sx={{ minWidth: 120 }} slotProps={{ htmlInput: { 'aria-label': `${l} row ${i + 1}` } }}><MenuItem value="">—</MenuItem>{(users.data ?? []).map((u) => <MenuItem key={u.id} value={u.id}>{u.displayName}</MenuItem>)}</TextField>
                    : (users.data?.find((u) => u.id === r[k2])?.displayName ?? '—'))
                  : editable ? <TextField size="small" variant="standard" type={t} value={r[k2] ?? ''} onChange={(e) => setCell(i, k2, e.target.value, t)} slotProps={{ htmlInput: { 'aria-label': `${l} row ${i + 1}` } }} /> : (t === 'date' ? fmtDate(r[k2]) : (r[k2] ?? '—'))}</TableCell>)}
                {kind === 'pfmea' && <>
                  <TableCell sx={{ fontFamily: mono }}>{r.rpn ?? r.severity * r.occurrence * r.detection}</TableCell>
                  <TableCell>{r.priority != null && <Box component="span" sx={{ px: 1, py: 0.25, borderRadius: 1, color: '#fff', fontSize: 12, bgcolor: prioTone[r.priority] }}>{priorities[r.priority]}</Box>}</TableCell>
                  <TableCell sx={{ fontFamily: mono }}>{r.revisedRpn ?? '—'}</TableCell>
                  <TableCell>{r.id && r.completedOn && !r.verifiedAt && can('apqp.edit') && <Button size="small" onClick={() => run(() => post(`/api/apqp/pfmea/${id}/lines/${r.id}/verify`))}>Verify action</Button>}{r.verifiedAt && <StatusChip status="Verified" />}</TableCell>
                </>}
                {editable && <TableCell><Button size="small" color="error" onClick={() => setDraft(rows.filter((_, j) => j !== i))}>Remove</Button></TableCell>}
              </TableRow>)}
              {rows.length === 0 && <TableRow><TableCell colSpan={cols.length + 5}><Typography color="text.secondary" sx={{ py: 2, textAlign: 'center' }}>No lines yet.</Typography></TableCell></TableRow>}
            </TableBody>
          </Table>
        </Paper>
      </Section>
      {kind !== 'flows' && <Section title="Line imports"><ImportHistory kind={kind} docId={d.id} /></Section>}
      {kind !== 'flows' && <LineImportDialog open={imp} onClose={() => setImp(false)} kind={kind} docId={d.id} released={d.status === 2} label={kindLabel[kind]}
        onDone={(r) => { setImported(`${r.accepted} line(s) imported into ${r.documentNumber} rev ${r.documentRevision}${r.revisionCreated ? ' (new revision)' : ''}.${r.rejected ? ` ${r.rejected} rejected row(s) left out.` : ''}`); refresh(); if (r.documentId !== d.id) nav(`/apqp/${kind}/${r.documentId}`); }} />}
      <Section title="Revision history">
        <DataTable data={hist.data ?? []} onRowClick={(r) => nav(`/apqp/${kind}/${r.document.id}`)} columns={[{ header: 'Rev', accessorFn: (r) => r.document.revision }, { header: 'Status', accessorFn: (r) => apqpStatus[r.document.status] },
          { header: 'Released', accessorFn: (r) => fmtDate(r.document.releasedAt) }, { header: 'Reason', accessorFn: (r) => r.document.changeReason ?? '' }]} />
      </Section>
      <StepDialog open={dlg !== null} title={dlg === 'revise' ? 'Start a new revision' : 'Make obsolete'} confirm={dlg === 'revise' ? 'Create draft revision' : 'Make obsolete'} danger={dlg === 'obsolete'}
        inputs={[{ name: 'reason', label: 'Reason', type: 'multiline' }]} onClose={() => setDlg(null)}
        onConfirm={async (v) => { const r = await post<{ id?: string }>(`/api/apqp/${kind}/${id}/${dlg}`, { reason: v.reason }); setDlg(null); if (r?.id) nav(`/apqp/${kind}/${r.id}`); else refresh(); }} />
    </ModuleWorkspace>
  );
}

// ---------------------------------------------------------------- SPC / MSA
const chartTypes = ['X̄-R', 'X̄-S', 'Individuals / MR', 'p', 'np', 'c', 'u'];
interface SpcChartRow { id: string; number: string; title: string; chartType: number; characteristic: string; unit?: string; subgroupSize: number; lsl?: number; usl?: number; autoNcr: boolean; isArchived: boolean }
interface SpcPoint { index: number; value: number; center: number; ucl: number; lcl: number; range?: number; violations: string[] }
interface SpcResult { center: number; ucl: number; lcl: number; rangeCenter?: number; rangeUcl?: number; points: SpcPoint[]; capability?: { mean: number; cp?: number; cpk?: number; pp?: number; ppk?: number }; violations: string[] }

export function SpcList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const q = useQuery({ queryKey: ['spc'], queryFn: () => get<SpcChartRow[]>('/api/spc') });
  return (
    <ModuleWorkspace module={mod('spc')} crumbs={[{ label: 'Control charts' }]} title="Statistical process control"
      actions={can('spc.edit') ? <Button variant="contained" component={RouterLink} to="/spc/new">New control chart</Button> : undefined}>
      <DataTable data={(q.data ?? []).filter((c) => !c.isArchived)} onRowClick={(r) => nav(`/spc/${r.id}`)} empty="No control charts yet."
        columns={[{ header: 'Number', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' }, { header: 'Chart', accessorKey: 'chartType', cell: (x) => chartTypes[x.getValue()] },
          { header: 'Characteristic', accessorKey: 'characteristic' }, { header: 'n', accessorKey: 'subgroupSize' }, { header: 'Spec', accessorFn: (r) => `${r.lsl ?? '—'} … ${r.usl ?? '—'} ${r.unit ?? ''}` },
          { header: 'Auto NCR', accessorKey: 'autoNcr', cell: (x) => (x.getValue() ? 'Yes' : 'No') }]} />
    </ModuleWorkspace>
  );
}

export function SpcNew() {
  const nav = useNavigate();
  const opt = (n?: number | '') => (n === '' || n == null ? null : Number(n));
  const schema = z.object({ title: z.string().trim().min(1, 'Required'), chartType: z.coerce.number().int(), characteristic: z.string().trim().min(1, 'Required'), unit: z.string().optional(), subgroupSize: z.coerce.number().int().min(1).max(25),
    lsl: z.union([z.literal(''), z.coerce.number()]).optional(), usl: z.union([z.literal(''), z.coerce.number()]).optional(), target: z.union([z.literal(''), z.coerce.number()]).optional(),
    partId: z.string().optional(), siteId: z.string().uuid('Choose a site'), departmentId: z.string().optional(), autoNcr: z.coerce.number().int() });
  return (
    <ModuleWorkspace module={mod('spc')} crumbs={[{ label: 'Control charts', to: '/spc' }, { label: 'New' }]} title="New control chart">
      <SchemaForm schema={schema} submitLabel="Create chart" onCancel={() => nav('/spc')}
        defaults={{ title: '', chartType: 0, characteristic: '', unit: '', subgroupSize: 5, lsl: '', usl: '', target: '', partId: '', siteId: '', departmentId: '', autoNcr: 1 }}
        fields={[{ name: 'title', label: 'Title', full: true }, { name: 'chartType', label: 'Chart type', type: 'select', options: chartTypes.map((c, i) => [i, c] as [number, string]) }, { name: 'characteristic', label: 'Characteristic' },
          { name: 'unit', label: 'Unit' }, { name: 'subgroupSize', label: 'Subgroup / sample size', type: 'number' }, { name: 'lsl', label: 'Lower spec', type: 'number' }, { name: 'usl', label: 'Upper spec', type: 'number' }, { name: 'target', label: 'Target', type: 'number' },
          { name: 'partId', label: 'Part', type: 'lookup', lookup: 'part' }, { name: 'siteId', label: 'Site', type: 'site' }, { name: 'departmentId', label: 'Department', type: 'department' },
          { name: 'autoNcr', label: 'Raise an NCR when a rule is broken', type: 'select', options: [[1, 'Yes'], [0, 'No']] }]}
        onSubmit={async (v) => { const r = await post<{ id: string }>('/api/spc', { ...clean(v), lsl: opt(v.lsl), usl: opt(v.usl), target: opt(v.target), autoNcr: v.autoNcr === 1 }); nav(`/spc/${r.id}`); }} />
    </ModuleWorkspace>
  );
}

/** Plain SVG control chart: centre line, limits, spec lines, flagged points. */
export function ControlChart({ r, lsl, usl }: { r: SpcResult; lsl?: number; usl?: number }) {
  const W = 760, H = 240, P = 36;
  const cc = useChartColors();
  const vals = r.points.flatMap((p) => [p.value, p.ucl, p.lcl]).concat([lsl, usl].filter((x): x is number => x != null));
  const lo = Math.min(...vals), hi = Math.max(...vals), span = hi - lo || 1;
  const x = (i: number) => P + (i * (W - 2 * P)) / Math.max(1, r.points.length - 1);
  const y = (v: number) => H - P - ((v - lo) / span) * (H - 2 * P);
  const line = (v: number, c: string, dash: string, label: string) => <g key={label}><line x1={P} x2={W - P} y1={y(v)} y2={y(v)} stroke={c} strokeDasharray={dash} /><text x={W - P + 4} y={y(v) + 4} fontSize={10} fill={c}>{label}</text></g>;
  return (
    <svg viewBox={`0 0 ${W + 40} ${H}`} width="100%" role="img" aria-label="Control chart">
      {line(r.center, '#3E5C76', '', `CL ${r.center.toFixed(3)}`)}{line(r.ucl, '#A4473B', '6 4', `UCL ${r.ucl.toFixed(3)}`)}{line(r.lcl, '#A4473B', '6 4', `LCL ${r.lcl.toFixed(3)}`)}
      {usl != null && line(usl, '#8A6D3B', '2 3', `USL ${usl}`)}{lsl != null && line(lsl, '#8A6D3B', '2 3', `LSL ${lsl}`)}
      <polyline fill="none" stroke={cc.ink} strokeWidth={1.5} points={r.points.map((p, i) => `${x(i)},${y(p.value)}`).join(' ')} />
      {r.points.map((p, i) => <circle key={i} cx={x(i)} cy={y(p.value)} r={p.violations.length ? 5 : 3} fill={p.violations.length ? '#A4473B' : cc.ink}><title>{`#${p.index + 1}: ${p.value}${p.violations.length ? ' — ' + p.violations.join('; ') : ''}`}</title></circle>)}
    </svg>
  );
}

export function SpcDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const { can } = useAuth();
  const q = useQuery({ queryKey: ['spc', id], queryFn: () => get<{ chart: SpcChartRow; chartType: string; samples: { id: string; takenAt: string; values: string; count?: number; units?: number; violations?: string; ncrId?: string }[]; result?: SpcResult }>(`/api/spc/${id}`) });
  const [vals, setVals] = useState('');
  const [count, setCount] = useState('');
  const [msg, setMsg] = useState<string | null>(null);
  const { error, busy, run } = useRunner(() => qc.invalidateQueries({ queryKey: ['spc', id] }));
  const c = q.data?.chart;
  if (!c) return <ModuleWorkspace module={mod('spc')} crumbs={[{ label: 'Control charts', to: '/spc' }]} title="Loading…">{null}</ModuleWorkspace>;
  const variables = c.chartType <= 2;
  const r = q.data?.result;
  const add = () => run(async () => {
    const body = variables ? { values: vals.split(/[\s,;]+/).filter(Boolean).map(Number) } : { count: Number(count), units: c.subgroupSize };
    const o = await post<{ violations: string[]; ncrNumber?: string; ncrSkippedReason?: string }>(`/api/spc/${id}/samples`, body);
    setVals(''); setCount('');
    setMsg(o.violations.length ? `Out of control: ${o.violations.join('; ')}${o.ncrNumber ? ` — ${o.ncrNumber} raised` : o.ncrSkippedReason ? ` — ${o.ncrSkippedReason}` : ''}` : 'Sample recorded; in control.');
  });
  return (
    <ModuleWorkspace module={mod('spc')} crumbs={[{ label: 'Control charts', to: '/spc' }, { label: c.number }]} title={<Stack direction="row" gap={1.5}><span style={{ fontFamily: mono }}>{c.number}</span><span>{c.title}</span></Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {msg && <Alert severity={msg.startsWith('Out') ? 'warning' : 'success'} sx={{ mb: 2 }} onClose={() => setMsg(null)}>{msg}</Alert>}
      <Facts items={[['Chart', chartTypes[c.chartType]], ['Characteristic', `${c.characteristic} ${c.unit ?? ''}`], ['Subgroup', String(c.subgroupSize)], ['Spec', `${c.lsl ?? '—'} … ${c.usl ?? '—'}`],
        ['Cp / Cpk', r?.capability ? `${r.capability.cp?.toFixed(2) ?? '—'} / ${r.capability.cpk?.toFixed(2) ?? '—'}` : '—'], ['Pp / Ppk', r?.capability ? `${r.capability.pp?.toFixed(2) ?? '—'} / ${r.capability.ppk?.toFixed(2) ?? '—'}` : '—']]} />
      <Section title="Chart">{r ? <Paper variant="outlined" sx={{ p: 1 }}><ControlChart r={r} lsl={c.lsl} usl={c.usl} /></Paper> : <Typography color="text.secondary">Limits appear once there are at least two samples.</Typography>}</Section>
      {can('spc.edit') && !c.isArchived && <Section title="Record a sample">
        <Stack direction="row" gap={1} component="form" onSubmit={(e) => { e.preventDefault(); add(); }}>
          {variables ? <TextField size="small" label={`${c.subgroupSize} reading(s), comma separated`} value={vals} onChange={(e) => setVals(e.target.value)} sx={{ width: 360 }} />
            : <TextField size="small" type="number" label={c.chartType <= 4 ? 'Defective units' : 'Defects'} value={count} onChange={(e) => setCount(e.target.value)} />}
          <Button type="submit" variant="contained" disabled={busy}>Record</Button>
        </Stack>
      </Section>}
      <Section title="Samples">
        <DataTable data={[...(q.data?.samples ?? [])].reverse()} columns={[{ header: 'Taken', accessorKey: 'takenAt', cell: (x) => fmtDateTime(x.getValue()) },
          { header: 'Values', accessorFn: (s) => (variables ? (JSON.parse(s.values) as number[]).join(', ') : `${s.count} / ${s.units}`), cell: num }, { header: 'Rule violations', accessorFn: (s) => s.violations ?? '' },
          { header: 'NCR', accessorKey: 'ncrId', cell: (x) => (x.getValue() ? <RouterLink to={`/ncr/${x.getValue()}`}>Open NCR</RouterLink> : '') }]} />
      </Section>
    </ModuleWorkspace>
  );
}

const msaTypes = ['Variable gage R&R', 'Attribute agreement', 'Bias', 'Linearity', 'Stability'];
const msaStatus = ['Draft', 'PendingApproval', 'Approved', 'Rejected'];
interface MsaRow { id: string; number: string; title: string; studyType: number; status: number; verdict?: string; nextReviewOn?: string; tolerance?: number; referenceValue?: number }

export function MsaList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const due = p.get('due') === '1';
  const q = useQuery({ queryKey: ['msa', due], queryFn: () => get<MsaRow[]>(`/api/spc/msa?dueOnly=${due}`) });
  return (
    <ModuleWorkspace module={mod('spc')} crumbs={[{ label: 'Measurement systems' }]} title="Measurement system analysis"
      tabs={[{ label: 'All studies', value: 'all' }, { label: 'Review due', value: 'due' }]} tab={due ? 'due' : 'all'} onTab={(v) => setP(v === 'due' ? { due: '1' } : {})}
      actions={can('spc.edit') ? <Button variant="contained" component={RouterLink} to="/spc/msa/new">New study</Button> : undefined}>
      <DataTable data={q.data ?? []} onRowClick={(r) => nav(`/spc/msa/${r.id}`)} empty="No studies in this view."
        columns={[{ header: 'Number', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' }, { header: 'Type', accessorKey: 'studyType', cell: (x) => msaTypes[x.getValue()] },
          { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={msaStatus[x.getValue()]} /> }, { header: 'Verdict', accessorKey: 'verdict' }, { header: 'Next review', accessorKey: 'nextReviewOn', cell: (x) => fmtDate(x.getValue()) }]} />
    </ModuleWorkspace>
  );
}

export function MsaNew() {
  const nav = useNavigate();
  const eq = useQuery({ queryKey: ['equipment-lite'], queryFn: () => get<{ id: string; number: string; assetNumber: string | null; name: string }[]>('/api/calibration/equipment') });
  const schema = z.object({ title: z.string().trim().min(1, 'Required'), studyType: z.coerce.number().int(), equipmentId: z.string().uuid('Choose equipment'), characteristic: z.string().optional(), inspectionMethod: z.string().optional(),
    tolerance: z.union([z.literal(''), z.coerce.number().positive()]).optional(), referenceValue: z.union([z.literal(''), z.coerce.number()]).optional(), reviewIntervalMonths: z.coerce.number().int().min(1).max(60), siteId: z.string().uuid('Choose a site'), departmentId: z.string().optional(), confidenceLevel: z.coerce.number().min(0.8, '0.80 to 0.999').max(0.999, '0.80 to 0.999') });
  return (
    <ModuleWorkspace module={mod('spc')} crumbs={[{ label: 'Measurement systems', to: '/spc/msa' }, { label: 'New' }]} title="New MSA study">
      <SchemaForm schema={schema} submitLabel="Create study" onCancel={() => nav('/spc/msa')}
        defaults={{ title: '', studyType: 0, equipmentId: '', characteristic: '', inspectionMethod: '', tolerance: '', referenceValue: '', reviewIntervalMonths: 12, siteId: '', departmentId: '', confidenceLevel: 0.95 }}
        fields={[{ name: 'title', label: 'Title', full: true }, { name: 'studyType', label: 'Study type', type: 'select', options: msaTypes.map((t, i) => [i, t] as [number, string]) },
          { name: 'equipmentId', label: 'Gage / equipment', type: 'select', options: (eq.data ?? []).map((e) => [e.id, `${e.number} ${e.name}`] as [string, string]) },
          { name: 'characteristic', label: 'Characteristic' }, { name: 'inspectionMethod', label: 'Inspection method' }, { name: 'tolerance', label: 'Tolerance (USL − LSL)', type: 'number' }, { name: 'referenceValue', label: 'Reference value', type: 'number' }, { name: 'confidenceLevel', label: 'Confidence for the bias t-test', type: 'number', helper: '0.95 = 95% two-sided' },
          { name: 'reviewIntervalMonths', label: 'Review every (months)', type: 'number' }, { name: 'siteId', label: 'Site', type: 'site' }, { name: 'departmentId', label: 'Department', type: 'department' }]}
        onSubmit={async (v) => { const r = await post<{ id: string }>('/api/spc/msa', { ...clean(v), tolerance: v.tolerance === '' ? null : v.tolerance, referenceValue: v.referenceValue === '' ? null : v.referenceValue }); nav(`/spc/msa/${r.id}`); }} />
    </ModuleWorkspace>
  );
}

export function MsaDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const { can } = useAuth();
  const q = useQuery({ queryKey: ['msa', id], queryFn: () => get<{ study: MsaRow; measurements: { operator?: string; part: number; trial: number; value?: number; attribute?: string; reference?: number; attributeReference?: string }[]; result?: Record<string, unknown>; verdict?: string; computeError?: string }>(`/api/spc/msa/${id}`) });
  const [text, setText] = useState<string | null>(null);
  const { error, busy, run } = useRunner(() => { qc.invalidateQueries({ queryKey: ['msa', id] }); setText(null); });
  const s = q.data?.study;
  if (!s) return <ModuleWorkspace module={mod('spc')} crumbs={[{ label: 'Measurement systems', to: '/spc/msa' }]} title="Loading…">{null}</ModuleWorkspace>;
  const attr = s.studyType === 1;
  const asText = (q.data?.measurements ?? []).map((m) => [m.operator ?? '', m.part, m.trial, attr ? m.attribute : m.value, attr ? m.attributeReference ?? '' : m.reference ?? ''].join(',')).join('\n');
  const parse = (t: string) => t.split('\n').map((l) => l.trim()).filter(Boolean).map((l) => { const [op, part, trial, v, ref] = l.split(',').map((x) => x.trim());
    return attr ? { operator: op || null, part: Number(part), trial: Number(trial), value: null, attribute: v, reference: null, attributeReference: ref || null } : { operator: op || null, part: Number(part), trial: Number(trial), value: Number(v), attribute: null, reference: ref ? Number(ref) : null, attributeReference: null }; });
  const res = q.data?.result;
  return (
    <ModuleWorkspace module={mod('spc')} crumbs={[{ label: 'Measurement systems', to: '/spc/msa' }, { label: s.number }]} title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{s.number}</span><span>{s.title}</span><StatusChip status={msaStatus[s.status]} /></Stack>}
      actions={<Stack direction="row" gap={1}>{s.status === 0 && can('spc.edit') && <Button variant="contained" disabled={busy || !!q.data?.computeError} onClick={() => run(() => post(`/api/spc/msa/${id}/submit`))}>Submit for approval</Button>}
        {s.status === 2 && can('spc.edit') && <Button onClick={() => run(() => post(`/api/spc/msa/${id}/review-cycle`))}>Start review study</Button>}
        {s.status !== 0 && can('spc.edit') && <Button onClick={() => run(async () => { await post(`/api/spc/msa/${id}/recalculate`); qc.invalidateQueries({ queryKey: ['msa-results', id] }); })}>Recalculate (keeps the original)</Button>}</Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <Facts items={[['Type', msaTypes[s.studyType]], ['Tolerance', String(s.tolerance ?? '—')], ['Reference', String(s.referenceValue ?? '—')], ['Verdict', q.data?.verdict ?? '—'], ['Next review', fmtDate(s.nextReviewOn)]]} />
      <Section title="Result">
        {q.data?.computeError && <Alert severity="info">{q.data.computeError}</Alert>}
        {res && <Paper variant="outlined" sx={{ p: 2 }}><Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(170px, 1fr))', gap: 1.5 }}>
          {Object.entries(res).filter(([, v]) => typeof v === 'number' || typeof v === 'string' || typeof v === 'boolean').map(([k, v]) =>
            <Box key={k}><Typography variant="caption" color="text.secondary">{k.replace(/([A-Z])/g, ' $1').toLowerCase()}</Typography><Typography sx={{ fontFamily: mono }}>{typeof v === 'number' ? +v.toFixed(4) : String(v)}</Typography></Box>)}
        </Box></Paper>}
      </Section>
      {s.status !== 0 && <MsaResultVersions id={id!} />}
      <Section title="Measurements" action={s.status === 0 && can('spc.edit') ? (text === null ? <Button size="small" onClick={() => setText(asText)}>Edit</Button>
        : <Stack direction="row" gap={1}><Button size="small" onClick={() => setText(null)}>Cancel</Button><Button size="small" variant="contained" disabled={busy} onClick={() => run(() => put(`/api/spc/msa/${id}/measurements`, parse(text)))}>Save</Button></Stack>) : undefined}>
        {text !== null ? <TextField multiline minRows={10} fullWidth label="Measurements" value={text} onChange={(e) => setText(e.target.value)} helperText={`One reading per line: operator, part, trial, ${attr ? 'result, reference result' : 'value, reference (bias/linearity)'}`} InputProps={{ sx: { fontFamily: mono, fontSize: 13 } }} />
          : <DataTable data={q.data?.measurements ?? []} columns={[{ header: 'Operator', accessorKey: 'operator' }, { header: 'Part', accessorKey: 'part' }, { header: 'Trial', accessorKey: 'trial' },
            { header: attr ? 'Result' : 'Value', accessorFn: (m) => (attr ? m.attribute : m.value), cell: num }, { header: 'Reference', accessorFn: (m) => (attr ? m.attributeReference : m.reference) ?? '' }]} />}
      </Section>
    </ModuleWorkspace>
  );
}

/** Every frozen and recalculated result with its calculation version; the result of record is never rewritten. */
function MsaResultVersions({ id }: { id: string }) {
  const q = useQuery({ queryKey: ['msa-results', id], queryFn: () => get<{ id: string; calculationVersion: string; verdict?: string; reason: string; isOfRecord: boolean; createdAt: string; result: Record<string, unknown> }[]>(`/api/spc/msa/${id}/results`) });
  if (!q.data?.length) return null;
  return <Section title="Result versions">
    <DataTable data={q.data} columns={[{ header: 'Recorded', accessorKey: 'createdAt', cell: (x) => fmtDate(x.getValue()) }, { header: 'Why', accessorKey: 'reason' }, { header: 'Calculation', accessorKey: 'calculationVersion' },
      { header: 'Verdict', accessorKey: 'verdict' }, { header: 't / critical / p', id: 'tp', cell: (x) => { const r = x.row.original.result as Record<string, number | null>; return r.t !== undefined ? `${r.t ?? '—'} / ${r.criticalT ?? '—'} / ${r.pValue ?? '—'}` : '—'; } },
      { header: 'Of record', accessorKey: 'isOfRecord', cell: (x) => (x.getValue() ? 'Yes (approved basis)' : '') }]} />
  </Section>;
}

// ---------------------------------------------------------------- LPA
const lpaStatus = ['Scheduled', 'Completed', 'Missed'];
const freq = ['Daily', 'Weekly', 'Monthly'];
interface LpaAuditRow { id: string; number: string; templateId: string; dueOn: string; status: number; failures?: number; assigneeId: string; ncrId?: string; capaId?: string; layer?: number }

export function LpaHome() {
  const { can } = useAuth();
  const qc = useQueryClient();
  const [p, setP] = useSearchParams();
  const view = p.get('view') ?? 'due';
  const audits = useQuery({ queryKey: ['lpa-audits', view], queryFn: () => get<LpaAuditRow[]>(`/api/lpa/audits${view === 'due' ? '?status=0' : view === 'missed' ? '?status=2' : ''}`), enabled: view !== 'templates' });
  const templates = useQuery({ queryKey: ['lpa-templates'], queryFn: () => get<{ id: string; name: string; layer: number; area?: string; questions: string; onFailure: number }[]>('/api/lpa/templates') });
  const [open, setOpen] = useState<string | null>(null);
  const [newT, setNewT] = useState(false);
  const [sched, setSched] = useState<string | null>(null);
  const { error, busy, run } = useRunner(() => qc.invalidateQueries({ queryKey: ['lpa-audits'] }));
  const tName = (id: string) => templates.data?.find((t) => t.id === id)?.name ?? '';
  return (
    <ModuleWorkspace module={mod('lpa')} crumbs={[{ label: 'Layered audits' }]} title="Layered process audits"
      tabs={[{ label: 'Due', value: 'due' }, { label: 'Missed', value: 'missed' }, { label: 'All audits', value: 'all' }, { label: 'Templates', value: 'templates' }]} tab={view} onTab={(v) => setP(v === 'due' ? {} : { view: v })}
      actions={can('lpa.edit') ? <Stack direction="row" gap={1}><Button disabled={busy} onClick={() => run(() => post('/api/lpa/generate'))}>Generate due audits</Button><Button variant="contained" onClick={() => setNewT(true)}>New template</Button></Stack> : undefined}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {view === 'templates' ? <DataTable data={templates.data ?? []} columns={[{ header: 'Name', accessorKey: 'name' }, { header: 'Layer', accessorKey: 'layer' }, { header: 'Area', accessorKey: 'area' },
        { header: 'Questions', accessorFn: (t) => (JSON.parse(t.questions) as string[]).length }, { header: 'On failure', accessorKey: 'onFailure', cell: (x) => ['Nothing', 'Raise NCR', 'Raise CAPA'][x.getValue()] },
        { header: '', id: 'act', cell: (x) => can('lpa.edit') ? <Button size="small" onClick={(e) => { e.stopPropagation(); setSched(x.row.original.id); }}>Schedule</Button> : null }]} />
        : <DataTable data={audits.data ?? []} onRowClick={(r) => r.status === 0 && setOpen(r.id)} empty="No audits in this view."
          columns={[{ header: 'Number', accessorKey: 'number', cell: num }, { header: 'Template', accessorFn: (r) => tName(r.templateId) }, { header: 'Due', accessorKey: 'dueOn', cell: (x) => fmtDate(x.getValue()) },
            { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={lpaStatus[x.getValue()]} /> }, { header: 'Failures', accessorKey: 'failures' },
            { header: 'Raised', accessorFn: (r) => r.ncrId ?? r.capaId, cell: (x) => x.row.original.ncrId ? <RouterLink to={`/ncr/${x.row.original.ncrId}`}>NCR</RouterLink> : x.row.original.capaId ? <RouterLink to={`/capa/${x.row.original.capaId}`}>CAPA</RouterLink> : '' }]} />}
      {open && <LpaRun id={open} onDone={() => { setOpen(null); qc.invalidateQueries({ queryKey: ['lpa-audits'] }); }} />}
      <StepDialog open={newT} title="New layered audit template" confirm="Create template" onClose={() => setNewT(false)}
        inputs={[{ name: 'name', label: 'Name' }, { name: 'layer', label: 'Layer (1 = operator lead … 4 = plant manager)', type: 'select', options: [[1, '1'], [2, '2'], [3, '3'], [4, '4']] }, { name: 'area', label: 'Area / line' },
          { name: 'questions', label: 'Questions, one per line', type: 'multiline' }, { name: 'onFailure', label: 'When a question fails', type: 'select', options: [[0, 'Record only'], [1, 'Raise an NCR'], [2, 'Raise a CAPA']] }, { name: 'siteId', label: 'Site', type: 'site', permission: 'lpa.edit' }]}
        onConfirm={async (v) => { if (!v.siteId) throw new Error('Select a site.'); await post('/api/lpa/templates', { name: v.name, layer: Number(v.layer || 1), area: v.area || null, questions: (v.questions ?? '').split('\n').map((x) => x.trim()).filter(Boolean), onFailure: Number(v.onFailure || 0), escalateToId: null, siteId: v.siteId || null, departmentId: null }); setNewT(false); qc.invalidateQueries({ queryKey: ['lpa-templates'] }); }} />
      <StepDialog open={sched !== null} title="Schedule audits" confirm="Schedule" onClose={() => setSched(null)}
        inputs={[{ name: 'assigneeId', label: 'Auditor', type: 'users' }, { name: 'frequency', label: 'Frequency', type: 'select', options: freq.map((f, i) => [i, f] as [number, string]) }, { name: 'firstDueOn', label: 'First due', type: 'date', initial: localDate() }]}
        onConfirm={async (v) => { await post('/api/lpa/schedules', { templateId: sched, assigneeId: v.assigneeId.split(',')[0], frequency: Number(v.frequency || 1), firstDueOn: v.firstDueOn || localDate() }); setSched(null); }} />
    </ModuleWorkspace>
  );
}

function LpaRun({ id, onDone }: { id: string; onDone: () => void }) {
  const q = useQuery({ queryKey: ['lpa-audit', id], queryFn: () => get<{ audit: LpaAuditRow; template: { name: string; questions: string } }>(`/api/lpa/audits/${id}`) });
  const qs: string[] = q.data ? JSON.parse(q.data.template.questions) : [];
  const [ans, setAns] = useState<Record<number, { pass: boolean; note: string }>>({});
  const { error, busy, run } = useRunner(onDone);
  return (
    <Paper variant="outlined" sx={{ p: 2, mt: 2 }}>
      <Typography variant="h6" sx={{ mb: 1 }}>{q.data?.audit.number} · {q.data?.template.name}</Typography>
      {error && <Alert severity="error" sx={{ mb: 1 }}>{error}</Alert>}
      {qs.map((x, i) => <Stack key={i} direction="row" gap={2} alignItems="center" sx={{ py: 0.75, borderBottom: 1, borderColor: 'divider' }}>
        <Typography sx={{ flex: 1 }}>{i + 1}. {x}</Typography>
        <TextField select size="small" value={ans[i]?.pass === false ? 'fail' : 'pass'} onChange={(e) => setAns({ ...ans, [i]: { pass: e.target.value === 'pass', note: ans[i]?.note ?? '' } })} sx={{ width: 110 }} slotProps={{ htmlInput: { 'aria-label': `Question ${i + 1} result` } }}><MenuItem value="pass">Pass</MenuItem><MenuItem value="fail">Fail</MenuItem></TextField>
        <TextField size="small" placeholder="Note" slotProps={{ htmlInput: { 'aria-label': `Question ${i + 1} note` } }} value={ans[i]?.note ?? ''} onChange={(e) => setAns({ ...ans, [i]: { pass: ans[i]?.pass ?? true, note: e.target.value } })} />
      </Stack>)}
      <Stack direction="row" gap={1} sx={{ mt: 2 }}><Button onClick={onDone}>Close</Button>
        <Button variant="contained" disabled={busy || !qs.length} onClick={() => run(() => post(`/api/lpa/audits/${id}/complete`, { answers: qs.map((x, i) => ({ question: x, pass: ans[i]?.pass ?? true, note: ans[i]?.note || null })) }))}>Complete audit</Button></Stack>
    </Paper>
  );
}

// ---------------------------------------------------------------- Cost of quality
const coqCats = ['Prevention', 'Appraisal', 'Internal failure', 'External failure'];
const coqTone = ['#5B7F6B', '#3E5C76', '#B98A2E', '#A4473B'];
interface CoqGroup { key: string; label: string; prevention: number; appraisal: number; internalFailure: number; externalFailure: number; total: number }

function StackBars({ rows }: { rows: CoqGroup[] }) {
  const max = Math.max(1, ...rows.map((r) => r.total));
  return <Stack gap={0.75}>{rows.map((r) => <Stack key={r.key} direction="row" gap={1} alignItems="center">
    <Typography sx={{ width: 150, fontSize: 13 }} noWrap title={r.label}>{r.label}</Typography>
    <Box sx={{ flex: 1, display: 'flex', height: 18, bgcolor: 'action.hover', borderRadius: 0.5, overflow: 'hidden' }}>
      {[r.prevention, r.appraisal, r.internalFailure, r.externalFailure].map((v, i) => <Box key={i} title={`${coqCats[i]}: ${money(v)}`} sx={{ width: `${(v / max) * 100}%`, bgcolor: coqTone[i] }} />)}
    </Box>
    <Typography sx={{ width: 90, textAlign: 'right', fontFamily: mono, fontSize: 13 }}>{money(r.total)}</Typography>
  </Stack>)}
    <Stack direction="row" gap={2} sx={{ mt: 1 }}>{coqCats.map((c, i) => <Stack key={c} direction="row" gap={0.5} alignItems="center"><Box sx={{ width: 10, height: 10, bgcolor: coqTone[i] }} /><Typography variant="caption">{c}</Typography></Stack>)}</Stack>
  </Stack>;
}

export function CoqHome() {
  const { can } = useAuth();
  const qc = useQueryClient();
  const [p, setP] = useSearchParams();
  const view = p.get('view') ?? 'month';
  const groupBy = ['month', 'category', 'product', 'supplier', 'customer'].includes(view) ? view : 'month';
  const summary = useQuery({ queryKey: ['coq', groupBy], queryFn: () => get<CoqGroup[]>(`/api/coq/summary?by=${groupBy}`), enabled: !['scrap', 'rework', 'entries'].includes(view) });
  const entries = useQuery({ queryKey: ['coq-entries'], queryFn: () => get<{ id: string; incurredOn: string; category: number; element: string; amount: number; description?: string; sourceType?: string }[]>('/api/coq/entries'), enabled: view === 'entries' });
  const scrap = useQuery({ queryKey: ['coq-scrap'], queryFn: () => get<{ id: string; number: string; scrappedOn: string; quantity: number; unitCost: number; reason: string; lotNumber?: string }[]>('/api/coq/scrap'), enabled: view === 'scrap' });
  const rework = useQuery({ queryKey: ['coq-rework'], queryFn: () => get<{ id: string; number: string; status: number; quantity: number; instructions: string; totalCost?: number }[]>('/api/coq/rework'), enabled: view === 'rework' });
  const elements = useQuery({ queryKey: ['coq-elements'], queryFn: () => get<Record<string, string[]>>('/api/coq/elements') });
  const [add, setAdd] = useState(false);
  const total = (summary.data ?? []).reduce((a, r) => a + r.total, 0);
  const fail = (summary.data ?? []).reduce((a, r) => a + r.internalFailure + r.externalFailure, 0);
  return (
    <ModuleWorkspace module={mod('coq')} crumbs={[{ label: 'Cost of quality' }]} title="Cost of quality"
      tabs={[{ label: 'By month', value: 'month' }, { label: 'By category', value: 'category' }, { label: 'By product', value: 'product' }, { label: 'By supplier', value: 'supplier' }, { label: 'By customer', value: 'customer' },
        { label: 'Entries', value: 'entries' }, { label: 'Scrap', value: 'scrap' }, { label: 'Rework', value: 'rework' }]} tab={view} onTab={(v) => setP(v === 'month' ? {} : { view: v })}
      actions={can('coq.edit') ? <Button variant="contained" onClick={() => setAdd(true)}>Record cost</Button> : undefined}>
      {summary.data && !['scrap', 'rework', 'entries'].includes(view) && <>
        <Stack direction="row" gap={2} sx={{ mb: 2 }}>
          <Paper variant="outlined" sx={{ p: 2, minWidth: 200 }}><Typography variant="caption" color="text.secondary">Total cost of quality</Typography><Typography variant="h5" sx={{ fontFamily: mono }}>{money(total)}</Typography></Paper>
          <Paper variant="outlined" sx={{ p: 2, minWidth: 200 }}><Typography variant="caption" color="text.secondary">Failure share</Typography><Typography variant="h5" sx={{ fontFamily: mono }}>{total ? pct((fail / total) * 100) : '—'}</Typography></Paper>
        </Stack>
        <Paper variant="outlined" sx={{ p: 2 }}>{summary.data.length ? <StackBars rows={summary.data} /> : <Typography color="text.secondary">No costs recorded in this period.</Typography>}</Paper>
      </>}
      {view === 'entries' && <DataTable data={entries.data ?? []} columns={[{ header: 'Date', accessorKey: 'incurredOn', cell: (x) => fmtDate(x.getValue()) }, { header: 'Category', accessorKey: 'category', cell: (x) => coqCats[x.getValue()] },
        { header: 'Element', accessorKey: 'element' }, { header: 'Amount', accessorKey: 'amount', cell: (x) => money(x.getValue()) }, { header: 'Source', accessorKey: 'sourceType' }, { header: 'Description', accessorKey: 'description' }]} />}
      {view === 'scrap' && <DataTable data={scrap.data ?? []} columns={[{ header: 'Number', accessorKey: 'number', cell: num }, { header: 'Date', accessorKey: 'scrappedOn', cell: (x) => fmtDate(x.getValue()) }, { header: 'Qty', accessorKey: 'quantity' },
        { header: 'Cost', accessorFn: (s) => s.quantity * s.unitCost, cell: (x) => money(x.getValue()) }, { header: 'Lot', accessorKey: 'lotNumber' }, { header: 'Reason', accessorKey: 'reason' }]} />}
      {view === 'rework' && <DataTable data={rework.data ?? []} columns={[{ header: 'Number', accessorKey: 'number', cell: num }, { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={['Draft', 'PendingApproval', 'Approved', 'Completed', 'Validated', 'Failed', 'Rejected'][x.getValue()]} /> },
        { header: 'Qty', accessorKey: 'quantity' }, { header: 'Instructions', accessorKey: 'instructions' }, { header: 'Cost', accessorKey: 'totalCost', cell: (x) => money(x.getValue()) }]} />}
      <StepDialog open={add} title="Record a quality cost" confirm="Record" onClose={() => setAdd(false)}
        inputs={[{ name: 'category', label: 'Category', type: 'select', options: coqCats.map((c, i) => [i, c] as [number, string]) }, { name: 'element', label: 'Element', helper: Object.values(elements.data ?? {}).flat().slice(0, 6).join(', ') + '…' },
          { name: 'amount', label: 'Amount (USD)' }, { name: 'incurredOn', label: 'Date', type: 'date', initial: localDate() }, { name: 'description', label: 'Description', type: 'multiline' }, { name: 'siteId', label: 'Site', type: 'site', permission: 'coq.edit' }]}
        onConfirm={async (v) => { if (!v.siteId) throw new Error('Select a site.'); await post('/api/coq/entries', { incurredOn: v.incurredOn || localDate(), category: Number(v.category || 0), element: v.element, amount: Number(v.amount), currency: 'USD', description: v.description || null, siteId: v.siteId || null }); setAdd(false); qc.invalidateQueries({ queryKey: ['coq'] }); qc.invalidateQueries({ queryKey: ['coq-entries'] }); }} />
    </ModuleWorkspace>
  );
}

// ---------------------------------------------------------------- Supplier development
interface ScoreRow { supplierId: string; code: string; name: string; quality: number; delivery: number; responsiveness: number; cost: number; correctiveAction: number; auditResults: number; certifications: number; risk: number; overall: number; rank: number }
const dims: [keyof ScoreRow, string][] = [['quality', 'Quality'], ['delivery', 'Delivery'], ['responsiveness', 'Response'], ['cost', 'Cost'], ['correctiveAction', 'Corrective action'], ['auditResults', 'Audits'], ['certifications', 'Certs'], ['risk', 'Risk']];
const scoreCell = (v: number) => <Box component="span" sx={{ fontFamily: mono, color: v < 60 ? '#A4473B' : v < 80 ? '#8A6D3B' : '#3F6B4F', fontWeight: 600 }}>{v.toFixed(0)}</Box>;

export function SupplierScorecard() {
  const nav = useNavigate();
  const [p, setP] = useSearchParams();
  const view = p.get('view') ?? 'scorecard';
  const q = useQuery({ queryKey: ['scorecard'], queryFn: () => get<ScoreRow[]>('/api/supplier-development/scorecard'), enabled: view === 'scorecard' });
  const programs = useQuery({ queryKey: ['sdp'], queryFn: () => get<{ id: string; number: string; title: string; kind: number; status: number; baselineScore?: number; targetScore?: number; currentScore?: number; plannedCompletion: string; supplierId: string }[]>('/api/supplier-development/programs'), enabled: view === 'programs' });
  const w = useQuery({ queryKey: ['sweights'], queryFn: () => get<Record<string, number>>('/api/supplier-development/weights'), enabled: view === 'weights' });
  return (
    <ModuleWorkspace module={mod('suppliers')} crumbs={[{ label: 'Suppliers', to: '/suppliers' }, { label: 'Performance' }]} title="Supplier performance and development"
      tabs={[{ label: 'Scorecard', value: 'scorecard' }, { label: 'Development programs', value: 'programs' }, { label: 'Weighting', value: 'weights' }]} tab={view} onTab={(v) => setP(v === 'scorecard' ? {} : { view: v })}>
      {view === 'scorecard' && <DataTable data={q.data ?? []} onRowClick={(r) => nav(`/suppliers/${r.supplierId}`)} empty="No approved suppliers to score."
        columns={[{ header: '#', accessorKey: 'rank' }, { header: 'Code', accessorKey: 'code', cell: num }, { header: 'Supplier', accessorKey: 'name' },
          ...dims.map(([k, l]) => ({ header: l, accessorKey: k, cell: (x: { getValue: () => number }) => scoreCell(x.getValue()) }) as ColumnDef<ScoreRow, any>),
          { header: 'Overall', accessorKey: 'overall', cell: (x) => <b>{scoreCell(x.getValue())}</b> }]} />}
      {view === 'programs' && <DataTable data={programs.data ?? []} empty="No development programs." columns={[{ header: 'Number', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' },
        { header: 'Kind', accessorKey: 'kind', cell: (x) => ['Assessment', 'Development plan', 'Improvement project', 'Risk reduction'][x.getValue()] }, { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={['Planned', 'Active', 'Completed', 'Cancelled'][x.getValue()]} /> },
        { header: 'Baseline → target', accessorFn: (r) => `${r.baselineScore ?? '—'} → ${r.targetScore ?? '—'}` }, { header: 'Current', accessorKey: 'currentScore' }, { header: 'Planned completion', accessorKey: 'plannedCompletion', cell: (x) => fmtDate(x.getValue()) }]} />}
      {view === 'weights' && w.data && <Paper variant="outlined" sx={{ p: 2, maxWidth: 520 }}>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>Overall score = Σ(dimension score × weight) ÷ Σ weights. Changing weights needs supplier approval rights at every site.</Typography>
        <Table size="small"><TableBody>{dims.map(([k, l]) => <TableRow key={k}><TableCell>{l}</TableCell><TableCell sx={{ fontFamily: mono }}>{w.data[k as string]}</TableCell></TableRow>)}</TableBody></Table>
      </Paper>}
    </ModuleWorkspace>
  );
}

// ---------------------------------------------------------------- Insights
interface Metric { key: string; label: string; value?: number; unit: string; numerator?: number; denominator?: number; definition: string; drillUrl?: string; denominatorSource?: string; denominatorLabel?: string; isFallback?: boolean; calculationVersion?: string }
const fmtMetric = (m: Metric) => (m.value == null ? '—' : m.unit === '%' ? pct(m.value) : m.unit === 'USD' ? money(m.value) : `${+m.value.toFixed(2)}${m.unit && m.unit !== 'count' ? ' ' + m.unit : ''}`);

function Tile({ m }: { m: Metric }) {
  const nav = useNavigate();
  return <Readout label={m.label} value={fmtMetric(m)} title={m.definition} color="#3E5C76" onClick={m.drillUrl ? () => nav(m.drillUrl!) : undefined}>
    {m.denominator != null && m.numerator != null && <Typography variant="caption" color="text.secondary" sx={{ fontFamily: mono }}>{m.numerator} / {m.denominator}</Typography>}
    {m.denominatorLabel && <Typography variant="caption" data-testid={`denominator-${m.key}`} sx={{ display: 'block', color: m.isFallback || m.value == null ? '#8A6D3B' : 'text.secondary' }}>{m.denominatorLabel}</Typography>}
  </Readout>;
}

export function InsightsHome() {
  const [p, setP] = useSearchParams();
  const view = p.get('view') ?? 'production';
  const [site, setSite] = useState('');
  const prod = useQuery({ queryKey: ['ins-prod', site], queryFn: () => get<Metric[]>(`/api/insights/production${site ? `?siteId=${site}` : ''}`), enabled: view === 'production' });
  const exec = useQuery({ queryKey: ['ins-exec', site], queryFn: () => get<{ tiles: Metric[]; defectTrend?: { key: string; count: number }[]; openNcrsBySite?: { site: string; count: number }[]; customerRisk?: { customer: string; open: number; overdue: number; url: string }[]; supplierRisk?: { name: string; overall: number; url: string }[]; auditFindings?: { type: string; open: number; overdue: number }[] }>(`/api/insights/executive${site ? `?siteId=${site}` : ''}`), enabled: view === 'executive' });
  const sites = useQuery({ queryKey: ['sites'], queryFn: () => get<{ id: string; name: string }[]>('/api/organization/sites') });
  const nav = useNavigate();
  return (
    <ModuleWorkspace module={mod('insights')} crumbs={[{ label: 'Insights' }]} title={view === 'executive' ? 'Executive overview' : 'Plant quality metrics'}
      tabs={[{ label: 'Production', value: 'production' }, { label: 'Executive', value: 'executive' }, { label: 'Production volumes', value: 'volumes' }]} tab={view} onTab={(v) => setP(v === 'production' ? {} : { view: v })}
      actions={<TextField select size="small" label="Site" value={site} onChange={(e) => setSite(e.target.value)} sx={{ width: 220 }}><MenuItem value="">All my sites</MenuItem>{(sites.data ?? []).map((s) => <MenuItem key={s.id} value={s.id}>{s.name}</MenuItem>)}</TextField>}>
      {view === 'production' && <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(210px, 1fr))', gap: 1.5 }}>{(prod.data ?? []).map((m) => <Tile key={m.key} m={m} />)}</Box>}
      {view === 'executive' && exec.data && <>
        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(210px, 1fr))', gap: 1.5, mb: 2 }}>{exec.data.tiles.map((m) => <Tile key={m.key} m={m} />)}</Box>
        <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}>
          {exec.data.openNcrsBySite && <Section title="Open NCRs by site"><Table size="small"><TableBody>{exec.data.openNcrsBySite.map((r) => <TableRow key={r.site}><TableCell>{r.site}</TableCell><TableCell sx={{ fontFamily: mono }}>{r.count}</TableCell></TableRow>)}</TableBody></Table></Section>}
          {exec.data.customerRisk && <Section title="Customers at risk"><Table size="small"><TableBody>{exec.data.customerRisk.map((r) => <TableRow key={r.customer} hover sx={{ cursor: 'pointer' }} onClick={() => nav(r.url)}><TableCell>{r.customer}</TableCell><TableCell>{r.open} open</TableCell><TableCell sx={{ color: r.overdue ? 'error.main' : undefined }}>{r.overdue} overdue</TableCell></TableRow>)}</TableBody></Table></Section>}
          {exec.data.supplierRisk && <Section title="Suppliers below target"><Table size="small"><TableBody>{exec.data.supplierRisk.map((r) => <TableRow key={r.name} hover sx={{ cursor: 'pointer' }} onClick={() => nav(r.url)}><TableCell>{r.name}</TableCell><TableCell>{scoreCell(r.overall)}</TableCell></TableRow>)}</TableBody></Table></Section>}
          {exec.data.auditFindings && <Section title="Open audit findings"><Table size="small"><TableBody>{exec.data.auditFindings.map((r) => <TableRow key={r.type}><TableCell>{r.type}</TableCell><TableCell>{r.open} open</TableCell><TableCell>{r.overdue} overdue</TableCell></TableRow>)}</TableBody></Table></Section>}
        </Box>
      </>}
      {view === 'volumes' && <ProductionVolumes site={site} />}
    </ModuleWorkspace>
  );
}

interface PVolume { id: string; siteId: string; periodStart: string; periodEnd: string; quantity: number; notes?: string; status: number; enteredById: string; decidedById?: string }
const volStatus = ['Pending', 'Approved', 'Rejected'];
/** Scrap and rework rate denominators: the configured source order per site, and manually entered volumes that need a second person's approval. */
function ProductionVolumes({ site }: { site: string }) {
  const qc = useQueryClient();
  const { run, error } = useRunner(() => { qc.invalidateQueries({ queryKey: ['ins-vols'] }); qc.invalidateQueries({ queryKey: ['ins-prod'] }); });
  const [add, setAdd] = useState(false);
  const cfg = useQuery({ queryKey: ['ins-denoms', site], queryFn: () => get<{ metrics: { metric: string; sources: string[]; allowFallback: boolean; labels: string[] }[] }>(`/api/insights/denominators${site ? `?siteId=${site}` : ''}`) });
  const vols = useQuery({ queryKey: ['ins-vols'], queryFn: () => get<PVolume[]>('/api/insights/production-volumes') });
  const sites = useQuery({ queryKey: ['sites'], queryFn: () => get<{ id: string; name: string }[]>('/api/organization/sites') });
  const sName = (id: string) => sites.data?.find((s) => s.id === id)?.name ?? '';
  const decide = (id: string, ok: boolean) => run(() => post(`/api/insights/production-volumes/${id}/${ok ? 'approve' : 'reject'}`));
  return <>
    <Section title="Denominator order">
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>Rates use the first source that has volume in the period. A lower source is shown as a fallback; with none, the rate is not calculated. Quantity inspected is used only when configured.</Typography>
      <Table size="small"><TableBody>{(cfg.data?.metrics ?? []).map((m) => <TableRow key={m.metric}><TableCell>{m.metric === 'scrap_rate' ? 'Scrap rate' : 'Rework rate'}</TableCell><TableCell>{m.labels.map((l, i) => `${i + 1}. ${l}`).join('  ·  ')}</TableCell><TableCell>{m.allowFallback ? 'Fallback allowed' : 'No fallback'}</TableCell></TableRow>)}</TableBody></Table>
    </Section>
    {error && <Alert severity="error" sx={{ mt: 1 }}>{error}</Alert>}
    <Stack direction="row" sx={{ my: 1.5 }}><Button variant="contained" onClick={() => setAdd(true)}>Enter production volume</Button></Stack>
    <DataTable data={vols.data ?? []} empty="No manually entered volumes."
      columns={[{ header: 'Site', accessorFn: (r) => sName(r.siteId) }, { header: 'Period', accessorFn: (r) => `${fmtDate(r.periodStart)} – ${fmtDate(r.periodEnd)}` }, { header: 'Quantity', accessorKey: 'quantity', cell: num },
        { header: 'Notes', accessorKey: 'notes' }, { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={volStatus[x.getValue()]} /> },
        { header: '', id: 'act', cell: (x) => x.row.original.status === 0 ? <Stack direction="row" spacing={1}><Button size="small" onClick={() => decide(x.row.original.id, true)}>Approve</Button><Button size="small" color="error" onClick={() => decide(x.row.original.id, false)}>Reject</Button></Stack> : null }]} />
    <StepDialog open={add} title="Enter production volume" confirm="Submit for approval" onClose={() => setAdd(false)}
      inputs={[{ name: 'siteId', label: 'Site', type: 'site', permission: 'production.edit' }, { name: 'periodStart', label: 'From', type: 'date', initial: localDate() }, { name: 'periodEnd', label: 'To', type: 'date', initial: localDate() }, { name: 'quantity', label: 'Quantity produced' }, { name: 'notes', label: 'Source / notes' }]}
      onConfirm={async (v) => { if (!v.siteId) throw new Error('Select a site.'); await post('/api/insights/production-volumes', { siteId: v.siteId, periodStart: v.periodStart, periodEnd: v.periodEnd, quantity: Number(v.quantity), notes: v.notes || null }); setAdd(false); qc.invalidateQueries({ queryKey: ['ins-vols'] }); }} />
  </>;
}

interface GNode { type: string; id: string; number: string; title: string; status?: string; url: string; depth: number }
const typeTone: Record<string, string> = { Product: '#3E5C76', Part: '#5D7A8C', ControlPlan: '#5B7F6B', Pfmea: '#7A6A4F', ProcessFlow: '#6B7F5B', Fai: '#8A6D3B', Ncr: '#A4473B', Capa: '#9A5A6B', Supplier: '#4F6F7A', SupplierCorrectiveAction: '#7A4F5E', Complaint: '#B0623F' };

/** Radial relationship map drawn in SVG: the record in the middle, linked records on rings by distance. */
export function RelationshipGraph() {
  const [p] = useSearchParams();
  const nav = useNavigate();
  const type = p.get('type') ?? '', id = p.get('id') ?? '';
  const [depth, setDepth] = useState(2);
  const q = useQuery({ queryKey: ['graph', type, id, depth], queryFn: () => get<{ nodes: GNode[]; edges: { fromId: string; toId: string; label: string }[] }>(`/api/insights/graph/${type}/${id}?depth=${depth}`), enabled: !!type && !!id });
  const layout = useMemo(() => {
    const pos = new Map<string, { x: number; y: number }>();
    const byDepth = new Map<number, GNode[]>();
    for (const n of q.data?.nodes ?? []) byDepth.set(n.depth, [...(byDepth.get(n.depth) ?? []), n]);
    for (const [d, ns] of byDepth) ns.forEach((n, i) => { const a = (2 * Math.PI * i) / ns.length + d * 0.4; pos.set(n.id, d === 0 ? { x: 450, y: 300 } : { x: 450 + Math.cos(a) * d * 130, y: 300 + Math.sin(a) * d * 110 }); });
    return pos;
  }, [q.data]);
  return (
    <ModuleWorkspace module={mod('insights')} crumbs={[{ label: 'Insights', to: '/insights' }, { label: 'Relationships' }]} title="Record relationships"
      actions={<TextField select size="small" label="Depth" value={depth} onChange={(e) => setDepth(Number(e.target.value))} sx={{ width: 100 }}>{[1, 2, 3].map((d) => <MenuItem key={d} value={d}>{d}</MenuItem>)}</TextField>}>
      {!type ? <Alert severity="info">Open a record and choose “Relationships” to see what it is linked to.</Alert> : q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> :
        <Paper variant="outlined" sx={{ p: 1 }}>
          <svg viewBox="0 0 900 600" width="100%" role="img" aria-label="Relationship diagram">
            {(q.data?.edges ?? []).map((e, i) => { const a = layout.get(e.fromId), b = layout.get(e.toId); return a && b ? <g key={i}><line x1={a.x} y1={a.y} x2={b.x} y2={b.y} stroke="#9AA5B1" /><text x={(a.x + b.x) / 2} y={(a.y + b.y) / 2 - 3} fontSize={9} fill="#7D776B" textAnchor="middle">{e.label}</text></g> : null; })}
            {(q.data?.nodes ?? []).map((n) => { const c = layout.get(n.id)!; return <g key={n.id} style={{ cursor: 'pointer' }} onClick={() => nav(n.url)}>
              <rect x={c.x - 66} y={c.y - 20} width={132} height={40} rx={4} fill="#fff" stroke={typeTone[n.type] ?? '#666'} strokeWidth={n.depth === 0 ? 3 : 1.5} />
              <rect x={c.x - 66} y={c.y - 20} width={6} height={40} fill={typeTone[n.type] ?? '#666'} />
              <text x={c.x - 54} y={c.y - 4} fontSize={10} fill="#555">{n.type.replace(/([a-z])([A-Z])/g, '$1 $2')}</text>
              <text x={c.x - 54} y={c.y + 11} fontSize={11.5} fontFamily={mono} fill="#222">{n.number.slice(0, 18)}</text>
              <title>{`${n.number} — ${n.title}${n.status ? ` (${n.status})` : ''}`}</title></g>; })}
          </svg>
        </Paper>}
    </ModuleWorkspace>
  );
}

// ---------------------------------------------------------------- Portal administration (internal)
export function PortalAccounts() {
  const qc = useQueryClient();
  const q = useQuery({ queryKey: ['portal-accounts'], queryFn: () => get<{ id: string; email: string; displayName: string; kind: string; partyName: string; isActive: boolean; lastSignInAt?: string }[]>('/api/portal/admin/accounts') });
  const [open, setOpen] = useState(false);
  const { error, run } = useRunner(() => qc.invalidateQueries({ queryKey: ['portal-accounts'] }));
  return (
    <ModuleWorkspace module={mod('admin')} crumbs={[{ label: 'Admin', to: '/admin' }, { label: 'Portal accounts' }]} title="Customer and supplier portal accounts"
      actions={<Button variant="contained" onClick={() => setOpen(true)}>Invite portal user</Button>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <DataTable data={q.data ?? []} empty="No portal accounts yet." columns={[{ header: 'Name', accessorKey: 'displayName' }, { header: 'Email', accessorKey: 'email' }, { header: 'Portal', accessorKey: 'kind' }, { header: 'Organization', accessorKey: 'partyName' },
        { header: 'Last sign-in', accessorKey: 'lastSignInAt', cell: (x) => fmtDateTime(x.getValue()) },
        { header: 'Active', accessorKey: 'isActive', cell: (x) => <Button size="small" onClick={() => run(() => post(`/api/portal/admin/accounts/${x.row.original.id}/active?value=${!x.getValue()}`))}>{x.getValue() ? 'Deactivate' : 'Activate'}</Button> }]} />
      <StepDialog open={open} title="Invite a portal user" confirm="Create account" onClose={() => setOpen(false)}
        inputs={[{ name: 'displayName', label: 'Name' }, { name: 'email', label: 'Email' }, { name: 'kind', label: 'Portal', type: 'select', options: [['Customer', 'Customer'], ['Supplier', 'Supplier']] }, { name: 'partyId', label: 'Customer or supplier id' }, { name: 'password', label: 'Temporary password', type: 'password' }]}
        onConfirm={async (v) => { await post('/api/portal/admin/accounts', { email: v.email, displayName: v.displayName, kind: v.kind || 'Customer', partyId: v.partyId, password: v.password }); setOpen(false); qc.invalidateQueries({ queryKey: ['portal-accounts'] }); }} />
    </ModuleWorkspace>
  );
}
