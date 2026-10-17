import { useState } from 'react';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { Alert, Box, Button, MenuItem, Paper, Stack, Step, StepLabel, Stepper, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Bar, CartesianGrid, Line, ComposedChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { get, post } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, fmtDate, fmtDateTime, localDate, severity } from '../components/Status';
import { Facts, Prose, Section, StepDialog, lookupLabel, useLookup, useRunner, useSites } from '../components/Kit';
import { AttachmentsAndHistory } from '../components/RecordTabs';
import { mono } from '../theme';

const mod = modules.find((m) => m.key === 'field')!;
export const recallStatus = ['Open', 'Investigation', 'Containment', 'Notification', 'PendingClosure', 'Closed', 'Cancelled'];
export const returnStatus = ['Authorized', 'Received', 'Inspected', 'Dispositioned', 'Closed', 'Cancelled'];
export const returnDisposition = ['None', 'Restock', 'Rework', 'Repair', 'Scrap', 'Return to customer', 'Replace', 'Credit'];
export const claimStatus = ['Submitted', 'UnderReview', 'Approved', 'Rejected', 'Paid', 'Closed'];
const mnum = (x: { getValue: () => unknown }) => <span style={{ fontFamily: mono }}>{String(x.getValue() ?? '')}</span>;
const money = (n?: number | null) => (n == null ? '—' : n.toLocaleString(undefined, { style: 'currency', currency: 'USD', maximumFractionDigits: 0 }));

interface Recall { id: string; number: string; title: string; reason: string; riskRating: number; status: number; containment?: string; customerNotification?: string; closureSummary?: string; capaId?: string; ncrId?: string; closedAt?: string; createdAt: string; siteId: string }
interface RecallItem { id: string; productId?: string; partId?: string; lotId?: string; serialId?: string; note?: string }
interface RecallParty { id: string; partyKind: string; partyId: string; quantity: number; lots: string; serials: string; notifiedAt?: string; containedAt?: string; response?: string }
interface Ret { id: string; number: string; customerId: string; productId?: string; partId?: string; serialNumber?: string; lotNumber?: string; quantity: number; returnReason: string; customerReference?: string; status: number; authorizedOn: string; receivedOn?: string; condition?: string; inspectionNotes?: string; failureConfirmed?: boolean; rootCause?: string; correctiveAction?: string; disposition: number; dispositionNotes?: string; complaintId?: string; ncrId?: string; capaId?: string; warrantyClaimId?: string; siteId: string }
interface Claim { id: string; number: string; customerId: string; serialNumber?: string; lotNumber?: string; quantity: number; claimedOn: string; failedOn?: string; failureMode: string; supplierId?: string; laborCost: number; partsCost: number; freightCost: number; otherCost: number; totalCost: number; supplierRecovery?: number; status: number; decision?: string; returnId?: string; complaintId?: string; ncrId?: string; capaId?: string; createdById?: string; siteId: string }

function useNames() {
  const c = useLookup('customer'); const s = useLookup('supplier'); const p = useLookup('part');
  return {
    customer: (id?: string) => { const x = c.data?.find((r) => r.id === id); return x ? lookupLabel(x) : '—'; },
    supplier: (id?: string) => { const x = s.data?.find((r) => r.id === id); return x ? lookupLabel(x) : '—'; },
    part: (id?: string) => { const x = p.data?.find((r) => r.id === id); return x ? lookupLabel(x) : '—'; },
    customers: c.data ?? [], suppliers: s.data ?? [], parts: p.data ?? [],
  };
}
function SiteSelect({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  const sites = useSites();
  return <TextField select label="Site" value={value} onChange={(e) => onChange(e.target.value)}>{(sites.data ?? []).filter((s) => !s.isArchived).map((s) => <MenuItem key={s.id} value={s.id}>{s.code} — {s.name}</MenuItem>)}</TextField>;
}
const Loading = ({ to, label, error }: { to: string; label: string; error: unknown }) =>
  <ModuleWorkspace module={mod} crumbs={[{ label, to }]} title="Loading…">{error ? <Alert severity="error">{(error as Error).message}</Alert> : null}</ModuleWorkspace>;

// ---------------------------------------------------------------- Recalls
export function RecallList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const q = useQuery({ queryKey: ['recalls'], queryFn: () => get<Recall[]>('/api/recalls') });
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Recalls' }]} title="Recalls" actions={can('recalls.edit') ? <Button variant="contained" component={RouterLink} to="/field/recalls/new">Open a recall</Button> : undefined}>
      <DataTable data={q.data ?? []} onRowClick={(r) => nav(`/field/recalls/${r.id}`)} empty="No recalls."
        columns={[{ header: 'Recall', accessorKey: 'number', cell: mnum }, { header: 'Title', accessorKey: 'title' }, { header: 'Risk', accessorFn: (r) => severity[r.riskRating], cell: (x) => <StatusChip status={x.getValue()} /> },
          { header: 'Opened', accessorKey: 'createdAt', cell: (x) => fmtDate(x.getValue()) }, { header: 'Status', accessorFn: (r) => recallStatus[r.status], cell: (x) => <StatusChip status={x.getValue()} /> }]} />
    </ModuleWorkspace>
  );
}

export function RecallNew() {
  const nav = useNavigate();
  const names = useNames();
  const lots = useQuery({ queryKey: ['lots', ''], queryFn: () => get<{ id: string; number: string }[]>('/api/trace/lots') });
  const sers = useQuery({ queryKey: ['serials', ''], queryFn: () => get<{ id: string; serialNumber: string }[]>('/api/trace/serials') });
  const [v, setV] = useState({ title: '', reason: '', riskRating: '1', siteId: '' });
  const [items, setItems] = useState<{ ref: string; note: string }[]>([{ ref: '', note: '' }]);
  const [error, setError] = useState<string | null>(null);
  const refs: [string, string][] = [...(lots.data ?? []).map((l) => [`lot:${l.id}`, `Lot ${l.number}`] as [string, string]), ...(sers.data ?? []).map((s) => [`serial:${s.id}`, `Serial ${s.serialNumber}`] as [string, string]), ...names.parts.map((p) => [`part:${p.id}`, `Part ${lookupLabel(p)}`] as [string, string])];
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Recalls', to: '/field/recalls' }, { label: 'New' }]} title="Open a recall">
      <Paper sx={{ p: 3, maxWidth: 860 }}>
        {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
        <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}>
          <TextField label="Title" value={v.title} onChange={(e) => setV({ ...v, title: e.target.value })} sx={{ gridColumn: '1 / -1' }} />
          <TextField label="Reason" multiline minRows={3} value={v.reason} onChange={(e) => setV({ ...v, reason: e.target.value })} sx={{ gridColumn: '1 / -1' }} />
          <TextField select label="Risk rating" value={v.riskRating} onChange={(e) => setV({ ...v, riskRating: e.target.value })}>{severity.map((s, i) => <MenuItem key={s} value={String(i)}>{s}</MenuItem>)}</TextField>
          <SiteSelect value={v.siteId} onChange={(s) => setV({ ...v, siteId: s })} />
        </Box>
        <Typography variant="overline" color="text.secondary" sx={{ display: 'block', mt: 3 }}>Suspect lots, serials or parts</Typography>
        {items.map((it, i) => <Stack key={i} direction="row" gap={1} sx={{ mb: 1 }}>
          <TextField select size="small" label="Item" value={it.ref} sx={{ minWidth: 320 }} onChange={(e) => setItems(items.map((x, j) => (j === i ? { ...x, ref: e.target.value } : x)))}>{refs.map(([k, l]) => <MenuItem key={k} value={k}>{l}</MenuItem>)}</TextField>
          <TextField size="small" label="Note" value={it.note} sx={{ flex: 1 }} onChange={(e) => setItems(items.map((x, j) => (j === i ? { ...x, note: e.target.value } : x)))} />
          <Button color="error" onClick={() => setItems(items.filter((_, j) => j !== i))}>Remove</Button>
        </Stack>)}
        <Button size="small" onClick={() => setItems([...items, { ref: '', note: '' }])}>Add item</Button>
        <Stack direction="row" gap={1} sx={{ mt: 3 }}>
          <Button variant="contained" onClick={async () => {
            setError(null);
            try {
              const its = items.filter((x) => x.ref).map((x) => { const [k, id] = x.ref.split(':'); return { productId: null, partId: k === 'part' ? id : null, lotId: k === 'lot' ? id : null, serialId: k === 'serial' ? id : null, note: x.note || null }; });
              const r = await post<{ id: string }>('/api/recalls', { ...v, riskRating: Number(v.riskRating), departmentId: null, ownerId: null, items: its });
              nav(`/field/recalls/${r.id}`);
            } catch (e) { setError((e as Error).message); }
          }}>Open recall</Button>
          <Button onClick={() => nav('/field/recalls')}>Cancel</Button>
        </Stack>
      </Paper>
    </ModuleWorkspace>
  );
}

export function RecallDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const { can } = useAuth();
  const names = useNames();
  const [tab, setTab] = useState('parties');
  const [dlg, setDlg] = useState<'contain' | 'close' | 'capa' | null>(null);
  const q = useQuery({ queryKey: ['recall', id], queryFn: () => get<{ recall: Recall; items: RecallItem[]; parties: RecallParty[] }>(`/api/recalls/${id}`) });
  const capas = useQuery({ queryKey: ['capas', ''], queryFn: () => get<{ items: { id: string; number: string; title: string }[] }>('/api/capas?pageSize=200'), enabled: dlg === 'capa' });
  const refresh = () => qc.invalidateQueries({ queryKey: ['recall', id] });
  const { error, busy, run } = useRunner(refresh);
  const r = q.data?.recall;
  if (!r) return <Loading to="/field/recalls" label="Recalls" error={q.error} />;
  const edit = can('recalls.edit') && r.status < 4;
  const customers = q.data!.parties.filter((p) => p.partyKind === 'Customer');
  const done = customers.filter((p) => p.notifiedAt && p.containedAt).length;
  const step = Math.min(r.status, 5);
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Recalls', to: '/field/recalls' }, { label: r.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{r.number}</span><span>{r.title}</span><StatusChip status={recallStatus[r.status]} /></Stack>}
      tabs={[{ label: 'Impacted parties', value: 'parties' }, { label: 'Suspect items', value: 'items' }, { label: 'Files & history', value: 'files' }]} tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1}>
        {edit && r.status <= 1 && <Button variant="contained" disabled={busy} onClick={() => run(() => post(`/api/recalls/${id}/trace`))}>{r.status === 0 ? 'Trace lots' : 'Re-run trace'}</Button>}
        {edit && (r.status === 1 || r.status === 2) && <Button variant="outlined" onClick={() => setDlg('contain')}>Record containment</Button>}
        {edit && <Button onClick={() => setDlg('capa')}>{r.capaId ? 'Change CAPA' : 'Link CAPA'}</Button>}
        {edit && r.status >= 2 && <Button variant="contained" color="secondary" onClick={() => setDlg('close')}>Submit for closure</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <Paper sx={{ p: 2, mb: 2 }}>
        <Stepper activeStep={step} alternativeLabel>{['Opened', 'Traced', 'Contained', 'Customers notified', 'Closure signed', 'Closed'].map((l) => <Step key={l}><StepLabel>{l}</StepLabel></Step>)}</Stepper>
      </Paper>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '300px 1fr' }, gap: 2 }}>
        <Facts items={[['Risk', severity[r.riskRating]], ['Customers contained', `${done} / ${customers.length}`], ['CAPA', r.capaId ? <RouterLink to={`/capa/${r.capaId}`}>Open CAPA</RouterLink> : '—'], ['Closed', fmtDateTime(r.closedAt)]]}>
          <Box sx={{ mt: 1 }}><Prose label="Reason" text={r.reason} /><Prose label="Containment" text={r.containment} /><Prose label="Closure summary" text={r.closureSummary} /></Box>
        </Facts>
        <Box>
          {tab === 'parties' && <Paper variant="outlined"><Table size="small">
            <TableHead><TableRow><TableCell>Party</TableCell><TableCell>Kind</TableCell><TableCell>Qty</TableCell><TableCell>Lots / serials</TableCell><TableCell>Notified</TableCell><TableCell>Contained</TableCell><TableCell>Response</TableCell><TableCell /></TableRow></TableHead>
            <TableBody>{q.data!.parties.map((p) => <TableRow key={p.id}>
              <TableCell>{p.partyKind === 'Customer' ? names.customer(p.partyId) : names.supplier(p.partyId)}</TableCell><TableCell>{p.partyKind}</TableCell><TableCell sx={{ fontFamily: mono }}>{p.quantity}</TableCell>
              <TableCell sx={{ fontFamily: mono, fontSize: 12 }}>{[p.lots, p.serials].filter(Boolean).join(' · ')}</TableCell><TableCell>{fmtDate(p.notifiedAt)}</TableCell><TableCell>{fmtDate(p.containedAt)}</TableCell><TableCell>{p.response ?? ''}</TableCell>
              <TableCell>{edit && p.partyKind === 'Customer' && <Stack direction="row" gap={0.5}>
                {!p.notifiedAt && <Button size="small" onClick={() => run(() => post(`/api/recalls/${id}/parties`, { partyId: p.id, notified: true, contained: !!p.containedAt, response: p.response ?? null }))}>Notified</Button>}
                {!p.containedAt && <Button size="small" onClick={() => run(() => post(`/api/recalls/${id}/parties`, { partyId: p.id, notified: !!p.notifiedAt, contained: true, response: p.response ?? null }))}>Contained</Button>}
              </Stack>}</TableCell></TableRow>)}
              {q.data!.parties.length === 0 && <TableRow><TableCell colSpan={8}><Typography color="text.secondary" sx={{ py: 2, textAlign: 'center' }}>Trace the suspect lots to find impacted customers and suppliers.</Typography></TableCell></TableRow>}
            </TableBody></Table></Paper>}
          {tab === 'items' && <DataTable data={q.data!.items} columns={[{ header: 'Lot', accessorKey: 'lotId', cell: (x) => x.getValue() ? <RouterLink to={`/trace/lots/${x.getValue()}`}>Open lot</RouterLink> : '' },
            { header: 'Serial', accessorKey: 'serialId', cell: (x) => x.getValue() ? <RouterLink to={`/trace/serials/${x.getValue()}`}>Open serial</RouterLink> : '' }, { header: 'Part', accessorFn: (i) => (i.partId ? names.part(i.partId) : '') }, { header: 'Note', accessorKey: 'note' }]} />}
          {tab === 'files' && <AttachmentsAndHistory type="Recall" id={id!} canEdit={edit} />}
        </Box>
      </Box>
      <StepDialog open={dlg === 'contain'} title="Record containment" confirm="Contain lots" onClose={() => setDlg(null)} intro="Every suspect lot goes on hold."
        inputs={[{ name: 'notes', label: 'Containment actions', type: 'multiline' }]} onConfirm={async (v) => { await post(`/api/recalls/${id}/contain`, v); refresh(); }} />
      <StepDialog open={dlg === 'close'} title="Submit for closure" confirm="Submit" onClose={() => setDlg(null)} intro="Closure is e-signed through the Recall approval route. All customers must be notified and contained, and a CAPA linked."
        inputs={[{ name: 'notes', label: 'Closure summary', type: 'multiline' }]} onConfirm={async (v) => { await post(`/api/recalls/${id}/close`, v); refresh(); }} />
      <StepDialog open={dlg === 'capa'} title="Link corrective action" confirm="Link" onClose={() => setDlg(null)}
        inputs={[{ name: 'capaId', label: 'CAPA', type: 'select', options: (capas.data?.items ?? []).map((c) => [c.id, `${c.number} — ${c.title}`]) }]} onConfirm={async (v) => { await post(`/api/recalls/${id}/capa`, v); refresh(); }} />
    </ModuleWorkspace>
  );
}

// ---------------------------------------------------------------- Returns
export function ReturnList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const names = useNames();
  const q = useQuery({ queryKey: ['returns'], queryFn: () => get<Ret[]>('/api/returns') });
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Returns' }]} title="Customer returns (RMA)" actions={can('returns.edit') ? <Button variant="contained" component={RouterLink} to="/field/returns/new">Authorize a return</Button> : undefined}>
      <DataTable data={q.data ?? []} onRowClick={(r) => nav(`/field/returns/${r.id}`)} empty="No returns."
        columns={[{ header: 'RMA', accessorKey: 'number', cell: mnum }, { header: 'Customer', accessorFn: (r) => names.customer(r.customerId) }, { header: 'Serial / lot', accessorFn: (r) => [r.serialNumber, r.lotNumber].filter(Boolean).join(' · ') },
          { header: 'Qty', accessorKey: 'quantity' }, { header: 'Reason', accessorKey: 'returnReason' }, { header: 'Authorized', accessorKey: 'authorizedOn', cell: (x) => fmtDate(x.getValue()) },
          { header: 'Status', accessorFn: (r) => returnStatus[r.status], cell: (x) => <StatusChip status={x.getValue()} /> }]} />
    </ModuleWorkspace>
  );
}

export function ReturnNew() {
  const nav = useNavigate();
  const names = useNames();
  const [v, setV] = useState({ customerId: '', partId: '', serialNumber: '', lotNumber: '', quantity: '1', returnReason: '', customerReference: '', siteId: '' });
  const [error, setError] = useState<string | null>(null);
  const set = (k: string) => (e: { target: { value: string } }) => setV({ ...v, [k]: e.target.value });
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Returns', to: '/field/returns' }, { label: 'New' }]} title="Authorize a return">
      <Paper sx={{ p: 3, maxWidth: 820, display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}>
        {error && <Alert severity="error" sx={{ gridColumn: '1 / -1' }}>{error}</Alert>}
        <TextField select label="Customer" value={v.customerId} onChange={set('customerId')}>{names.customers.map((c) => <MenuItem key={c.id} value={c.id}>{lookupLabel(c)}</MenuItem>)}</TextField>
        <TextField select label="Part" value={v.partId} onChange={set('partId')}><MenuItem value="">—</MenuItem>{names.parts.map((p) => <MenuItem key={p.id} value={p.id}>{lookupLabel(p)}</MenuItem>)}</TextField>
        <TextField label="Serial number" value={v.serialNumber} onChange={set('serialNumber')} />
        <TextField label="Lot number" value={v.lotNumber} onChange={set('lotNumber')} />
        <TextField label="Quantity" type="number" value={v.quantity} onChange={set('quantity')} />
        <TextField label="Customer reference" value={v.customerReference} onChange={set('customerReference')} />
        <TextField label="Reason for return" multiline minRows={3} value={v.returnReason} onChange={set('returnReason')} sx={{ gridColumn: '1 / -1' }} />
        <SiteSelect value={v.siteId} onChange={(s) => setV({ ...v, siteId: s })} />
        <Stack direction="row" gap={1} sx={{ gridColumn: '1 / -1' }}>
          <Button variant="contained" onClick={async () => {
            setError(null);
            const o = Object.fromEntries(Object.entries(v).map(([k, x]) => [k, x === '' ? null : x]));
            try { const r = await post<{ id: string }>('/api/returns', { ...o, quantity: Number(v.quantity), productId: null, complaintId: null, departmentId: null, ownerId: null }); nav(`/field/returns/${r.id}`); }
            catch (e) { setError((e as Error).message); }
          }}>Authorize return</Button>
          <Button onClick={() => nav('/field/returns')}>Cancel</Button>
        </Stack>
      </Paper>
    </ModuleWorkspace>
  );
}

export function ReturnDetail() {
  const { id } = useParams();
  const nav = useNavigate();
  const qc = useQueryClient();
  const { can } = useAuth();
  const names = useNames();
  const [tab, setTab] = useState('details');
  const [dlg, setDlg] = useState<'receive' | 'inspect' | 'disposition' | null>(null);
  const q = useQuery({ queryKey: ['return', id], queryFn: () => get<Ret>(`/api/returns/${id}`) });
  const refresh = () => qc.invalidateQueries({ queryKey: ['return', id] });
  const { error, busy, run } = useRunner(refresh);
  const r = q.data;
  if (!r) return <Loading to="/field/returns" label="Returns" error={q.error} />;
  const edit = can('returns.edit') && r.status < 4;
  const link = (label: string, url?: string, idv?: string) => (idv ? <RouterLink to={`${url}/${idv}`}>{label}</RouterLink> : '—');
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Returns', to: '/field/returns' }, { label: r.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{r.number}</span><StatusChip status={returnStatus[r.status]} /></Stack>}
      tabs={[{ label: 'Details', value: 'details' }, { label: 'Files & history', value: 'files' }]} tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1} flexWrap="wrap">
        {edit && r.status === 0 && <Button variant="contained" onClick={() => setDlg('receive')}>Receive</Button>}
        {edit && r.status === 1 && <Button variant="contained" onClick={() => setDlg('inspect')}>Record inspection</Button>}
        {edit && (r.status === 1 || r.status === 2) && !r.ncrId && <Button disabled={busy} onClick={() => run(async () => { const x = await post<{ id: string }>(`/api/returns/${id}/ncr`); nav(`/ncr/${x.id}`); })}>Raise NCR</Button>}
        {edit && !r.complaintId && <Button disabled={busy} onClick={() => run(() => post(`/api/returns/${id}/complaint`))}>Log complaint</Button>}
        {edit && r.status >= 1 && !r.capaId && <Button disabled={busy} onClick={() => run(() => post(`/api/returns/${id}/capa`))}>Open CAPA</Button>}
        {edit && r.status === 2 && <Button variant="contained" onClick={() => setDlg('disposition')}>Disposition</Button>}
        {edit && r.status === 3 && <Button variant="contained" color="secondary" disabled={busy} onClick={() => run(() => post(`/api/returns/${id}/close`))}>Close</Button>}
        {can('returns.edit') && !r.warrantyClaimId && r.status >= 1 && <Button component={RouterLink} to={`/field/warranty/new?returnId=${r.id}`}>Warranty claim</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <Paper sx={{ p: 2, mb: 2 }}><Stepper activeStep={Math.min(r.status, 4)} alternativeLabel>{['Authorized', 'Received', 'Inspected', 'Dispositioned', 'Closed'].map((l) => <Step key={l}><StepLabel>{l}</StepLabel></Step>)}</Stepper></Paper>
      {tab === 'details' && <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '300px 1fr' }, gap: 2 }}>
        <Facts items={[['Customer', names.customer(r.customerId)], ['Part', names.part(r.partId)], ['Serial', r.serialNumber ?? '—'], ['Lot', r.lotNumber ?? '—'], ['Quantity', String(r.quantity)], ['Customer ref', r.customerReference ?? '—'],
          ['Authorized', fmtDate(r.authorizedOn)], ['Received', fmtDate(r.receivedOn)], ['Condition', r.condition ?? '—'], ['Failure confirmed', r.failureConfirmed == null ? '—' : r.failureConfirmed ? 'Yes' : 'No'], ['Disposition', returnDisposition[r.disposition]]]} />
        <Stack gap={2}>
          <Section title="Investigation"><Prose label="Reason for return" text={r.returnReason} /><Prose label="Inspection" text={r.inspectionNotes} /><Prose label="Root cause" text={r.rootCause} /><Prose label="Corrective action" text={r.correctiveAction} /><Prose label="Disposition notes" text={r.dispositionNotes} /></Section>
          <Section title="Linked records"><Facts items={[['Complaint', link('Open complaint', '/complaints', r.complaintId)], ['NCR', link('Open NCR', '/ncr', r.ncrId)], ['CAPA', link('Open CAPA', '/capa', r.capaId)], ['Warranty claim', link('Open claim', '/field/warranty', r.warrantyClaimId)]]} /></Section>
        </Stack>
      </Box>}
      {tab === 'files' && <AttachmentsAndHistory type="CustomerReturn" id={id!} canEdit={edit} />}
      <StepDialog open={dlg === 'receive'} title="Receive return" confirm="Receive" onClose={() => setDlg(null)}
        inputs={[{ name: 'receivedOn', label: 'Received on', type: 'date', initial: localDate() }, { name: 'quantity', label: 'Quantity received', initial: String(r.quantity) }, { name: 'condition', label: 'Condition on arrival' }]}
        onConfirm={async (v) => { await post(`/api/returns/${id}/receive`, { receivedOn: v.receivedOn, quantity: Number(v.quantity), condition: v.condition || null }); refresh(); }} />
      <StepDialog open={dlg === 'inspect'} title="Record inspection" confirm="Save inspection" onClose={() => setDlg(null)}
        inputs={[{ name: 'inspectionNotes', label: 'Inspection notes', type: 'multiline' }, { name: 'failureConfirmed', label: 'Failure confirmed', type: 'checkbox' }, { name: 'rootCause', label: 'Root cause', type: 'multiline' }, { name: 'correctiveAction', label: 'Corrective action', type: 'multiline' }]}
        onConfirm={async (v) => { await post(`/api/returns/${id}/inspect`, { inspectionNotes: v.inspectionNotes, failureConfirmed: v.failureConfirmed === 'true', rootCause: v.rootCause || null, correctiveAction: v.correctiveAction || null }); refresh(); }} />
      <StepDialog open={dlg === 'disposition'} title="Disposition" confirm="Save disposition" onClose={() => setDlg(null)} intro="A confirmed failure needs an NCR first."
        inputs={[{ name: 'disposition', label: 'Disposition', type: 'select', initial: '1', options: returnDisposition.map((d, i) => [i, d] as [number, string]).slice(1) }, { name: 'notes', label: 'Notes', type: 'multiline' }]}
        onConfirm={async (v) => { await post(`/api/returns/${id}/disposition`, { disposition: Number(v.disposition), notes: v.notes || null }); refresh(); }} />
    </ModuleWorkspace>
  );
}

// ---------------------------------------------------------------- Warranty
export function WarrantyList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const names = useNames();
  const q = useQuery({ queryKey: ['claims'], queryFn: () => get<Claim[]>('/api/warranty') });
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Warranty' }]} title="Warranty claims"
      actions={<Stack direction="row" gap={1}><Button component={RouterLink} to="/field/warranty/metrics">Metrics</Button>{can('returns.edit') && <Button variant="contained" component={RouterLink} to="/field/warranty/new">New claim</Button>}</Stack>}>
      <DataTable data={q.data ?? []} onRowClick={(r) => nav(`/field/warranty/${r.id}`)} empty="No warranty claims."
        columns={[{ header: 'Claim', accessorKey: 'number', cell: mnum }, { header: 'Customer', accessorFn: (r) => names.customer(r.customerId) }, { header: 'Failure mode', accessorKey: 'failureMode' }, { header: 'Claimed', accessorKey: 'claimedOn', cell: (x) => fmtDate(x.getValue()) },
          { header: 'Cost', accessorKey: 'totalCost', cell: (x) => money(x.getValue()) }, { header: 'Status', accessorFn: (r) => claimStatus[r.status], cell: (x) => <StatusChip status={x.getValue()} /> }]} />
    </ModuleWorkspace>
  );
}

export function WarrantyNew() {
  const nav = useNavigate();
  const names = useNames();
  const returnId = new URLSearchParams(location.search).get('returnId') ?? '';
  const ret = useQuery({ queryKey: ['return', returnId], queryFn: () => get<Ret>(`/api/returns/${returnId}`), enabled: !!returnId });
  const [v, setV] = useState<Record<string, string>>({ customerId: '', serialNumber: '', lotNumber: '', quantity: '1', claimedOn: localDate(), failedOn: '', failureMode: '', componentPartId: '', supplierId: '', laborCost: '0', partsCost: '0', freightCost: '0', otherCost: '0', siteId: '' });
  const [error, setError] = useState<string | null>(null);
  const r = ret.data;
  const val = (k: string) => v[k] || (r && k === 'customerId' ? r.customerId : r && k === 'serialNumber' ? r.serialNumber ?? '' : r && k === 'lotNumber' ? r.lotNumber ?? '' : r && k === 'siteId' ? r.siteId : r && k === 'failureMode' ? r.rootCause ?? '' : '') || '';
  const set = (k: string) => (e: { target: { value: string } }) => setV({ ...v, [k]: e.target.value });
  const fld = (k: string, label: string, type = 'text') => <TextField label={label} type={type} value={val(k)} onChange={set(k)} slotProps={type === 'date' ? { inputLabel: { shrink: true } } : undefined} />;
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Warranty', to: '/field/warranty' }, { label: 'New' }]} title={r ? `Warranty claim for ${r.number}` : 'New warranty claim'}>
      <Paper sx={{ p: 3, maxWidth: 860, display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}>
        {error && <Alert severity="error" sx={{ gridColumn: '1 / -1' }}>{error}</Alert>}
        <TextField select label="Customer" value={val('customerId')} onChange={set('customerId')}>{names.customers.map((c) => <MenuItem key={c.id} value={c.id}>{lookupLabel(c)}</MenuItem>)}</TextField>
        {fld('failureMode', 'Failure mode')}{fld('serialNumber', 'Serial')}{fld('lotNumber', 'Lot')}{fld('quantity', 'Quantity', 'number')}{fld('claimedOn', 'Claimed on', 'date')}{fld('failedOn', 'Failed on', 'date')}
        <TextField select label="Failed component" value={val('componentPartId')} onChange={set('componentPartId')}><MenuItem value="">—</MenuItem>{names.parts.map((p) => <MenuItem key={p.id} value={p.id}>{lookupLabel(p)}</MenuItem>)}</TextField>
        <TextField select label="Responsible supplier" value={val('supplierId')} onChange={set('supplierId')}><MenuItem value="">—</MenuItem>{names.suppliers.map((s) => <MenuItem key={s.id} value={s.id}>{lookupLabel(s)}</MenuItem>)}</TextField>
        {fld('laborCost', 'Labor cost', 'number')}{fld('partsCost', 'Parts cost', 'number')}{fld('freightCost', 'Freight cost', 'number')}{fld('otherCost', 'Other cost', 'number')}
        <SiteSelect value={val('siteId')} onChange={(s) => setV({ ...v, siteId: s })} />
        <Stack direction="row" gap={1} sx={{ gridColumn: '1 / -1' }}>
          <Button variant="contained" onClick={async () => {
            setError(null);
            const nz = (k: string) => (val(k) === '' ? null : val(k));
            try {
              const x = await post<{ id: string }>('/api/warranty', { customerId: nz('customerId'), productId: null, partId: r?.partId ?? null, serialNumber: nz('serialNumber'), lotNumber: nz('lotNumber'), quantity: Number(val('quantity')), claimedOn: val('claimedOn'), failedOn: nz('failedOn'),
                failureMode: val('failureMode'), componentPartId: nz('componentPartId'), supplierId: nz('supplierId'), laborCost: Number(val('laborCost')), partsCost: Number(val('partsCost')), freightCost: Number(val('freightCost')), otherCost: Number(val('otherCost')),
                returnId: returnId || null, complaintId: null, siteId: val('siteId'), departmentId: null });
              nav(`/field/warranty/${x.id}`);
            } catch (e) { setError((e as Error).message); }
          }}>Submit claim</Button>
          <Button onClick={() => nav('/field/warranty')}>Cancel</Button>
        </Stack>
      </Paper>
    </ModuleWorkspace>
  );
}

export function WarrantyDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const { can, me } = useAuth();
  const names = useNames();
  const [tab, setTab] = useState('details');
  const [dlg, setDlg] = useState(false);
  const q = useQuery({ queryKey: ['claim', id], queryFn: () => get<Claim>(`/api/warranty/${id}`) });
  const w = q.data;
  if (!w) return <Loading to="/field/warranty" label="Warranty" error={q.error} />;
  const canDecide = can('returns.approve') && w.status <= 1 && w.createdById !== me?.id;
  const link = (label: string, url: string, idv?: string) => (idv ? <RouterLink to={`${url}/${idv}`}>{label}</RouterLink> : '—');
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Warranty', to: '/field/warranty' }, { label: w.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{w.number}</span><StatusChip status={claimStatus[w.status]} /></Stack>}
      tabs={[{ label: 'Details', value: 'details' }, { label: 'Files & history', value: 'files' }]} tab={tab} onTab={setTab}
      actions={canDecide ? <Button variant="contained" onClick={() => setDlg(true)}>Decide claim</Button> : undefined}>
      {tab === 'details' && <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '300px 1fr' }, gap: 2 }}>
        <Facts items={[['Customer', names.customer(w.customerId)], ['Serial', w.serialNumber ?? '—'], ['Lot', w.lotNumber ?? '—'], ['Quantity', String(w.quantity)], ['Claimed', fmtDate(w.claimedOn)], ['Failed', fmtDate(w.failedOn)], ['Supplier', names.supplier(w.supplierId)]]} />
        <Stack gap={2}>
          <Section title="Cost">
            <Table size="small"><TableBody>{[['Labor', w.laborCost], ['Parts', w.partsCost], ['Freight', w.freightCost], ['Other', w.otherCost]].map(([l, c]) => <TableRow key={l as string}><TableCell>{l}</TableCell><TableCell sx={{ fontFamily: mono }} align="right">{money(c as number)}</TableCell></TableRow>)}
              <TableRow><TableCell sx={{ fontWeight: 600 }}>Total</TableCell><TableCell sx={{ fontFamily: mono, fontWeight: 600 }} align="right">{money(w.totalCost)}</TableCell></TableRow>
              <TableRow><TableCell>Supplier recovery</TableCell><TableCell sx={{ fontFamily: mono }} align="right">{money(w.supplierRecovery)}</TableCell></TableRow></TableBody></Table>
          </Section>
          <Section title="Failure and decision"><Prose label="Failure mode" text={w.failureMode} /><Prose label="Decision" text={w.decision} /></Section>
          <Section title="Linked records"><Facts items={[['Return', link('Open return', '/field/returns', w.returnId)], ['Complaint', link('Open complaint', '/complaints', w.complaintId)], ['NCR', link('Open NCR', '/ncr', w.ncrId)], ['CAPA', link('Open CAPA', '/capa', w.capaId)]]} /></Section>
        </Stack>
      </Box>}
      {tab === 'files' && <AttachmentsAndHistory type="WarrantyClaim" id={id!} canEdit={can('returns.edit')} />}
      <StepDialog open={dlg} title="Decide warranty claim" confirm="Record decision" onClose={() => setDlg(false)} intro="You cannot decide a claim you entered."
        inputs={[{ name: 'approve', label: 'Approve', type: 'checkbox', initial: 'true' }, { name: 'decision', label: 'Decision notes', type: 'multiline' }, { name: 'recovery', label: 'Supplier recovery ($)' }]}
        onConfirm={async (v) => { await post(`/api/warranty/${id}/decision`, { approve: v.approve === 'true', decision: v.decision, supplierRecovery: v.recovery ? Number(v.recovery) : null }); qc.invalidateQueries({ queryKey: ['claim', id] }); }} />
    </ModuleWorkspace>
  );
}

interface Metrics { claimQuantity: number; shippedQuantity: number; ppm: number; totalCost: number; supplierRecovery: number; claims: number; trend: { period: string; claims: number; quantity: number; cost: number; shipped: number; ppm: number }[]; byProduct: G[]; byComponent: G[]; bySupplier: G[] }
interface G { id?: string; label: string; claims: number; quantity: number; cost: number }

export function WarrantyMetrics() {
  const [from, setFrom] = useState(localDate(-365));
  const [to, setTo] = useState(localDate());
  const q = useQuery({ queryKey: ['wmetrics', from, to], queryFn: () => get<Metrics>(`/api/warranty/metrics?from=${from}&to=${to}`) });
  const d = q.data;
  const grp = (title: string, rows: G[]) => <Section title={title}><Table size="small"><TableBody>{rows.map((r) => <TableRow key={r.label}><TableCell>{r.label}</TableCell><TableCell sx={{ fontFamily: mono }}>{r.claims}</TableCell><TableCell sx={{ fontFamily: mono }} align="right">{money(r.cost)}</TableCell></TableRow>)}
    {rows.length === 0 && <TableRow><TableCell><Typography color="text.secondary">No claims.</Typography></TableCell></TableRow>}</TableBody></Table></Section>;
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Warranty', to: '/field/warranty' }, { label: 'Metrics' }]} title="Warranty metrics"
      actions={<Stack direction="row" gap={1}><TextField size="small" type="date" label="From" value={from} onChange={(e) => setFrom(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} /><TextField size="small" type="date" label="To" value={to} onChange={(e) => setTo(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} /></Stack>}>
      {q.error && <Alert severity="error">{(q.error as Error).message}</Alert>}
      {d && <>
        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: 1.5, mb: 2 }}>
          {[['Warranty PPM', d.ppm.toFixed(0), `${d.claimQuantity} claimed / ${d.shippedQuantity} shipped`], ['Claims', String(d.claims), ''], ['Cost', money(d.totalCost), ''], ['Supplier recovery', money(d.supplierRecovery), d.totalCost ? `${((d.supplierRecovery / d.totalCost) * 100).toFixed(0)}% of cost` : '']].map(([l, val, sub]) =>
            <Paper key={l} sx={{ p: 1.75, borderTop: 3, borderTopColor: 'secondary.main' }}><Typography variant="caption" color="text.secondary">{l}</Typography><Typography sx={{ fontFamily: mono, fontSize: 26 }}>{val}</Typography><Typography variant="caption" color="text.secondary">{sub}</Typography></Paper>)}
        </Box>
        <Section title="Trend">
          <Box sx={{ height: 260 }}><ResponsiveContainer><ComposedChart data={d.trend}><CartesianGrid stroke="#E2DED5" /><XAxis dataKey="period" /><YAxis yAxisId="l" /><YAxis yAxisId="r" orientation="right" /><Tooltip />
            <Bar yAxisId="l" dataKey="claims" fill="#B4632A" name="Claims" /><Line yAxisId="r" dataKey="ppm" stroke="#1F4E4A" name="PPM" /></ComposedChart></ResponsiveContainer></Box>
        </Section>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr 1fr' }, gap: 2, mt: 2 }}>{grp('By product', d.byProduct)}{grp('By component', d.byComponent)}{grp('By supplier', d.bySupplier)}</Box>
      </>}
    </ModuleWorkspace>
  );
}
