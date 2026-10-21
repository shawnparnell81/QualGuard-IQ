import { setupOptions } from '../components/SetupLink';
import { useEffect, useRef, useState } from 'react';
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Chip, MenuItem, Paper, Stack, Table, TableBody, TableCell, TableRow, TextField, ToggleButton, ToggleButtonGroup, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { get, post } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, fmtDate, fmtDateTime, localDate } from '../components/Status';
import { Section, StepDialog, lookupLabel, useLookup, useSites } from '../components/Kit';
import { AttachmentsAndHistory } from '../components/RecordTabs';
import { mono } from '../theme';
import { tones } from '../brand';

const mod = modules.find((m) => m.key === 'trace')!;
export const lotKind = ['Raw material', 'Component', 'Finished product', 'Supplier lot', 'Customer lot', 'WIP'];
export const lotStatus = ['Active', 'OnHold', 'Quarantined', 'Consumed', 'Shipped', 'Scrapped', 'Merged'];
export const serialStatus = ['Created', 'InStock', 'Assigned', 'Shipped', 'Returned', 'Scrapped', 'InService'];
export const traceKind = ['Receipt', 'Consumption', 'Production', 'Inspection', 'Shipment', 'Return', 'Split', 'Merge', 'Transfer', 'Assembly', 'Service', 'Scrap'];
const mnum = (x: { getValue: () => unknown }) => <span style={{ fontFamily: mono }}>{String(x.getValue() ?? '')}</span>;

interface Lot { id: string; number: string; kind: number; partId: string; revision?: string; supplierId?: string; supplierLotNumber?: string; customerId?: string; quantity: number; unitOfMeasure?: string; manufacturedOn?: string; expiresOn?: string; status: number; purchaseOrder?: string; source: string; siteId: string; departmentId?: string }
interface Serial { id: string; serialNumber: string; partId: string; revision?: string; lotId?: string; manufacturedOn?: string; status: number; customerId?: string; shippedOn?: string; warrantyEndsOn?: string; siteId: string }
interface GNode { kind: string; id: string; number: string; partNumber?: string; revision?: string; status: string; depth: number; direction: string; quantity?: number; via?: string }
interface Party { id: string; code: string; name: string; quantity: number; lots: string[]; serials: string[] }
interface TEvent { id: string; kind: number; occurredAt: string; from?: string; to?: string; quantity?: number; workOrder?: string; ref?: string; party?: string; notes?: string }
interface Genealogy { root: GNode; backward: GNode[]; forward: GNode[]; events: TEvent[]; suppliers: Party[]; customers: Party[] }
interface Hit { type: string; id: string; number: string; title: string; status: string; url: string; group: string }

/** Trace search across products, parts, lots, serials, suppliers and customers, then every quality record they touch. */
export function TraceSearch() {
  const nav = useNavigate();
  const [p, setP] = useSearchParams();
  const [q, setQ] = useState(p.get('q') ?? '');
  const term = p.get('q') ?? '';
  const r = useQuery({ queryKey: ['trace-search', term], queryFn: () => get<{ matches: Hit[]; related: Hit[] }>(`/api/trace/search?q=${encodeURIComponent(term)}`), enabled: term.length > 0 });
  const groups = Object.entries((r.data?.related ?? []).reduce<Record<string, Hit[]>>((a, h) => { (a[h.group] ??= []).push(h); return a; }, {}));
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Trace search' }]} title="Trace search">
      <Paper component="form" onSubmit={(e) => { e.preventDefault(); setP(q ? { q } : {}); }} sx={{ p: 2, mb: 2, display: 'flex', gap: 1 }}>
        <TextField fullWidth autoFocus placeholder="Part, product, lot, batch, serial, supplier or customer" value={q} onChange={(e) => setQ(e.target.value)} />
        <Button type="submit" variant="contained">Trace</Button>
      </Paper>
      {r.error && <Alert severity="error">{(r.error as Error).message}</Alert>}
      {r.data && <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', lg: '340px 1fr' }, gap: 2 }}>
        <Section title={`Matches (${r.data.matches.length})`}>
          <Stack gap={1}>{r.data.matches.map((h) => <Paper key={h.type + h.id} variant="outlined" onClick={() => nav(h.url)} sx={{ p: 1.25, cursor: 'pointer', '&:hover': { borderColor: 'primary.main' } }}>
            <Stack direction="row" justifyContent="space-between"><Typography sx={{ fontFamily: mono, fontWeight: 600 }}>{h.number}</Typography><Chip size="small" label={h.type === 'SerialUnit' ? 'Serial' : h.type} /></Stack>
            <Typography variant="body2" color="text.secondary">{h.title}</Typography></Paper>)}
            {r.data.matches.length === 0 && <Typography color="text.secondary">Nothing matches “{term}”.</Typography>}</Stack>
        </Section>
        <Stack gap={2}>{groups.map(([g, hits]) => <Section key={g} title={`${g} (${hits.length})`}>
          <Table size="small"><TableBody>{hits.map((h) => <TableRow key={h.type + h.id} hover sx={{ cursor: 'pointer' }} onClick={() => nav(h.url)}>
            <TableCell sx={{ fontFamily: mono, width: 160 }}>{h.number}</TableCell><TableCell>{h.title}</TableCell><TableCell sx={{ width: 160 }}><StatusChip status={h.status} /></TableCell></TableRow>)}</TableBody></Table>
        </Section>)}
          {groups.length === 0 && <Typography color="text.secondary">No related quality records.</Typography>}</Stack>
      </Box>}
    </ModuleWorkspace>
  );
}

export function LotList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [q, setQ] = useState('');
  const rows = useQuery({ queryKey: ['lots', q], queryFn: () => get<Lot[]>(`/api/trace/lots${q ? `?q=${encodeURIComponent(q)}` : ''}`) });
  const parts = useLookup('part');
  const pn = (id: string) => parts.data?.find((p) => p.id === id)?.partNumber ?? '';
  const cols: ColumnDef<Lot, any>[] = [{ header: 'Lot', accessorKey: 'number', cell: mnum }, { header: 'Kind', accessorFn: (r) => lotKind[r.kind] }, { header: 'Part', accessorFn: (r) => `${pn(r.partId)}${r.revision ? ` rev ${r.revision}` : ''}` },
    { header: 'Qty', accessorFn: (r) => `${r.quantity} ${r.unitOfMeasure ?? ''}` }, { header: 'Supplier lot', accessorKey: 'supplierLotNumber' }, { header: 'Expires', accessorKey: 'expiresOn', cell: (x) => fmtDate(x.getValue()) },
    { header: 'Status', accessorFn: (r) => lotStatus[r.status], cell: (x) => <StatusChip status={x.getValue()} /> }, { header: 'Source', accessorKey: 'source' }];
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Lots & batches' }]} title="Lots & batches"
      actions={<Stack direction="row" gap={1}><TextField size="small" placeholder="Lot or supplier lot" value={q} onChange={(e) => setQ(e.target.value)} />{can('trace.edit') && <Button variant="contained" component={RouterLink} to="/trace/lots/new">New lot</Button>}</Stack>}>
      <DataTable data={rows.data ?? []} columns={cols} onRowClick={(r) => nav(`/trace/lots/${r.id}`)} empty="No lots yet." />
    </ModuleWorkspace>
  );
}

export function SerialList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [q, setQ] = useState('');
  const rows = useQuery({ queryKey: ['serials', q], queryFn: () => get<Serial[]>(`/api/trace/serials${q ? `?q=${encodeURIComponent(q)}` : ''}`) });
  const parts = useLookup('part');
  const pn = (id: string) => parts.data?.find((p) => p.id === id)?.partNumber ?? '';
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Serial numbers' }]} title="Serial numbers"
      actions={<Stack direction="row" gap={1}><TextField size="small" placeholder="Serial" value={q} onChange={(e) => setQ(e.target.value)} />{can('trace.edit') && <Button variant="contained" component={RouterLink} to="/trace/serials/new">New serial</Button>}</Stack>}>
      <DataTable data={rows.data ?? []} onRowClick={(r) => nav(`/trace/serials/${r.id}`)} empty="No serial numbers yet."
        columns={[{ header: 'Serial', accessorKey: 'serialNumber', cell: mnum }, { header: 'Part', accessorFn: (r) => `${pn(r.partId)}${r.revision ? ` rev ${r.revision}` : ''}` }, { header: 'Built', accessorKey: 'manufacturedOn', cell: (x) => fmtDate(x.getValue()) },
          { header: 'Shipped', accessorKey: 'shippedOn', cell: (x) => fmtDate(x.getValue()) }, { header: 'Warranty ends', accessorKey: 'warrantyEndsOn', cell: (x) => fmtDate(x.getValue()) }, { header: 'Status', accessorFn: (r) => serialStatus[r.status], cell: (x) => <StatusChip status={x.getValue()} /> }]} />
    </ModuleWorkspace>
  );
}

function SiteSelect({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  const sites = useSites();
  return <TextField select label="Site" value={value} onChange={(e) => onChange(e.target.value)}>{(sites.data ?? []).filter((s) => !s.isArchived).map((s) => <MenuItem key={s.id} value={s.id}>{s.code} — {s.name}</MenuItem>)}{setupOptions('site', sites.data)}</TextField>;
}

export function LotNew() {
  const nav = useNavigate();
  const parts = useLookup('part'); const sups = useLookup('supplier');
  const [v, setV] = useState({ number: '', kind: '0', partId: '', revision: '', supplierId: '', supplierLotNumber: '', quantity: '1', unitOfMeasure: 'EA', manufacturedOn: localDate(), expiresOn: '', purchaseOrder: '', siteId: '' });
  const [error, setError] = useState<string | null>(null);
  const set = (k: string) => (e: { target: { value: string } }) => setV({ ...v, [k]: e.target.value });
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Lots & batches', to: '/trace/lots' }, { label: 'New' }]} title="New lot">
      <Paper sx={{ p: 3, maxWidth: 820, display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: '1fr 1fr' }, gap: 2 }}>
        {error && <Alert severity="error" sx={{ gridColumn: '1 / -1' }}>{error}</Alert>}
        <TextField label="Lot number (blank = next)" value={v.number} onChange={set('number')} />
        <TextField select label="Kind" value={v.kind} onChange={set('kind')}>{lotKind.map((k, i) => <MenuItem key={k} value={String(i)}>{k}</MenuItem>)}</TextField>
        <TextField select label="Part" value={v.partId} onChange={set('partId')}>{(parts.data ?? []).map((p) => <MenuItem key={p.id} value={p.id}>{lookupLabel(p)}</MenuItem>)}{setupOptions('part', parts.data)}</TextField>
        <TextField label="Revision" value={v.revision} onChange={set('revision')} />
        <TextField label="Quantity" type="number" value={v.quantity} onChange={set('quantity')} />
        <TextField label="Unit" value={v.unitOfMeasure} onChange={set('unitOfMeasure')} />
        <TextField select label="Supplier" value={v.supplierId} onChange={set('supplierId')}><MenuItem value="">None</MenuItem>{(sups.data ?? []).map((s) => <MenuItem key={s.id} value={s.id}>{lookupLabel(s)}</MenuItem>)}{setupOptions('supplier', sups.data)}</TextField>
        <TextField label="Supplier lot" value={v.supplierLotNumber} onChange={set('supplierLotNumber')} />
        <TextField label="Made on" type="date" value={v.manufacturedOn} onChange={set('manufacturedOn')} slotProps={{ inputLabel: { shrink: true } }} />
        <TextField label="Expires on" type="date" value={v.expiresOn} onChange={set('expiresOn')} slotProps={{ inputLabel: { shrink: true } }} />
        <TextField label="Purchase order" value={v.purchaseOrder} onChange={set('purchaseOrder')} />
        <SiteSelect value={v.siteId} onChange={(s) => setV({ ...v, siteId: s })} />
        <Stack direction="row" gap={1} sx={{ gridColumn: '1 / -1' }}>
          <Button variant="contained" onClick={async () => {
            setError(null);
            const o = Object.fromEntries(Object.entries(v).map(([k, x]) => [k, x === '' ? null : x]));
            try { const r = await post<{ id: string }>('/api/trace/lots', { ...o, kind: Number(v.kind), quantity: Number(v.quantity), customerId: null, customerLotNumber: null, workOrderId: null, departmentId: null }); nav(`/trace/lots/${r.id}`); }
            catch (e) { setError((e as Error).message); }
          }}>Create lot</Button>
          <Button onClick={() => nav('/trace/lots')}>Cancel</Button>
        </Stack>
      </Paper>
    </ModuleWorkspace>
  );
}

export function SerialNew() {
  const nav = useNavigate();
  const parts = useLookup('part');
  const lots = useQuery({ queryKey: ['lots', ''], queryFn: () => get<Lot[]>('/api/trace/lots') });
  const [v, setV] = useState({ serialNumber: '', partId: '', revision: '', lotId: '', manufacturedOn: localDate(), warrantyMonths: '12', siteId: '' });
  const [error, setError] = useState<string | null>(null);
  const set = (k: string) => (e: { target: { value: string } }) => setV({ ...v, [k]: e.target.value });
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Serial numbers', to: '/trace/serials' }, { label: 'New' }]} title="New serial number">
      <Paper sx={{ p: 3, maxWidth: 820, display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: '1fr 1fr' }, gap: 2 }}>
        {error && <Alert severity="error" sx={{ gridColumn: '1 / -1' }}>{error}</Alert>}
        <TextField label="Serial number" value={v.serialNumber} onChange={set('serialNumber')} />
        <TextField select label="Part" value={v.partId} onChange={set('partId')}>{(parts.data ?? []).map((p) => <MenuItem key={p.id} value={p.id}>{lookupLabel(p)}</MenuItem>)}{setupOptions('part', parts.data)}</TextField>
        <TextField label="Revision" value={v.revision} onChange={set('revision')} />
        <TextField select label="Lot of origin" value={v.lotId} onChange={set('lotId')}><MenuItem value="">None</MenuItem>{(lots.data ?? []).map((l) => <MenuItem key={l.id} value={l.id}>{l.number}</MenuItem>)}</TextField>
        <TextField label="Built on" type="date" value={v.manufacturedOn} onChange={set('manufacturedOn')} slotProps={{ inputLabel: { shrink: true } }} />
        <TextField label="Warranty (months)" type="number" value={v.warrantyMonths} onChange={set('warrantyMonths')} />
        <SiteSelect value={v.siteId} onChange={(s) => setV({ ...v, siteId: s })} />
        <Stack direction="row" gap={1} sx={{ gridColumn: '1 / -1' }}>
          <Button variant="contained" onClick={async () => {
            setError(null);
            try { const r = await post<{ id: string }>('/api/trace/serials', { ...v, revision: v.revision || null, lotId: v.lotId || null, productId: null, workOrderId: null, warrantyMonths: v.warrantyMonths ? Number(v.warrantyMonths) : null, departmentId: null }); nav(`/trace/serials/${r.id}`); }
            catch (e) { setError((e as Error).message); }
          }}>Create serial</Button>
          <Button onClick={() => nav('/trace/serials')}>Cancel</Button>
        </Stack>
      </Paper>
    </ModuleWorkspace>
  );
}

const nodeTone = (s: string) => (['OnHold', 'Quarantined', 'Returned', 'Scrapped'].includes(s) ? tones.bad : s === 'Shipped' ? tones.info : tones.primary);

function NodeCard({ n, onOpen, root }: { n: GNode; onOpen: () => void; root?: boolean }) {
  return (
    <Paper variant="outlined" onClick={onOpen} sx={{ p: 1, minWidth: 150, cursor: 'pointer', borderLeft: 4, borderLeftColor: nodeTone(n.status), bgcolor: root ? 'action.selected' : undefined, '&:hover': { boxShadow: 1 } }}>
      <Typography variant="caption" color="text.secondary">{n.kind}{n.via ? ` · ${n.via}` : ''}{n.quantity != null ? ` · ${n.quantity}` : ''}</Typography>
      <Typography sx={{ fontFamily: mono, fontWeight: 600, fontSize: 13 }}>{n.number}</Typography>
      <Typography variant="caption" display="block">{n.partNumber}{n.revision ? ` rev ${n.revision}` : ''}</Typography>
      <Typography variant="caption" sx={{ color: nodeTone(n.status) }}>{n.status}</Typography>
    </Paper>
  );
}

/** Left-to-right genealogy: suppliers, input generations, the record, output generations, customers. */
export function GenealogyMap({ g }: { g: Genealogy }) {
  const nav = useNavigate();
  const open = (n: GNode) => nav(`/trace/${n.kind === 'Lot' ? 'lots' : 'serials'}/${n.id}`);
  const back = Math.max(0, ...g.backward.map((x) => x.depth));
  const fwd = Math.max(0, ...g.forward.map((x) => x.depth));
  const col = (title: string, body: React.ReactNode, tone?: string) => <Stack gap={1} sx={{ minWidth: 160 }}><Typography variant="overline" sx={{ color: tone ?? 'text.secondary' }}>{title}</Typography>{body}</Stack>;
  const arrow = <Box sx={{ alignSelf: 'center', color: 'text.secondary', fontSize: 22, px: 0.5 }}>→</Box>;
  const parties = (ps: Party[]) => ps.map((p) => <Paper key={p.id} variant="outlined" sx={{ p: 1, bgcolor: 'background.default' }}><Typography sx={{ fontWeight: 600, fontSize: 13 }}>{p.code}</Typography><Typography variant="caption" display="block">{p.name}</Typography><Typography variant="caption" color="text.secondary">{p.quantity} · {[...p.lots, ...p.serials].slice(0, 3).join(', ')}</Typography></Paper>);
  const cols: React.ReactNode[] = [];
  cols.push(col('Suppliers', g.suppliers.length ? parties(g.suppliers) : <Typography variant="caption" color="text.secondary">None recorded</Typography>, tones.slate));
  for (let d = back; d >= 1; d--) { cols.push(arrow); cols.push(col(`Inputs −${d}`, g.backward.filter((x) => x.depth === d).map((x) => <NodeCard key={x.id} n={x} onOpen={() => open(x)} />))); }
  cols.push(arrow); cols.push(col('This record', <NodeCard n={g.root} root onOpen={() => {}} />, tones.primary));
  for (let d = 1; d <= fwd; d++) { cols.push(arrow); cols.push(col(`Outputs +${d}`, g.forward.filter((x) => x.depth === d).map((x) => <NodeCard key={x.id} n={x} onOpen={() => open(x)} />))); }
  cols.push(arrow); cols.push(col('Customers', g.customers.length ? parties(g.customers) : <Typography variant="caption" color="text.secondary">Not shipped</Typography>, tones.accent));
  return <Paper sx={{ p: 2, overflowX: 'auto' }}><Stack direction="row" gap={1} alignItems="flex-start">{cols.map((c, i) => <Box key={i}>{c}</Box>)}</Stack></Paper>;
}

export function TraceRecord({ kind }: { kind: 'lot' | 'serial' }) {
  const { id } = useParams();
  const { can } = useAuth();
  const qc = useQueryClient();
  const [tab, setTab] = useState('genealogy');
  const [dlg, setDlg] = useState(false);
  const g = useQuery({ queryKey: ['genealogy', kind, id], queryFn: () => get<Genealogy>(`/api/trace/${kind}/${id}/genealogy`) });
  const lots = useQuery({ queryKey: ['lots', ''], queryFn: () => get<Lot[]>('/api/trace/lots'), enabled: dlg });
  const sers = useQuery({ queryKey: ['serials', ''], queryFn: () => get<Serial[]>('/api/trace/serials'), enabled: dlg });
  const custs = useLookup('customer', dlg);
  const list = kind === 'lot' ? '/trace/lots' : '/trace/serials';
  const r = g.data?.root;
  if (!r) return <ModuleWorkspace module={mod} crumbs={[{ label: kind === 'lot' ? 'Lots' : 'Serials', to: list }]} title="Loading…">{g.error ? <Alert severity="error">{(g.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const code = `QG:${kind === 'lot' ? 'LOT' : 'SN'}:${r.number}`;
  const type = kind === 'lot' ? 'Lot' : 'SerialUnit';
  const targets: [string, string][] = [...(lots.data ?? []).filter((l) => l.id !== id).map((l) => [`lot:${l.id}`, `Lot ${l.number}`] as [string, string]), ...(sers.data ?? []).filter((s) => s.id !== id).map((s) => [`serial:${s.id}`, `Serial ${s.serialNumber}`] as [string, string])];
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: kind === 'lot' ? 'Lots & batches' : 'Serial numbers', to: list }, { label: r.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center" sx={{ flexWrap: 'wrap', rowGap: 0.5 }}><span style={{ fontFamily: mono }}>{r.number}</span><Typography color="text.secondary">{r.partNumber}{r.revision ? ` rev ${r.revision}` : ''}</Typography><StatusChip status={r.status} /></Stack>}
      tabs={[{ label: 'Genealogy', value: 'genealogy' }, { label: 'History', value: 'events' }, { label: 'Label', value: 'label' }, { label: 'Files & links', value: 'files' }]} tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1}>
        <Button component={RouterLink} to={`/trace?q=${encodeURIComponent(r.number)}`}>Trace search</Button>
        {can('trace.edit') && <Button variant="contained" onClick={() => setDlg(true)}>Record movement</Button>}</Stack>}>
      {tab === 'genealogy' && <GenealogyMap g={g.data!} />}
      {tab === 'events' && <DataTable data={g.data!.events} empty="No events."
        columns={[{ header: 'When', accessorKey: 'occurredAt', cell: (x) => fmtDateTime(x.getValue()) }, { header: 'Event', accessorFn: (e) => traceKind[e.kind] }, { header: 'From', accessorKey: 'from', cell: mnum }, { header: 'To', accessorKey: 'to', cell: mnum },
          { header: 'Qty', accessorKey: 'quantity' }, { header: 'Work order', accessorKey: 'workOrder' }, { header: 'Reference', accessorKey: 'ref' }, { header: 'Party', accessorKey: 'party' }, { header: 'Notes', accessorKey: 'notes' }]} />}
      {tab === 'label' && <LabelPreview initial={code} />}
      {tab === 'files' && <AttachmentsAndHistory type={type} id={id!} canEdit={can('trace.edit')} />}
      <StepDialog open={dlg} title="Record a movement" confirm="Record" onClose={() => setDlg(false)}
        intro="Consumption and assembly link this record (input) to the record it goes into. Shipments name the customer. A hold or scrap changes the status."
        inputs={[{ name: 'kind', label: 'Event', type: 'select', initial: '1', options: traceKind.map((k, i) => [i, k]) }, { name: 'target', label: 'Goes into (consumption / assembly / transfer)', type: 'select', initial: '', options: [['', 'None'], ...targets] },
          { name: 'customerId', label: 'Customer (shipment)', type: 'select', setup: 'customer', initial: '', options: [['', 'None'], ...(custs.data ?? []).map((c) => [c.id, lookupLabel(c)] as [string, string])] }, { name: 'quantity', label: 'Quantity', initial: '1' }, { name: 'ref', label: 'Reference (shipment, PO, order)' }, { name: 'notes', label: 'Notes', type: 'multiline' }]}
        onConfirm={async (v) => {
          const [tk, tid] = v.target ? v.target.split(':') : [null, null];
          await post('/api/trace/events', { kind: Number(v.kind), fromLotId: kind === 'lot' ? id : null, fromSerialId: kind === 'serial' ? id : null, toLotId: tk === 'lot' ? tid : null, toSerialId: tk === 'serial' ? tid : null,
            quantity: v.quantity ? Number(v.quantity) : null, workOrderId: null, operationSequence: null, supplierId: null, customerId: v.customerId || null, refType: null, refId: null, refNumber: v.ref || null, notes: v.notes || null, occurredAt: null });
          qc.invalidateQueries({ queryKey: ['genealogy'] });
        }} />
    </ModuleWorkspace>
  );
}
export const LotDetail = () => <TraceRecord kind="lot" />;
export const SerialDetail = () => <TraceRecord kind="serial" />;

const symbologies = ['Code128', 'Code39', 'DataMatrix', 'Qr'];
function LabelPreview({ initial }: { initial: string }) {
  const [sym, setSym] = useState('Code128');
  const [content, setContent] = useState(initial);
  const [ok, setOk] = useState(true);
  const src = `/api/trace/labels?symbology=${sym}&content=${encodeURIComponent(content)}`;
  return (
    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: '320px 1fr' }, gap: 2 }}>
      <Paper sx={{ p: 2 }}>
        <Stack gap={2}>
          <ToggleButtonGroup exclusive size="small" value={sym} onChange={(_, s) => s && (setSym(s), setOk(true))}>{symbologies.map((s) => <ToggleButton key={s} value={s}>{s === 'Qr' ? 'QR' : s === 'DataMatrix' ? 'Data Matrix' : s.replace('Code', 'Code ')}</ToggleButton>)}</ToggleButtonGroup>
          <TextField label="Content" value={content} onChange={(e) => { setContent(e.target.value); setOk(true); }} helperText="QG:LOT:…, QG:SN:…, QG:WO:…, QG:PO:… or GS1 (10)/(21)" />
          <Button variant="outlined" onClick={() => { const w = window.open(''); w?.document.write(`<img src="${src}" style="width:320px"/><div style="font-family:monospace">${content.replace(/</g, '&lt;')}</div><script>setTimeout(()=>print(),300)</script>`); }}>Print label</Button>
        </Stack>
      </Paper>
      <Paper sx={{ p: 3, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', minHeight: 220, bgcolor: '#fff' }}>
        {content && ok ? <img alt="label" src={src} onError={() => setOk(false)} style={{ maxWidth: '100%', width: sym === 'Qr' || sym === 'DataMatrix' ? 180 : 360 }} /> : <Alert severity="warning">This content cannot be encoded in {sym} (Code 39 takes A–Z, 0–9 and - . $ / + % space).</Alert>}
        <Typography sx={{ fontFamily: mono, mt: 1 }}>{content}</Typography>
      </Paper>
    </Box>
  );
}

export function LabelsPage() {
  return <ModuleWorkspace module={mod} crumbs={[{ label: 'Labels' }]} title="Barcode & QR labels"><LabelPreview initial="QG:LOT:" /></ModuleWorkspace>;
}

interface ScanResult { raw: string; kind: string; type?: string; id?: string; number?: string; url?: string; detail?: string; candidates?: ScanResult[] }

/** Scanner page: a keyboard-wedge field that is always focused, plus the device camera through ZXing. */
export function ScanPage() {
  const nav = useNavigate();
  const [code, setCode] = useState('');
  const [hist, setHist] = useState<ScanResult[]>([]);
  const [camera, setCamera] = useState(false);
  const [camErr, setCamErr] = useState<string | null>(null);
  const video = useRef<HTMLVideoElement>(null);
  const input = useRef<HTMLInputElement>(null);
  const resolve = async (c: string) => {
    if (!c.trim()) return;
    try { const r = await post<ScanResult>('/api/trace/scan', { code: c.trim() }); setHist((h) => [r, ...h].slice(0, 20)); }
    catch (e) { setHist((h) => [{ raw: c, kind: 'Error', detail: (e as Error).message }, ...h]); }
    setCode(''); input.current?.focus();
  };
  useEffect(() => {
    if (!camera) return;
    let stop: (() => void) | undefined; let last = '';
    (async () => {
      try {
        const { BrowserMultiFormatReader } = await import('@zxing/browser');
        const reader = new BrowserMultiFormatReader();
        const controls = await reader.decodeFromVideoDevice(undefined, video.current!, (res) => { const t = res?.getText(); if (t && t !== last) { last = t; resolve(t); } });
        stop = () => controls.stop();
      } catch (e) { setCamErr((e as Error).message || 'Camera not available'); setCamera(false); }
    })();
    return () => stop?.();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [camera]);
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Scan' }]} title="Scan a code">
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: '1fr 1fr' }, gap: 2 }}>
        <Paper sx={{ p: 2 }}>
          <TextField inputRef={input} fullWidth autoFocus label="Scanner input" value={code} onChange={(e) => setCode(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && resolve(code)}
            helperText="A handheld scanner types the code and presses Enter." sx={{ '& input': { fontFamily: mono, fontSize: 20 } }} />
          <Stack direction="row" gap={1} sx={{ mt: 2 }}>
            <Button variant="contained" onClick={() => resolve(code)}>Look up</Button>
            <Button variant="outlined" onClick={() => { setCamErr(null); setCamera(!camera); }}>{camera ? 'Stop camera' : 'Use camera'}</Button>
          </Stack>
          {camErr && <Alert severity="warning" sx={{ mt: 2 }}>{camErr}</Alert>}
          <Box sx={{ mt: 2, display: camera ? 'block' : 'none' }}><video ref={video} style={{ width: '100%', borderRadius: 6, background: '#000' }} muted playsInline /></Box>
        </Paper>
        <Section title="Results">
          {hist.length === 0 && <Typography color="text.secondary">Scan a lot, serial, work order, purchase order, location or GS1 code.</Typography>}
          <Stack gap={1}>{hist.map((h, i) => <Paper key={i} variant="outlined" sx={{ p: 1.25, borderLeft: 4, borderLeftColor: h.url ? 'primary.main' : 'secondary.main' }}>
            <Stack direction="row" justifyContent="space-between" alignItems="center">
              <Box><Typography sx={{ fontFamily: mono }}>{h.raw}</Typography><Typography variant="body2">{h.kind}{h.number ? ` ${h.number}` : ''}{h.detail ? ` · ${h.detail}` : ''}</Typography></Box>
              {h.url && <Button size="small" onClick={() => nav(h.url!)}>Open</Button>}
            </Stack>
            {h.candidates && <Stack direction="row" gap={1} sx={{ mt: 1, flexWrap: 'wrap' }}>{h.candidates.map((c) => <Chip key={c.type! + c.id} label={`${c.kind} ${c.number}`} onClick={() => c.url && nav(c.url)} />)}</Stack>}
          </Paper>)}</Stack>
        </Section>
      </Box>
    </ModuleWorkspace>
  );
}
