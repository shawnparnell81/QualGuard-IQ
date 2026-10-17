import { useState, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { Alert, Box, Button, Chip, MenuItem, Paper, Slider, Stack, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { get, post } from '../api';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { fmtDateTime } from '../components/Status';
import { Section, lookupLabel, useLookup, useRunner } from '../components/Kit';
import { mono } from '../theme';
import { Loading, SiteSelect, Tile, mod, num, useSite } from './Enterprise';

/* eslint-disable @typescript-eslint/no-explicit-any */
const C = (label: string) => [{ label: 'Quality OS', to: '/qos' }, { label }];
const scoreColor = (s: number | null | undefined) => (s == null ? 'divider' : s >= 85 ? 'success.main' : s >= 70 ? 'warning.main' : 'error.main');
const fmt = (v: number | null | undefined) => (v == null ? 'n/a' : Number(v).toFixed(1));

/** Ring gauge for a 0–100 score. */
function Gauge({ value, label, size = 120 }: { value: number | null; label: string; size?: number }) {
  const r = size / 2 - 10; const c = 2 * Math.PI * r; const v = Math.max(0, Math.min(100, value ?? 0));
  const col = value == null ? '#bdbdbd' : v >= 85 ? '#2e7d32' : v >= 70 ? '#f9a825' : '#c62828';
  return (
    <Box sx={{ textAlign: 'center' }} data-testid="gauge">
      <svg width={size} height={size}><circle cx={size / 2} cy={size / 2} r={r} stroke="#e0e0e0" strokeWidth={10} fill="none" />
        <circle cx={size / 2} cy={size / 2} r={r} stroke={col} strokeWidth={10} fill="none" strokeDasharray={`${(c * v) / 100} ${c}`} transform={`rotate(-90 ${size / 2} ${size / 2})`} />
        <text x="50%" y="54%" textAnchor="middle" style={{ font: `600 ${size / 5}px ${mono}` }}>{value == null ? '—' : Math.round(v)}</text></svg>
      <Typography variant="caption" display="block">{label}</Typography>
    </Box>
  );
}

export function CommandCenter() {
  const q = useQuery({ queryKey: ['command-center'], queryFn: () => get<any>('/api/qos/command-center') });
  if (!q.data) return <Loading m="qos" crumbs={C('Command center')} q={q} />;
  const d = q.data; const g = d.globalStatus;
  return (
    <ModuleWorkspace module={mod('qos')} crumbs={[{ label: 'Quality OS' }]} title="Global quality command center">
      <Paper sx={{ p: 2, mb: 2, display: 'flex', gap: 3, alignItems: 'center', flexWrap: 'wrap', background: 'linear-gradient(90deg, rgba(0,121,107,.08), transparent)' }}>
        <Gauge value={g.corporate.score} label="Corporate quality score" size={150} />
        {g.regions.map((r: any) => <Gauge key={r.id} value={r.score} label={`${r.name} (region)`} size={100} />)}
        <Box sx={{ flex: 1, minWidth: 260 }}>
          <Typography variant="overline">Score components (corporate)</Typography>
          {g.corporate.components.map((c: any) => <Stack key={c.component} direction="row" gap={1} alignItems="center" sx={{ mb: 0.5 }}>
            <Typography variant="caption" sx={{ width: 170 }}>{c.component}</Typography>
            <Box sx={{ flex: 1, height: 8, bgcolor: 'grey.200', borderRadius: 1 }}><Box sx={{ width: `${c.score ?? 0}%`, height: 8, borderRadius: 1, bgcolor: scoreColor(c.score) }} /></Box>
            <Typography variant="caption" sx={{ fontFamily: mono, width: 40 }}>{fmt(c.score)}</Typography></Stack>)}
        </Box>
      </Paper>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(4, 1fr)' }, gap: 1.5, mb: 2 }}>
        <Tile label="Suppliers scored" value={d.supplierPerformance.scored} hint={`${d.supplierPerformance.high} high risk · ${d.supplierPerformance.medium} medium`} color={d.supplierPerformance.high ? 'error.main' : 'success.main'} />
        <Tile label="Complaints (12 months)" value={d.complaintPerformance.received12m} hint={`${d.complaintPerformance.open} open · ${d.complaintPerformance.overdue} overdue`} color={d.complaintPerformance.overdue ? 'warning.main' : 'success.main'} />
        <Tile label="Average days to close" value={d.complaintPerformance.avgDaysToClose ?? '—'} />
        <Tile label="Cost of quality (90 days)" value={d.costOfQuality.reduce((a: number, c: any) => a + c.amount, 0).toLocaleString()} hint={d.costOfQuality.map((c: any) => c.category).join(' · ')} />
      </Box>
      <Stack direction={{ xs: 'column', md: 'row' }} gap={2}>
        <Box sx={{ flex: 1 }}><Section title="Plant ranking">
          <DataTable data={g.sites} empty="No sites." columns={[{ header: 'Site', accessorKey: 'code', cell: num }, { header: 'Name', accessorKey: 'name' }, { header: 'Score', accessorKey: 'score', cell: (x) => <Chip size="small" sx={{ bgcolor: scoreColor(x.getValue()), color: '#fff' }} label={fmt(x.getValue())} /> }]} />
        </Section></Box>
        <Box sx={{ flex: 1 }}><Section title="Customers by complaints">
          <DataTable data={d.customerPerformance} empty="No complaints." columns={[{ header: 'Customer', accessorKey: 'customer' }, { header: 'Complaints', accessorKey: 'complaints' }, { header: 'Open', accessorKey: 'open' }]} />
        </Section></Box>
        <Box sx={{ flex: 1 }}><Section title="Highest-risk suppliers">
          <DataTable data={d.supplierPerformance.worst} empty="No supplier data." columns={[{ header: 'Supplier', accessorKey: 'name' }, { header: 'Health', accessorKey: 'health', cell: (x) => fmt(x.getValue()) }, { header: 'Level', accessorKey: 'level' }]} />
        </Section></Box>
      </Stack>
    </ModuleWorkspace>
  );
}

function Lane({ title, items, color }: { title: string; items: any[]; color: string }) {
  const nav = useNavigate();
  return (
    <Paper variant="outlined" sx={{ p: 1, bgcolor: 'grey.50', minHeight: 200 }} data-testid={`lane-${title}`}>
      <Stack direction="row" justifyContent="space-between" sx={{ mb: 1 }}><Typography variant="overline" sx={{ fontWeight: 700 }}>{title}</Typography><Chip size="small" label={items.length} sx={{ bgcolor: items.length ? color : undefined, color: items.length ? '#fff' : undefined }} /></Stack>
      <Stack gap={0.75}>{items.slice(0, 12).map((i, n) => <Paper key={n} sx={{ p: 0.75, cursor: i.url ? 'pointer' : 'default', borderLeft: 3, borderColor: color }} onClick={() => i.url && nav(i.url)}>
        <Typography variant="caption" sx={{ fontFamily: mono }}>{i.number}</Typography><Typography variant="body2" sx={{ fontSize: 12 }}>{i.title}</Typography></Paper>)}</Stack>
    </Paper>
  );
}

export function FactoryCenter() {
  const [site, setSite] = useSite();
  const q = useQuery({ queryKey: ['factory', site], enabled: !!site, queryFn: () => get<any>(`/api/qos/factory/${site}`) });
  const d = q.data;
  return (
    <ModuleWorkspace module={mod('qos')} crumbs={C('Factory center')} title="Factory quality center" actions={<SiteSelect value={site} onChange={setSite} />}>
      {q.error && <Alert severity="error">{(q.error as Error).message}</Alert>}
      {d && <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(6, 1fr)' }, gap: 1 }}>
        <Lane title="Quality alerts" items={d.qualityAlerts} color="#c62828" /><Lane title="SPC" items={d.spcViolations} color="#6a1b9a" /><Lane title="Production" items={d.productionAlerts} color="#ef6c00" />
        <Lane title="NCRs" items={d.openNcrs} color="#d84315" /><Lane title="CAPAs" items={d.openCapas} color="#1565c0" /><Lane title="Suppliers" items={d.supplierIssues} color="#00796b" />
      </Box>}
    </ModuleWorkspace>
  );
}

export function EnterpriseRiskMatrix() {
  const q = useQuery({ queryKey: ['risk-matrix'], queryFn: () => get<any>('/api/qos/risk-matrix') });
  if (!q.data) return <Loading m="qos" crumbs={C('Risk matrix')} q={q} />;
  const d = q.data; const max = Math.max(1, ...d.heatmap.map((h: any) => h.count));
  const cell = (p: number, s: number) => d.heatmap.find((h: any) => h.probability === p && h.severity === s)?.count ?? 0;
  const hue = (p: number, s: number) => { const sc = p * s; return sc >= 17 ? '#c62828' : sc >= 10 ? '#ef6c00' : sc >= 5 ? '#f9a825' : '#2e7d32'; };
  return (
    <ModuleWorkspace module={mod('qos')} crumbs={C('Risk matrix')} title="Enterprise risk matrix">
      <Stack direction={{ xs: 'column', md: 'row' }} gap={3}>
        <Box>
          <Typography variant="overline">Open risks: probability × severity</Typography>
          <Box sx={{ display: 'grid', gridTemplateColumns: '30px repeat(5, 56px)', gap: 0.5 }} data-testid="heatmap">
            {[5, 4, 3, 2, 1].map((p) => [<Typography key={`l${p}`} variant="caption" sx={{ alignSelf: 'center' }}>P{p}</Typography>,
              ...[1, 2, 3, 4, 5].map((s) => { const n = cell(p, s); return <Box key={`${p}-${s}`} sx={{ height: 56, borderRadius: 1, bgcolor: hue(p, s), opacity: n ? 0.35 + (0.65 * n) / max : 0.15, color: '#fff', display: 'grid', placeItems: 'center', fontFamily: mono, fontWeight: 700 }}>{n || ''}</Box>; })])}
            <span />{[1, 2, 3, 4, 5].map((s) => <Typography key={s} variant="caption" align="center">S{s}</Typography>)}
          </Box>
        </Box>
        <Box sx={{ flex: 1 }}><Section title="By enterprise category">
          <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(5, 1fr)', gap: 1 }}>{d.categories.map((c: any) => <Tile key={c.category} label={c.category} value={c.open} hint={`${c.high} high`} color={c.high ? 'error.main' : 'success.main'} />)}</Box>
        </Section>
          <Box sx={{ mt: 2 }}><Section title="12-month trend">
            <Stack direction="row" gap={0.5} alignItems="flex-end" sx={{ height: 90 }}>{d.trend.map((t: any) => <Box key={t.month} title={`${t.month}: ${t.open} open, ${t.opened} opened, ${t.closed} closed`} sx={{ flex: 1, bgcolor: 'primary.main', opacity: 0.75, height: `${(100 * t.open) / Math.max(1, ...d.trend.map((x: any) => x.open))}%`, minHeight: 2 }} />)}</Stack>
          </Section></Box></Box>
      </Stack>
      <Box sx={{ mt: 2 }}><Section title="Risk ranking">
        <DataTable data={d.ranking} empty="No open risks." columns={[{ header: 'Risk', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' }, { header: 'Category', accessorKey: 'category' }, { header: 'P', accessorKey: 'p' }, { header: 'S', accessorKey: 's' }, { header: 'Score', accessorKey: 'score' }, { header: 'Band', accessorKey: 'band' }]} />
      </Section></Box>
    </ModuleWorkspace>
  );
}

export function KpiFramework() {
  const q = useQuery({ queryKey: ['kpi-framework'], queryFn: () => get<any>('/api/qos/kpis') });
  if (!q.data) return <Loading m="qos" crumbs={C('KPI framework')} q={q} />;
  const d = q.data; const rc = (r?: string) => (r === 'Critical' ? 'error.main' : r === 'Warning' ? 'warning.main' : r ? 'success.main' : 'divider');
  return (
    <ModuleWorkspace module={mod('qos')} crumbs={C('KPI framework')} title="KPI framework">
      {d.alerts.length > 0 && <Alert severity="warning" sx={{ mb: 2 }}>{d.alerts.length} KPI(s) at warning or critical: {d.alerts.map((a: any) => a.title).join(', ')}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(4, 1fr)' }, gap: 1.5 }}>
        {Object.entries(d.levels).map(([lvl, list]: [string, any]) => <Paper key={lvl} variant="outlined" sx={{ p: 1, bgcolor: 'grey.50' }} data-testid={`kpi-level-${lvl}`}>
          <Typography variant="overline" sx={{ fontWeight: 700 }}>{lvl}</Typography>
          <Stack gap={0.75}>{list.map((o: any) => <Paper key={o.id} sx={{ p: 1, borderLeft: 4, borderColor: rc(o.rating) }}>
            <Typography variant="body2" sx={{ fontWeight: 600 }}>{o.title}</Typography><Typography variant="caption" color="text.secondary">{o.metricName} · target {o.target}{o.unit ? ` ${o.unit}` : ''}</Typography>
            <Typography sx={{ fontFamily: mono }}>{o.latest ?? '—'}</Typography></Paper>)}</Stack></Paper>)}
      </Box>
      <Box sx={{ mt: 2 }}><Section title="Rollups (corporate and regional KPIs from site KPIs with the same metric)">
        {d.rollups.length === 0 ? <Typography variant="body2" color="text.secondary">No corporate or regional KPIs.</Typography> : <Stack gap={1}>{d.rollups.map((r: any) => <Paper key={r.objective.id} variant="outlined" sx={{ p: 1.25 }}>
          <Stack direction="row" gap={2} alignItems="center"><Typography sx={{ fontWeight: 600, flex: 1 }}>{r.objective.title}</Typography><Typography variant="caption">rolled up from {r.children.length} site KPI(s)</Typography><Chip label={r.rollupValue ?? 'no data'} sx={{ fontFamily: mono }} /></Stack></Paper>)}</Stack>}
      </Section></Box>
    </ModuleWorkspace>
  );
}

export function DigitalTwin() {
  const [tab, setTab] = useState('process');
  const charts = useQuery({ queryKey: ['spc-charts'], queryFn: () => get<any[]>('/api/spc') });
  const fmeas = useQuery({ queryKey: ['pfmea-list'], queryFn: () => get<any[]>('/api/apqp/pfmea') });
  const suppliers = useLookup('supplier');
  const [chart, setChart] = useState(''); const [shift, setShift] = useState(0); const [sigma, setSigma] = useState(1);
  const [fmea, setFmea] = useState(''); const [sup, setSup] = useState('');
  const [w, setW] = useState({ audit: 25, quality: 25, delivery: 20, correctiveActions: 15, complaints: 15 });
  const [res, setRes] = useState<any>(null);
  const r = useRunner(() => undefined);
  const Compare = ({ a, b, rows }: { a: any; b: any; rows: [string, string][] }) => <Box component="table" sx={{ fontFamily: mono, '& td, & th': { p: 0.75, borderBottom: 1, borderColor: 'divider' } }}><thead><tr><th /><th>Current</th><th>Scenario</th></tr></thead>
    <tbody>{rows.map(([k, l]) => <tr key={k}><td>{l}</td><td>{String(a?.[k] ?? '—')}</td><td>{String(b?.[k] ?? '—')}</td></tr>)}</tbody></Box>;
  const Side = ({ children }: { children: ReactNode }) => <Paper variant="outlined" sx={{ p: 2, width: { md: 340 } }}><Stack gap={2}>{children}</Stack></Paper>;
  return (
    <ModuleWorkspace module={mod('qos')} crumbs={C('Digital twin')} title="Quality digital twin" tabs={[{ label: 'Process', value: 'process' }, { label: 'Product risk', value: 'product' }, { label: 'Supplier', value: 'supplier' }]} tab={tab} onTab={(t) => { setTab(t); setRes(null); }}>
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      <Stack direction={{ xs: 'column', md: 'row' }} gap={2}>
        {tab === 'process' && <Side>
          <TextField select size="small" label="SPC chart" value={chart} onChange={(e) => setChart(e.target.value)}>{(charts.data ?? []).map((c) => <MenuItem key={c.id} value={c.id}>{c.number} — {c.title}</MenuItem>)}</TextField>
          <Box><Typography variant="caption">Mean shift: {shift}</Typography><Slider size="small" min={-0.1} max={0.1} step={0.005} value={shift} onChange={(_, v) => setShift(v as number)} /></Box>
          <Box><Typography variant="caption">Spread factor: ×{sigma}</Typography><Slider size="small" min={0.5} max={2} step={0.05} value={sigma} onChange={(_, v) => setSigma(v as number)} /></Box>
          <Button variant="contained" disabled={!chart} onClick={() => r.run(async () => setRes(await post('/api/qos/twin/process', { chartId: chart, meanShift: shift, sigmaFactor: sigma })))}>Simulate</Button></Side>}
        {tab === 'product' && <Side>
          <TextField select size="small" label="FMEA" value={fmea} onChange={(e) => setFmea(e.target.value)}>{(fmeas.data ?? []).map((f) => <MenuItem key={f.document.id} value={f.document.id}>{f.document.number} — {f.document.title}</MenuItem>)}</TextField>
          <Button variant="contained" disabled={!fmea} onClick={() => r.run(async () => setRes(await post('/api/qos/twin/product', { pfmeaId: fmea })))}>Simulate actions</Button></Side>}
        {tab === 'supplier' && <Side>
          <TextField select size="small" label="Supplier" value={sup} onChange={(e) => setSup(e.target.value)}>{(suppliers.data ?? []).map((s) => <MenuItem key={s.id} value={s.id}>{lookupLabel(s)}</MenuItem>)}</TextField>
          {Object.entries(w).map(([k, v]) => <Box key={k}><Typography variant="caption">{k} weight: {v}%</Typography><Slider size="small" min={0} max={60} value={v} onChange={(_, x) => setW({ ...w, [k]: x as number })} /></Box>)}
          <Button variant="contained" disabled={!sup} onClick={() => r.run(async () => setRes(await post('/api/qos/twin/supplier', { supplierId: sup, weights: Object.fromEntries(Object.entries(w).map(([k, v]) => [k, v / 100])) })))}>Re-weight</Button></Side>}
        <Box sx={{ flex: 1 }} data-testid="twin-result">
          {!res && <Typography color="text.secondary">Choose inputs and run the simulation. Results are computed from recorded data only.</Typography>}
          {res && tab === 'process' && <Section title={`${res.chart} · ${res.characteristic} (${res.observations} values)`}><Compare a={res.current} b={res.scenario} rows={[['mean', 'Mean'], ['sigma', 'Sigma'], ['ppk', 'Ppk'], ['ppmOutOfSpec', 'PPM out of spec']]} /><Typography variant="caption" color="text.secondary">{res.method}</Typography></Section>}
          {res && tab === 'product' && <Section title={`${res.number} · ${res.type} FMEA (${res.lines} lines)`}><Compare a={res.current} b={res.afterActions} rows={[['totalRpn', 'Total RPN'], ['maxRpn', 'Max RPN'], ['high', 'High priority'], ['medium', 'Medium priority']]} /><Typography variant="caption" color="text.secondary">{res.linesWithoutRevisedRatings} line(s) with actions but no revised ratings. {res.method}</Typography></Section>}
          {res && tab === 'supplier' && <Section title={res.current.name}><Compare a={res.current} b={res.scenario} rows={[['health', 'Health'], ['risk', 'Risk'], ['level', 'Level']]} /><Typography variant="caption" color="text.secondary">{res.method}</Typography></Section>}
        </Box>
      </Stack>
    </ModuleWorkspace>
  );
}

export function BoardReports() {
  const [kind, setKind] = useState('board');
  const q = useQuery({ queryKey: ['board', kind], queryFn: () => get<any>(`/api/qos/board/${kind}`) });
  const d = q.data;
  return (
    <ModuleWorkspace module={mod('qos')} crumbs={C('Board reports')} title="Executive and board reporting" tabs={[{ label: 'Board', value: 'board' }, { label: 'Executive', value: 'executive' }, { label: 'Strategic risk', value: 'strategic-risk' }]} tab={kind} onTab={setKind}
      actions={<Button variant="outlined" onClick={() => window.print()}>Print</Button>}>
      {q.error && <Alert severity="error">{(q.error as Error).message}</Alert>}
      {d && <Paper sx={{ p: 3, maxWidth: 980 }} data-testid="board-report">
        <Typography variant="h5" sx={{ fontWeight: 700 }}>{d.title}</Typography><Typography variant="caption" color="text.secondary">Generated {fmtDateTime(d.generated)}</Typography>
        {kind === 'board' && <Stack gap={2} sx={{ mt: 2 }}>
          <Stack direction="row" gap={3} alignItems="center"><Gauge value={d.corporateScore.score} label="Corporate quality score" />{d.regions.map((r: any) => <Gauge key={r.id} value={r.score} label={r.name} size={90} />)}</Stack>
          <Section title="Top risks"><DataTable data={d.topRisks} empty="None." columns={[{ header: 'Risk', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' }, { header: 'Category', accessorKey: 'category' }, { header: 'Score', accessorKey: 'score' }]} /></Section>
          <Section title="Complaints"><Typography>{d.complaints.received12m} received in 12 months, {d.complaints.open} open, {d.complaints.overdue} overdue.</Typography></Section></Stack>}
        {kind === 'executive' && <Stack gap={1} sx={{ mt: 2 }}><Typography>Corporate score {fmt(d.sections.globalStatus.corporate.score)} across {d.sections.globalStatus.sites.length} site(s).</Typography>
          <DataTable data={d.sections.sitePerformance} empty="No sites." columns={[{ header: 'Site', accessorKey: 'siteCode', cell: num }, { header: 'Open NCRs', accessorKey: 'openNcrs' }, { header: 'Open CAPAs', accessorKey: 'openCapas' }, { header: 'Open complaints', accessorKey: 'openComplaints' }]} /></Stack>}
        {kind === 'strategic-risk' && <Stack gap={1} sx={{ mt: 2 }}>
          <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(5, 1fr)', gap: 1 }}>{d.categories.map((c: any) => <Tile key={c.category} label={c.category} value={c.open} hint={`${c.high} high`} />)}</Box>
          <DataTable data={d.ranking} empty="No open risks." columns={[{ header: 'Risk', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' }, { header: 'Category', accessorKey: 'category' }, { header: 'Band', accessorKey: 'band' }]} /></Stack>}
      </Paper>}
    </ModuleWorkspace>
  );
}
