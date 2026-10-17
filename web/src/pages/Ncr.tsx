import { useState } from 'react';
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Link, Stack, TextField } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { z } from 'zod';
import { get, post } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { localDate, StatusChip, capaStatus, dispositions, fmtDate, fmtDateTime, ncrStatus, severity } from '../components/Status';
import { ActionsPanel, ApprovalsPanel, AttachmentsAndHistory } from '../components/RecordTabs';
import { CustomFieldsPanel } from '../components/RecordPanels';
import { Facts, Prose, SchemaForm, Section, StepDialog, useRunner } from '../components/Kit';
import { SignDialog } from '../components/SignDialog';
import { useWork } from './Home';
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
});
const sources = ['Receiving', 'In-process', 'Final inspection', 'Customer', 'Supplier', 'Audit', 'Calibration', 'Other'];

export function NcrNew() {
  const nav = useNavigate();
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Nonconformances', to: '/ncr' }, { label: 'New' }]} title="Report a nonconformance">
      <SchemaForm schema={ncrSchema} submitLabel="Create NCR" onCancel={() => nav('/ncr')}
        defaults={{ title: '', description: '', source: '', siteId: '', departmentId: '', ownerId: '', partNumber: '', lotNumber: '', quantityAffected: '', detectedOn: today(), severity: 0 }}
        fields={[
          { name: 'title', label: 'Title', full: true }, { name: 'description', label: 'What is wrong (requirement versus actual)', type: 'multiline' },
          { name: 'source', label: 'Source', type: 'select', options: sources.map((s) => [s, s]) }, { name: 'severity', label: 'Severity', type: 'select', options: severity.map((s, i) => [i, s]) },
          { name: 'siteId', label: 'Site', type: 'site' }, { name: 'departmentId', label: 'Department (optional)', type: 'department' },
          { name: 'partNumber', label: 'Part number' }, { name: 'lotNumber', label: 'Lot / batch' },
          { name: 'quantityAffected', label: 'Quantity affected', type: 'number' }, { name: 'detectedOn', label: 'Detected on', type: 'date' },
          { name: 'ownerId', label: 'Owner', type: 'user' },
        ]}
        onSubmit={async (v) => {
          const r = await post<{ id: string }>('/api/ncrs', { ...v, departmentId: v.departmentId || null, ownerId: v.ownerId || null, partNumber: v.partNumber || null, lotNumber: v.lotNumber || null, quantityAffected: v.quantityAffected === '' ? null : v.quantityAffected });
          nav(`/ncr/${r.id}`);
        }} />
    </ModuleWorkspace>
  );
}

interface NcrDetailDto { id: string; number: string; title: string; description: string; source: string; partNumber: string | null; lotNumber: string | null; quantityAffected: number | null; detectedOn: string; severity: number; status: number; containmentSummary: string | null; disposition: number; dispositionJustification: string | null; capaRequired: boolean; closureNotes: string | null; closedAt: string | null; ownerName: string; siteCode: string; capas: { id: string; number: string; status: number }[]; canEdit: boolean }

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
        {d.canEdit && s <= 1 && <Button variant="outlined" onClick={() => setDlg('contain')}>Record containment</Button>}
        {d.canEdit && s === 1 && <Button variant="contained" onClick={() => setDlg('disposition')}>Propose disposition</Button>}
        {d.canEdit && s >= 1 && s <= 3 && <Button variant="outlined" onClick={() => setDlg('capa')}>Create CAPA</Button>}
        {d.canEdit && s === 3 && <Button variant="contained" onClick={() => setDlg('close')}>Close NCR</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: '280px 1fr', gap: 3 }}>
        <Facts items={[['Source', d.source], ['Severity', severity[d.severity]], ['Part / lot', [d.partNumber, d.lotNumber].filter(Boolean).join(' / ') || '—'], ['Quantity affected', d.quantityAffected ?? '—'],
          ['Detected', fmtDate(d.detectedOn)], ['Owner', d.ownerName], ['Site', d.siteCode], ['CAPA required', d.capaRequired ? 'Yes' : 'No'], ['Closed', fmtDateTime(d.closedAt)]]}>
          <Box sx={{ mt: 2 }}><CustomFieldsPanel type="Ncr" id={d.id} canEdit={d.canEdit && s < 4} /></Box>
        </Facts>
        <Box sx={{ minWidth: 0 }}>
          {tab === 'overview' && <Stack gap={2}>
            <Section title="Description"><Prose label="Nonconformance" text={d.description} /></Section>
            <Section title="Containment">{d.containmentSummary ? <Prose label="Actions taken" text={d.containmentSummary} /> : <Alert severity="warning">Containment has not been recorded.</Alert>}</Section>
            <Section title="Disposition">{d.disposition ? <><Prose label="Disposition" text={dispositions[d.disposition]} /><Prose label="Justification" text={d.dispositionJustification} /></> : <Alert severity="info">No disposition proposed yet.</Alert>}</Section>
            <Section title="Corrective action">{d.capas.length ? d.capas.map((c) => <Stack key={c.id} direction="row" gap={1} alignItems="center"><Link component={RouterLink} to={`/capa/${c.id}`} sx={{ fontFamily: mono }}>{c.number}</Link><StatusChip status={capaStatus[c.status]} /></Stack>) : 'No CAPA linked.'}</Section>
            {d.closureNotes && <Section title="Closure"><Prose label="Notes" text={d.closureNotes} /></Section>}
          </Stack>}
          {tab === 'actions' && <ActionsPanel type="Ncr" id={d.id} canEdit={d.canEdit && s < 4} />}
          {tab === 'approvals' && <ApprovalsPanel type="Ncr" id={d.id} />}
          {tab === 'more' && <AttachmentsAndHistory type="Ncr" id={d.id} canEdit={d.canEdit && s < 4} />}
        </Box>
      </Box>
      <StepDialog open={dlg === 'contain'} title="Record containment" confirm="Save" onClose={() => setDlg(null)} inputs={[{ name: 'text', label: 'Containment actions (quarantine, sort, stop shipment)', type: 'multiline', initial: d.containmentSummary ?? '' }]}
        onConfirm={(v) => run(() => post(`/api/ncrs/${d.id}/containment`, v))} />
      <StepDialog open={dlg === 'disposition'} title="Propose disposition" intro="The disposition is routed for approval with an electronic signature." confirm="Submit for approval" onClose={() => setDlg(null)}
        inputs={[{ name: 'disposition', label: 'Disposition', type: 'select', options: dispositions.slice(1).map((x, i) => [i + 1, x]) }, { name: 'justification', label: 'Justification', type: 'multiline' }, { name: 'capaRequired', label: 'CAPA required', type: 'checkbox' }]}
        onConfirm={(v) => run(() => post(`/api/ncrs/${d.id}/disposition`, { disposition: Number(v.disposition), justification: v.justification, capaRequired: v.capaRequired === 'true' }))} />
      <StepDialog open={dlg === 'capa'} title="Create linked CAPA" intro="Title and problem statement default to the NCR." confirm="Create CAPA" onClose={() => setDlg(null)}
        inputs={[{ name: 'title', label: 'Title', initial: d.title }, { name: 'problemStatement', label: 'Problem statement', type: 'multiline', initial: d.description }]}
        onConfirm={async (v) => { const r = await post<{ id: string }>(`/api/ncrs/${d.id}/capa`, { ...v, ownerId: null }); nav(`/capa/${r.id}`); }} />
      <StepDialog open={dlg === 'close'} title={`Close ${d.number}`} confirm="Close NCR" onClose={() => setDlg(null)} inputs={[{ name: 'text', label: 'Closure notes and evidence', type: 'multiline' }]}
        onConfirm={(v) => run(() => post(`/api/ncrs/${d.id}/close`, v))} />
      {signing && approval && <SignDialog approvalId={approval.id} recordNumber={approval.recordNumber} stepName={approval.stepName} onClose={() => { setSigning(false); refresh(); }} />}
    </ModuleWorkspace>
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

interface CapaDetailDto { id: string; number: string; title: string; problemStatement: string; capaType: string; source: string; status: number; rootCauseMethod: string | null; rootCause: string | null; effectivenessCriteria: string | null; effectivenessDueOn: string | null; effectivenessResult: string | null; wasEffective: boolean | null; closedAt: string | null; ownerName: string; verifiedByName: string | null; siteCode: string; canEdit: boolean; canVerify: boolean }

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
        {d.canEdit && s <= 2 && <Button variant="outlined" onClick={() => setDlg('rca')}>{d.rootCause ? 'Edit root cause' : 'Record root cause'}</Button>}
        {d.canEdit && s === 2 && <Button variant="contained" onClick={() => run(() => post(`/api/capas/${d.id}/effectiveness-check`))}>Ready for effectiveness check</Button>}
        {d.canVerify && s === 3 && <Button variant="contained" color="secondary" onClick={() => setDlg('verify')}>Verify effectiveness</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: '280px 1fr', gap: 3 }}>
        <Facts items={[['Type', d.capaType], ['Source', d.source], ['Owner', d.ownerName], ['Site', d.siteCode], ['Effectiveness due', fmtDate(d.effectivenessDueOn)], ['Verified by', d.verifiedByName ?? '—'], ['Closed', fmtDateTime(d.closedAt)]]}>
          <Box sx={{ mt: 2 }}><CustomFieldsPanel type="Capa" id={d.id} canEdit={d.canEdit && s < 4} /></Box>
        </Facts>
        <Box sx={{ minWidth: 0 }}>
          {tab === 'overview' && <Stack gap={2}>
            <Section title="Problem"><Prose label="Problem statement" text={d.problemStatement} /></Section>
            <Section title="Root cause">{d.rootCause ? <><Prose label="Method" text={d.rootCauseMethod} /><Prose label="Root cause" text={d.rootCause} /></> : <Alert severity="warning">Root cause not yet determined.</Alert>}</Section>
            <Section title="Effectiveness"><Prose label="Criteria" text={d.effectivenessCriteria} />{d.effectivenessResult ? <><Prose label="Result" text={d.effectivenessResult} /><StatusChip status={d.wasEffective ? 'Verified' : 'Rejected'} /></> : <Box sx={{ color: 'text.secondary', fontSize: 14 }}>Verified by someone other than the owner once all actions are closed.</Box>}</Section>
          </Stack>}
          {tab === 'actions' && <ActionsPanel type="Capa" id={d.id} canEdit={d.canEdit && s < 3} />}
          {tab === 'more' && <AttachmentsAndHistory type="Capa" id={d.id} canEdit={d.canEdit && s < 4} />}
        </Box>
      </Box>
      <StepDialog open={dlg === 'rca'} title="Root cause analysis" confirm="Save" onClose={() => setDlg(null)}
        inputs={[{ name: 'method', label: 'Method', type: 'select', initial: d.rootCauseMethod ?? '5 Whys', options: ['5 Whys', 'Fishbone (Ishikawa)', 'Fault tree', 'Is / is not', '8D', 'Other'].map((x) => [x, x]) },
          { name: 'rootCause', label: 'Root cause', type: 'multiline', initial: d.rootCause ?? '' }, { name: 'effectivenessCriteria', label: 'How effectiveness will be judged', type: 'multiline', initial: d.effectivenessCriteria ?? '' },
          { name: 'effectivenessDueOn', label: 'Effectiveness check due', type: 'date', initial: d.effectivenessDueOn ?? '' }]}
        onConfirm={(v) => run(() => post(`/api/capas/${d.id}/root-cause`, v))} />
      <StepDialog open={dlg === 'verify'} title="Verify effectiveness" confirm="Record verification" onClose={() => setDlg(null)}
        inputs={[{ name: 'effective', label: 'Was the action effective?', type: 'checkbox', initial: 'true' }, { name: 'result', label: 'Evidence and result', type: 'multiline' }]}
        onConfirm={(v) => run(() => post(`/api/capas/${d.id}/verify`, { effective: v.effective === 'true', result: v.result }))} />
    </ModuleWorkspace>
  );
}
