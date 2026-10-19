import { setupOptions } from '../components/SetupLink';
import { useState } from 'react';
import { Alert, Box, Button, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { get, post, put } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { Section, StepDialog } from '../components/Kit';
import { SitePicker } from '../components/SitePicker';
import { fmtDate, fmtDateTime } from '../components/Status';
import { mono } from '../theme';
import { useCarriers } from '../warehouse/common';

const mod = modules.find((m) => m.key === 'shipping')!;

export const carrierProviders = ['Fixture (simulated)', 'Generic REST', 'UPS', 'FedEx', 'DHL', 'LTL (REST contract)'];
export const carrierEnvironments = ['Sandbox', 'Production'];
const verification = ['Unverified', 'Verified', 'Failed'];
const operations = ['Connection test', 'Address validation', 'Rate', 'Create shipment', 'Label', 'Cancel', 'Track'];
const callResults = ['Succeeded', 'Failed', 'Not supported'];
const labelStatus = ['Active', 'Voided', 'Void failed'];

export interface Address { name?: string | null; line1?: string | null; line2?: string | null; city?: string | null; region?: string | null; postalCode?: string | null; country?: string | null; phone?: string | null; residential: boolean }
interface Capabilities { provider: string; description: string; addressValidation: boolean; rating: boolean; shipmentCreation: boolean; labelRetrieval: boolean; cancellation: boolean; tracking: boolean; sandboxOnly: boolean;
  requiresBaseUrl: boolean; requiresSecret: boolean; labelFormats: string[]; services: { code: string; name: string }[]; unverified: string[]; status: string }
export interface Connection { id: string; carrierId: string; carrier: string; name: string; provider: number; environment: number; baseUrl?: string; accountNumber?: string; clientId?: string; hasSecret: boolean; optionsJson: string;
  shipper: Address; enabled: boolean; verification: number; verifiedAt?: string; verifiedBy?: string; lastTestAt?: string; lastTestMessage?: string; productionApproved: boolean; approvedAt?: string; approvedBy?: string; approvalNotes?: string;
  siteId: string; site: string; capabilities: Capabilities; blockers: string[] }
interface RequestLog { id: string; operation: number; environment: number; attempt: number; result: number; httpStatus?: number; errorCode?: string; message?: string; durationMs: number; at: string; by?: string; connection?: string }
interface LabelRow { id: string; packageNumber: number; container?: string; carrierShipmentId: string; trackingNumber: string; serviceCode: string; format: string; bytes: number; charge?: number; currency?: string; status: number; createdOn: string; voidedAt?: string; voidMessage?: string }
interface Quote { serviceCode: string; serviceName: string; charge: number; currency: string; transitDays?: number; estimatedDelivery?: string }
interface CarrierView { address: Address; addressValidatedAt?: string; addressValidationNote?: string; connectionId?: string; connection?: string; environment?: number; serviceCode?: string; serviceLevel?: string; quotedCharge?: number; quotedCurrency?: string;
  labels: LabelRow[]; tracking: { occurredAt: string; trackingNumber: string; code: string; description: string; location?: string; delivered: boolean }[]; requests: RequestLog[]; packages: { number: number; reference?: string; weightKg: number; lengthCm?: number; widthCm?: number; heightCm?: number }[] }

export const useCarrierConnections = (siteId?: string) => useQuery({ queryKey: ['carrier-connections', siteId ?? ''], queryFn: () => get<Connection[]>(`/api/shipping/carrier-connections${siteId ? `?siteId=${siteId}` : ''}`) });
const money = (v?: number | null, cur?: string | null) => (v == null ? '—' : `${v.toFixed(2)} ${cur ?? ''}`.trim());
const addrLine = (a: Address) => [a.name, a.line1, a.line2, a.city, a.region, a.postalCode, a.country].filter(Boolean).join(', ');

function RequestTable({ rows }: { rows: RequestLog[] }) {
  if (rows.length === 0) return <Typography variant="body2" color="text.secondary">No carrier calls yet.</Typography>;
  return <Table size="small" aria-label="Carrier requests"><TableHead><TableRow><TableCell>When</TableCell><TableCell>Operation</TableCell><TableCell>Attempt</TableCell><TableCell>Result</TableCell><TableCell>Detail</TableCell><TableCell>By</TableCell></TableRow></TableHead>
    <TableBody>{rows.map((r) => <TableRow key={r.id}><TableCell>{fmtDateTime(r.at)}</TableCell><TableCell>{operations[r.operation]}{r.connection ? <Typography variant="caption" display="block" color="text.secondary">{r.connection}</Typography> : null}</TableCell>
      <TableCell>{r.attempt}</TableCell><TableCell><Chip size="small" color={r.result === 0 ? 'success' : r.result === 2 ? 'default' : 'error'} label={callResults[r.result]} /></TableCell>
      <TableCell><Typography variant="caption">{[r.httpStatus ? `HTTP ${r.httpStatus}` : null, r.errorCode, r.message, `${r.durationMs} ms`].filter(Boolean).join(' · ')}</Typography></TableCell><TableCell>{r.by ?? 'Scheduled job'}</TableCell></TableRow>)}</TableBody></Table>;
}

const blankAddress: Address = { name: '', line1: '', line2: '', city: '', region: '', postalCode: '', country: 'US', phone: '', residential: false };
function AddressFields({ value, onChange, residential }: { value: Address; onChange: (a: Address) => void; residential?: boolean }) {
  const f = (k: keyof Address) => ({ value: (value[k] as string | null | undefined) ?? '', onChange: (e: React.ChangeEvent<HTMLInputElement>) => onChange({ ...value, [k]: e.target.value }) });
  return <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 1.5 }}>
    <TextField size="small" label="Name / company" {...f('name')} /><TextField size="small" label="Phone" {...f('phone')} />
    <TextField size="small" label="Street" {...f('line1')} /><TextField size="small" label="Street (line 2)" {...f('line2')} />
    <TextField size="small" label="City" {...f('city')} /><TextField size="small" label="State / region" {...f('region')} />
    <TextField size="small" label="Postal code" {...f('postalCode')} /><TextField size="small" label="Country (2 letters)" {...f('country')} slotProps={{ htmlInput: { maxLength: 2 } }} />
    {residential && <FormControlLabel control={<Checkbox checked={value.residential} onChange={(e) => onChange({ ...value, residential: e.target.checked })} />} label="Residential address" />}
  </Box>;
}

function ConnectionDialog({ open, edit, onClose, onSaved }: { open: boolean; edit?: Connection; onClose: () => void; onSaved: () => void }) {
  const carriers = useCarriers();
  const [v, setV] = useState(() => ({ carrierId: edit?.carrierId ?? '', name: edit?.name ?? '', provider: String(edit?.provider ?? 0), environment: String(edit?.environment ?? 0), baseUrl: edit?.baseUrl ?? '', accountNumber: edit?.accountNumber ?? '',
    clientId: edit?.clientId ?? '', secret: '', clearSecret: false, optionsJson: edit?.optionsJson ?? '{}', siteId: edit?.siteId ?? '' }));
  const [shipper, setShipper] = useState<Address>(edit?.shipper ?? blankAddress);
  const [err, setErr] = useState<string | null>(null);
  const set = (k: string) => (e: React.ChangeEvent<HTMLInputElement>) => setV({ ...v, [k]: e.target.value });
  const p = Number(v.provider);
  const save = async () => {
    setErr(null);
    const body = { ...v, provider: p, environment: Number(v.environment), baseUrl: v.baseUrl || null, accountNumber: v.accountNumber || null, clientId: v.clientId || null, secret: v.secret || null, shipper };
    try { if (edit) await put(`/api/shipping/carrier-connections/${edit.id}`, body); else await post('/api/shipping/carrier-connections', body); onSaved(); onClose(); } catch (e) { setErr((e as Error).message); }
  };
  return <Dialog open={open} onClose={onClose} maxWidth="md" fullWidth>
    <DialogTitle>{edit ? `Edit ${edit.name}` : 'Add carrier connection'}</DialogTitle>
    <DialogContent><Stack gap={2} sx={{ pt: 1 }}>
      {err && <Alert severity="error">{err}</Alert>}
      <Alert severity="info">New connections are disabled and unverified. Changing any setting later switches the connection off until a new test passes.</Alert>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 1.5 }}>
        <TextField select size="small" label="Carrier" value={v.carrierId} onChange={set('carrierId')}>{(carriers.data ?? []).filter((c) => c.active || c.id === v.carrierId).map((c) => <MenuItem key={c.id} value={c.id}>{c.name}</MenuItem>)}{setupOptions('carrier', carriers.data)}</TextField>
        <TextField size="small" label="Connection name" value={v.name} onChange={set('name')} />
        <TextField select size="small" label="Adapter" value={v.provider} onChange={set('provider')}>{carrierProviders.map((x, i) => <MenuItem key={x} value={String(i)}>{x}</MenuItem>)}</TextField>
        <TextField select size="small" label="Environment" value={v.environment} onChange={set('environment')} helperText={p === 0 ? 'The fixture carrier runs in sandbox only.' : undefined}>
          {carrierEnvironments.map((x, i) => <MenuItem key={x} value={String(i)} disabled={p === 0 && i === 1}>{x}</MenuItem>)}</TextField>
        {(p === 1 || p === 5) && <TextField size="small" label="Endpoint (https://)" value={v.baseUrl} onChange={set('baseUrl')} sx={{ gridColumn: { sm: 'span 2' } }} />}
        <TextField size="small" label="Account number" value={v.accountNumber} onChange={set('accountNumber')} />
        <TextField size="small" label="Client id" value={v.clientId} onChange={set('clientId')} />
        <TextField size="small" type="password" label={edit?.hasSecret ? 'Secret (stored; leave blank to keep)' : 'Secret / API key'} value={v.secret} onChange={set('secret')} autoComplete="new-password" helperText="Encrypted when saved. It is never shown again." />
        {edit?.hasSecret && <FormControlLabel control={<Checkbox checked={v.clearSecret} onChange={(e) => setV({ ...v, clearSecret: e.target.checked })} />} label="Remove the stored secret" />}
        <Box sx={{ gridColumn: { sm: 'span 2' } }}><SitePicker permission="integrations.edit" value={v.siteId} onChange={(id) => setV({ ...v, siteId: id ?? '' })} label="Site" required /></Box>
        <TextField size="small" label="Adapter options (JSON)" value={v.optionsJson} onChange={set('optionsJson')} multiline minRows={2} sx={{ gridColumn: { sm: 'span 2' }, '& textarea': { fontFamily: mono } }}
          helperText="For example paths, authStyle, headerName, labelFormat, freightClass or tokenUrl. Credentials go in the secret field." />
      </Box>
      <Typography variant="subtitle2">Shipper address (as registered with the carrier account)</Typography>
      <AddressFields value={shipper} onChange={setShipper} />
    </Stack></DialogContent>
    <DialogActions><Button onClick={onClose}>Cancel</Button><Button variant="contained" onClick={save}>Save</Button></DialogActions>
  </Dialog>;
}

function CapabilityChips({ c }: { c: Capabilities }) {
  const ops: [string, boolean][] = [['Address validation', c.addressValidation], ['Rates', c.rating], ['Shipments & labels', c.shipmentCreation], ['Label retrieval', c.labelRetrieval], ['Void', c.cancellation], ['Tracking', c.tracking]];
  return <Stack direction="row" gap={0.5} flexWrap="wrap">{ops.map(([l, on]) => <Chip key={l} size="small" variant={on ? 'filled' : 'outlined'} color={on ? 'primary' : 'default'} label={`${on ? '✓' : '✕'} ${l}`} />)}</Stack>;
}

function ConnectionCard({ c, onEdit, refresh }: { c: Connection; onEdit: () => void; refresh: () => void }) {
  const { can } = useAuth();
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const [showLog, setShowLog] = useState(false);
  const [approve, setApprove] = useState(false);
  const log = useQuery({ queryKey: ['carrier-log', c.id], queryFn: () => get<RequestLog[]>(`/api/shipping/carrier-connections/${c.id}/requests?take=50`), enabled: showLog });
  const run = async (f: () => Promise<unknown>, ok: string) => { setMsg(null); try { await f(); setMsg({ ok: true, text: ok }); refresh(); log.refetch(); } catch (e) { setMsg({ ok: false, text: (e as Error).message }); } };
  const edit = can('integrations.edit');
  return <Paper sx={{ p: 2, borderLeft: 4, borderColor: c.enabled ? 'success.main' : c.verification === 2 ? 'error.main' : 'divider' }} data-testid={`carrier-connection-${c.name}`}>
    <Stack direction={{ xs: 'column', md: 'row' }} gap={2} alignItems={{ md: 'flex-start' }}>
      <Box sx={{ flex: 1 }}>
        <Stack direction="row" gap={1} alignItems="center" flexWrap="wrap">
          <Typography variant="h3">{c.name}</Typography>
          <Chip size="small" label={carrierProviders[c.provider]} />
          <Chip size="small" color={c.environment === 1 ? 'warning' : 'default'} variant="outlined" label={carrierEnvironments[c.environment]} />
          <Chip size="small" color={c.verification === 1 ? 'success' : c.verification === 2 ? 'error' : 'default'} label={verification[c.verification]} />
          <Chip size="small" color={c.enabled ? 'success' : 'default'} variant={c.enabled ? 'filled' : 'outlined'} label={c.enabled ? 'Enabled' : 'Disabled'} />
          {c.environment === 1 && <Chip size="small" variant="outlined" color={c.productionApproved ? 'success' : 'default'} label={c.productionApproved ? `Production approved${c.approvedBy ? ` by ${c.approvedBy}` : ''}` : 'Production approval needed'} />}
        </Stack>
        <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>{c.carrier} · {c.site}{c.accountNumber ? ` · account ${c.accountNumber}` : ''}{c.baseUrl ? ` · ${c.baseUrl}` : ''}{c.hasSecret ? ' · secret stored' : ''}</Typography>
        <Typography variant="body2" sx={{ mt: 1 }}>{c.capabilities.description}</Typography>
        <Box sx={{ mt: 1 }}><CapabilityChips c={c.capabilities} /></Box>
        {c.lastTestAt && <Typography variant="caption" display="block" sx={{ mt: 1 }}>Last test {fmtDateTime(c.lastTestAt)}: {c.lastTestMessage}{c.verifiedBy ? ` (verified by ${c.verifiedBy})` : ''}</Typography>}
        {c.capabilities.unverified.length > 0 && <Alert severity="warning" sx={{ mt: 1 }}><b>Unverified:</b> {c.capabilities.unverified.join(' ')}</Alert>}
        {!c.enabled && c.blockers.length > 0 && <Alert severity="info" sx={{ mt: 1 }}><b>Before it can be enabled:</b><ul style={{ margin: 0, paddingLeft: 18 }}>{c.blockers.map((b) => <li key={b}>{b}</li>)}</ul></Alert>}
        {msg && <Alert severity={msg.ok ? 'success' : 'error'} sx={{ mt: 1 }} onClose={() => setMsg(null)}>{msg.text}</Alert>}
      </Box>
      <Stack gap={1} sx={{ minWidth: 190 }}>
        {edit && <Button variant="outlined" onClick={() => run(async () => { const r = await post<Connection>(`/api/shipping/carrier-connections/${c.id}/test`); if (r.verification !== 1) throw new Error(r.lastTestMessage ?? 'The test failed.'); }, 'Connection test passed for this configuration.')}>Test connection</Button>}
        {edit && (c.enabled
          ? <Button color="inherit" onClick={() => run(() => put(`/api/shipping/carrier-connections/${c.id}/enabled`, { enabled: false }), 'Connection disabled.')}>Disable</Button>
          : <Button variant="contained" disabled={c.blockers.length > 0} onClick={() => run(() => put(`/api/shipping/carrier-connections/${c.id}/enabled`, { enabled: true }), 'Connection enabled.')}>Enable</Button>)}
        {c.environment === 1 && !c.productionApproved && can('integrations.approve') && <Button variant="outlined" disabled={c.verification !== 1} onClick={() => setApprove(true)}>Approve for production</Button>}
        {edit && <Button color="inherit" onClick={onEdit}>Edit</Button>}
        <Button size="small" color="inherit" onClick={() => setShowLog(!showLog)}>{showLog ? 'Hide request log' : 'Request log'}</Button>
      </Stack>
    </Stack>
    {showLog && <Box sx={{ mt: 2 }}><RequestTable rows={log.data ?? []} /></Box>}
    <StepDialog open={approve} title={`Approve ${c.name} for production`} confirm="Approve" onClose={() => setApprove(false)}
      intro="You approve the tested configuration for live shipments. The person who ran the test cannot approve it. Any later change needs a new test and approval."
      inputs={[{ name: 'notes', label: 'What you reviewed', type: 'multiline' }]}
      onConfirm={async (v) => { await post(`/api/shipping/carrier-connections/${c.id}/production-approval`, { notes: v.notes }); refresh(); }} />
  </Paper>;
}

export function CarrierConnections() {
  const { can } = useAuth();
  const qc = useQueryClient();
  const list = useCarrierConnections();
  const [dlg, setDlg] = useState<Connection | 'new' | null>(null);
  const refresh = () => qc.invalidateQueries({ queryKey: ['carrier-connections'] });
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Carrier connections' }]} title="Carrier connections"
      actions={can('integrations.edit') && <Button variant="contained" onClick={() => setDlg('new')}>Add connection</Button>}>
      <Alert severity="info" sx={{ mb: 2 }}>Connections start disabled and in sandbox mode. A connection is enabled only after a test passes for its exact configuration; production also needs approval by a second person. A passing test is not a carrier certification.</Alert>
      {list.error && <Alert severity="error">{(list.error as Error).message}</Alert>}
      <Stack gap={2}>
        {(list.data ?? []).map((c) => <ConnectionCard key={c.id} c={c} onEdit={() => setDlg(c)} refresh={refresh} />)}
        {list.data?.length === 0 && <Paper sx={{ p: 3 }}><Typography color="text.secondary">No carrier connections yet. Add the fixture carrier to try the flow in sandbox mode, or a generic REST connection to your integration middleware.</Typography></Paper>}
      </Stack>
      {dlg && <ConnectionDialog open edit={dlg === 'new' ? undefined : dlg} onClose={() => setDlg(null)} onSaved={refresh} />}
    </ModuleWorkspace>
  );
}

/** Shipment › Carrier tab: delivery address, validation, rates and service, labels, void, tracking and the carrier request log. */
export function ShipmentCarrierPanel({ shipmentId, siteId, status, edit, onChanged }: { shipmentId: string; siteId: string; status: number; edit: boolean; onChanged: () => void }) {
  const qc = useQueryClient();
  const v = useQuery({ queryKey: ['shipment-carrier', shipmentId], queryFn: () => get<CarrierView>(`/api/shipping/shipments/${shipmentId}/carrier`) });
  const conns = useCarrierConnections(siteId);
  const enabled = (conns.data ?? []).filter((c) => c.enabled);
  const [conn, setConn] = useState('');
  const [addr, setAddr] = useState<Address | null>(null);
  const [quotes, setQuotes] = useState<Quote[] | null>(null);
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const [voidDlg, setVoidDlg] = useState(false);
  if (!v.data) return v.error ? <Alert severity="error">{(v.error as Error).message}</Alert> : null;
  const d = v.data;
  const chosen = conn || d.connectionId || enabled[0]?.id || '';
  const open = status < 5;
  const active = d.labels.filter((l) => l.status === 0);
  // a blank ship-to country defaults to the shipper's country on the chosen connection (domestic is the common case)
  const a = addr ?? (d.address.country ? d.address : { ...d.address, country: (conns.data ?? []).find((c) => c.id === chosen)?.shipper.country || 'US' });
  const refresh = () => { qc.invalidateQueries({ queryKey: ['shipment-carrier', shipmentId] }); onChanged(); };
  const run = async (f: () => Promise<unknown>, ok: string | ((r: unknown) => string)) => { setMsg(null); try { const r = await f(); setMsg({ ok: true, text: typeof ok === 'string' ? ok : ok(r) }); refresh(); } catch (e) { setMsg({ ok: false, text: (e as Error).message }); } };
  const env = (conns.data ?? []).find((c) => c.id === chosen)?.environment;
  return <Stack gap={2}>
    {msg && <Alert severity={msg.ok ? 'success' : 'error'} onClose={() => setMsg(null)}>{msg.text}</Alert>}
    {enabled.length === 0 && <Alert severity="info">No enabled carrier connection at this site. Labels can still be made by hand; add a connection under Carrier connections.</Alert>}
    {env === 0 && <Alert severity="warning">Sandbox connection: rates and labels are test results, not real shipments.</Alert>}
    <Section title="Delivery address" action={d.addressValidatedAt ? <Chip size="small" color="success" label={`Validated ${fmtDateTime(d.addressValidatedAt)}`} /> : <Chip size="small" variant="outlined" label="Not validated" />}>
      <Stack gap={1.5}>
        {edit && open && active.length === 0 ? <AddressFields value={a} onChange={setAddr} residential /> : <Typography>{addrLine(d.address) || '—'}{d.address.residential ? ' (residential)' : ''}</Typography>}
        {d.addressValidationNote && <Typography variant="body2" color="text.secondary">{d.addressValidationNote}</Typography>}
        {edit && open && <Stack direction="row" gap={1} flexWrap="wrap" alignItems="center">
          {active.length === 0 && <Button variant="outlined" disabled={!addr} onClick={() => run(async () => { await put(`/api/shipping/shipments/${shipmentId}/carrier/ship-to`, { address: a }); setAddr(null); }, 'Address saved.')}>Save address</Button>}
          <TextField select size="small" label="Carrier connection" value={chosen} onChange={(e) => { setConn(e.target.value); setQuotes(null); }} sx={{ minWidth: 240 }}>
            {enabled.map((c) => <MenuItem key={c.id} value={c.id}>{c.name} ({carrierEnvironments[c.environment]})</MenuItem>)}</TextField>
          {active.length === 0 && <Button variant="outlined" disabled={!chosen || !!addr} onClick={() => run(() => post<{ valid: boolean; messages: string[] }>(`/api/shipping/shipments/${shipmentId}/carrier/validate-address`, { connectionId: chosen }),
            (r) => { const x = r as { valid: boolean; messages: string[] }; return x.valid ? `Address valid${x.messages.length ? ': ' + x.messages.join(' ') : '.'}` : `Address not valid: ${x.messages.join(' ')}`; })}>Validate with carrier</Button>}
        </Stack>}
      </Stack>
    </Section>
    <Section title="Rates & service">
      <Stack gap={1.5}>
        <Typography variant="body2" color="text.secondary">Packages: {d.packages.length === 0 ? 'none yet (pack the shipment first)' : d.packages.map((p) => `${p.reference ?? p.number} ${p.weightKg} kg${p.lengthCm ? ` ${p.lengthCm}×${p.widthCm}×${p.heightCm} cm` : ''}`).join(' · ')}</Typography>
        {d.serviceCode && <Typography>Selected: <b>{d.serviceLevel}</b> <span style={{ fontFamily: mono }}>({d.serviceCode})</span> via {d.connection} · {money(d.quotedCharge, d.quotedCurrency)}</Typography>}
        {edit && open && active.length === 0 && <Box><Button variant="outlined" disabled={!chosen} onClick={() => run(async () => setQuotes(await post<Quote[]>(`/api/shipping/shipments/${shipmentId}/carrier/rates`, { connectionId: chosen })), 'Rates received.')}>Get rates</Button></Box>}
        {quotes && <Table size="small" aria-label="Rate quotes"><TableHead><TableRow><TableCell>Service</TableCell><TableCell align="right">Charge</TableCell><TableCell>Transit</TableCell><TableCell /></TableRow></TableHead>
          <TableBody>{quotes.map((q) => <TableRow key={q.serviceCode}><TableCell>{q.serviceName} <Typography variant="caption" sx={{ fontFamily: mono }}>{q.serviceCode}</Typography></TableCell><TableCell align="right">{money(q.charge, q.currency)}</TableCell>
            <TableCell>{q.transitDays != null ? `${q.transitDays} day(s)` : '—'}{q.estimatedDelivery ? ` · ${fmtDate(q.estimatedDelivery)}` : ''}</TableCell>
            <TableCell><Button size="small" onClick={() => run(async () => { await post(`/api/shipping/shipments/${shipmentId}/carrier/service`, { connectionId: chosen, serviceCode: q.serviceCode }); setQuotes(null); }, `${q.serviceName} selected.`)}>Select</Button></TableCell></TableRow>)}</TableBody></Table>}
      </Stack>
    </Section>
    <Section title="Carrier labels" action={edit && <Stack direction="row" gap={1}>
      {open && status >= 3 && active.length === 0 && <Button variant="contained" disabled={!d.serviceCode} onClick={() => run(() => post(`/api/shipping/shipments/${shipmentId}/carrier/labels`), 'Carrier labels created.')}>Create labels</Button>}
      {open && active.length > 0 && <Button color="inherit" onClick={() => setVoidDlg(true)}>Void labels</Button>}
      {status === 5 && active.length > 0 && <Button variant="outlined" onClick={() => run(() => post<{ newEvents: number; delivered: boolean }>(`/api/shipping/shipments/${shipmentId}/carrier/track`), (r) => { const x = r as { newEvents: number; delivered: boolean }; return `${x.newEvents} new tracking event(s)${x.delivered ? '; delivered.' : '.'}`; })}>Refresh tracking</Button>}
    </Stack>}>
      {status < 3 && <Typography variant="body2" color="text.secondary">Labels are created once the shipment is packed.</Typography>}
      {d.labels.length > 0 && <Table size="small" aria-label="Carrier labels"><TableHead><TableRow><TableCell>Package</TableCell><TableCell>Tracking</TableCell><TableCell>Service</TableCell><TableCell>Status</TableCell><TableCell>Created</TableCell><TableCell /></TableRow></TableHead>
        <TableBody>{d.labels.map((l) => <TableRow key={l.id}><TableCell>{l.packageNumber}{l.container ? <Typography variant="caption" display="block" sx={{ fontFamily: mono }}>{l.container}</Typography> : null}</TableCell>
          <TableCell sx={{ fontFamily: mono }}>{l.trackingNumber}</TableCell><TableCell sx={{ fontFamily: mono }}>{l.serviceCode}</TableCell>
          <TableCell><Chip size="small" color={l.status === 0 ? 'success' : l.status === 2 ? 'error' : 'default'} label={labelStatus[l.status]} />{l.voidMessage && <Typography variant="caption" display="block" color="text.secondary">{l.voidMessage}</Typography>}</TableCell>
          <TableCell>{fmtDateTime(l.createdOn)}</TableCell><TableCell><Button size="small" href={`/api/shipping/carrier-labels/${l.id}`} target="_blank">{l.format}</Button></TableCell></TableRow>)}</TableBody></Table>}
    </Section>
    {d.tracking.length > 0 && <Section title="Tracking">
      <Stack gap={0.5}>{d.tracking.map((t) => <Stack key={`${t.trackingNumber}-${t.occurredAt}-${t.code}`} direction="row" gap={1.5} alignItems="center">
        <Box sx={{ width: 10, height: 10, borderRadius: '50%', bgcolor: t.delivered ? 'success.main' : 'primary.main', flexShrink: 0 }} />
        <Typography variant="body2" sx={{ minWidth: 150 }}>{fmtDateTime(t.occurredAt)}</Typography><Typography variant="body2" sx={{ flex: 1 }}><b>{t.description}</b>{t.location ? ` · ${t.location}` : ''}</Typography>
        <Typography variant="caption" sx={{ fontFamily: mono }}>{t.trackingNumber}</Typography></Stack>)}</Stack>
    </Section>}
    <Section title="Carrier request log"><RequestTable rows={d.requests} /></Section>
    <StepDialog open={voidDlg} danger title="Void carrier labels" confirm="Void labels" onClose={() => setVoidDlg(false)} intro="The labels are cancelled at the carrier. If the carrier refuses, the labels show 'Void failed' and you can try again."
      inputs={[{ name: 'reason', label: 'Reason' }]} onConfirm={async (x) => { const r = await post<{ failed: number; messages: string[] }>(`/api/shipping/shipments/${shipmentId}/carrier/void`, { reason: x.reason }); refresh(); if (r.failed > 0) throw new Error(r.messages.join(' ')); }} />
  </Stack>;
}
