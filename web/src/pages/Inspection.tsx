import { useEffect, useState } from 'react';
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Checkbox, Chip, IconButton, Link, MenuItem, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Tooltip, Typography } from '@mui/material';
import DeleteOutline from '@mui/icons-material/DeleteOutline';
import Add from '@mui/icons-material/Add';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { get, post, put } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, dispositions, fmtDate, fmtDateTime, localDate, ncrStatus } from '../components/Status';
import { ApprovalsPanel, AttachmentsAndHistory } from '../components/RecordTabs';
import { Facts, Prose, Section, StepDialog, lookupLabel, useLookup, useRunner, useSites } from '../components/Kit';
import { SignDialog } from '../components/SignDialog';
import { useWork } from '../work';
import { mono } from '../theme';

export const inspectionStatus = ['InProgress', 'Passed', 'Failed', 'Released', 'Rejected', 'Cancelled'];
export const planRevStatus = ['Draft', 'InReview', 'Effective', 'Superseded'];
const mod = () => modules.find((m) => m.key === 'inspection')!;
const num = (x: { getValue: () => unknown }) => <span style={{ fontFamily: mono }}>{String(x.getValue() ?? '')}</span>;
const useTypes = () => useQuery({ queryKey: ['inspection-types'], queryFn: () => get<string[]>('/api/inspection/types') });

interface Char { id?: string; name: string; kind: number; specification: string | null; lowerLimit: number | null; upperLimit: number | null; target: number | null; unit: string | null; method: string | null; requiresEquipment: boolean; sampleSize: number | null; isCritical: boolean }
const blankChar = (): Char => ({ name: '', kind: 0, specification: '', lowerLimit: null, upperLimit: null, target: null, unit: '', method: '', requiresEquipment: true, sampleSize: null, isCritical: false });
const numOrNull = (v: string) => (v.trim() === '' ? null : Number(v));
const limits = (c: { kind: number; lowerLimit: number | null; upperLimit: number | null; unit: string | null }) =>
  c.kind === 1 ? 'Attribute (pass/fail)' : `${c.lowerLimit ?? '—'} … ${c.upperLimit ?? '—'} ${c.unit ?? ''}`;

/** Spreadsheet-style editor for plan characteristics. */
function CharacteristicEditor({ value, onChange }: { value: Char[]; onChange: (v: Char[]) => void }) {
  const set = (i: number, patch: Partial<Char>) => onChange(value.map((c, j) => (j === i ? { ...c, ...patch } : c)));
  const cell = { px: 0.5, py: 0.5 };
  const tf = { size: 'small' as const, variant: 'standard' as const, fullWidth: true };
  return (
    <Box sx={{ overflowX: 'auto' }}>
      <Table size="small" sx={{ minWidth: 1050 }}>
        <TableHead><TableRow>{['#', 'Characteristic', 'Type', 'Specification', 'Lower', 'Target', 'Upper', 'Unit', 'Method', 'n', 'Gauge req.', 'Critical', ''].map((h) => <TableCell key={h} sx={{ ...cell, fontSize: 12 }}>{h}</TableCell>)}</TableRow></TableHead>
        <TableBody>{value.map((c, i) => (
          <TableRow key={i}>
            <TableCell sx={cell}>{i + 1}</TableCell>
            <TableCell sx={{ ...cell, minWidth: 160 }}><TextField {...tf} value={c.name} onChange={(e) => set(i, { name: e.target.value })} slotProps={{ htmlInput: { 'aria-label': `Characteristic ${i + 1} name` } }} /></TableCell>
            <TableCell sx={{ ...cell, width: 110 }}><TextField {...tf} select value={c.kind} onChange={(e) => set(i, { kind: Number(e.target.value), requiresEquipment: Number(e.target.value) === 0 })}><MenuItem value={0}>Measured</MenuItem><MenuItem value={1}>Attribute</MenuItem></TextField></TableCell>
            <TableCell sx={{ ...cell, minWidth: 150 }}><TextField {...tf} value={c.specification ?? ''} onChange={(e) => set(i, { specification: e.target.value })} /></TableCell>
            {(['lowerLimit', 'target', 'upperLimit'] as const).map((k) => <TableCell key={k} sx={{ ...cell, width: 80 }}><TextField {...tf} type="number" disabled={c.kind === 1} value={c[k] ?? ''} onChange={(e) => set(i, { [k]: numOrNull(e.target.value) })} slotProps={{ htmlInput: { 'aria-label': `Characteristic ${i + 1} ${k}`, step: 'any' } }} /></TableCell>)}
            <TableCell sx={{ ...cell, width: 60 }}><TextField {...tf} value={c.unit ?? ''} onChange={(e) => set(i, { unit: e.target.value })} /></TableCell>
            <TableCell sx={{ ...cell, minWidth: 110 }}><TextField {...tf} value={c.method ?? ''} onChange={(e) => set(i, { method: e.target.value })} /></TableCell>
            <TableCell sx={{ ...cell, width: 50 }}><TextField {...tf} type="number" value={c.sampleSize ?? ''} onChange={(e) => set(i, { sampleSize: numOrNull(e.target.value) })} /></TableCell>
            <TableCell sx={cell}><Checkbox size="small" checked={c.requiresEquipment} onChange={(e) => set(i, { requiresEquipment: e.target.checked })} /></TableCell>
            <TableCell sx={cell}><Checkbox size="small" checked={c.isCritical} onChange={(e) => set(i, { isCritical: e.target.checked })} /></TableCell>
            <TableCell sx={cell}><IconButton size="small" aria-label="Remove characteristic" onClick={() => onChange(value.filter((_, j) => j !== i))}><DeleteOutline fontSize="small" /></IconButton></TableCell>
          </TableRow>))}
        </TableBody>
      </Table>
      <Button size="small" startIcon={<Add />} sx={{ mt: 1 }} onClick={() => onChange([...value, blankChar()])}>Add characteristic</Button>
    </Box>
  );
}
const cleanChars = (cs: Char[]) => cs.map(({ id: _id, ...c }) => ({ ...c, specification: c.specification || null, unit: c.unit || null, method: c.method || null }));

interface PlanRow { id: string; number: string; title: string; inspectionType: string; part: string | null; effectiveRevision: string | null; pending: string | null; ownerName: string; isObsolete: boolean }

export function PlanList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const type = p.get('type') ?? '';
  const types = useTypes();
  const list = useQuery({ queryKey: ['plans', type], queryFn: () => get<{ items: PlanRow[] }>(`/api/inspection/plans?pageSize=200${type ? `&type=${encodeURIComponent(type)}` : ''}`) });
  const cols: ColumnDef<PlanRow, any>[] = [{ header: 'Plan', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' }, { header: 'Type', accessorKey: 'inspectionType' }, { header: 'Part', accessorKey: 'part' },
    { header: 'Effective rev.', accessorKey: 'effectiveRevision', cell: (x) => x.getValue() ?? <Box component="span" sx={{ color: 'warning.main' }}>none</Box> }, { header: 'In progress', accessorKey: 'pending' }, { header: 'Owner', accessorKey: 'ownerName' }];
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Inspection plans' }]} title="Inspection and test plans"
      tabs={[{ label: 'All types', value: '' }, ...(types.data ?? []).slice(0, 6).map((t) => ({ label: t, value: t }))]} tab={type} onTab={(v) => setP(v ? { type: v } : {})}
      actions={can('inspection.edit') ? <Button variant="contained" component={RouterLink} to="/inspection/plans/new">New plan</Button> : undefined}>
      <DataTable data={list.data?.items ?? []} columns={cols} onRowClick={(r) => nav(`/inspection/plans/${r.id}`)} empty="No plans." />
    </ModuleWorkspace>
  );
}

export function PlanNew() {
  const nav = useNavigate();
  const sites = useSites();
  const types = useTypes();
  const parts = useLookup('part');
  const [h, setH] = useState({ title: '', inspectionType: 'Receiving', partId: '', partRevision: '', siteId: '', samplingPlan: '' });
  const [chars, setChars] = useState<Char[]>([blankChar()]);
  const [error, setError] = useState<string | null>(null);
  const f = (k: keyof typeof h, label: string, extra?: object) => <TextField size="small" label={label} value={h[k]} onChange={(e) => setH({ ...h, [k]: e.target.value })} {...extra} />;
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Inspection plans', to: '/inspection/plans' }, { label: 'New' }]} title="New inspection plan">
      <Paper sx={{ p: 3 }}>
        {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 2, mb: 3 }}>
          {f('title', 'Title')}
          <TextField size="small" label="Inspection type" value={h.inspectionType} onChange={(e) => setH({ ...h, inspectionType: e.target.value })} helperText="Pick or type a custom type"
            slotProps={{ htmlInput: { list: 'insp-types' } }} />
          <datalist id="insp-types">{(types.data ?? []).map((t) => <option key={t} value={t} />)}</datalist>
          <TextField size="small" select label="Site" value={h.siteId} onChange={(e) => setH({ ...h, siteId: e.target.value })}>{(sites.data ?? []).filter((s) => !s.isArchived).map((s) => <MenuItem key={s.id} value={s.id}>{s.code} — {s.name}</MenuItem>)}</TextField>
          <TextField size="small" select label="Part" value={h.partId} onChange={(e) => setH({ ...h, partId: e.target.value })}><MenuItem value="">None</MenuItem>{(parts.data ?? []).map((r) => <MenuItem key={r.id} value={r.id}>{lookupLabel(r)}</MenuItem>)}</TextField>
          {f('partRevision', 'Part revision (defaults to current)')}
          {f('samplingPlan', 'Sampling plan (e.g. Z1.4 GII AQL 1.0)')}
        </Box>
        <Typography variant="overline" color="text.secondary">Characteristics</Typography>
        <CharacteristicEditor value={chars} onChange={setChars} />
        <Stack direction="row" gap={1} sx={{ mt: 3 }}>
          <Button variant="contained" onClick={async () => {
            setError(null);
            try { const r = await post<{ id: string }>('/api/inspection/plans', { ...h, partId: h.partId || null, partRevision: h.partRevision || null, samplingPlan: h.samplingPlan || null, characteristics: cleanChars(chars) }); nav(`/inspection/plans/${r.id}`); }
            catch (e) { setError((e as Error).message); }
          }}>Create plan (revision A draft)</Button>
          <Button onClick={() => nav('/inspection/plans')}>Cancel</Button>
        </Stack>
      </Paper>
    </ModuleWorkspace>
  );
}

interface Rev { id: string; revision: string; status: number; samplingPlan: string | null; partRevision: string | null; changeReason: string | null; effectiveAt: string | null; rowVersion: number; characteristics: Char[] }
interface PlanDto { id: string; number: string; title: string; inspectionType: string; isObsolete: boolean; part: { id: string; partNumber: string; name: string } | null; product: { id: string; number: string; name: string } | null; ownerName: string;
  revisions: Rev[]; inspections: { id: string; number: string; planRevision: string; lotNumber: string | null; status: number; inspectedOn: string }[]; canEdit: boolean; allowed: string[] }

export function PlanDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const nav = useNavigate();
  const work = useWork();
  const [tab, setTab] = useState('current');
  const [dlg, setDlg] = useState<string | null>(null);
  const [signing, setSigning] = useState(false);
  const [draft, setDraft] = useState<{ chars: Char[]; samplingPlan: string; partRevision: string; changeReason: string } | null>(null);
  const q = useQuery({ queryKey: ['plan', id], queryFn: () => get<PlanDto>(`/api/inspection/plans/${id}`) });
  const refresh = () => { qc.invalidateQueries({ queryKey: ['plan', id] }); qc.invalidateQueries({ queryKey: ['work'] }); };
  const { error, run } = useRunner(refresh);
  const d = q.data;
  const working = d?.revisions.find((r) => r.status <= 1);
  useEffect(() => { if (working?.status === 0) setDraft({ chars: working.characteristics, samplingPlan: working.samplingPlan ?? '', partRevision: working.partRevision ?? '', changeReason: working.changeReason ?? '' }); else setDraft(null); }, [working?.id, working?.status, working?.rowVersion]);
  if (!d) return <ModuleWorkspace module={mod()} crumbs={[{ label: 'Inspection plans', to: '/inspection/plans' }]} title="Loading">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const effective = d.revisions.find((r) => r.status === 2);
  const approval = working ? work.data?.approvals.find((a) => a.recordId === working.id) : undefined;
  const shown = tab === 'current' ? effective : tab === 'working' ? working : d.revisions.find((r) => r.id === tab);
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Inspection plans', to: '/inspection/plans' }, { label: d.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono, whiteSpace: 'nowrap' }}>{d.number}</span><span>{d.title}</span>{effective && <Chip size="small" label={`Rev ${effective.revision} effective`} color="success" variant="outlined" />}{d.isObsolete && <StatusChip status="Obsolete" />}</Stack>}
      tabs={[{ label: effective ? `Effective (rev ${effective.revision})` : 'Effective', value: 'current' }, ...(working ? [{ label: `Rev ${working.revision} ${planRevStatus[working.status].toLowerCase()}`, value: 'working' }] : []),
        { label: 'Revision history', value: 'history' }, { label: 'Inspections', value: 'inspections' }, { label: 'Approvals', value: 'approvals' }, { label: 'Files & history', value: 'more' }]} tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1}>
        {approval && <Button variant="contained" color="secondary" onClick={() => setSigning(true)}>Sign approval</Button>}
        {d.allowed.includes('inspect') && <Button variant="contained" onClick={() => setDlg('inspect')}>Start inspection</Button>}
        {d.allowed.includes('newRevision') && <Button variant="outlined" onClick={() => setDlg('rev')}>New revision</Button>}
        {d.allowed.includes('obsolete') && <Button color="warning" onClick={() => { if (confirm('Make this plan obsolete?')) run(() => post(`/api/inspection/plans/${d.id}/obsolete`, { obsolete: true })); }}>Make obsolete</Button>}
        {d.allowed.includes('restore') && <Button onClick={() => run(() => post(`/api/inspection/plans/${d.id}/obsolete`, { obsolete: false }))}>Restore</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {!effective && <Alert severity="warning" sx={{ mb: 2 }}>No revision is effective yet. Inspections can start once a revision is approved.</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: '260px 1fr', gap: 3 }}>
        <Facts items={[['Type', d.inspectionType], ['Part', d.part ? <Link component={RouterLink} to={`/products/parts/${d.part.id}`}>{d.part.partNumber} {d.part.name}</Link> : '—'], ['Product', d.product ? `${d.product.number} ${d.product.name}` : '—'], ['Owner', d.ownerName],
          ['Effective since', fmtDateTime(effective?.effectiveAt)]]} />
        <Box sx={{ minWidth: 0 }}>
          {tab === 'working' && working && working.status === 0 && draft && d.canEdit ? <Section title={`Draft revision ${working.revision}`} action={<Stack direction="row" gap={1}>
              <Button size="small" onClick={() => run(() => put(`/api/inspection/revisions/${working.id}`, { samplingPlan: draft.samplingPlan || null, partRevision: draft.partRevision || null, changeReason: draft.changeReason || null, characteristics: cleanChars(draft.chars), rowVersion: working.rowVersion }))}>Save draft</Button>
              <Button size="small" variant="contained" onClick={() => run(() => post(`/api/inspection/revisions/${working.id}/submit`))}>Submit for approval</Button></Stack>}>
              <Stack direction="row" gap={2} sx={{ mb: 2 }}>
                <TextField size="small" label="Sampling plan" value={draft.samplingPlan} onChange={(e) => setDraft({ ...draft, samplingPlan: e.target.value })} sx={{ flex: 1 }} />
                <TextField size="small" label="Part revision" value={draft.partRevision} onChange={(e) => setDraft({ ...draft, partRevision: e.target.value })} sx={{ width: 140 }} />
                <TextField size="small" label="Reason for change" value={draft.changeReason} onChange={(e) => setDraft({ ...draft, changeReason: e.target.value })} sx={{ flex: 2 }} />
              </Stack>
              <CharacteristicEditor value={draft.chars} onChange={(chars) => setDraft({ ...draft, chars })} />
            </Section>
            : (tab === 'current' || tab === 'working' || d.revisions.some((r) => r.id === tab)) && (shown ? <RevisionView rev={shown} /> : <Alert severity="info">No effective revision.</Alert>)}
          {tab === 'history' && <Section title="Revisions">
            <DataTable data={d.revisions} onRowClick={(r) => setTab(r.id)} columns={[{ header: 'Rev', accessorKey: 'revision', cell: num }, { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={planRevStatus[x.getValue() as number]} /> },
              { header: 'Effective', accessorKey: 'effectiveAt', cell: (x) => fmtDateTime(x.getValue() as string) }, { header: 'Characteristics', id: 'n', accessorFn: (r) => r.characteristics.length }, { header: 'Reason', accessorKey: 'changeReason' }]} empty="" />
          </Section>}
          {tab === 'inspections' && <Section title="Inspections using this plan">
            <DataTable data={d.inspections} onRowClick={(r) => nav(`/inspection/${r.id}`)} columns={[{ header: 'Number', accessorKey: 'number', cell: num }, { header: 'Plan rev.', accessorKey: 'planRevision' }, { header: 'Lot', accessorKey: 'lotNumber' },
              { header: 'Date', accessorKey: 'inspectedOn', cell: (x) => fmtDate(x.getValue() as string) }, { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={inspectionStatus[x.getValue() as number]} /> }]} empty="None yet." />
          </Section>}
          {tab === 'approvals' && working && <ApprovalsPanel type="InspectionPlanRevision" id={working.id} />}
          {tab === 'approvals' && !working && effective && <ApprovalsPanel type="InspectionPlanRevision" id={effective.id} />}
          {tab === 'more' && <AttachmentsAndHistory type="InspectionPlan" id={d.id} canEdit={d.canEdit} />}
        </Box>
      </Box>
      <StepDialog open={dlg === 'rev'} title="Start a new revision" intro="The new draft copies the current characteristics. Inspections already done stay tied to their revision." confirm="Create draft" onClose={() => setDlg(null)}
        inputs={[{ name: 'reason', label: 'Reason for the revision', type: 'multiline' }]} onConfirm={(v) => run(async () => { await post(`/api/inspection/plans/${d.id}/revisions`, v); setTab('working'); })} />
      <StartInspectionDialog open={dlg === 'inspect'} planId={d.id} onClose={() => setDlg(null)} />
      {signing && approval && <SignDialog approvalId={approval.id} recordNumber={approval.recordNumber} stepName={approval.stepName} onClose={() => { setSigning(false); refresh(); }} />}
    </ModuleWorkspace>
  );
}

function RevisionView({ rev }: { rev: Rev }) {
  return (
    <Section title={`Revision ${rev.revision} — ${planRevStatus[rev.status]}`}>
      <Stack direction="row" gap={4} sx={{ mb: 1.5, fontSize: 14 }}><span>Sampling: <b>{rev.samplingPlan ?? '—'}</b></span><span>Part revision: <b>{rev.partRevision ?? '—'}</b></span></Stack>
      {rev.changeReason && <Prose label="Reason for change" text={rev.changeReason} />}
      <DataTable data={rev.characteristics} columns={[{ header: '#', id: 'seq', accessorFn: (_r, i) => i + 1 }, { header: 'Characteristic', accessorKey: 'name', cell: (x) => <>{x.getValue() as string}{x.row.original.isCritical && <Chip size="small" label="critical" color="error" variant="outlined" sx={{ ml: 1 }} />}</> },
        { header: 'Specification', accessorKey: 'specification' }, { header: 'Limits', id: 'lim', accessorFn: (r) => limits(r) }, { header: 'Method', accessorKey: 'method' }, { header: 'n', accessorKey: 'sampleSize' },
        { header: 'Gauge', accessorKey: 'requiresEquipment', cell: (x) => (x.getValue() ? 'required' : '') }]} empty="" />
    </Section>
  );
}

function StartInspectionDialog({ open, planId, onClose }: { open: boolean; planId: string; onClose: () => void }) {
  const nav = useNavigate();
  const suppliers = useLookup('supplier', open);
  return <StepDialog open={open} title="Start inspection" intro="The inspection uses the effective plan revision and stays tied to it. Restricted suppliers need an override reason and permission." confirm="Start" onClose={onClose}
    inputs={[{ name: 'supplierId', label: 'Supplier (receiving)', type: 'select', initial: '', options: [['', '— none —'], ...(suppliers.data ?? []).map((s) => [s.id, lookupLabel(s)] as [string, string])] },
      { name: 'purchaseOrder', label: 'Purchase order' }, { name: 'workOrder', label: 'Work order' }, { name: 'lotNumber', label: 'Lot' }, { name: 'batchNumber', label: 'Batch' }, { name: 'serialNumber', label: 'Serial number' },
      { name: 'quantityReceived', label: 'Quantity received' }, { name: 'sampleSize', label: 'Sample size' }, { name: 'inspectedOn', label: 'Inspection date', type: 'date', initial: localDate() },
      { name: 'supplierOverrideReason', label: 'Override reason (only if the supplier is restricted)', type: 'multiline' }]}
    onConfirm={async (v) => {
      const r = await post<{ id: string }>('/api/inspection', { planId, supplierId: v.supplierId || null, purchaseOrder: v.purchaseOrder || null, workOrder: v.workOrder || null, lotNumber: v.lotNumber || null, batchNumber: v.batchNumber || null,
        serialNumber: v.serialNumber || null, quantityReceived: v.quantityReceived ? Number(v.quantityReceived) : null, sampleSize: v.sampleSize ? Number(v.sampleSize) : null, inspectedOn: v.inspectedOn, supplierOverrideReason: v.supplierOverrideReason || null });
      nav(`/inspection/${r.id}`);
    }} />;
}

interface InspRow { id: string; number: string; inspectionType: string; partNumber: string | null; planRevision: string; lotNumber: string | null; purchaseOrder: string | null; quantityReceived: number | null; inspectedOn: string; status: number; supplier: string | null; inspectorName: string }

export function InspectionList() {
  const nav = useNavigate();
  const [p, setP] = useSearchParams();
  const view = p.get('view') ?? 'pending';
  const [q, setQ] = useState('');
  const [applied, setApplied] = useState('');
  const list = useQuery({ queryKey: ['inspections', view, applied], queryFn: () => get<{ items: InspRow[] }>(`/api/inspection?pageSize=200${view === 'pending' ? '&pendingOnly=true' : view === 'all' ? '' : `&status=${view}`}${applied ? `&q=${encodeURIComponent(applied)}` : ''}`) });
  const cols: ColumnDef<InspRow, any>[] = [{ header: 'Number', accessorKey: 'number', cell: num }, { header: 'Type', accessorKey: 'inspectionType' }, { header: 'Part', accessorKey: 'partNumber' }, { header: 'Plan rev.', accessorKey: 'planRevision' },
    { header: 'Supplier', accessorKey: 'supplier' }, { header: 'PO', accessorKey: 'purchaseOrder' }, { header: 'Lot', accessorKey: 'lotNumber' }, { header: 'Qty', accessorKey: 'quantityReceived' },
    { header: 'Date', accessorKey: 'inspectedOn', cell: (x) => fmtDate(x.getValue()) }, { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={inspectionStatus[x.getValue()]} /> }, { header: 'Inspector', accessorKey: 'inspectorName' }];
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Inspections' }]} title="Inspections"
      tabs={[{ label: 'Awaiting decision', value: 'pending' }, { label: 'Failed', value: '2' }, { label: 'Released', value: '3' }, { label: 'Rejected', value: '4' }, { label: 'All', value: 'all' }]} tab={view} onTab={(v) => setP(v === 'pending' ? {} : { view: v })}
      actions={<Button variant="outlined" component={RouterLink} to="/inspection/plans">Start from a plan</Button>}>
      <Stack direction="row" gap={1} component="form" sx={{ mb: 2 }} onSubmit={(e) => { e.preventDefault(); setApplied(q); }}>
        <TextField size="small" placeholder="Number, part, lot, PO, work order, serial" value={q} onChange={(e) => setQ(e.target.value)} sx={{ width: 380 }} />
        <Button type="submit" variant="outlined">Filter</Button>
      </Stack>
      <DataTable data={list.data?.items ?? []} columns={cols} onRowClick={(r) => nav(`/inspection/${r.id}`)} empty="No inspections in this view." />
    </ModuleWorkspace>
  );
}

interface ResultDto { values: string | null; minimum: number | null; maximum: number | null; defects: number | null; pass: boolean; equipmentId: string | null; equipmentNumber: string | null; equipmentCalibrationDueOn: string | null; notes: string | null }
interface InspDto { id: string; number: string; inspectionType: string; planId: string; planRevision: string; partId: string | null; partNumber: string | null; partRevision: string | null; purchaseOrder: string | null; workOrder: string | null;
  lotNumber: string | null; batchNumber: string | null; serialNumber: string | null; quantityReceived: number | null; sampleSize: number | null; samplingPlan: string | null; inspectedOn: string; status: number; supplierOverrideReason: string | null;
  completedAt: string | null; decisionNotes: string | null; decidedAt: string | null; plan: { id: string; number: string; title: string }; supplierId: string | null; supplierLabel: string | null; customerLabel: string | null; inspectorName: string; decidedByName: string | null;
  characteristics: (Char & { id: string; result: ResultDto | null })[]; ncr: { id: string; number: string; status: number; disposition: number } | null; canEdit: boolean; allowed: string[] }

export function InspectionDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const nav = useNavigate();
  const [tab, setTab] = useState('results');
  const [dlg, setDlg] = useState<string | null>(null);
  const [entry, setEntry] = useState<Record<string, { values: string; defects: string; equipmentId: string; notes: string }>>({});
  const q = useQuery({ queryKey: ['inspection', id], queryFn: () => get<InspDto>(`/api/inspection/${id}`) });
  const equipment = useQuery({ queryKey: ['usable-eq', q.data?.inspectedOn], enabled: !!q.data, queryFn: () => get<{ id: string; number: string; name: string; nextDueOn: string }[]>(`/api/inspection/equipment?on=${q.data!.inspectedOn}`) });
  const refresh = () => qc.invalidateQueries({ queryKey: ['inspection', id] });
  const { error, run } = useRunner(refresh);
  const d = q.data;
  useEffect(() => {
    if (!d) return;
    setEntry(Object.fromEntries(d.characteristics.map((c) => [c.id, { values: c.result?.values?.split(';').join(' ') ?? '', defects: c.result?.defects?.toString() ?? '', equipmentId: c.result?.equipmentId ?? '', notes: c.result?.notes ?? '' }])));
  }, [d?.id, d?.status, d?.characteristics.map((c) => c.result?.values ?? c.result?.defects).join('|')]);
  if (!d) return <ModuleWorkspace module={mod()} crumbs={[{ label: 'Inspections', to: '/inspection' }]} title="Loading">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const editing = d.allowed.includes('record');
  const save = () => run(() => put(`/api/inspection/${d.id}/results`, { results: d.characteristics.filter((c) => (c.kind === 0 ? entry[c.id]?.values.trim() : entry[c.id]?.defects.trim() !== '')).map((c) => ({
    characteristicId: c.id, values: c.kind === 0 ? entry[c.id].values.trim().split(/[\s,;]+/).filter(Boolean).map(Number) : null, defects: c.kind === 1 ? Number(entry[c.id].defects) : null,
    equipmentId: entry[c.id].equipmentId || null, notes: entry[c.id].notes || null })) }));
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Inspections', to: '/inspection' }, { label: d.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono, whiteSpace: 'nowrap' }}>{d.number}</span><span>{d.inspectionType} inspection {d.partNumber ?? ''}</span><StatusChip status={inspectionStatus[d.status]} /></Stack>}
      tabs={[{ label: 'Results', value: 'results' }, { label: 'Files & history', value: 'more' }]} tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1}>
        {editing && <Button variant="outlined" onClick={save}>Save results</Button>}
        {d.allowed.includes('complete') && <Button variant="contained" onClick={() => run(() => post(`/api/inspection/${d.id}/complete`))}>Complete inspection</Button>}
        {d.allowed.includes('ncr') && <Button variant="contained" color="error" onClick={() => setDlg('ncr')}>Raise NCR</Button>}
        {d.allowed.includes('release') && <Button variant="contained" color="success" onClick={() => setDlg('release')}>Release</Button>}
        {d.allowed.includes('reject') && <Button variant="outlined" color="error" onClick={() => setDlg('reject')}>Reject</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {d.supplierOverrideReason && <Alert severity="warning" sx={{ mb: 2 }}>Supplier restriction overridden: {d.supplierOverrideReason}</Alert>}
      {d.status === 2 && !d.ncr && <Alert severity="error" sx={{ mb: 2 }}>Failed. Raise an NCR to disposition the material before release or rejection.</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: '270px 1fr', gap: 3 }}>
        <Facts items={[['Plan', <Link component={RouterLink} to={`/inspection/plans/${d.plan.id}`} sx={{ fontFamily: mono }}>{d.plan.number}</Link>], ['Plan revision', <b>{d.planRevision}</b>], ['Sampling', d.samplingPlan ?? '—'],
          ['Part / rev', d.partNumber ? `${d.partNumber} / ${d.partRevision ?? '—'}` : '—'], ['Supplier', d.supplierId ? <Link component={RouterLink} to={`/suppliers/${d.supplierId}`}>{d.supplierLabel}</Link> : '—'], ['Customer', d.customerLabel ?? '—'],
          ['PO / WO', [d.purchaseOrder, d.workOrder].filter(Boolean).join(' / ') || '—'], ['Lot / batch', [d.lotNumber, d.batchNumber].filter(Boolean).join(' / ') || '—'], ['Serial', d.serialNumber ?? '—'],
          ['Qty received', d.quantityReceived ?? '—'], ['Sample size', d.sampleSize ?? '—'], ['Inspector', d.inspectorName], ['Date', fmtDate(d.inspectedOn)],
          ['NCR', d.ncr ? <span><Link component={RouterLink} to={`/ncr/${d.ncr.id}`} sx={{ fontFamily: mono }}>{d.ncr.number}</Link> <StatusChip status={ncrStatus[d.ncr.status]} />{d.ncr.disposition ? ` ${dispositions[d.ncr.disposition]}` : ''}</span> : '—'],
          ['Decision', d.decidedAt ? `${inspectionStatus[d.status]} by ${d.decidedByName} ${fmtDateTime(d.decidedAt)}` : '—']]} />
        <Box sx={{ minWidth: 0 }}>
          {tab === 'results' && <Section title="Characteristics">
            <Box sx={{ overflowX: 'auto' }}><Table size="small">
              <TableHead><TableRow>{['#', 'Characteristic', 'Limits', editing ? 'Measured values / defects' : 'Result', 'Equipment', 'Notes'].map((h) => <TableCell key={h} sx={{ fontSize: 12 }}>{h}</TableCell>)}</TableRow></TableHead>
              <TableBody>{d.characteristics.map((c, i) => {
                const r = c.result; const e = entry[c.id] ?? { values: '', defects: '', equipmentId: '', notes: '' };
                const setE = (patch: Partial<typeof e>) => setEntry({ ...entry, [c.id]: { ...e, ...patch } });
                return <TableRow key={c.id} sx={{ bgcolor: r && !r.pass ? 'rgba(198,40,40,0.06)' : undefined }}>
                  <TableCell>{i + 1}</TableCell>
                  <TableCell>{c.name}{c.isCritical && <Chip size="small" label="critical" color="error" variant="outlined" sx={{ ml: 1 }} />}<Typography fontSize={12} color="text.secondary">{c.specification} {c.method ? `· ${c.method}` : ''}</Typography></TableCell>
                  <TableCell sx={{ whiteSpace: 'nowrap' }}>{limits(c)}{c.sampleSize ?? d.sampleSize ? <Typography fontSize={12} color="text.secondary">n = {c.sampleSize ?? d.sampleSize}</Typography> : null}</TableCell>
                  <TableCell sx={{ minWidth: 200 }}>{editing ? (c.kind === 0
                    ? <TextField size="small" variant="standard" fullWidth placeholder="e.g. 12.00 12.01 11.99" value={e.values} onChange={(ev) => setE({ values: ev.target.value })} slotProps={{ htmlInput: { 'aria-label': `${c.name} values` } }} />
                    : <TextField size="small" variant="standard" type="number" placeholder="defects" value={e.defects} onChange={(ev) => setE({ defects: ev.target.value })} slotProps={{ htmlInput: { 'aria-label': `${c.name} defects` } }} />)
                    : r ? <Stack direction="row" gap={1} alignItems="center"><Chip size="small" label={r.pass ? 'Pass' : 'Fail'} color={r.pass ? 'success' : 'error'} />{c.kind === 0 ? <span style={{ fontFamily: mono, fontSize: 13 }}>{r.values?.split(';').join('  ')}</span> : `${r.defects} defect(s)`}</Stack> : '—'}
                    {editing && r && <Chip size="small" sx={{ mt: 0.5 }} label={r.pass ? 'Saved · pass' : 'Saved · fail'} color={r.pass ? 'success' : 'error'} variant="outlined" />}</TableCell>
                  <TableCell sx={{ minWidth: 170 }}>{editing ? <TextField size="small" variant="standard" select fullWidth value={e.equipmentId} onChange={(ev) => setE({ equipmentId: ev.target.value })} slotProps={{ htmlInput: { 'aria-label': `${c.name} equipment` } }}>
                      <MenuItem value="">{c.requiresEquipment ? '— choose —' : 'None'}</MenuItem>{(equipment.data ?? []).map((q2) => <MenuItem key={q2.id} value={q2.id}>{q2.number} {q2.name}</MenuItem>)}</TextField>
                    : r?.equipmentNumber ? <Tooltip title={`Calibration due ${fmtDate(r.equipmentCalibrationDueOn)}`}><span style={{ fontFamily: mono }}>{r.equipmentNumber}</span></Tooltip> : '—'}</TableCell>
                  <TableCell>{editing ? <TextField size="small" variant="standard" value={e.notes} onChange={(ev) => setE({ notes: ev.target.value })} /> : r?.notes ?? ''}</TableCell>
                </TableRow>;
              })}</TableBody>
            </Table></Box>
            {editing && <Typography fontSize={12} color="text.secondary" sx={{ mt: 1 }}>Only equipment that is active and in calibration on the inspection date is listed.</Typography>}
            {d.decisionNotes && <Prose label="Decision notes" text={d.decisionNotes} />}
          </Section>}
          {tab === 'more' && <AttachmentsAndHistory type="Inspection" id={d.id} canEdit={d.canEdit} />}
        </Box>
      </Box>
      <StepDialog open={dlg === 'ncr'} title="Raise NCR" intro="The NCR gets the part, revision, supplier, PO, lot and the failed measurements, and is linked to this inspection." confirm="Raise NCR" onClose={() => setDlg(null)}
        inputs={[{ name: 'title', label: 'NCR title (optional)' }]} onConfirm={(v) => run(async () => { const r = await post<{ id: string }>(`/api/inspection/${d.id}/ncr`, { title: v.title || null }); nav(`/ncr/${r.id}`); })} />
      <StepDialog open={dlg === 'release'} title="Release material" confirm="Release" onClose={() => setDlg(null)} inputs={[{ name: 'notes', label: 'Decision notes', type: 'multiline' }]}
        onConfirm={(v) => run(() => post(`/api/inspection/${d.id}/decision`, { release: true, notes: v.notes }))} />
      <StepDialog open={dlg === 'reject'} title="Reject material" confirm="Reject" danger onClose={() => setDlg(null)} inputs={[{ name: 'notes', label: 'Decision notes', type: 'multiline' }]}
        onConfirm={(v) => run(() => post(`/api/inspection/${d.id}/decision`, { release: false, notes: v.notes }))} />
    </ModuleWorkspace>
  );
}
