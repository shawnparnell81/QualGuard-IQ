import { useState } from 'react';
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, IconButton, Link, Stack, TextField } from '@mui/material';
import DeleteOutline from '@mui/icons-material/DeleteOutline';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { z } from 'zod';
import { del, get, post, put } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, criticality, fmtDate, fmtDateTime, localDate, ncrStatus, reviewOutcome, scarStatus, supplierStatus } from '../components/Status';
import { ApprovalsPanel, AttachmentsAndHistory } from '../components/RecordTabs';
import { Facts, Prose, SchemaForm, Section, StepDialog, useRunner } from '../components/Kit';
import { SignDialog } from '../components/SignDialog';
import { useWork } from '../work';
import { mono } from '../theme';

const mod = () => modules.find((m) => m.key === 'suppliers')!;
const num = (x: { getValue: () => unknown }) => <span style={{ fontFamily: mono }}>{String(x.getValue())}</span>;
const critOpts = criticality.map((c, i) => [i, c] as [number, string]);

interface SupplierRow { id: string; code: string; legalName: string; operatingName: string | null; category: string; criticality: number; risk: number; status: number; nextReviewOn: string | null; nextCertExpiry: string | null; siteCode: string }

export function SupplierList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const status = p.get('status') ?? '';
  const [q, setQ] = useState('');
  const [applied, setApplied] = useState('');
  const list = useQuery({ queryKey: ['suppliers', status, applied], queryFn: () => get<{ items: SupplierRow[] }>(`/api/suppliers?pageSize=200${status ? `&status=${status}` : ''}${applied ? `&q=${encodeURIComponent(applied)}` : ''}`) });
  const today = localDate();
  const cols: ColumnDef<SupplierRow, any>[] = [
    { header: 'Code', accessorKey: 'code', cell: num }, { header: 'Supplier', accessorFn: (r) => r.operatingName ? `${r.legalName} (${r.operatingName})` : r.legalName }, { header: 'Category', accessorKey: 'category' },
    { header: 'Criticality', accessorKey: 'criticality', cell: (x) => criticality[x.getValue()] }, { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={supplierStatus[x.getValue()]} /> },
    { header: 'Next review', accessorKey: 'nextReviewOn', cell: (x) => fmtDate(x.getValue()) },
    { header: 'Earliest cert. expiry', accessorKey: 'nextCertExpiry', cell: (x) => x.getValue() ? <Box component="span" sx={{ color: x.getValue() < today ? 'error.main' : undefined }}>{fmtDate(x.getValue())}</Box> : '—' },
    { header: 'Site', accessorKey: 'siteCode' },
  ];
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Supplier register' }]} title="Supplier register"
      tabs={[{ label: 'All', value: '' }, { label: 'Approved', value: '2' }, { label: 'Conditional', value: '3' }, { label: 'Pending approval', value: '1' }, { label: 'Prospective', value: '0' }, { label: 'Suspended', value: '4' }, { label: 'Disqualified', value: '5' }]}
      tab={status} onTab={(v) => setP(v ? { status: v } : {})}
      actions={can('suppliers.edit') ? <Button variant="contained" component={RouterLink} to="/suppliers/new">Add supplier</Button> : undefined}>
      <Stack direction="row" gap={1} component="form" sx={{ mb: 2 }} onSubmit={(e) => { e.preventDefault(); setApplied(q); }}>
        <TextField size="small" placeholder="Filter by code, name or category" value={q} onChange={(e) => setQ(e.target.value)} sx={{ width: 360 }} />
        <Button type="submit" variant="outlined">Filter</Button>
      </Stack>
      <DataTable data={list.data?.items ?? []} columns={cols} onRowClick={(r) => nav(`/suppliers/${r.id}`)} empty="No suppliers in this view." />
    </ModuleWorkspace>
  );
}

const supplierSchema = z.object({
  code: z.string().trim().min(1, 'Required').max(40), legalName: z.string().trim().min(1, 'Required').max(200), operatingName: z.string().max(200).optional(),
  category: z.string().trim().min(1, 'Required').max(100), suppliedScope: z.string().max(2000).optional(), criticality: z.coerce.number().int(), risk: z.coerce.number().int(),
  address: z.string().max(500).optional(), website: z.string().max(300).optional(), siteId: z.string().uuid('Choose a site'), departmentId: z.string().optional(), ownerId: z.string().optional(),
});

export function SupplierNew() {
  const nav = useNavigate();
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Supplier register', to: '/suppliers' }, { label: 'New' }]} title="Add supplier">
      <SchemaForm schema={supplierSchema} submitLabel="Create supplier" onCancel={() => nav('/suppliers')}
        defaults={{ code: '', legalName: '', operatingName: '', category: '', suppliedScope: '', criticality: 1, risk: 1, address: '', website: '', siteId: '', departmentId: '', ownerId: '' }}
        fields={[{ name: 'code', label: 'Supplier code' }, { name: 'category', label: 'Category (castings, machining, distributor, service...)' },
          { name: 'legalName', label: 'Legal name' }, { name: 'operatingName', label: 'Operating name' },
          { name: 'criticality', label: 'Criticality', type: 'select', options: critOpts }, { name: 'risk', label: 'Risk', type: 'select', options: critOpts },
          { name: 'siteId', label: 'Managing site', type: 'site' }, { name: 'departmentId', label: 'Department (optional)', type: 'department' },
          { name: 'ownerId', label: 'Supplier quality owner', type: 'user' }, { name: 'website', label: 'Website' },
          { name: 'suppliedScope', label: 'Products, parts and services supplied', type: 'multiline' }, { name: 'address', label: 'Address', type: 'multiline' }]}
        onSubmit={async (v) => {
          const r = await post<{ id: string }>('/api/suppliers', { ...v, departmentId: v.departmentId || null, ownerId: v.ownerId || null, operatingName: v.operatingName || null, suppliedScope: v.suppliedScope || null, address: v.address || null, website: v.website || null });
          nav(`/suppliers/${r.id}`);
        }} />
    </ModuleWorkspace>
  );
}

interface SupplierDto { id: string; code: string; legalName: string; operatingName: string | null; category: string; suppliedScope: string | null; criticality: number; risk: number; status: number; approvedScope: string | null; statusReason: string | null;
  proposedStatus: number | null; proposedScope: string | null; approvedOn: string | null; nextReviewOn: string | null; address: string | null; website: string | null; siteId: string; departmentId: string | null; ownerId: string; rowVersion: number; ownerName: string;
  contacts: { id: string; name: string; role: string | null; email: string | null; phone: string | null }[];
  certifications: { id: string; standard: string; certificateNumber: string | null; issuer: string | null; issuedOn: string | null; expiresOn: string }[];
  overrides: { id: string; recordType: string; condition: string; reason: string; createdAt: string; userName: string }[];
  check: { blocked: boolean; conditions: { condition: string; restriction: number; message: string }[] };
  ncrs: { id: string; number: string; title: string; status: number; detectedOn: string }[] | null;
  scars: { id: string; number: string; title: string; status: number; responseDueOn: string; responseReceivedOn: string | null }[];
  performance: { id: string; periodStart: string; periodEnd: string; lotsReceived: number; lotsRejected: number; deliveries: number; onTimeDeliveries: number; ncrCount: number; qualityScore: number; deliveryScore: number; responsivenessScore: number; overallScore: number; comments: string | null }[];
  reviews: { id: string; reviewedOn: string; outcome: number; summary: string; overallScore: number | null; openScars: number; ncrsLast12Months: number; nextReviewOn: string; reviewerName: string }[]; canEdit: boolean; canApprove: boolean; allowed: string[] }

export function SupplierDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const [tab, setTab] = useState('overview');
  const [dlg, setDlg] = useState<string | null>(null);
  const [signing, setSigning] = useState(false);
  const work = useWork();
  const nav = useNavigate();
  const q = useQuery({ queryKey: ['supplier', id], queryFn: () => get<SupplierDto>(`/api/suppliers/${id}`) });
  const refresh = () => { qc.invalidateQueries({ queryKey: ['supplier', id] }); qc.invalidateQueries({ queryKey: ['work'] }); };
  const { error, run } = useRunner(refresh);
  const d = q.data;
  if (!d) return <ModuleWorkspace module={mod()} crumbs={[{ label: 'Supplier register', to: '/suppliers' }]} title="Loading">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const approval = work.data?.approvals.find((a) => a.recordId === d.id);
  const today = localDate();
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Supplier register', to: '/suppliers' }, { label: d.code }]}
      title={<Stack direction="row" gap={1.5} alignItems="center" sx={{ flexWrap: 'wrap', rowGap: 0.5 }}><span style={{ fontFamily: mono, whiteSpace: 'nowrap' }}>{d.code}</span><span>{d.legalName}</span><StatusChip status={supplierStatus[d.status]} /></Stack>}
      tabs={[{ label: 'Overview', value: 'overview' }, { label: 'Contacts', value: 'contacts' }, { label: 'Certifications', value: 'certs' }, { label: 'Quality events', value: 'ncrs' }, { label: 'SCARs', value: 'scars' }, { label: 'Performance', value: 'perf' }, { label: 'Reviews', value: 'reviews' }, { label: 'Approvals', value: 'approvals' }, { label: 'Files & history', value: 'more' }]} tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1}>
        {approval && <Button variant="contained" color="secondary" onClick={() => setSigning(true)}>Sign approval</Button>}
        {d.allowed.includes('edit') && <Button variant="outlined" onClick={() => setDlg('edit')}>Edit</Button>}
        {d.allowed.includes('requestApproval') && <Button variant="contained" onClick={() => setDlg('approve')}>{d.status >= 2 ? 'Request re-approval' : 'Submit for approval'}</Button>}
        {d.allowed.includes('suspend') && <Button variant="outlined" color="warning" onClick={() => setDlg('suspend')}>Suspend</Button>}
        {d.allowed.includes('disqualify') && <Button variant="outlined" color="error" onClick={() => setDlg('disqualify')}>Disqualify</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {d.check.conditions.map((c) => <Alert key={c.condition} severity={c.restriction === 2 ? 'error' : 'warning'} sx={{ mb: 1 }}>{c.restriction === 2 ? 'Restricted — ' : 'Warning — '}{c.message}</Alert>)}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: '280px minmax(0, 1fr)' }, gap: 3 }}>
        <Facts items={[['Category', d.category], ['Operating name', d.operatingName ?? '—'], ['Criticality', criticality[d.criticality]], ['Risk', criticality[d.risk]], ['Owner', d.ownerName],
          ['Approved on', fmtDate(d.approvedOn)], ['Next review', fmtDate(d.nextReviewOn)], ['Website', d.website ?? '—']]} />
        <Box sx={{ minWidth: 0 }}>
          {tab === 'overview' && <Stack gap={2}>
            <Section title="Scope">
              <Prose label="Supplied products, parts and services" text={d.suppliedScope} />
              {d.approvedScope ? <Prose label="Approved scope" text={d.approvedScope} /> : <Alert severity="info">No approved scope yet.</Alert>}
              {d.proposedScope && <Prose label={`Proposed (${supplierStatus[d.proposedStatus ?? 2]}) — waiting for approval`} text={d.proposedScope} />}
              <Prose label="Status reason" text={d.statusReason} />
            </Section>
            {d.address && <Section title="Address"><Prose label="Address" text={d.address} /></Section>}
            {d.overrides.length > 0 && <Section title="Restriction overrides">
              <DataTable data={d.overrides} columns={[{ header: 'When', accessorKey: 'createdAt', cell: (x) => fmtDateTime(x.getValue() as string) }, { header: 'By', accessorKey: 'userName' },
                { header: 'Record', accessorKey: 'recordType' }, { header: 'Condition', accessorKey: 'condition' }, { header: 'Reason', accessorKey: 'reason' }]} empty="" />
            </Section>}
          </Stack>}
          {tab === 'contacts' && <Section title="Contacts" action={d.canEdit ? <Button size="small" onClick={() => setDlg('contact')}>Add contact</Button> : undefined}>
            <DataTable data={d.contacts} columns={[{ header: 'Name', accessorKey: 'name' }, { header: 'Role', accessorKey: 'role' }, { header: 'Email', accessorKey: 'email' }, { header: 'Phone', accessorKey: 'phone' },
              ...(d.canEdit ? [{ header: '', id: 'rm', cell: (x: { row: { original: { id: string } } }) => <IconButton size="small" aria-label="Remove contact" onClick={() => { if (confirm('Remove this contact?')) run(() => del(`/api/suppliers/${d.id}/contacts/${x.row.original.id}`)); }}><DeleteOutline fontSize="small" /></IconButton> }] : [])]} empty="No contacts." />
          </Section>}
          {tab === 'certs' && <Section title="Certifications" action={d.canEdit ? <Button size="small" onClick={() => setDlg('cert')}>Add certification</Button> : undefined}>
            <DataTable data={d.certifications} columns={[{ header: 'Standard', accessorKey: 'standard' }, { header: 'Certificate', accessorKey: 'certificateNumber' }, { header: 'Issuer', accessorKey: 'issuer' },
              { header: 'Issued', accessorKey: 'issuedOn', cell: (x) => fmtDate(x.getValue() as string) },
              { header: 'Expires', accessorKey: 'expiresOn', cell: (x) => <Box component="span" sx={{ color: (x.getValue() as string) < today ? 'error.main' : undefined }}>{fmtDate(x.getValue() as string)}</Box> }]} empty="No certifications recorded." />
          </Section>}
          {tab === 'ncrs' && <Section title="Supplier nonconformances">
            {d.ncrs === null ? <Alert severity="info">You do not have access to nonconformances.</Alert> : d.ncrs.length === 0 ? <Box sx={{ color: 'text.secondary', fontSize: 14 }}>None recorded.</Box> :
              d.ncrs.map((n) => <Box key={n.id} sx={{ py: 0.5 }}><Link component={RouterLink} to={`/ncr/${n.id}`} sx={{ fontFamily: mono }}>{n.number}</Link> {n.title} · {fmtDate(n.detectedOn)} <StatusChip status={ncrStatus[n.status]} /></Box>)}
          </Section>}
          {tab === 'scars' && <Section title="Supplier corrective action requests" action={d.allowed.includes('scar') ? <Button size="small" onClick={() => setDlg('scar')}>Issue SCAR</Button> : undefined}>
            <DataTable data={d.scars} onRowClick={(r) => nav(`/suppliers/scar/${r.id}`)} columns={[{ header: 'Number', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' },
              { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={scarStatus[x.getValue() as number]} /> },
              { header: 'Response due', accessorKey: 'responseDueOn', cell: (x) => <Box component="span" sx={{ color: (x.getValue() as string) < today && x.row.original.status === 0 ? 'error.main' : undefined }}>{fmtDate(x.getValue() as string)}</Box> },
              { header: 'Response received', accessorKey: 'responseReceivedOn', cell: (x) => fmtDate(x.getValue() as string) }]} empty="No SCARs issued." />
          </Section>}
          {tab === 'perf' && <Section title="Performance scorecards" action={d.allowed.includes('performance') ? <Button size="small" onClick={() => setDlg('perf')}>Record period</Button> : undefined}>
            <Box sx={{ color: 'text.secondary', fontSize: 13, mb: 1 }}>Overall = quality 50% + delivery 30% + responsiveness 20%. NCR counts and SCAR responsiveness are taken from records.</Box>
            <DataTable data={d.performance} columns={[{ header: 'Period', id: 'p', accessorFn: (r) => `${fmtDate(r.periodStart)} – ${fmtDate(r.periodEnd)}` },
              { header: 'Lots (rej.)', id: 'l', accessorFn: (r) => `${r.lotsReceived} (${r.lotsRejected})` }, { header: 'On time', id: 'o', accessorFn: (r) => `${r.onTimeDeliveries}/${r.deliveries}` }, { header: 'NCRs', accessorKey: 'ncrCount' },
              { header: 'Quality', accessorKey: 'qualityScore' }, { header: 'Delivery', accessorKey: 'deliveryScore' }, { header: 'Responsiveness', accessorKey: 'responsivenessScore' },
              { header: 'Overall', accessorKey: 'overallScore', cell: (x) => <b style={{ color: (x.getValue() as number) < 80 ? '#c62828' : undefined }}>{String(x.getValue())}</b> }, { header: 'Comments', accessorKey: 'comments' }]} empty="No performance periods recorded." />
          </Section>}
          {tab === 'reviews' && <Section title="Periodic reviews" action={d.allowed.includes('review') ? <Button size="small" onClick={() => setDlg('review')}>Record review</Button> : undefined}>
            <DataTable data={d.reviews} columns={[{ header: 'Reviewed', accessorKey: 'reviewedOn', cell: (x) => fmtDate(x.getValue() as string) }, { header: 'Reviewer', accessorKey: 'reviewerName' },
              { header: 'Outcome', accessorKey: 'outcome', cell: (x) => reviewOutcome[x.getValue() as number] }, { header: 'Score', accessorKey: 'overallScore', cell: (x) => x.getValue() ?? '—' },
              { header: 'Open SCARs', accessorKey: 'openScars' }, { header: 'NCRs (12 mo)', accessorKey: 'ncrsLast12Months' }, { header: 'Summary', accessorKey: 'summary' },
              { header: 'Next review', accessorKey: 'nextReviewOn', cell: (x) => fmtDate(x.getValue() as string) }]} empty="No periodic reviews recorded." />
          </Section>}
          {tab === 'approvals' && <ApprovalsPanel type="Supplier" id={d.id} />}
          {tab === 'more' && <AttachmentsAndHistory type="Supplier" id={d.id} canEdit={d.canEdit} />}
        </Box>
      </Box>
      <StepDialog open={dlg === 'approve'} title="Submit for approval" intro="Supplier approvers sign the decision electronically." confirm="Submit" onClose={() => setDlg(null)}
        inputs={[{ name: 'proposedStatus', label: 'Proposed status', type: 'select', initial: '2', options: [[2, 'Approved'], [3, 'Conditional']] },
          { name: 'scope', label: 'Scope to approve (and conditions, if conditional)', type: 'multiline', initial: d.approvedScope ?? d.suppliedScope ?? '' }, { name: 'nextReviewOn', label: 'Next periodic review', type: 'date', initial: d.nextReviewOn ?? localDate(365) }]}
        onConfirm={(v) => run(() => post(`/api/suppliers/${d.id}/request-approval`, { proposedStatus: Number(v.proposedStatus), scope: v.scope, nextReviewOn: v.nextReviewOn || null }))} />
      <StepDialog open={dlg === 'suspend'} title={`Suspend ${d.code}`} confirm="Suspend" danger onClose={() => setDlg(null)} inputs={[{ name: 'reason', label: 'Reason', type: 'multiline' }]}
        onConfirm={(v) => run(() => post(`/api/suppliers/${d.id}/suspend`, v))} />
      <StepDialog open={dlg === 'disqualify'} title={`Disqualify ${d.code}`} confirm="Disqualify" danger onClose={() => setDlg(null)} inputs={[{ name: 'reason', label: 'Reason', type: 'multiline' }]}
        onConfirm={(v) => run(() => post(`/api/suppliers/${d.id}/disqualify`, v))} />
      <StepDialog open={dlg === 'contact'} title="Add contact" confirm="Add" onClose={() => setDlg(null)}
        inputs={[{ name: 'name', label: 'Name' }, { name: 'role', label: 'Role' }, { name: 'email', label: 'Email' }, { name: 'phone', label: 'Phone' }]}
        onConfirm={(v) => run(() => post(`/api/suppliers/${d.id}/contacts`, { name: v.name, role: v.role || null, email: v.email || null, phone: v.phone || null }))} />
      <StepDialog open={dlg === 'cert'} title="Add certification" confirm="Add" onClose={() => setDlg(null)}
        inputs={[{ name: 'standard', label: 'Standard (e.g. ISO 9001:2015)' }, { name: 'certificateNumber', label: 'Certificate number' }, { name: 'issuer', label: 'Issuer / registrar' },
          { name: 'issuedOn', label: 'Issued on', type: 'date' }, { name: 'expiresOn', label: 'Expires on', type: 'date' }]}
        onConfirm={(v) => run(() => post(`/api/suppliers/${d.id}/certifications`, { standard: v.standard, certificateNumber: v.certificateNumber || null, issuer: v.issuer || null, issuedOn: v.issuedOn || null, expiresOn: v.expiresOn }))} />
      <StepDialog open={dlg === 'edit'} title="Edit supplier" confirm="Save" onClose={() => setDlg(null)}
        inputs={[{ name: 'legalName', label: 'Legal name', initial: d.legalName }, { name: 'operatingName', label: 'Operating name', initial: d.operatingName ?? '' }, { name: 'category', label: 'Category', initial: d.category },
          { name: 'criticality', label: 'Criticality', type: 'select', initial: String(d.criticality), options: critOpts }, { name: 'risk', label: 'Risk', type: 'select', initial: String(d.risk), options: critOpts },
          { name: 'suppliedScope', label: 'Supplied scope', type: 'multiline', initial: d.suppliedScope ?? '' }, { name: 'website', label: 'Website', initial: d.website ?? '' }, { name: 'address', label: 'Address', type: 'multiline', initial: d.address ?? '' }]}
        onConfirm={(v) => run(() => put(`/api/suppliers/${d.id}`, { code: d.code, legalName: v.legalName, operatingName: v.operatingName || null, category: v.category, suppliedScope: v.suppliedScope || null, criticality: Number(v.criticality), risk: Number(v.risk),
          address: v.address || null, website: v.website || null, siteId: d.siteId, departmentId: d.departmentId, ownerId: d.ownerId, rowVersion: d.rowVersion }))} />
      <StepDialog open={dlg === 'scar'} title={`Issue SCAR to ${d.code}`} confirm="Issue" onClose={() => setDlg(null)}
        inputs={[{ name: 'ncrId', label: 'Related NCR (optional)', type: 'select', initial: '', options: [['', '— none —'], ...(d.ncrs ?? []).map((n) => [n.id, `${n.number} ${n.title}`] as [string, string])] },
          { name: 'title', label: 'Title' }, { name: 'problem', label: 'Problem statement', type: 'multiline' }, { name: 'responseDueOn', label: 'Response due', type: 'date', initial: localDate(14) }]}
        onConfirm={(v) => run(async () => { const r = await post<{ id: string }>('/api/suppliers/scars', { supplierId: d.id, ncrId: v.ncrId || null, title: v.title, problem: v.problem, responseDueOn: v.responseDueOn }); nav(`/suppliers/scar/${r.id}`); })} />
      <StepDialog open={dlg === 'perf'} title="Record performance period" confirm="Record" onClose={() => setDlg(null)}
        inputs={[{ name: 'periodStart', label: 'Period start', type: 'date', initial: localDate(-90) }, { name: 'periodEnd', label: 'Period end', type: 'date', initial: localDate() },
          { name: 'lotsReceived', label: 'Lots received', initial: '0' }, { name: 'lotsRejected', label: 'Lots rejected', initial: '0' }, { name: 'deliveries', label: 'Deliveries', initial: '0' }, { name: 'onTimeDeliveries', label: 'On-time deliveries', initial: '0' },
          { name: 'comments', label: 'Comments', type: 'multiline' }]}
        onConfirm={(v) => run(() => post(`/api/suppliers/${d.id}/performance`, { periodStart: v.periodStart, periodEnd: v.periodEnd, lotsReceived: Number(v.lotsReceived), lotsRejected: Number(v.lotsRejected), deliveries: Number(v.deliveries), onTimeDeliveries: Number(v.onTimeDeliveries), comments: v.comments || null }))} />
      <StepDialog open={dlg === 'review'} title={`Periodic review — ${d.code}`} intro="The latest scorecard, open SCARs and NCRs from the last 12 months are captured with the review. Suspend or disqualify outcomes change the supplier status." confirm="Record review" onClose={() => setDlg(null)}
        inputs={[{ name: 'outcome', label: 'Outcome', type: 'select', initial: '0', options: reviewOutcome.map((o, i) => [i, o] as [number, string]) }, { name: 'summary', label: 'Summary and rationale', type: 'multiline' }, { name: 'nextReviewOn', label: 'Next review', type: 'date', initial: localDate(365) }]}
        onConfirm={(v) => run(() => post(`/api/suppliers/${d.id}/reviews`, { outcome: Number(v.outcome), summary: v.summary, nextReviewOn: v.nextReviewOn }))} />
      {signing && approval && <SignDialog approvalId={approval.id} recordNumber={approval.recordNumber} stepName={approval.stepName} onClose={() => { setSigning(false); refresh(); }} />}
    </ModuleWorkspace>
  );
}

interface ScarRow { id: string; number: string; title: string; status: number; issuedOn: string; responseDueOn: string; responseReceivedOn: string | null; responseRejections: number; supplier: string; ownerName: string }

export function ScarList() {
  const nav = useNavigate();
  const [p, setP] = useSearchParams();
  const all = p.get('all') === '1';
  const list = useQuery({ queryKey: ['scars', all], queryFn: () => get<{ items: ScarRow[] }>(`/api/suppliers/scars?pageSize=200${all ? '' : '&openOnly=true'}`) });
  const today = localDate();
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Corrective action requests' }]} title="Supplier corrective action requests"
      tabs={[{ label: 'Open', value: '' }, { label: 'All', value: '1' }]} tab={all ? '1' : ''} onTab={(v) => setP(v ? { all: v } : {})}>
      <DataTable data={list.data?.items ?? []} onRowClick={(r) => nav(`/suppliers/scar/${r.id}`)} empty="No SCARs in this view." columns={[
        { header: 'Number', accessorKey: 'number', cell: num }, { header: 'Supplier', accessorKey: 'supplier' }, { header: 'Title', accessorKey: 'title' },
        { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={scarStatus[x.getValue() as number]} /> }, { header: 'Issued', accessorKey: 'issuedOn', cell: (x) => fmtDate(x.getValue() as string) },
        { header: 'Response due', accessorKey: 'responseDueOn', cell: (x) => <Box component="span" sx={{ color: (x.getValue() as string) < today && x.row.original.status === 0 ? 'error.main' : undefined }}>{fmtDate(x.getValue() as string)}</Box> },
        { header: 'Rejected responses', accessorKey: 'responseRejections' }, { header: 'Owner', accessorKey: 'ownerName' }]} />
    </ModuleWorkspace>
  );
}

interface ScarDto { id: string; number: string; title: string; problem: string; issuedOn: string; responseDueOn: string; status: number; supplierContainment: string | null; supplierRootCause: string | null; correctiveActionPlan: string | null;
  supplierRespondent: string | null; responseReceivedOn: string | null; responseRejections: number; reviewNotes: string | null; effectivenessNotes: string | null; wasEffective: boolean | null; closedAt: string | null;
  supplier: { id: string; code: string; legalName: string }; ncr: { id: string; number: string; title: string } | null; ownerName: string; verifiedByName: string | null; canEdit: boolean; allowed: string[] }

export function ScarDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const [tab, setTab] = useState('response');
  const [dlg, setDlg] = useState<string | null>(null);
  const q = useQuery({ queryKey: ['scar', id], queryFn: () => get<ScarDto>(`/api/suppliers/scars/${id}`) });
  const { error, run } = useRunner(() => qc.invalidateQueries({ queryKey: ['scar', id] }));
  const d = q.data;
  if (!d) return <ModuleWorkspace module={mod()} crumbs={[{ label: 'Corrective action requests', to: '/suppliers/scar' }]} title="Loading">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Corrective action requests', to: '/suppliers/scar' }, { label: d.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center" sx={{ flexWrap: 'wrap', rowGap: 0.5 }}><span style={{ fontFamily: mono, whiteSpace: 'nowrap' }}>{d.number}</span><span>{d.title}</span><StatusChip status={scarStatus[d.status]} /></Stack>}
      tabs={[{ label: 'Response & review', value: 'response' }, { label: 'Files & history', value: 'more' }]} tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1}>
        {d.allowed.includes('respond') && <Button variant="contained" onClick={() => setDlg('respond')}>Record supplier response</Button>}
        {d.allowed.includes('review') && <Button variant="contained" onClick={() => setDlg('review')}>Review response</Button>}
        {d.allowed.includes('verify') && <Button variant="contained" color="secondary" onClick={() => setDlg('verify')}>Verify effectiveness</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: '280px minmax(0, 1fr)' }, gap: 3 }}>
        <Facts items={[['Supplier', <Link component={RouterLink} to={`/suppliers/${d.supplier.id}`}>{d.supplier.code} {d.supplier.legalName}</Link>],
          ['NCR', d.ncr ? <Link component={RouterLink} to={`/ncr/${d.ncr.id}`} sx={{ fontFamily: mono }}>{d.ncr.number}</Link> : '—'], ['Owner', d.ownerName], ['Issued', fmtDate(d.issuedOn)], ['Response due', fmtDate(d.responseDueOn)],
          ['Response received', fmtDate(d.responseReceivedOn)], ['Rejected responses', String(d.responseRejections)], ['Verified by', d.verifiedByName ?? '—'], ['Closed', fmtDateTime(d.closedAt)]]} />
        <Box sx={{ minWidth: 0 }}>
          {tab === 'response' && <Stack gap={2}>
            <Section title="Problem"><Prose label="Problem statement" text={d.problem} /></Section>
            <Section title="Supplier response">{d.supplierRootCause ? <>
              <Prose label="Respondent" text={d.supplierRespondent} /><Prose label="Containment" text={d.supplierContainment} /><Prose label="Root cause" text={d.supplierRootCause} /><Prose label="Corrective action plan" text={d.correctiveActionPlan} /></>
              : <Alert severity="info">Waiting for the supplier's response.</Alert>}</Section>
            {d.reviewNotes && <Section title="Review"><Prose label="Review notes" text={d.reviewNotes} /></Section>}
            {d.wasEffective !== null && <Section title="Effectiveness"><Alert severity={d.wasEffective ? 'success' : 'warning'} sx={{ mb: 1 }}>{d.wasEffective ? 'Verified effective' : 'Not effective'}</Alert><Prose label="Evidence" text={d.effectivenessNotes} /></Section>}
          </Stack>}
          {tab === 'more' && <AttachmentsAndHistory type="SupplierCorrectiveAction" id={d.id} canEdit={d.canEdit} />}
        </Box>
      </Box>
      <StepDialog open={dlg === 'respond'} title="Record supplier response" confirm="Record" onClose={() => setDlg(null)}
        inputs={[{ name: 'respondent', label: 'Respondent (supplier contact)' }, { name: 'receivedOn', label: 'Received on', type: 'date', initial: localDate() },
          { name: 'containment', label: 'Containment', type: 'multiline' }, { name: 'rootCause', label: 'Root cause', type: 'multiline' }, { name: 'correctiveActionPlan', label: 'Corrective action plan', type: 'multiline' }]}
        onConfirm={(v) => run(() => post(`/api/suppliers/scars/${d.id}/response`, v))} />
      <StepDialog open={dlg === 'review'} title="Review supplier response" intro="Rejecting returns the SCAR to the supplier for a new response." confirm="Save review" onClose={() => setDlg(null)}
        inputs={[{ name: 'accept', label: 'Decision', type: 'select', initial: 'true', options: [['true', 'Accept response'], ['false', 'Reject — request new response']] }, { name: 'notes', label: 'Notes', type: 'multiline' }]}
        onConfirm={(v) => run(() => post(`/api/suppliers/scars/${d.id}/review`, { accept: v.accept === 'true', notes: v.notes }))} />
      <StepDialog open={dlg === 'verify'} title="Verify effectiveness" intro="Verification must be done by someone other than the SCAR owner." confirm="Verify and close" onClose={() => setDlg(null)}
        inputs={[{ name: 'effective', label: 'Result', type: 'select', initial: 'true', options: [['true', 'Effective'], ['false', 'Not effective']] }, { name: 'notes', label: 'Objective evidence', type: 'multiline' }]}
        onConfirm={(v) => run(() => post(`/api/suppliers/scars/${d.id}/verify`, { effective: v.effective === 'true', notes: v.notes }))} />
    </ModuleWorkspace>
  );
}

export function ReviewsDue() {
  const nav = useNavigate();
  const list = useQuery({ queryKey: ['reviews-due'], queryFn: () => get<{ id: string; code: string; legalName: string; status: number; nextReviewOn: string }[]>('/api/suppliers/reviews-due?days=60') });
  const today = localDate();
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Periodic reviews due' }]} title="Periodic reviews due (next 60 days)">
      <DataTable data={list.data ?? []} onRowClick={(r) => nav(`/suppliers/${r.id}`)} empty="No reviews due." columns={[
        { header: 'Code', accessorKey: 'code', cell: num }, { header: 'Supplier', accessorKey: 'legalName' }, { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={supplierStatus[x.getValue() as number]} /> },
        { header: 'Review due', accessorKey: 'nextReviewOn', cell: (x) => <Box component="span" sx={{ color: (x.getValue() as string) < today ? 'error.main' : undefined }}>{fmtDate(x.getValue() as string)}</Box> }]} />
    </ModuleWorkspace>
  );
}
