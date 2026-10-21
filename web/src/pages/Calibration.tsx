import { useState } from 'react';
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Chip, Link, MenuItem, Stack, TextField } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { z } from 'zod';
import { del, get, post, put } from '../api';
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
interface EqRow { id: string; number: string; name: string; category: string; assetNumber: string | null; requiresCalibration: boolean; nextMaintenanceOn: string | null; serialNumber: string | null; location: string | null; status: number; lastCalibratedOn: string | null; nextDueOn: string | null; overdue: boolean; siteCode: string }

export function EquipmentList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const view = p.get('view') ?? 'all';
  const category = p.get('category') ?? '';
  const urls: Record<string, string> = { due: '?dueWithinDays=30&calibrated=true', quarantined: '?status=1', down: '?status=4', pm: '?maintenanceDueWithinDays=14', calibrated: '?calibrated=true', tooling: '?calibrated=false' };
  const base = `/api/calibration/equipment${urls[view] ?? '?'}`;
  const url = category ? `${base}&category=${encodeURIComponent(category)}` : base;
  const list = useQuery({ queryKey: ['equipment', view, category], queryFn: () => get<EqRow[]>(url) });
  const cats = useQuery({ queryKey: ['equipment-categories'], queryFn: () => get<string[]>('/api/calibration/equipment-categories') });
  const cols: ColumnDef<EqRow, any>[] = [
    { header: 'ID', accessorKey: 'number', cell: num }, { header: 'Name', accessorKey: 'name' }, { header: 'Type', accessorKey: 'category' }, { header: 'Asset', accessorKey: 'assetNumber' }, { header: 'Serial', accessorKey: 'serialNumber' }, { header: 'Location', accessorKey: 'location' },
    { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={equipmentStatus[x.getValue()]} /> },
    { header: 'Last calibrated', accessorKey: 'lastCalibratedOn', cell: (x) => fmtDate(x.getValue()) },
    { header: 'Calibration due', id: 'due', cell: (x) => x.row.original.requiresCalibration ? <Stack direction="row" gap={1} alignItems="center">{fmtDate(x.row.original.nextDueOn)}{x.row.original.overdue && <Chip size="small" color="error" label="Overdue" />}</Stack> : <Box component="span" sx={{ color: 'text.secondary' }}>Not calibrated</Box> },
    { header: 'Next PM', accessorKey: 'nextMaintenanceOn', cell: (x) => <Box component="span" sx={{ color: x.getValue() && x.getValue() < today() ? 'error.main' : undefined }}>{fmtDate(x.getValue())}</Box> },
    { header: 'Site', accessorKey: 'siteCode' },
  ];
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Equipment' }]} title="Equipment, tooling and gages"
      tabs={[{ label: 'All', value: 'all' }, { label: 'Calibration due 30 d', value: 'due' }, { label: 'Maintenance due', value: 'pm' }, { label: 'Quarantined', value: 'quarantined' }, { label: 'Down', value: 'down' }]} tab={view} onTab={(v) => setP({ view: v })}
      actions={can('calibration.edit') ? <Button variant="contained" component={RouterLink} to="/calibration/equipment/new">Add equipment</Button> : undefined}>
      <TextField select size="small" label="Type" value={category} sx={{ mb: 2, width: 260 }} onChange={(e) => setP(e.target.value ? { view, category: e.target.value } : { view })}>
        <MenuItem value="">All types</MenuItem>{(cats.data ?? []).map((c) => <MenuItem key={c} value={c}>{c}</MenuItem>)}</TextField>
      <DataTable data={list.data ?? []} columns={cols} onRowClick={(r) => nav(`/calibration/equipment/${r.id}`)} empty="No equipment in this view." />
    </ModuleWorkspace>
  );
}

const eqSchema = z.object({
  name: z.string().trim().min(1, 'Required').max(200), manufacturer: z.string().max(200).optional(), model: z.string().max(200).optional(), serialNumber: z.string().max(200).optional(),
  location: z.string().max(200).optional(), measurementRange: z.string().max(200).optional(), tolerance: z.string().max(200).optional(), siteId: z.string().uuid('Choose a site'),
  departmentId: z.string().optional(), ownerId: z.string().optional(), calibrationIntervalDays: z.coerce.number().int().min(1).max(3650), nextDueOn: z.string().optional(),
  category: z.string().min(1, 'Required'), assetNumber: z.string().max(60).optional(), requiresCalibration: z.coerce.number().int(),
});
export function EquipmentNew() {
  const nav = useNavigate();
  const cats = useQuery({ queryKey: ['equipment-categories'], queryFn: () => get<string[]>('/api/calibration/equipment-categories') });
  if (!cats.data) return null;
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Equipment', to: '/calibration/equipment' }, { label: 'New' }]} title="Add equipment">
      <SchemaForm schema={eqSchema} submitLabel="Add equipment" onCancel={() => nav('/calibration/equipment')}
        defaults={{ category: 'Inspection equipment', assetNumber: '', requiresCalibration: 1, name: '', manufacturer: '', model: '', serialNumber: '', location: '', measurementRange: '', tolerance: '', siteId: '', departmentId: '', ownerId: '', calibrationIntervalDays: 365, nextDueOn: today() }}
        fields={[{ name: 'name', label: 'Name', full: true }, { name: 'category', label: 'Equipment type', type: 'select', options: cats.data.map((c) => [c, c] as [string, string]) }, { name: 'assetNumber', label: 'Asset number' },
          { name: 'requiresCalibration', label: 'Calibration control', type: 'select', options: [[1, 'Calibrated (measuring/test equipment)'], [0, 'Not calibrated (maintained only)']] }, { name: 'manufacturer', label: 'Manufacturer' }, { name: 'model', label: 'Model' }, { name: 'serialNumber', label: 'Serial number' }, { name: 'location', label: 'Location' },
          { name: 'measurementRange', label: 'Range' }, { name: 'tolerance', label: 'Tolerance' }, { name: 'siteId', label: 'Site', type: 'site' }, { name: 'departmentId', label: 'Department (optional)', type: 'department' },
          { name: 'calibrationIntervalDays', label: 'Calibration interval (days, if calibrated)', type: 'number' }, { name: 'nextDueOn', label: 'First calibration due', type: 'date' }, { name: 'ownerId', label: 'Owner', type: 'user' }]}
        onSubmit={async (v) => {
          const blank = (s?: string) => (s ? s : null);
          const r = await post<{ id: string }>('/api/calibration/equipment', { ...v, requiresCalibration: v.requiresCalibration === 1, assetNumber: blank(v.assetNumber), manufacturer: blank(v.manufacturer), model: blank(v.model), serialNumber: blank(v.serialNumber), location: blank(v.location), measurementRange: blank(v.measurementRange), tolerance: blank(v.tolerance), departmentId: blank(v.departmentId), ownerId: blank(v.ownerId), nextDueOn: blank(v.nextDueOn) });
          nav(`/calibration/equipment/${r.id}`);
        }} />
    </ModuleWorkspace>
  );
}

interface Ev { id: string; number: string; performedOn: string; performedBy: string; result: number; asFound: string | null; asLeft: string | null; standardsUsed: string | null; certificateNumber: string | null; nextDueOn: string | null }
interface Ia { id: string; number: string; status: number; lastKnownGoodOn: string | null; affectedUntil: string; createdAt: string }
interface Plan { id: string; title: string; instructions: string | null; intervalDays: number; nextDueOn: string; lastDoneOn: string | null }
interface Mnt { id: string; number: string; kind: number; performedOn: string; performedBy: string; description: string; downtimeHours: number | null; returnedToService: boolean; calibrationRequired: boolean; planTitle: string | null }
const mntKind = ['Preventive', 'Repair', 'Breakdown', 'Qualification', 'Validation'];
interface EqDto { category: string; assetNumber: string | null; requiresCalibration: boolean; recalibrateAfter: string | null; qualifiedOn: string | null; qualificationDueOn: string | null; qualificationNotes: string | null; rowVersion: number; ownerId: string; departmentId: string | null; siteCode: string;
  plans: Plan[]; maintenance: Mnt[]; downtime: number; inspections: { id: string; number: string; planNumber: string; planRevision: string; status: number; createdAt: string }[];
  id: string; number: string; name: string; manufacturer: string | null; model: string | null; serialNumber: string | null; location: string | null; measurementRange: string | null; tolerance: string | null; status: number; calibrationIntervalDays: number; lastCalibratedOn: string | null; nextDueOn: string | null; ownerName: string; events: Ev[]; impacts: Ia[]; canEdit: boolean; canApprove: boolean; allowed: string[] }

export function EquipmentDetail() {
  const { id } = useParams();
  const nav = useNavigate();
  const qc = useQueryClient();
  const [tab, setTab] = useState('history');
  const [dlg, setDlg] = useState(false);
  const [mdlg, setMdlg] = useState<string | null>(null);
  const q = useQuery({ queryKey: ['eq', id], queryFn: () => get<EqDto>(`/api/calibration/equipment/${id}`) });
  const { error, run } = useRunner(() => qc.invalidateQueries({ queryKey: ['eq', id] }));
  const e = q.data;
  if (!e) return <ModuleWorkspace module={mod()} crumbs={[{ label: 'Equipment', to: '/calibration/equipment' }]} title="Loading">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const openIa = e.impacts.find((i) => i.status === 0);
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Equipment', to: '/calibration/equipment' }, { label: e.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center" sx={{ flexWrap: 'wrap', rowGap: 0.5 }}><span style={{ fontFamily: mono }}>{e.number}</span><span>{e.name}</span><StatusChip status={equipmentStatus[e.status]} /></Stack>}
      tabs={[...(e.requiresCalibration ? [{ label: 'Calibration history', value: 'history' }, { label: `Impact assessments (${e.impacts.length})`, value: 'impacts' }] : []),
        { label: `Maintenance (${e.maintenance.length})`, value: 'maintenance' }, { label: `Used in inspections (${e.inspections.length})`, value: 'inspections' }, { label: 'Files & history', value: 'more' }]}
      tab={!e.requiresCalibration && (tab === 'history' || tab === 'impacts') ? 'maintenance' : tab} onTab={setTab}
      actions={<Stack direction="row" gap={1} flexWrap="wrap">
        {e.allowed.includes('maintain') && <Button variant="outlined" onClick={() => setMdlg('record')}>Record maintenance</Button>}
        {e.allowed.includes('repair') && <Button variant="contained" color="secondary" onClick={() => setMdlg('repair')}>Record repair</Button>}
        {e.allowed.includes('edit') && <Button onClick={() => setMdlg('edit')}>Edit</Button>}
        {e.allowed.includes('retire') && <Button color="error" onClick={() => setMdlg('retire')}>Retire</Button>}
        {e.allowed.includes('record') && <Button variant="contained" onClick={() => setDlg(true)}>Record calibration</Button>}
        {e.allowed.includes('returnToService') && <Button variant="outlined" color="secondary" onClick={() => run(() => post(`/api/calibration/equipment/${e.id}/return-to-service`))}>Return to service</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {e.status === 1 && <Alert severity="error" sx={{ mb: 2 }} action={openIa && <Button color="inherit" size="small" onClick={() => nav(`/calibration/impact/${openIa.id}`)}>Open {openIa.number}</Button>}>
        {e.recalibrateAfter ? `Quarantined after a repair on ${fmtDate(e.recalibrateAfter)}. Do not use. Return to service needs a passing calibration on or after that date.` : 'Quarantined after an out-of-tolerance result. Do not use. Return to service needs a completed impact assessment and a passing calibration.'}</Alert>}
      {e.status === 4 && <Alert severity="error" sx={{ mb: 2 }}>Down after a breakdown. Do not use until a repair is recorded.</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: '280px minmax(0, 1fr)' }, gap: 3 }}>
        <Facts items={[['Type', e.category], ['Asset number', e.assetNumber ?? '—'], ['Site', e.siteCode], ['Manufacturer / model', [e.manufacturer, e.model].filter(Boolean).join(' ') || '—'], ['Serial', e.serialNumber ?? '—'], ['Location', e.location ?? '—'], ['Range', e.measurementRange ?? '—'], ['Tolerance', e.tolerance ?? '—'],
          ...(e.requiresCalibration ? [['Interval', `${e.calibrationIntervalDays} days`], ['Last calibrated', fmtDate(e.lastCalibratedOn)], ['Next due', fmtDate(e.nextDueOn)]] as [string, string][] : [['Calibration', 'Not calibrated']] as [string, string][]),
          ['Qualified', fmtDate(e.qualifiedOn)], ['Requalify by', fmtDate(e.qualificationDueOn)], ['Downtime logged', `${e.downtime} h`], ['Owner', e.ownerName]]} />
        <Box sx={{ minWidth: 0 }}>
          {tab === 'history' && <DataTable data={e.events} empty="No calibrations recorded." columns={[
            { header: 'Record', accessorKey: 'number', cell: num }, { header: 'Date', accessorKey: 'performedOn', cell: (x) => fmtDate(x.getValue() as string) }, { header: 'By', accessorKey: 'performedBy' },
            { header: 'Result', accessorKey: 'result', cell: (x) => <StatusChip status={calResult[x.getValue() as number]} /> }, { header: 'As found', accessorKey: 'asFound' }, { header: 'As left', accessorKey: 'asLeft' },
            { header: 'Standards', accessorKey: 'standardsUsed' }, { header: 'Certificate', accessorKey: 'certificateNumber' }, { header: 'Next due', accessorKey: 'nextDueOn', cell: (x) => fmtDate(x.getValue() as string) }]} />}
          {tab === 'impacts' && <DataTable data={e.impacts} onRowClick={(i) => nav(`/calibration/impact/${i.id}`)} empty="No out-of-tolerance events." columns={[
            { header: 'Assessment', accessorKey: 'number', cell: num }, { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={impactStatus[x.getValue() as number]} /> },
            { header: 'Last known good', accessorKey: 'lastKnownGoodOn', cell: (x) => fmtDate(x.getValue() as string) }, { header: 'Affected until', accessorKey: 'affectedUntil', cell: (x) => fmtDate(x.getValue() as string) },
            { header: 'Opened', accessorKey: 'createdAt', cell: (x) => fmtDateTime(x.getValue() as string) }]} />}
          {tab === 'maintenance' && <Stack gap={2}>
            <Section title="Preventive maintenance schedule" action={e.allowed.includes('maintain') ? <Button size="small" onClick={() => setMdlg('plan')}>Add task</Button> : undefined}>
              {e.plans.length === 0 ? <Box sx={{ color: 'text.secondary', fontSize: 14 }}>No scheduled tasks.</Box> : <DataTable data={e.plans} empty="" columns={[
                { header: 'Task', accessorKey: 'title' }, { header: 'Every', accessorKey: 'intervalDays', cell: (x) => `${x.getValue()} days` }, { header: 'Last done', accessorKey: 'lastDoneOn', cell: (x) => fmtDate(x.getValue() as string) },
                { header: 'Next due', accessorKey: 'nextDueOn', cell: (x) => <Box component="span" sx={{ color: (x.getValue() as string) < today() ? 'error.main' : undefined }}>{fmtDate(x.getValue() as string)}</Box> },
                { header: 'Instructions', accessorKey: 'instructions' },
                ...(e.allowed.includes('maintain') ? [{ header: '', id: 'x', cell: (x: { row: { original: Plan } }) => <Button size="small" color="error" onClick={() => run(() => del(`/api/calibration/equipment/${e.id}/maintenance-plans/${x.row.original.id}`))}>Deactivate</Button> }] : [])]} />}
            </Section>
            <Section title="Maintenance, repair and qualification history">
              <DataTable data={e.maintenance} empty="No maintenance recorded." columns={[
                { header: 'Record', accessorKey: 'number', cell: num }, { header: 'Date', accessorKey: 'performedOn', cell: (x) => fmtDate(x.getValue() as string) }, { header: 'Type', accessorKey: 'kind', cell: (x) => mntKind[x.getValue() as number] },
                { header: 'Task', accessorKey: 'planTitle' }, { header: 'By', accessorKey: 'performedBy' }, { header: 'Description', accessorKey: 'description' }, { header: 'Downtime h', accessorKey: 'downtimeHours' },
                { header: 'Outcome', id: 'o', cell: (x) => [x.row.original.returnedToService && 'Returned to service', x.row.original.calibrationRequired && 'Recalibration required'].filter(Boolean).join('; ') }]} />
            </Section>
          </Stack>}
          {tab === 'inspections' && <DataTable data={e.inspections} onRowClick={(i) => nav(`/inspection/${i.id}`)} empty="This equipment has not been used to record inspection results." columns={[
            { header: 'Inspection', accessorKey: 'number', cell: num }, { header: 'Plan', id: 'p', cell: (x) => `${x.row.original.planNumber} rev ${x.row.original.planRevision}` }, { header: 'Recorded', accessorKey: 'createdAt', cell: (x) => fmtDateTime(x.getValue() as string) }]} />}
          {tab === 'more' && <AttachmentsAndHistory type="Equipment" id={e.id} canEdit={e.canEdit} />}
        </Box>
      </Box>
      <StepDialog open={mdlg === 'plan'} title="Add preventive maintenance task" confirm="Add" onClose={() => setMdlg(null)}
        inputs={[{ name: 'title', label: 'Task' }, { name: 'intervalDays', label: 'Interval (days)', initial: '30' }, { name: 'firstDueOn', label: 'First due', type: 'date', initial: today() }, { name: 'instructions', label: 'Instructions', type: 'multiline' }]}
        onConfirm={(v) => run(() => post(`/api/calibration/equipment/${e.id}/maintenance-plans`, { title: v.title, intervalDays: Number(v.intervalDays), firstDueOn: v.firstDueOn || null, instructions: v.instructions || null }))} />
      <StepDialog open={mdlg === 'record' || mdlg === 'repair'} title={mdlg === 'repair' ? `Record repair of ${e.number}` : `Record maintenance on ${e.number}`} intro="Maintenance records cannot be edited once saved. A breakdown takes the equipment out of use until a repair is recorded." confirm="Save record" onClose={() => setMdlg(null)}
        inputs={[{ name: 'kind', label: 'Type', type: 'select', initial: mdlg === 'repair' ? '1' : e.plans.length ? '0' : '1', options: mntKind.map((k, i) => [i, k] as [number, string]) },
          { name: 'planId', label: 'Scheduled task (preventive only)', type: 'select', initial: e.plans[0]?.id ?? '', options: [['', 'None'], ...e.plans.map((p) => [p.id, p.title] as [string, string])] },
          { name: 'performedOn', label: 'Date performed', type: 'date', initial: today() }, { name: 'performedBy', label: 'Performed by' }, { name: 'description', label: 'Work done / findings', type: 'multiline' }, { name: 'downtimeHours', label: 'Downtime (hours)' },
          ...(e.status === 4 ? [{ name: 'returnToService', label: 'Repair complete: return to service', type: 'checkbox' as const, initial: mdlg === 'repair' ? 'true' : 'false' }] : []),
          ...(e.requiresCalibration ? [{ name: 'calibrationRequired', label: 'Work affects measurement: recalibrate before use', type: 'checkbox' as const }] : []),
          { name: 'qualificationDueOn', label: 'Requalification due (qualification only)', type: 'date' }]}
        onConfirm={(v) => run(() => post(`/api/calibration/equipment/${e.id}/maintenance`, { kind: Number(v.kind), planId: v.kind === '0' ? v.planId || null : null, performedOn: v.performedOn, performedBy: v.performedBy, description: v.description,
          downtimeHours: v.downtimeHours ? Number(v.downtimeHours) : null, returnToService: v.returnToService === 'true', calibrationRequired: v.calibrationRequired === 'true', qualificationDueOn: v.kind === '3' && v.qualificationDueOn ? v.qualificationDueOn : null }))} />
      <StepDialog open={mdlg === 'edit'} title={`Edit ${e.number}`} confirm="Save" onClose={() => setMdlg(null)}
        inputs={[{ name: 'name', label: 'Name', initial: e.name }, { name: 'assetNumber', label: 'Asset number', initial: e.assetNumber ?? '' }, { name: 'manufacturer', label: 'Manufacturer', initial: e.manufacturer ?? '' }, { name: 'model', label: 'Model', initial: e.model ?? '' },
          { name: 'serialNumber', label: 'Serial number', initial: e.serialNumber ?? '' }, { name: 'location', label: 'Location', initial: e.location ?? '' }, { name: 'measurementRange', label: 'Range', initial: e.measurementRange ?? '' }, { name: 'tolerance', label: 'Tolerance', initial: e.tolerance ?? '' },
          { name: 'calibrationIntervalDays', label: 'Calibration interval (days)', initial: String(e.calibrationIntervalDays) }]}
        onConfirm={(v) => run(() => put(`/api/calibration/equipment/${e.id}`, { ...v, calibrationIntervalDays: Number(v.calibrationIntervalDays), category: e.category, ownerId: e.ownerId, departmentId: e.departmentId, rowVersion: e.rowVersion }))} />
      <StepDialog open={mdlg === 'retire'} title={`Retire ${e.number}`} intro="Retired equipment stays on record with its history but cannot be calibrated, maintained or used for inspection." confirm="Retire" danger onClose={() => setMdlg(null)} inputs={[]}
        onConfirm={() => run(() => post(`/api/calibration/equipment/${e.id}/retire`))} />
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
      title={<Stack direction="row" gap={1.5} alignItems="center" sx={{ flexWrap: 'wrap', rowGap: 0.5 }}><span style={{ fontFamily: mono }}>{a.number}</span><span>Impact assessment</span><StatusChip status={impactStatus[a.status]} /></Stack>}
      actions={<Stack direction="row" gap={1}>
        {a.allowed.includes('ncr') && <Button variant="outlined" onClick={() => setDlg('ncr')}>Raise NCR</Button>}
        {a.allowed.includes('complete') && <Button variant="contained" onClick={() => setDlg('complete')}>Complete assessment</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: '300px minmax(0, 1fr)' }, gap: 3 }}>
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
