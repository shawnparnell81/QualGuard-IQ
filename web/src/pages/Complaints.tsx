import { useState } from 'react';
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Link, Stack, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { z } from 'zod';
import { get, post } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, capaStatus, fmtDate, fmtDateTime, localDate, ncrStatus, severity } from '../components/Status';
import { ActionsPanel, AttachmentsAndHistory } from '../components/RecordTabs';
import { Facts, Prose, SchemaForm, Section, StepDialog, useRunner } from '../components/Kit';
import { mono } from '../theme';

export const complaintStatus = ['Open', 'Investigation', 'AwaitingCustomer', 'Verification', 'Closed', 'Cancelled'];
const warranty = ['Unknown', 'In warranty', 'Out of warranty', 'Not applicable'];
const mod = () => modules.find((m) => m.key === 'complaints')!;
const num = (x: { getValue: () => unknown }) => <span style={{ fontFamily: mono }}>{String(x.getValue())}</span>;
const sevOpts = severity.map((s, i) => [i, s] as [number, string]);

interface Row { id: string; number: string; title: string; status: number; severity: number; complaintDate: string; responseDueOn: string; partNumber: string | null; failureMode: string | null; returnAuthorization: string | null; customer: string; ownerName: string }

export function ComplaintList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const view = p.get('view') ?? 'open';
  const [q, setQ] = useState('');
  const [applied, setApplied] = useState('');
  const list = useQuery({ queryKey: ['complaints', view, applied], queryFn: () => get<{ items: Row[] }>(`/api/complaints?pageSize=200${view === 'open' ? '&openOnly=true' : view === 'all' ? '' : `&status=${view}`}${applied ? `&q=${encodeURIComponent(applied)}` : ''}`) });
  const today = localDate();
  const cols: ColumnDef<Row, any>[] = [
    { header: 'Number', accessorKey: 'number', cell: num }, { header: 'Customer', accessorKey: 'customer' }, { header: 'Title', accessorKey: 'title' },
    { header: 'Part', accessorKey: 'partNumber' }, { header: 'Failure mode', accessorKey: 'failureMode' }, { header: 'Severity', accessorKey: 'severity', cell: (x) => severity[x.getValue()] },
    { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={complaintStatus[x.getValue()]} /> },
    { header: 'Response due', accessorKey: 'responseDueOn', cell: (x) => <Box component="span" sx={{ color: x.getValue() < today && x.row.original.status < 2 ? 'error.main' : undefined }}>{fmtDate(x.getValue())}</Box> },
    { header: 'RMA', accessorKey: 'returnAuthorization' }, { header: 'Owner', accessorKey: 'ownerName' },
  ];
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Complaints' }]} title="Customer complaints"
      tabs={[{ label: 'Open', value: 'open' }, { label: 'Awaiting customer', value: '2' }, { label: 'Verification', value: '3' }, { label: 'Closed', value: '4' }, { label: 'All', value: 'all' }]}
      tab={view} onTab={(v) => setP(v === 'open' ? {} : { view: v })}
      actions={can('complaints.edit') ? <Button variant="contained" component={RouterLink} to="/complaints/new">Record complaint</Button> : undefined}>
      <Stack direction="row" gap={1} component="form" sx={{ mb: 2 }} onSubmit={(e) => { e.preventDefault(); setApplied(q); }}>
        <TextField size="small" placeholder="Number, part, lot, serial, RMA, customer reference" value={q} onChange={(e) => setQ(e.target.value)} sx={{ width: 400 }} />
        <Button type="submit" variant="outlined">Filter</Button>
      </Stack>
      <DataTable data={list.data?.items ?? []} columns={cols} onRowClick={(r) => nav(`/complaints/${r.id}`)} empty="No complaints in this view." />
    </ModuleWorkspace>
  );
}

const schema = z.object({
  title: z.string().trim().min(1, 'Required').max(300), description: z.string().trim().min(1, 'Required').max(10000), customerId: z.string().uuid('Choose a customer'),
  customerContact: z.string().max(200).optional(), customerReference: z.string().max(200).optional(), complaintDate: z.string().min(10, 'Required'), reportedOn: z.string().min(10, 'Required'),
  responseDueOn: z.string().min(10, 'Required'), productId: z.string().optional(), partId: z.string().optional(), partRevision: z.string().max(10).optional(), partNumber: z.string().max(200).optional(),
  lotNumber: z.string().max(200).optional(), batchNumber: z.string().max(200).optional(), serialNumber: z.string().max(200).optional(),
  quantity: z.union([z.literal(''), z.coerce.number().min(0)]).optional(), failureMode: z.string().max(200).optional(), severity: z.coerce.number().int(), risk: z.coerce.number().int(),
  warranty: z.coerce.number().int(), supplierId: z.string().optional(), returnAuthorization: z.string().max(200).optional(),
  siteId: z.string().uuid('Choose a site'), departmentId: z.string().optional(), ownerId: z.string().optional(),
});
type Form = z.infer<typeof schema>;
const nul = (v?: string) => (v ? v : null);
const toBody = (v: Form) => ({ ...v, productId: nul(v.productId), partId: nul(v.partId), supplierId: nul(v.supplierId), departmentId: nul(v.departmentId), ownerId: nul(v.ownerId),
  partRevision: nul(v.partRevision), partNumber: nul(v.partNumber), quantity: v.quantity === '' || v.quantity === undefined ? null : v.quantity, customerContact: nul(v.customerContact), customerReference: nul(v.customerReference),
  lotNumber: nul(v.lotNumber), batchNumber: nul(v.batchNumber), serialNumber: nul(v.serialNumber), failureMode: nul(v.failureMode), returnAuthorization: nul(v.returnAuthorization), returnExpected: !!v.returnAuthorization });

export function ComplaintNew() {
  const nav = useNavigate();
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Complaints', to: '/complaints' }, { label: 'New' }]} title="Record customer complaint">
      <SchemaForm schema={schema} submitLabel="Record complaint" onCancel={() => nav('/complaints')}
        defaults={{ title: '', description: '', customerId: '', customerContact: '', customerReference: '', complaintDate: localDate(), reportedOn: localDate(), responseDueOn: localDate(14), productId: '', partId: '', partRevision: '', partNumber: '',
          lotNumber: '', batchNumber: '', serialNumber: '', quantity: '', failureMode: '', severity: 1, risk: 1, warranty: 0, supplierId: '', returnAuthorization: '', siteId: '', departmentId: '', ownerId: '' }}
        fields={[{ name: 'title', label: 'Title', full: true }, { name: 'customerId', label: 'Customer', type: 'lookup', lookup: 'customer' }, { name: 'customerContact', label: 'Customer contact' },
          { name: 'customerReference', label: 'Customer reference' }, { name: 'complaintDate', label: 'Complaint date', type: 'date' }, { name: 'reportedOn', label: 'Reported to us on', type: 'date' },
          { name: 'responseDueOn', label: 'Response deadline', type: 'date' }, { name: 'description', label: 'Complaint description', type: 'multiline', full: true },
          { name: 'traceability' as never, label: 'Product traceability', type: 'heading' }, { name: 'partId', label: 'Catalog part (sets revision)', type: 'lookup', lookup: 'part' }, { name: 'partRevision', label: 'Revision' },
          { name: 'productId', label: 'Product', type: 'lookup', lookup: 'product' }, { name: 'partNumber', label: 'Part number (if not in catalog)' }, { name: 'lotNumber', label: 'Lot' }, { name: 'batchNumber', label: 'Batch' },
          { name: 'serialNumber', label: 'Serial number' }, { name: 'quantity', label: 'Quantity affected', type: 'number' }, { name: 'supplierId', label: 'Supplier (if supplied material)', type: 'lookup', lookup: 'supplier' },
          { name: 'classification' as never, label: 'Classification', type: 'heading' }, { name: 'failureMode', label: 'Failure mode' }, { name: 'severity', label: 'Severity', type: 'select', options: sevOpts },
          { name: 'risk', label: 'Risk', type: 'select', options: sevOpts }, { name: 'warranty', label: 'Warranty status', type: 'select', options: warranty.map((w, i) => [i, w] as [number, string]) },
          { name: 'returnAuthorization', label: 'Return authorization (RMA), if material is coming back' },
          { name: 'siteId', label: 'Site', type: 'site' }, { name: 'departmentId', label: 'Department (optional)', type: 'department' }, { name: 'ownerId', label: 'Owner (defaults to you)', type: 'user' }]}
        onSubmit={async (v) => { const r = await post<{ id: string }>('/api/complaints', toBody(v)); nav(`/complaints/${r.id}`); }} />
    </ModuleWorkspace>
  );
}

interface Dto { id: string; number: string; title: string; description: string; customerContact: string | null; customerReference: string | null; complaintDate: string; reportedOn: string; partNumber: string | null; partRevision: string | null;
  lotNumber: string | null; batchNumber: string | null; serialNumber: string | null; quantity: number | null; failureMode: string | null; severity: number; risk: number; warranty: number; responseDueOn: string; status: number;
  containment: string | null; containedAt: string | null; returnAuthorization: string | null; returnExpected: boolean; returnReceivedOn: string | null; returnQuantity: number | null; returnCondition: string | null; returnEvaluation: string | null;
  testResults: string | null; failureConfirmed: boolean | null; investigation: string | null; rootCause: string | null; correctiveAction: string | null; customerResponse: string | null; responseSentOn: string | null;
  customerAccepted: boolean | null; customerFeedback: string | null; acceptedOn: string | null; verificationNotes: string | null; verifiedAt: string | null; closureNotes: string | null; closedAt: string | null; reopenCount: number; reopenReason: string | null;
  customerId: string; customerLabel: string; productLabel: string | null; partId: string | null; partLabel: string | null; supplierId: string | null; supplierLabel: string | null; ownerName: string; verifiedByName: string | null; siteCode: string;
  ncrs: { id: string; number: string; title: string; status: number }[] | null; capas: { id: string; number: string; title: string; status: number }[] | null; canEdit: boolean; allowed: string[] }

export function ComplaintDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const nav = useNavigate();
  const [tab, setTab] = useState('case');
  const [dlg, setDlg] = useState<string | null>(null);
  const q = useQuery({ queryKey: ['complaint', id], queryFn: () => get<Dto>(`/api/complaints/${id}`) });
  const refresh = () => qc.invalidateQueries({ queryKey: ['complaint', id] });
  const { error, run } = useRunner(refresh);
  const d = q.data;
  if (!d) return <ModuleWorkspace module={mod()} crumbs={[{ label: 'Complaints', to: '/complaints' }]} title="Loading">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const a = (k: string) => d.allowed.includes(k);
  const today = localDate();
  const steps: [string, boolean][] = [['Containment', !!d.containment], ['Return evaluated', !d.returnExpected || !!d.returnEvaluation], ['Investigation', !!d.rootCause], ['Response sent', !!d.responseSentOn], ['Customer accepted', d.customerAccepted === true], ['Verified', !!d.verifiedAt], ['Closed', d.status === 4]];
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Complaints', to: '/complaints' }, { label: d.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono, whiteSpace: 'nowrap' }}>{d.number}</span><span>{d.title}</span><StatusChip status={complaintStatus[d.status]} /></Stack>}
      tabs={[{ label: 'Case', value: 'case' }, { label: 'Return', value: 'return' }, { label: 'Linked records', value: 'links' }, { label: 'Actions', value: 'actions' }, { label: 'Files & history', value: 'more' }]} tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1} flexWrap="wrap">
        {a('contain') && <Button variant={d.containment ? 'outlined' : 'contained'} onClick={() => setDlg('contain')}>{d.containment ? 'Update containment' : 'Record containment'}</Button>}
        {a('investigate') && <Button variant="outlined" onClick={() => setDlg('investigate')}>Investigation</Button>}
        {a('respond') && <Button variant="contained" onClick={() => setDlg('respond')}>Send customer response</Button>}
        {a('acceptance') && <Button variant="contained" onClick={() => setDlg('acceptance')}>Record customer decision</Button>}
        {a('verify') && <Button variant="contained" color="secondary" onClick={() => setDlg('verify')}>Verify corrective action</Button>}
        {a('close') && <Button variant="contained" onClick={() => setDlg('close')}>Close</Button>}
        {a('reopen') && <Button variant="outlined" color="warning" onClick={() => setDlg('reopen')}>Reopen</Button>}
        {a('cancel') && <Button color="error" onClick={() => setDlg('cancel')}>Cancel</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {d.status < 2 && d.responseDueOn < today && <Alert severity="error" sx={{ mb: 2 }}>The customer response was due {fmtDate(d.responseDueOn)}.</Alert>}
      <Stack direction="row" gap={0.75} sx={{ mb: 2, flexWrap: 'wrap' }}>
        {steps.map(([l, ok]) => <Box key={l} sx={{ px: 1.25, py: 0.4, fontSize: 12.5, borderRadius: 1, border: 1, borderColor: ok ? 'success.main' : 'divider', color: ok ? 'success.dark' : 'text.secondary', bgcolor: ok ? 'rgba(46,125,50,0.06)' : undefined }}>{ok ? '✓ ' : ''}{l}</Box>)}
      </Stack>
      <Box sx={{ display: 'grid', gridTemplateColumns: '290px 1fr', gap: 3 }}>
        <Facts items={[['Customer', d.customerLabel], ['Contact', d.customerContact ?? '—'], ['Customer ref.', d.customerReference ?? '—'], ['Complaint date', fmtDate(d.complaintDate)], ['Reported', fmtDate(d.reportedOn)],
          ['Response due', fmtDate(d.responseDueOn)], ['Part', d.partId ? <Link component={RouterLink} to={`/products/parts/${d.partId}`}>{d.partLabel}</Link> : d.partNumber ?? '—'], ['Revision', d.partRevision ?? '—'], ['Product', d.productLabel ?? '—'],
          ['Lot / batch', [d.lotNumber, d.batchNumber].filter(Boolean).join(' / ') || '—'], ['Serial', d.serialNumber ?? '—'], ['Quantity', d.quantity ?? '—'], ['Failure mode', d.failureMode ?? '—'],
          ['Severity / risk', `${severity[d.severity]} / ${severity[d.risk]}`], ['Warranty', warranty[d.warranty]], ['Supplier', d.supplierId ? <Link component={RouterLink} to={`/suppliers/${d.supplierId}`}>{d.supplierLabel}</Link> : '—'],
          ['Owner', d.ownerName], ['Site', d.siteCode], ['Reopened', d.reopenCount ? `${d.reopenCount}×` : '—']]} />
        <Box sx={{ minWidth: 0 }}>
          {tab === 'case' && <Stack gap={2}>
            <Section title="Complaint"><Prose label="Description" text={d.description} />{d.reopenReason && <Prose label="Reopen reason" text={d.reopenReason} />}</Section>
            <Section title="Containment">{d.containment ? <><Prose label={`Recorded ${fmtDateTime(d.containedAt)}`} text={d.containment} /></> : <Alert severity="warning">No containment recorded.</Alert>}</Section>
            <Section title="Investigation">{d.rootCause ? <><Prose label="Investigation" text={d.investigation} /><Prose label="Root cause" text={d.rootCause} /><Prose label="Corrective action" text={d.correctiveAction} /></> : <Alert severity="info">Not started.</Alert>}</Section>
            <Section title="Customer response">{d.customerResponse ? <><Prose label={`Sent ${fmtDate(d.responseSentOn)}`} text={d.customerResponse} />
              {d.customerAccepted !== null && <Alert severity={d.customerAccepted ? 'success' : 'warning'} sx={{ my: 1 }}>{d.customerAccepted ? `Accepted ${fmtDate(d.acceptedOn)}` : 'Rejected by customer'}</Alert>}
              <Prose label="Customer feedback" text={d.customerFeedback} /></> : <Alert severity="info">No response sent yet.</Alert>}</Section>
            {(d.verifiedAt || d.closedAt) && <Section title="Verification and closure"><Prose label={`Verified by ${d.verifiedByName ?? '—'} ${fmtDateTime(d.verifiedAt)}`} text={d.verificationNotes} /><Prose label={`Closure ${fmtDateTime(d.closedAt)}`} text={d.closureNotes} /></Section>}
          </Stack>}
          {tab === 'return' && <Section title="Returned material" action={<Stack direction="row" gap={1}>{a('receiveReturn') && <Button size="small" onClick={() => setDlg('return')}>Record receipt</Button>}{a('evaluateReturn') && <Button size="small" onClick={() => setDlg('evaluate')}>Evaluate</Button>}</Stack>}>
            <Facts items={[['RMA', d.returnAuthorization ?? '—'], ['Return expected', d.returnExpected ? 'Yes' : 'No'], ['Received', fmtDate(d.returnReceivedOn)], ['Quantity returned', d.returnQuantity ?? '—'],
              ['Failure confirmed', d.failureConfirmed === null ? '—' : d.failureConfirmed ? 'Yes' : 'No']]} />
            <Prose label="Condition on receipt" text={d.returnCondition} /><Prose label="Evaluation" text={d.returnEvaluation} /><Prose label="Test results" text={d.testResults} />
            {d.returnExpected && !d.returnEvaluation && <Alert severity="info" sx={{ mt: 1 }}>The returned material must be evaluated before the customer response.</Alert>}
          </Section>}
          {tab === 'links' && <Stack gap={2}>
            <Section title="Nonconformances" action={a('ncr') ? <Button size="small" onClick={() => setDlg('ncr')}>Create NCR from complaint</Button> : undefined}>
              {d.ncrs === null ? <Alert severity="info">You do not have access to NCRs.</Alert> : d.ncrs.length === 0 ? <Typography color="text.secondary" fontSize={14}>None.</Typography> :
                d.ncrs.map((n) => <Box key={n.id} sx={{ py: 0.5 }}><Link component={RouterLink} to={`/ncr/${n.id}`} sx={{ fontFamily: mono }}>{n.number}</Link> {n.title} <StatusChip status={ncrStatus[n.status]} /></Box>)}
            </Section>
            <Section title="CAPAs" action={a('capa') ? <Button size="small" onClick={() => setDlg('capa')}>Create CAPA</Button> : undefined}>
              {d.capas === null ? <Alert severity="info">You do not have access to CAPAs.</Alert> : d.capas.length === 0 ? <Typography color="text.secondary" fontSize={14}>None.</Typography> :
                d.capas.map((n) => <Box key={n.id} sx={{ py: 0.5 }}><Link component={RouterLink} to={`/capa/${n.id}`} sx={{ fontFamily: mono }}>{n.number}</Link> {n.title} <StatusChip status={capaStatus[n.status]} /></Box>)}
            </Section>
          </Stack>}
          {tab === 'actions' && <ActionsPanel type="Complaint" id={d.id} canEdit={a('actions')} canVerifyAs={d.canEdit} onChanged={refresh} />}
          {tab === 'more' && <AttachmentsAndHistory type="Complaint" id={d.id} canEdit={d.canEdit} />}
        </Box>
      </Box>
      <StepDialog open={dlg === 'contain'} title="Containment" confirm="Save" onClose={() => setDlg(null)} inputs={[{ name: 'text', label: 'Containment (stock, in-transit, at customer)', type: 'multiline', initial: d.containment ?? '' }]}
        onConfirm={(v) => run(() => post(`/api/complaints/${d.id}/containment`, v))} />
      <StepDialog open={dlg === 'return'} title="Returned material received" confirm="Record" onClose={() => setDlg(null)}
        inputs={[{ name: 'returnAuthorization', label: 'Return authorization (RMA)', initial: d.returnAuthorization ?? '' }, { name: 'receivedOn', label: 'Received on', type: 'date', initial: localDate() },
          { name: 'quantity', label: 'Quantity received' }, { name: 'condition', label: 'Condition on receipt', type: 'multiline' }]}
        onConfirm={(v) => run(() => post(`/api/complaints/${d.id}/return`, { ...v, quantity: Number(v.quantity), condition: v.condition || null }))} />
      <StepDialog open={dlg === 'evaluate'} title="Returned-material evaluation" intro="Attach photographs and test reports on the Files tab." confirm="Save" onClose={() => setDlg(null)}
        inputs={[{ name: 'evaluation', label: 'Evaluation', type: 'multiline', initial: d.returnEvaluation ?? '' }, { name: 'testResults', label: 'Test results', type: 'multiline', initial: d.testResults ?? '' },
          { name: 'failureConfirmed', label: 'Failure confirmed?', type: 'select', initial: d.failureConfirmed === false ? 'false' : 'true', options: [['true', 'Yes — failure reproduced'], ['false', 'No — no fault found']] }]}
        onConfirm={(v) => run(() => post(`/api/complaints/${d.id}/return-evaluation`, { evaluation: v.evaluation, testResults: v.testResults || null, failureConfirmed: v.failureConfirmed === 'true' }))} />
      <StepDialog open={dlg === 'investigate'} title="Investigation" confirm="Save" onClose={() => setDlg(null)}
        inputs={[{ name: 'investigation', label: 'Investigation', type: 'multiline', initial: d.investigation ?? '' }, { name: 'rootCause', label: 'Root cause', type: 'multiline', initial: d.rootCause ?? '' },
          { name: 'correctiveAction', label: 'Corrective action', type: 'multiline', initial: d.correctiveAction ?? '' }]}
        onConfirm={(v) => run(() => post(`/api/complaints/${d.id}/investigation`, v))} />
      <StepDialog open={dlg === 'respond'} title="Customer response" confirm="Record as sent" onClose={() => setDlg(null)}
        inputs={[{ name: 'response', label: 'Response sent to the customer', type: 'multiline' }, { name: 'sentOn', label: 'Sent on', type: 'date', initial: localDate() }]}
        onConfirm={(v) => run(() => post(`/api/complaints/${d.id}/response`, v))} />
      <StepDialog open={dlg === 'acceptance'} title="Customer decision" intro="A rejection returns the complaint to investigation." confirm="Save" onClose={() => setDlg(null)}
        inputs={[{ name: 'accepted', label: 'Decision', type: 'select', initial: 'true', options: [['true', 'Customer accepted'], ['false', 'Customer rejected']] }, { name: 'feedback', label: 'Customer feedback', type: 'multiline' }, { name: 'on', label: 'Date', type: 'date', initial: localDate() }]}
        onConfirm={(v) => run(() => post(`/api/complaints/${d.id}/acceptance`, { accepted: v.accepted === 'true', feedback: v.feedback, on: v.on }))} />
      <StepDialog open={dlg === 'verify'} title="Verify corrective action" intro="Verification must be done by someone other than the complaint owner." confirm="Verify" onClose={() => setDlg(null)}
        inputs={[{ name: 'text', label: 'Objective evidence', type: 'multiline' }]} onConfirm={(v) => run(() => post(`/api/complaints/${d.id}/verify`, v))} />
      <StepDialog open={dlg === 'close'} title={`Close ${d.number}`} confirm="Close" onClose={() => setDlg(null)} inputs={[{ name: 'text', label: 'Closure notes', type: 'multiline' }]} onConfirm={(v) => run(() => post(`/api/complaints/${d.id}/close`, v))} />
      <StepDialog open={dlg === 'reopen'} title={`Reopen ${d.number}`} confirm="Reopen" onClose={() => setDlg(null)} inputs={[{ name: 'text', label: 'Reason', type: 'multiline' }]} onConfirm={(v) => run(() => post(`/api/complaints/${d.id}/reopen`, v))} />
      <StepDialog open={dlg === 'cancel'} title={`Cancel ${d.number}`} confirm="Cancel complaint" danger onClose={() => setDlg(null)} inputs={[{ name: 'text', label: 'Reason', type: 'multiline' }]} onConfirm={(v) => run(() => post(`/api/complaints/${d.id}/cancel`, v))} />
      <StepDialog open={dlg === 'ncr'} title="Create NCR" intro="The NCR is created with this complaint's customer, part, revision, lot and serial number, and linked here." confirm="Create NCR" onClose={() => setDlg(null)}
        inputs={[{ name: 'title', label: 'NCR title', initial: d.title }]} onConfirm={(v) => run(async () => { const r = await post<{ id: string }>(`/api/complaints/${d.id}/ncr`, { title: v.title, description: null }); nav(`/ncr/${r.id}`); })} />
      <StepDialog open={dlg === 'capa'} title="Create CAPA" confirm="Create CAPA" onClose={() => setDlg(null)}
        inputs={[{ name: 'title', label: 'CAPA title', initial: d.title }, { name: 'description', label: 'Problem statement', type: 'multiline', initial: d.description }]}
        onConfirm={(v) => run(async () => { const r = await post<{ id: string }>(`/api/complaints/${d.id}/capa`, v); nav(`/capa/${r.id}`); })} />
    </ModuleWorkspace>
  );
}

interface Trends { total: number; open: number; byMonth: { month: string; count: number; critical: number }[]; byFailureMode: { label: string; count: number }[]; byCustomer: { label: string; count: number }[]; byProduct: { label: string; count: number }[] }

export function ComplaintTrends() {
  const [months, setMonths] = useState(12);
  const t = useQuery({ queryKey: ['complaint-trends', months], queryFn: () => get<Trends>(`/api/complaints/trends?months=${months}`) });
  const top = (title: string, rows?: { label: string; count: number }[]) => (
    <Section title={title}>{!rows?.length ? <Typography color="text.secondary" fontSize={14}>No data.</Typography> : rows.map((r) => (
      <Stack key={r.label} direction="row" alignItems="center" gap={1} sx={{ py: 0.4 }}>
        <Box sx={{ width: 200, fontSize: 13.5, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{r.label}</Box>
        <Box sx={{ height: 10, bgcolor: 'primary.main', borderRadius: 0.5, width: `${(r.count / Math.max(...rows.map((x) => x.count))) * 220}px` }} /><Box sx={{ fontSize: 13 }}>{r.count}</Box>
      </Stack>))}</Section>);
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Complaints', to: '/complaints' }, { label: 'Trends' }]} title="Complaint trends"
      tabs={[{ label: '6 months', value: '6' }, { label: '12 months', value: '12' }, { label: '24 months', value: '24' }]} tab={String(months)} onTab={(v) => setMonths(Number(v))}>
      <Stack direction="row" gap={4} sx={{ mb: 2 }}><Box><Typography variant="h4">{t.data?.total ?? '—'}</Typography><Typography color="text.secondary" fontSize={13}>complaints in period</Typography></Box>
        <Box><Typography variant="h4">{t.data?.open ?? '—'}</Typography><Typography color="text.secondary" fontSize={13}>still open</Typography></Box></Stack>
      <Section title="Complaints per month">
        <Box sx={{ height: 240 }}><ResponsiveContainer><BarChart data={t.data?.byMonth ?? []}><CartesianGrid strokeDasharray="3 3" vertical={false} /><XAxis dataKey="month" fontSize={12} /><YAxis allowDecimals={false} fontSize={12} /><Tooltip />
          <Bar dataKey="count" name="All" fill="#37474f" /><Bar dataKey="critical" name="Critical" fill="#c62828" /></BarChart></ResponsiveContainer></Box>
      </Section>
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 2, mt: 2 }}>{top('By failure mode', t.data?.byFailureMode)}{top('By customer', t.data?.byCustomer)}{top('By product', t.data?.byProduct)}</Box>
    </ModuleWorkspace>
  );
}
