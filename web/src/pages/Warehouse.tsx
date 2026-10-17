import { useMemo, useState } from 'react';
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Chip, MenuItem, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { get, post, put } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, fmtDate, fmtDateTime, localDate } from '../components/Status';
import { Facts, Section, StepDialog, lookupLabel, useLookup, useSites } from '../components/Kit';
import { AttachmentsAndHistory } from '../components/RecordTabs';
import { mono } from '../theme';
import {
  FillBar, LocationSelect, MetricTile, StockBadge, categories, countFreq, countStatus, holdKinds, holdStatus, lineStatus, locKinds, mrbDisp, mrbStatus, purposes, receiptKinds, receiptStatus,
  rtvStatus, stockStatusLabel, transferKinds, transferStatus, txKinds, useCarriers, useLocations, type LocationRow, type StockRow, type TxRow,
} from '../warehouse/common';

const mod = modules.find((m) => m.key === 'warehouse')!;
const mnum = (x: { getValue: () => unknown }) => <span style={{ fontFamily: mono }}>{String(x.getValue() ?? '')}</span>;
export const qty = (n?: number | null) => (n == null ? '—' : Number(n).toLocaleString(undefined, { maximumFractionDigits: 4 }));
const nullify = (o: Record<string, string>) => Object.fromEntries(Object.entries(o).map(([k, v]) => [k, v === '' ? null : v]));

export function SitePicker({ value, onChange, label = 'Site', allowAll }: { value: string; onChange: (v: string) => void; label?: string; allowAll?: boolean }) {
  const sites = useSites();
  return <TextField select size="small" label={label} value={value} onChange={(e) => onChange(e.target.value)} sx={{ minWidth: 200 }}>
    {allowAll && <MenuItem value="">All sites</MenuItem>}
    {(sites.data ?? []).filter((s) => !s.isArchived).map((s) => <MenuItem key={s.id} value={s.id}>{s.code} — {s.name}</MenuItem>)}</TextField>;
}

export function MirrorBanner({ siteId }: { siteId?: string }) {
  const m = useQuery({ queryKey: ['wh-mode', siteId], queryFn: () => get<{ mirror: boolean; connection?: string }>(`/api/warehouse/sites/${siteId}/mode`), enabled: !!siteId });
  if (!m.data?.mirror) return null;
  return <Alert severity="info" sx={{ mb: 2 }}>Inventory at this site is a read-only mirror of <b>{m.data.connection}</b>. Movements are posted in the ERP and arrive here with the next sync.</Alert>;
}

// ------------------------------------------------------------------ overview dashboard
interface Tile { key: string; label: string; value?: number | null; unit: string; numerator?: number | null; denominator?: number | null; url: string; note?: string }
export interface Dash { tiles: Tile[]; utilization: { warehouseId: string; code: string; name: string; locations: number; occupied: number; onHand: number; capacity?: number; utilization?: number }[];
  carriers: { carrierId: string; name: string; shipments: number; delivered: number; onTime: number; withPromise: number; onTimePct?: number }[]; inboundByDay: Record<string, number>; outboundByDay: Record<string, number> }

export function FlowChart({ d }: { d?: Dash }) {
  const flow = useMemo(() => {
    const days = Array.from(new Set([...Object.keys(d?.inboundByDay ?? {}), ...Object.keys(d?.outboundByDay ?? {})])).sort();
    return days.map((k) => ({ day: k.slice(5), inbound: d?.inboundByDay[k] ?? 0, outbound: d?.outboundByDay[k] ?? 0 }));
  }, [d]);
  if (flow.length === 0) return <Typography color="text.secondary">No receipts or shipments in the last 30 days.</Typography>;
  return <Box sx={{ height: 240 }}><ResponsiveContainer><BarChart data={flow}><CartesianGrid strokeDasharray="3 3" stroke="#E6E1D6" /><XAxis dataKey="day" fontSize={11} /><YAxis fontSize={11} /><Tooltip /><Legend />
    <Bar dataKey="inbound" name="Inbound" fill="#2E6B5E" /><Bar dataKey="outbound" name="Outbound" fill="#B4632A" /></BarChart></ResponsiveContainer></Box>;
}

export function WarehouseOverview() {
  const nav = useNavigate();
  const [site, setSite] = useState('');
  const d = useQuery({ queryKey: ['wh-dash', site], queryFn: () => get<Dash>(`/api/warehouse/dashboard${site ? `?siteId=${site}` : ''}`) });
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Overview' }]} title="Warehouse overview" actions={<SitePicker value={site} onChange={setSite} allowAll />}>
      {d.error && <Alert severity="error">{(d.error as Error).message}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(5, 1fr)' }, gap: 1.5, mb: 2 }}>
        {(d.data?.tiles ?? []).map((t) => <MetricTile key={t.key} label={t.label} value={t.value} unit={t.unit} num={t.numerator} den={t.denominator} note={t.note} onClick={() => nav(t.url)} />)}
      </Box>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '1.2fr 1fr' }, gap: 2 }}>
        <Section title="Warehouse utilization">
          <Stack gap={1.5}>{(d.data?.utilization ?? []).map((u) => (
            <Box key={u.warehouseId} onClick={() => nav(`/warehouse/locations/${u.warehouseId}`)} sx={{ cursor: 'pointer', p: 1, borderRadius: 1, '&:hover': { bgcolor: '#F3F0E8' } }}>
              <Stack direction="row" justifyContent="space-between"><Typography sx={{ fontFamily: mono, fontWeight: 600 }}>{u.code}</Typography>
                <Typography variant="caption" color="text.secondary">{u.occupied}/{u.locations} locations occupied · {qty(u.onHand)} units{u.capacity ? ` of ${qty(u.capacity)}` : ''}</Typography></Stack>
              <FillBar pct={u.utilization} />
            </Box>))}
            {d.data && d.data.utilization.length === 0 && <Typography color="text.secondary">No warehouses yet. <RouterLink to="/warehouse/locations">Set up locations</RouterLink>.</Typography>}
          </Stack>
        </Section>
        <Section title="Material flow (30 days)"><FlowChart d={d.data} /></Section>
      </Box>
    </ModuleWorkspace>
  );
}

// ------------------------------------------------------------------ stock
export function StockPage() {
  const [p, setP] = useSearchParams();
  const { can } = useAuth();
  const qc = useQueryClient();
  const [q, setQ] = useState('');
  const status = p.get('status') ?? '';
  const view = p.get('view') ?? 'balances';
  const [site, setSite] = useState('');
  const rows = useQuery({ queryKey: ['wh-stock', site, status, q], queryFn: () => get<StockRow[]>(`/api/warehouse/stock?${new URLSearchParams({ ...(site ? { siteId: site } : {}), ...(status ? { status } : {}), ...(q ? { q } : {}) })}`), enabled: view === 'balances' });
  const summary = useQuery({ queryKey: ['wh-summary', site, q], queryFn: () => get<{ partId: string; partNumber: string; partName?: string; unitOfMeasure: string; onHand: number; available: number; reserved: number; allocated: number; awaitingInspection: number; quarantined: number; mrb: number; damaged: number; inTransit: number; wip: number }[]>(`/api/warehouse/summary?${new URLSearchParams({ ...(site ? { siteId: site } : {}), ...(q ? { q } : {}) })}`), enabled: view === 'parts' });
  const [act, setAct] = useState<{ kind: 'move' | 'hold' | 'scrap' | 'split' | 'adjust' | 'mrb'; row: StockRow } | null>(null);
  const nav = useNavigate();
  const refresh = () => { qc.invalidateQueries({ queryKey: ['wh-stock'] }); qc.invalidateQueries({ queryKey: ['wh-summary'] }); };
  const cols: ColumnDef<StockRow, any>[] = [
    { header: 'Part', accessorKey: 'partNumber', cell: mnum }, { header: 'Rev', accessorKey: 'revision' }, { header: 'Lot', accessorKey: 'lotNumber', cell: mnum }, { header: 'Serial', accessorKey: 'serialNumber', cell: mnum },
    { header: 'Location', accessorKey: 'locationCode', cell: mnum }, { header: 'Status', accessorKey: 'status', cell: (x) => <StockBadge status={x.getValue()} held={x.row.original.held} /> },
    { header: 'Type', accessorFn: (r) => categories[r.category] }, { header: 'On hand', accessorKey: 'onHand', cell: (x) => qty(x.getValue()) }, { header: 'Available', accessorKey: 'available', cell: (x) => qty(x.getValue()) },
    { header: 'Reserved', accessorFn: (r) => r.reserved + r.allocated, cell: (x) => qty(x.getValue()) }, { header: 'Work order', accessorKey: 'workOrderNumber' },
    { id: 'act', header: '', cell: (x) => can('inventory.edit') && <Stack direction="row" gap={0.25}>
      {(['move', 'hold', 'split', 'scrap'] as const).filter((k) => k !== 'split' || x.row.original.lotId).map((k) => <Button key={k} size="small" onClick={(e) => { e.stopPropagation(); setAct({ kind: k, row: x.row.original }); }}>{k[0].toUpperCase() + k.slice(1)}</Button>)}
      {(x.row.original.status !== 0 || x.row.original.held) && <Button size="small" onClick={() => nav(`/warehouse/mrb/new?balance=${x.row.original.id}`)}>MRB</Button>}
      {can('inventory.approve') && <Button size="small" onClick={() => setAct({ kind: 'adjust', row: x.row.original })}>Adjust</Button>}</Stack> },
  ];
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Stock' }]} title="Stock on hand"
      tabs={[{ label: 'By location & lot', value: 'balances' }, { label: 'By part', value: 'parts' }]} tab={view} onTab={(v) => setP(v === 'balances' ? {} : { view: v })}
      actions={<Stack direction="row" gap={1} flexWrap="wrap">
        <SitePicker value={site} onChange={setSite} allowAll />
        {view === 'balances' && <TextField select size="small" label="Status" value={status} onChange={(e) => setP(e.target.value ? { status: e.target.value } : {})} sx={{ minWidth: 170 }}>
          <MenuItem value="">Any status</MenuItem>{stockStatusLabel.map((s, i) => <MenuItem key={s} value={String(i)}>{s}</MenuItem>)}</TextField>}
        <TextField size="small" placeholder="Part, lot, serial or location" value={q} onChange={(e) => setQ(e.target.value)} /></Stack>}>
      <MirrorBanner siteId={site || undefined} />
      {view === 'balances' && <DataTable data={rows.data ?? []} columns={cols} empty="No stock matches." />}
      {view === 'parts' && <DataTable data={summary.data ?? []} empty="No stock."
        columns={[{ header: 'Part', accessorKey: 'partNumber', cell: mnum }, { header: 'Name', accessorKey: 'partName' }, { header: 'On hand', accessorKey: 'onHand', cell: (x) => qty(x.getValue()) },
          { header: 'Available', accessorKey: 'available', cell: (x) => qty(x.getValue()) }, { header: 'Reserved', accessorKey: 'reserved', cell: (x) => qty(x.getValue()) }, { header: 'Allocated', accessorKey: 'allocated', cell: (x) => qty(x.getValue()) },
          { header: 'Awaiting insp.', accessorKey: 'awaitingInspection', cell: (x) => qty(x.getValue()) }, { header: 'Quarantine / held', accessorKey: 'quarantined', cell: (x) => qty(x.getValue()) },
          { header: 'MRB', accessorKey: 'mrb', cell: (x) => qty(x.getValue()) }, { header: 'Damaged', accessorKey: 'damaged', cell: (x) => qty(x.getValue()) }, { header: 'In transit', accessorKey: 'inTransit', cell: (x) => qty(x.getValue()) }, { header: 'WIP', accessorKey: 'wip', cell: (x) => qty(x.getValue()) }]} />}
      {act && act.kind !== 'mrb' && <StockAction kind={act.kind} row={act.row} onClose={() => setAct(null)} onDone={refresh} />}
    </ModuleWorkspace>
  );
}

function StockAction({ kind, row: r, onClose, onDone }: { kind: 'move' | 'hold' | 'scrap' | 'split' | 'adjust'; row: StockRow; onClose: () => void; onDone: () => void }) {
  const locs = useLocations(r.siteId);
  const opts = (locs.data ?? []).filter((l) => l.active && l.id !== r.locationId).map((l) => [l.id, `${l.path} · ${purposes[l.purpose]}`] as [string, string]);
  const what = `${r.partNumber}${r.lotNumber ? ` lot ${r.lotNumber}` : ''}${r.serialNumber ? ` SN ${r.serialNumber}` : ''} at ${r.locationCode}`;
  const base = { partId: r.partId, lotId: r.lotId ?? null, serialId: r.serialId ?? null };
  const done = async (fn: () => Promise<unknown>) => { await fn(); onDone(); };
  if (kind === 'move') return <StepDialog open title="Move stock" intro={what} confirm="Move" onClose={onClose}
    inputs={[{ name: 'to', label: 'To location', type: 'select', options: opts }, { name: 'quantity', label: 'Quantity', initial: String(r.serialId ? 1 : r.available || r.onHand) }, { name: 'reason', label: 'Reason' }]}
    onConfirm={(v) => done(() => post('/api/warehouse/moves', { ...base, fromLocationId: r.locationId, toLocationId: v.to, quantity: Number(v.quantity), status: r.status, reason: v.reason || null, clientRequestId: null }))} />;
  if (kind === 'hold') return <StepDialog open title="Place on hold" intro={`${what}. Held material cannot be issued, picked or shipped until the release is approved.`} confirm="Place hold" danger onClose={onClose}
    inputs={[{ name: 'kind', label: 'Hold type', type: 'select', initial: '0', options: holdKinds.map((k, i) => [i, k]) },
      { name: 'scope', label: 'Hold applies to', type: 'select', initial: r.lotId ? 'lot' : r.serialId ? 'serial' : 'location', options: [...(r.lotId ? [['lot', 'This lot (everywhere)']] : []), ...(r.serialId ? [['serial', 'This serial']] : []), ['location', 'Everything at this location'], ['part', 'The whole part']] as [string, string][] },
      { name: 'reason', label: 'Reason', type: 'multiline' }]}
    onConfirm={(v) => done(() => post('/api/warehouse/holds', { kind: Number(v.kind), reason: v.reason, partId: v.scope === 'location' ? null : r.partId, lotId: v.scope === 'lot' ? r.lotId : null, serialId: v.scope === 'serial' ? r.serialId : null,
      locationId: v.scope === 'location' ? r.locationId : null, ncrId: null, customerId: null, supplierId: null, siteId: r.siteId, departmentId: null }))} />;
  if (kind === 'scrap') return <StepDialog open title="Scrap" intro={what} confirm="Scrap" danger onClose={onClose}
    inputs={[{ name: 'quantity', label: 'Quantity', initial: String(r.serialId ? 1 : r.onHand) }, { name: 'reason', label: 'Reason', type: 'multiline' }]}
    onConfirm={(v) => done(() => post('/api/warehouse/scrap', { ...base, locationId: r.locationId, status: r.status, quantity: Number(v.quantity), reason: v.reason, ncrId: null, clientRequestId: null }))} />;
  if (kind === 'adjust') return <StepDialog open title="Inventory adjustment" intro={`${what}. On hand ${qty(r.onHand)}. Enter a positive or negative change.`} confirm="Post adjustment" onClose={onClose}
    inputs={[{ name: 'change', label: 'Quantity change', initial: '-1' }, { name: 'reason', label: 'Reason', type: 'multiline' }]}
    onConfirm={(v) => done(() => post('/api/warehouse/adjustments', { ...base, locationId: r.locationId, status: r.status, quantityChange: Number(v.change), reason: v.reason, clientRequestId: null }))} />;
  return <StepDialog open title="Split lot" intro={`Split part of lot ${r.lotNumber ?? '—'} (${qty(r.onHand)} at ${r.locationCode}) into a new lot. Holds on the lot follow the new lot.`} confirm="Split" onClose={onClose}
    inputs={[{ name: 'quantity', label: 'Quantity for the new lot' }, { name: 'number', label: 'New lot number (blank = next)' }, { name: 'to', label: 'Put the new lot at', type: 'select', initial: '', options: [['', `Same location (${r.locationCode})`], ...opts] }, { name: 'reason', label: 'Reason' }]}
    onConfirm={(v) => done(() => post('/api/warehouse/lots/split', { lotId: r.lotId, locationId: r.locationId, status: r.status, quantity: Number(v.quantity), newLotNumber: v.number || null, toLocationId: v.to || null, reason: v.reason || null, clientRequestId: null }))} />;
}

export function LedgerPage() {
  const [p] = useSearchParams();
  const [kind, setKind] = useState(p.get('kind') ?? '');
  const [site, setSite] = useState('');
  const rows = useQuery({ queryKey: ['wh-ledger', kind, site], queryFn: () => get<TxRow[]>(`/api/warehouse/ledger?${new URLSearchParams({ ...(kind ? { kind } : {}), ...(site ? { siteId: site } : {}) })}`) });
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Transaction history' }]} title="Inventory transaction history"
      actions={<Stack direction="row" gap={1}><SitePicker value={site} onChange={setSite} allowAll />
        <TextField select size="small" label="Type" value={kind} onChange={(e) => setKind(e.target.value)} sx={{ minWidth: 180 }}><MenuItem value="">All</MenuItem>{txKinds.map((k, i) => <MenuItem key={k} value={String(i)}>{k}</MenuItem>)}</TextField></Stack>}>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>Entries are permanent. A mistake is corrected with a new entry, never by editing one.</Typography>
      <DataTable data={rows.data ?? []} empty="No transactions."
        columns={[{ header: 'Entry', accessorKey: 'number', cell: mnum }, { header: 'When', accessorKey: 'occurredAt', cell: (x) => fmtDateTime(x.getValue()) }, { header: 'Type', accessorFn: (r) => txKinds[r.kind] },
          { header: 'Part', accessorKey: 'partNumber', cell: mnum }, { header: 'Lot / serial', accessorFn: (r) => r.serialNumber ?? r.lotNumber ?? '', cell: mnum },
          { header: 'From', accessorFn: (r) => r.fromLocation ? `${r.fromLocation}${r.fromStatus != null && r.fromStatus !== 0 ? ` (${stockStatusLabel[r.fromStatus]})` : ''}` : '', cell: mnum },
          { header: 'To', accessorFn: (r) => r.toLocation ? `${r.toLocation}${r.toStatus != null && r.toStatus !== 0 ? ` (${stockStatusLabel[r.toStatus]})` : ''}` : '', cell: mnum },
          { header: 'Qty', accessorFn: (r) => `${qty(r.quantity)} ${r.unitOfMeasure}` }, { header: 'Reference', accessorFn: (r) => r.refNumber ?? r.workOrder ?? '' }, { header: 'Reason', accessorKey: 'reason' }, { header: 'Who', accessorKey: 'performedBy' }]} />
    </ModuleWorkspace>
  );
}

interface WipRow { workOrderId: string; workOrderNumber: string; workOrderStatus: number; itemNumber: string; partNumber: string; lotNumber?: string; serialNumber?: string; locationCode: string; status: number; quantity: number; unitOfMeasure: string; operationSequence?: number; operationName?: string; lastMovedAt?: string }
export function WipPage() {
  const nav = useNavigate();
  const rows = useQuery({ queryKey: ['wh-wip'], queryFn: () => get<WipRow[]>('/api/warehouse/wip') });
  const groups = Object.entries((rows.data ?? []).reduce<Record<string, WipRow[]>>((a, r) => { (a[r.workOrderNumber] ??= []).push(r); return a; }, {}));
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Work in process' }]} title="Work in process">
      {groups.length === 0 && <Paper sx={{ p: 3 }}><Typography color="text.secondary">No material is issued to open work orders.</Typography></Paper>}
      <Stack gap={2}>{groups.map(([wo, list]) => (
        <Section key={wo} title={`${wo} · ${list[0].itemNumber}`} action={<Button size="small" onClick={() => nav(`/production/work-orders/${list[0].workOrderId}`)}>Open order</Button>}>
          <Table size="small"><TableHead><TableRow><TableCell>Operation</TableCell><TableCell>Material</TableCell><TableCell>Lot / serial</TableCell><TableCell>Location</TableCell><TableCell>Status</TableCell><TableCell align="right">Quantity</TableCell><TableCell>Last moved</TableCell></TableRow></TableHead>
            <TableBody>{list.map((r, i) => <TableRow key={i}><TableCell>{r.operationSequence ? `${r.operationSequence} · ${r.operationName ?? ''}` : '—'}</TableCell><TableCell sx={{ fontFamily: mono }}>{r.partNumber}</TableCell>
              <TableCell sx={{ fontFamily: mono }}>{r.serialNumber ?? r.lotNumber ?? '—'}</TableCell><TableCell sx={{ fontFamily: mono }}>{r.locationCode}</TableCell><TableCell><StockBadge status={r.status} /></TableCell>
              <TableCell align="right">{qty(r.quantity)} {r.unitOfMeasure}</TableCell><TableCell>{fmtDateTime(r.lastMovedAt)}</TableCell></TableRow>)}</TableBody></Table>
        </Section>))}</Stack>
    </ModuleWorkspace>
  );
}

// ------------------------------------------------------------------ locations
export function LocationsPage() {
  const nav = useNavigate();
  const { can } = useAuth();
  const qc = useQueryClient();
  const [site, setSite] = useState('');
  const sites = useSites();
  const siteId = site || sites.data?.find((s) => !s.isArchived)?.id || '';
  const locs = useLocations(siteId || undefined);
  const [dlg, setDlg] = useState<{ parent?: LocationRow } | null>(null);
  const list = (locs.data ?? []).slice().sort((a, b) => a.path.localeCompare(b.path));
  const depth = (l: LocationRow) => l.path.split('/').length - 1;
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Locations' }]} title="Warehouse locations"
      actions={<Stack direction="row" gap={1}><SitePicker value={siteId} onChange={setSite} />{can('inventory.edit') && <Button variant="contained" onClick={() => setDlg({})}>New warehouse</Button>}</Stack>}>
      <Paper>
        <Table size="small"><TableHead><TableRow><TableCell>Location</TableCell><TableCell>Level</TableCell><TableCell>Used for</TableCell><TableCell align="right">On hand</TableCell><TableCell>Fill</TableCell><TableCell /></TableRow></TableHead>
          <TableBody>{list.map((l) => (
            <TableRow key={l.id} hover sx={{ cursor: 'pointer', opacity: l.active ? 1 : 0.5 }} onClick={() => nav(`/warehouse/locations/${l.id}`)}>
              <TableCell sx={{ pl: 2 + depth(l) * 2.5 }}><Typography component="span" sx={{ fontFamily: mono, fontWeight: depth(l) === 0 ? 700 : 500 }}>{l.code}</Typography> <Typography component="span" variant="body2" color="text.secondary">{l.name}</Typography>
                {l.frozen && <Chip size="small" color="info" label="Frozen for count" sx={{ ml: 1 }} />}</TableCell>
              <TableCell>{locKinds[l.kind]}</TableCell><TableCell>{purposes[l.purpose]}</TableCell><TableCell align="right" sx={{ fontFamily: mono }}>{qty(l.onHand)}</TableCell>
              <TableCell sx={{ width: 180 }}>{l.capacity ? <FillBar pct={Math.round((100 * l.onHand) / l.capacity)} /> : ''}</TableCell>
              <TableCell align="right">{can('inventory.edit') && <Button size="small" onClick={(e) => { e.stopPropagation(); setDlg({ parent: l }); }}>Add inside</Button>}</TableCell>
            </TableRow>))}
            {list.length === 0 && <TableRow><TableCell colSpan={6}><Typography color="text.secondary" sx={{ py: 2, textAlign: 'center' }}>No locations at this site yet.</Typography></TableCell></TableRow>}</TableBody></Table>
      </Paper>
      <StepDialog open={!!dlg} title={dlg?.parent ? `New location inside ${dlg.parent.code}` : 'New warehouse'} confirm="Create" onClose={() => setDlg(null)}
        inputs={[{ name: 'code', label: 'Code (unique at the site)' }, { name: 'name', label: 'Name' },
          { name: 'kind', label: 'Level', type: 'select', initial: dlg?.parent ? String(Math.min(6, dlg.parent.kind + 1)) : '0', options: locKinds.map((k, i) => [i, k]) },
          { name: 'purpose', label: 'Used for', type: 'select', initial: '0', options: purposes.map((k, i) => [i, k]) }, { name: 'capacity', label: 'Capacity (units, optional)' }]}
        onConfirm={async (v) => { await post('/api/warehouse/locations', { code: v.code, name: v.name, kind: Number(v.kind), purpose: Number(v.purpose), parentId: dlg?.parent?.id ?? null, capacity: v.capacity ? Number(v.capacity) : null, siteId, departmentId: null }); qc.invalidateQueries({ queryKey: ['wh-locations'] }); }} />
    </ModuleWorkspace>
  );
}

export function LocationDetail() {
  const { id } = useParams();
  const { can } = useAuth();
  const qc = useQueryClient();
  const [tab, setTab] = useState('stock');
  const [edit, setEdit] = useState(false);
  const loc = useQuery({ queryKey: ['wh-location', id], queryFn: () => get<{ id: string; code: string; name: string; kind: number; purpose: number; path: string; capacity?: number; active: boolean; frozenByCountId?: string; siteId: string }>(`/api/warehouse/locations/${id}`) });
  const stock = useQuery({ queryKey: ['wh-stock', 'loc', id], queryFn: () => get<StockRow[]>(`/api/warehouse/stock?locationId=${id}&includeChildren=true`) });
  const tx = useQuery({ queryKey: ['wh-ledger', 'loc', id], queryFn: () => get<TxRow[]>(`/api/warehouse/ledger?locationId=${id}`), enabled: tab === 'history' });
  const l = loc.data;
  if (!l) return <ModuleWorkspace module={mod} crumbs={[{ label: 'Locations', to: '/warehouse/locations' }]} title="Loading…">{loc.error ? <Alert severity="error">{(loc.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const code = `QG:LOC:${l.code}`;
  const label = (sym: string) => `/api/trace/labels?symbology=${sym}&content=${encodeURIComponent(code)}`;
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Locations', to: '/warehouse/locations' }, { label: l.code }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{l.path}</span><Typography color="text.secondary">{l.name} · {purposes[l.purpose]}</Typography>{l.frozenByCountId && <Chip size="small" color="info" label="Frozen for count" />}</Stack>}
      tabs={[{ label: 'Stock here', value: 'stock' }, { label: 'Movements', value: 'history' }, { label: 'Label', value: 'label' }, { label: 'Audit', value: 'files' }]} tab={tab} onTab={setTab}
      actions={can('inventory.edit') && <Button variant="outlined" onClick={() => setEdit(true)}>Edit</Button>}>
      <MirrorBanner siteId={l.siteId} />
      {tab === 'stock' && <DataTable data={stock.data ?? []} empty="Nothing stored here."
        columns={[{ header: 'Part', accessorKey: 'partNumber', cell: mnum }, { header: 'Lot / serial', accessorFn: (r) => r.serialNumber ?? r.lotNumber ?? '', cell: mnum }, { header: 'Bin', accessorKey: 'locationCode', cell: mnum },
          { header: 'Status', accessorKey: 'status', cell: (x) => <StockBadge status={x.getValue()} held={x.row.original.held} /> }, { header: 'On hand', accessorKey: 'onHand', cell: (x) => qty(x.getValue()) }, { header: 'Available', accessorKey: 'available', cell: (x) => qty(x.getValue()) }]} />}
      {tab === 'history' && <DataTable data={tx.data ?? []} empty="No movements."
        columns={[{ header: 'Entry', accessorKey: 'number', cell: mnum }, { header: 'When', accessorKey: 'occurredAt', cell: (x) => fmtDateTime(x.getValue()) }, { header: 'Type', accessorFn: (r) => txKinds[r.kind] }, { header: 'Part', accessorKey: 'partNumber' },
          { header: 'From', accessorKey: 'fromLocation' }, { header: 'To', accessorKey: 'toLocation' }, { header: 'Qty', accessorKey: 'quantity' }, { header: 'Who', accessorKey: 'performedBy' }]} />}
      {tab === 'label' && <Paper sx={{ p: 3, display: 'flex', gap: 4, alignItems: 'center', flexWrap: 'wrap', bgcolor: '#fff' }}>
        <Box sx={{ textAlign: 'center' }}><img alt="Code 128" src={label('Code128')} style={{ width: 320 }} /><Typography sx={{ fontFamily: mono }}>{code}</Typography></Box>
        <Box sx={{ textAlign: 'center' }}><img alt="QR" src={label('Qr')} style={{ width: 140 }} /><Typography variant="caption" display="block">QR</Typography></Box>
        <Box sx={{ textAlign: 'center' }}><img alt="Data Matrix" src={label('DataMatrix')} style={{ width: 120 }} /><Typography variant="caption" display="block">Data Matrix</Typography></Box>
      </Paper>}
      {tab === 'files' && <AttachmentsAndHistory type="WarehouseLocation" id={id!} canEdit={false} />}
      <StepDialog open={edit} title={`Edit ${l.code}`} confirm="Save" onClose={() => setEdit(false)}
        inputs={[{ name: 'name', label: 'Name', initial: l.name }, { name: 'purpose', label: 'Used for', type: 'select', initial: String(l.purpose), options: purposes.map((k, i) => [i, k]) },
          { name: 'capacity', label: 'Capacity', initial: l.capacity != null ? String(l.capacity) : '' }, { name: 'active', label: 'Active', type: 'checkbox', initial: String(l.active) }]}
        onConfirm={async (v) => { await put(`/api/warehouse/locations/${id}`, { name: v.name, purpose: Number(v.purpose), capacity: v.capacity ? Number(v.capacity) : null, active: v.active === 'true' }); qc.invalidateQueries({ queryKey: ['wh-location', id] }); }} />
    </ModuleWorkspace>
  );
}

// ------------------------------------------------------------------ receiving
interface Receipt { id: string; number: string; kind: number; status: number; supplierId?: string; purchaseOrderId?: string; purchaseOrderNumber?: string; asnNumber?: string; packingSlip?: string; trackingNumber?: string; receivedOn: string; receivingLocationId: string; notes?: string; siteId: string }
interface ReceiptLine { id: string; lineNumber: number; partId: string; revision?: string; quantityExpected: number; quantityReceived: number; quantityAccepted: number; quantityRejected: number; supplierLotNumber?: string; lotId?: string; serialNumbers?: string;
  inspectionRequired: boolean; inspectionId?: string; aqlInspectionId?: string; ncrId?: string; holdId?: string; putAwayLocationId?: string; status: number; decisionNotes?: string }
interface ReceiptDetail { receipt: Receipt; lines: { line: ReceiptLine; partNumber: string; partName?: string; lotNumber?: string; inspectionNumber?: string; inspectionStatus?: string; aqlNumber?: string; aqlResult?: string; ncrNumber?: string; holdNumber?: string }[] }

export function ReceiptList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const status = p.get('status') ?? '';
  const sups = useLookup('supplier');
  const rows = useQuery({ queryKey: ['wh-receipts', status], queryFn: () => get<Receipt[]>(`/api/warehouse/receipts${status ? `?status=${status}` : ''}`) });
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Receiving' }]} title="Receiving"
      tabs={[{ label: 'All', value: '' }, { label: 'Inspecting', value: '2' }, { label: 'Received', value: '1' }, { label: 'Completed', value: '3' }]} tab={status} onTab={(v) => setP(v ? { status: v } : {})}
      actions={can('inventory.edit') && <Stack direction="row" gap={1}><Button variant="outlined" component={RouterLink} to="/warehouse/scan?flow=receive">Scan to receive</Button><Button variant="contained" component={RouterLink} to="/warehouse/receipts/new">New receipt</Button></Stack>}>
      <DataTable data={rows.data ?? []} onRowClick={(r) => nav(`/warehouse/receipts/${r.id}`)} empty="No receipts."
        columns={[{ header: 'Receipt', accessorKey: 'number', cell: mnum }, { header: 'Received', accessorKey: 'receivedOn', cell: (x) => fmtDate(x.getValue()) }, { header: 'Type', accessorFn: (r) => receiptKinds[r.kind] },
          { header: 'Supplier', accessorFn: (r) => { const s = sups.data?.find((x) => x.id === r.supplierId); return s ? lookupLabel(s) : ''; } }, { header: 'PO', accessorKey: 'purchaseOrderNumber', cell: mnum },
          { header: 'ASN / packing slip', accessorFn: (r) => r.asnNumber ?? r.packingSlip ?? '' }, { header: 'Status', accessorFn: (r) => receiptStatus[r.status], cell: (x) => <StatusChip status={x.getValue()} /> }]} />
    </ModuleWorkspace>
  );
}

interface PoDetail { purchaseOrder: { id: string; number: string; supplierId: string; siteId: string }; lines: { id: string; lineNumber: number; partId?: string; itemNumber: string; revision?: string; quantity: number; quantityReceived: number }[] }
interface DraftLine { partId: string; revision: string; poLineId: string; qty: string; supplierLot: string; lot: string; serials: string; inspect: string; putAway: string }
const emptyLine: DraftLine = { partId: '', revision: '', poLineId: '', qty: '', supplierLot: '', lot: '', serials: '', inspect: 'auto', putAway: '' };

function SiteField({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  const sites = useSites();
  return <TextField select label="Site" value={value} onChange={(e) => onChange(e.target.value)}>{(sites.data ?? []).filter((s) => !s.isArchived).map((s) => <MenuItem key={s.id} value={s.id}>{s.code} — {s.name}</MenuItem>)}</TextField>;
}

export function ReceiptNew() {
  const nav = useNavigate();
  const [p] = useSearchParams();
  const sups = useLookup('supplier'); const parts = useLookup('part'); const carriers = useCarriers();
  const pos = useQuery({ queryKey: ['po-list'], queryFn: () => get<{ id: string; number: string; supplierId: string; siteId: string; status: number }[]>('/api/production/purchase-orders') });
  const [h, setH] = useState({ kind: p.get('po') ? '0' : '3', supplierId: '', purchaseOrderId: p.get('po') ?? '', asnNumber: '', packingSlip: '', carrierId: '', trackingNumber: '', receivedOn: localDate(), siteId: '', receivingLocationId: '', notes: '', supplierOverrideReason: '' });
  const [lines, setLines] = useState<DraftLine[]>([{ ...emptyLine }]);
  const [error, setError] = useState<string | null>(null);
  const po = useQuery({ queryKey: ['po', h.purchaseOrderId], queryFn: () => get<PoDetail>(`/api/production/purchase-orders/${h.purchaseOrderId}`), enabled: !!h.purchaseOrderId });
  const siteId = h.siteId || po.data?.purchaseOrder.siteId || '';
  const set = (k: keyof typeof h) => (e: { target: { value: string } }) => setH({ ...h, [k]: e.target.value });
  const setLine = (i: number, k: keyof DraftLine, v: string) => setLines(lines.map((l, j) => (j === i ? { ...l, [k]: v } : l)));
  const fromPo = () => po.data && setLines(po.data.lines.filter((l) => l.quantity > l.quantityReceived && l.partId).map((l) => ({ ...emptyLine, partId: l.partId!, revision: l.revision ?? '', poLineId: l.id, qty: String(l.quantity - l.quantityReceived) })));
  const save = async () => {
    setError(null);
    try {
      const r = await post<{ id: string }>('/api/warehouse/receipts', {
        ...nullify(h), kind: Number(h.kind), siteId, supplierId: h.supplierId || po.data?.purchaseOrder.supplierId || null, departmentId: null,
        lines: lines.filter((l) => l.partId).map((l) => ({ partId: l.partId, revision: l.revision || null, purchaseOrderLineId: l.poLineId || null, quantityExpected: null, quantityReceived: Number(l.qty), supplierLotNumber: l.supplierLot || null,
          lotNumber: l.lot || null, serialNumbers: l.serials ? l.serials.split(/[\s,]+/).filter(Boolean) : null, inspectionRequired: l.inspect === 'auto' ? null : l.inspect === 'yes', inspectionPlanId: null, aqlPlanId: null, aqlInspectionLevel: null, aqlLevel: null,
          putAwayLocationId: l.putAway || null, clientRequestId: null })),
      });
      nav(`/warehouse/receipts/${r.id}`);
    } catch (e) { setError((e as Error).message); }
  };
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Receiving', to: '/warehouse/receipts' }, { label: 'New' }]} title="Receive material">
      <MirrorBanner siteId={siteId || undefined} />
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <Paper sx={{ p: 2.5, mb: 2, display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(4, 1fr)' }, gap: 2 }}>
        <TextField select label="Receipt type" value={h.kind} onChange={set('kind')}>{receiptKinds.map((k, i) => <MenuItem key={k} value={String(i)}>{k}</MenuItem>)}</TextField>
        <TextField select label="Purchase order" value={h.purchaseOrderId} onChange={set('purchaseOrderId')}><MenuItem value="">None</MenuItem>{(pos.data ?? []).map((x) => <MenuItem key={x.id} value={x.id}>{x.number}</MenuItem>)}</TextField>
        <TextField select label="Supplier" value={h.supplierId || po.data?.purchaseOrder.supplierId || ''} onChange={set('supplierId')}><MenuItem value="">None (blind)</MenuItem>{(sups.data ?? []).map((s) => <MenuItem key={s.id} value={s.id}>{lookupLabel(s)}</MenuItem>)}</TextField>
        <TextField label="Received on" type="date" value={h.receivedOn} onChange={set('receivedOn')} slotProps={{ inputLabel: { shrink: true } }} />
        <SiteField value={siteId} onChange={(v) => setH({ ...h, siteId: v })} />
        <LocationSelect label="Receiving dock" value={h.receivingLocationId} onChange={(v) => setH({ ...h, receivingLocationId: v })} siteId={siteId || undefined} purposes={[1, 2]} />
        <TextField label="ASN number" value={h.asnNumber} onChange={set('asnNumber')} />
        <TextField label="Packing slip" value={h.packingSlip} onChange={set('packingSlip')} />
        <TextField select label="Carrier" value={h.carrierId} onChange={set('carrierId')}><MenuItem value="">None</MenuItem>{(carriers.data ?? []).map((c) => <MenuItem key={c.id} value={c.id}>{c.name}</MenuItem>)}</TextField>
        <TextField label="Tracking / PRO number" value={h.trackingNumber} onChange={set('trackingNumber')} />
        <TextField label="Notes" value={h.notes} onChange={set('notes')} sx={{ gridColumn: { md: 'span 2' } }} />
      </Paper>
      <Section title="Lines" action={<Stack direction="row" gap={1}>{po.data && <Button onClick={fromPo}>Fill from PO {po.data.purchaseOrder.number}</Button>}<Button onClick={() => setLines([...lines, { ...emptyLine }])}>Add line</Button></Stack>}>
        <Stack gap={1.5}>{lines.map((l, i) => (
          <Box key={i} sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', md: '2fr 70px 90px 1fr 1fr 1.3fr 110px 1.3fr 40px' }, gap: 1, alignItems: 'center' }}>
            <TextField select size="small" label="Part" value={l.partId} onChange={(e) => setLine(i, 'partId', e.target.value)}>{(parts.data ?? []).map((x) => <MenuItem key={x.id} value={x.id}>{lookupLabel(x)}</MenuItem>)}</TextField>
            <TextField size="small" label="Rev" value={l.revision} onChange={(e) => setLine(i, 'revision', e.target.value)} />
            <TextField size="small" label="Qty" type="number" value={l.qty} onChange={(e) => setLine(i, 'qty', e.target.value)} />
            <TextField size="small" label="Supplier lot / heat" value={l.supplierLot} onChange={(e) => setLine(i, 'supplierLot', e.target.value)} />
            <TextField size="small" label="Our lot (blank = next)" value={l.lot} onChange={(e) => setLine(i, 'lot', e.target.value)} />
            <TextField size="small" label="Serials (space separated)" value={l.serials} onChange={(e) => setLine(i, 'serials', e.target.value)} />
            <TextField select size="small" label="Inspect" value={l.inspect} onChange={(e) => setLine(i, 'inspect', e.target.value)}><MenuItem value="auto">By plan</MenuItem><MenuItem value="yes">Yes</MenuItem><MenuItem value="no">No</MenuItem></TextField>
            <LocationSelect size="small" label="Put away to" value={l.putAway} onChange={(v) => setLine(i, 'putAway', v)} siteId={siteId || undefined} purposes={[0, 4, 5, 10]} allowNone />
            <Button size="small" color="inherit" aria-label="Remove line" onClick={() => setLines(lines.filter((_, j) => j !== i))}>✕</Button>
          </Box>))}</Stack>
        <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: 1.5 }}>Lines with an approved receiving inspection plan wait in receiving until the inspection is released; the AQL sample is drawn when the part has a sampling plan.</Typography>
      </Section>
      <Stack direction="row" gap={1} sx={{ mt: 2 }}><Button variant="contained" onClick={save}>Receive</Button><Button onClick={() => nav('/warehouse/receipts')}>Cancel</Button></Stack>
    </ModuleWorkspace>
  );
}

export function ReceiptDetailPage() {
  const { id } = useParams();
  const { can } = useAuth();
  const qc = useQueryClient();
  const [tab, setTab] = useState('lines');
  const [dec, setDec] = useState<ReceiptDetail['lines'][number] | null>(null);
  const d = useQuery({ queryKey: ['wh-receipt', id], queryFn: () => get<ReceiptDetail>(`/api/warehouse/receipts/${id}`) });
  const sups = useLookup('supplier');
  const r = d.data?.receipt;
  if (!r) return <ModuleWorkspace module={mod} crumbs={[{ label: 'Receiving', to: '/warehouse/receipts' }]} title="Loading…">{d.error ? <Alert severity="error">{(d.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const sup = sups.data?.find((s) => s.id === r.supplierId);
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Receiving', to: '/warehouse/receipts' }, { label: r.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{r.number}</span><StatusChip status={receiptStatus[r.status]} /></Stack>}
      tabs={[{ label: 'Lines & inspection', value: 'lines' }, { label: 'Ledger', value: 'ledger' }, { label: 'Files & history', value: 'files' }]} tab={tab} onTab={setTab}>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '1fr 280px' }, gap: 2 }}>
        <Box>
          {tab === 'lines' && <Stack gap={1.5}>{d.data!.lines.map((x) => (
            <Paper key={x.line.id} variant="outlined" sx={{ p: 2, borderLeft: 5, borderLeftColor: x.line.status === 2 ? '#2E6B5E' : x.line.status === 1 ? '#B4842A' : x.line.status >= 3 ? '#A4473B' : 'divider' }}>
              <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" gap={1}>
                <Box><Typography sx={{ fontFamily: mono, fontWeight: 600 }}>{x.line.lineNumber}. {x.partNumber}{x.line.revision ? ` rev ${x.line.revision}` : ''}</Typography>
                  <Typography variant="body2" color="text.secondary">{x.partName} · lot <span style={{ fontFamily: mono }}>{x.lotNumber ?? '—'}</span>{x.line.supplierLotNumber ? ` · supplier lot ${x.line.supplierLotNumber}` : ''}</Typography>
                  <Typography variant="body2">Received {qty(x.line.quantityReceived)} · accepted {qty(x.line.quantityAccepted)} · rejected {qty(x.line.quantityRejected)}</Typography></Box>
                <Stack alignItems={{ md: 'flex-end' }} gap={0.75}>
                  <StatusChip status={lineStatus[x.line.status]} />
                  <Stack direction="row" gap={1} flexWrap="wrap">
                    {x.line.inspectionId && <Chip size="small" component={RouterLink} to={`/inspection/${x.line.inspectionId}`} clickable label={`Inspection ${x.inspectionNumber ?? ''} · ${x.inspectionStatus ?? ''}`} />}
                    {x.line.aqlInspectionId && <Chip size="small" component={RouterLink} to={`/fai/aql/${x.line.aqlInspectionId}`} clickable label={`AQL ${x.aqlNumber ?? ''} · ${x.aqlResult ?? 'open'}`} />}
                    {x.line.ncrId && <Chip size="small" color="error" variant="outlined" component={RouterLink} to={`/ncr/${x.line.ncrId}`} clickable label={`NCR ${x.ncrNumber ?? ''}`} />}
                    {x.line.holdId && <Chip size="small" color="warning" variant="outlined" component={RouterLink} to={`/warehouse/holds/${x.line.holdId}`} clickable label={`Hold ${x.holdNumber ?? ''}`} />}
                  </Stack>
                  {can('inventory.edit') && (x.line.status === 1 || x.line.status === 0) && <Button size="small" variant="contained" onClick={() => setDec(x)}>Record outcome</Button>}
                </Stack>
              </Stack>
              {x.line.decisionNotes && <Typography variant="caption" color="text.secondary">{x.line.decisionNotes}</Typography>}
            </Paper>))}</Stack>}
          {tab === 'ledger' && <RefLedger refType="Receipt" refId={r.id} />}
          {tab === 'files' && <AttachmentsAndHistory type="Receipt" id={id!} canEdit={can('inventory.edit')} />}
        </Box>
        <Facts items={[['Type', receiptKinds[r.kind]], ['Supplier', sup ? lookupLabel(sup) : 'Blind receipt'], ['PO', r.purchaseOrderNumber], ['ASN', r.asnNumber], ['Packing slip', r.packingSlip], ['Tracking', r.trackingNumber], ['Received on', fmtDate(r.receivedOn)], ['Notes', r.notes]]} />
      </Box>
      {dec && <StepDialog open title={`Line ${dec.line.lineNumber}: inspection outcome`} confirm="Record" onClose={() => setDec(null)}
        intro="Accept puts the material away as available. Reject moves it to quarantine as rejected, raises an NCR and a quality hold. Quarantine holds it for review."
        inputs={[{ name: 'decision', label: 'Outcome', type: 'select', initial: '0', options: [[0, 'Accept'], [1, 'Reject'], [2, 'Quarantine']] }, { name: 'accepted', label: 'Quantity accepted (partial acceptance)', initial: String(dec.line.quantityReceived) },
          { name: 'notes', label: 'Notes', type: 'multiline' }]}
        onConfirm={async (v) => {
          const locs = await get<LocationRow[]>(`/api/warehouse/locations?siteId=${r.siteId}`);
          const quar = locs.find((l) => l.active && l.purpose === 3)?.id ?? null;
          await post(`/api/warehouse/receipts/${r.id}/lines/${dec.line.id}/decision`, { decision: Number(v.decision), quantityAccepted: v.accepted ? Number(v.accepted) : null, notes: v.notes || null, putAwayLocationId: dec.line.putAwayLocationId ?? null, quarantineLocationId: Number(v.decision) === 0 ? null : quar, holdKind: null });
          qc.invalidateQueries({ queryKey: ['wh-receipt', id] });
        }} />}
    </ModuleWorkspace>
  );
}
export function RefLedger({ refType, refId }: { refType: string; refId: string }) {
  const tx = useQuery({ queryKey: ['wh-ledger', refType, refId], queryFn: () => get<TxRow[]>(`/api/warehouse/ledger?refType=${refType}&refId=${refId}`) });
  return <DataTable data={tx.data ?? []} empty="No postings."
    columns={[{ header: 'Entry', accessorKey: 'number', cell: mnum }, { header: 'When', accessorKey: 'occurredAt', cell: (x) => fmtDateTime(x.getValue()) }, { header: 'Type', accessorFn: (r) => txKinds[r.kind] }, { header: 'Lot / serial', accessorFn: (r) => r.serialNumber ?? r.lotNumber ?? '' },
      { header: 'From', accessorKey: 'fromLocation' }, { header: 'To', accessorKey: 'toLocation' }, { header: 'Status', accessorFn: (r) => (r.toStatus != null ? stockStatusLabel[r.toStatus] : '') }, { header: 'Qty', accessorKey: 'quantity' }, { header: 'Who', accessorKey: 'performedBy' }]} />;
}

// ------------------------------------------------------------------ holds
interface Hold { id: string; number: string; kind: number; status: number; reason: string; partId?: string; lotId?: string; serialId?: string; locationId?: string; ncrId?: string; customerId?: string; supplierId?: string; releaseNotes?: string; releasedAt?: string; createdAt?: string; siteId: string }
export function HoldList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const status = p.get('status') ?? '0';
  const parts = useLookup('part');
  const rows = useQuery({ queryKey: ['wh-holds', status], queryFn: () => get<Hold[]>(`/api/warehouse/holds${status !== 'all' ? `?status=${status}` : ''}`) });
  const qc = useQueryClient();
  const [open, setOpen] = useState(false);
  const sites = useSites();
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Holds' }]} title="Inventory holds"
      tabs={[{ label: 'Active', value: '0' }, { label: 'Release requested', value: '1' }, { label: 'Released', value: '2' }, { label: 'All', value: 'all' }]} tab={status} onTab={(v) => setP({ status: v })}
      actions={can('inventory.edit') && <Button variant="contained" onClick={() => setOpen(true)}>Place hold</Button>}>
      <DataTable data={rows.data ?? []} onRowClick={(r) => nav(`/warehouse/holds/${r.id}`)} empty="No holds in this view."
        columns={[{ header: 'Hold', accessorKey: 'number', cell: mnum }, { header: 'Kind', accessorFn: (r) => holdKinds[r.kind] }, { header: 'Part', accessorFn: (r) => { const x = parts.data?.find((q) => q.id === r.partId); return x ? lookupLabel(x) : r.lotId ? 'Lot scope' : 'Location scope'; } },
          { header: 'Reason', accessorKey: 'reason' }, { header: 'Status', accessorFn: (r) => holdStatus[r.status], cell: (x) => <StatusChip status={x.getValue()} /> }]} />
      <StepDialog open={open} title="Place an inventory hold" confirm="Place hold" onClose={() => setOpen(false)} intro="Held stock cannot be moved, issued or shipped until the release is approved."
        inputs={[{ name: 'kind', label: 'Kind', type: 'select', initial: '0', options: holdKinds.map((k, i) => [i, k]) }, { name: 'partId', label: 'Part', type: 'select', options: (parts.data ?? []).map((x) => [x.id, lookupLabel(x)]) },
          { name: 'lot', label: 'Lot number (optional; blank holds every lot of the part)' }, { name: 'siteId', label: 'Site', type: 'select', initial: sites.data?.[0]?.id, options: (sites.data ?? []).map((s) => [s.id, s.name]) }, { name: 'reason', label: 'Reason', type: 'multiline' }]}
        onConfirm={async (v) => {
          let lotId: string | null = null;
          if (v.lot) { const s = await get<StockRow[]>(`/api/warehouse/stock?q=${encodeURIComponent(v.lot)}`); lotId = s.find((x) => x.lotNumber === v.lot)?.lotId ?? null; if (!lotId) throw new Error(`No stock found for lot ${v.lot}.`); }
          await post('/api/warehouse/holds', { kind: Number(v.kind), reason: v.reason, partId: v.partId || null, lotId, serialId: null, locationId: null, ncrId: null, customerId: null, supplierId: null, siteId: v.siteId, departmentId: null });
          qc.invalidateQueries({ queryKey: ['wh-holds'] });
        }} />
    </ModuleWorkspace>
  );
}
export function HoldDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const { can } = useAuth();
  const [tab, setTab] = useState('scope');
  const [rel, setRel] = useState(false);
  const h = useQuery({ queryKey: ['wh-hold', id], queryFn: () => get<Hold>(`/api/warehouse/holds/${id}`) });
  const parts = useLookup('part');
  const d = h.data;
  const stock = useQuery({ queryKey: ['wh-stock', 'hold', id], queryFn: () => get<StockRow[]>(`/api/warehouse/stock?${d!.lotId ? `lotId=${d!.lotId}` : d!.serialId ? `serialId=${d!.serialId}` : d!.partId ? `partId=${d!.partId}` : `locationId=${d!.locationId}&includeChildren=true`}`), enabled: !!d });
  if (!d) return <ModuleWorkspace module={mod} crumbs={[{ label: 'Holds', to: '/warehouse/holds' }]} title="Loading…">{h.error ? <Alert severity="error">{(h.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const part = parts.data?.find((x) => x.id === d.partId);
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Holds', to: '/warehouse/holds' }, { label: d.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{d.number}</span><StatusChip status={holdStatus[d.status]} /></Stack>}
      tabs={[{ label: 'Held stock', value: 'scope' }, { label: 'Files & history', value: 'files' }]} tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1}>{d.ncrId && <Button component={RouterLink} to={`/ncr/${d.ncrId}`}>Open NCR</Button>}
        {can('inventory.edit') && d.status === 0 && <Button variant="outlined" component={RouterLink} to={`/warehouse/mrb/new?hold=${d.id}`}>Send to MRB</Button>}
        {can('inventory.edit') && d.status === 0 && <Button variant="contained" onClick={() => setRel(true)}>Request release</Button>}</Stack>}>
      {d.status === 1 && <Alert severity="info" sx={{ mb: 2 }}>Release is waiting for e-signed approval. Approvers see it in their approvals inbox.</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '1fr 280px' }, gap: 2 }}>
        <Box>{tab === 'scope' ? <DataTable data={stock.data ?? []} empty="No stock currently in scope."
          columns={[{ header: 'Part', accessorKey: 'partNumber', cell: mnum }, { header: 'Lot / serial', accessorFn: (r) => r.serialNumber ?? r.lotNumber ?? '', cell: mnum }, { header: 'Location', accessorKey: 'locationCode', cell: mnum },
            { header: 'Status', accessorKey: 'status', cell: (x) => <StockBadge status={x.getValue()} held={x.row.original.held} /> }, { header: 'On hand', accessorKey: 'onHand', cell: (x) => qty(x.getValue()) }]} />
          : <AttachmentsAndHistory type="InventoryHold" id={id!} canEdit={can('inventory.edit')} />}</Box>
        <Facts items={[['Kind', holdKinds[d.kind]], ['Part', part ? lookupLabel(part) : undefined], ['Reason', d.reason], ['Release notes', d.releaseNotes], ['Released', d.releasedAt ? fmtDateTime(d.releasedAt) : undefined]]} />
      </Box>
      <StepDialog open={rel} title={`Request release of ${d.number}`} confirm="Request release" onClose={() => setRel(false)} intro="The hold stays in force until the release is e-signed on the approval route."
        inputs={[{ name: 'notes', label: 'Why the stock can be released (evidence, NCR disposition…)', type: 'multiline' }]}
        onConfirm={async (v) => { await post(`/api/warehouse/holds/${id}/request-release`, { notes: v.notes }); qc.invalidateQueries({ queryKey: ['wh-hold', id] }); }} />
    </ModuleWorkspace>
  );
}

// ------------------------------------------------------------------ MRB
interface Mrb { id: string; number: string; status: number; partId: string; lotId?: string; serialId?: string; locationId: string; stockStatus: number; quantity: number; ncrId?: string; holdId?: string; supplierId?: string; disposition: number; justification?: string; targetLocationId?: string; supplierReturnId?: string; executedAt?: string; siteId: string; createdAt?: string }
const mrbSteps = ['Open', 'Proposed', 'Approved', 'Executed'];
const mrbStep = (s: number) => (s === 0 ? 0 : s === 1 ? 1 : s === 2 ? 2 : s === 4 ? 3 : -1);
function Chevrons({ steps, at, failed }: { steps: string[]; at: number; failed?: string }) {
  return <Stack direction="row" sx={{ mb: 2, flexWrap: 'wrap' }}>{steps.map((s, i) => (
    <Box key={s} sx={{ px: 2.5, py: 0.75, mr: '-8px', clipPath: 'polygon(0 0, calc(100% - 10px) 0, 100% 50%, calc(100% - 10px) 100%, 0 100%, 10px 50%)', bgcolor: i <= at ? 'primary.main' : 'action.hover', color: i <= at ? 'primary.contrastText' : 'text.secondary', fontSize: 13, fontWeight: 600 }}>{s}</Box>))}
    {failed && <Chip color="error" label={failed} sx={{ ml: 2 }} />}</Stack>;
}
export function MrbList() {
  const nav = useNavigate();
  const [p, setP] = useSearchParams();
  const status = p.get('status') ?? '';
  const parts = useLookup('part');
  const rows = useQuery({ queryKey: ['wh-mrb', status], queryFn: () => get<Mrb[]>(`/api/warehouse/mrb${status ? `?status=${status}` : ''}`) });
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'MRB' }]} title="Material review board"
      tabs={[{ label: 'All', value: '' }, { label: 'Open', value: '0' }, { label: 'Awaiting approval', value: '1' }, { label: 'Executed', value: '4' }]} tab={status} onTab={(v) => setP(v ? { status: v } : {})}>
      <DataTable data={rows.data ?? []} onRowClick={(r) => nav(`/warehouse/mrb/${r.id}`)} empty="No MRB cases."
        columns={[{ header: 'Case', accessorKey: 'number', cell: mnum }, { header: 'Part', accessorFn: (r) => { const x = parts.data?.find((q) => q.id === r.partId); return x ? lookupLabel(x) : ''; } }, { header: 'Qty', accessorKey: 'quantity', cell: (x) => qty(x.getValue()) },
          { header: 'Stock status', accessorKey: 'stockStatus', cell: (x) => <StockBadge status={x.getValue()} /> }, { header: 'Disposition', accessorFn: (r) => (r.disposition ? mrbDisp[r.disposition] : '—') }, { header: 'Status', accessorFn: (r) => mrbStatus[r.status], cell: (x) => <StatusChip status={x.getValue()} /> }]} />
    </ModuleWorkspace>
  );
}
export function MrbNew() {
  const nav = useNavigate();
  const [p] = useSearchParams();
  const balance = p.get('balance'); const holdId = p.get('hold');
  const hold = useQuery({ queryKey: ['wh-hold', holdId], queryFn: () => get<Hold>(`/api/warehouse/holds/${holdId}`), enabled: !!holdId });
  const stock = useQuery({ queryKey: ['wh-stock', 'mrb', balance, holdId, hold.data?.id], enabled: !!balance || !!hold.data,
    queryFn: () => get<StockRow[]>(`/api/warehouse/stock${hold.data ? (hold.data.lotId ? `?lotId=${hold.data.lotId}` : hold.data.partId ? `?partId=${hold.data.partId}` : '') : ''}`) });
  const [pick, setPick] = useState(balance ?? '');
  const [q, setQ] = useState(''); const [notes, setNotes] = useState(''); const [err, setErr] = useState<string | null>(null);
  const rows = (stock.data ?? []).filter((r) => r.onHand > 0 && (balance ? r.id === balance : true));
  const sel = rows.find((r) => r.id === pick);
  const save = async () => {
    if (!sel) return;
    setErr(null);
    try {
      const r = await post<{ id: string }>('/api/warehouse/mrb', { partId: sel.partId, lotId: sel.lotId ?? null, serialId: sel.serialId ?? null, locationId: sel.locationId, stockStatus: sel.status, quantity: Number(q || sel.onHand), ncrId: hold.data?.ncrId ?? null, holdId: holdId ?? null, notes: notes || null });
      nav(`/warehouse/mrb/${r.id}`);
    } catch (e) { setErr((e as Error).message); }
  };
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'MRB', to: '/warehouse/mrb' }, { label: 'New case' }]} title="Open an MRB case">
      {err && <Alert severity="error" sx={{ mb: 2 }}>{err}</Alert>}
      {!balance && !holdId && <Alert severity="info" sx={{ mb: 2 }}>Start an MRB case from a stock row (Stock → MRB) or from a hold.</Alert>}
      <Paper sx={{ p: 2.5 }}><Stack gap={2}>
        <TextField select label="Stock to review" value={pick} onChange={(e) => setPick(e.target.value)}>{rows.map((r) => <MenuItem key={r.id} value={r.id}>{r.partNumber} · {r.serialNumber ?? r.lotNumber ?? 'no lot'} · {r.locationCode} · {stockStatusLabel[r.status]} · {qty(r.onHand)}</MenuItem>)}</TextField>
        <TextField label="Quantity" type="number" value={q || (sel ? String(sel.onHand) : '')} onChange={(e) => setQ(e.target.value)} />
        <TextField label="Notes" multiline minRows={3} value={notes} onChange={(e) => setNotes(e.target.value)} />
        <Stack direction="row" gap={1}><Button variant="contained" disabled={!sel} onClick={save}>Open case</Button><Button onClick={() => nav(-1)}>Cancel</Button></Stack>
      </Stack></Paper>
    </ModuleWorkspace>
  );
}
export function MrbDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const { can } = useAuth();
  const [tab, setTab] = useState('case');
  const [prop, setProp] = useState(false);
  const c = useQuery({ queryKey: ['wh-mrb-case', id], queryFn: () => get<Mrb>(`/api/warehouse/mrb/${id}`) });
  const parts = useLookup('part');
  const d = c.data;
  const locs = useLocations(d?.siteId);
  if (!d) return <ModuleWorkspace module={mod} crumbs={[{ label: 'MRB', to: '/warehouse/mrb' }]} title="Loading…">{c.error ? <Alert severity="error">{(c.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const part = parts.data?.find((x) => x.id === d.partId);
  const loc = (x?: string) => locs.data?.find((l) => l.id === x)?.code;
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'MRB', to: '/warehouse/mrb' }, { label: d.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{d.number}</span><StatusChip status={mrbStatus[d.status]} /></Stack>}
      tabs={[{ label: 'Case', value: 'case' }, { label: 'Ledger', value: 'ledger' }, { label: 'Files & history', value: 'files' }]} tab={tab} onTab={setTab}
      actions={can('inventory.edit') && (d.status === 0 || d.status === 3) && <Button variant="contained" onClick={() => setProp(true)}>Propose disposition</Button>}>
      <Chevrons steps={mrbSteps} at={mrbStep(d.status)} failed={d.status === 3 ? 'Proposal rejected' : d.status === 5 ? 'Cancelled' : undefined} />
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '1fr 300px' }, gap: 2 }}>
        <Box>
          {tab === 'case' && <Section title="Disposition">
            {d.disposition ? <Stack gap={1}><Typography variant="h6">{mrbDisp[d.disposition]}</Typography><Typography>{d.justification}</Typography>
              {d.targetLocationId && <Typography variant="body2">Target location: <b style={{ fontFamily: mono }}>{loc(d.targetLocationId)}</b></Typography>}
              {d.supplierReturnId && <Button sx={{ alignSelf: 'start' }} component={RouterLink} to={`/warehouse/rtv/${d.supplierReturnId}`}>Open return to vendor</Button>}
              {d.status === 1 && <Alert severity="info">Awaiting e-signed approval. Once approved the disposition is posted to the ledger automatically.</Alert>}</Stack>
              : <Typography color="text.secondary">No disposition proposed yet.</Typography>}</Section>}
          {tab === 'ledger' && <RefLedger refType="MrbCase" refId={d.id} />}
          {tab === 'files' && <AttachmentsAndHistory type="MrbCase" id={id!} canEdit={can('inventory.edit')} />}
        </Box>
        <Facts items={[['Part', part ? lookupLabel(part) : undefined], ['Quantity', qty(d.quantity)], ['Location', loc(d.locationId)], ['Stock status', stockStatusLabel[d.stockStatus]], ['Executed', d.executedAt ? fmtDateTime(d.executedAt) : undefined]]} />
      </Box>
      <Box sx={{ display: 'flex', gap: 1, mt: 1 }}>{d.ncrId && <Button component={RouterLink} to={`/ncr/${d.ncrId}`}>NCR</Button>}{d.holdId && <Button component={RouterLink} to={`/warehouse/holds/${d.holdId}`}>Hold</Button>}</Box>
      <StepDialog open={prop} title="Propose disposition" confirm="Submit for approval" onClose={() => setProp(false)}
        inputs={[{ name: 'disposition', label: 'Disposition', type: 'select', initial: '1', options: mrbDisp.map((k, i) => [i, k] as [number, string]).slice(1) },
          { name: 'target', label: 'Target location (accept / use as is / rework)', type: 'select', options: [['', 'Default'] as [string, string], ...(locs.data ?? []).filter((l) => l.active).map((l) => [l.id, `${l.code} — ${purposes[l.purpose]}`] as [string, string])] },
          { name: 'justification', label: 'Justification', type: 'multiline' }]}
        onConfirm={async (v) => { await post(`/api/warehouse/mrb/${id}/propose`, { disposition: Number(v.disposition), justification: v.justification, targetLocationId: v.target || null }); qc.invalidateQueries({ queryKey: ['wh-mrb-case', id] }); }} />
    </ModuleWorkspace>
  );
}

// ------------------------------------------------------------------ return to vendor
interface Rtv { id: string; number: string; supplierId: string; status: number; partId: string; lotId?: string; locationId: string; stockStatus: number; quantity: number; reason: string; ncrId?: string; mrbCaseId?: string; supplierRma?: string; carrierId?: string; trackingNumber?: string; shippedOn?: string; creditAmount?: number; creditReference?: string; creditedOn?: string; siteId: string }
export function RtvList() {
  const nav = useNavigate();
  const sups = useLookup('supplier'); const parts = useLookup('part');
  const rows = useQuery({ queryKey: ['wh-rtv'], queryFn: () => get<Rtv[]>('/api/warehouse/supplier-returns') });
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Returns to vendor' }]} title="Returns to vendor">
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>Returns are raised by an approved MRB disposition of “Return to supplier”.</Typography>
      <DataTable data={rows.data ?? []} onRowClick={(r) => nav(`/warehouse/rtv/${r.id}`)} empty="No returns to vendor."
        columns={[{ header: 'Return', accessorKey: 'number', cell: mnum }, { header: 'Supplier', accessorFn: (r) => { const s = sups.data?.find((x) => x.id === r.supplierId); return s ? lookupLabel(s) : ''; } },
          { header: 'Part', accessorFn: (r) => { const x = parts.data?.find((q) => q.id === r.partId); return x ? lookupLabel(x) : ''; } }, { header: 'Qty', accessorKey: 'quantity', cell: (x) => qty(x.getValue()) },
          { header: 'Supplier RMA', accessorKey: 'supplierRma' }, { header: 'Credit', accessorFn: (r) => (r.creditAmount != null ? r.creditAmount.toFixed(2) : '') }, { header: 'Status', accessorFn: (r) => rtvStatus[r.status], cell: (x) => <StatusChip status={x.getValue()} /> }]} />
    </ModuleWorkspace>
  );
}
export function RtvDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const { can } = useAuth();
  const [tab, setTab] = useState('ret');
  const [dlg, setDlg] = useState<'ship' | 'credit' | null>(null);
  const r = useQuery({ queryKey: ['wh-rtv', id], queryFn: () => get<Rtv>(`/api/warehouse/supplier-returns/${id}`) });
  const sups = useLookup('supplier'); const parts = useLookup('part'); const carriers = useCarriers();
  const d = r.data;
  if (!d) return <ModuleWorkspace module={mod} crumbs={[{ label: 'Returns to vendor', to: '/warehouse/rtv' }]} title="Loading…">{r.error ? <Alert severity="error">{(r.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const sup = sups.data?.find((x) => x.id === d.supplierId); const part = parts.data?.find((x) => x.id === d.partId);
  const refresh = () => qc.invalidateQueries({ queryKey: ['wh-rtv', id] });
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Returns to vendor', to: '/warehouse/rtv' }, { label: d.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{d.number}</span><StatusChip status={rtvStatus[d.status]} /></Stack>}
      tabs={[{ label: 'Return', value: 'ret' }, { label: 'Custody', value: 'custody' }, { label: 'Ledger', value: 'ledger' }, { label: 'Files & history', value: 'files' }]} tab={tab} onTab={setTab}
      actions={can('inventory.edit') && <Stack direction="row" gap={1}>
        {d.status === 0 && <Button variant="contained" onClick={() => setDlg('ship')}>Ship to supplier</Button>}
        {d.status === 1 && <Button variant="contained" onClick={() => setDlg('credit')}>Record credit</Button>}
        {(d.status === 1 || d.status === 2) && <Button variant="outlined" onClick={async () => { await post(`/api/warehouse/supplier-returns/${id}/close`, {}); refresh(); }}>Close</Button>}</Stack>}>
      <Chevrons steps={['Open', 'Shipped', 'Credited', 'Closed']} at={d.status <= 3 ? d.status : -1} failed={d.status === 4 ? 'Cancelled' : undefined} />
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '1fr 300px' }, gap: 2 }}>
        <Box>
          {tab === 'ret' && <Section title="Reason"><Typography>{d.reason}</Typography>
            <Stack direction="row" gap={1} sx={{ mt: 2 }}>{d.mrbCaseId && <Button component={RouterLink} to={`/warehouse/mrb/${d.mrbCaseId}`}>MRB case</Button>}{d.ncrId && <Button component={RouterLink} to={`/ncr/${d.ncrId}`}>NCR</Button>}</Stack></Section>}
          {tab === 'custody' && <Custody url={`/api/warehouse/supplier-returns/${id}/custody`} refType="SupplierReturn" refId={d.id} />}
          {tab === 'ledger' && <RefLedger refType="SupplierReturn" refId={d.id} />}
          {tab === 'files' && <AttachmentsAndHistory type="SupplierReturn" id={id!} canEdit={can('inventory.edit')} />}
        </Box>
        <Facts items={[['Supplier', sup ? lookupLabel(sup) : undefined], ['Part', part ? lookupLabel(part) : undefined], ['Quantity', qty(d.quantity)], ['Supplier RMA', d.supplierRma], ['Carrier', carriers.data?.find((c) => c.id === d.carrierId)?.name], ['Tracking', d.trackingNumber],
          ['Shipped', d.shippedOn ? fmtDate(d.shippedOn) : undefined], ['Credit', d.creditAmount != null ? `${d.creditAmount.toFixed(2)} (${d.creditReference})` : undefined], ['Credited', d.creditedOn ? fmtDate(d.creditedOn) : undefined]]} />
      </Box>
      <StepDialog open={dlg === 'ship'} title="Ship to supplier" confirm="Ship" onClose={() => setDlg(null)} intro="Shipping posts the stock out of inventory as a supplier return."
        inputs={[{ name: 'carrierId', label: 'Carrier', type: 'select', options: (carriers.data ?? []).map((c) => [c.id, c.name]) }, { name: 'tracking', label: 'Tracking number' }, { name: 'rma', label: 'Supplier RMA', initial: d.supplierRma }, { name: 'on', label: 'Shipped on', type: 'date', initial: localDate() }]}
        onConfirm={async (v) => { await post(`/api/warehouse/supplier-returns/${id}/ship`, { carrierId: v.carrierId || null, trackingNumber: v.tracking || null, shippedOn: v.on, supplierRma: v.rma || null }); refresh(); }} />
      <StepDialog open={dlg === 'credit'} title="Record supplier credit" confirm="Record" onClose={() => setDlg(null)}
        inputs={[{ name: 'amount', label: 'Credit amount' }, { name: 'ref', label: 'Credit memo / reference' }, { name: 'on', label: 'Credited on', type: 'date', initial: localDate() }]}
        onConfirm={async (v) => { await post(`/api/warehouse/supplier-returns/${id}/credit`, { creditAmount: Number(v.amount), creditReference: v.ref, creditedOn: v.on }); refresh(); }} />
    </ModuleWorkspace>
  );
}

interface Custody { action: string; by: string; at: string; location?: string; notes?: string }
export function Custody({ url }: { url: string; refType?: string; refId?: string }) {
  const c = useQuery({ queryKey: ['custody', url], queryFn: () => get<Custody[]>(url) });
  const rows = c.data ?? [];
  if (!rows.length) return <Typography color="text.secondary">No custody events yet.</Typography>;
  return <Box sx={{ position: 'relative', pl: 3 }}>
    <Box sx={{ position: 'absolute', left: 9, top: 6, bottom: 6, width: 2, bgcolor: 'divider' }} />
    {rows.map((e, i) => (
      <Box key={i} sx={{ position: 'relative', mb: 2 }}>
        <Box sx={{ position: 'absolute', left: -21, top: 4, width: 12, height: 12, bgcolor: i === rows.length - 1 ? 'primary.main' : 'background.paper', border: 2, borderColor: 'primary.main', transform: 'rotate(45deg)' }} />
        <Typography sx={{ fontWeight: 600 }}>{e.action}{e.location ? <Typography component="span" sx={{ fontFamily: mono, ml: 1 }} color="text.secondary">@ {e.location}</Typography> : null}</Typography>
        <Typography variant="body2" color="text.secondary">{fmtDateTime(e.at)} · {e.by}</Typography>
        {e.notes && <Typography variant="body2">{e.notes}</Typography>}
      </Box>))}
  </Box>;
}

// ------------------------------------------------------------------ counts
interface CountRow { id: string; number: string; frequency: number; status: number; locationId: string; partId?: string; freeze: boolean; scheduledOn: string; submittedAt?: string; postedAt?: string; notes?: string; siteId: string }
interface Variance { lineId: string; partNumber: string; lotNumber?: string; serialNumber?: string; locationCode: string; status: number; system: number; counted?: number | null; variance?: number | null; variancePct?: number | null }
interface CountDetailDto { count: CountRow; locationCode: string; lines: Variance[]; counted: number; withVariance: number; netVariance: number }
export function CountList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const qc = useQueryClient();
  const rows = useQuery({ queryKey: ['wh-counts'], queryFn: () => get<CountRow[]>('/api/warehouse/counts') });
  const locs = useLocations(); const parts = useLookup('part');
  const [open, setOpen] = useState(false);
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Counts' }]} title="Cycle counts & physical inventory"
      actions={can('inventory.edit') && <Button variant="contained" onClick={() => setOpen(true)}>Schedule count</Button>}>
      <DataTable data={rows.data ?? []} onRowClick={(r) => nav(`/warehouse/counts/${r.id}`)} empty="No counts scheduled."
        columns={[{ header: 'Count', accessorKey: 'number', cell: mnum }, { header: 'Type', accessorFn: (r) => countFreq[r.frequency] }, { header: 'Location', accessorFn: (r) => locs.data?.find((l) => l.id === r.locationId)?.path ?? '', cell: mnum },
          { header: 'Scheduled', accessorKey: 'scheduledOn', cell: (x) => fmtDate(x.getValue()) }, { header: 'Freeze', accessorFn: (r) => (r.freeze ? 'Frozen' : '') }, { header: 'Status', accessorFn: (r) => countStatus[r.status], cell: (x) => <StatusChip status={x.getValue()} /> }]} />
      <StepDialog open={open} title="Schedule a count" confirm="Create count" onClose={() => setOpen(false)} intro="The count snapshots system quantities for the location and everything inside it. Freezing blocks all movements in or out until the count is posted or cancelled."
        inputs={[{ name: 'frequency', label: 'Type', type: 'select', initial: '0', options: countFreq.map((k, i) => [i, k]) }, { name: 'locationId', label: 'Location', type: 'select', options: (locs.data ?? []).filter((l) => l.active).map((l) => [l.id, l.path]) },
          { name: 'partId', label: 'Only this part (optional)', type: 'select', options: [['', 'All parts'] as [string, string], ...(parts.data ?? []).map((x) => [x.id, lookupLabel(x)] as [string, string])] }, { name: 'freeze', label: 'Freeze the location during the count', type: 'checkbox', initial: 'false' },
          { name: 'on', label: 'Scheduled on', type: 'date', initial: localDate() }, { name: 'notes', label: 'Notes', type: 'multiline' }]}
        onConfirm={async (v) => { const r = await post<{ id: string }>('/api/warehouse/counts', { frequency: Number(v.frequency), locationId: v.locationId, partId: v.partId || null, freeze: v.freeze === 'true', scheduledOn: v.on, notes: v.notes || null }); qc.invalidateQueries({ queryKey: ['wh-counts'] }); nav(`/warehouse/counts/${r.id}`); }} />
    </ModuleWorkspace>
  );
}
export function CountDetailPage() {
  const { id } = useParams();
  const qc = useQueryClient();
  const { can } = useAuth();
  const [tab, setTab] = useState('sheet');
  const [entry, setEntry] = useState<Record<string, string>>({});
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const c = useQuery({ queryKey: ['wh-count', id], queryFn: () => get<CountDetailDto>(`/api/warehouse/counts/${id}`) });
  const d = c.data;
  if (!d) return <ModuleWorkspace module={mod} crumbs={[{ label: 'Counts', to: '/warehouse/counts' }]} title="Loading…">{c.error ? <Alert severity="error">{(c.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const open = d.count.status === 0 && can('inventory.edit');
  const run = async (f: () => Promise<unknown>, ok: string) => { setMsg(null); try { await f(); setMsg({ ok: true, text: ok }); setEntry({}); qc.invalidateQueries({ queryKey: ['wh-count', id] }); } catch (e) { setMsg({ ok: false, text: (e as Error).message }); } };
  const saveEntries = () => run(() => post(`/api/warehouse/counts/${id}/entries`, { entries: Object.entries(entry).filter(([, v]) => v !== '').map(([lineId, v]) => ({ lineId, partId: null, lotId: null, serialId: null, locationId: null, countedQuantity: Number(v), notes: null })) }), 'Counts saved.');
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Counts', to: '/warehouse/counts' }, { label: d.count.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{d.count.number}</span><StatusChip status={countStatus[d.count.status]} />{d.count.freeze && <Chip size="small" color="info" label={`${d.locationCode} frozen`} />}</Stack>}
      tabs={[{ label: 'Count sheet', value: 'sheet' }, { label: 'Ledger', value: 'ledger' }, { label: 'Files & history', value: 'files' }]} tab={tab} onTab={setTab}
      actions={open && <Stack direction="row" gap={1}><Button onClick={saveEntries} disabled={!Object.keys(entry).length}>Save counts</Button>
        <Button variant="contained" onClick={() => run(() => post(`/api/warehouse/counts/${id}/submit`, {}), 'Submitted. Variances post once the adjustment is e-signed.')}>Submit for approval</Button>
        <Button color="inherit" onClick={() => run(() => post(`/api/warehouse/counts/${id}/cancel`, {}), 'Count cancelled; the location is unfrozen.')}>Cancel count</Button></Stack>}>
      {msg && <Alert severity={msg.ok ? 'success' : 'error'} sx={{ mb: 2 }}>{msg.text}</Alert>}
      <Stack direction="row" gap={2} sx={{ mb: 2, flexWrap: 'wrap' }}>
        <MetricTile label="Lines counted" value={d.counted} unit="" num={d.counted} den={d.lines.length} />
        <MetricTile label="Lines with variance" value={d.withVariance} unit="" />
        <MetricTile label="Net variance" value={d.netVariance} unit="units" />
      </Stack>
      {tab === 'sheet' && <Paper><Table size="small"><TableHead><TableRow><TableCell>Location</TableCell><TableCell>Part</TableCell><TableCell>Lot / serial</TableCell><TableCell>Status</TableCell><TableCell align="right">System</TableCell><TableCell align="right">Counted</TableCell><TableCell align="right">Variance</TableCell></TableRow></TableHead>
        <TableBody>{d.lines.map((l) => (
          <TableRow key={l.lineId} sx={{ bgcolor: l.variance ? 'rgba(164,71,59,0.06)' : undefined }}>
            <TableCell sx={{ fontFamily: mono }}>{l.locationCode}</TableCell><TableCell sx={{ fontFamily: mono }}>{l.partNumber}</TableCell><TableCell sx={{ fontFamily: mono }}>{l.serialNumber ?? l.lotNumber ?? ''}</TableCell>
            <TableCell><StockBadge status={l.status} /></TableCell><TableCell align="right">{qty(l.system)}</TableCell>
            <TableCell align="right" sx={{ width: 140 }}>{open ? <TextField size="small" type="number" value={entry[l.lineId] ?? (l.counted ?? '')} onChange={(e) => setEntry({ ...entry, [l.lineId]: e.target.value })} slotProps={{ htmlInput: { 'aria-label': `Counted ${l.partNumber} ${l.lotNumber ?? ''}`, style: { textAlign: 'right' } } }} /> : qty(l.counted)}</TableCell>
            <TableCell align="right" sx={{ color: l.variance ? 'error.main' : undefined, fontWeight: l.variance ? 700 : 400 }}>{l.variance != null ? `${l.variance > 0 ? '+' : ''}${qty(l.variance)}${l.variancePct != null ? ` (${l.variancePct.toFixed(1)}%)` : ''}` : ''}</TableCell>
          </TableRow>))}</TableBody></Table></Paper>}
      {tab === 'ledger' && <RefLedger refType="CycleCount" refId={d.count.id} />}
      {tab === 'files' && <AttachmentsAndHistory type="CycleCount" id={id!} canEdit={can('inventory.edit')} />}
    </ModuleWorkspace>
  );
}

// ------------------------------------------------------------------ transfers
interface Transfer { id: string; number: string; kind: number; status: number; fromLocationId: string; toLocationId: string; toSiteId: string; partId: string; lotId?: string; serialId?: string; quantity: number; carrierId?: string; trackingNumber?: string; shippedAt?: string; receivedAt?: string; notes?: string; siteId: string }
export function TransferList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const status = p.get('status') ?? '';
  const parts = useLookup('part'); const locs = useLocations();
  const rows = useQuery({ queryKey: ['wh-transfers', status], queryFn: () => get<Transfer[]>(`/api/warehouse/transfers${status ? `?status=${status}` : ''}`) });
  const code = (x: string) => locs.data?.find((l) => l.id === x)?.code ?? '';
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Transfers' }]} title="Transfers"
      tabs={[{ label: 'All', value: '' }, { label: 'Open', value: '0' }, { label: 'In transit', value: '1' }, { label: 'Received', value: '2' }]} tab={status} onTab={(v) => setP(v ? { status: v } : {})}
      actions={can('inventory.edit') && <Button variant="contained" component={RouterLink} to="/warehouse/transfers/new">New transfer</Button>}>
      <DataTable data={rows.data ?? []} onRowClick={(r) => nav(`/warehouse/transfers/${r.id}`)} empty="No transfers."
        columns={[{ header: 'Transfer', accessorKey: 'number', cell: mnum }, { header: 'Type', accessorFn: (r) => transferKinds[r.kind] }, { header: 'Part', accessorFn: (r) => { const x = parts.data?.find((q) => q.id === r.partId); return x ? lookupLabel(x) : ''; } },
          { header: 'Route', accessorFn: (r) => `${code(r.fromLocationId)} → ${code(r.toLocationId)}`, cell: mnum }, { header: 'Qty', accessorKey: 'quantity', cell: (x) => qty(x.getValue()) }, { header: 'Status', accessorFn: (r) => transferStatus[r.status], cell: (x) => <StatusChip status={x.getValue()} /> }]} />
    </ModuleWorkspace>
  );
}
export function TransferNew() {
  const nav = useNavigate();
  const [p] = useSearchParams();
  const [site, setSite] = useState('');
  const sites = useSites();
  const siteId = site || sites.data?.find((s) => !s.isArchived)?.id || '';
  const stock = useQuery({ queryKey: ['wh-stock', 'xfer', siteId], queryFn: () => get<StockRow[]>(`/api/warehouse/stock?siteId=${siteId}&status=0`), enabled: !!siteId });
  const [f, setF] = useState({ kind: '0', balance: p.get('balance') ?? '', to: '', q: '', notes: '' });
  const [err, setErr] = useState<string | null>(null);
  const sel = stock.data?.find((r) => r.id === f.balance);
  const save = async () => {
    if (!sel) return;
    setErr(null);
    try {
      const r = await post<{ id: string }>('/api/warehouse/transfers', { kind: Number(f.kind), partId: sel.partId, lotId: sel.lotId ?? null, serialId: sel.serialId ?? null, fromLocationId: sel.locationId, toLocationId: f.to, quantity: Number(f.q || sel.available), notes: f.notes || null });
      nav(`/warehouse/transfers/${r.id}`);
    } catch (e) { setErr((e as Error).message); }
  };
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Transfers', to: '/warehouse/transfers' }, { label: 'New' }]} title="New transfer">
      {err && <Alert severity="error" sx={{ mb: 2 }}>{err}</Alert>}
      <Paper sx={{ p: 2.5 }}><Stack gap={2}>
        <SitePicker value={siteId} onChange={setSite} label="From site" />
        <TextField select label="Type" value={f.kind} onChange={(e) => setF({ ...f, kind: e.target.value })}>{transferKinds.map((k, i) => <MenuItem key={k} value={String(i)}>{k} transfer</MenuItem>)}</TextField>
        <TextField select label="Stock to send" value={f.balance} onChange={(e) => setF({ ...f, balance: e.target.value })}>{(stock.data ?? []).filter((r) => r.available > 0).map((r) => <MenuItem key={r.id} value={r.id}>{r.partNumber} · {r.serialNumber ?? r.lotNumber ?? 'no lot'} · {r.locationCode} · {qty(r.available)} available</MenuItem>)}</TextField>
        <LocationSelect label="Destination (any site)" value={f.to} onChange={(v) => setF({ ...f, to: v })} />
        <TextField label="Quantity" type="number" value={f.q || (sel ? String(sel.available) : '')} onChange={(e) => setF({ ...f, q: e.target.value })} />
        <TextField label="Notes" value={f.notes} onChange={(e) => setF({ ...f, notes: e.target.value })} />
        <Typography variant="caption" color="text.secondary">Site-to-site transfers go in transit when shipped and land when the destination receives them. Within a site the stock moves when shipped.</Typography>
        <Stack direction="row" gap={1}><Button variant="contained" disabled={!sel || !f.to} onClick={save}>Create transfer</Button><Button onClick={() => nav(-1)}>Cancel</Button></Stack>
      </Stack></Paper>
    </ModuleWorkspace>
  );
}
export function TransferDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const { can } = useAuth();
  const [tab, setTab] = useState('custody');
  const [ship, setShip] = useState(false);
  const t = useQuery({ queryKey: ['wh-transfer', id], queryFn: () => get<Transfer>(`/api/warehouse/transfers/${id}`) });
  const parts = useLookup('part'); const locs = useLocations(); const carriers = useCarriers();
  const d = t.data;
  if (!d) return <ModuleWorkspace module={mod} crumbs={[{ label: 'Transfers', to: '/warehouse/transfers' }]} title="Loading…">{t.error ? <Alert severity="error">{(t.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const part = parts.data?.find((x) => x.id === d.partId);
  const path = (x: string) => locs.data?.find((l) => l.id === x)?.path;
  const refresh = () => { qc.invalidateQueries({ queryKey: ['wh-transfer', id] }); qc.invalidateQueries({ queryKey: ['custody'] }); };
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Transfers', to: '/warehouse/transfers' }, { label: d.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{d.number}</span><StatusChip status={transferStatus[d.status]} /></Stack>}
      tabs={[{ label: 'Chain of custody', value: 'custody' }, { label: 'Ledger', value: 'ledger' }, { label: 'Files & history', value: 'files' }]} tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1}><Button href={`/api/warehouse/transfers/${id}/document`} target="_blank">Transfer document</Button>
        {can('inventory.edit') && d.status === 0 && <Button variant="contained" onClick={() => setShip(true)}>Ship</Button>}
        {can('inventory.edit') && d.status === 1 && <Button variant="contained" onClick={async () => { await post(`/api/warehouse/transfers/${id}/receive`, { notes: '' }); refresh(); }}>Receive</Button>}</Stack>}>
      <Chevrons steps={['Open', 'In transit', 'Received']} at={d.status <= 2 ? d.status : -1} failed={d.status === 3 ? 'Cancelled' : undefined} />
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '1fr 300px' }, gap: 2 }}>
        <Box>
          {tab === 'custody' && <Custody url={`/api/warehouse/transfers/${id}/custody`} />}
          {tab === 'ledger' && <RefLedger refType="StockTransfer" refId={d.id} />}
          {tab === 'files' && <AttachmentsAndHistory type="StockTransfer" id={id!} canEdit={can('inventory.edit')} />}
        </Box>
        <Facts items={[['Type', transferKinds[d.kind]], ['Part', part ? lookupLabel(part) : undefined], ['Quantity', qty(d.quantity)], ['From', path(d.fromLocationId)], ['To', path(d.toLocationId)],
          ['Carrier', carriers.data?.find((c) => c.id === d.carrierId)?.name], ['Tracking', d.trackingNumber], ['Shipped', d.shippedAt ? fmtDateTime(d.shippedAt) : undefined], ['Received', d.receivedAt ? fmtDateTime(d.receivedAt) : undefined], ['Notes', d.notes]]} />
      </Box>
      <StepDialog open={ship} title={`Ship ${d.number}`} confirm="Ship" onClose={() => setShip(false)}
        inputs={[{ name: 'carrierId', label: 'Carrier (optional)', type: 'select', options: [['', 'None / internal'] as [string, string], ...(carriers.data ?? []).map((c) => [c.id, c.name] as [string, string])] }, { name: 'tracking', label: 'Tracking number' }, { name: 'notes', label: 'Notes' }]}
        onConfirm={async (v) => { await post(`/api/warehouse/transfers/${id}/ship`, { carrierId: v.carrierId || null, trackingNumber: v.tracking || null, notes: v.notes || null }); refresh(); }} />
    </ModuleWorkspace>
  );
}

// ------------------------------------------------------------------ lot merge
export function LotMergePage() {
  const nav = useNavigate();
  const [site, setSite] = useState('');
  const sites = useSites();
  const siteId = site || sites.data?.find((s) => !s.isArchived)?.id || '';
  const [part, setPart] = useState('');
  const parts = useLookup('part');
  const stock = useQuery({ queryKey: ['wh-stock', 'merge', siteId, part], queryFn: () => get<StockRow[]>(`/api/warehouse/stock?siteId=${siteId}&partId=${part}`), enabled: !!siteId && !!part });
  const [picked, setPicked] = useState<string[]>([]);
  const [f, setF] = useState({ target: '', newLot: '', to: '', reason: '' });
  const [res, setRes] = useState<{ ok: boolean; text: string; lotId?: string } | null>(null);
  const rows = useMemo(() => (stock.data ?? []).filter((r) => r.lotId && !r.serialId && r.onHand > 0), [stock.data]);
  const chosen = rows.filter((r) => picked.includes(r.id));
  const merge = async () => {
    setRes(null);
    try {
      const r = await post<{ lotId: string; lotNumber: string; quantity: number }>('/api/warehouse/lots/merge', { sources: chosen.map((c) => ({ lotId: c.lotId, locationId: c.locationId, status: c.status })), targetLotId: f.target || null, newLotNumber: f.newLot || null, toLocationId: f.to, reason: f.reason || null });
      setRes({ ok: true, text: `Merged into ${r.lotNumber} — ${qty(r.quantity)} on hand. Genealogy records each source lot.`, lotId: r.lotId }); setPicked([]); stock.refetch();
    } catch (e) { setRes({ ok: false, text: (e as Error).message }); }
  };
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Lot merge' }]} title="Merge lots">
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>Combine whole lots of the same part, revision and status into one lot. Held, reserved or split-location lots are refused. Split a lot from the Stock page.</Typography>
      {res && <Alert severity={res.ok ? 'success' : 'error'} sx={{ mb: 2 }} action={res.lotId && <Button color="inherit" onClick={() => nav(`/trace/lots/${res.lotId}`)}>Genealogy</Button>}>{res.text}</Alert>}
      <Stack direction={{ xs: 'column', md: 'row' }} gap={2} sx={{ mb: 2 }}>
        <SitePicker value={siteId} onChange={setSite} />
        <TextField select size="small" label="Part" value={part} onChange={(e) => { setPart(e.target.value); setPicked([]); }} sx={{ minWidth: 260 }}>{(parts.data ?? []).map((x) => <MenuItem key={x.id} value={x.id}>{lookupLabel(x)}</MenuItem>)}</TextField>
      </Stack>
      <Paper sx={{ mb: 2 }}><Table size="small"><TableHead><TableRow><TableCell padding="checkbox" /><TableCell>Lot</TableCell><TableCell>Rev</TableCell><TableCell>Location</TableCell><TableCell>Status</TableCell><TableCell align="right">On hand</TableCell><TableCell align="right">Reserved</TableCell></TableRow></TableHead>
        <TableBody>{rows.map((r) => (
          <TableRow key={r.id} hover selected={picked.includes(r.id)} sx={{ cursor: 'pointer' }} onClick={() => setPicked(picked.includes(r.id) ? picked.filter((x) => x !== r.id) : [...picked, r.id])}>
            <TableCell padding="checkbox"><input type="checkbox" readOnly checked={picked.includes(r.id)} aria-label={`Select lot ${r.lotNumber}`} /></TableCell>
            <TableCell sx={{ fontFamily: mono }}>{r.lotNumber}</TableCell><TableCell>{r.revision}</TableCell><TableCell sx={{ fontFamily: mono }}>{r.locationCode}</TableCell><TableCell><StockBadge status={r.status} held={r.held} /></TableCell>
            <TableCell align="right">{qty(r.onHand)}</TableCell><TableCell align="right">{qty(r.reserved)}</TableCell></TableRow>))}
          {rows.length === 0 && <TableRow><TableCell colSpan={7}><Typography color="text.secondary" sx={{ py: 2, textAlign: 'center' }}>{part ? 'No lot-tracked stock for this part.' : 'Choose a part.'}</Typography></TableCell></TableRow>}</TableBody></Table></Paper>
      <Paper sx={{ p: 2.5, display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(4, 1fr)' }, gap: 2, alignItems: 'center' }}>
        <TextField select size="small" label="Merge into" value={f.target} onChange={(e) => setF({ ...f, target: e.target.value })}><MenuItem value="">A new lot</MenuItem>{[...new Map(chosen.map((c) => [c.lotId!, c.lotNumber])).entries()].map(([k, v]) => <MenuItem key={k} value={k}>{v}</MenuItem>)}</TextField>
        <TextField size="small" label="New lot number (blank = next)" value={f.newLot} disabled={!!f.target} onChange={(e) => setF({ ...f, newLot: e.target.value })} />
        <LocationSelect size="small" label="Put merged lot at" value={f.to} onChange={(v) => setF({ ...f, to: v })} siteId={siteId || undefined} />
        <TextField size="small" label="Reason" value={f.reason} onChange={(e) => setF({ ...f, reason: e.target.value })} />
        <Typography variant="body2" sx={{ gridColumn: { md: 'span 3' } }}>{chosen.length} lot(s) selected · {qty(chosen.reduce((a, c) => a + c.onHand, 0))} units</Typography>
        <Button variant="contained" disabled={chosen.length < 2 || !f.to} onClick={merge}>Merge {chosen.length} lots</Button>
      </Paper>
    </ModuleWorkspace>
  );
}
