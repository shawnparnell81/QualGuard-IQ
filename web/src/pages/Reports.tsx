import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Grid, Paper, Stack, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { Bar, BarChart, CartesianGrid, Legend, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { get } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { fmtDate, fmtDateTime } from '../components/Status';
import { PanelHead, Readout } from '../components/Kit';

interface Def { key: string; title: string; category: string; permission: string; description: string; usesDates: boolean }
interface Col { key: string; label: string; type: string }
interface Result { definition: Def; params: { from: string; to: string }; columns: Col[]; rows: Record<string, unknown>[]; chart: { type: string; x: string; y: string[] } | null }
const mod = () => modules.find((m) => m.key === 'reports')!;
const palette = ['#37474f', '#b0754a', '#5b7f62', '#8a6d9e'];

const fmt = (v: unknown, t: string) => v == null || v === '' ? '' : t === 'date' ? fmtDate(String(v)) : t === 'datetime' ? fmtDateTime(String(v))
  : t === 'money' ? Number(v).toLocaleString(undefined, { style: 'currency', currency: 'USD' }) : t === 'number' ? Number(v).toLocaleString() : String(v);

function Chart({ r, height = 260 }: { r: Result; height?: number }) {
  if (!r.chart || r.rows.length === 0) return null;
  const label = (k: string) => r.columns.find((c) => c.key === k)?.label ?? k;
  const C = r.chart.type === 'line' ? LineChart : BarChart;
  return (
    <ResponsiveContainer width="100%" height={height}>
      <C data={r.rows}>
        <CartesianGrid strokeDasharray="3 3" /><XAxis dataKey={r.chart.x} fontSize={12} /><YAxis fontSize={12} /><Tooltip />{r.chart.y.length > 1 && <Legend />}
        {r.chart.y.map((y, i) => r.chart!.type === 'line'
          ? <Line key={y} dataKey={y} name={label(y)} stroke={palette[i]} strokeWidth={2} />
          : <Bar key={y} dataKey={y} name={label(y)} fill={palette[i]} />)}
      </C>
    </ResponsiveContainer>
  );
}

export function ReportCatalog() {
  const nav = useNavigate();
  const list = useQuery({ queryKey: ['reports'], queryFn: () => get<Def[]>('/api/reports') });
  const cats = [...new Set((list.data ?? []).map((d) => d.category))];
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Reports' }]} title="Report library">
      <Typography color="text.secondary" sx={{ mb: 2, maxWidth: 820 }}>Only reports on modules you can view are listed. Results include records from your sites and departments only.</Typography>
      {cats.map((c) => (
        <Paper key={c} sx={{ mb: 2.5, overflow: 'hidden' }} component="section" aria-label={c}>
          <PanelHead title={c} />
          {list.data!.filter((d) => d.category === c).map((d, i) => (
            <Box key={d.key} component="button" type="button" onClick={() => nav(`/reports/r/${d.key}`)}
              sx={{ all: 'unset', boxSizing: 'border-box', width: '100%', cursor: 'pointer', display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: 'minmax(220px, 32%) 1fr' }, gap: { xs: 0.25, md: 2 }, px: 2, py: 1.1, borderTop: i ? 1 : 0, borderColor: 'divider',
                '&:hover, &:focus-visible': { bgcolor: 'action.hover' }, '&:focus-visible': { outline: '2px solid', outlineColor: 'primary.main', outlineOffset: -2 } }}>
              <Typography fontWeight={600} variant="body2">{d.title}</Typography><Typography variant="body2" color="text.secondary">{d.description}</Typography>
            </Box>))}
        </Paper>))}
      {list.data?.length === 0 && <Alert severity="info">No reports are available for your permissions.</Alert>}
    </ModuleWorkspace>
  );
}

export function ReportRun() {
  const { key } = useParams();
  const nav = useNavigate();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const qs = new URLSearchParams(); if (p.get('from')) qs.set('from', p.get('from')!); if (p.get('to')) qs.set('to', p.get('to')!);
  const r = useQuery({ queryKey: ['report', key, qs.toString()], queryFn: () => get<Result>(`/api/reports/${key}?${qs}`) });
  const cols: ColumnDef<Record<string, unknown>, any>[] = (r.data?.columns ?? []).map((c) => ({ header: c.label, accessorKey: c.key, cell: (x) => fmt(x.getValue(), c.type) }));
  const setDate = (k: string, v: string) => { const n = new URLSearchParams(p); if (v) n.set(k, v); else n.delete(k); setP(n); };
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Reports', to: '/reports/catalog' }, { label: r.data?.definition.title ?? '…' }]} title={r.data?.definition.title ?? 'Report'}
      actions={can('reports.export') && r.data ? <Button variant="outlined" component="a" href={`/api/reports/${key}/csv?${qs}`}>Export CSV</Button> : undefined}>
      {r.error && <Alert severity="error">{(r.error as Error).message}</Alert>}
      {r.data && <>
        <Typography color="text.secondary" sx={{ mb: 2 }}>{r.data.definition.description}</Typography>
        {r.data.definition.usesDates && (
          <Stack direction="row" spacing={2} sx={{ mb: 2 }}>
            <TextField type="date" size="small" label="From" value={r.data.params.from} onChange={(e) => setDate('from', e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
            <TextField type="date" size="small" label="To" value={r.data.params.to} onChange={(e) => setDate('to', e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
          </Stack>)}
        <Chart r={r.data} />
        <DataTable data={r.data.rows} columns={cols} onRowClick={(row) => row._url ? nav(String(row._url)) : undefined} empty="No records match." />
      </>}
    </ModuleWorkspace>
  );
}

const tiles: { key: string; title: string }[] = [
  { key: 'ncr.volume', title: 'Nonconformances per month' }, { key: 'complaints.trend', title: 'Complaints per month' },
  { key: 'inspection.results', title: 'Inspections and failures' }, { key: 'approvals.cycle_time', title: 'Approval cycle time (days)' },
  { key: 'training.compliance', title: 'Training compliance %' }, { key: 'suppliers.performance', title: 'Supplier overall score' },
];

function Tile({ k, title }: { k: string; title: string }) {
  const r = useQuery({ queryKey: ['report', k, ''], queryFn: () => get<Result>(`/api/reports/${k}`) });
  return (
    <Paper sx={{ overflow: 'hidden', height: '100%' }}>
      <PanelHead title={title} action={<Button size="small" component={RouterLink} to={`/reports/r/${k}`}>Open</Button>} />
      <Box sx={{ p: 2 }}>{r.data && (r.data.rows.length ? <Chart r={r.data} height={200} /> : <Typography color="text.secondary" sx={{ py: 4 }}>No data in the last 12 months.</Typography>)}</Box>
    </Paper>
  );
}

export function ReportDashboard() {
  const list = useQuery({ queryKey: ['reports'], queryFn: () => get<Def[]>('/api/reports') });
  const avail = new Set((list.data ?? []).map((d) => d.key));
  const overdue = useQuery({ queryKey: ['report', 'actions.overdue', ''], queryFn: () => get<Result>('/api/reports/actions.overdue'), enabled: avail.has('actions.overdue') });
  const aging = useQuery({ queryKey: ['report', 'ncr.aging', ''], queryFn: () => get<Result>('/api/reports/ncr.aging'), enabled: avail.has('ncr.aging') });
  const pending = useQuery({ queryKey: ['report', 'approvals.pending', ''], queryFn: () => get<Result>('/api/reports/approvals.pending'), enabled: avail.has('approvals.pending') });
  const kpi = (label: string, v: number | undefined, to: string) => (
    <Readout key={label} label={label} value={v ?? '—'} to={to} color="primary.main" />);
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Dashboard' }]} title="Quality dashboard">
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(190px, 1fr))', gap: 1.5, mb: 2 }}>
        {avail.has('actions.overdue') && kpi('Overdue actions', overdue.data?.rows.length, '/reports/r/actions.overdue')}
        {avail.has('ncr.aging') && kpi('Open NCRs', aging.data?.rows.length, '/reports/r/ncr.aging')}
        {avail.has('ncr.aging') && kpi('NCRs open > 30 days', aging.data?.rows.filter((r) => Number(r.days) > 30).length, '/reports/r/ncr.aging')}
        {avail.has('approvals.pending') && kpi('Pending approvals', pending.data?.rows.length, '/reports/r/approvals.pending')}
      </Box>
      <Grid container spacing={2}>
        {tiles.filter((t) => avail.has(t.key)).map((t) => <Grid key={t.key} size={{ xs: 12, lg: 6 }}><Tile k={t.key} title={t.title} /></Grid>)}
      </Grid>
    </ModuleWorkspace>
  );
}
