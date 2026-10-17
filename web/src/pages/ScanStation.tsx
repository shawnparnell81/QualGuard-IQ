import { useEffect, useState, type ReactNode } from 'react';
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Checkbox, Chip, FormControlLabel, Paper, Stack, TextField, Typography } from '@mui/material';
import CloudOffOutlined from '@mui/icons-material/CloudOffOutlined';
import { useQuery } from '@tanstack/react-query';
import { get, post } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { useLookup } from '../components/Kit';
import { localDate } from '../components/Status';
import { mono } from '../theme';
import { newRequestId, postOrQueue, useOfflineQueue } from '../offline';
import { checkPickItem, downloadPickWork, markPicked, normalizeLocation, pickWorkExpired, PICK_WORK_HOURS, readPickWork, type PickWork } from '../offlinePicks';
import { fmtDateTime } from '../components/Status';
import { checkVerifyScan, downloadVerifyWork, markVerified, progress, readVerifyWork, resetVerified, verifyWorkExpired, VERIFY_WORK_HOURS, type VerifyWork } from '../offlineVerifications';
import { SyncBanner } from '../components/SyncBanner';
import { LabelStatus } from '../components/LabelPrint';
import { ScanField, StockBadge, stockStatusLabel, useLocations, type StockRow } from '../warehouse/common';
import { qty } from './Warehouse';

const mod = modules.find((m) => m.key === 'warehouse')!;
interface ScanLink { type: string; id: string; number: string; title: string; status: string; url: string }
interface Ctx { match: { raw: string; kind: string; type?: string; id?: string; number?: string; url?: string; detail?: string }; stock: StockRow[]; holds: ScanLink[]; related: ScanLink[]; actions: string[] }
const flows: [string, string, string, string?][] = [['receive', 'Receive PO', 'Against a purchase order'], ['blind', 'Blind receipt', 'No PO or delivery', 'inventory.receive_blind'], ['inspect', 'Inspect', 'Open the inspection'], ['move', 'Move', 'Put away / relocate'], ['issue', 'Issue', 'To a work order'], ['pick', 'Pick', 'Assigned picks · works offline', 'shipping.edit'], ['verify', 'Verify shipment', 'Assigned · works offline', 'shipping.edit'], ['ship', 'Ship', 'Pick & verify'], ['trace', 'Trace', 'What is this?']];
const scan = (code: string) => post<Ctx>('/api/warehouse/scan', { code });

/** Result banner: green for posted, amber for queued offline, red for refused. */
type Outcome = { tone: 'success' | 'warning' | 'error'; text: string } | null;
const done = (queued: boolean, what: string): Outcome => (queued ? { tone: 'warning', text: `${what} saved on this device; it posts when the connection returns.` } : { tone: 'success', text: `${what} posted.` });

function Big({ children, onClick, disabled, color = 'primary' }: { children: ReactNode; onClick: () => void; disabled?: boolean; color?: 'primary' | 'inherit' }) {
  return <Button fullWidth size="large" variant={color === 'primary' ? 'contained' : 'outlined'} color={color} disabled={disabled} onClick={onClick} sx={{ py: 1.8, fontSize: 17 }}>{children}</Button>;
}
function Step({ n, label, value, children }: { n: number; label: string; value?: string; children: ReactNode }) {
  return <Box sx={{ display: 'grid', gridTemplateColumns: '34px 1fr', gap: 1.5, alignItems: 'start' }}>
    <Box sx={{ width: 34, height: 34, display: 'grid', placeItems: 'center', bgcolor: value ? 'primary.main' : 'action.hover', color: value ? 'primary.contrastText' : 'text.secondary', fontWeight: 700 }}>{n}</Box>
    <Box><Typography variant="overline" color="text.secondary">{label}</Typography>{value ? <Typography sx={{ fontFamily: mono, fontSize: 17, wordBreak: 'break-all' }}>{value}</Typography> : children}</Box>
  </Box>;
}

export function ScanStation() {
  const [p, setP] = useSearchParams();
  const flow = p.get('flow') ?? '';
  const q = useOfflineQueue();
  const { can } = useAuth();
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Scan station' }]} title="Scan station"
      actions={<Chip icon={q.online ? undefined : <CloudOffOutlined />} color={q.online ? 'success' : 'warning'} label={q.online ? 'Online' : 'Offline — scans are queued'} />}>
      <Box sx={{ maxWidth: 560, mx: 'auto' }}>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', sm: 'repeat(3, 1fr)' }, gap: 1, mb: 2 }}>
          {flows.filter(([, , , perm]) => !perm || can(perm)).map(([k, l, sub]) => (
            <Button key={k} variant={flow === k ? 'contained' : 'outlined'} onClick={() => setP({ flow: k })} sx={{ py: 1.5, flexDirection: 'column', alignItems: 'flex-start', textAlign: 'left' }}>
              <Typography sx={{ fontWeight: 700, fontSize: 17 }}>{l}</Typography><Typography variant="caption" sx={{ opacity: 0.8, textTransform: 'none' }}>{sub}</Typography></Button>))}
        </Box>
        <Paper sx={{ p: 2 }}>
          {flow === '' && <Typography color="text.secondary">Choose what you are doing. Scanners that type and press Enter work in every field; the camera button reads codes on a phone.</Typography>}
          {flow === 'receive' && <PoReceiveFlow key="r" />}
          {flow === 'blind' && (can('inventory.receive_blind') ? <BlindReceiveFlow key="b" /> : <Alert severity="warning">Blind receipts need the blind receiving permission. Receive against the purchase order instead.</Alert>)}
          {flow === 'inspect' && <InspectFlow key="i" />}
          {flow === 'move' && <MoveFlow key="m" initialItem={p.get('item') ?? ''} />}
          {flow === 'issue' && <IssueFlow key="is" initialItem={p.get('item') ?? ''} />}
          {flow === 'pick' && <PickFlow key="p" />}
          {flow === 'verify' && <VerifyFlow key="v" />}
          {flow === 'ship' && <ShipFlow key="s" />}
          {(flow === 'trace' || flow === 'lookup') && <TraceFlow key="t" onAction={(a, code) => setP({ flow: a, item: code })} />}
        </Paper>
        <SyncBanner q={q} />
      </Box>
    </ModuleWorkspace>
  );
}

const strip = (code: string, prefix: string) => (code.toUpperCase().startsWith(prefix) ? code.slice(prefix.length) : code).trim();

function BlindReceiveFlow() {
  const parts = useLookup('part');
  const locs = useLocations();
  const [part, setPart] = useState<{ id: string; label: string } | null>(null);
  const [lot, setLot] = useState(''); const [n, setN] = useState('');
  const [dock, setDock] = useState<{ id: string; code: string; siteId: string } | null>(null);
  const [out, setOut] = useState<Outcome>(null);
  // Resolve from the cached lists first (works offline); fall back to the server when the cache is cold or stale.
  const findPart = async (code: string) => {
    setOut(null);
    const v = strip(code, 'QG:PART:').toUpperCase();
    const x = parts.data?.find((r) => (r.partNumber ?? r.number ?? '').toUpperCase() === v);
    if (x) { setPart({ id: x.id, label: `${x.partNumber ?? x.number} ${x.name ?? ''}` }); return; }
    const c = navigator.onLine ? await scan(code.toUpperCase().startsWith('QG:') ? code : `QG:PART:${code}`).catch(() => null) : null;
    if (c?.match.kind === 'Part' && c.match.id) setPart({ id: c.match.id, label: `${c.match.number} ${c.match.detail ?? ''}` }); else setOut({ tone: 'error', text: `No part ${v}.` });
  };
  const findLoc = async (code: string) => {
    setOut(null);
    const v = strip(code, 'QG:LOC:').toUpperCase();
    const l = locs.data?.find((r) => r.code.toUpperCase() === v);
    if (l) { setDock({ id: l.id, code: l.code, siteId: l.siteId }); return; }
    const c = navigator.onLine ? await scan(code.toUpperCase().startsWith('QG:') ? code : `QG:LOC:${code}`).catch(() => null) : null;
    if (c?.match.kind === 'Location' && c.match.id) { const x = await get<{ id: string; code: string; siteId: string }>(`/api/warehouse/locations/${c.match.id}`); setDock({ id: x.id, code: x.code, siteId: x.siteId }); }
    else setOut({ tone: 'error', text: `No location ${v}.` });
  };
  const submit = async () => {
    setOut(null);
    const id = newRequestId();
    try {
      const r = await postOrQueue<{ number: string }>('/api/warehouse/receipts', { clientRequestId: id, kind: 3, supplierId: null, purchaseOrderId: null, asnNumber: null, packingSlip: null, carrierId: null, trackingNumber: null, receivedOn: localDate(), receivingLocationId: dock!.id, notes: 'Received at scan station',
        supplierOverrideReason: null, siteId: dock!.siteId, departmentId: null, channel: 'ScanStation',
        lines: [{ partId: part!.id, revision: null, purchaseOrderLineId: null, quantityExpected: null, quantityReceived: Number(n), supplierLotNumber: lot || null, lotNumber: null, serialNumbers: null, inspectionRequired: null, inspectionPlanId: null, aqlPlanId: null, aqlInspectionLevel: null, aqlLevel: null, putAwayLocationId: null, clientRequestId: id }] },
        `Receive ${n} × ${part!.label} at ${dock!.code}`);
      setOut(done(r.queued, r.result ? `Receipt ${r.result.number}` : 'Receipt')); setPart(null); setLot(''); setN('');
    } catch (e) { setOut({ tone: 'error', text: (e as Error).message }); }
  };
  return <Stack gap={2}>
    <Step n={1} label="Receiving dock" value={dock?.code}><ScanField label="Scan dock location" onScan={findLoc} autoFocus /></Step>
    <Step n={2} label="Part" value={part?.label}><ScanField label="Scan part label (QG:PART) or type part number" onScan={findPart} /></Step>
    <Step n={3} label="Supplier lot / heat"><TextField fullWidth value={lot} onChange={(e) => setLot(e.target.value)} placeholder="Optional" slotProps={{ htmlInput: { 'aria-label': 'Supplier lot' } }} /></Step>
    <Step n={4} label="Quantity"><TextField fullWidth type="number" value={n} onChange={(e) => setN(e.target.value)} slotProps={{ htmlInput: { inputMode: 'decimal', style: { fontSize: 22 }, 'aria-label': 'Quantity' } }} /></Step>
    {out && <Alert severity={out.tone}>{out.text}</Alert>}
    <Big disabled={!dock || !part || !(Number(n) > 0)} onClick={submit}>Receive</Big>
    <Typography variant="caption" color="text.secondary">Blind receipts have no purchase order to check against. Parts with a receiving inspection plan land as awaiting inspection.</Typography>
  </Stack>;
}

interface Requirement { id?: string; inspectionRequired: boolean; inspectionPlanNumber?: string; aqlPlanId?: string; aqlPlanName?: string; holdOnReceipt: boolean; holdReason?: string; serialsRequired: boolean; labelCopies: number }
interface PoLine { id: string; lineNumber: number; partId?: string; itemNumber: string; partName?: string; revision?: string; ordered: number; received: number; remaining: number; maxReceivable: number; unitOfMeasure: string; dueOn?: string; requirement: Requirement }
interface PoInfo { id: string; number: string; supplierId: string; supplierName: string; supplierStatus: string; supplierBlocked: boolean; supplierConditions: string[]; status: string; siteId: string;
  policy: { overReceiptPercent: number; overReceiptQuantity: number; allowPartialReceipt: boolean }; lines: PoLine[] }
interface CheckResult { ok: boolean; errors: { code: string; message: string }[]; warnings: string[]; overReceived: number; underReceipt: boolean }
interface ReceiptLabel { lineNumber: number; kind: string; number: string; content: string; copies: number }
interface ReceiptRow { line: { status: number; lotId?: string }; lotNumber?: string; inspectionNumber?: string; aqlNumber?: string; holdNumber?: string }

function RequirementChips({ r }: { r: Requirement }) {
  return <Stack direction="row" gap={0.5} flexWrap="wrap">
    {r.inspectionRequired && <Chip size="small" color="warning" label={`Inspection${r.inspectionPlanNumber ? ` ${r.inspectionPlanNumber}` : ''}`} />}
    {r.aqlPlanId && <Chip size="small" color="warning" label={`AQL ${r.aqlPlanName ?? ''}`} />}
    {r.holdOnReceipt && <Chip size="small" color="error" label="Receiving hold" />}
    {r.serialsRequired && <Chip size="small" label="Serial per unit" />}
  </Stack>;
}

/** Receiving against a purchase order: dock → PO → line → item → revision, lot, serials, quantity → server check → receive, then labels. */
function PoReceiveFlow() {
  const locs = useLocations();
  const [dock, setDock] = useState<{ id: string; code: string; siteId: string } | null>(null);
  const [po, setPo] = useState<PoInfo | null>(null);
  const [line, setLine] = useState<PoLine | null>(null);
  const [item, setItem] = useState('');
  const [rev, setRev] = useState(''); const [lot, setLot] = useState(''); const [n, setN] = useState('');
  const [serials, setSerials] = useState<string[]>([]); const [serialInput, setSerialInput] = useState('');
  const [hold, setHold] = useState(false); const [holdReason, setHoldReason] = useState('');
  const [check, setCheck] = useState<CheckResult | null>(null);
  const [out, setOut] = useState<Outcome>(null);
  const [done2, setDone2] = useState<{ id?: string; number?: string; rows: ReceiptRow[]; labels: ReceiptLabel[] } | null>(null);
  const reset = (keepPo = true) => { setLine(null); setItem(''); setRev(''); setLot(''); setN(''); setSerials([]); setHold(false); setHoldReason(''); setCheck(null); if (!keepPo) setPo(null); };
  const findLoc = async (code: string) => {
    setOut(null);
    const v = strip(code, 'QG:LOC:').toUpperCase();
    const l = locs.data?.find((r) => r.code.toUpperCase() === v);
    if (l) { setDock({ id: l.id, code: l.code, siteId: l.siteId }); return; }
    // cache cold or stale: ask the server
    const c = await scan(code.toUpperCase().startsWith('QG:') ? code : `QG:LOC:${code}`).catch(() => null);
    if (c?.match.kind === 'Location' && c.match.id) { const x = await get<{ id: string; code: string; siteId: string }>(`/api/warehouse/locations/${c.match.id}`); setDock({ id: x.id, code: x.code, siteId: x.siteId }); }
    else setOut({ tone: 'error', text: `No location ${v}.` });
  };
  const findPo = async (code: string) => {
    setOut(null); setDone2(null); reset(false);
    try { const x = await get<PoInfo>(`/api/warehouse/receiving/po?number=${encodeURIComponent(code.trim())}`); setPo(x); if (x.lines.length === 0) setOut({ tone: 'warning', text: `PO ${x.number} has no open lines.` }); }
    catch (e) { setOut({ tone: 'error', text: (e as Error).message }); }
  };
  const pick = (l: PoLine) => { setLine(l); setRev(l.revision ?? ''); setN(String(l.remaining)); setCheck(null); setOut(null); };
  const onItem = (code: string) => {
    setCheck(null);
    const v = strip(code, 'QG:PART:');
    if (line && v.toUpperCase() !== line.itemNumber.toUpperCase()) { setItem(''); setOut({ tone: 'error', text: `Wrong part: scanned ${v}, PO line ${line.lineNumber} is ${line.itemNumber}.` }); return; }
    setOut(null); setItem(v);
  };
  const onSerial = (code: string) => {
    const v = code.replace(/^QG:SN:[^:]+:/i, '').trim();
    setSerialInput('');
    if (!v) return;
    if (serials.some((s) => s.toUpperCase() === v.toUpperCase())) { setOut({ tone: 'error', text: `Serial ${v} is already scanned.` }); return; }
    setOut(null); const next = [...serials, v]; setSerials(next); setN(String(next.length)); setCheck(null);
  };
  const lineBody = () => ({ partId: line!.partId, revision: rev.trim() || null, purchaseOrderLineId: line!.id, quantityExpected: null, quantityReceived: Number(n), supplierLotNumber: lot.trim() || null, lotNumber: null,
    serialNumbers: serials.length ? serials : null, inspectionRequired: null, inspectionPlanId: null, aqlPlanId: null, aqlInspectionLevel: null, aqlLevel: null, putAwayLocationId: null, clientRequestId: null as string | null,
    placeHold: hold || null, holdReason: hold ? holdReason.trim() : null });
  const runCheck = async () => {
    setOut(null);
    try {
      const [r] = await post<CheckResult[]>('/api/warehouse/receiving/check', { purchaseOrderId: po!.id, siteId: po!.siteId, supplierId: null, lines: [lineBody()], scannedItem: item });
      setCheck(r);
      return r;
    } catch (e) { setOut({ tone: 'error', text: (e as Error).message }); return null; }
  };
  const submit = async () => {
    const r = check ?? await runCheck();
    if (!r || r.errors.length > 0) return;
    const id = newRequestId();
    try {
      const res = await postOrQueue<{ id: string; number: string }>('/api/warehouse/receipts', { clientRequestId: id, kind: 0, supplierId: po!.supplierId, purchaseOrderId: po!.id, asnNumber: null, packingSlip: null, carrierId: null, trackingNumber: null,
        receivedOn: localDate(), receivingLocationId: dock!.id, notes: null, supplierOverrideReason: null, siteId: po!.siteId, departmentId: null, channel: 'ScanStation', lines: [{ ...lineBody(), clientRequestId: id }] },
        `Receive ${n} × ${line!.itemNumber} on PO ${po!.number} line ${line!.lineNumber}`);
      if (res.result) {
        const d = await get<{ lines: ReceiptRow[] }>(`/api/warehouse/receipts/${res.result.id}`);
        const labels = await get<ReceiptLabel[]>(`/api/warehouse/receipts/${res.result.id}/labels`);
        setDone2({ id: res.result.id, number: res.result.number, rows: d.lines, labels });
        setOut({ tone: 'success', text: `Receipt ${res.result.number} posted.` });
        setPo(await get<PoInfo>(`/api/warehouse/receiving/po?number=${encodeURIComponent(po!.number)}`));
      } else { setDone2(null); setOut(done(true, 'Receipt')); }
      reset();
    } catch (e) { setOut({ tone: 'error', text: (e as Error).message }); }
  };
  const ready = !!dock && !!po && !!line && !!item && Number(n) > 0 && !po.supplierBlocked && (!hold || holdReason.trim().length > 1) && (!line.requirement.serialsRequired || serials.length > 0);
  return <Stack gap={2}>
    <Step n={1} label="Receiving dock" value={dock?.code}><ScanField label="Scan dock location" onScan={findLoc} autoFocus /></Step>
    <Step n={2} label="Purchase order" value={po ? `${po.number} · ${po.supplierName}` : undefined}><ScanField label="Scan PO label or type the PO number" onScan={findPo} /></Step>
    {po && po.supplierBlocked && <Alert severity="error">Supplier {po.supplierName} is restricted ({po.supplierConditions.join('; ')}). Receive on the desktop receiving page, where an authorized override with a reason is recorded.</Alert>}
    {po && !po.supplierBlocked && po.supplierConditions.length > 0 && <Alert severity="warning">{po.supplierConditions.join('; ')}</Alert>}
    {po && !line && po.lines.length > 0 && <Stack gap={1} data-testid="po-lines">
      <Typography variant="overline" color="text.secondary">3 · Select the PO line</Typography>
      {po.lines.map((l) => <Button key={l.id} variant="outlined" onClick={() => pick(l)} sx={{ justifyContent: 'flex-start', textAlign: 'left', py: 1.25, flexDirection: 'column', alignItems: 'flex-start', textTransform: 'none' }}>
        <Typography sx={{ fontFamily: mono, fontWeight: 700 }}>Line {l.lineNumber} · {l.itemNumber}{l.revision ? ` rev ${l.revision}` : ''}</Typography>
        <Typography variant="body2">{qty(l.remaining)} of {qty(l.ordered)} {l.unitOfMeasure} open{l.maxReceivable > l.remaining ? ` · up to ${qty(l.maxReceivable)} with tolerance` : ''}</Typography>
        <RequirementChips r={l.requirement} /></Button>)}
    </Stack>}
    {line && <>
      <Step n={3} label="PO line" value={`Line ${line.lineNumber} · ${line.itemNumber} · ${qty(line.remaining)} open`}><span /></Step>
      <RequirementChips r={line.requirement} />
      <Step n={4} label="Item" value={item || undefined}><ScanField label="Scan the item (part label)" onScan={onItem} autoFocus /></Step>
      <Step n={5} label="Revision"><TextField fullWidth value={rev} onChange={(e) => { setRev(e.target.value); setCheck(null); }} helperText={line.revision ? `PO orders revision ${line.revision}` : undefined} slotProps={{ htmlInput: { 'aria-label': 'Revision' } }} /></Step>
      <Step n={6} label="Supplier lot / heat"><TextField fullWidth value={lot} onChange={(e) => { setLot(e.target.value); setCheck(null); }} placeholder="Optional" slotProps={{ htmlInput: { 'aria-label': 'Supplier lot' } }} /></Step>
      <Step n={7} label={line.requirement.serialsRequired ? 'Serial numbers (one per unit)' : 'Serial numbers (optional)'}>
        <ScanField label="Scan serial" onScan={onSerial} value={serialInput} onChange={setSerialInput} />
        {serials.length > 0 && <Stack direction="row" gap={0.5} flexWrap="wrap" sx={{ mt: 1 }} data-testid="scanned-serials">{serials.map((s) => <Chip key={s} label={s} onDelete={() => { const next = serials.filter((x) => x !== s); setSerials(next); setN(String(next.length)); setCheck(null); }} />)}</Stack>}
      </Step>
      <Step n={8} label="Quantity"><TextField fullWidth type="number" value={n} disabled={serials.length > 0} onChange={(e) => { setN(e.target.value); setCheck(null); }} slotProps={{ htmlInput: { inputMode: 'decimal', style: { fontSize: 22 }, 'aria-label': 'Quantity received' } }} /></Step>
      {!line.requirement.holdOnReceipt && <FormControlLabel control={<Checkbox checked={hold} onChange={(e) => { setHold(e.target.checked); setCheck(null); }} />} label="Place a receiving hold" />}
      {hold && <TextField label="Hold reason" value={holdReason} onChange={(e) => { setHoldReason(e.target.value); setCheck(null); }} />}
      {check && check.errors.length > 0 && <Alert severity="error" data-testid="receive-check">{check.errors.map((e) => <div key={e.code + e.message}>{e.message}</div>)}</Alert>}
      {check && check.errors.length === 0 && check.warnings.length > 0 && <Alert severity="warning" data-testid="receive-check">{check.warnings.map((w) => <div key={w}>{w}</div>)}</Alert>}
      <Stack direction="row" gap={1}><Button variant="outlined" disabled={!ready} onClick={runCheck}>Check</Button><Button onClick={() => reset()}>Change line</Button></Stack>
    </>}
    {out && <Alert severity={out.tone}>{out.text}</Alert>}
    {line && <Big disabled={!ready || (check?.errors.length ?? 0) > 0} onClick={submit}>{check && check.warnings.length > 0 ? 'Receive with warnings' : 'Receive'}</Big>}
    {done2 && <Paper variant="outlined" sx={{ p: 1.5 }} data-testid="receive-result">
      <Typography sx={{ fontFamily: mono, fontWeight: 700 }}>Receipt {done2.number}</Typography>
      {done2.rows.map((r, i) => <Stack key={i} direction="row" gap={0.5} flexWrap="wrap" sx={{ mt: 0.5 }}>
        {r.lotNumber && <Chip size="small" label={`Lot ${r.lotNumber}`} />}
        <Chip size="small" color={r.line.status === 1 ? 'warning' : 'success'} label={r.line.status === 1 ? 'Awaiting inspection' : 'Accepted to stock'} />
        {r.inspectionNumber && <Chip size="small" label={`Inspection ${r.inspectionNumber}`} />}
        {r.aqlNumber && <Chip size="small" label={`AQL ${r.aqlNumber}`} />}
        {r.holdNumber && <Chip size="small" color="error" label={`Hold ${r.holdNumber}`} />}
      </Stack>)}
      {done2.id && <Box sx={{ mt: 1 }}><LabelStatus refType="Receipt" refId={done2.id} /></Box>}
      <Stack direction="row" gap={1} sx={{ mt: 1 }}>
        <Button variant="contained" component={RouterLink} to={`/warehouse/receipts/${done2.id}/labels`}>Labels ({done2.labels.reduce((a, l) => a + l.copies, 0)})</Button>
        <Button component={RouterLink} to={`/warehouse/receipts/${done2.id}`}>Open receipt</Button>
      </Stack>
    </Paper>}
    <Typography variant="caption" color="text.secondary">The server checks part, revision, open quantity and over-receipt tolerance, serials and the part's receiving requirements before anything is posted.</Typography>
  </Stack>;
}

function InspectFlow() {
  const [ctx, setCtx] = useState<Ctx | null>(null); const [err, setErr] = useState<string | null>(null);
  const go = async (c: string) => { setErr(null); try { setCtx(await scan(c)); } catch (e) { setErr((e as Error).message); } };
  const insp = ctx?.related.filter((r) => r.type === 'Inspection' || r.type === 'AqlInspection') ?? [];
  return <Stack gap={2}>
    <ScanField label="Scan the lot label" onScan={go} autoFocus />
    {err && <Alert severity="error">{err}</Alert>}
    {ctx && <>
      <Typography sx={{ fontFamily: mono, fontSize: 18 }}>{ctx.match.kind} {ctx.match.number}</Typography>
      {!ctx.actions.includes('inspect') && <Alert severity="info">Nothing is waiting for receiving inspection on this {ctx.match.kind.toLowerCase()}.</Alert>}
      {insp.map((r) => <Button key={r.id} variant="contained" size="large" component={RouterLink} to={r.url}>{r.number} · {r.title} · {r.status}</Button>)}
      {ctx.related.filter((r) => r.type === 'Receipt').map((r) => <Button key={r.id} variant="outlined" component={RouterLink} to={r.url}>Receipt {r.number} — record the outcome</Button>)}
    </>}
  </Stack>;
}

function MoveFlow({ initialItem }: { initialItem: string }) {
  const [item, setItem] = useState(initialItem); const [from, setFrom] = useState(''); const [to, setTo] = useState(''); const [n, setN] = useState('');
  const [stock, setStock] = useState<StockRow[]>([]);
  const [out, setOut] = useState<Outcome>(null);
  useEffect(() => { if (initialItem && navigator.onLine) scan(initialItem).then((c) => setStock(c.stock)).catch(() => undefined); }, [initialItem]);
  const onItem = (c: string) => { setItem(c); if (navigator.onLine) scan(c).then((x) => { setStock(x.stock); if (x.stock.length === 1) { setFrom(`QG:LOC:${x.stock[0].locationCode}`); setN(String(x.stock[0].available)); } }).catch(() => setStock([])); };
  const submit = async () => {
    setOut(null);
    try { const r = await postOrQueue('/api/warehouse/moves/scan', { item, from, to, quantity: Number(n) }, `Move ${n} × ${item} ${strip(from, 'QG:LOC:')} → ${strip(to, 'QG:LOC:')}`); setOut(done(r.queued, 'Move')); setItem(''); setFrom(''); setTo(''); setN(''); setStock([]); }
    catch (e) { setOut({ tone: 'error', text: (e as Error).message }); }
  };
  return <Stack gap={2}>
    <Step n={1} label="Item" value={item}><ScanField label="Scan lot, serial or part" onScan={onItem} autoFocus /></Step>
    {stock.length > 1 && !from && <Stack gap={0.5}>{stock.map((s) => <Button key={s.id} variant="outlined" onClick={() => { setFrom(`QG:LOC:${s.locationCode}`); setN(String(s.available)); }} sx={{ justifyContent: 'space-between', fontFamily: mono }}>
      <span>{s.locationCode}</span><span>{qty(s.available)} {stockStatusLabel[s.status]}</span></Button>)}</Stack>}
    <Step n={2} label="From" value={from}><ScanField label="Scan source location" onScan={setFrom} /></Step>
    <Step n={3} label="To" value={to}><ScanField label="Scan destination location" onScan={setTo} /></Step>
    <Step n={4} label="Quantity"><TextField fullWidth type="number" value={n} onChange={(e) => setN(e.target.value)} slotProps={{ htmlInput: { inputMode: 'decimal', style: { fontSize: 22 }, 'aria-label': 'Quantity' } }} /></Step>
    {out && <Alert severity={out.tone}>{out.text}</Alert>}
    <Big disabled={!item || !from || !to || !(Number(n) > 0)} onClick={submit}>Move</Big>
    {(item || from || to) && <Big color="inherit" onClick={() => { setItem(''); setFrom(''); setTo(''); setN(''); setStock([]); setOut(null); }}>Start over</Big>}
  </Stack>;
}

function IssueFlow({ initialItem }: { initialItem: string }) {
  const [wo, setWo] = useState(''); const [mat, setMat] = useState(initialItem); const [from, setFrom] = useState(''); const [n, setN] = useState(''); const [op, setOp] = useState('');
  const [out, setOut] = useState<Outcome>(null);
  const submit = async () => {
    setOut(null);
    try { const r = await postOrQueue('/api/warehouse/issues/scan', { workOrder: wo, material: mat, from, quantity: Number(n), operationSequence: op ? Number(op) : null }, `Issue ${n} × ${mat} to ${strip(wo, 'QG:WO:')}`); setOut(done(r.queued, 'Issue')); setMat(''); setFrom(''); setN(''); }
    catch (e) { setOut({ tone: 'error', text: (e as Error).message }); }
  };
  return <Stack gap={2}>
    <Step n={1} label="Work order" value={wo}><ScanField label="Scan work order traveler (QG:WO)" onScan={setWo} autoFocus /></Step>
    <Step n={2} label="Material" value={mat}><ScanField label="Scan material lot or serial" onScan={setMat} /></Step>
    <Step n={3} label="From" value={from}><ScanField label="Scan the location it comes from" onScan={setFrom} /></Step>
    <Step n={4} label="Quantity & operation"><Stack direction="row" gap={1}><TextField fullWidth type="number" label="Quantity" value={n} onChange={(e) => setN(e.target.value)} slotProps={{ htmlInput: { inputMode: 'decimal' } }} /><TextField label="Op" value={op} onChange={(e) => setOp(e.target.value)} sx={{ width: 110 }} slotProps={{ htmlInput: { inputMode: 'numeric' } }} /></Stack></Step>
    {out && <Alert severity={out.tone}>{out.text}</Alert>}
    <Big disabled={!wo || !mat || !from || !(Number(n) > 0)} onClick={submit}>Issue to work order</Big>
    <Typography variant="caption" color="text.secondary">Issuing records genealogy from the material lot to the work order. Held or uninspected material is refused.</Typography>
  </Stack>;
}

interface ShipDetail { shipment: { id: string; number: string; status: number }; picks: { task: { id: string; quantity: number; status: number }; partNumber: string; lotNumber?: string; serialNumber?: string; locationCode: string }[] }
/**
 * Assigned picking that keeps working without a connection: download my picks, then scan location and item for each pick; the
 * confirmation posts at once when online or is saved on the device with its request id and device time and syncs on reconnect.
 * Work that changed meanwhile (cancelled, picked by someone else, held, stock moved, reassigned) comes back as a conflict.
 */
function PickFlow() {
  const { me } = useAuth();
  const q = useOfflineQueue();
  const uid = me!.id;
  const [work, setWork] = useState<PickWork | null>(() => readPickWork(uid));
  const [sel, setSel] = useState<string | null>(null);
  const [loc, setLoc] = useState(''); const [locIn, setLocIn] = useState('');
  const [item, setItem] = useState(''); const [itemIn, setItemIn] = useState(''); const [warn, setWarn] = useState<string | null>(null);
  const [n, setN] = useState('');
  const [out, setOut] = useState<Outcome>(null);
  const [busy, setBusy] = useState(false);
  const pendingTasks = q.items.filter((x) => x.url.includes('/picks/') && x.state !== 'failed' && x.state !== 'conflict').map((x) => x.url.split('/picks/')[1].split('/')[0]);
  const download = async () => {
    setOut(null); setBusy(true);
    try { const w = await downloadPickWork(uid, pendingTasks); setWork(w); setSel(null); const c = w.lists.reduce((a, l) => a + l.tasks.length, 0); setOut({ tone: 'success', text: `Downloaded ${c} pick${c === 1 ? '' : 's'} on ${w.lists.length} shipment${w.lists.length === 1 ? '' : 's'} assigned to you.` }); }
    catch (e) { setOut({ tone: 'error', text: (e as Error).message }); }
    finally { setBusy(false); }
  };
  const reset = () => { setLoc(''); setLocIn(''); setItem(''); setItemIn(''); setWarn(null); setN(''); };
  const expired = !!work && pickWorkExpired(work);
  const list = work?.lists.find((l) => l.shipmentId === sel) ?? null;
  const open = list?.tasks.filter((t) => !work!.done[t.taskId]) ?? [];
  const next = open[0];
  const scanLoc = (c: string) => {
    setOut(null);
    if (normalizeLocation(c) !== next!.locationCode.toUpperCase()) { setLocIn(''); setOut({ tone: 'error', text: `Wrong location: pick from ${next!.locationCode}.` }); return; }
    setLoc(c);
  };
  const scanItem = (c: string) => {
    setOut(null);
    const r = checkPickItem(next!, c);
    if (r.error) { setItemIn(''); setOut({ tone: 'error', text: r.error }); return; }
    setWarn(r.warning ?? null); setItem(c); setN(String(next!.quantity));
  };
  const confirm = async () => {
    const quantity = Number(n);
    if (!(quantity > 0) || quantity > next!.quantity) { setOut({ tone: 'error', text: `Quantity must be between 0 and ${qty(next!.quantity)}.` }); return; }
    setBusy(true); setOut(null);
    const rid = newRequestId();
    try {
      const r = await postOrQueue<{ outcome: number; message: string }>(`/api/shipping/shipments/${list!.shipmentId}/picks/${next!.taskId}/confirm`,
        { locationCode: loc, itemCode: item, quantity, clientRequestId: rid, deviceTimestamp: new Date().toISOString(), offline: !navigator.onLine },
        `Pick ${list!.number} · ${next!.partNumber} ${next!.serialNumber ? 'SN ' + next!.serialNumber : next!.lotNumber ? 'lot ' + next!.lotNumber : ''} × ${qty(quantity)} from ${next!.locationCode}`);
      setWork(markPicked(uid, next!.taskId, rid));
      setOut(r.queued ? { tone: 'warning', text: `Pick of ${next!.partNumber} saved on this device; it syncs when the connection returns.` } : { tone: 'success', text: r.result?.message ?? 'Picked.' });
      reset();
    } catch (e) { setOut({ tone: 'error', text: (e as Error).message }); }
    finally { setBusy(false); }
  };
  return <Stack gap={2} data-testid="pick-flow">
    <Stack direction="row" gap={1} alignItems="center" justifyContent="space-between">
      <Typography variant="body2" color="text.secondary" data-testid="pick-downloaded">{work ? `Downloaded ${fmtDateTime(work.downloadedAt)}` : 'No pick work on this device yet.'}</Typography>
      <Button variant="outlined" disabled={!q.online || busy} onClick={download}>{work ? 'Refresh my picks' : 'Download my picks'}</Button>
    </Stack>
    {!q.online && !work && <Alert severity="warning">Download your assigned picks while connected; they can then be picked offline.</Alert>}
    {expired && <Alert severity="warning" data-testid="pick-expired">This download is older than {PICK_WORK_HOURS} hours. Refresh it before picking so cancelled or changed work is not picked.</Alert>}
    {out && <Alert severity={out.tone} data-testid="pick-result">{out.text}</Alert>}
    {work && !expired && !list && (work.lists.length === 0 ? <Alert severity="info">No picks are assigned to you.</Alert>
      : <Stack gap={1} data-testid="pick-lists">{work.lists.map((l) => { const left = l.tasks.filter((t) => !work.done[t.taskId]).length; return (
        <Button key={l.shipmentId} variant="outlined" onClick={() => { setSel(l.shipmentId); reset(); setOut(null); }} sx={{ justifyContent: 'space-between', py: 1.5 }}>
          <span style={{ fontFamily: mono }}>{l.number}</span><span>{l.customer ?? ''} · {left ? `${left} to pick` : 'done'}</span></Button>); })}</Stack>)}
    {list && !expired && <>
      <Stack direction="row" justifyContent="space-between" alignItems="center">
        <Typography sx={{ fontFamily: mono, fontSize: 18 }}>{list.number}</Typography>
        <Button size="small" onClick={() => { setSel(null); reset(); }}>All my picks</Button>
      </Stack>
      {next ? <Paper variant="outlined" sx={{ p: 2 }} data-testid="pick-next">
        <Typography variant="overline">Next pick · {open.length} left</Typography>
        <Typography sx={{ fontFamily: mono, fontSize: 26, fontWeight: 700 }}>{next.locationCode}</Typography>
        <Typography>{next.partNumber}{next.revision ? ` rev ${next.revision}` : ''} · {next.serialNumber ? 'SN ' + next.serialNumber : next.lotNumber ? 'lot ' + next.lotNumber : ''} · <b>{qty(next.quantity)} {next.unitOfMeasure}</b></Typography>
        <Stack gap={1.5} sx={{ mt: 1.5 }}>
          <Step n={1} label="Location" value={loc}><ScanField label="Scan pick location" value={locIn} onChange={setLocIn} onScan={scanLoc} autoFocus /></Step>
          {loc && <Step n={2} label="Item" value={item}><ScanField label="Scan the lot / serial picked" value={itemIn} onChange={setItemIn} onScan={scanItem} autoFocus /></Step>}
          {warn && <Alert severity="info">{warn}</Alert>}
          {item && <Step n={3} label="Quantity picked"><TextField fullWidth value={n} onChange={(e) => setN(e.target.value)} slotProps={{ htmlInput: { inputMode: 'decimal', 'aria-label': 'Quantity picked' } }} /></Step>}
          {item && <Big disabled={busy} onClick={confirm}>Confirm pick</Big>}
          {(loc || item) && <Button color="inherit" onClick={reset}>Start this pick again</Button>}
        </Stack>
      </Paper> : <Alert severity="success">All picks on {list.number} are done{q.pending.length ? '; they sync when the connection returns' : ''}.</Alert>}
    </>}
  </Stack>;
}

interface VerificationStatus { outcome: number; message: string; state: number; status: number; checks: { key: string; label: string; passed: boolean; message: string }[]; offlineScans: number }
const vStates = ['Not started', 'In progress', 'Pending validation', 'Validated'];

/**
 * Shipment verification that keeps working without a connection: download my assigned shipments, scan cartons, pallets, lots and
 * serials. The device refuses duplicates and wrong items at once; each accepted scan posts now or is saved with its request id and
 * device time and syncs on reconnect, where the server checks it again. Offline scans never release the shipment: once the contents
 * are complete it waits as "pending validation" until the release conditions (stock, holds, revision, customer, status) are
 * confirmed online.
 */
function VerifyFlow() {
  const { me } = useAuth();
  const q = useOfflineQueue();
  const uid = me!.id;
  const [work, setWork] = useState<VerifyWork | null>(() => readVerifyWork(uid));
  const [sel, setSel] = useState<string | null>(null);
  const [code, setCode] = useState(''); const [n, setN] = useState('');
  const [out, setOut] = useState<Outcome>(null);
  const [status, setStatus] = useState<VerificationStatus | null>(null);
  const [busy, setBusy] = useState(false);
  const pendingIds = q.items.filter((x) => x.url.includes('/verification/scans') && x.state !== 'failed' && x.state !== 'conflict').map((x) => x.id);
  const download = async () => {
    setOut(null); setBusy(true);
    try { const w = await downloadVerifyWork(uid, pendingIds); setWork(w); setSel(null); setStatus(null); setOut({ tone: 'success', text: `Downloaded ${w.lists.length} shipment${w.lists.length === 1 ? '' : 's'} assigned to you for verification.` }); }
    catch (e) { setOut({ tone: 'error', text: (e as Error).message }); }
    finally { setBusy(false); }
  };
  const expired = !!work && verifyWorkExpired(work);
  const list = work?.lists.find((l) => l.shipmentId === sel) ?? null;
  const scans = list ? work!.scanned[list.shipmentId] ?? [] : [];
  const prog = list ? progress(list, scans) : {};
  const complete = !!list && list.lines.every((l) => (prog[l.lineId] ?? 0) === l.quantity);
  const pendingHere = list ? q.items.filter((x) => x.url.includes(`/shipments/${list.shipmentId}/verification/scans`)) : [];
  const record = async (raw: string) => {
    setOut(null);
    const quantity = n ? Number(n) : null;
    const r = checkVerifyScan(list!, scans, raw, quantity);
    if (r.error) { setCode(''); setOut({ tone: 'error', text: r.error }); return; }
    setBusy(true);
    const rid = newRequestId();
    try {
      const res = await postOrQueue<VerificationStatus>(`/api/shipping/shipments/${list!.shipmentId}/verification/scans`,
        { code: raw.trim(), quantity, clientRequestId: rid, deviceTimestamp: new Date().toISOString(), offline: !navigator.onLine }, `Verify ${list!.number} · ${raw.trim()}`);
      setWork(markVerified(uid, list!.shipmentId, { code: raw.trim(), lines: r.lines!, containers: r.containers!, requestId: rid }));
      if (res.queued) setOut({ tone: 'warning', text: `${raw.trim()} saved on this device; it syncs when the connection returns. The shipment stays pending validation until it is confirmed online.` });
      else { setStatus(res.result!); setOut({ tone: 'success', text: res.result!.message }); }
      setCode(''); setN('');
    } catch (e) { setCode(''); setOut({ tone: 'error', text: (e as Error).message }); }
    finally { setBusy(false); }
  };
  const confirmOnline = async () => {
    setBusy(true); setOut(null);
    try {
      const r = await post<VerificationStatus>(`/api/shipping/shipments/${list!.shipmentId}/verification/validate`, {});
      setStatus(r); setOut({ tone: r.state === 3 ? 'success' : 'error', text: r.message });
    } catch (e) { setOut({ tone: 'error', text: (e as Error).message }); }
    finally { setBusy(false); }
  };
  const restart = async () => {
    setBusy(true); setOut(null);
    try { const r = await post<VerificationStatus>(`/api/shipping/shipments/${list!.shipmentId}/verification/restart`, {}); setStatus(r); setWork(resetVerified(uid, list!.shipmentId)); setOut({ tone: 'success', text: r.message }); }
    catch (e) { setOut({ tone: 'error', text: (e as Error).message }); }
    finally { setBusy(false); }
  };
  const state = status?.state ?? (list && !pendingHere.length && list.state > 0 && scans.length ? list.state : complete ? 2 : scans.length ? 1 : 0);
  return <Stack gap={2} data-testid="verify-flow">
    <Stack direction="row" gap={1} alignItems="center" justifyContent="space-between">
      <Typography variant="body2" color="text.secondary">{work ? `Downloaded ${fmtDateTime(work.downloadedAt)}` : 'No verification work on this device yet.'}</Typography>
      <Button variant="outlined" disabled={!q.online || busy} onClick={download}>{work ? 'Refresh my verifications' : 'Download my verifications'}</Button>
    </Stack>
    {!q.online && !work && <Alert severity="warning">Download your assigned shipments while connected; their contents can then be verified offline.</Alert>}
    {expired && <Alert severity="warning">This download is older than {VERIFY_WORK_HOURS} hours. Refresh it before verifying so cancelled or changed shipments are not loaded.</Alert>}
    {out && <Alert severity={out.tone} data-testid="verify-result">{out.text}</Alert>}
    {work && !expired && !list && (work.lists.length === 0 ? <Alert severity="info">No shipments are assigned to you for verification.</Alert>
      : <Stack gap={1} data-testid="verify-lists">{work.lists.map((l) => { const s = work.scanned[l.shipmentId] ?? []; const pr = progress(l, s); const left = l.lines.reduce((a, x) => a + Math.max(0, x.quantity - (pr[x.lineId] ?? 0)), 0); return (
        <Button key={l.shipmentId} variant="outlined" onClick={() => { setSel(l.shipmentId); setStatus(null); setOut(null); setCode(''); setN(''); }} sx={{ justifyContent: 'space-between', py: 1.5 }}>
          <span style={{ fontFamily: mono }}>{l.number}</span><span>{l.customer ?? ''} · {left ? `${qty(left)} to verify` : 'contents verified'}</span></Button>); })}</Stack>)}
    {list && !expired && <>
      <Stack direction="row" justifyContent="space-between" alignItems="center">
        <Typography sx={{ fontFamily: mono, fontSize: 18 }}>{list.number}</Typography>
        <Stack direction="row" gap={1} alignItems="center"><Chip size="small" data-testid="verify-state" color={state === 3 ? 'success' : state === 2 ? 'warning' : 'default'} label={vStates[state]} />
          <Button size="small" onClick={() => { setSel(null); setStatus(null); }}>All my shipments</Button></Stack>
      </Stack>
      <Paper variant="outlined" sx={{ p: 1.5 }}>
        {list.lines.map((l) => <Stack key={l.lineId} direction="row" justifyContent="space-between" sx={{ py: 0.5 }}>
          <Typography>{l.lineNumber}. {l.partNumber}{l.revision ? ` rev ${l.revision}` : ''}{l.requiredLot ? ` · lot ${l.requiredLot}` : ''}</Typography>
          <Typography sx={{ fontWeight: 700, color: (prog[l.lineId] ?? 0) === l.quantity ? 'success.main' : 'text.primary' }}>{qty(prog[l.lineId] ?? 0)} / {qty(l.quantity)}</Typography></Stack>)}
        <Typography variant="caption" color="text.secondary">Packed: {list.containers.filter((c) => !c.parentId).map((c) => c.number).join(', ') || 'no containers'}</Typography>
      </Paper>
      {!complete && state !== 3 && <Stack gap={1.5}>
        <ScanField label="Scan carton, pallet, lot or serial" value={code} onChange={setCode} onScan={record} autoFocus />
        <TextField label="Quantity (loose lot scans only)" size="small" value={n} onChange={(e) => setN(e.target.value)} inputProps={{ inputMode: 'decimal' }} />
      </Stack>}
      {complete && state !== 3 && <Alert severity="warning" data-testid="verify-pending">Contents verified{pendingHere.length ? `; ${pendingHere.length} scan${pendingHere.length === 1 ? '' : 's'} still on this device` : ''}. The shipment is pending validation: holds, stock, revision, customer requirements and status must be confirmed online before it can ship.</Alert>}
      {complete && state !== 3 && <Big disabled={!q.online || busy || pendingHere.some((x) => x.state === 'local' || x.state === 'syncing')} onClick={confirmOnline}>Confirm release conditions online</Big>}
      {state === 3 && <Alert severity="success" data-testid="verify-validated">Verified and release conditions confirmed. Shipping checks them again at release.</Alert>}
      {status?.checks.length ? <Stack gap={0.5} data-testid="verify-checks">{status.checks.map((c) => <Typography key={c.key} variant="body2" color={c.passed ? 'success.main' : 'error.main'}>{c.passed ? '✓' : '✗'} {c.label}: {c.message}</Typography>)}</Stack> : null}
      {scans.length > 0 && <Button color="inherit" disabled={!q.online || busy} onClick={restart}>Restart verification</Button>}
    </>}
  </Stack>;
}

function ShipFlow() {
  const [id, setId] = useState<string | null>(null); const [out, setOut] = useState<Outcome>(null);
  const [loc, setLoc] = useState(''); const [verify, setVerify] = useState<string[]>([]);
  const d = useQuery({ queryKey: ['shipment', id], queryFn: () => get<ShipDetail>(`/api/shipping/shipments/${id}`), enabled: !!id });
  const open = d.data?.picks.filter((x) => x.task.status === 0) ?? [];
  const next = open[0];
  const find = async (c: string) => { setOut(null); try { const x = await scan(c); if (x.match.kind !== 'Shipment' || !x.match.id) throw new Error('That is not a shipment or carton label.'); setId(x.match.id); } catch (e) { setOut({ tone: 'error', text: (e as Error).message }); } };
  const pick = async (item: string) => {
    setOut(null);
    try { await post(`/api/shipping/shipments/${id}/picks/${next!.task.id}`, { locationCode: loc, itemCode: item, quantity: next!.task.quantity }); setOut({ tone: 'success', text: `Picked ${next!.partNumber} from ${next!.locationCode}.` }); setLoc(''); d.refetch(); }
    catch (e) { setOut({ tone: 'error', text: (e as Error).message }); }
  };
  const doVerify = async () => {
    setOut(null);
    try { const r = await post<{ verified: boolean; issues: string[] }>(`/api/shipping/shipments/${id}/verify`, { scans: verify.map((code) => ({ code, quantity: null })) }); setOut(r.verified ? { tone: 'success', text: 'Shipment verified — ready to ship.' } : { tone: 'warning', text: r.issues.join(' ') || 'Not verified yet.' }); d.refetch(); }
    catch (e) { setOut({ tone: 'error', text: (e as Error).message }); }
  };
  if (!navigator.onLine) return <Alert severity="warning">Desk verification here checks live stock, so it needs a connection. Assigned shipments can be verified offline under Verify shipment, assigned picks under Pick; moves and issues also queue offline.</Alert>;
  return <Stack gap={2}>
    {!id ? <ScanField label="Scan shipment number or carton (QG:CTN)" onScan={find} autoFocus /> : <Typography sx={{ fontFamily: mono, fontSize: 18 }}>{d.data?.shipment.number}</Typography>}
    {out && <Alert severity={out.tone}>{out.text}</Alert>}
    {id && d.data && next && <Paper variant="outlined" sx={{ p: 2 }}>
      <Typography variant="overline">Next pick · {open.length} left</Typography>
      <Typography sx={{ fontFamily: mono, fontSize: 26, fontWeight: 700 }}>{next.locationCode}</Typography>
      <Typography>{next.partNumber} · {next.serialNumber ?? next.lotNumber ?? ''} · <b>{qty(next.task.quantity)}</b></Typography>
      <Stack gap={1.5} sx={{ mt: 1.5 }}>
        <Step n={1} label="Location" value={loc}><ScanField label="Scan pick location" onScan={setLoc} autoFocus /></Step>
        {loc && <Step n={2} label="Item"><ScanField label="Scan the lot / serial picked" onScan={pick} autoFocus /></Step>}
      </Stack>
    </Paper>}
    {id && d.data && !next && d.data.picks.length > 0 && d.data.shipment.status < 5 && <Stack gap={1.5}>
      <Typography sx={{ fontWeight: 600 }}>All picks done. Scan cartons to verify.</Typography>
      <ScanField label="Scan carton / item" onScan={(c) => setVerify([...verify, c])} autoFocus />
      {verify.map((v, i) => <Typography key={i} sx={{ fontFamily: mono }}>{v}</Typography>)}
      <Big disabled={!verify.length} onClick={doVerify}>Verify</Big>
    </Stack>}
    {id && d.data && d.data.picks.length === 0 && <Alert severity="info">No pick list yet — build it on the shipment.</Alert>}
    {id && <Button component={RouterLink} to={`/shipping/shipments/${id}`}>Open shipment</Button>}
  </Stack>;
}

function TraceFlow({ onAction }: { onAction: (a: string, code: string) => void }) {
  const nav = useNavigate();
  const [ctx, setCtx] = useState<Ctx | null>(null); const [err, setErr] = useState<string | null>(null);
  const go = async (c: string) => { setErr(null); try { setCtx(await scan(c)); } catch (e) { setErr((e as Error).message); setCtx(null); } };
  return <Stack gap={2}>
    <ScanField label="Scan anything" onScan={go} autoFocus helper="Lot, serial, location, part, work order, receipt, shipment or carton" />
    {err && <Alert severity="error">{err}</Alert>}
    {ctx && <>
      <Box><Typography variant="overline" color="text.secondary">{ctx.match.kind}</Typography><Typography sx={{ fontFamily: mono, fontSize: 22, fontWeight: 700 }}>{ctx.match.number ?? ctx.match.raw}</Typography>{ctx.match.detail && <Typography color="text.secondary">{ctx.match.detail}</Typography>}</Box>
      {ctx.holds.map((h) => <Alert key={h.id} severity="error" action={<Button color="inherit" component={RouterLink} to={h.url}>Open</Button>}>On hold {h.number}: {h.title}</Alert>)}
      {ctx.stock.length > 0 && <Stack gap={0.5}>{ctx.stock.map((s) => <Stack key={s.id} direction="row" justifyContent="space-between" alignItems="center" sx={{ py: 0.75, borderBottom: 1, borderColor: 'divider' }}>
        <Box><Typography sx={{ fontFamily: mono }}>{s.locationCode}</Typography><Typography variant="caption" color="text.secondary">{s.partNumber} {s.serialNumber ?? s.lotNumber ?? ''}</Typography></Box>
        <Stack direction="row" gap={1} alignItems="center"><StockBadge status={s.status} held={s.held} /><b>{qty(s.onHand)}</b></Stack></Stack>)}</Stack>}
      {ctx.related.length > 0 && <Stack gap={0.5}>{ctx.related.map((r) => <Button key={`${r.type}${r.id}`} component={RouterLink} to={r.url} sx={{ justifyContent: 'space-between' }}><span>{r.number} · {r.title}</span><span>{r.status}</span></Button>)}</Stack>}
      <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 1 }}>
        {ctx.actions.filter((a) => ['move', 'issue', 'ship', 'inspect', 'receive'].includes(a)).map((a) => <Big key={a} onClick={() => onAction(a, ctx.match.raw)}>{a[0].toUpperCase() + a.slice(1)}</Big>)}
        {ctx.match.url && <Big color="inherit" onClick={() => nav(ctx.match.url!)}>Open record</Big>}
      </Box>
    </>}
  </Stack>;
}
