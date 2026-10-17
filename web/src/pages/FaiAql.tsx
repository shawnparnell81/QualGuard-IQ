import { useState, type ReactNode } from 'react';
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, LinearProgress, MenuItem, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { z } from 'zod';
import { get, post } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, fmtDate, fmtDateTime } from '../components/Status';
import { AttachmentsAndHistory } from '../components/RecordTabs';
import { Facts, SchemaForm, Section, StepDialog, useRunner, useLookup, lookupLabel, useUsers } from '../components/Kit';
import { mono } from '../theme';

const mod = () => modules.find((m) => m.key === 'faiaql')!;
const num = (x: { getValue: () => unknown }) => <span style={{ fontFamily: mono }}>{String(x.getValue())}</span>;
const nul = (v?: string) => (v ? v : null);
const faiStatusDomain = ['Draft', 'InProgress', 'Review', 'Returned', 'PendingApproval', 'Approved', 'Rejected', 'Closed', 'Cancelled'];
const faiTypes = ['Full', 'Partial', 'Delta', 'Requalification'];
const faiReasons = ['NewProduct', 'NewPart', 'NewRevision', 'DrawingRevision', 'EngineeringChange', 'CustomerRequirement', 'SupplierRequirement'];
const charTypes = ['Critical', 'Key', 'Significant', 'Major', 'Minor', 'General'];
const words = (s: string) => s.replace(/([a-z])([A-Z])/g, '$1 $2');
const opts = (a: string[]) => a.map((x) => [x, words(x)] as [string, string]);

interface Paged<T> { items: T[]; metadata: { totalItems: number } }
interface Series { name: string; points: { label: string; value: number }[]; unit?: string | null }

/** Coverage meter: share of characteristics with a passing (or N/A) result. */
function Coverage({ passed, failed, total }: { passed: number; failed: number; total: number }) {
  const pct = total ? Math.round((passed / total) * 100) : 0;
  return <Stack direction="row" gap={1} alignItems="center" sx={{ minWidth: 150 }}>
    <LinearProgress variant="determinate" value={pct} color={failed ? 'error' : 'success'} sx={{ flex: 1, height: 6, borderRadius: 3 }} />
    <Typography fontSize={12} sx={{ fontFamily: mono }}>{passed}/{total}{failed ? ` · ${failed}✕` : ''}</Typography></Stack>;
}

// ---------------- FAI ----------------

interface FaiRow { id: string; number: string; title: string; type: number; status: number; faiRevision: number; partNumber: string; partRevision: string | null; drawingNumber: string | null; drawingRevision: string | null;
  supplierCode: string | null; dueOn: string | null; overdue: boolean; inspectorName: string; reviewerName: string | null; characteristics: number; passed: number; failed: number }
const faiTypeDetail = ['Full', 'Partial', 'Delta', 'EngineeringChange', 'ProcessChange', 'SupplierChange', 'ToolingChange', 'MaterialChange', 'Requalification'];

export function FaiList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const view = p.get('view') ?? 'active';
  const [q, setQ] = useState('');
  const [applied, setApplied] = useState('');
  const filter = { active: '', pending: '&status=4', overdue: '&overdue=true', review: '&status=2', closed: '&status=7', all: '' }[view] ?? '';
  const list = useQuery({ queryKey: ['fai', view, applied], queryFn: () => get<Paged<FaiRow>>(`/api/v1/fai/filter?pageSize=300${filter}${applied ? `&search=${encodeURIComponent(applied)}` : ''}`) });
  const rows = (list.data?.items ?? []).filter((r) => view !== 'active' || r.status <= 4);
  const cols: ColumnDef<FaiRow, any>[] = [
    { header: 'FAI', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' }, { header: 'Type', accessorKey: 'type', cell: (x) => words(faiTypeDetail[x.getValue()]) },
    { header: 'Part / rev', accessorKey: 'partNumber', cell: (x) => `${x.getValue()}${x.row.original.partRevision ? ' / ' + x.row.original.partRevision : ''}` },
    { header: 'Drawing', accessorKey: 'drawingNumber', cell: (x) => x.getValue() ? `${x.getValue()} rev ${x.row.original.drawingRevision ?? '—'}` : '—' },
    { header: 'Coverage', accessorKey: 'passed', cell: (x) => <Coverage passed={x.getValue()} failed={x.row.original.failed} total={x.row.original.characteristics} /> },
    { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={faiStatusDomain[x.getValue()]} /> },
    { header: 'Due', accessorKey: 'dueOn', cell: (x) => <Box component="span" sx={{ color: x.row.original.overdue ? 'error.main' : undefined, fontWeight: x.row.original.overdue ? 600 : undefined }}>{fmtDate(x.getValue())}</Box> },
    { header: 'Inspector', accessorKey: 'inspectorName' },
  ];
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'First articles' }]} title="First article inspections"
      tabs={[{ label: 'Active', value: 'active' }, { label: 'In review', value: 'review' }, { label: 'Awaiting signatures', value: 'pending' }, { label: 'Overdue', value: 'overdue' }, { label: 'Closed', value: 'closed' }, { label: 'All', value: 'all' }]}
      tab={view} onTab={(v) => setP(v === 'active' ? {} : { view: v })}
      actions={can('fai.edit') ? <Button variant="contained" component={RouterLink} to="/fai/new">Start a first article</Button> : undefined}>
      <Stack direction="row" gap={1} component="form" sx={{ mb: 2 }} onSubmit={(e) => { e.preventDefault(); setApplied(q); }}>
        <TextField size="small" placeholder="FAI, part, drawing, lot or serial" value={q} onChange={(e) => setQ(e.target.value)} sx={{ width: 360 }} />
        <Button type="submit" variant="outlined">Filter</Button>
      </Stack>
      <DataTable data={rows} columns={cols} onRowClick={(r) => nav(`/fai/${r.id}`)} empty="No first articles in this view." />
    </ModuleWorkspace>
  );
}

const useDrawings = () => useQuery({ queryKey: ['drawings-lookup'], retry: false, queryFn: async () => { try { return (await get<{ items: { id: string; number: string; title: string }[] }>('/api/documents?kind=1&pageSize=200')).items; } catch { return []; } } });

export function FaiNew() {
  const nav = useNavigate();
  const drawings = useDrawings();
  const schema = z.object({
    title: z.string().trim().min(1, 'Required').max(300), faiType: z.string(), reason: z.string(), productId: z.string().uuid('Choose the product'), partId: z.string().uuid('Choose the part'),
    drawingId: z.string().optional(), drawingRevision: z.string().max(20).optional(), supplierId: z.string().optional(), lotNumber: z.string().max(100).optional(), serialNumber: z.string().max(100).optional(),
    dueDate: z.string().optional(), siteId: z.string().uuid('Choose a site'), reviewerId: z.string().optional(), comments: z.string().max(4000).optional(),
  });
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'First articles', to: '/fai' }, { label: 'New' }]} title="Start a first article">
      <SchemaForm schema={schema} submitLabel="Create FAI" onCancel={() => nav('/fai')}
        defaults={{ title: '', faiType: 'Full', reason: 'NewPart', productId: '', partId: '', drawingId: '', drawingRevision: '', supplierId: '', lotNumber: '', serialNumber: '', dueDate: '', siteId: '', reviewerId: '', comments: '' }}
        fields={[{ name: 'title', label: 'Title', full: true }, { name: 'faiType', label: 'FAI type', type: 'select', options: opts(faiTypes) }, { name: 'reason', label: 'Reason', type: 'select', options: opts(faiReasons) },
          { name: 'what' as never, label: 'Article', type: 'heading' }, { name: 'productId', label: 'Product', type: 'lookup', lookup: 'product' }, { name: 'partId', label: 'Part', type: 'lookup', lookup: 'part' },
          { name: 'drawingId', label: 'Drawing (balloons map onto the FAI)', type: 'select', options: [['', 'None'], ...(drawings.data ?? []).map((d) => [d.id, `${d.number} — ${d.title}`] as [string, string])] },
          { name: 'drawingRevision', label: 'Drawing revision' }, { name: 'supplierId', label: 'Supplier (optional)', type: 'lookup', lookup: 'supplier' }, { name: 'lotNumber', label: 'Lot' }, { name: 'serialNumber', label: 'Serial' },
          { name: 'who' as never, label: 'Responsibility', type: 'heading' }, { name: 'siteId', label: 'Site', type: 'site' }, { name: 'reviewerId', label: 'Reviewer (optional now, needed to submit)', type: 'user' },
          { name: 'dueDate', label: 'Due', type: 'date' }, { name: 'comments', label: 'Notes', type: 'multiline' }]}
        onSubmit={async (v) => {
          const r = await post<{ id: string }>('/api/v1/fai', { ...v, drawingId: nul(v.drawingId), drawingRevision: nul(v.drawingRevision), supplierId: nul(v.supplierId), lotNumber: nul(v.lotNumber), serialNumber: nul(v.serialNumber),
            dueDate: nul(v.dueDate), reviewerId: nul(v.reviewerId), comments: nul(v.comments) });
          nav(`/fai/${r.id}`);
        }} />
    </ModuleWorkspace>
  );
}

interface Char { id: string; balloonNumber: number; characteristicType: string; description: string; dataType: string; nominalValue: number | null; lowerSpecificationLimit: number | null; upperSpecificationLimit: number | null;
  unitOfMeasure: string | null; inspectionMethod: string | null; acceptanceCriteria: string | null; drawingReference: string | null; status: string; measuredValue: number | null; carriedOver: boolean; ncrId: string | null }
interface Approval { id: string; approvalRole: string; status: string; approverName: string | null; comment: string | null; decidedAt: string | null; meaning: string | null; step: number }
interface FaiDetailDto {
  fai: { id: string; faiNumber: string; title: string; faiType: string; typeDetail: string; reason: string; status: string; faiRevision: number; lotNumber: string | null; serialNumber: string | null; dueDate: string | null; customerApprovalRequired: boolean; version: number; comments: string | null };
  part: { partNumber: string; partRevision: string | null }; drawing: { drawingNumber: string | null; drawingRevision: string | null }; reviewNotes: string | null; customerApprovalReference: string | null;
  site: { code: string } | null; product: { name: string } | null; supplier: { code: string; legalName: string } | null; inspectorName: string; reviewerName: string | null;
  characteristics: Char[]; accountability: { total: number; open: number; passed: number; failed: number; unresolvedFailures: unknown[]; missing: number[] };
  approvals: Approval[]; balloonDrawings: { id: string; drawingRevision: string; attachment: { fileName: string } | null }[]; revisions: { faiRevision: number; reason: string; createdAt: string }[]; allowed: string[];
}
const limits = (c: Char) => c.dataType === 'Numeric' ? `${c.lowerSpecificationLimit ?? '—'} … ${c.upperSpecificationLimit ?? '—'} ${c.unitOfMeasure ?? ''}` : c.acceptanceCriteria ?? '—';

/** Balloon grid: one tile per characteristic, colored by result, so open and failed balloons stand out at a glance. */
function BalloonBoard({ chars, onPick }: { chars: Char[]; onPick?: (c: Char) => void }) {
  const tone: Record<string, string> = { Pass: '#2E7D5B', Fail: '#B3261E', NotApplicable: '#6B6F76', NotInspected: '#FFFFFF' };
  return <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1 }}>
    {chars.map((c) => <Box key={c.id} role={onPick ? 'button' : undefined} onClick={() => onPick?.(c)} title={`${c.description} · ${words(c.status)}`}
      sx={{ width: 44, height: 44, borderRadius: '50%', border: 2, borderColor: c.characteristicType === 'Critical' ? '#B3261E' : 'divider', bgcolor: tone[c.status] ?? '#fff', color: c.status === 'NotInspected' ? 'text.primary' : '#fff',
        display: 'flex', alignItems: 'center', justifyContent: 'center', fontFamily: mono, fontWeight: 700, cursor: onPick ? 'pointer' : 'default' }}>{c.balloonNumber}</Box>)}
  </Box>;
}

export function FaiDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const nav = useNavigate();
  const [tab, setTab] = useState('characteristics');
  const [dlg, setDlg] = useState<string | null>(null);
  const [pick, setPick] = useState<Char | null>(null);
  const q = useQuery({ queryKey: ['fai-detail', id], queryFn: () => get<FaiDetailDto>(`/api/v1/fai/${id}/detail`) });
  const eq = useQuery({ queryKey: ['eq-lookup'], retry: false, queryFn: async () => { try { const r = await get<{ items?: { id: string; number: string; name: string; status: number }[] } | { id: string; number: string; name: string; status: number }[]>('/api/calibration/equipment?pageSize=300'); return (Array.isArray(r) ? r : r.items ?? []).filter((e) => e.status === 0); } catch { return []; } } });
  const users = useUsers();
  const refresh = () => qc.invalidateQueries({ queryKey: ['fai-detail', id] });
  const { error, run } = useRunner(refresh);
  const d = q.data;
  if (!d) return <ModuleWorkspace module={mod()} crumbs={[{ label: 'First articles', to: '/fai' }]} title="Loading">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const f = d.fai;
  const a = (k: string) => d.allowed.includes(k);
  const act = (path: string, body: Record<string, unknown>) => run(() => post(`/api/v1/fai/${f.id}/${path}`, { expectedVersion: f.version, ...body }));
  const acc = d.accountability;
  const sig = (name: string) => [{ name: 'comment', label: 'Comment', type: 'multiline' as const }, { name: 'password', label: 'Your password (electronic signature)', type: 'password' as const, helper: name }];
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'First articles', to: '/fai' }, { label: f.faiNumber }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono, whiteSpace: 'nowrap' }}>{f.faiNumber}</span><span>{f.title}</span><StatusChip status={words(f.status)} />{f.faiRevision > 1 && <Typography fontSize={13} color="text.secondary">rev {f.faiRevision}</Typography>}</Stack>}
      tabs={[{ label: `Characteristics (${d.characteristics.length})`, value: 'characteristics' }, { label: 'Balloon board', value: 'board' }, { label: `Signatures (${d.approvals.length})`, value: 'approvals' }, { label: 'Drawings & revisions', value: 'drawings' }, { label: 'Files & history', value: 'more' }]}
      tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1} flexWrap="wrap">
        {a('characteristics') && <Button variant="outlined" onClick={() => setDlg('char')}>Add characteristic</Button>}
        {a('start-inspection') && <Button variant="contained" onClick={() => setDlg('start')}>Start inspection</Button>}
        {a('complete-inspection') && <Button variant="outlined" onClick={() => act('complete-inspection', { comment: 'Inspection complete' })}>Check completeness</Button>}
        {a('submit') && <Button variant="contained" onClick={() => setDlg('submit')}>Submit for review</Button>}
        {a('request-approval') && <Button variant="contained" onClick={() => setDlg('request')}>Request signatures</Button>}
        {a('return') && <Button color="warning" onClick={() => setDlg('return')}>Return for correction</Button>}
        {a('approve') && <Button variant="contained" onClick={() => setDlg('approve')}>Sign approval</Button>}
        {a('approval-return') && <Button color="warning" onClick={() => setDlg('approval-return')}>Return</Button>}
        {a('reject') && <Button color="error" onClick={() => setDlg('reject')}>Reject</Button>}
        {a('ncr') && <Button color="error" variant="outlined" onClick={() => setDlg('ncr')}>Raise NCR</Button>}
        {a('release') && f.customerApprovalRequired && <Button variant="outlined" onClick={() => setDlg('release')}>Record customer approval</Button>}
        {a('close') && <Button onClick={() => setDlg('close')}>Close</Button>}
        {a('reopen') && <Button color="warning" onClick={() => setDlg('reopen')}>Reopen</Button>}
        {a('clone') && (f.status === 'Approved' || f.status === 'Closed') && <Button onClick={() => setDlg('clone')}>Delta FAI</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {acc.failed > 0 && f.status !== 'Closed' && <Alert severity="error" sx={{ mb: 2 }}>{acc.failed} characteristic(s) failed. Each failure needs an NCR with an approved disposition before approval.</Alert>}
      {acc.missing.length > 0 && <Alert severity="warning" sx={{ mb: 2 }}>Drawing balloons without a characteristic: {acc.missing.join(', ')}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: '290px 1fr', gap: 3 }}>
        <Facts items={[['Type', `${f.faiType} · ${words(f.typeDetail)}`], ['Reason', words(f.reason)], ['Product', d.product?.name ?? '—'], ['Part', `${d.part.partNumber}${d.part.partRevision ? ' rev ' + d.part.partRevision : ''}`],
          ['Drawing', d.drawing.drawingNumber ? `${d.drawing.drawingNumber} rev ${d.drawing.drawingRevision ?? '—'}` : '—'], ['Supplier', d.supplier ? `${d.supplier.code} — ${d.supplier.legalName}` : '—'],
          ['Lot / serial', [f.lotNumber, f.serialNumber].filter(Boolean).join(' / ') || '—'], ['Inspector', d.inspectorName], ['Reviewer', d.reviewerName ?? '—'], ['Site', d.site?.code ?? '—'], ['Due', fmtDate(f.dueDate)],
          ['Customer approval', f.customerApprovalRequired ? d.customerApprovalReference ?? 'Required' : 'Not required']]}>
          <Box sx={{ pt: 1.5, borderTop: 1, borderColor: 'divider' }}><Typography variant="caption" color="text.secondary">Accountability</Typography><Coverage passed={acc.passed} failed={acc.failed} total={acc.total} /></Box>
        </Facts>
        <Box sx={{ minWidth: 0 }}>
          {tab === 'characteristics' && <Paper>
            <Table size="small"><TableHead><TableRow><TableCell>#</TableCell><TableCell>Characteristic</TableCell><TableCell>Class</TableCell><TableCell>Requirement</TableCell><TableCell>Method</TableCell><TableCell>Measured</TableCell><TableCell>Result</TableCell><TableCell /></TableRow></TableHead>
              <TableBody>{d.characteristics.map((c) => <TableRow key={c.id}>
                <TableCell sx={{ fontFamily: mono, fontWeight: 700 }}>{c.balloonNumber}</TableCell><TableCell>{c.description}{c.drawingReference && <Typography fontSize={12} color="text.secondary">{c.drawingReference}</Typography>}</TableCell>
                <TableCell>{c.characteristicType}</TableCell><TableCell sx={{ fontFamily: mono, fontSize: 13 }}>{limits(c)}</TableCell><TableCell>{c.inspectionMethod ?? '—'}</TableCell>
                <TableCell sx={{ fontFamily: mono }}>{c.measuredValue ?? '—'}</TableCell>
                <TableCell><Stack direction="row" gap={0.5}><StatusChip status={c.status === 'NotInspected' ? 'Open' : c.status} />{c.carriedOver && <StatusChip status="Carried over" />}{c.ncrId && <StatusChip status="NCR" />}</Stack></TableCell>
                <TableCell>{a('results') && <Button size="small" onClick={() => setPick(c)}>Record</Button>}</TableCell>
              </TableRow>)}
              {d.characteristics.length === 0 && <TableRow><TableCell colSpan={8}><Typography color="text.secondary" sx={{ py: 3, textAlign: 'center' }}>No characteristics yet. Pick a ballooned drawing on create, or add them here.</Typography></TableCell></TableRow>}</TableBody></Table>
          </Paper>}
          {tab === 'board' && <Section title="Balloon board"><BalloonBoard chars={d.characteristics} onPick={a('results') ? setPick : undefined} />
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 2 }}>Green pass · red fail · grey not applicable · white open. A red ring marks a critical characteristic.</Typography></Section>}
          {tab === 'approvals' && <Section title="Approval route">{d.approvals.length === 0 ? <Typography color="text.secondary" fontSize={14}>No signatures requested yet.</Typography> :
            <Table size="small"><TableHead><TableRow><TableCell>Step</TableCell><TableCell>Role</TableCell><TableCell>Status</TableCell><TableCell>Signer</TableCell><TableCell>Meaning / comment</TableCell><TableCell>Signed</TableCell></TableRow></TableHead>
              <TableBody>{d.approvals.map((x) => <TableRow key={x.id}><TableCell>{x.step}</TableCell><TableCell>{x.approvalRole}</TableCell><TableCell><StatusChip status={x.status} /></TableCell><TableCell>{x.approverName ?? '—'}</TableCell><TableCell>{x.meaning ?? x.comment ?? '—'}</TableCell><TableCell>{fmtDateTime(x.decidedAt)}</TableCell></TableRow>)}</TableBody></Table>}</Section>}
          {tab === 'drawings' && <Stack gap={2}>
            <Section title="Ballooned drawings">{d.balloonDrawings.length === 0 ? <Typography color="text.secondary" fontSize={14}>None uploaded.</Typography> : d.balloonDrawings.map((b) => <Typography key={b.id} fontSize={14}>Rev {b.drawingRevision} · {b.attachment?.fileName ?? '—'}</Typography>)}
              {a('edit') && <Button component="label" size="small" sx={{ mt: 1 }}>Upload ballooned PDF<input hidden type="file" accept="application/pdf" onChange={(e) => { const file = e.target.files?.[0]; if (!file) return; const fd = new FormData(); fd.append('file', file); fd.append('drawingRevision', d.drawing.drawingRevision ?? 'A'); fd.append('importCharacteristics', 'false'); run(() => post(`/api/v1/fai/${f.id}/balloon-drawings`, fd)); }} /></Button>}
            </Section>
            <Section title="FAI revisions">{d.revisions.length === 0 ? <Typography color="text.secondary" fontSize={14}>Original issue.</Typography> : d.revisions.map((r) => <Typography key={r.faiRevision} fontSize={14}>Rev {r.faiRevision}: {r.reason} ({fmtDateTime(r.createdAt)})</Typography>)}</Section>
            <Stack direction="row" gap={1}><Button variant="outlined" href={`/api/v1/fai/${f.id}/pdf`} target="_blank">FAI report (PDF)</Button><Button variant="outlined" href={`/api/v1/fai/${f.id}/export-results`}>Results (CSV)</Button></Stack>
          </Stack>}
          {tab === 'more' && <AttachmentsAndHistory type="Fai" id={f.id} canEdit={a('edit')} />}
        </Box>
      </Box>
      <ResultDialog fai={f.id} c={pick} equipment={eq.data ?? []} onClose={() => setPick(null)} onSaved={refresh} />
      <StepDialog open={dlg === 'char'} title="Add characteristic" confirm="Add" onClose={() => setDlg(null)}
        inputs={[{ name: 'balloonNumber', label: 'Balloon number' }, { name: 'description', label: 'Description' }, { name: 'characteristicType', label: 'Class', type: 'select', options: opts(charTypes), initial: 'General' },
          { name: 'dataType', label: 'Data', type: 'select', options: [['Numeric', 'Numeric (measured)'], ['Visual', 'Visual / attribute']], initial: 'Numeric' }, { name: 'nominalValue', label: 'Nominal' }, { name: 'lowerSpecificationLimit', label: 'Lower limit' },
          { name: 'upperSpecificationLimit', label: 'Upper limit' }, { name: 'unitOfMeasure', label: 'Unit' }, { name: 'acceptanceCriteria', label: 'Acceptance criteria (visual)' }, { name: 'inspectionMethod', label: 'Method' }, { name: 'drawingReference', label: 'Drawing zone / note' }]}
        onConfirm={async (v) => { const n = (x: string) => (x === '' ? null : Number(x)); await post(`/api/v1/fai/${f.id}/characteristics`, { ...v, balloonNumber: Number(v.balloonNumber), nominalValue: n(v.nominalValue), lowerSpecificationLimit: n(v.lowerSpecificationLimit), upperSpecificationLimit: n(v.upperSpecificationLimit),
          unitOfMeasure: nul(v.unitOfMeasure), acceptanceCriteria: nul(v.acceptanceCriteria), inspectionMethod: nul(v.inspectionMethod), drawingReference: nul(v.drawingReference), requirementSource: 'Drawing' }); refresh(); }} />
      <StepDialog open={dlg === 'start'} title="Start inspection" confirm="Start" onClose={() => setDlg(null)} inputs={[{ name: 'comment', label: 'Comment', type: 'multiline', initial: 'Starting inspection' }]} onConfirm={(v) => act('start-inspection', v)} />
      <StepDialog open={dlg === 'submit'} title="Submit for review" confirm="Submit" onClose={() => setDlg(null)} inputs={[{ name: 'comment', label: 'Comment', type: 'multiline', initial: 'Ready for review' }, { name: 'assigneeId', label: 'Reviewer (keeps current when blank)', type: 'select', options: [['', 'Keep current'], ...(users.data ?? []).map((u) => [u.id, u.displayName] as [string, string])] }]}
        onConfirm={(v) => act('submit', { comment: v.comment, assigneeId: nul(v.assigneeId) })} />
      <StepDialog open={dlg === 'request'} title="Request signatures" intro="Starts the configured approval route (quality, engineering, manufacturing)." confirm="Request" onClose={() => setDlg(null)}
        inputs={[{ name: 'approvalRole', label: 'First role', type: 'select', options: [['Quality', 'Quality']], initial: 'Quality' }, { name: 'comment', label: 'Comment', type: 'multiline' }]} onConfirm={async (v) => { await post(`/api/v1/fai/${f.id}/approvals`, v); refresh(); }} />
      <StepDialog open={dlg === 'return'} title="Return for correction" confirm="Return" onClose={() => setDlg(null)} inputs={[{ name: 'comment', label: 'What needs correcting', type: 'multiline' }]} onConfirm={(v) => act('return', v)} />
      <StepDialog open={dlg === 'approve'} title={`Sign ${f.faiNumber}`} intro="Your name, the time and the meaning of this signature are recorded permanently." confirm="Approve and sign" onClose={() => setDlg(null)} inputs={sig('Re-enter your password to sign.')} onConfirm={(v) => act('approve', v)} />
      <StepDialog open={dlg === 'approval-return'} title="Return from approval" confirm="Return" onClose={() => setDlg(null)} inputs={sig('Returning is a signed decision.')} onConfirm={async (v) => { await post(`/api/v1/fai/${f.id}/approval-decision`, { decision: 'Returned', expectedVersion: f.version, ...v }); refresh(); }} />
      <StepDialog open={dlg === 'reject'} title={`Reject ${f.faiNumber}`} danger confirm="Reject" onClose={() => setDlg(null)} inputs={sig('Rejecting is a signed decision.')} onConfirm={(v) => act('reject', v)} />
      <StepDialog open={dlg === 'ncr'} title="Raise NCR for failed characteristics" intro="The NCR carries the part, drawing, lot and the failed measurements and is linked to this FAI." confirm="Raise NCR" onClose={() => setDlg(null)}
        inputs={[{ name: 'description', label: 'Description (optional)', type: 'multiline' }]} onConfirm={async (v) => { const r = await post<{ recordId: string }>(`/api/v1/fai/${f.id}/ncr`, { description: nul(v.description) }); refresh(); nav(`/ncr/${r.recordId}`); }} />
      <StepDialog open={dlg === 'release'} title="Customer approval" confirm="Record" onClose={() => setDlg(null)} inputs={[{ name: 'reasonCode', label: 'Customer approval reference' }, { name: 'comment', label: 'Comment', initial: 'Customer approval received' }]} onConfirm={(v) => act('release', v)} />
      <StepDialog open={dlg === 'close'} title={`Close ${f.faiNumber}`} confirm="Close" onClose={() => setDlg(null)} inputs={[{ name: 'comment', label: 'Closure note', type: 'multiline' }]} onConfirm={(v) => act('close', v)} />
      <StepDialog open={dlg === 'reopen'} title={`Reopen ${f.faiNumber}`} confirm="Reopen" onClose={() => setDlg(null)} inputs={[{ name: 'comment', label: 'Reason', type: 'multiline' }]} onConfirm={(v) => act('reopen', v)} />
      <StepDialog open={dlg === 'clone'} title="Delta FAI from this one" intro="Passed results carry over except for the balloons you list as changed." confirm="Create delta FAI" onClose={() => setDlg(null)}
        inputs={[{ name: 'changed', label: 'Changed balloons (comma separated)' }]} onConfirm={async (v) => { const r = await post<{ id: string }>(`/api/v1/fai/${f.id}/clone`, { changedBalloons: v.changed.split(',').map((x) => Number(x.trim())).filter((x) => x > 0) }); nav(`/fai/${r.id}`); }} />
    </ModuleWorkspace>
  );
}

function ResultDialog({ fai, c, equipment, onClose, onSaved }: { fai: string; c: Char | null; equipment: { id: string; number: string; name: string }[]; onClose: () => void; onSaved: () => void }) {
  if (!c) return null;
  const numeric = c.dataType === 'Numeric';
  return <StepDialog open title={`Balloon ${c.balloonNumber}: ${c.description}`} intro={`Requirement: ${limits(c)}. Pass/fail of a measured value is checked against the limits.`} confirm="Save result" onClose={onClose}
    inputs={numeric
      ? [{ name: 'measuredValue', label: `Measured (${c.unitOfMeasure ?? ''})` }, { name: 'equipmentId', label: 'Gage / equipment', type: 'select', options: equipment.map((e) => [e.id, `${e.number} — ${e.name}`] as [string, string]) }, { name: 'notes', label: 'Notes' }]
      : [{ name: 'result', label: 'Result', type: 'select', options: [['Pass', 'Pass'], ['Fail', 'Fail'], ['NotApplicable', 'Not applicable']], initial: 'Pass' }, { name: 'textValue', label: 'Observation' }, { name: 'notes', label: 'Notes (required for N/A)' }]}
    onConfirm={async (v) => {
      const existing = await get<{ metadata: { totalItems: number } }>(`/api/v1/fai/${fai}/characteristics/${c.id}/results?pageSize=1`);
      const seq = existing.metadata.totalItems + 1;
      const mv = Number(v.measuredValue);
      const pass = c.lowerSpecificationLimit != null && c.upperSpecificationLimit != null ? mv >= c.lowerSpecificationLimit && mv <= c.upperSpecificationLimit : true;
      await post(`/api/v1/fai/${fai}/characteristics/${c.id}/results`, numeric ? { sequence: seq, measuredValue: mv, result: pass ? 'Pass' : 'Fail', equipmentId: nul(v.equipmentId), notes: nul(v.notes) }
        : { sequence: seq, result: v.result, textValue: nul(v.textValue), notes: nul(v.notes) });
      onSaved();
    }} />;
}

// ---------------- AQL ----------------

interface Lot { id: string; inspectionNumber: string; planLabel: string; lotNumber: string | null; purchaseOrder: string | null; lotSize: number; sampleSize: number; codeLetter: string | null; inspectionLevel: string;
  criticalDefects: number; majorDefects: number; minorDefects: number; status: string; overallResult: string; workflowState: string; inspectionDate: string | null; supplierId: string | null; inspectionSeverity: string }

export function AqlList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const view = p.get('view') ?? 'open';
  const url = { open: '/api/v1/aql/inspections/open', accepted: '/api/v1/aql/inspections/accepted', rejected: '/api/v1/aql/inspections/rejected', completed: '/api/v1/aql/inspections/completed', all: '/api/v1/aql/inspections' }[view]!;
  const list = useQuery({ queryKey: ['aql', view], queryFn: () => get<Paged<Lot>>(`${url}?pageSize=200`) });
  const cols: ColumnDef<Lot, any>[] = [
    { header: 'Lot record', accessorKey: 'inspectionNumber', cell: num }, { header: 'Plan', accessorKey: 'planLabel' }, { header: 'Lot / PO', accessorKey: 'lotNumber', cell: (x) => [x.getValue(), x.row.original.purchaseOrder].filter(Boolean).join(' / ') || '—' },
    { header: 'Sample', accessorKey: 'sampleSize', cell: (x) => <span style={{ fontFamily: mono }}>{x.getValue()} of {x.row.original.lotSize} · {x.row.original.codeLetter ?? '—'}</span> },
    { header: 'Defects C/M/m', accessorKey: 'majorDefects', cell: (x) => <span style={{ fontFamily: mono }}>{x.row.original.criticalDefects}/{x.getValue()}/{x.row.original.minorDefects}</span> },
    { header: 'Severity', accessorKey: 'inspectionSeverity' },
    { header: 'Decision', accessorKey: 'overallResult', cell: (x) => <StatusChip status={x.getValue() === 'Accepted' ? 'Pass' : x.getValue() === 'Rejected' ? 'Fail' : 'Pending'} /> },
    { header: 'State', accessorKey: 'workflowState', cell: (x) => <StatusChip status={x.getValue()} /> }, { header: 'Inspected', accessorKey: 'inspectionDate', cell: (x) => fmtDate(x.getValue()) },
  ];
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'AQL lots' }]} title="AQL sampling lots"
      tabs={[{ label: 'Open', value: 'open' }, { label: 'Accepted', value: 'accepted' }, { label: 'Rejected', value: 'rejected' }, { label: 'Completed', value: 'completed' }, { label: 'All', value: 'all' }]}
      tab={view} onTab={(v) => setP(v === 'open' ? {} : { view: v })}
      actions={can('inspection.edit') ? <Button variant="contained" component={RouterLink} to="/aql/new">Sample a lot</Button> : undefined}>
      <DataTable data={list.data?.items ?? []} columns={cols} onRowClick={(r) => nav(`/aql/${r.id}`)} empty="No lots in this view." />
    </ModuleWorkspace>
  );
}

interface Plan { id: string; name: string; severity: string; status: string; revision: string; inspectionType: string; rowCount: number; activatedAt: string | null; description: string | null }
const usePlans = (history = false) => useQuery({ queryKey: ['aql-plans', history], queryFn: () => get<Paged<Plan>>(`/api/v1/aql/plans?pageSize=200${history ? '&includeHistory=true' : ''}`) });

export function AqlNew() {
  const nav = useNavigate();
  const plans = usePlans();
  const parts = useLookup('part');
  const suppliers = useLookup('supplier');
  const [v, setV] = useState<Record<string, string>>({ inspectionLevel: 'II', aqlLevel: '1.0' });
  const [calc, setCalc] = useState<{ sampleSize: number; codeLetter: string | null; thresholds: { defectClass: string | null; acceptanceNumber: number; rejectionNumber: number; aql: number }[] } | null>(null);
  const { error, run, setError } = useRunner(() => undefined);
  const set = (k: string) => (e: { target: { value: string } }) => { setV({ ...v, [k]: e.target.value }); setCalc(null); };
  const body = () => ({ planId: v.planId, lotSize: Number(v.lotSize), inspectionLevel: v.inspectionLevel, aqlLevel: Number(v.aqlLevel) });
  const field = (k: string, label: string, children?: ReactNode) => <TextField size="small" label={label} value={v[k] ?? ''} onChange={set(k)} select={!!children}>{children}</TextField>;
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'AQL lots', to: '/aql' }, { label: 'New' }]} title="Sample a lot">
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: 'minmax(0, 560px) 1fr', gap: 3, alignItems: 'start' }}>
        <Paper sx={{ p: 3, display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}>
          {field('planId', 'Sampling plan', (plans.data?.items ?? []).filter((p) => p.status === 'Active').map((p) => <MenuItem key={p.id} value={p.id}>{p.name} · {p.severity} · rev {p.revision}</MenuItem>))}
          {field('partId', 'Part', (parts.data ?? []).map((r) => <MenuItem key={r.id} value={r.id}>{lookupLabel(r)}</MenuItem>))}
          {field('supplierId', 'Supplier', [<MenuItem key="" value="">None</MenuItem>, ...(suppliers.data ?? []).map((r) => <MenuItem key={r.id} value={r.id}>{lookupLabel(r)}</MenuItem>)])}
          {field('lotNumber', 'Lot number')}{field('purchaseOrder', 'Purchase order')}{field('lotSize', 'Lot size')}
          {field('inspectionLevel', 'Inspection level', ['I', 'II', 'III', 'S-1', 'S-2', 'S-3', 'S-4'].map((l) => <MenuItem key={l} value={l}>{l}</MenuItem>))}
          {field('aqlLevel', 'AQL (%)')}
          <Stack direction="row" gap={1} sx={{ gridColumn: '1 / -1' }}>
            <Button variant="outlined" onClick={() => run(async () => { setError(null); setCalc(await post('/api/v1/aql/calculate-sample', body())); })}>Preview sample</Button>
            <Button variant="contained" onClick={() => run(async () => { const r = await post<{ id: string }>('/api/v1/aql/inspections', { ...body(), partId: v.partId, supplierId: nul(v.supplierId), lotNumber: nul(v.lotNumber), purchaseOrder: nul(v.purchaseOrder) }); nav(`/aql/${r.id}`); })}>Create lot record</Button>
          </Stack>
        </Paper>
        {calc && <Paper sx={{ p: 3 }}>
          <Typography variant="overline" color="text.secondary">Frozen on creation</Typography>
          <Typography sx={{ fontFamily: mono, fontSize: 40, fontWeight: 700, lineHeight: 1.1 }}>{calc.sampleSize}</Typography>
          <Typography color="text.secondary" fontSize={14}>units to sample · code letter {calc.codeLetter ?? '—'}</Typography>
          {calc.thresholds.map((t, i) => <Typography key={i} sx={{ mt: 1, fontFamily: mono }}>{t.defectClass ?? 'All defects'} @ AQL {t.aql}: accept ≤ {t.acceptanceNumber}, reject ≥ {t.rejectionNumber}</Typography>)}
        </Paper>}
      </Box>
    </ModuleWorkspace>
  );
}

interface LotDetail { aql: Lot & { thresholds: { defectClass: string | null; acceptanceNumber: number; rejectionNumber: number; aql: number }[]; rationale: string | null; samplingRecommendation?: string | null; version: number; linkedInspectionId: string | null };
  defects: { id: string; defectCode: string; defectClass: string; quantityFound: number; sampleUnitNumber: number | null; description: string | null; stage: number }[];
  inspection: { number: string } | null; receipt: { purchaseOrder: string | null } | null; part: { partNumber: string; name: string } | null; supplier: { code: string; legalName: string } | null; inspectorName: string | null; approverName: string | null }

export function AqlDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const nav = useNavigate();
  const [dlg, setDlg] = useState<string | null>(null);
  const [tab, setTab] = useState('defects');
  const q = useQuery({ queryKey: ['aql-detail', id], queryFn: () => get<LotDetail>(`/api/v1/aql/inspections/${id}/detail`) });
  const cats = useQuery({ queryKey: ['defect-cats'], queryFn: () => get<{ id: string; code: string; description: string; defectClass: string; active: boolean }[]>('/api/v1/aql/defect-categories') });
  const refresh = () => qc.invalidateQueries({ queryKey: ['aql-detail', id] });
  const { error, run } = useRunner(refresh);
  const d = q.data;
  if (!d) return <ModuleWorkspace module={mod()} crumbs={[{ label: 'AQL lots', to: '/aql' }]} title="Loading">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const a = d.aql;
  const act = (path: string, comment = 'ok') => run(() => post(`/api/v1/aql/inspections/${a.id}/${path}`, { comment, expectedVersion: a.version }));
  const sampling = a.workflowState === 'Sampling';
  const counts: Record<string, number> = { Critical: a.criticalDefects, Major: a.majorDefects, Minor: a.minorDefects };
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'AQL lots', to: '/aql' }, { label: a.inspectionNumber }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{a.inspectionNumber}</span><span>{d.part?.partNumber}</span><StatusChip status={a.workflowState} /></Stack>}
      tabs={[{ label: `Defects (${d.defects.length})`, value: 'defects' }, { label: 'Files & history', value: 'more' }]} tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1} flexWrap="wrap">
        {sampling && <Button variant="outlined" onClick={() => setDlg('defect')}>Record defect</Button>}
        {sampling && <Button variant="contained" onClick={() => act('evaluate')}>Evaluate</Button>}
        {a.status === 'InProgress' && a.overallResult === 'Accepted' && <Button variant="contained" onClick={() => act('accept')}>Accept lot</Button>}
        {a.workflowState === 'Decided' && <Button variant="contained" onClick={() => act('submit')}>Submit for disposition</Button>}
        {a.overallResult === 'Rejected' && <Button color="error" variant="outlined" onClick={() => run(async () => { const r = await post<{ recordId: string }>(`/api/v1/aql/inspections/${a.id}/ncr`, {}); nav(`/ncr/${r.recordId}`); })}>Raise NCR</Button>}
        {a.workflowState === 'Submitted' && <Button variant="contained" onClick={() => setDlg('approve')}>Approve disposition</Button>}
        {a.workflowState === 'Approved' && <Button onClick={() => act('close')}>Close</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {a.samplingRecommendation && <Alert severity="info" sx={{ mb: 2 }}>{a.samplingRecommendation}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: '290px 1fr', gap: 3 }}>
        <Facts items={[['Plan', a.planLabel], ['Severity', a.inspectionSeverity], ['Level / letter', `${a.inspectionLevel} / ${a.codeLetter ?? '—'}`], ['Lot size', a.lotSize], ['Supplier', d.supplier ? `${d.supplier.code} — ${d.supplier.legalName}` : '—'],
          ['Lot / PO', [a.lotNumber, a.purchaseOrder].filter(Boolean).join(' / ') || '—'], ['Receiving inspection', d.inspection?.number ?? 'Standalone'], ['Inspector', d.inspectorName ?? '—'], ['Approver', d.approverName ?? '—']]} />
        <Stack gap={2} sx={{ minWidth: 0 }}>
          <Box sx={{ display: 'grid', gridTemplateColumns: `repeat(${Math.max(a.thresholds.length, 1) + 1}, minmax(0, 1fr))`, gap: 2 }}>
            <Paper sx={{ p: 2 }}><Typography variant="overline" color="text.secondary">Sample</Typography><Typography sx={{ fontFamily: mono, fontSize: 32, fontWeight: 700 }}>{a.sampleSize}</Typography><Typography fontSize={13} color="text.secondary">of {a.lotSize}</Typography></Paper>
            {a.thresholds.map((t, i) => { const n = t.defectClass ? counts[t.defectClass] : a.criticalDefects + a.majorDefects + a.minorDefects; const bad = n >= t.rejectionNumber;
              return <Paper key={i} sx={{ p: 2, borderTop: 4, borderColor: bad ? 'error.main' : 'success.main' }}><Typography variant="overline" color="text.secondary">{t.defectClass ?? 'All defects'} · AQL {t.aql}</Typography>
                <Typography sx={{ fontFamily: mono, fontSize: 32, fontWeight: 700 }}>{n}</Typography><Typography fontSize={13} color="text.secondary">accept ≤ {t.acceptanceNumber} · reject ≥ {t.rejectionNumber}</Typography></Paper>; })}
          </Box>
          {a.rationale && <Alert severity={a.overallResult === 'Rejected' ? 'error' : a.overallResult === 'Accepted' ? 'success' : 'info'}>{a.rationale}</Alert>}
          {tab === 'defects' && <Paper><Table size="small"><TableHead><TableRow><TableCell>Code</TableCell><TableCell>Class</TableCell><TableCell>Qty</TableCell><TableCell>Unit #</TableCell><TableCell>Stage</TableCell><TableCell>Description</TableCell></TableRow></TableHead>
            <TableBody>{d.defects.map((x) => <TableRow key={x.id}><TableCell sx={{ fontFamily: mono }}>{x.defectCode}</TableCell><TableCell><StatusChip status={x.defectClass} /></TableCell><TableCell>{x.quantityFound}</TableCell><TableCell>{x.sampleUnitNumber ?? '—'}</TableCell><TableCell>{x.stage}</TableCell><TableCell>{x.description ?? '—'}</TableCell></TableRow>)}
              {d.defects.length === 0 && <TableRow><TableCell colSpan={6}><Typography color="text.secondary" sx={{ py: 3, textAlign: 'center' }}>No defects recorded.</Typography></TableCell></TableRow>}</TableBody></Table></Paper>}
          {tab === 'more' && <AttachmentsAndHistory type="AqlInspection" id={a.id} canEdit={sampling} />}
        </Stack>
      </Box>
      <StepDialog open={dlg === 'defect'} title="Record defect" confirm="Record" onClose={() => setDlg(null)}
        inputs={[{ name: 'defectCategoryId', label: 'Defect', type: 'select', options: (cats.data ?? []).filter((c) => c.active).map((c) => [c.id, `${c.code} · ${c.defectClass} · ${c.description}`] as [string, string]) },
          { name: 'quantityFound', label: 'Quantity', initial: '1' }, { name: 'sampleUnitNumber', label: 'Sample unit number (optional)' }, { name: 'description', label: 'Description', type: 'multiline' }]}
        onConfirm={async (v) => { await post(`/api/v1/aql/inspections/${a.id}/defects`, { defectCategoryId: v.defectCategoryId, quantityFound: Number(v.quantityFound), sampleUnitNumber: v.sampleUnitNumber ? Number(v.sampleUnitNumber) : null, description: nul(v.description) }); refresh(); }} />
      <StepDialog open={dlg === 'approve'} title="Approve disposition" confirm="Approve" onClose={() => setDlg(null)} inputs={[{ name: 'comment', label: 'Disposition note', type: 'multiline' }]} onConfirm={(v) => act('approve', v.comment)} />
    </ModuleWorkspace>
  );
}

export function AqlPlans() {
  const [sel, setSel] = useState<string | null>(null);
  const plans = usePlans(true);
  const rows = useQuery({ queryKey: ['aql-rows', sel], enabled: !!sel, queryFn: () => get<{ id: string; lotSizeMin: number; lotSizeMax: number | null; inspectionLevel: string; aqlLevel: number; sampleSize: number; acceptanceNumber: number; rejectionNumber: number; defectClass: string | null; codeLetter: string | null }[]>(`/api/v1/aql/plans/${sel}/levels`) });
  const cols: ColumnDef<Plan, any>[] = [{ header: 'Plan', accessorKey: 'name' }, { header: 'Severity', accessorKey: 'severity' }, { header: 'Type', accessorKey: 'inspectionType' }, { header: 'Rev', accessorKey: 'revision', cell: num },
    { header: 'Rows', accessorKey: 'rowCount' }, { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={x.getValue()} /> }, { header: 'Activated', accessorKey: 'activatedAt', cell: (x) => fmtDate(x.getValue()) }];
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Sampling plans' }]} title="Sampling plans">
      <Stack gap={2}>
        <DataTable data={plans.data?.items ?? []} columns={cols} onRowClick={(r) => setSel(r.id)} empty="No plans." />
        {sel && <Section title="Plan rows">
          <Box sx={{ maxHeight: 420, overflow: 'auto' }}><Table size="small" stickyHeader><TableHead><TableRow><TableCell>Lot size</TableCell><TableCell>Level</TableCell><TableCell>Letter</TableCell><TableCell>AQL</TableCell><TableCell>Class</TableCell><TableCell>n</TableCell><TableCell>Ac</TableCell><TableCell>Re</TableCell></TableRow></TableHead>
            <TableBody>{(rows.data ?? []).map((r) => <TableRow key={r.id}><TableCell sx={{ fontFamily: mono }}>{r.lotSizeMin}–{r.lotSizeMax ?? '∞'}</TableCell><TableCell>{r.inspectionLevel}</TableCell><TableCell>{r.codeLetter ?? '—'}</TableCell><TableCell>{r.aqlLevel}</TableCell><TableCell>{r.defectClass ?? 'All'}</TableCell>
              <TableCell sx={{ fontFamily: mono }}>{r.sampleSize}</TableCell><TableCell sx={{ fontFamily: mono }}>{r.acceptanceNumber}</TableCell><TableCell sx={{ fontFamily: mono }}>{r.rejectionNumber}</TableCell></TableRow>)}</TableBody></Table></Box>
        </Section>}
      </Stack>
    </ModuleWorkspace>
  );
}

/** Horizontal bar list for a MetricSeries. */
function Bars({ s }: { s?: Series }) {
  if (!s) return <LinearProgress />;
  const max = Math.max(1, ...s.points.map((p) => p.value));
  return <Section title={s.name}>{s.points.length === 0 ? <Typography color="text.secondary" fontSize={14}>No data yet.</Typography> : s.points.map((p) => <Box key={p.label} sx={{ display: 'grid', gridTemplateColumns: '120px 1fr 70px', alignItems: 'center', gap: 1, mb: 0.75 }}>
    <Typography fontSize={13} color="text.secondary" noWrap>{words(p.label)}</Typography><Box sx={{ height: 10, borderRadius: 5, bgcolor: '#E4E1D9' }}><Box sx={{ width: `${(p.value / max) * 100}%`, height: '100%', borderRadius: 5, bgcolor: 'primary.main' }} /></Box>
    <Typography fontSize={13} sx={{ fontFamily: mono, textAlign: 'right' }}>{Math.round(p.value * 100) / 100}{s.unit === '%' ? '%' : ''}</Typography></Box>)}</Section>;
}

export function FaiAqlDashboard() {
  const names = ['fai/open', 'fai/overdue', 'fai/pass-rate', 'fai/fail-rate', 'aql/acceptance-rate', 'aql/rejection-rate', 'aql/defect-rate', 'aql/ppm'];
  const q = useQuery({ queryKey: ['faiaql-dash'], queryFn: () => Promise.all(names.map((n) => get<Series>(`/api/v1/dashboard/${n}`).catch(() => ({ name: n, points: [] } as Series)))) });
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Dashboard' }]} title="First article and AQL dashboard">
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 2 }}>{names.map((n, i) => <Bars key={n} s={q.data?.[i]} />)}</Box>
    </ModuleWorkspace>
  );
}

export function SupplierAql() {
  const suppliers = useLookup('supplier');
  const [sid, setSid] = useState('');
  const m = useQuery({ queryKey: ['sup-aql', sid], enabled: !!sid, queryFn: () => Promise.all(['metrics', 'acceptance-rate', 'ppm', 'defect-trends'].map((k) => get<Series>(`/api/v1/aql/suppliers/${sid}/${k}`))) });
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Supplier trends' }]} title="Supplier AQL trends">
      <TextField select size="small" label="Supplier" value={sid} onChange={(e) => setSid(e.target.value)} sx={{ width: 420, mb: 2 }}>{(suppliers.data ?? []).map((r) => <MenuItem key={r.id} value={r.id}>{lookupLabel(r)}</MenuItem>)}</TextField>
      {sid && <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 2 }}>{(m.data ?? [undefined, undefined, undefined, undefined]).map((s, i) => <Bars key={i} s={s} />)}</Box>}
    </ModuleWorkspace>
  );
}
