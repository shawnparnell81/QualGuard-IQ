import { useState } from 'react';
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Link, Stack, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { z } from 'zod';
import { get, post, put } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { localDate, StatusChip, capaStatus, dispositions, fmtDate, fmtDateTime, ncrStatus, severity } from '../components/Status';
import { ActionsPanel, ApprovalsPanel, AttachmentsAndHistory } from '../components/RecordTabs';
import { CustomFieldsPanel } from '../components/RecordPanels';
import { Facts, Prose, SchemaForm, Section, StepDialog, useRunner, useLookup, lookupLabel, type LookupKind } from '../components/Kit';
import { Dialog, DialogActions, DialogContent, DialogTitle, MenuItem } from '@mui/material';
import { SignDialog } from '../components/SignDialog';
import { useWork } from '../work';
import { mono } from '../theme';

const mod = () => modules.find((m) => m.key === 'quality')!;
const today = () => localDate();
const num = (x: { getValue: () => unknown }) => <span style={{ fontFamily: mono }}>{String(x.getValue())}</span>;

interface NcrRow { id: string; number: string; title: string; source: string; severity: number; status: number; partNumber: string | null; lotNumber: string | null; detectedOn: string; siteCode: string; ownerName: string }

export function NcrList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const view = p.get('view') ?? 'open';
  const [q, setQ] = useState('');
  const [applied, setApplied] = useState('');
  const list = useQuery({ queryKey: ['ncrs', view, applied], queryFn: () => get<{ items: NcrRow[] }>(`/api/ncrs?pageSize=200${view === 'open' ? '&openOnly=true' : view === 'all' ? '' : `&status=${view}`}${applied ? `&q=${encodeURIComponent(applied)}` : ''}`) });
  const cols: ColumnDef<NcrRow, any>[] = [
    { header: 'NCR', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' }, { header: 'Source', accessorKey: 'source' },
    { header: 'Part / lot', accessorFn: (r) => [r.partNumber, r.lotNumber].filter(Boolean).join(' / ') || '—' },
    { header: 'Severity', accessorKey: 'severity', cell: (x) => <StatusChip status={severity[x.getValue()]} /> },
    { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={ncrStatus[x.getValue()]} /> },
    { header: 'Detected', accessorKey: 'detectedOn', cell: (x) => fmtDate(x.getValue()) }, { header: 'Site', accessorKey: 'siteCode' }, { header: 'Owner', accessorKey: 'ownerName' },
  ];
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Nonconformances' }]} title="Nonconformances"
      tabs={[{ label: 'Open', value: 'open' }, { label: 'Awaiting disposition', value: '2' }, { label: 'Closed', value: '4' }, { label: 'All', value: 'all' }]} tab={view} onTab={(v) => setP({ view: v })}
      actions={can('ncr.edit') ? <Button variant="contained" component={RouterLink} to="/ncr/new">Report nonconformance</Button> : undefined}>
      <Stack direction="row" gap={1} component="form" sx={{ mb: 2 }} onSubmit={(e) => { e.preventDefault(); setApplied(q); }}>
        <TextField size="small" placeholder="Filter by number, title, part or lot" value={q} onChange={(e) => setQ(e.target.value)} sx={{ width: 380 }} />
        <Button type="submit" variant="outlined">Filter</Button>
      </Stack>
      <DataTable data={list.data?.items ?? []} columns={cols} onRowClick={(r) => nav(`/ncr/${r.id}`)} empty="No nonconformances in this view." />
    </ModuleWorkspace>
  );
}

const ncrSchema = z.object({
  title: z.string().trim().min(1, 'Required').max(300), description: z.string().trim().min(1, 'Required').max(10000),
  source: z.string().min(1, 'Required'), siteId: z.string().uuid('Choose a site'), departmentId: z.string().optional(), ownerId: z.string().optional(),
  partNumber: z.string().max(60).optional(), lotNumber: z.string().max(60).optional(),
  quantityAffected: z.union([z.literal(''), z.coerce.number().min(0)]).optional(), detectedOn: z.string().min(10, 'Required'), severity: z.coerce.number().int().min(0).max(2),
  productId: z.string().optional(), partId: z.string().optional(), supplierId: z.string().optional(), customerId: z.string().optional(),
  workOrder: z.string().max(100).optional(), purchaseOrder: z.string().max(100).optional(), batchNumber: z.string().max(100).optional(), serialNumber: z.string().max(100).optional(),
  process: z.string().max(100).optional(), operation: z.string().max(100).optional(), defectCode: z.string().max(100).optional(),
  requirement: z.string().max(4000).optional(), actualCondition: z.string().max(4000).optional(),
});
const nz = (v?: string) => (v ? v : null);
const detailKeys = ['productId', 'partId', 'supplierId', 'customerId', 'workOrder', 'purchaseOrder', 'batchNumber', 'serialNumber', 'process', 'operation', 'defectCode', 'requirement', 'actualCondition'] as const;
const sources = ['Receiving', 'In-process', 'Final inspection', 'Customer', 'Supplier', 'Audit', 'Calibration', 'Other'];

export function NcrNew() {
  const nav = useNavigate();
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Nonconformances', to: '/ncr' }, { label: 'New' }]} title="Report a nonconformance">
      <SchemaForm schema={ncrSchema} submitLabel="Create NCR" onCancel={() => nav('/ncr')}
        defaults={{ title: '', description: '', source: '', siteId: '', departmentId: '', ownerId: '', partNumber: '', lotNumber: '', quantityAffected: '', detectedOn: today(), severity: 0,
          productId: '', partId: '', supplierId: '', customerId: '', workOrder: '', purchaseOrder: '', batchNumber: '', serialNumber: '', process: '', operation: '', defectCode: '', requirement: '', actualCondition: '' }}
        fields={[
          { name: 'title', label: 'Title', full: true }, { name: 'description', label: 'What is wrong (requirement versus actual)', type: 'multiline' },
          { name: 'source', label: 'Source', type: 'select', options: sources.map((s) => [s, s]) }, { name: 'severity', label: 'Severity', type: 'select', options: severity.map((s, i) => [i, s]) },
          { name: 'siteId', label: 'Site', type: 'site' }, { name: 'departmentId', label: 'Department (optional)', type: 'department' },
          { name: 'partNumber', label: 'Part number' }, { name: 'lotNumber', label: 'Lot / batch' },
          { name: 'quantityAffected', label: 'Quantity affected', type: 'number' }, { name: 'detectedOn', label: 'Detected on', type: 'date' },
          { name: 'ownerId', label: 'Owner', type: 'user' },
          { name: 'productId', label: 'Traceability', type: 'heading' },
          { name: 'productId', label: 'Product', type: 'lookup', lookup: 'product' }, { name: 'partId', label: 'Catalog part (sets revision)', type: 'lookup', lookup: 'part' },
          { name: 'supplierId', label: 'Supplier', type: 'lookup', lookup: 'supplier' }, { name: 'customerId', label: 'Customer', type: 'lookup', lookup: 'customer' },
          { name: 'purchaseOrder', label: 'Purchase order' }, { name: 'workOrder', label: 'Work order' },
          { name: 'batchNumber', label: 'Batch' }, { name: 'serialNumber', label: 'Serial number' },
          { name: 'process', label: 'Process' }, { name: 'operation', label: 'Operation' },
          { name: 'defectCode', label: 'Defect code' },
          { name: 'requirement', label: 'Requirement (specification, drawing note)', type: 'multiline' }, { name: 'actualCondition', label: 'Actual condition found', type: 'multiline' },
        ]}
        onSubmit={async (v) => {
          const details = Object.fromEntries(detailKeys.map((k) => [k, nz(v[k])]));
          const r = await post<{ id: string }>('/api/ncrs', { title: v.title, description: v.description, source: v.source, siteId: v.siteId, severity: v.severity, detectedOn: v.detectedOn,
            departmentId: v.departmentId || null, ownerId: v.ownerId || null, partNumber: v.partNumber || null, lotNumber: v.lotNumber || null, quantityAffected: v.quantityAffected === '' ? null : v.quantityAffected, details });
          nav(`/ncr/${r.id}`);
        }} />
    </ModuleWorkspace>
  );
}

interface NcrDetailDto { id: string; number: string; title: string; description: string; source: string; partNumber: string | null; lotNumber: string | null; quantityAffected: number | null; detectedOn: string; severity: number; status: number; containmentSummary: string | null; disposition: number; dispositionJustification: string | null; capaRequired: boolean; closureNotes: string | null; closedAt: string | null; ownerName: string; siteCode: string; capas: { id: string; number: string; status: number }[]; canEdit: boolean; allowed: string[];
  productId: string | null; partId: string | null; partRevision: string | null; supplierId: string | null; customerId: string | null; process: string | null; operation: string | null; workOrder: string | null; purchaseOrder: string | null;
  batchNumber: string | null; serialNumber: string | null; detectionPoint: string | null; defectCode: string | null; requirement: string | null; actualCondition: string | null; segregation: string | null; investigation: string | null;
  dispositionInstructions: string | null; quantityDispositioned: number | null; quantityScrapped: number | null; costOfPoorQuality: number | null; verificationNotes: string | null; verifiedAt: string | null; verifiedByName: string | null;
  reopenCount: number; reopenReason: string | null; rowVersion: number; productLabel: string | null; partLabel: string | null; supplierLabel: string | null; customerLabel: string | null }

export function NcrDetail() {
  const { id } = useParams();
  const nav = useNavigate();
  const qc = useQueryClient();
  const [tab, setTab] = useState('overview');
  const [dlg, setDlg] = useState<string | null>(null);
  const [signing, setSigning] = useState(false);
  const work = useWork();
  const q = useQuery({ queryKey: ['ncr', id], queryFn: () => get<NcrDetailDto>(`/api/ncrs/${id}`) });
  const refresh = () => { qc.invalidateQueries({ queryKey: ['ncr', id] }); qc.invalidateQueries({ queryKey: ['work'] }); };
  const { error, run } = useRunner(refresh);
  const d = q.data;
  if (!d) return <ModuleWorkspace module={mod()} crumbs={[{ label: 'Nonconformances', to: '/ncr' }]} title="Loading">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const approval = work.data?.approvals.find((a) => a.recordId === d.id);
  const s = d.status;
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Nonconformances', to: '/ncr' }, { label: d.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{d.number}</span><span>{d.title}</span><StatusChip status={ncrStatus[s]} /></Stack>}
      tabs={[{ label: 'Overview', value: 'overview' }, { label: 'Actions', value: 'actions' }, { label: 'Approvals', value: 'approvals' }, { label: 'Files & history', value: 'more' }]} tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1}>
        {approval && <Button variant="contained" color="secondary" onClick={() => setSigning(true)}>Sign disposition</Button>}
        {d.allowed.includes('contain') && <Button variant="outlined" onClick={() => setDlg('contain')}>Record containment</Button>}
        {d.allowed.includes('dispose') && <Button variant="contained" onClick={() => setDlg('disposition')}>Propose disposition</Button>}
        {d.allowed.includes('capa') && <Button variant="outlined" onClick={() => setDlg('capa')}>Create CAPA</Button>}
        {d.allowed.includes('details') && <Button variant="outlined" onClick={() => setDlg('details')}>Edit details</Button>}
        {d.allowed.includes('verify') && <Button variant="contained" onClick={() => setDlg('verify')}>Verify disposition</Button>}
        {d.allowed.includes('close') && <Button variant="contained" onClick={() => setDlg('close')}>Close NCR</Button>}
        {d.allowed.includes('reopen') && <Button variant="outlined" color="warning" onClick={() => setDlg('reopen')}>Reopen</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: '280px 1fr', gap: 3 }}>
        <Facts items={[['Source', d.source], ['Severity', severity[d.severity]], ['Part / rev / lot', [d.partNumber, d.partRevision && `rev ${d.partRevision}`, d.lotNumber].filter(Boolean).join(' / ') || '—'], ['Quantity affected', d.quantityAffected ?? '—'],
          ['Product', d.productLabel ?? '—'], ['Supplier', d.supplierId ? <Link component={RouterLink} to={`/suppliers/${d.supplierId}`}>{d.supplierLabel}</Link> : '—'], ['Customer', d.customerLabel ?? '—'],
          ['PO / WO', [d.purchaseOrder, d.workOrder].filter(Boolean).join(' / ') || '—'], ['Batch / serial', [d.batchNumber, d.serialNumber].filter(Boolean).join(' / ') || '—'],
          ['Process / operation', [d.process, d.operation].filter(Boolean).join(' / ') || '—'], ['Defect code', d.defectCode ?? '—'], ['Cost of poor quality', d.costOfPoorQuality != null ? d.costOfPoorQuality.toFixed(2) : '—'],
          ...(d.reopenCount ? [['Reopened', `${d.reopenCount}× — ${d.reopenReason ?? ''}`] as [string, string]] : []),
          ['Detected', fmtDate(d.detectedOn)], ['Owner', d.ownerName], ['Site', d.siteCode], ['CAPA required', d.capaRequired ? 'Yes' : 'No'], ['Closed', fmtDateTime(d.closedAt)]]}>
          <Box sx={{ mt: 2 }}><CustomFieldsPanel type="Ncr" id={d.id} canEdit={d.canEdit && s < 4} /></Box>
        </Facts>
        <Box sx={{ minWidth: 0 }}>
          {tab === 'overview' && <Stack gap={2}>
            <Section title="Description"><Prose label="Nonconformance" text={d.description} /><Prose label="Requirement" text={d.requirement} /><Prose label="Actual condition" text={d.actualCondition} /><Prose label="Investigation" text={d.investigation} /></Section>
            <Section title="Containment">{d.containmentSummary ? <Prose label="Actions taken" text={d.containmentSummary} /> : <Alert severity="warning">Containment has not been recorded.</Alert>}</Section>
            <Section title="Disposition">{d.disposition ? <><Prose label="Disposition" text={dispositions[d.disposition]} /><Prose label="Instructions" text={d.dispositionInstructions} />{d.verifiedAt && <Prose label={`Verified ${fmtDateTime(d.verifiedAt)} by ${d.verifiedByName}`} text={d.verificationNotes} />}<Prose label="Justification" text={d.dispositionJustification} /></> : <Alert severity="info">No disposition proposed yet.</Alert>}</Section>
            <Section title="Corrective action">{d.capas.length ? d.capas.map((c) => <Stack key={c.id} direction="row" gap={1} alignItems="center"><Link component={RouterLink} to={`/capa/${c.id}`} sx={{ fontFamily: mono }}>{c.number}</Link><StatusChip status={capaStatus[c.status]} /></Stack>) : 'No CAPA linked.'}</Section>
            {d.closureNotes && <Section title="Closure"><Prose label="Notes" text={d.closureNotes} /></Section>}
          </Stack>}
          {tab === 'actions' && <ActionsPanel type="Ncr" id={d.id} canEdit={d.allowed.includes('actions')} canVerifyAs={d.canEdit} onChanged={refresh} />}
          {tab === 'approvals' && <ApprovalsPanel type="Ncr" id={d.id} />}
          {tab === 'more' && <AttachmentsAndHistory type="Ncr" id={d.id} canEdit={d.canEdit && s < 4} />}
        </Box>
      </Box>
      <StepDialog open={dlg === 'contain'} title="Record containment" confirm="Save" onClose={() => setDlg(null)} inputs={[{ name: 'text', label: 'Containment actions (quarantine, sort, stop shipment)', type: 'multiline', initial: d.containmentSummary ?? '' }]}
        onConfirm={(v) => run(() => post(`/api/ncrs/${d.id}/containment`, v))} />
      <StepDialog open={dlg === 'disposition'} title="Propose disposition" intro="The disposition is routed for approval with an electronic signature." confirm="Submit for approval" onClose={() => setDlg(null)}
        inputs={[{ name: 'disposition', label: 'Disposition', type: 'select', options: dispositions.slice(1).map((x, i) => [i + 1, x]) }, { name: 'justification', label: 'Justification', type: 'multiline' },
          { name: 'instructions', label: 'Instructions (required for rework, repair, sort, deviation, concession, other)', type: 'multiline' },
          { name: 'quantityDispositioned', label: 'Quantity dispositioned (defaults to affected)', initial: d.quantityAffected != null ? String(d.quantityAffected) : '' },
          { name: 'quantityScrapped', label: 'Quantity scrapped' }, { name: 'capaRequired', label: 'CAPA required', type: 'checkbox' }]}
        onConfirm={(v) => run(() => post(`/api/ncrs/${d.id}/disposition`, { disposition: Number(v.disposition), justification: v.justification, capaRequired: v.capaRequired === 'true', instructions: v.instructions || null,
          quantityDispositioned: v.quantityDispositioned === '' ? null : Number(v.quantityDispositioned), quantityScrapped: v.quantityScrapped === '' ? null : Number(v.quantityScrapped) }))} />
      <StepDialog open={dlg === 'verify'} title="Verify disposition" intro="Confirm the approved disposition was carried out and every affected unit is accounted for." confirm="Record verification" onClose={() => setDlg(null)}
        inputs={[{ name: 'notes', label: 'Verification evidence', type: 'multiline' }, { name: 'quantityDispositioned', label: 'Quantity dispositioned', initial: d.quantityDispositioned != null ? String(d.quantityDispositioned) : '' },
          { name: 'costOfPoorQuality', label: 'Cost of poor quality', initial: d.costOfPoorQuality != null ? String(d.costOfPoorQuality) : '' }]}
        onConfirm={(v) => run(() => post(`/api/ncrs/${d.id}/verify`, { notes: v.notes, quantityDispositioned: v.quantityDispositioned === '' ? null : Number(v.quantityDispositioned), costOfPoorQuality: v.costOfPoorQuality === '' ? null : Number(v.costOfPoorQuality) }))} />
      <StepDialog open={dlg === 'reopen'} title={`Reopen ${d.number}`} intro="The NCR returns to containment. The reason is kept in the history." confirm="Reopen" danger onClose={() => setDlg(null)} inputs={[{ name: 'text', label: 'Reason', type: 'multiline' }]}
        onConfirm={(v) => run(() => post(`/api/ncrs/${d.id}/reopen`, v))} />
      <NcrDetailsDialog open={dlg === 'details'} d={d} onClose={() => setDlg(null)} onSave={(details) => run(() => put(`/api/ncrs/${d.id}/details`, { details, rowVersion: d.rowVersion }))} />
      <StepDialog open={dlg === 'capa'} title="Create linked CAPA" intro="Title and problem statement default to the NCR." confirm="Create CAPA" onClose={() => setDlg(null)}
        inputs={[{ name: 'title', label: 'Title', initial: d.title }, { name: 'problemStatement', label: 'Problem statement', type: 'multiline', initial: d.description }]}
        onConfirm={async (v) => { const r = await post<{ id: string }>(`/api/ncrs/${d.id}/capa`, { ...v, ownerId: null }); nav(`/capa/${r.id}`); }} />
      <StepDialog open={dlg === 'close'} title={`Close ${d.number}`} confirm="Close NCR" onClose={() => setDlg(null)} inputs={[{ name: 'text', label: 'Closure notes and evidence', type: 'multiline' }]}
        onConfirm={(v) => run(() => post(`/api/ncrs/${d.id}/close`, v))} />
      {signing && approval && <SignDialog approvalId={approval.id} recordNumber={approval.recordNumber} stepName={approval.stepName} onClose={() => { setSigning(false); refresh(); }} />}
    </ModuleWorkspace>
  );
}

const detailText: [keyof NcrDetailDto, string, boolean?][] = [['purchaseOrder', 'Purchase order'], ['workOrder', 'Work order'], ['batchNumber', 'Batch'], ['serialNumber', 'Serial number'], ['partRevision', 'Part revision'],
  ['process', 'Process'], ['operation', 'Operation'], ['detectionPoint', 'Detection point'], ['defectCode', 'Defect code'], ['costOfPoorQuality', 'Cost of poor quality'],
  ['requirement', 'Requirement', true], ['actualCondition', 'Actual condition', true], ['segregation', 'Segregation', true], ['investigation', 'Investigation', true]];
const detailLookups: [keyof NcrDetailDto, string, LookupKind][] = [['productId', 'Product', 'product'], ['partId', 'Part', 'part'], ['supplierId', 'Supplier', 'supplier'], ['customerId', 'Customer', 'customer']];

function LookupSelect({ kind, label, value, onChange }: { kind: LookupKind; label: string; value: string; onChange: (v: string) => void }) {
  const rows = useLookup(kind);
  return <TextField select label={label} value={value} onChange={(e) => onChange(e.target.value)}><MenuItem value="">None</MenuItem>{(rows.data ?? []).map((r) => <MenuItem key={r.id} value={r.id}>{lookupLabel(r)}</MenuItem>)}</TextField>;
}

function NcrDetailsDialog({ open, d, onClose, onSave }: { open: boolean; d: NcrDetailDto; onClose: () => void; onSave: (v: Record<string, unknown>) => Promise<void> }) {
  const init = () => Object.fromEntries([...detailText, ...detailLookups].map(([k]) => [k, d[k] == null ? '' : String(d[k])]));
  const [v, setV] = useState<Record<string, string>>(init);
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="md" TransitionProps={{ onEnter: () => setV(init()) }}>
      <DialogTitle>Traceability and investigation</DialogTitle>
      <DialogContent>
        <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2, pt: 1 }}>
          {detailLookups.map(([k, l, kind]) => <LookupSelect key={k} kind={kind} label={l} value={v[k]} onChange={(x) => setV({ ...v, [k]: x })} />)}
          {detailText.map(([k, l, multi]) => <TextField key={k} label={l} value={v[k]} multiline={multi} minRows={multi ? 2 : undefined} sx={multi ? { gridColumn: '1 / -1' } : undefined} onChange={(e) => setV({ ...v, [k]: e.target.value })} />)}
        </Box>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" onClick={async () => { await onSave(Object.fromEntries(Object.entries(v).map(([k, x]) => [k, x === '' ? null : k === 'costOfPoorQuality' ? Number(x) : x]))); onClose(); }}>Save</Button>
      </DialogActions>
    </Dialog>
  );
}

interface CapaRow { id: string; number: string; title: string; capaType: string; source: string; status: number; effectivenessDueOn: string | null; siteCode: string; ownerName: string }

export function CapaList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const status = p.get('status') ?? '';
  const list = useQuery({ queryKey: ['capas', status], queryFn: () => get<{ items: CapaRow[] }>(`/api/capas?pageSize=200${status ? `&status=${status}` : ''}`) });
  const cols: ColumnDef<CapaRow, any>[] = [
    { header: 'CAPA', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' }, { header: 'Type', accessorKey: 'capaType' }, { header: 'Source', accessorKey: 'source' },
    { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={capaStatus[x.getValue()]} /> },
    { header: 'Effectiveness due', accessorKey: 'effectivenessDueOn', cell: (x) => fmtDate(x.getValue()) }, { header: 'Site', accessorKey: 'siteCode' }, { header: 'Owner', accessorKey: 'ownerName' },
  ];
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'CAPA' }]} title="Corrective and preventive actions"
      tabs={[{ label: 'All', value: '' }, { label: 'Open', value: '0' }, { label: 'Implementation', value: '2' }, { label: 'Effectiveness check', value: '3' }, { label: 'Closed', value: '4' }]} tab={status} onTab={(v) => setP(v ? { status: v } : {})}
      actions={can('capa.edit') ? <Button variant="contained" component={RouterLink} to="/capa/new">New CAPA</Button> : undefined}>
      <DataTable data={list.data?.items ?? []} columns={cols} onRowClick={(r) => nav(`/capa/${r.id}`)} empty="No CAPAs in this view." />
    </ModuleWorkspace>
  );
}

const capaSchema = z.object({
  title: z.string().trim().min(1, 'Required').max(300), problemStatement: z.string().trim().min(1, 'Required').max(10000), capaType: z.enum(['Corrective', 'Preventive']),
  source: z.string().min(1, 'Required'), siteId: z.string().uuid('Choose a site'), departmentId: z.string().optional(), ownerId: z.string().optional(),
});

export function CapaNew() {
  const nav = useNavigate();
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'CAPA', to: '/capa' }, { label: 'New' }]} title="New CAPA">
      <SchemaForm schema={capaSchema} submitLabel="Create CAPA" onCancel={() => nav('/capa')}
        defaults={{ title: '', problemStatement: '', capaType: 'Corrective', source: '', siteId: '', departmentId: '', ownerId: '' }}
        fields={[{ name: 'title', label: 'Title', full: true }, { name: 'problemStatement', label: 'Problem statement', type: 'multiline' },
          { name: 'capaType', label: 'Type', type: 'select', options: [['Corrective', 'Corrective'], ['Preventive', 'Preventive']] },
          { name: 'source', label: 'Source', type: 'select', options: ['Customer complaint', 'Audit', 'NCR trend', 'Management review', 'Risk', 'Other'].map((s) => [s, s]) },
          { name: 'siteId', label: 'Site', type: 'site' }, { name: 'departmentId', label: 'Department (optional)', type: 'department' }, { name: 'ownerId', label: 'Owner', type: 'user' }]}
        onSubmit={async (v) => { const r = await post<{ id: string }>('/api/capas', { ...v, departmentId: v.departmentId || null, ownerId: v.ownerId || null }); nav(`/capa/${r.id}`); }} />
    </ModuleWorkspace>
  );
}

interface CapaDetailDto { id: string; number: string; title: string; problemStatement: string; capaType: string; source: string; status: number; rootCauseMethod: string | null; rootCause: string | null; effectivenessCriteria: string | null; effectivenessDueOn: string | null; effectivenessResult: string | null; wasEffective: boolean | null; closedAt: string | null; ownerName: string; verifiedByName: string | null; siteCode: string; canEdit: boolean; canVerify: boolean; allowed: string[];
  scope: string | null; impact: string | null; risk: number; immediateCorrection: string | null; containment: string | null; contributingCauses: string | null; escapePoint: string | null;
  recurrenceMonitoring: string | null; recurrenceReviewOn: string | null; reopenCount: number; reopenReason: string | null; rowVersion: number;
  analysis: { whys?: string[]; fishbone?: Record<string, string[]>; isIsNot?: { dimension: string; is: string; isNot: string }[] }; team: { id: string; displayName: string }[] }

const fishboneCats = ['Man', 'Machine', 'Method', 'Material', 'Measurement', 'Environment'];
const lines = (t: string) => t.split('\n').map((x) => x.trim()).filter(Boolean);
function parseAnalysis(v: Record<string, string>) {
  const fishbone: Record<string, string[]> = {};
  for (const l of lines(v.fishbone ?? '')) {
    const [cat, ...rest] = l.split(':');
    const c = fishboneCats.find((x) => x.toLowerCase() === cat.trim().toLowerCase());
    if (!c || !rest.join(':').trim()) throw new Error(`Fishbone line "${l}" must start with one of: ${fishboneCats.join(', ')}, then a colon.`);
    (fishbone[c] ??= []).push(rest.join(':').trim());
  }
  const isIsNot = lines(v.isIsNot ?? '').map((l) => { const [dimension, is, isNot] = l.split('|').map((x) => (x ?? '').trim()); return { dimension, is: is ?? '', isNot: isNot ?? '' }; });
  return { whys: lines(v.whys ?? ''), fishbone, isIsNot };
}

function AnalysisView({ a }: { a: CapaDetailDto['analysis'] }) {
  const fb = Object.entries(a.fishbone ?? {});
  if (!a.whys?.length && !fb.length && !a.isIsNot?.length) return null;
  return (
    <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { md: '1fr 1fr' }, mt: 1 }}>
      {!!a.whys?.length && <Box><Typography variant="caption" color="text.secondary">Five Whys</Typography><ol style={{ margin: '4px 0', paddingLeft: 20 }}>{a.whys.map((w, i) => <li key={i}><Typography variant="body2">{w}</Typography></li>)}</ol></Box>}
      {!!fb.length && <Box><Typography variant="caption" color="text.secondary">Fishbone</Typography>{fb.map(([c, xs]) => <Typography key={c} variant="body2"><b>{c}:</b> {xs.join('; ')}</Typography>)}</Box>}
      {!!a.isIsNot?.length && <Box sx={{ gridColumn: '1 / -1' }}><Typography variant="caption" color="text.secondary">Is / Is Not</Typography>
        <Box component="table" sx={{ width: '100%', fontSize: 14, borderCollapse: 'collapse', '& td, & th': { borderBottom: 1, borderColor: 'divider', p: 0.75, textAlign: 'left' } }}>
          <thead><tr><th>Dimension</th><th>Is</th><th>Is not</th></tr></thead>
          <tbody>{a.isIsNot.map((r, i) => <tr key={i}><td>{r.dimension}</td><td>{r.is}</td><td>{r.isNot}</td></tr>)}</tbody></Box></Box>}
    </Box>
  );
}

export function CapaDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const [tab, setTab] = useState('overview');
  const [dlg, setDlg] = useState<string | null>(null);
  const q = useQuery({ queryKey: ['capa', id], queryFn: () => get<CapaDetailDto>(`/api/capas/${id}`) });
  const { error, run } = useRunner(() => qc.invalidateQueries({ queryKey: ['capa', id] }));
  const d = q.data;
  if (!d) return <ModuleWorkspace module={mod()} crumbs={[{ label: 'CAPA', to: '/capa' }]} title="Loading">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const s = d.status;
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'CAPA', to: '/capa' }, { label: d.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{d.number}</span><span>{d.title}</span><StatusChip status={capaStatus[s]} /></Stack>}
      tabs={[{ label: 'Overview', value: 'overview' }, { label: 'Actions', value: 'actions' }, { label: 'Files & history', value: 'more' }]} tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1}>
        {d.allowed.includes('plan') && <Button variant="outlined" onClick={() => setDlg('plan')}>Scope & containment</Button>}
        {d.allowed.includes('reopen') && <Button variant="outlined" color="warning" onClick={() => setDlg('reopen')}>Reopen</Button>}
        {d.allowed.includes('rootCause') && <Button variant="outlined" onClick={() => setDlg('rca')}>{d.rootCause ? 'Edit root cause' : 'Record root cause'}</Button>}
        {d.allowed.includes('check') && <Button variant="contained" onClick={() => run(() => post(`/api/capas/${d.id}/effectiveness-check`))}>Ready for effectiveness check</Button>}
        {d.allowed.includes('verify') && <Button variant="contained" color="secondary" onClick={() => setDlg('verify')}>Verify effectiveness</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: '280px 1fr', gap: 3 }}>
        <Facts items={[['Type', d.capaType], ['Source', d.source], ['Risk', severity[d.risk]], ['Owner', d.ownerName], ['Team', d.team.map((t) => t.displayName).join(', ') || '—'], ['Site', d.siteCode],
          ['Recurrence review', fmtDate(d.recurrenceReviewOn)], ...(d.reopenCount ? [['Reopened', `${d.reopenCount}× — ${d.reopenReason ?? ''}`] as [string, string]] : []), ['Effectiveness due', fmtDate(d.effectivenessDueOn)], ['Verified by', d.verifiedByName ?? '—'], ['Closed', fmtDateTime(d.closedAt)]]}>
          <Box sx={{ mt: 2 }}><CustomFieldsPanel type="Capa" id={d.id} canEdit={d.canEdit && s < 4} /></Box>
        </Facts>
        <Box sx={{ minWidth: 0 }}>
          {tab === 'overview' && <Stack gap={2}>
            <Section title="Problem"><Prose label="Problem statement" text={d.problemStatement} /><Prose label="Scope" text={d.scope} /><Prose label="Impact" text={d.impact} /></Section>
            <Section title="Correction and containment">{d.immediateCorrection || d.containment ? <><Prose label="Immediate correction" text={d.immediateCorrection} /><Prose label="Containment" text={d.containment} /></> : <Box sx={{ color: 'text.secondary', fontSize: 14 }}>Not recorded.</Box>}</Section>
            <Section title="Root cause">{d.rootCause ? <><Prose label="Method" text={d.rootCauseMethod} /><Prose label="Root cause" text={d.rootCause} /><Prose label="Contributing causes" text={d.contributingCauses} /><Prose label="Escape point" text={d.escapePoint} /><AnalysisView a={d.analysis} /></> : <Alert severity="warning">Root cause not yet determined.</Alert>}</Section>
            {d.recurrenceMonitoring && <Section title="Recurrence monitoring"><Prose label="Plan" text={d.recurrenceMonitoring} /></Section>}
            <Section title="Effectiveness"><Prose label="Criteria" text={d.effectivenessCriteria} />{d.effectivenessResult ? <><Prose label="Result" text={d.effectivenessResult} /><StatusChip status={d.wasEffective ? 'Verified' : 'Rejected'} /></> : <Box sx={{ color: 'text.secondary', fontSize: 14 }}>Verified by someone other than the owner once all actions are closed.</Box>}</Section>
          </Stack>}
          {tab === 'actions' && <ActionsPanel type="Capa" id={d.id} canEdit={d.allowed.includes('actions')} canVerifyAs={d.canEdit} onChanged={() => qc.invalidateQueries({ queryKey: ['capa', id] })} />}
          {tab === 'more' && <AttachmentsAndHistory type="Capa" id={d.id} canEdit={d.canEdit && s < 4} />}
        </Box>
      </Box>
      <StepDialog open={dlg === 'rca'} title="Root cause analysis" confirm="Save" onClose={() => setDlg(null)}
        inputs={[{ name: 'method', label: 'Method', type: 'select', initial: d.rootCauseMethod ?? '5 Whys', options: ['5 Whys', 'Fishbone (Ishikawa)', 'Fault tree', 'Is / is not', '8D', 'Other'].map((x) => [x, x]) },
          { name: 'rootCause', label: 'Root cause', type: 'multiline', initial: d.rootCause ?? '' }, { name: 'effectivenessCriteria', label: 'How effectiveness will be judged', type: 'multiline', initial: d.effectivenessCriteria ?? '' },
          { name: 'effectivenessDueOn', label: 'Effectiveness check due', type: 'date', initial: d.effectivenessDueOn ?? '' },
          { name: 'contributingCauses', label: 'Contributing causes', type: 'multiline', initial: d.contributingCauses ?? '' }, { name: 'escapePoint', label: 'Escape point (where it should have been caught)', initial: d.escapePoint ?? '' },
          { name: 'whys', label: 'Five Whys (one per line)', type: 'multiline', initial: (d.analysis.whys ?? []).join('\n') },
          { name: 'fishbone', label: 'Fishbone causes (one per line, "Machine: worn die")', type: 'multiline', initial: Object.entries(d.analysis.fishbone ?? {}).flatMap(([c, xs]) => xs.map((x) => `${c}: ${x}`)).join('\n'), helper: fishboneCats.join(', ') },
          { name: 'isIsNot', label: 'Is / Is Not (one per line, "Where | Edge A | Edge B")', type: 'multiline', initial: (d.analysis.isIsNot ?? []).map((r) => `${r.dimension} | ${r.is} | ${r.isNot}`).join('\n') }]}
        onConfirm={async (v) => { const analysis = parseAnalysis(v); await run(() => post(`/api/capas/${d.id}/root-cause`, { method: v.method, rootCause: v.rootCause, effectivenessCriteria: v.effectivenessCriteria, effectivenessDueOn: v.effectivenessDueOn, contributingCauses: v.contributingCauses || null, escapePoint: v.escapePoint || null, analysis })); }} />
      <StepDialog open={dlg === 'plan'} title="Scope, correction and containment" confirm="Save" onClose={() => setDlg(null)}
        inputs={[{ name: 'scope', label: 'Scope', type: 'multiline', initial: d.scope ?? '' }, { name: 'impact', label: 'Impact', type: 'multiline', initial: d.impact ?? '' },
          { name: 'risk', label: 'Risk', type: 'select', options: severity.map((x, i) => [i, x]), initial: String(d.risk) },
          { name: 'immediateCorrection', label: 'Immediate correction', type: 'multiline', initial: d.immediateCorrection ?? '' }, { name: 'containment', label: 'Containment', type: 'multiline', initial: d.containment ?? '' },
          { name: 'team', label: 'Investigation team', type: 'users', initial: d.team.map((t) => t.id).join(',') },
          { name: 'recurrenceMonitoring', label: 'Recurrence monitoring plan', type: 'multiline', initial: d.recurrenceMonitoring ?? '' }, { name: 'recurrenceReviewOn', label: 'Recurrence review date', type: 'date', initial: d.recurrenceReviewOn ?? '' }]}
        onConfirm={(v) => run(() => put(`/api/capas/${d.id}/plan`, { ...v, risk: Number(v.risk), team: v.team.split(',').filter(Boolean), recurrenceReviewOn: v.recurrenceReviewOn || null, rowVersion: d.rowVersion }))} />
      <StepDialog open={dlg === 'reopen'} title={`Reopen ${d.number}`} intro="The CAPA returns to implementation." confirm="Reopen" danger onClose={() => setDlg(null)} inputs={[{ name: 'text', label: 'Reason', type: 'multiline' }]}
        onConfirm={(v) => run(() => post(`/api/capas/${d.id}/reopen`, v))} />
      <StepDialog open={dlg === 'verify'} title="Verify effectiveness" confirm="Record verification" onClose={() => setDlg(null)}
        inputs={[{ name: 'effective', label: 'Was the action effective?', type: 'checkbox', initial: 'true' }, { name: 'result', label: 'Evidence and result', type: 'multiline' }]}
        onConfirm={(v) => run(() => post(`/api/capas/${d.id}/verify`, { effective: v.effective === 'true', result: v.result }))} />
    </ModuleWorkspace>
  );
}
