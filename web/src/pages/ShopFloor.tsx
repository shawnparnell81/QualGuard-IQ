import { useState } from 'react';
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Chip, LinearProgress, MenuItem, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, ToggleButton, ToggleButtonGroup, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { get, post } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, fmtDate, fmtDateTime, localDate } from '../components/Status';
import { Facts, Section, useLookup, lookupLabel, useSites } from '../components/Kit';
import { mono } from '../theme';

const mod = modules.find((m) => m.key === 'shopfloor')!;
export const woStatus = ['Planned', 'Released', 'InProcess', 'Completed', 'Closed', 'Cancelled', 'OnHold'];
export const woKind = ['Production order', 'Manufacturing order', 'Job'];
export const poStatus = ['Open', 'PartiallyReceived', 'Received', 'Closed', 'Cancelled'];
const entryKind = ['Production', 'Downtime', 'Inspection', 'Process parameter'];
const n = (v?: number | null, d = 0) => (v == null ? '—' : Number(v).toLocaleString(undefined, { maximumFractionDigits: d }));
const mnum = (x: { getValue: () => unknown }) => <span style={{ fontFamily: mono }}>{String(x.getValue() ?? '')}</span>;

interface Metric { key: string; label: string; value: number; unit?: string; numerator?: number; denominator?: number; url: string }
interface Related { type: string; id: string; number: string; title: string; status: string; url: string }
interface Live { asOf: string; metrics: Metric[]; openNcrs: Related[]; spcViolations: { id: string; number: string; characteristic: string; takenAt: string; violations: string; ncrId?: string }[]; scrapToday: { id: string; workOrderId?: string; scrapped: number; recordedAt: string; device?: string }[]; refreshSeconds: number }
export interface WorkOrder { id: string; number: string; kind: number; partId?: string; itemNumber: string; revision?: string; quantity: number; quantityCompleted: number; quantityScrapped: number; quantityReworked: number; startOn?: string; dueOn?: string; status: number; source: string; externalRef?: string; routingRef?: string; siteId: string }
interface Operation { id: string; sequence: number; name: string; workCenter?: string; status: number; quantityGood: number; quantityRejected: number; quantityScrapped: number; quantityReworked: number; quantityFirstPassGood: number }
interface Po { id: string; number: string; supplierId: string; orderedOn?: string; status: number; source: string; externalRef?: string; siteId: string }
interface PoLine { id: string; lineNumber: number; partId?: string; itemNumber: string; revision?: string; quantity: number; quantityReceived: number; unitOfMeasure?: string; dueOn?: string }
interface Entry { id: string; kind: number; workOrderId?: string; operationSequence?: number; workCenter?: string; good: number; rejected: number; scrapped: number; reworked: number; firstPass: boolean; downtimeMinutes?: number; downtimeReason?: string; characteristic?: string; value?: number; lowerLimit?: number; upperLimit?: number; inSpec?: boolean; lotNumber?: string; serialNumber?: string; device?: string; source: string; ncrId?: string; recordedAt: string }

/** Large-number gauge strip: each metric shows its fraction so the reader can check the arithmetic. */
function Gauge({ m }: { m: Metric }) {
  const nav = useNavigate();
  const pct = m.unit === '%';
  const bad = (m.key === 'fpy24h' && m.denominator && m.value < 95) || (m.key !== 'fpy24h' && m.value > 0 && ['openNcrs', 'spcViolations', 'scrap24h', 'supplierRejectRate'].includes(m.key));
  return (
    <Paper onClick={() => nav(m.url)} sx={{ p: 1.75, cursor: 'pointer', borderLeft: 4, borderLeftColor: bad ? 'secondary.main' : 'primary.main', '&:hover': { bgcolor: 'action.hover' } }}>
      <Typography variant="caption" color="text.secondary">{m.label}</Typography>
      <Typography sx={{ fontFamily: mono, fontSize: 30, lineHeight: 1.2 }}>{n(m.value, 1)}{m.unit && <Typography component="span" sx={{ fontSize: 14, ml: 0.5 }} color="text.secondary">{m.unit}</Typography>}</Typography>
      {pct && m.denominator != null && <><LinearProgress variant="determinate" value={Math.min(100, m.value)} sx={{ my: 0.75, height: 4 }} /><Typography variant="caption" color="text.secondary" sx={{ fontFamily: mono }}>{n(m.numerator, 1)} / {n(m.denominator, 1)}</Typography></>}
    </Paper>
  );
}

export function LiveMonitor() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [interval, setIntervalSec] = useState(15);
  const q = useQuery({ queryKey: ['live'], queryFn: () => get<Live>('/api/production/live'), refetchInterval: interval * 1000 });
  const d = q.data;
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Live monitor' }]} title="Live quality monitor"
      actions={<Stack direction="row" gap={1.5} alignItems="center">
        <Chip size="small" color={q.isFetching ? 'info' : 'default'} label={d ? `Updated ${new Date(d.asOf).toLocaleTimeString()}` : 'Loading'} />
        <ToggleButtonGroup size="small" exclusive value={interval} onChange={(_, v) => v && setIntervalSec(v)}>
          {[15, 30, 60].map((s) => <ToggleButton key={s} value={s}>{s}s</ToggleButton>)}
        </ToggleButtonGroup>
        {can('production.edit') && <Button variant="contained" component={RouterLink} to="/production/entry">Record entry</Button>}
      </Stack>}>
      {q.error && <Alert severity="error">{(q.error as Error).message}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(190px, 1fr))', gap: 1.5, mb: 2 }}>{(d?.metrics ?? []).map((m) => <Gauge key={m.key} m={m} />)}</Box>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '1.2fr 1fr 0.8fr' }, gap: 2 }}>
        <Section title="Open nonconformances">
          <Table size="small"><TableBody>{(d?.openNcrs ?? []).map((r) => <TableRow key={r.id} hover sx={{ cursor: 'pointer' }} onClick={() => nav(r.url)}>
            <TableCell sx={{ fontFamily: mono }}>{r.number}</TableCell><TableCell>{r.title}</TableCell><TableCell><StatusChip status={r.status} /></TableCell></TableRow>)}
            {d && d.openNcrs.length === 0 && <TableRow><TableCell><Typography color="text.secondary">None open.</Typography></TableCell></TableRow>}</TableBody></Table>
        </Section>
        <Section title="SPC signals (24 h)">
          <Table size="small"><TableBody>{(d?.spcViolations ?? []).map((r, i) => <TableRow key={i} hover sx={{ cursor: 'pointer' }} onClick={() => nav(`/spc/${r.id}`)}>
            <TableCell sx={{ fontFamily: mono }}>{r.number}</TableCell><TableCell>{r.characteristic}<Typography variant="caption" display="block" color="error.main">{r.violations}</Typography></TableCell><TableCell>{new Date(r.takenAt).toLocaleTimeString()}</TableCell></TableRow>)}
            {d && d.spcViolations.length === 0 && <TableRow><TableCell><Typography color="text.secondary">No rule violations.</Typography></TableCell></TableRow>}</TableBody></Table>
        </Section>
        <Section title="Scrap (24 h)">
          <Table size="small"><TableBody>{(d?.scrapToday ?? []).map((r) => <TableRow key={r.id} hover={!!r.workOrderId} sx={{ cursor: r.workOrderId ? 'pointer' : undefined }} onClick={() => r.workOrderId && nav(`/production/work-orders/${r.workOrderId}`)}>
            <TableCell sx={{ fontFamily: mono }}>{n(r.scrapped, 2)}</TableCell><TableCell>{r.device ?? '—'}</TableCell><TableCell>{new Date(r.recordedAt).toLocaleTimeString()}</TableCell></TableRow>)}
            {d && d.scrapToday.length === 0 && <TableRow><TableCell><Typography color="text.secondary">No scrap recorded.</Typography></TableCell></TableRow>}</TableBody></Table>
        </Section>
      </Box>
    </ModuleWorkspace>
  );
}

export function WorkOrderList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [q, setQ] = useState('');
  const rows = useQuery({ queryKey: ['wos', q], queryFn: () => get<WorkOrder[]>(`/api/production/work-orders${q ? `?q=${encodeURIComponent(q)}` : ''}`) });
  const cols: ColumnDef<WorkOrder, any>[] = [
    { header: 'Work order', accessorKey: 'number', cell: mnum }, { header: 'Item', accessorFn: (r) => `${r.itemNumber}${r.revision ? ` rev ${r.revision}` : ''}` },
    { header: 'Progress', accessorFn: (r) => (r.quantity ? r.quantityCompleted / r.quantity : 0), cell: (x) => { const r = x.row.original; return <Stack sx={{ minWidth: 140 }}><LinearProgress variant="determinate" value={Math.min(100, (r.quantityCompleted / (r.quantity || 1)) * 100)} /><Typography variant="caption" sx={{ fontFamily: mono }}>{n(r.quantityCompleted)} / {n(r.quantity)} · scrap {n(r.quantityScrapped)}</Typography></Stack>; } },
    { header: 'Due', accessorKey: 'dueOn', cell: (x) => fmtDate(x.getValue()) }, { header: 'Status', accessorFn: (r) => woStatus[r.status], cell: (x) => <StatusChip status={x.getValue()} /> },
    { header: 'Source', accessorKey: 'source' },
  ];
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Work orders' }]} title="Work orders"
      actions={<Stack direction="row" gap={1}><TextField size="small" placeholder="Number or item" value={q} onChange={(e) => setQ(e.target.value)} />{can('production.edit') && <Button variant="contained" component={RouterLink} to="/production/work-orders/new">New work order</Button>}</Stack>}>
      <DataTable data={rows.data ?? []} columns={cols} onRowClick={(r) => nav(`/production/work-orders/${r.id}`)} empty="No work orders. They arrive from the ERP or MES, or can be added here." />
    </ModuleWorkspace>
  );
}

function SitePicker({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  const sites = useSites();
  const active = (sites.data ?? []).filter((s) => !s.isArchived);
  return <TextField select label="Site" value={value} onChange={(e) => onChange(e.target.value)}>{value && !active.some((s) => s.id === value) && <MenuItem value={value}>{sites.isLoading ? 'Loading…' : 'Archived site'}</MenuItem>}{active.map((s) => <MenuItem key={s.id} value={s.id}>{s.code} — {s.name}</MenuItem>)}</TextField>;
}

export function WorkOrderNew() {
  const nav = useNavigate();
  const parts = useLookup('part');
  const [v, setV] = useState({ number: '', kind: 0, partId: '', revision: '', quantity: '10', startOn: localDate(), dueOn: localDate(7), routingRef: '', siteId: '' });
  const [ops, setOps] = useState([{ sequence: 10, name: 'Machine', workCenter: '' }, { sequence: 20, name: 'Final inspection', workCenter: '' }]);
  const [error, setError] = useState<string | null>(null);
  const set = (k: string) => (e: { target: { value: string } }) => setV({ ...v, [k]: e.target.value });
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Work orders', to: '/production/work-orders' }, { label: 'New' }]} title="New work order">
      <Paper sx={{ p: 3, maxWidth: 860 }}>
        {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
        <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}>
          <TextField label="Number (blank = next)" value={v.number} onChange={set('number')} />
          <TextField select label="Kind" value={v.kind} onChange={set('kind')}>{woKind.map((k, i) => <MenuItem key={k} value={i}>{k}</MenuItem>)}</TextField>
          <TextField select label="Part" value={v.partId} onChange={set('partId')}>{(parts.data ?? []).map((p) => <MenuItem key={p.id} value={p.id}>{lookupLabel(p)}</MenuItem>)}</TextField>
          <TextField label="Revision" value={v.revision} onChange={set('revision')} />
          <TextField label="Quantity" type="number" value={v.quantity} onChange={set('quantity')} />
          <TextField label="Routing reference" value={v.routingRef} onChange={set('routingRef')} />
          <TextField label="Start" type="date" value={v.startOn} onChange={set('startOn')} slotProps={{ inputLabel: { shrink: true } }} />
          <TextField label="Due" type="date" value={v.dueOn} onChange={set('dueOn')} slotProps={{ inputLabel: { shrink: true } }} />
          <SitePicker value={v.siteId} onChange={(s) => setV({ ...v, siteId: s })} />
        </Box>
        <Typography variant="overline" color="text.secondary" sx={{ display: 'block', mt: 3 }}>Operations</Typography>
        {ops.map((o, i) => <Stack key={i} direction="row" gap={1} sx={{ mb: 1 }}>
          <TextField size="small" label="Seq" type="number" value={o.sequence} sx={{ width: 90 }} onChange={(e) => setOps(ops.map((x, j) => (j === i ? { ...x, sequence: Number(e.target.value) } : x)))} />
          <TextField size="small" label="Operation" value={o.name} sx={{ flex: 1 }} onChange={(e) => setOps(ops.map((x, j) => (j === i ? { ...x, name: e.target.value } : x)))} />
          <TextField size="small" label="Work center" value={o.workCenter} onChange={(e) => setOps(ops.map((x, j) => (j === i ? { ...x, workCenter: e.target.value } : x)))} />
          <Button color="error" onClick={() => setOps(ops.filter((_, j) => j !== i))}>Remove</Button>
        </Stack>)}
        <Button size="small" onClick={() => setOps([...ops, { sequence: (ops.length + 1) * 10, name: '', workCenter: '' }])}>Add operation</Button>
        <Stack direction="row" gap={1} sx={{ mt: 3 }}>
          <Button variant="contained" onClick={async () => {
            setError(null);
            try {
              const r = await post<{ id: string }>('/api/production/work-orders', { ...v, number: v.number || null, kind: Number(v.kind), partId: v.partId || null, revision: v.revision || null, quantity: Number(v.quantity), routingRef: v.routingRef || null,
                operations: ops.map((o) => ({ ...o, workCenter: o.workCenter || null })), departmentId: null });
              nav(`/production/work-orders/${r.id}`);
            } catch (e) { setError((e as Error).message); }
          }}>Create work order</Button>
          <Button onClick={() => nav('/production/work-orders')}>Cancel</Button>
        </Stack>
      </Paper>
    </ModuleWorkspace>
  );
}

function RelatedList({ url }: { url: string }) {
  const nav = useNavigate();
  const q = useQuery({ queryKey: ['related', url], queryFn: () => get<Related[]>(url) });
  return <DataTable data={q.data ?? []} onRowClick={(r) => nav(r.url)} empty="No linked quality records."
    columns={[{ header: 'Type', accessorKey: 'type' }, { header: 'Number', accessorKey: 'number', cell: mnum }, { header: 'Title', accessorKey: 'title' }, { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={x.getValue()} /> }]} />;
}

function EntryTable({ rows }: { rows: Entry[] }) {
  const nav = useNavigate();
  return <DataTable data={rows} empty="No entries yet."
    columns={[{ header: 'When', accessorKey: 'recordedAt', cell: (x) => fmtDateTime(x.getValue()) }, { header: 'Kind', accessorFn: (r) => entryKind[r.kind] }, { header: 'Op', accessorKey: 'operationSequence' },
      { header: 'Good / rej / scrap', accessorFn: (r) => r.kind === 0 ? `${n(r.good, 2)} / ${n(r.rejected, 2)} / ${n(r.scrapped, 2)}` : r.kind === 1 ? `${n(r.downtimeMinutes)} min · ${r.downtimeReason ?? ''}` : r.characteristic ? `${r.characteristic} = ${r.value ?? '—'}` : '—' },
      { header: 'In spec', accessorFn: (r) => (r.inSpec == null ? '' : r.inSpec ? 'Yes' : 'No'), cell: (x) => x.getValue() === 'No' ? <Chip size="small" color="error" label="Out" /> : x.getValue() },
      { header: 'Lot / serial', accessorFn: (r) => [r.lotNumber, r.serialNumber].filter(Boolean).join(' · ') }, { header: 'Device', accessorKey: 'device' }, { header: 'Source', accessorKey: 'source' },
      { header: 'NCR', accessorKey: 'ncrId', cell: (x) => x.getValue() ? <Button size="small" onClick={(e) => { e.stopPropagation(); nav(`/ncr/${x.getValue()}`); }}>Open NCR</Button> : '' }]} />;
}

export function WorkOrderDetail() {
  const { id } = useParams();
  const { can } = useAuth();
  const [tab, setTab] = useState('ops');
  const q = useQuery({ queryKey: ['wo', id], queryFn: () => get<{ workOrder: WorkOrder; operations: Operation[] }>(`/api/production/work-orders/${id}`) });
  const entries = useQuery({ queryKey: ['entries', id], queryFn: () => get<Entry[]>(`/api/production/entries?workOrderId=${id}`), enabled: tab === 'entries' });
  const w = q.data?.workOrder;
  if (!w) return <ModuleWorkspace module={mod} crumbs={[{ label: 'Work orders', to: '/production/work-orders' }]} title="Loading…">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Work orders', to: '/production/work-orders' }, { label: w.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{w.number}</span><StatusChip status={woStatus[w.status]} /></Stack>}
      tabs={[{ label: 'Operations', value: 'ops' }, { label: 'Entries', value: 'entries' }, { label: 'Quality records', value: 'related' }, { label: 'Label', value: 'label' }]} tab={tab} onTab={setTab}
      actions={can('production.edit') ? <Button variant="contained" component={RouterLink} to={`/production/entry?wo=${w.id}`}>Record entry</Button> : undefined}>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '280px 1fr' }, gap: 2 }}>
        <Facts items={[['Kind', woKind[w.kind]], ['Item', `${w.itemNumber}${w.revision ? ` rev ${w.revision}` : ''}`], ['Quantity', n(w.quantity)], ['Completed', n(w.quantityCompleted)], ['Scrapped', n(w.quantityScrapped)], ['Reworked', n(w.quantityReworked)],
          ['Start', fmtDate(w.startOn)], ['Due', fmtDate(w.dueOn)], ['Routing', w.routingRef ?? '—'], ['Source', `${w.source}${w.externalRef ? ` · ${w.externalRef}` : ''}`]]} />
        <Box>
          {tab === 'ops' && <Paper variant="outlined"><Table size="small">
            <TableHead><TableRow><TableCell>Seq</TableCell><TableCell>Operation</TableCell><TableCell>Work center</TableCell><TableCell>Good</TableCell><TableCell>First-pass good</TableCell><TableCell>Rejected</TableCell><TableCell>Scrap</TableCell><TableCell>Rework</TableCell><TableCell>FPY</TableCell></TableRow></TableHead>
            <TableBody>{q.data!.operations.map((o) => { const tot = o.quantityGood + o.quantityRejected + o.quantityScrapped; return <TableRow key={o.id}>
              <TableCell sx={{ fontFamily: mono }}>{o.sequence}</TableCell><TableCell>{o.name}</TableCell><TableCell>{o.workCenter ?? '—'}</TableCell>
              {[o.quantityGood, o.quantityFirstPassGood, o.quantityRejected, o.quantityScrapped, o.quantityReworked].map((x, i) => <TableCell key={i} sx={{ fontFamily: mono }}>{n(x, 2)}</TableCell>)}
              <TableCell sx={{ fontFamily: mono }}>{tot ? `${((o.quantityFirstPassGood / tot) * 100).toFixed(1)}%` : '—'}</TableCell></TableRow>; })}</TableBody></Table></Paper>}
          {tab === 'entries' && <EntryTable rows={entries.data ?? []} />}
          {tab === 'related' && <RelatedList url={`/api/production/work-orders/${id}/related`} />}
          {tab === 'label' && <Paper sx={{ p: 2, display: 'inline-block' }}><img alt={`Label ${w.number}`} src={`/api/trace/labels?symbology=Code128&content=${encodeURIComponent('QG:WO:' + w.number)}`} /><Typography sx={{ fontFamily: mono, mt: 1 }}>QG:WO:{w.number}</Typography></Paper>}
        </Box>
      </Box>
    </ModuleWorkspace>
  );
}

export function PurchaseOrderList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [q, setQ] = useState('');
  const rows = useQuery({ queryKey: ['pos', q], queryFn: () => get<Po[]>(`/api/production/purchase-orders${q ? `?q=${encodeURIComponent(q)}` : ''}`) });
  const sups = useLookup('supplier');
  const sup = (id: string) => sups.data?.find((s) => s.id === id);
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Purchase orders' }]} title="Purchase orders"
      actions={<Stack direction="row" gap={1}><TextField size="small" placeholder="PO number" value={q} onChange={(e) => setQ(e.target.value)} />{can('production.edit') && <Button variant="contained" component={RouterLink} to="/production/purchase-orders/new">New purchase order</Button>}</Stack>}>
      <DataTable data={rows.data ?? []} onRowClick={(r) => nav(`/production/purchase-orders/${r.id}`)} empty="No purchase orders yet."
        columns={[{ header: 'PO', accessorKey: 'number', cell: mnum }, { header: 'Supplier', accessorFn: (r) => (sup(r.supplierId) ? lookupLabel(sup(r.supplierId)!) : '—') }, { header: 'Ordered', accessorKey: 'orderedOn', cell: (x) => fmtDate(x.getValue()) },
          { header: 'Status', accessorFn: (r) => poStatus[r.status], cell: (x) => <StatusChip status={x.getValue()} /> }, { header: 'Source', accessorKey: 'source' }]} />
    </ModuleWorkspace>
  );
}

export function PurchaseOrderNew() {
  const nav = useNavigate();
  const sups = useLookup('supplier');
  const parts = useLookup('part');
  const [v, setV] = useState({ number: '', supplierId: '', orderedOn: localDate(), siteId: '' });
  const [lines, setLines] = useState([{ lineNumber: 1, partId: '', itemNumber: '', revision: '', quantity: '100', dueOn: localDate(14) }]);
  const [error, setError] = useState<string | null>(null);
  const setLine = (i: number, k: string, x: string) => setLines(lines.map((l, j) => {
    if (j !== i) return l;
    const nl = { ...l, [k]: x };
    if (k === 'partId') { const p = parts.data?.find((pp) => pp.id === x); if (p) { nl.itemNumber = p.partNumber ?? p.number ?? ''; nl.revision = p.revision ?? ''; } }
    return nl;
  }));
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Purchase orders', to: '/production/purchase-orders' }, { label: 'New' }]} title="New purchase order">
      <Paper sx={{ p: 3, maxWidth: 980 }}>
        {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
        <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}>
          <TextField label="PO number" value={v.number} onChange={(e) => setV({ ...v, number: e.target.value })} />
          <TextField select label="Supplier" value={v.supplierId} onChange={(e) => setV({ ...v, supplierId: e.target.value })}>{(sups.data ?? []).map((s) => <MenuItem key={s.id} value={s.id}>{lookupLabel(s)}</MenuItem>)}</TextField>
          <TextField label="Ordered" type="date" value={v.orderedOn} onChange={(e) => setV({ ...v, orderedOn: e.target.value })} slotProps={{ inputLabel: { shrink: true } }} />
          <SitePicker value={v.siteId} onChange={(s) => setV({ ...v, siteId: s })} />
        </Box>
        <Typography variant="overline" color="text.secondary" sx={{ display: 'block', mt: 3 }}>Lines</Typography>
        {lines.map((l, i) => <Stack key={i} direction="row" gap={1} sx={{ mb: 1 }}>
          <TextField size="small" label="Line" type="number" value={l.lineNumber} sx={{ width: 80 }} onChange={(e) => setLine(i, 'lineNumber', e.target.value)} />
          <TextField size="small" select label="Part" value={l.partId} sx={{ minWidth: 220 }} onChange={(e) => setLine(i, 'partId', e.target.value)}><MenuItem value="">Not in master data</MenuItem>{(parts.data ?? []).map((p) => <MenuItem key={p.id} value={p.id}>{lookupLabel(p)}</MenuItem>)}</TextField>
          <TextField size="small" label="Item" value={l.itemNumber} onChange={(e) => setLine(i, 'itemNumber', e.target.value)} />
          <TextField size="small" label="Rev" value={l.revision} sx={{ width: 80 }} onChange={(e) => setLine(i, 'revision', e.target.value)} />
          <TextField size="small" label="Qty" type="number" value={l.quantity} sx={{ width: 100 }} onChange={(e) => setLine(i, 'quantity', e.target.value)} />
          <TextField size="small" label="Due" type="date" value={l.dueOn} onChange={(e) => setLine(i, 'dueOn', e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
          <Button color="error" onClick={() => setLines(lines.filter((_, j) => j !== i))}>Remove</Button>
        </Stack>)}
        <Button size="small" onClick={() => setLines([...lines, { lineNumber: lines.length + 1, partId: '', itemNumber: '', revision: '', quantity: '1', dueOn: localDate(14) }])}>Add line</Button>
        <Stack direction="row" gap={1} sx={{ mt: 3 }}>
          <Button variant="contained" onClick={async () => {
            setError(null);
            try {
              const r = await post<{ id: string }>('/api/production/purchase-orders', { ...v, departmentId: null, lines: lines.map((l) => ({ lineNumber: Number(l.lineNumber), partId: l.partId || null, itemNumber: l.itemNumber, revision: l.revision || null, quantity: Number(l.quantity), dueOn: l.dueOn || null })) });
              nav(`/production/purchase-orders/${r.id}`);
            } catch (e) { setError((e as Error).message); }
          }}>Create purchase order</Button>
          <Button onClick={() => nav('/production/purchase-orders')}>Cancel</Button>
        </Stack>
      </Paper>
    </ModuleWorkspace>
  );
}

export function PurchaseOrderDetail() {
  const { id } = useParams();
  const [tab, setTab] = useState('lines');
  const q = useQuery({ queryKey: ['po', id], queryFn: () => get<{ purchaseOrder: Po; lines: PoLine[] }>(`/api/production/purchase-orders/${id}`) });
  const sups = useLookup('supplier');
  const p = q.data?.purchaseOrder;
  if (!p) return <ModuleWorkspace module={mod} crumbs={[{ label: 'Purchase orders', to: '/production/purchase-orders' }]} title="Loading…">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const s = sups.data?.find((x) => x.id === p.supplierId);
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Purchase orders', to: '/production/purchase-orders' }, { label: p.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{p.number}</span><StatusChip status={poStatus[p.status]} /></Stack>}
      tabs={[{ label: 'Lines', value: 'lines' }, { label: 'Receiving quality', value: 'related' }]} tab={tab} onTab={setTab}>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '280px 1fr' }, gap: 2 }}>
        <Facts items={[['Supplier', s ? lookupLabel(s) : '—'], ['Ordered', fmtDate(p.orderedOn)], ['Source', `${p.source}${p.externalRef ? ` · ${p.externalRef}` : ''}`]]} />
        <Box>
          {tab === 'lines' && <DataTable data={q.data!.lines} columns={[{ header: 'Line', accessorKey: 'lineNumber' }, { header: 'Item', accessorFn: (r) => `${r.itemNumber}${r.revision ? ` rev ${r.revision}` : ''}` },
            { header: 'Ordered', accessorKey: 'quantity', cell: mnum }, { header: 'Received', accessorKey: 'quantityReceived', cell: mnum }, { header: 'Due', accessorKey: 'dueOn', cell: (x) => fmtDate(x.getValue()) }]} />}
          {tab === 'related' && <RelatedList url={`/api/production/purchase-orders/${id}/related`} />}
        </Box>
      </Box>
    </ModuleWorkspace>
  );
}

export function EntryLog() {
  const q = useQuery({ queryKey: ['entries', 'all'], queryFn: () => get<Entry[]>('/api/production/entries') });
  return <ModuleWorkspace module={mod} crumbs={[{ label: 'Entry log' }]} title="Shop-floor entry log"><EntryTable rows={q.data ?? []} /></ModuleWorkspace>;
}

/**
 * Shop-floor entry built for a tablet or phone at the machine: big touch targets, one column on small screens,
 * and a scan box that accepts a keyboard-wedge scanner (QG:WO:…, QG:LOT:…, QG:SN:…).
 */
export function ShopFloorEntry() {
  const qc = useQueryClient();
  const [p] = useSearchParams();
  const wos = useQuery({ queryKey: ['wos', ''], queryFn: () => get<WorkOrder[]>('/api/production/work-orders') });
  const [kind, setKind] = useState(0);
  const [woId, setWoId] = useState(p.get('wo') ?? '');
  const wo = useQuery({ queryKey: ['wo', woId], queryFn: () => get<{ workOrder: WorkOrder; operations: Operation[] }>(`/api/production/work-orders/${woId}`), enabled: !!woId });
  const [v, setV] = useState<Record<string, string>>({ op: '', good: '', rejected: '0', scrapped: '0', reworked: '0', minutes: '', reason: '', characteristic: '', value: '', lsl: '', usl: '', lot: '', serial: '', spcChartId: '' });
  const [firstPass, setFirstPass] = useState(true);
  const [scan, setScan] = useState('');
  const [msg, setMsg] = useState<{ ok: boolean; text: string; ncr?: string } | null>(null);
  const charts = useQuery({ queryKey: ['spc-charts'], queryFn: () => get<{ id: string; number: string; characteristic: string }[]>('/api/spc').catch(() => []), enabled: kind === 2 });
  const set = (k: string) => (e: { target: { value: string } }) => setV({ ...v, [k]: e.target.value });
  const big = { '& .MuiInputBase-root': { fontSize: 20, minHeight: 56 } };
  const onScan = async () => {
    if (!scan.trim()) return;
    try {
      const r = await post<{ kind: string; id?: string; number?: string; detail?: string }>('/api/trace/scan', { code: scan.trim() });
      if (r.kind === 'WorkOrder' && r.id) setWoId(r.id);
      else if (r.kind === 'Lot' && r.number) setV({ ...v, lot: r.number });
      else if (r.kind === 'Serial' && r.number) setV({ ...v, serial: r.number });
      setMsg({ ok: r.kind !== 'NotFound' && r.kind !== 'Ambiguous', text: r.kind === 'NotFound' ? `Code not recognised: ${scan}` : `${r.kind} ${r.number ?? ''} ${r.detail ?? ''}` });
    } catch (e) { setMsg({ ok: false, text: (e as Error).message }); }
    setScan('');
  };
  const submit = async () => {
    setMsg(null);
    const num = (x: string) => (x === '' ? null : Number(x));
    try {
      const r = await post<{ spcViolations: string[]; ncrId?: string; ncrNumber?: string }>('/api/production/entries', {
        kind, workOrderId: woId || null, operationSequence: num(v.op), workCenter: null, good: num(v.good) ?? 0, rejected: num(v.rejected) ?? 0, scrapped: num(v.scrapped) ?? 0, reworked: num(v.reworked) ?? 0, firstPass,
        downtimeMinutes: num(v.minutes), downtimeReason: v.reason || null, characteristic: v.characteristic || null, value: num(v.value), lowerLimit: num(v.lsl), upperLimit: num(v.usl), parameters: null,
        lotNumber: v.lot || null, serialNumber: v.serial || null, device: window.innerWidth < 700 ? 'Phone' : window.innerWidth < 1100 ? 'Tablet' : 'Desktop', spcChartId: v.spcChartId || null, recordedAt: null, siteId: null });
      setMsg({ ok: r.spcViolations.length === 0, text: r.spcViolations.length ? `Recorded. SPC: ${r.spcViolations.join('; ')}` : 'Recorded.', ncr: r.ncrId ? `${r.ncrNumber}|${r.ncrId}` : undefined });
      setV({ ...v, good: '', rejected: '0', scrapped: '0', reworked: '0', minutes: '', reason: '', value: '' });
      qc.invalidateQueries({ queryKey: ['wo', woId] });
    } catch (e) { setMsg({ ok: false, text: (e as Error).message }); }
  };
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Record entry' }]} title="Record a shop-floor entry">
      <Paper sx={{ p: { xs: 2, sm: 3 }, maxWidth: 720 }}>
        <TextField fullWidth autoFocus label="Scan (badge, work order, lot or serial)" value={scan} onChange={(e) => setScan(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && onScan()} sx={{ ...big, mb: 2 }} inputProps={{ 'aria-label': 'scan' }} />
        <ToggleButtonGroup exclusive fullWidth value={kind} onChange={(_, k) => k != null && setKind(k)} sx={{ mb: 2, flexWrap: 'wrap', '& .MuiToggleButton-root': { py: 1.5, fontSize: 15 } }}>
          {entryKind.map((k, i) => <ToggleButton key={k} value={i}>{k}</ToggleButton>)}
        </ToggleButtonGroup>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 2 }}>
          <TextField select label="Work order" value={woId} onChange={(e) => setWoId(e.target.value)} sx={big}><MenuItem value="">None</MenuItem>{woId && !(wos.data ?? []).some((w) => w.id === woId && (w.status < 3 || w.status === 6)) && <MenuItem value={woId}>{wo.data?.workOrder.number ?? (wos.isLoading ? 'Loading…' : 'Work order not open for entries')}</MenuItem>}{(wos.data ?? []).filter((w) => w.status < 3 || w.status === 6).map((w) => <MenuItem key={w.id} value={w.id}>{w.number} · {w.itemNumber}</MenuItem>)}</TextField>
          <TextField select label="Operation" value={v.op} onChange={set('op')} sx={big} disabled={!wo.data}><MenuItem value="">—</MenuItem>{(wo.data?.operations ?? []).map((o) => <MenuItem key={o.id} value={String(o.sequence)}>{o.sequence} · {o.name}</MenuItem>)}</TextField>
          {kind === 0 && <>
            <TextField label="Good" type="number" value={v.good} onChange={set('good')} sx={big} />
            <TextField label="Rejected" type="number" value={v.rejected} onChange={set('rejected')} sx={big} />
            <TextField label="Scrapped" type="number" value={v.scrapped} onChange={set('scrapped')} sx={big} />
            <TextField label="Reworked" type="number" value={v.reworked} onChange={set('reworked')} sx={big} />
            <ToggleButtonGroup exclusive value={firstPass} onChange={(_, x) => x != null && setFirstPass(x)} sx={{ gridColumn: '1 / -1' }}>
              <ToggleButton value={true} sx={{ py: 1.5 }}>First pass</ToggleButton><ToggleButton value={false} sx={{ py: 1.5 }}>After rework</ToggleButton>
            </ToggleButtonGroup>
          </>}
          {kind === 1 && <>
            <TextField label="Minutes down" type="number" value={v.minutes} onChange={set('minutes')} sx={big} />
            <TextField label="Reason" value={v.reason} onChange={set('reason')} sx={big} />
          </>}
          {(kind === 2 || kind === 3) && <>
            <TextField label={kind === 2 ? 'Characteristic' : 'Parameter'} value={v.characteristic} onChange={set('characteristic')} sx={big} />
            <TextField label="Measured value" type="number" value={v.value} onChange={set('value')} sx={big} />
            <TextField label="Lower limit" type="number" value={v.lsl} onChange={set('lsl')} sx={big} />
            <TextField label="Upper limit" type="number" value={v.usl} onChange={set('usl')} sx={big} />
            {kind === 2 && <TextField select label="SPC chart (adds a sample)" value={v.spcChartId} onChange={set('spcChartId')} sx={{ ...big, gridColumn: '1 / -1' }}><MenuItem value="">None</MenuItem>{(charts.data ?? []).map((c) => <MenuItem key={c.id} value={c.id}>{c.number} · {c.characteristic}</MenuItem>)}</TextField>}
          </>}
          <TextField label="Lot" value={v.lot} onChange={set('lot')} sx={big} />
          <TextField label="Serial" value={v.serial} onChange={set('serial')} sx={big} />
        </Box>
        {msg && <Alert severity={msg.ok ? 'success' : 'warning'} sx={{ mt: 2 }} action={msg.ncr ? <Button component={RouterLink} to={`/ncr/${msg.ncr.split('|')[1]}`}>Open {msg.ncr.split('|')[0]}</Button> : undefined}>{msg.text}</Alert>}
        <Button fullWidth variant="contained" size="large" onClick={submit} sx={{ mt: 3, py: 1.75, fontSize: 18 }}>Record</Button>
      </Paper>
    </ModuleWorkspace>
  );
}
