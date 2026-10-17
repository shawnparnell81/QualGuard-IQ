import { useState } from 'react';
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Chip, Link, Stack } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { z } from 'zod';
import { get, post } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { localDate, StatusChip, calResult, equipmentStatus, fmtDate, fmtDateTime, impactStatus, ncrStatus } from '../components/Status';
import { AttachmentsAndHistory } from '../components/RecordTabs';
import { Facts, Prose, SchemaForm, Section, StepDialog, useRunner } from '../components/Kit';
import { mono } from '../theme';

const mod = () => modules.find((m) => m.key === 'calibration')!;
const num = (x: { getValue: () => unknown }) => <span style={{ fontFamily: mono }}>{String(x.getValue())}</span>;
const today = () => localDate();
interface EqRow { id: string; number: string; name: string; serialNumber: string | null; location: string | null; status: number; lastCalibratedOn: string | null; nextDueOn: string | null; overdue: boolean; siteCode: string }

export function EquipmentList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const view = p.get('view') ?? 'all';
  const url = view === 'due' ? '/api/calibration/equipment?dueWithinDays=30' : view === 'quarantined' ? '/api/calibration/equipment?status=1' : '/api/calibration/equipment';
  const list = useQuery({ queryKey: ['equipment', view], queryFn: () => get<EqRow[]>(url) });
  const cols: ColumnDef<EqRow, any>[] = [
    { header: 'ID', accessorKey: 'number', cell: num }, { header: 'Name', accessorKey: 'name' }, { header: 'Serial', accessorKey: 'serialNumber' }, { header: 'Location', accessorKey: 'location' },
    { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={equipmentStatus[x.getValue()]} /> },
    { header: 'Last calibrated', accessorKey: 'lastCalibratedOn', cell: (x) => fmtDate(x.getValue()) },
    { header: 'Next due', id: 'due', cell: (x) => <Stack direction="row" gap={1} alignItems="center">{fmtDate(x.row.original.nextDueOn)}{x.row.original.overdue && <Chip size="small" color="error" label="Overdue" />}</Stack> },
    { header: 'Site', accessorKey: 'siteCode' },
  ];
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Equipment' }]} title="Measuring and test equipment"
      tabs={[{ label: 'All', value: 'all' }, { label: 'Due in 30 days', value: 'due' }, { label: 'Quarantined', value: 'quarantined' }]} tab={view} onTab={(v) => setP({ view: v })}
      actions={can('calibration.edit') ? <Button variant="contained" component={RouterLink} to="/calibration/equipment/new">Add equipment</Button> : undefined}>
      <DataTable data={list.data ?? []} columns={cols} onRowClick={(r) => nav(`/calibration/equipment/${r.id}`)} empty="No equipment in this view." />
    </ModuleWorkspace>
  );
}

const eqSchema = z.object({
  name: z.string().trim().min(1, 'Required').max(200), manufacturer: z.string().max(200).optional(), model: z.string().max(200).optional(), serialNumber: z.string().max(200).optional(),
  location: z.string().max(200).optional(), measurementRange: z.string().max(200).optional(), tolerance: z.string().max(200).optional(), siteId: z.string().uuid('Choose a site'),
  departmentId: z.string().optional(), ownerId: z.string().optional(), calibrationIntervalDays: z.coerce.number().int().min(1).max(3650), nextDueOn: z.string().optional(),
});
export function EquipmentNew() {
  const nav = useNavigate();
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Equipment', to: '/calibration/equipment' }, { label: 'New' }]} title="Add equipment">
      <SchemaForm schema={eqSchema} submitLabel="Add equipment" onCancel={() => nav('/calibration/equipment')}
        defaults={{ name: '', manufacturer: '', model: '', serialNumber: '', location: '', measurementRange: '', tolerance: '', siteId: '', departmentId: '', ownerId: '', calibrationIntervalDays: 365, nextDueOn: today() }}
        fields={[{ name: 'name', label: 'Name', full: true }, { name: 'manufacturer', label: 'Manufacturer' }, { name: 'model', label: 'Model' }, { name: 'serialNumber', label: 'Serial number' }, { name: 'location', label: 'Location' },
          { name: 'measurementRange', label: 'Range' }, { name: 'tolerance', label: 'Tolerance' }, { name: 'siteId', label: 'Site', type: 'site' }, { name: 'departmentId', label: 'Department (optional)', type: 'department' },
          { name: 'calibrationIntervalDays', label: 'Calibration interval (days)', type: 'number' }, { name: 'nextDueOn', label: 'First calibration due', type: 'date' }, { name: 'ownerId', label: 'Owner', type: 'user' }]}
        onSubmit={async (v) => {
          const blank = (s?: string) => (s ? s : null);
          const r = await post<{ id: string }>('/api/calibration/equipment', { ...v, manufacturer: blank(v.manufacturer), model: blank(v.model), serialNumber: blank(v.serialNumber), location: blank(v.location), measurementRange: blank(v.measurementRange), tolerance: blank(v.tolerance), departmentId: blank(v.departmentId), ownerId: blank(v.ownerId), nextDueOn: blank(v.nextDueOn) });
          nav(`/calibration/equipment/${r.id}`);
        }} />
    </ModuleWorkspace>
  );
}

interface Ev { id: string; number: string; performedOn: string; performedBy: string; result: number; asFound: string | null; asLeft: string | null; standardsUsed: string | null; certificateNumber: string | null; nextDueOn: string | null }
interface Ia { id: string; number: string; status: number; lastKnownGoodOn: string | null; affectedUntil: string; createdAt: string }
interface EqDto { id: string; number: string; name: string; manufacturer: string | null; model: string | null; serialNumber: string | null; location: string | null; measurementRange: string | null; tolerance: string | null; status: number; calibrationIntervalDays: number; lastCalibratedOn: string | null; nextDueOn: string | null; ownerName: string; events: Ev[]; impacts: Ia[]; canEdit: boolean; canApprove: boolean; allowed: string[] }

export function EquipmentDetail() {
  const { id } = useParams();
  const nav = useNavigate();
  const qc = useQueryClient();
  const [tab, setTab] = useState('history');
  const [dlg, setDlg] = useState(false);
  const q = useQuery({ queryKey: ['eq', id], queryFn: () => get<EqDto>(`/api/calibration/equipment/${id}`) });
  const { error, run } = useRunner(() => qc.invalidateQueries({ queryKey: ['eq', id] }));
  const e = q.data;
  if (!e) return <ModuleWorkspace module={mod()} crumbs={[{ label: 'Equipment', to: '/calibration/equipment' }]} title="Loading">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const openIa = e.impacts.find((i) => i.status === 0);
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Equipment', to: '/calibration/equipment' }, { label: e.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{e.number}</span><span>{e.name}</span><StatusChip status={equipmentStatus[e.status]} /></Stack>}
      tabs={[{ label: 'Calibration history', value: 'history' }, { label: `Impact assessments (${e.impacts.length})`, value: 'impacts' }, { label: 'Files & history', value: 'more' }]} tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1}>
        {e.allowed.includes('record') && <Button variant="contained" onClick={() => setDlg(true)}>Record calibration</Button>}
        {e.allowed.includes('returnToService') && <Button variant="outlined" color="secondary" onClick={() => run(() => post(`/api/calibration/equipment/${e.id}/return-to-service`))}>Return to service</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {e.status === 1 && <Alert severity="error" sx={{ mb: 2 }} action={openIa && <Button color="inherit" size="small" onClick={() => nav(`/calibration/impact/${openIa.id}`)}>Open {openIa.number}</Button>}>
        Quarantined after an out-of-tolerance result. Do not use. Return to service needs a completed impact assessment and a passing calibration.</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: '280px 1fr', gap: 3 }}>
        <Facts items={[['Manufacturer / model', [e.manufacturer, e.model].filter(Boolean).join(' ') || '—'], ['Serial', e.serialNumber ?? '—'], ['Location', e.location ?? '—'], ['Range', e.measurementRange ?? '—'], ['Tolerance', e.tolerance ?? '—'],
          ['Interval', `${e.calibrationIntervalDays} days`], ['Last calibrated', fmtDate(e.lastCalibratedOn)], ['Next due', fmtDate(e.nextDueOn)], ['Owner', e.ownerName]]} />
        <Box sx={{ minWidth: 0 }}>
          {tab === 'history' && <DataTable data={e.events} empty="No calibrations recorded." columns={[
            { header: 'Record', accessorKey: 'number', cell: num }, { header: 'Date', accessorKey: 'performedOn', cell: (x) => fmtDate(x.getValue() as string) }, { header: 'By', accessorKey: 'performedBy' },
            { header: 'Result', accessorKey: 'result', cell: (x) => <StatusChip status={calResult[x.getValue() as number]} /> }, { header: 'As found', accessorKey: 'asFound' }, { header: 'As left', accessorKey: 'asLeft' },
            { header: 'Standards', accessorKey: 'standardsUsed' }, { header: 'Certificate', accessorKey: 'certificateNumber' }, { header: 'Next due', accessorKey: 'nextDueOn', cell: (x) => fmtDate(x.getValue() as string) }]} />}
          {tab === 'impacts' && <DataTable data={e.impacts} onRowClick={(i) => nav(`/calibration/impact/${i.id}`)} empty="No out-of-tolerance events." columns={[
            { header: 'Assessment', accessorKey: 'number', cell: num }, { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={impactStatus[x.getValue() as number]} /> },
            { header: 'Last known good', accessorKey: 'lastKnownGoodOn', cell: (x) => fmtDate(x.getValue() as string) }, { header: 'Affected until', accessorKey: 'affectedUntil', cell: (x) => fmtDate(x.getValue() as string) },
            { header: 'Opened', accessorKey: 'createdAt', cell: (x) => fmtDateTime(x.getValue() as string) }]} />}
          {tab === 'more' && <AttachmentsAndHistory type="Equipment" id={e.id} canEdit={e.canEdit} />}
        </Box>
      </Box>
      <StepDialog open={dlg} title={`Record calibration for ${e.number}`} intro="Calibration records cannot be edited once saved. An out-of-tolerance or failed result quarantines the equipment and opens an impact assessment." confirm="Save record" onClose={() => setDlg(false)}
        inputs={[{ name: 'performedOn', label: 'Date performed', type: 'date', initial: today() }, { name: 'performedBy', label: 'Performed by (person or lab)' },
          { name: 'result', label: 'Result', type: 'select', initial: '0', options: [[0, 'Pass'], [1, 'Pass after adjustment'], [2, 'Out of tolerance'], [3, 'Fail']] },
          { name: 'asFound', label: 'As found' }, { name: 'asLeft', label: 'As left' }, { name: 'standardsUsed', label: 'Reference standards used' }, { name: 'certificateNumber', label: 'Certificate number' }]}
        onConfirm={async (v) => {
          const r = await post<{ impactAssessmentId: string | null }>(`/api/calibration/equipment/${e.id}/events`, { ...v, result: Number(v.result), asFound: v.asFound || null, asLeft: v.asLeft || null, standardsUsed: v.standardsUsed || null, certificateNumber: v.certificateNumber || null });
          qc.invalidateQueries({ queryKey: ['eq', id] });
          if (r.impactAssessmentId) nav(`/calibration/impact/${r.impactAssessmentId}`);
        }} />
    </ModuleWorkspace>
  );
}

interface IaDto { id: string; number: string; status: number; lastKnownGoodOn: string | null; affectedUntil: string; affectedProduct: string | null; assessment: string | null; decision: string | null; completedAt: string | null;
  equipment: { id: string; number: string; name: string }; event: { id: string; number: string; performedOn: string; result: number; asFound: string | null }; ncrs: { id: string; number: string; status: number }[]; canApprove: boolean; allowed: string[] }
export function ImpactDetail() {
  const { id } = useParams();
  const nav = useNavigate();
  const qc = useQueryClient();
  const [dlg, setDlg] = useState<string | null>(null);
  const q = useQuery({ queryKey: ['ia', id], queryFn: () => get<IaDto>(`/api/calibration/impact/${id}`) });
  const { error, run } = useRunner(() => qc.invalidateQueries({ queryKey: ['ia', id] }));
  const a = q.data;
  if (!a) return <ModuleWorkspace module={mod()} crumbs={[{ label: 'Equipment', to: '/calibration/equipment' }]} title="Loading">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Equipment', to: '/calibration/equipment' }, { label: a.equipment.number, to: `/calibration/equipment/${a.equipment.id}` }, { label: a.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{a.number}</span><span>Impact assessment</span><StatusChip status={impactStatus[a.status]} /></Stack>}
      actions={<Stack direction="row" gap={1}>
        {a.allowed.includes('ncr') && <Button variant="outlined" onClick={() => setDlg('ncr')}>Raise NCR</Button>}
        {a.allowed.includes('complete') && <Button variant="contained" onClick={() => setDlg('complete')}>Complete assessment</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: '300px 1fr', gap: 3 }}>
        <Facts items={[['Equipment', <Link component={RouterLink} to={`/calibration/equipment/${a.equipment.id}`}>{a.equipment.number} {a.equipment.name}</Link>], ['Triggering record', `${a.event.number} (${calResult[a.event.result]})`],
          ['As found', a.event.asFound ?? '—'], ['Last known good', fmtDate(a.lastKnownGoodOn) === '—' ? 'No earlier passing calibration' : fmtDate(a.lastKnownGoodOn)], ['Affected until', fmtDate(a.affectedUntil)], ['Completed', fmtDateTime(a.completedAt)]]} />
        <Stack gap={2}>
          <Section title="Assessment">{a.status === 1 ? <><Prose label="Affected product" text={a.affectedProduct} /><Prose label="Assessment" text={a.assessment} /><Prose label="Decision" text={a.decision} /></>
            : <Alert severity="warning">Identify product measured with this equipment between the last known good calibration and the out-of-tolerance result, then record the assessment.</Alert>}</Section>
          <Section title="Linked nonconformances">{a.ncrs.length ? a.ncrs.map((n) => <Stack key={n.id} direction="row" gap={1} alignItems="center"><Link component={RouterLink} to={`/ncr/${n.id}`} sx={{ fontFamily: mono }}>{n.number}</Link><StatusChip status={ncrStatus[n.status]} /></Stack>) : 'None.'}</Section>
          <AttachmentsAndHistory type="ImpactAssessment" id={a.id} canEdit={a.canApprove && a.status === 0} />
        </Stack>
      </Box>
      <StepDialog open={dlg === 'complete'} title="Complete impact assessment" confirm="Complete" onClose={() => setDlg(null)}
        inputs={[{ name: 'affectedProduct', label: 'Affected product (parts, lots, dates)', type: 'multiline' }, { name: 'assessment', label: 'Assessment', type: 'multiline' }, { name: 'decision', label: 'Decision', type: 'multiline' }]}
        onConfirm={(v) => run(() => post(`/api/calibration/impact/${a.id}/complete`, v))} />
      <StepDialog open={dlg === 'ncr'} title="Raise NCR for affected product" confirm="Create NCR" onClose={() => setDlg(null)}
        inputs={[{ name: 'title', label: 'Title', initial: `Product measured with out-of-tolerance ${a.equipment.number}` }, { name: 'description', label: 'Description', type: 'multiline' }]}
        onConfirm={async (v) => { const r = await post<{ id: string }>(`/api/calibration/impact/${a.id}/ncr`, v); nav(`/ncr/${r.id}`); }} />
    </ModuleWorkspace>
  );
}
