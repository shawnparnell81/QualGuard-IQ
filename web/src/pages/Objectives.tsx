import { useState } from 'react';
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Checkbox, FormControlLabel, Stack, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { CartesianGrid, Line, LineChart, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
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

export const objectiveStatus = ['Draft', 'PendingApproval', 'Active', 'Achieved', 'Retired'];
const ratings = ['On target', 'Warning', 'Critical'];
const ratingColor = ['#2e7d32', '#ed6c02', '#c62828'];
const freq = ['Monthly', 'Quarterly', 'Yearly'];
const dirs = ['Higher is better', 'Lower is better'];
const mod = () => modules.find((m) => m.key === 'objectives')!;
const num = (x: { getValue: () => unknown }) => <span style={{ fontFamily: mono }}>{String(x.getValue())}</span>;
const fmtV = (v: number | null | undefined, unit?: string | null) => v == null ? '—' : `${Number(v).toLocaleString(undefined, { maximumFractionDigits: 2 })}${unit === '%' ? '%' : unit && unit !== 'count' ? ` ${unit}` : ''}`;
export const Rating = ({ r }: { r: number | null | undefined }) => r == null ? <span>—</span> :
  <Box component="span" sx={{ px: 1, py: 0.2, borderRadius: 1, fontSize: 12.5, fontWeight: 600, color: '#fff', bgcolor: ratingColor[r], whiteSpace: 'nowrap' }}>{ratings[r]}</Box>;
interface Calc { key: string; label: string; unit: string; direction: number; formula: string }

function Spark({ values }: { values: number[] }) {
  if (values.length < 2) return <span style={{ color: '#999' }}>—</span>;
  const v = [...values].reverse(); const min = Math.min(...v), max = Math.max(...v), w = 80, h = 22;
  const pts = v.map((y, i) => `${(i / (v.length - 1)) * w},${max === min ? h / 2 : h - ((y - min) / (max - min)) * h}`).join(' ');
  return <svg width={w} height={h} aria-label="trend"><polyline points={pts} fill="none" stroke="#37474f" strokeWidth="1.5" /></svg>;
}

interface Row { id: string; number: string; title: string; metricName: string; unit: string | null; target: number; status: number; frequency: number; process: string | null; ownerName: string; siteCode: string;
  latest: { periodStart: string; value: number; rating: number } | null; trend: number[] }

export function ObjectiveList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const view = p.get('view') ?? '2';
  const list = useQuery({ queryKey: ['objectives', view], queryFn: () => get<{ items: Row[] }>(`/api/objectives?pageSize=300${view === 'all' ? '' : `&status=${view}`}`) });
  const cols: ColumnDef<Row, any>[] = [
    { header: 'Number', accessorKey: 'number', cell: num }, { header: 'Objective', accessorKey: 'title' }, { header: 'Metric', accessorKey: 'metricName' }, { header: 'Process', accessorKey: 'process' },
    { header: 'Target', accessorKey: 'target', cell: (x) => fmtV(x.getValue(), x.row.original.unit) },
    { header: 'Latest', id: 'latest', cell: (x) => x.row.original.latest ? `${fmtV(x.row.original.latest.value, x.row.original.unit)} (${fmtDate(x.row.original.latest.periodStart).slice(0, 8)})` : '—' },
    { header: 'Rating', id: 'rating', cell: (x) => <Rating r={x.row.original.latest?.rating} /> }, { header: 'Trend', id: 'trend', cell: (x) => <Spark values={x.row.original.trend} /> },
    { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={objectiveStatus[x.getValue()]} /> }, { header: 'Owner', accessorKey: 'ownerName' }, { header: 'Site', accessorKey: 'siteCode' },
  ];
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Objectives' }]} title="Quality objectives and KPIs"
      tabs={[{ label: 'Active', value: '2' }, { label: 'Draft', value: '0' }, { label: 'Pending approval', value: '1' }, { label: 'Achieved', value: '3' }, { label: 'All', value: 'all' }]} tab={view} onTab={(v) => setP(v === '2' ? {} : { view: v })}
      actions={can('objectives.edit') ? <Button variant="contained" component={RouterLink} to="/objectives/new">New objective</Button> : undefined}>
      <DataTable data={list.data?.items ?? []} columns={cols} onRowClick={(r) => nav(`/objectives/${r.id}`)} empty="No objectives in this view." />
    </ModuleWorkspace>
  );
}

const schema = z.object({
  title: z.string().trim().min(1, 'Required').max(300), description: z.string().max(4000).optional(), process: z.string().max(200).optional(),
  metricName: z.string().trim().min(1, 'Required').max(200), metricDefinition: z.string().trim().min(1, 'Required').max(4000), dataSource: z.string().min(1), unit: z.string().max(30).optional(),
  direction: z.coerce.number().int(), frequency: z.coerce.number().int(), baseline: z.union([z.literal(''), z.coerce.number()]).optional(),
  target: z.coerce.number(), warning: z.coerce.number(), critical: z.coerce.number(), triggerActionOnCritical: z.coerce.number().int(), actionPlan: z.string().max(4000).optional(),
  startsOn: z.string().min(10, 'Required'), endsOn: z.string().optional(), siteId: z.string().uuid('Choose a site'), departmentId: z.string().optional(), ownerId: z.string().optional(),
});
const nul = (v?: string) => (v ? v : null);

export function ObjectiveNew() {
  const nav = useNavigate();
  const calcs = useQuery({ queryKey: ['kpi-calcs'], queryFn: () => get<Calc[]>('/api/objectives/calculations') });
  if (!calcs.data) return null;
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Objectives', to: '/objectives' }, { label: 'New' }]} title="New quality objective">
      <Alert severity="info" sx={{ mb: 2, maxWidth: 820 }}>Calculated data sources use controlled, documented formulas over system records for the objective's site (and department). Choose manual entry for metrics kept outside the system. Targets are approved before measurement starts.</Alert>
      <SchemaForm schema={schema} submitLabel="Create draft" onCancel={() => nav('/objectives')}
        defaults={{ title: '', description: '', process: '', metricName: '', metricDefinition: '', dataSource: 'manual', unit: '', direction: 0, frequency: 0, baseline: '', target: 0, warning: 0, critical: 0, triggerActionOnCritical: 1, actionPlan: '', startsOn: localDate(), endsOn: '', siteId: '', departmentId: '', ownerId: '' }}
        fields={[{ name: 'title', label: 'Objective', full: true }, { name: 'description', label: 'Description', type: 'multiline' }, { name: 'process', label: 'Process' }, { name: 'metricName', label: 'Metric (KPI) name' },
          { name: 'dataSource', label: 'Data source', type: 'select', options: [['manual', 'Manual entry'], ...calcs.data.map((c) => [c.key, `Calculated: ${c.label}`] as [string, string])] },
          { name: 'metricDefinition', label: 'Metric definition and calculation', type: 'multiline', helper: 'For calculated sources the system formula is shown on the objective; describe the intent here.' },
          { name: 'unit', label: 'Unit (%, count, ppm, days…)' }, { name: 'direction', label: 'Direction', type: 'select', options: dirs.map((d, i) => [i, d] as [number, string]) }, { name: 'frequency', label: 'Frequency', type: 'select', options: freq.map((d, i) => [i, d] as [number, string]) },
          { name: 'thr' as never, label: 'Targets and thresholds', type: 'heading' }, { name: 'baseline', label: 'Baseline', type: 'number' }, { name: 'target', label: 'Target', type: 'number' },
          { name: 'warning', label: 'Warning threshold', type: 'number', helper: 'Between target and critical' }, { name: 'critical', label: 'Critical threshold', type: 'number' },
          { name: 'triggerActionOnCritical', label: 'Corrective-action trigger', type: 'select', options: [[1, 'Raise an action on a critical result'], [0, 'No automatic action']] }, { name: 'actionPlan', label: 'Action plan', type: 'multiline' },
          { name: 'startsOn', label: 'Starts', type: 'date' }, { name: 'endsOn', label: 'Ends (optional)', type: 'date' }, { name: 'siteId', label: 'Site', type: 'site' }, { name: 'departmentId', label: 'Department (optional)', type: 'department' }, { name: 'ownerId', label: 'Owner (defaults to you)', type: 'user' }]}
        onSubmit={async (v) => {
          const r = await post<{ id: string }>('/api/objectives', { ...v, description: nul(v.description), process: nul(v.process), unit: nul(v.unit), baseline: v.baseline === '' || v.baseline === undefined ? null : v.baseline, actionPlan: nul(v.actionPlan),
            endsOn: nul(v.endsOn), departmentId: nul(v.departmentId), ownerId: nul(v.ownerId), triggerActionOnCritical: v.triggerActionOnCritical === 1 });
          nav(`/objectives/${r.id}`);
        }} />
    </ModuleWorkspace>
  );
}

interface Meas { id: string; periodStart: string; periodEnd: string; value: number; rating: number; calculated: boolean; basis: string | null; notes: string | null; supersededById: string | null; createdAt: string; recordedByName: string }
interface Dto { id: string; number: string; title: string; description: string | null; siteId: string; departmentId: string | null; process: string | null; ownerId: string; status: number; metricName: string; metricDefinition: string; dataSource: string;
  unit: string | null; direction: number; frequency: number; baseline: number | null; target: number; warning: number; critical: number; triggerActionOnCritical: boolean; actionPlan: string | null; startsOn: string; endsOn: string | null; approvedAt: string | null; rowVersion: number;
  calculation: Calc | null; ownerName: string; siteCode: string; measurements: Meas[]; canEdit: boolean; allowed: string[] }

export function ObjectiveDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const [tab, setTab] = useState('performance');
  const [dlg, setDlg] = useState<string | null>(null);
  const [showAll, setShowAll] = useState(false);
  const q = useQuery({ queryKey: ['objective', id], queryFn: () => get<Dto>(`/api/objectives/${id}`) });
  const refresh = () => qc.invalidateQueries({ queryKey: ['objective', id] });
  const { error, run } = useRunner(refresh);
  const d = q.data;
  if (!d) return <ModuleWorkspace module={mod()} crumbs={[{ label: 'Objectives', to: '/objectives' }]} title="Loading">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const a = (k: string) => d.allowed.includes(k);
  const current = d.measurements.filter((m) => !m.supersededById);
  const chart = [...current].reverse().map((m) => ({ period: m.periodStart.slice(0, 7), value: Number(m.value) }));
  const body = (o: Partial<Dto>) => ({ title: d.title, description: d.description, siteId: d.siteId, departmentId: d.departmentId, process: d.process, ownerId: d.ownerId, metricName: d.metricName, metricDefinition: d.metricDefinition, dataSource: d.dataSource,
    unit: d.unit, direction: d.direction, frequency: d.frequency, baseline: d.baseline, target: d.target, warning: d.warning, critical: d.critical, triggerActionOnCritical: d.triggerActionOnCritical, actionPlan: d.actionPlan, startsOn: d.startsOn, endsOn: d.endsOn, rowVersion: d.rowVersion, ...o });
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Objectives', to: '/objectives' }, { label: d.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono, whiteSpace: 'nowrap' }}>{d.number}</span><span>{d.title}</span><StatusChip status={objectiveStatus[d.status]} /></Stack>}
      tabs={[{ label: 'Performance', value: 'performance' }, { label: 'Definition', value: 'definition' }, { label: 'Actions', value: 'actions' }, { label: 'Files & history', value: 'more' }]} tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1} flexWrap="wrap">
        {a('measure') && <Button variant="contained" onClick={() => setDlg('measure')}>{d.dataSource === 'manual' ? 'Record result' : 'Calculate result'}</Button>}
        {a('submit') && <Button variant="contained" onClick={() => run(() => post(`/api/objectives/${d.id}/submit`))}>Submit for approval</Button>}
        {a('edit') && <Button variant="outlined" onClick={() => setDlg(d.status === 0 ? 'edit' : 'narrative')}>Edit</Button>}
        {a('revise') && <Button onClick={() => setDlg('revise')}>Revise targets</Button>}
        {a('outcome') && <Button onClick={() => setDlg('outcome')}>Close out</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {d.status === 1 && <Alert severity="info" sx={{ mb: 2 }}>Waiting for approval of the objective and its targets.</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: '290px 1fr', gap: 3 }}>
        <Facts items={[['Metric', d.metricName], ['Data source', d.calculation ? `Calculated: ${d.calculation.label}` : 'Manual entry'], ['Direction', dirs[d.direction]], ['Frequency', freq[d.frequency]],
          ['Baseline', fmtV(d.baseline, d.unit)], ['Target', fmtV(d.target, d.unit)], ['Warning at', fmtV(d.warning, d.unit)], ['Critical at', fmtV(d.critical, d.unit)],
          ['Latest', current[0] ? <Stack direction="row" gap={1} alignItems="center"><span>{fmtV(current[0].value, d.unit)}</span><Rating r={current[0].rating} /></Stack> : '—'],
          ['Process', d.process ?? '—'], ['Owner', d.ownerName], ['Site', d.siteCode], ['Period', `${fmtDate(d.startsOn)} – ${d.endsOn ? fmtDate(d.endsOn) : 'open'}`], ['Approved', fmtDateTime(d.approvedAt)]]} />
        <Box sx={{ minWidth: 0 }}>
          {tab === 'performance' && <Stack gap={2}>
            <Section title="Trend">{chart.length === 0 ? <Typography color="text.secondary" fontSize={14}>No results yet.</Typography> :
              <Box sx={{ height: 260 }}><ResponsiveContainer><LineChart data={chart} margin={{ top: 10, right: 20, bottom: 0, left: 0 }}>
                <CartesianGrid strokeDasharray="3 3" /><XAxis dataKey="period" fontSize={12} /><YAxis fontSize={12} /><Tooltip />
                <ReferenceLine y={Number(d.target)} stroke="#2e7d32" strokeDasharray="4 4" label={{ value: 'Target', fontSize: 11, position: 'right' }} />
                <ReferenceLine y={Number(d.critical)} stroke="#c62828" strokeDasharray="4 4" label={{ value: 'Critical', fontSize: 11, position: 'right' }} />
                <Line type="monotone" dataKey="value" stroke="#263238" strokeWidth={2} dot />
              </LineChart></ResponsiveContainer></Box>}</Section>
            <Section title="Results" action={<FormControlLabel control={<Checkbox size="small" checked={showAll} onChange={(e) => setShowAll(e.target.checked)} />} label="Show superseded" />}>
              <Table size="small"><TableHead><TableRow><TableCell>Period</TableCell><TableCell>Value</TableCell><TableCell>Rating</TableCell><TableCell>Basis</TableCell><TableCell>Notes</TableCell><TableCell>Recorded</TableCell></TableRow></TableHead>
                <TableBody>{(showAll ? d.measurements : current).map((m) => <TableRow key={m.id} sx={{ opacity: m.supersededById ? 0.5 : 1 }}>
                  <TableCell>{fmtDate(m.periodStart)} – {fmtDate(m.periodEnd)}</TableCell><TableCell sx={{ fontFamily: mono }}>{fmtV(m.value, d.unit)}</TableCell><TableCell><Rating r={m.rating} /></TableCell>
                  <TableCell>{m.basis ?? (m.calculated ? '' : 'Manual')}</TableCell><TableCell>{m.notes}{m.supersededById ? ' (superseded)' : ''}</TableCell><TableCell>{m.recordedByName}, {fmtDateTime(m.createdAt)}</TableCell></TableRow>)}</TableBody></Table>
            </Section>
          </Stack>}
          {tab === 'definition' && <Stack gap={2}>
            <Section title="Objective"><Prose label="Description" text={d.description} /><Prose label="Action plan" text={d.actionPlan} />
              <Typography variant="body2">Corrective-action trigger: {d.triggerActionOnCritical ? 'an action is raised automatically when a result is critical (one open action at a time).' : 'off.'}</Typography></Section>
            <Section title="Metric definition"><Prose label="Definition" text={d.metricDefinition} />
              {d.calculation && <><Prose label="Controlled calculation" text={`${d.calculation.label} (${d.calculation.key})`} /><Prose label="Formula" text={d.calculation.formula} /></>}</Section>
          </Stack>}
          {tab === 'actions' && <ActionsPanel type="QualityObjective" id={d.id} canEdit={a('actions')} canVerifyAs={d.canEdit} onChanged={refresh} />}
          {tab === 'more' && <AttachmentsAndHistory type="QualityObjective" id={d.id} canEdit={d.canEdit} />}
        </Box>
      </Box>
      <StepDialog open={dlg === 'measure'} title={d.dataSource === 'manual' ? 'Record result' : 'Calculate result'} confirm={d.dataSource === 'manual' ? 'Record' : 'Calculate'} onClose={() => setDlg(null)}
        intro={d.dataSource === 'manual' ? 'Pick any day in the period. A second result for the same period supersedes the first.' : `The value is calculated from records: ${d.calculation?.formula} Recalculating a period supersedes the earlier result.`}
        inputs={[{ name: 'periodStart', label: 'Day in period', type: 'date', initial: localDate() }, ...(d.dataSource === 'manual' ? [{ name: 'value', label: `Value${d.unit ? ` (${d.unit})` : ''}` }] : []), { name: 'notes', label: 'Notes / evidence', type: 'multiline' as const }]}
        onConfirm={(v) => run(() => post(`/api/objectives/${d.id}/measurements`, { periodStart: v.periodStart, value: v.value ? Number(v.value) : null, notes: v.notes || null }))} />
      <StepDialog open={dlg === 'edit'} title="Edit draft targets" confirm="Save" onClose={() => setDlg(null)}
        inputs={[{ name: 'title', label: 'Objective', initial: d.title }, { name: 'target', label: 'Target', initial: String(d.target) }, { name: 'warning', label: 'Warning threshold', initial: String(d.warning) }, { name: 'critical', label: 'Critical threshold', initial: String(d.critical) },
          { name: 'metricDefinition', label: 'Metric definition', type: 'multiline', initial: d.metricDefinition }, { name: 'actionPlan', label: 'Action plan', type: 'multiline', initial: d.actionPlan ?? '' }]}
        onConfirm={(v) => run(() => put(`/api/objectives/${d.id}`, body({ title: v.title, target: Number(v.target), warning: Number(v.warning), critical: Number(v.critical), metricDefinition: v.metricDefinition, actionPlan: v.actionPlan || null })))} />
      <StepDialog open={dlg === 'narrative'} title="Edit objective" intro="Targets, thresholds and the metric of an active objective are frozen; use Revise targets to change them." confirm="Save" onClose={() => setDlg(null)}
        inputs={[{ name: 'description', label: 'Description', type: 'multiline', initial: d.description ?? '' }, { name: 'actionPlan', label: 'Action plan', type: 'multiline', initial: d.actionPlan ?? '' }, { name: 'endsOn', label: 'Ends', type: 'date', initial: d.endsOn ?? '' }]}
        onConfirm={(v) => run(() => put(`/api/objectives/${d.id}`, body({ description: v.description || null, actionPlan: v.actionPlan || null, endsOn: v.endsOn || null })))} />
      <StepDialog open={dlg === 'revise'} title="Revise targets" intro="The objective returns to draft and needs approval again before new results are recorded." confirm="Return to draft" onClose={() => setDlg(null)}
        inputs={[{ name: 'text', label: 'Reason', type: 'multiline' }]} onConfirm={(v) => run(() => post(`/api/objectives/${d.id}/revise`, v))} />
      <StepDialog open={dlg === 'outcome'} title="Close out objective" confirm="Close out" onClose={() => setDlg(null)}
        inputs={[{ name: 'achieved', label: 'Outcome', type: 'select', initial: 'true', options: [['true', 'Achieved'], ['false', 'Retired (not achieved / no longer relevant)']] }]}
        onConfirm={(v) => run(() => post(`/api/objectives/${d.id}/outcome`, { achieved: v.achieved === 'true' }))} />
    </ModuleWorkspace>
  );
}
