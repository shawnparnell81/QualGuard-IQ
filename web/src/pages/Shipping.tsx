import { useState } from 'react';
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Chip, MenuItem, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { get, post, put } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, fmtDate, fmtDateTime, localDate } from '../components/Status';
import { Facts, Section, StepDialog, lookupLabel, useLookup, useSites } from '../components/Kit';
import { AttachmentsAndHistory } from '../components/RecordTabs';
import { mono } from '../theme';
import { LocationSelect, MetricTile, ScanField, carrierKinds, containerKinds, pickStatus, shipKinds, shipStatus, useCarriers, type Carrier } from '../warehouse/common';
import { Custody, FlowChart, RefLedger, SitePicker, qty, type Dash } from './Warehouse';

const mod = modules.find((m) => m.key === 'shipping')!;
const mnum = (x: { getValue: () => unknown }) => <span style={{ fontFamily: mono }}>{String(x.getValue() ?? '')}</span>;

interface Shipment { id: string; number: string; kind: number; status: number; customerId?: string; supplierId?: string; toSiteId?: string; shipTo?: string; customerPo?: string; promisedOn?: string; stagingLocationId: string; carrierId?: string;
  serviceLevel?: string; trackingNumber?: string; bolNumber?: string; grossWeight?: number; shippedAt?: string; deliveredOn?: string; notes?: string; siteId: string }
interface Line { id: string; lineNumber: number; partId: string; revision?: string; quantity: number; lotId?: string; quantityPicked: number; quantityVerified: number }
interface Pick { id: string; lineId: string; partId: string; lotId?: string; serialId?: string; locationId: string; quantity: number; quantityPicked: number; status: number; pickedAt?: string }
interface Packed { id: string; lineId: string; partNumber: string; lotNumber?: string; serialNumber?: string; quantity: number }
interface Container { id: string; number: string; kind: number; parentId?: string; weight?: number; verified: boolean }
interface Detail { shipment: Shipment; lines: { line: Line; partNumber: string; partName?: string; lotNumber?: string }[]; picks: { task: Pick; partNumber: string; lotNumber?: string; serialNumber?: string; locationCode: string }[];
  containers: { container: Container; items: Packed[] }[]; customer?: string; carrier?: string; trackingUrl?: string }
interface VerifyResult { verified: boolean; issues: string[]; lines: { lineId: string; lineNumber: number; partNumber: string; revision?: string; expected: number; verified: number }[] }

// ------------------------------------------------------------------ logistics overview
export function LogisticsOverview() {
  const nav = useNavigate();
  const [site, setSite] = useState('');
  const d = useQuery({ queryKey: ['wh-dash', site], queryFn: () => get<Dash>(`/api/warehouse/dashboard${site ? `?siteId=${site}` : ''}`) });
  const open = useQuery({ queryKey: ['shipments', 'open'], queryFn: () => get<Shipment[]>('/api/shipping/shipments') });
  const by = (s: number) => (open.data ?? []).filter((x) => x.status === s).length;
  const lanes: [string, number[]][] = [['To pick', [0, 1]], ['To pack', [2]], ['To verify', [3]], ['Ready to ship', [4]], ['In transit', [5]]];
  const reports: [string, string][] = [['logistics.outbound', 'Outbound shipments'], ['logistics.inbound', 'Inbound receipts'], ['logistics.carriers', 'Carrier performance'], ['logistics.transfers', 'Transfers'], ['inventory.count_variance', 'Count variance'], ['inventory.adjustments', 'Inventory adjustments'], ['warehouse.utilization', 'Warehouse utilization']];
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Overview' }]} title="Logistics overview" actions={<SitePicker value={site} onChange={setSite} allowAll />}>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(5, 1fr)' }, gap: 1.5, mb: 2 }}>
        {lanes.map(([label, st]) => <MetricTile key={label} label={label} value={st.reduce((a, s) => a + by(s), 0)} unit="shipments" onClick={() => nav(`/shipping/shipments?status=${st[st.length - 1]}`)} />)}
      </Box>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '1fr 1fr' }, gap: 2 }}>
        <Section title="Carrier on-time delivery (90 days)">
          <Table size="small"><TableHead><TableRow><TableCell>Carrier</TableCell><TableCell align="right">Shipments</TableCell><TableCell align="right">Delivered</TableCell><TableCell align="right">On time</TableCell><TableCell sx={{ width: 160 }} /></TableRow></TableHead>
            <TableBody>{(d.data?.carriers ?? []).map((c) => (
              <TableRow key={c.carrierId}><TableCell>{c.name}</TableCell><TableCell align="right">{c.shipments}</TableCell><TableCell align="right">{c.delivered}</TableCell>
                <TableCell align="right" sx={{ fontFamily: mono }}>{c.onTimePct != null ? `${c.onTimePct.toFixed(0)}%` : '—'} <Typography component="span" variant="caption" color="text.secondary">({c.onTime}/{c.withPromise})</Typography></TableCell>
                <TableCell><Box sx={{ height: 8, bgcolor: '#E6E1D6' }}><Box sx={{ height: 8, width: `${c.onTimePct ?? 0}%`, bgcolor: (c.onTimePct ?? 0) >= 95 ? '#2E6B5E' : '#B4842A' }} /></Box></TableCell></TableRow>))}
              {d.data && d.data.carriers.length === 0 && <TableRow><TableCell colSpan={5}><Typography color="text.secondary">No carrier shipments yet.</Typography></TableCell></TableRow>}</TableBody></Table>
        </Section>
        <Section title="Inbound vs outbound (30 days)"><FlowChart d={d.data} /></Section>
      </Box>
      <Section title="Logistics reports">
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(4, 1fr)' }, gap: 1 }}>{reports.map(([k, l]) => <Button key={k} variant="outlined" component={RouterLink} to={`/reports/r/${k}`} sx={{ justifyContent: 'flex-start' }}>{l}</Button>)}</Box>
      </Section>
    </ModuleWorkspace>
  );
}

// ------------------------------------------------------------------ shipments
export function ShipmentList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const status = p.get('status') ?? '';
  const custs = useLookup('customer'); const carriers = useCarriers();
  const rows = useQuery({ queryKey: ['shipments', status], queryFn: () => get<Shipment[]>(`/api/shipping/shipments${status ? `?status=${status}` : ''}`) });
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Shipments' }]} title="Shipments"
      tabs={[{ label: 'All', value: '' }, ...shipStatus.slice(0, 7).map((s, i) => ({ label: s, value: String(i) }))]} tab={status} onTab={(v) => setP(v ? { status: v } : {})}
      actions={can('shipping.edit') && <Button variant="contained" component={RouterLink} to="/shipping/shipments/new">New shipment</Button>}>
      <DataTable data={rows.data ?? []} onRowClick={(r) => nav(`/shipping/shipments/${r.id}`)} empty="No shipments."
        columns={[{ header: 'Shipment', accessorKey: 'number', cell: mnum }, { header: 'Type', accessorFn: (r) => shipKinds[r.kind] }, { header: 'Ship to', accessorFn: (r) => { const c = custs.data?.find((x) => x.id === r.customerId); return c ? lookupLabel(c) : r.shipTo ?? ''; } },
          { header: 'Customer PO', accessorKey: 'customerPo' }, { header: 'Promised', accessorKey: 'promisedOn', cell: (x) => (x.getValue() ? fmtDate(x.getValue()) : '') }, { header: 'Carrier', accessorFn: (r) => carriers.data?.find((c) => c.id === r.carrierId)?.name ?? '' },
          { header: 'Tracking', accessorKey: 'trackingNumber', cell: mnum }, { header: 'Status', accessorFn: (r) => shipStatus[r.status], cell: (x) => <StatusChip status={x.getValue()} /> }]} />
    </ModuleWorkspace>
  );
}

interface DraftLine { partId: string; revision: string; qty: string }
export function ShipmentNew() {
  const nav = useNavigate();
  const sites = useSites(); const custs = useLookup('customer'); const sups = useLookup('supplier'); const parts = useLookup('part'); const carriers = useCarriers();
  const [h, setH] = useState({ kind: '0', customerId: '', supplierId: '', toSiteId: '', shipTo: '', customerPo: '', promisedOn: '', stagingLocationId: '', carrierId: '', serviceLevel: '', notes: '', siteId: '' });
  const [lines, setLines] = useState<DraftLine[]>([{ partId: '', revision: '', qty: '' }]);
  const [err, setErr] = useState<string | null>(null);
  const siteId = h.siteId || sites.data?.find((s) => !s.isArchived)?.id || '';
  const set = (k: keyof typeof h) => (e: { target: { value: string } }) => setH({ ...h, [k]: e.target.value });
  const save = async () => {
    setErr(null);
    try {
      const r = await post<{ id: string }>('/api/shipping/shipments', { kind: Number(h.kind), customerId: h.customerId || null, supplierId: h.supplierId || null, toSiteId: h.toSiteId || null, shipTo: h.shipTo || null, customerPo: h.customerPo || null, promisedOn: h.promisedOn || null,
        stagingLocationId: h.stagingLocationId, carrierId: h.carrierId || null, serviceLevel: h.serviceLevel || null, notes: h.notes || null, siteId, departmentId: null,
        lines: lines.filter((l) => l.partId).map((l) => ({ partId: l.partId, revision: l.revision || null, quantity: Number(l.qty), lotId: null })) });
      nav(`/shipping/shipments/${r.id}`);
    } catch (e) { setErr((e as Error).message); }
  };
  const k = Number(h.kind);
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Shipments', to: '/shipping/shipments' }, { label: 'New' }]} title="New shipment">
      {err && <Alert severity="error" sx={{ mb: 2 }}>{err}</Alert>}
      <Paper sx={{ p: 2.5, mb: 2, display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(3, 1fr)' }, gap: 2 }}>
        <TextField select label="Type" value={h.kind} onChange={set('kind')}>{shipKinds.map((s, i) => <MenuItem key={s} value={String(i)}>{s}</MenuItem>)}</TextField>
        {(k === 0 || k === 3) && <TextField select label="Customer" value={h.customerId} onChange={set('customerId')}>{(custs.data ?? []).map((c) => <MenuItem key={c.id} value={c.id}>{lookupLabel(c)}</MenuItem>)}</TextField>}
        {k === 1 && <TextField select label="Supplier" value={h.supplierId} onChange={set('supplierId')}>{(sups.data ?? []).map((c) => <MenuItem key={c.id} value={c.id}>{lookupLabel(c)}</MenuItem>)}</TextField>}
        {k === 2 && <TextField select label="Destination site" value={h.toSiteId} onChange={set('toSiteId')}>{(sites.data ?? []).map((s) => <MenuItem key={s.id} value={s.id}>{s.name}</MenuItem>)}</TextField>}
        <TextField label="Customer PO" value={h.customerPo} onChange={set('customerPo')} />
        <TextField label="Ship-to address" multiline value={h.shipTo} onChange={set('shipTo')} />
        <TextField label="Promised date" type="date" value={h.promisedOn} onChange={set('promisedOn')} slotProps={{ inputLabel: { shrink: true } }} />
        <TextField select label="Ship from site" value={siteId} onChange={set('siteId')}>{(sites.data ?? []).filter((s) => !s.isArchived).map((s) => <MenuItem key={s.id} value={s.id}>{s.name}</MenuItem>)}</TextField>
        <LocationSelect label="Staging / shipping dock" value={h.stagingLocationId} onChange={(v) => setH({ ...h, stagingLocationId: v })} siteId={siteId || undefined} purposes={[6]} />
        <TextField select label="Carrier" value={h.carrierId} onChange={set('carrierId')}><MenuItem value="">Decide at ship</MenuItem>{(carriers.data ?? []).filter((c) => c.active).map((c) => <MenuItem key={c.id} value={c.id}>{c.name}</MenuItem>)}</TextField>
        <TextField label="Service level" value={h.serviceLevel} onChange={set('serviceLevel')} />
        <TextField label="Notes" value={h.notes} onChange={set('notes')} sx={{ gridColumn: { md: 'span 2' } }} />
      </Paper>
      <Section title="Lines" action={<Button onClick={() => setLines([...lines, { partId: '', revision: '', qty: '' }])}>Add line</Button>}>
        <Stack gap={1.5}>{lines.map((l, i) => (
          <Stack key={i} direction={{ xs: 'column', md: 'row' }} gap={1}>
            <TextField select size="small" label="Part" value={l.partId} sx={{ flex: 2 }} onChange={(e) => setLines(lines.map((x, j) => (j === i ? { ...x, partId: e.target.value } : x)))}>{(parts.data ?? []).map((x) => <MenuItem key={x.id} value={x.id}>{lookupLabel(x)}</MenuItem>)}</TextField>
            <TextField size="small" label="Rev" value={l.revision} onChange={(e) => setLines(lines.map((x, j) => (j === i ? { ...x, revision: e.target.value } : x)))} />
            <TextField size="small" label="Quantity" type="number" value={l.qty} onChange={(e) => setLines(lines.map((x, j) => (j === i ? { ...x, qty: e.target.value } : x)))} />
            <Button color="inherit" aria-label="Remove line" onClick={() => setLines(lines.filter((_, j) => j !== i))}>✕</Button>
          </Stack>))}</Stack>
      </Section>
      <Stack direction="row" gap={1} sx={{ mt: 2 }}><Button variant="contained" onClick={save} disabled={!h.stagingLocationId}>Create shipment</Button><Button onClick={() => nav('/shipping/shipments')}>Cancel</Button></Stack>
    </ModuleWorkspace>
  );
}

// ------------------------------------------------------------------ shipment detail
const stageRail = ['Draft', 'Picking', 'Picked', 'Packed', 'Verified', 'Shipped', 'Delivered'];
function StageRail({ status }: { status: number }) {
  return <Box sx={{ display: 'grid', gridTemplateColumns: `repeat(${stageRail.length}, 1fr)`, mb: 2, border: 1, borderColor: 'divider', bgcolor: 'background.paper' }}>
    {stageRail.map((s, i) => (
      <Box key={s} sx={{ py: 1, textAlign: 'center', borderRight: i < stageRail.length - 1 ? 1 : 0, borderColor: 'divider', bgcolor: status === 7 ? 'action.disabledBackground' : i < status ? '#DCE8E4' : i === status ? 'primary.main' : undefined, color: i === status && status !== 7 ? 'primary.contrastText' : 'text.primary' }}>
        <Typography variant="caption" sx={{ fontWeight: 700, letterSpacing: 0.5 }}>{i + 1}. {s.toUpperCase()}</Typography></Box>))}
  </Box>;
}

export function ShipmentDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const { can } = useAuth();
  const [tab, setTab] = useState('lines');
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const [dlg, setDlg] = useState<'ship' | 'delivered' | 'cancel' | null>(null);
  const d = useQuery({ queryKey: ['shipment', id], queryFn: () => get<Detail>(`/api/shipping/shipments/${id}`) });
  const carriers = useCarriers();
  const s = d.data?.shipment;
  if (!s) return <ModuleWorkspace module={mod} crumbs={[{ label: 'Shipments', to: '/shipping/shipments' }]} title="Loading…">{d.error ? <Alert severity="error">{(d.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const edit = can('shipping.edit');
  const refresh = () => { qc.invalidateQueries({ queryKey: ['shipment', id] }); qc.invalidateQueries({ queryKey: ['custody'] }); };
  const run = async (f: () => Promise<unknown>, ok: string) => { setMsg(null); try { await f(); setMsg({ ok: true, text: ok }); refresh(); } catch (e) { setMsg({ ok: false, text: (e as Error).message }); } };
  const live = s.status < 5;
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Shipments', to: '/shipping/shipments' }, { label: s.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{s.number}</span><Typography color="text.secondary">{d.data!.customer ?? s.shipTo}</Typography>{s.status === 7 && <Chip color="default" label="Cancelled" />}</Stack>}
      tabs={[{ label: 'Lines', value: 'lines' }, { label: 'Pick', value: 'pick' }, { label: 'Pack', value: 'pack' }, { label: 'Verify', value: 'verify' }, { label: 'Ship & documents', value: 'ship' }, { label: 'Custody', value: 'custody' }, { label: 'Ledger', value: 'ledger' }, { label: 'Files & history', value: 'files' }]} tab={tab} onTab={setTab}
      actions={edit && <Stack direction="row" gap={1}>
        {s.status === 0 && <Button variant="contained" onClick={() => run(() => post(`/api/shipping/shipments/${id}/pick-list`, {}), 'Pick list built from available stock (FIFO by lot).').then(() => setTab('pick'))}>Build pick list</Button>}
        {s.status === 4 && <Button variant="contained" onClick={() => setDlg('ship')}>Ship</Button>}
        {s.status === 5 && <Button variant="contained" onClick={() => setDlg('delivered')}>Mark delivered</Button>}
        {live && <Button color="inherit" onClick={() => setDlg('cancel')}>Cancel shipment</Button>}</Stack>}>
      <StageRail status={s.status} />
      {msg && <Alert severity={msg.ok ? 'success' : 'error'} sx={{ mb: 2 }} onClose={() => setMsg(null)}>{msg.text}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '1fr 280px' }, gap: 2 }}>
        <Box>
          {tab === 'lines' && <Paper><Table size="small"><TableHead><TableRow><TableCell>#</TableCell><TableCell>Part</TableCell><TableCell>Lot</TableCell><TableCell align="right">Ordered</TableCell><TableCell align="right">Picked</TableCell><TableCell align="right">Verified</TableCell></TableRow></TableHead>
            <TableBody>{d.data!.lines.map((l) => <TableRow key={l.line.id}><TableCell>{l.line.lineNumber}</TableCell><TableCell><b style={{ fontFamily: mono }}>{l.partNumber}</b>{l.line.revision ? ` rev ${l.line.revision}` : ''} <Typography component="span" variant="body2" color="text.secondary">{l.partName}</Typography></TableCell>
              <TableCell sx={{ fontFamily: mono }}>{l.lotNumber}</TableCell><TableCell align="right">{qty(l.line.quantity)}</TableCell><TableCell align="right">{qty(l.line.quantityPicked)}</TableCell><TableCell align="right">{qty(l.line.quantityVerified)}</TableCell></TableRow>)}</TableBody></Table></Paper>}
          {tab === 'pick' && <PickTab d={d.data!} edit={edit && live} run={run} />}
          {tab === 'pack' && <PackTab d={d.data!} edit={edit && live} run={run} />}
          {tab === 'verify' && <VerifyTab d={d.data!} edit={edit && live} onDone={refresh} />}
          {tab === 'ship' && <Section title="Documents & tracking"><Stack gap={2}>
            <Stack direction="row" gap={1} flexWrap="wrap">
              <Button variant="outlined" href={`/api/shipping/shipments/${id}/packing-list`} target="_blank">Packing list (PDF)</Button>
              <Button variant="outlined" href={`/api/shipping/shipments/${id}/bill-of-lading`} target="_blank">Bill of lading (PDF)</Button>
              {d.data!.trackingUrl && <Button variant="outlined" href={d.data!.trackingUrl} target="_blank" rel="noreferrer">Track with {d.data!.carrier}</Button>}
            </Stack>
            {s.status < 4 && <Typography variant="body2" color="text.secondary">The shipment can be shipped once every line is picked, packed and scan-verified.</Typography>}
          </Stack></Section>}
          {tab === 'custody' && <Custody url={`/api/shipping/shipments/${id}/custody`} />}
          {tab === 'ledger' && <RefLedger refType="Shipment" refId={s.id} />}
          {tab === 'files' && <AttachmentsAndHistory type="Shipment" id={id!} canEdit={edit} />}
        </Box>
        <Facts items={[['Type', shipKinds[s.kind]], ['Customer PO', s.customerPo], ['Promised', s.promisedOn ? fmtDate(s.promisedOn) : undefined], ['Carrier', d.data!.carrier], ['Service', s.serviceLevel], ['Tracking', s.trackingNumber], ['BOL', s.bolNumber],
          ['Gross weight', s.grossWeight != null ? `${qty(s.grossWeight)} kg` : undefined], ['Shipped', s.shippedAt ? fmtDateTime(s.shippedAt) : undefined], ['Delivered', s.deliveredOn ? fmtDate(s.deliveredOn) : undefined], ['Ship to', s.shipTo], ['Notes', s.notes]]} />
      </Box>
      <StepDialog open={dlg === 'ship'} title={`Ship ${s.number}`} confirm="Ship" onClose={() => setDlg(null)} intro="Shipping posts the picked stock out of inventory and assigns a bill of lading number."
        inputs={[{ name: 'carrierId', label: 'Carrier', type: 'select', initial: s.carrierId, options: (carriers.data ?? []).filter((c) => c.active).map((c) => [c.id, c.name]) }, { name: 'tracking', label: 'Tracking / PRO number' }, { name: 'service', label: 'Service level', initial: s.serviceLevel }, { name: 'weight', label: 'Gross weight (kg)' }]}
        onConfirm={async (v) => { await post(`/api/shipping/shipments/${id}/ship`, { carrierId: v.carrierId, trackingNumber: v.tracking || null, serviceLevel: v.service || null, grossWeight: v.weight ? Number(v.weight) : null }); refresh(); }} />
      <StepDialog open={dlg === 'delivered'} title="Confirm delivery" confirm="Record delivery" onClose={() => setDlg(null)} inputs={[{ name: 'on', label: 'Delivered on', type: 'date', initial: localDate() }]}
        onConfirm={async (v) => { await post(`/api/shipping/shipments/${id}/delivered`, { deliveredOn: v.on }); refresh(); }} />
      <StepDialog open={dlg === 'cancel'} danger title={`Cancel ${s.number}?`} confirm="Cancel shipment" onClose={() => setDlg(null)} intro="Open picks are released and reserved stock becomes available again." inputs={[]}
        onConfirm={async () => { await post(`/api/shipping/shipments/${id}/cancel`, {}); refresh(); }} />
    </ModuleWorkspace>
  );
}

type Run = (f: () => Promise<unknown>, ok: string) => Promise<void>;
function PickTab({ d, edit, run }: { d: Detail; edit: boolean; run: Run }) {
  const [loc, setLoc] = useState<Record<string, string>>({});
  const [item, setItem] = useState<Record<string, string>>({});
  if (!d.picks.length) return <Typography color="text.secondary">No pick list yet. Build it from the header.</Typography>;
  return <Stack gap={1.5}>{d.picks.map((p) => (
    <Paper key={p.task.id} variant="outlined" sx={{ p: 2, display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1.3fr 1fr 1fr 120px' }, gap: 1.5, alignItems: 'center', opacity: p.task.status === 1 ? 0.6 : 1 }}>
      <Box><Typography sx={{ fontFamily: mono, fontWeight: 700, fontSize: 18 }}>{p.locationCode}</Typography>
        <Typography variant="body2">{p.partNumber} · {p.serialNumber ?? p.lotNumber ?? 'no lot'} · <b>{qty(p.task.quantity)}</b></Typography>
        <Typography variant="caption" color="text.secondary">{pickStatus[p.task.status]}{p.task.pickedAt ? ` ${fmtDateTime(p.task.pickedAt)}` : ''}</Typography></Box>
      {p.task.status === 0 && edit ? <>
        <TextField size="small" label="Scan location" value={loc[p.task.id] ?? ''} onChange={(e) => setLoc({ ...loc, [p.task.id]: e.target.value })} sx={{ '& input': { fontFamily: mono } }} />
        <TextField size="small" label="Scan item (lot / serial)" value={item[p.task.id] ?? ''} onChange={(e) => setItem({ ...item, [p.task.id]: e.target.value })} sx={{ '& input': { fontFamily: mono } }} />
        <Button variant="contained" onClick={() => run(() => post(`/api/shipping/shipments/${d.shipment.id}/picks/${p.task.id}`, { locationCode: loc[p.task.id] || null, itemCode: item[p.task.id] || null, quantity: p.task.quantity }), `Picked ${p.partNumber} from ${p.locationCode}.`)}>Confirm pick</Button>
      </> : <Box sx={{ gridColumn: { md: 'span 3' } }}><StatusChip status={pickStatus[p.task.status]} /></Box>}
    </Paper>))}</Stack>;
}

function PackTab({ d, edit, run }: { d: Detail; edit: boolean; run: Run }) {
  const [kind, setKind] = useState('0'); const [parent, setParent] = useState(''); const [weight, setWeight] = useState('');
  const [take, setTake] = useState<Record<string, string>>({});
  // Packing works per picked lot / serial: what was picked of each, less what is already in a container.
  const items = d.containers.flatMap((c) => c.items);
  const groups = new Map<string, { key: string; lineId: string; lotId?: string; serialId?: string; partNumber: string; ident: string; picked: number }>();
  for (const p of d.picks.filter((x) => x.task.quantityPicked > 0)) {
    const key = `${p.task.lineId}|${p.task.lotId ?? ''}|${p.task.serialId ?? ''}`;
    const g = groups.get(key) ?? { key, lineId: p.task.lineId, lotId: p.task.lotId, serialId: p.task.serialId, partNumber: p.partNumber, ident: p.serialNumber ?? p.lotNumber ?? '', picked: 0 };
    g.picked += p.task.quantityPicked; groups.set(key, g);
  }
  const remaining = [...groups.values()].map((g) => ({ ...g, left: g.picked - items.filter((i) => i.lineId === g.lineId && (i.serialNumber ?? i.lotNumber ?? '') === g.ident).reduce((a, i) => a + i.quantity, 0) })).filter((g) => g.left > 0);
  const tree = (pid?: string, depth = 0): React.ReactNode => d.containers.filter((c) => (c.container.parentId ?? undefined) === pid).map((c) => (
    <Box key={c.container.id} sx={{ ml: depth * 3, mb: 1, borderLeft: 3, borderColor: c.container.verified ? '#2E6B5E' : 'divider', pl: 1.5 }}>
      <Typography sx={{ fontWeight: 600 }}><span style={{ fontFamily: mono }}>QG:CTN:{c.container.number}</span> · {containerKinds[c.container.kind]}{c.container.weight != null ? ` · ${qty(c.container.weight)} kg` : ''}{c.container.verified && <Chip size="small" color="success" label="verified" sx={{ ml: 1 }} />}</Typography>
      {c.items.map((i) => <Typography key={i.id} variant="body2" sx={{ fontFamily: mono }}>{i.partNumber} {i.serialNumber ?? i.lotNumber ?? ''} × {qty(i.quantity)}</Typography>)}
      {tree(c.container.id, depth + 1)}
    </Box>));
  return <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2 }}>
    <Section title="Packing tree">{d.containers.length ? tree(undefined) : <Typography color="text.secondary">Nothing packed yet.</Typography>}</Section>
    {edit && <Section title="Pack a container"><Stack gap={1.5}>
      <Stack direction="row" gap={1}><TextField select size="small" label="Container" value={kind} onChange={(e) => setKind(e.target.value)} sx={{ flex: 1 }}>{containerKinds.map((k, i) => <MenuItem key={k} value={String(i)}>{k}</MenuItem>)}</TextField>
        <TextField select size="small" label="Inside" value={parent} onChange={(e) => setParent(e.target.value)} sx={{ flex: 1 }}><MenuItem value="">Top level</MenuItem>{d.containers.map((c) => <MenuItem key={c.container.id} value={c.container.id}>{c.container.number}</MenuItem>)}</TextField>
        <TextField size="small" label="Weight kg" value={weight} onChange={(e) => setWeight(e.target.value)} sx={{ width: 110 }} /></Stack>
      {remaining.map((l) => <Stack key={l.key} direction="row" gap={1} alignItems="center"><Typography sx={{ flex: 1, fontFamily: mono }}>{l.partNumber} {l.ident} <Typography component="span" variant="caption" color="text.secondary">({qty(l.left)} picked, unpacked)</Typography></Typography>
        <TextField size="small" type="number" label="Qty" value={take[l.key] ?? ''} onChange={(e) => setTake({ ...take, [l.key]: e.target.value })} sx={{ width: 100 }} /></Stack>)}
      {remaining.length === 0 && <Typography variant="body2" color="text.secondary">Every picked unit is packed. Empty containers (pallets) can still be added to nest cartons.</Typography>}
      <Button variant="contained" onClick={() => run(async () => { await post(`/api/shipping/shipments/${d.shipment.id}/containers`, { kind: Number(kind), parentId: parent || null, weight: weight ? Number(weight) : null,
        items: remaining.filter((l) => Number(take[l.key]) > 0).map((l) => ({ lineId: l.lineId, lotId: l.lotId ?? null, serialId: l.serialId ?? null, quantity: Number(take[l.key]) })) }); setTake({}); setWeight(''); }, 'Container packed.')}>Pack</Button>
    </Stack></Section>}
  </Box>;
}

function VerifyTab({ d, edit, onDone }: { d: Detail; edit: boolean; onDone: () => void }) {
  const [scans, setScans] = useState<{ code: string; quantity?: number }[]>([]);
  const [res, setRes] = useState<VerifyResult | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const submit = async () => { setErr(null); try { const r = await post<VerifyResult>(`/api/shipping/shipments/${d.shipment.id}/verify`, { scans: scans.map((s) => ({ code: s.code, quantity: s.quantity ?? null })) }); setRes(r); if (r.verified) onDone(); } catch (e) { setErr((e as Error).message); } };
  return <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2 }}>
    <Section title="Scan cartons or items">
      {edit ? <Stack gap={1.5}>
        <ScanField label="Scan QG:CTN, QG:LOT or QG:SN" onScan={(c) => setScans([...scans, { code: c }])} autoFocus helper="Scanning a carton verifies everything packed in it." />
        <Stack gap={0.5}>{scans.map((s, i) => <Stack key={i} direction="row" justifyContent="space-between" sx={{ fontFamily: mono, borderBottom: 1, borderColor: 'divider', py: 0.5 }}><span>{s.code}</span><Button size="small" color="inherit" onClick={() => setScans(scans.filter((_, j) => j !== i))}>remove</Button></Stack>)}</Stack>
        <Button variant="contained" disabled={!scans.length} onClick={submit}>Verify {scans.length} scan(s)</Button>
        {err && <Alert severity="error">{err}</Alert>}
      </Stack> : <Typography color="text.secondary">Verification is closed for this shipment.</Typography>}
    </Section>
    <Section title="Result">
      {res ? <Stack gap={1}><Alert severity={res.verified ? 'success' : 'warning'}>{res.verified ? 'All lines verified. The shipment is ready to ship.' : 'Not yet verified.'}</Alert>
        {res.issues.map((x, i) => <Typography key={i} color="error" variant="body2">• {x}</Typography>)}
        <Table size="small"><TableBody>{res.lines.map((l) => <TableRow key={l.lineId}><TableCell>{l.lineNumber}. {l.partNumber}</TableCell><TableCell align="right" sx={{ color: l.verified === l.expected ? 'success.main' : 'error.main', fontWeight: 700 }}>{qty(l.verified)} / {qty(l.expected)}</TableCell></TableRow>)}</TableBody></Table></Stack>
        : <Table size="small"><TableBody>{d.lines.map((l) => <TableRow key={l.line.id}><TableCell>{l.line.lineNumber}. {l.partNumber}</TableCell><TableCell align="right">{qty(l.line.quantityVerified)} / {qty(l.line.quantity)}</TableCell></TableRow>)}</TableBody></Table>}
    </Section>
  </Box>;
}

// ------------------------------------------------------------------ carriers
export function CarrierList() {
  const { can } = useAuth();
  const qc = useQueryClient();
  const carriers = useCarriers();
  const [dlg, setDlg] = useState<Carrier | 'new' | null>(null);
  const c = dlg && dlg !== 'new' ? dlg : undefined;
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Carriers' }]} title="Carriers" actions={can('shipping.edit') && <Button variant="contained" onClick={() => setDlg('new')}>Add carrier</Button>}>
      <DataTable data={carriers.data ?? []} onRowClick={can('shipping.edit') ? (r) => setDlg(r) : undefined} empty="No carriers yet."
        columns={[{ header: 'Code', accessorKey: 'code', cell: mnum }, { header: 'Name', accessorKey: 'name' }, { header: 'Mode', accessorFn: (r) => carrierKinds[r.kind] }, { header: 'SCAC', accessorKey: 'scac', cell: mnum },
          { header: 'Tracking link', accessorKey: 'trackingUrl', cell: (x) => <Typography variant="caption" sx={{ fontFamily: mono }}>{String(x.getValue() ?? '')}</Typography> }, { header: 'Active', accessorFn: (r) => (r.active ? 'Yes' : 'No') }]} />
      <Typography variant="caption" color="text.secondary" sx={{ mt: 1, display: 'block' }}>Use {'{tracking}'} in the tracking link where the tracking number goes.</Typography>
      <StepDialog open={!!dlg} title={c ? `Edit ${c.name}` : 'Add carrier'} confirm="Save" onClose={() => setDlg(null)}
        inputs={[{ name: 'code', label: 'Code', initial: c?.code }, { name: 'name', label: 'Name', initial: c?.name }, { name: 'kind', label: 'Mode', type: 'select', initial: String(c?.kind ?? 0), options: carrierKinds.map((k, i) => [i, k]) },
          { name: 'scac', label: 'SCAC', initial: c?.scac }, { name: 'url', label: 'Tracking link', initial: c?.trackingUrl }, { name: 'active', label: 'Active', type: 'checkbox', initial: String(c?.active ?? true) }]}
        onConfirm={async (v) => { const body = { code: v.code, name: v.name, kind: Number(v.kind), scac: v.scac || null, trackingUrl: v.url || null, active: v.active !== 'false' };
          if (c) await put(`/api/shipping/carriers/${c.id}`, body); else await post('/api/shipping/carriers', body); qc.invalidateQueries({ queryKey: ['carriers'] }); }} />
    </ModuleWorkspace>
  );
}
