import { useState } from 'react';
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, IconButton, Link, MenuItem, Stack, TextField, Typography } from '@mui/material';
import DeleteOutline from '@mui/icons-material/DeleteOutline';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { z } from 'zod';
import { del, get, post, put } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, fmtDate, fmtDateTime, localDate, severity } from '../components/Status';
import { ActionsPanel, ApprovalsPanel, AttachmentsAndHistory } from '../components/RecordTabs';
import { Facts, Prose, SchemaForm, Section, StepDialog, useRunner } from '../components/Kit';
import { RecordPicker, type PickedRecord } from '../components/RecordPicker';
import { SignDialog } from '../components/SignDialog';
import { useWork } from '../work';
import { mono } from '../theme';

export const changeStatus = ['Draft', 'InReview', 'Approved', 'Implementation', 'Verification', 'Review', 'Closed', 'Rejected', 'Cancelled'];
const itemStatus = ['Open', 'Completed', 'Waived'];
const mod = () => modules.find((m) => m.key === 'changes')!;
const num = (x: { getValue: () => unknown }) => <span style={{ fontFamily: mono }}>{String(x.getValue())}</span>;
const changeTypes = ['Design change', 'Drawing change', 'Specification change', 'Document change', 'Material change', 'Supplier change', 'Process change', 'Inspection method change', 'Test method change',
  'Equipment change', 'Software change', 'Facility change', 'Packaging change', 'Labeling change', 'Quality process change', 'Responsibility change', 'Customer requirement change'];
const useCategories = () => useQuery({ queryKey: ['change-categories'], queryFn: () => get<string[]>('/api/changes/categories') });

interface Row { id: string; number: string; title: string; changeType: string; status: number; risk: number; plannedEffectiveOn: string | null; implementedOn: string | null; openItems: number; items: number; ownerName: string }

export function ChangeList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const view = p.get('view') ?? 'open';
  const list = useQuery({ queryKey: ['changes', view], queryFn: () => get<{ items: Row[] }>(`/api/changes?pageSize=200${view === 'open' ? '&openOnly=true' : view === 'all' ? '' : `&status=${view}`}`) });
  const cols: ColumnDef<Row, any>[] = [{ header: 'Number', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' }, { header: 'Type', accessorKey: 'changeType' },
    { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={changeStatus[x.getValue()]} /> }, { header: 'Risk', accessorKey: 'risk', cell: (x) => severity[x.getValue()] },
    { header: 'Affected items', id: 'it', accessorFn: (r) => `${r.items - r.openItems}/${r.items} closed` }, { header: 'Planned effective', accessorKey: 'plannedEffectiveOn', cell: (x) => fmtDate(x.getValue()) },
    { header: 'Implemented', accessorKey: 'implementedOn', cell: (x) => fmtDate(x.getValue()) }, { header: 'Owner', accessorKey: 'ownerName' }];
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Changes' }]} title="Change requests"
      tabs={[{ label: 'Open', value: 'open' }, { label: 'In review', value: '1' }, { label: 'Implementation', value: '3' }, { label: 'Closed', value: '6' }, { label: 'All', value: 'all' }]} tab={view} onTab={(v) => setP(v === 'open' ? {} : { view: v })}
      actions={can('changes.edit') ? <Button variant="contained" component={RouterLink} to="/changes/new">Request change</Button> : undefined}>
      <DataTable data={list.data?.items ?? []} columns={cols} onRowClick={(r) => nav(`/changes/${r.id}`)} empty="No changes in this view." />
    </ModuleWorkspace>
  );
}

const schema = z.object({
  title: z.string().trim().min(1, 'Required').max(300), changeType: z.string().min(1, 'Required'), reason: z.string().trim().min(1, 'Required').max(4000), justification: z.string().max(4000).optional(),
  currentCondition: z.string().max(10000).optional(), proposedCondition: z.string().max(10000).optional(), plannedEffectiveOn: z.string().optional(), costImpact: z.union([z.literal(''), z.coerce.number()]).optional(),
  scheduleImpact: z.string().max(1000).optional(), implementationPlan: z.string().max(10000).optional(), validationPlan: z.string().max(4000).optional(), communicationPlan: z.string().max(4000).optional(),
  siteId: z.string().uuid('Choose a site'), departmentId: z.string().optional(), ownerId: z.string().optional(),
});
type Form = z.infer<typeof schema>;
const n = (v?: string) => (v ? v : null);
const body = (v: Form) => ({ ...v, justification: n(v.justification), currentCondition: n(v.currentCondition), proposedCondition: n(v.proposedCondition), plannedEffectiveOn: n(v.plannedEffectiveOn),
  costImpact: v.costImpact === '' || v.costImpact === undefined ? null : v.costImpact, scheduleImpact: n(v.scheduleImpact), implementationPlan: n(v.implementationPlan), validationPlan: n(v.validationPlan),
  communicationPlan: n(v.communicationPlan), departmentId: n(v.departmentId), ownerId: n(v.ownerId) });

export function ChangeNew() {
  const nav = useNavigate();
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Changes', to: '/changes' }, { label: 'New' }]} title="Request a change">
      <SchemaForm schema={schema} submitLabel="Create change request" onCancel={() => nav('/changes')}
        defaults={{ title: '', changeType: 'Design change', reason: '', justification: '', currentCondition: '', proposedCondition: '', plannedEffectiveOn: '', costImpact: '', scheduleImpact: '', implementationPlan: '', validationPlan: '', communicationPlan: '', siteId: '', departmentId: '', ownerId: '' }}
        fields={[{ name: 'title', label: 'Title', full: true }, { name: 'changeType', label: 'Type of change', type: 'select', options: changeTypes.map((t) => [t, t]) }, { name: 'plannedEffectiveOn', label: 'Planned effective date', type: 'date' },
          { name: 'reason', label: 'Reason', type: 'multiline' }, { name: 'justification', label: 'Justification', type: 'multiline' },
          { name: 'currentCondition', label: 'Current condition', type: 'multiline' }, { name: 'proposedCondition', label: 'Proposed condition', type: 'multiline' },
          { name: 'costImpact', label: 'Cost impact', type: 'number' }, { name: 'scheduleImpact', label: 'Schedule impact' },
          { name: 'implementationPlan', label: 'Implementation plan', type: 'multiline' }, { name: 'validationPlan', label: 'Validation / verification plan', type: 'multiline' }, { name: 'communicationPlan', label: 'Communication plan', type: 'multiline' },
          { name: 'siteId', label: 'Site', type: 'site' }, { name: 'departmentId', label: 'Department (optional)', type: 'department' }, { name: 'ownerId', label: 'Change owner (defaults to you)', type: 'user' }]}
        onSubmit={async (v) => { const r = await post<{ id: string }>('/api/changes', body(v)); nav(`/changes/${r.id}`); }} />
    </ModuleWorkspace>
  );
}

interface Item { id: string; category: string; description: string; requiredAction: string | null; recordType: string | null; recordId: string | null; recordLabel: string | null; status: number; completionNotes: string | null; evidenceType: string | null; evidenceLabel: string | null; closedAt: string | null; closedByName: string | null }
interface Dto { id: string; number: string; title: string; changeType: string; reason: string; justification: string | null; currentCondition: string | null; proposedCondition: string | null; riskAssessment: string | null; risk: number; impactAnalysis: string | null;
  costImpact: number | null; scheduleImpact: string | null; implementationPlan: string | null; validationPlan: string | null; communicationPlan: string | null; plannedEffectiveOn: string | null; implementedOn: string | null; implementationNotes: string | null;
  verificationNotes: string | null; verifiedAt: string | null; postImplementationReview: string | null; wasEffective: boolean | null; status: number; closedAt: string | null; reopenCount: number; reopenReason: string | null; siteId: string; departmentId: string | null; ownerId: string;
  rowVersion: number; ownerName: string; requesterName: string; verifiedByName: string | null; siteCode: string; items: Item[]; blockers: string[]; canEdit: boolean; allowed: string[] }

export function ChangeDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const work = useWork();
  const [tab, setTab] = useState('overview');
  const [dlg, setDlg] = useState<string | null>(null);
  const [item, setItem] = useState<Item | null>(null);
  const [signing, setSigning] = useState(false);
  const q = useQuery({ queryKey: ['change', id], queryFn: () => get<Dto>(`/api/changes/${id}`) });
  const refresh = () => { qc.invalidateQueries({ queryKey: ['change', id] }); qc.invalidateQueries({ queryKey: ['work'] }); };
  const { error, run } = useRunner(refresh);
  const d = q.data;
  if (!d) return <ModuleWorkspace module={mod()} crumbs={[{ label: 'Changes', to: '/changes' }]} title="Loading">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const a = (k: string) => d.allowed.includes(k);
  const approval = work.data?.approvals.find((x) => x.recordId === d.id);
  const stages = ['Draft', 'Review', 'Approved', 'Implementation', 'Verification', 'Post-impl. review', 'Closed'];
  const stageIdx = d.status === 1 ? 1 : d.status === 2 ? 2 : d.status === 3 ? 3 : d.status === 4 ? 4 : d.status === 5 ? 5 : d.status === 6 ? 6 : 0;
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Changes', to: '/changes' }, { label: d.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{d.number}</span><span>{d.title}</span><StatusChip status={changeStatus[d.status]} /></Stack>}
      tabs={[{ label: 'Overview', value: 'overview' }, { label: `Affected items (${d.items.length})`, value: 'items' }, { label: 'Actions', value: 'actions' }, { label: 'Approvals', value: 'approvals' }, { label: 'Files & history', value: 'more' }]} tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1} flexWrap="wrap">
        {approval && <Button variant="contained" color="secondary" onClick={() => setSigning(true)}>Sign approval</Button>}
        {a('risk') && <Button variant="outlined" onClick={() => setDlg('risk')}>Risk & impact</Button>}
        {a('submit') && <Button variant="contained" onClick={() => run(() => post(`/api/changes/${d.id}/submit`))}>Submit for approval</Button>}
        {a('start') && <Button variant="contained" onClick={() => run(() => post(`/api/changes/${d.id}/start`))}>Start implementation</Button>}
        {a('implement') && <Button variant="contained" onClick={() => setDlg('implement')}>Implementation complete</Button>}
        {a('verify') && <Button variant="contained" color="secondary" onClick={() => setDlg('verify')}>Verify implementation</Button>}
        {a('review') && <Button variant="contained" onClick={() => setDlg('review')}>Post-implementation review</Button>}
        {a('edit') && <Button onClick={() => setDlg('edit')}>Edit</Button>}
        {a('reopen') && <Button color="warning" onClick={() => setDlg('reopen')}>Reopen</Button>}
        {a('cancel') && <Button color="error" onClick={() => setDlg('cancel')}>Cancel</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <Stack direction="row" sx={{ mb: 2 }}>{stages.map((s, i) => <Box key={s} sx={{ flex: 1, py: 0.6, textAlign: 'center', fontSize: 12.5, borderBottom: 3, borderColor: i <= stageIdx && d.status < 7 ? 'primary.main' : 'divider', color: i === stageIdx ? 'text.primary' : 'text.secondary', fontWeight: i === stageIdx ? 600 : 400 }}>{s}</Box>)}</Stack>
      {d.status === 3 && d.blockers.length > 0 && <Alert severity="warning" sx={{ mb: 2 }}><b>Implementation cannot be completed yet:</b><ul style={{ margin: '4px 0 0', paddingLeft: 18 }}>{d.blockers.map((b) => <li key={b}>{b}</li>)}</ul></Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: '280px 1fr', gap: 3 }}>
        <Facts items={[['Type', d.changeType], ['Requester', d.requesterName], ['Owner', d.ownerName], ['Site', d.siteCode], ['Risk', d.riskAssessment ? severity[d.risk] : '—'], ['Cost impact', d.costImpact?.toLocaleString() ?? '—'],
          ['Schedule impact', d.scheduleImpact ?? '—'], ['Planned effective', fmtDate(d.plannedEffectiveOn)], ['Implemented', fmtDate(d.implementedOn)], ['Verified by', d.verifiedByName ? `${d.verifiedByName} ${fmtDateTime(d.verifiedAt)}` : '—'],
          ['Effective', d.wasEffective === null ? '—' : d.wasEffective ? 'Yes' : 'No'], ['Reopened', d.reopenCount ? `${d.reopenCount}×` : '—']]} />
        <Box sx={{ minWidth: 0 }}>
          {tab === 'overview' && <Stack gap={2}>
            <Section title="Request"><Prose label="Reason" text={d.reason} /><Prose label="Justification" text={d.justification} /><Prose label="Current condition" text={d.currentCondition} /><Prose label="Proposed condition" text={d.proposedCondition} />{d.reopenReason && <Prose label="Reopen reason" text={d.reopenReason} />}</Section>
            <Section title="Risk and impact">{d.riskAssessment ? <><Prose label={`Risk assessment (${severity[d.risk]})`} text={d.riskAssessment} /><Prose label="Impact analysis" text={d.impactAnalysis} /></> : <Alert severity="warning">Risk assessment and impact analysis are required before submission.</Alert>}</Section>
            <Section title="Plans"><Prose label="Implementation plan" text={d.implementationPlan} /><Prose label="Validation / verification" text={d.validationPlan} /><Prose label="Communication" text={d.communicationPlan} /></Section>
            {(d.implementationNotes || d.verificationNotes || d.postImplementationReview) && <Section title="Implementation and review"><Prose label="Implementation notes" text={d.implementationNotes} /><Prose label="Verification" text={d.verificationNotes} /><Prose label="Post-implementation review" text={d.postImplementationReview} /></Section>}
          </Stack>}
          {tab === 'items' && <Section title="Affected items" action={a('items') ? <Button size="small" onClick={() => setDlg('item')}>Add affected item</Button> : undefined}>
            <Typography fontSize={13} color="text.secondary" sx={{ mb: 1 }}>Every item must be completed (documents, drawings, inspection plans and training need the new record linked as evidence) or formally waived by an approver before implementation can be completed.</Typography>
            <DataTable data={d.items} empty="No affected items identified." columns={[{ header: 'Category', accessorKey: 'category' }, { header: 'Description', accessorKey: 'description' },
              { header: 'Record', accessorKey: 'recordLabel', cell: (x) => x.row.original.recordId ? <Link component={RouterLink} to={`/records/${x.row.original.recordType}/${x.row.original.recordId}`}>{x.getValue() as string}</Link> : '—' },
              { header: 'Status', accessorKey: 'status', cell: (x) => <Chip size="small" label={itemStatus[x.getValue() as number]} color={x.getValue() === 1 ? 'success' : x.getValue() === 2 ? 'warning' : 'default'} variant="outlined" /> },
              { header: 'Evidence / notes', id: 'ev', cell: (x) => <Box sx={{ fontSize: 13 }}>{x.row.original.evidenceLabel && <div><b>{x.row.original.evidenceLabel}</b></div>}{x.row.original.completionNotes}{x.row.original.closedByName && <Typography fontSize={12} color="text.secondary">{x.row.original.closedByName} · {fmtDateTime(x.row.original.closedAt)}</Typography>}</Box> },
              { header: '', id: 'act', cell: (x) => x.row.original.status === 0 ? <Stack direction="row" gap={0.5}>
                {a('completeItem') && <Button size="small" onClick={() => { setItem(x.row.original); setDlg('complete'); }}>Complete</Button>}
                {a('waive') && <Button size="small" color="warning" onClick={() => { setItem(x.row.original); setDlg('waive'); }}>Waive</Button>}
                {a('removeItem') && <IconButton size="small" aria-label="Remove item" onClick={() => run(() => del(`/api/changes/${d.id}/items/${x.row.original.id}`))}><DeleteOutline fontSize="small" /></IconButton>}</Stack> : null }]} />
          </Section>}
          {tab === 'actions' && <ActionsPanel type="ChangeRequest" id={d.id} canEdit={a('actions')} canVerifyAs={d.canEdit} onChanged={refresh} />}
          {tab === 'approvals' && <ApprovalsPanel type="ChangeRequest" id={d.id} />}
          {tab === 'more' && <AttachmentsAndHistory type="ChangeRequest" id={d.id} canEdit={d.canEdit} />}
        </Box>
      </Box>
      <StepDialog open={dlg === 'risk'} title="Risk assessment and impact analysis" confirm="Save" onClose={() => setDlg(null)}
        inputs={[{ name: 'risk', label: 'Risk level', type: 'select', initial: String(d.risk), options: severity.map((s, i) => [i, s] as [number, string]) }, { name: 'riskAssessment', label: 'Risk assessment', type: 'multiline', initial: d.riskAssessment ?? '' },
          { name: 'impactAnalysis', label: 'Impact analysis (products, inventory, customers, suppliers, validation)', type: 'multiline', initial: d.impactAnalysis ?? '' }]}
        onConfirm={(v) => run(() => put(`/api/changes/${d.id}/risk`, { risk: Number(v.risk), riskAssessment: v.riskAssessment, impactAnalysis: v.impactAnalysis, rowVersion: d.rowVersion }))} />
      <AddItemDialog open={dlg === 'item'} changeId={d.id} onClose={() => { setDlg(null); refresh(); }} />
      <CompleteItemDialog open={dlg === 'complete'} changeId={d.id} item={item} onClose={() => { setDlg(null); setItem(null); refresh(); }} />
      <StepDialog open={dlg === 'waive'} title={`Waive: ${item?.description ?? ''}`} intro="A waiver is a formal decision that this item does not need to be done. It is recorded with your name." confirm="Waive" onClose={() => { setDlg(null); setItem(null); }}
        inputs={[{ name: 'text', label: 'Justification', type: 'multiline' }]} onConfirm={(v) => run(() => post(`/api/changes/${d.id}/items/${item!.id}/waive`, v))} />
      <StepDialog open={dlg === 'implement'} title="Implementation complete" confirm="Confirm" onClose={() => setDlg(null)}
        inputs={[{ name: 'implementedOn', label: 'Implemented on', type: 'date', initial: localDate() }, { name: 'notes', label: 'Implementation notes', type: 'multiline' }]} onConfirm={(v) => run(() => post(`/api/changes/${d.id}/implement`, v))} />
      <StepDialog open={dlg === 'verify'} title="Verify implementation" intro="Must be someone other than the change owner." confirm="Save" onClose={() => setDlg(null)}
        inputs={[{ name: 'accepted', label: 'Result', type: 'select', initial: 'true', options: [['true', 'Verified — implemented as approved'], ['false', 'Not verified — return to implementation']] }, { name: 'notes', label: 'Verification notes', type: 'multiline' }]}
        onConfirm={(v) => run(() => post(`/api/changes/${d.id}/verify`, { accepted: v.accepted === 'true', notes: v.notes }))} />
      <StepDialog open={dlg === 'review'} title="Post-implementation review" confirm="Record and close" onClose={() => setDlg(null)}
        inputs={[{ name: 'effective', label: 'Was the change effective?', type: 'select', initial: 'true', options: [['true', 'Yes'], ['false', 'No']] }, { name: 'review', label: 'Review', type: 'multiline' }]}
        onConfirm={(v) => run(() => post(`/api/changes/${d.id}/review`, { effective: v.effective === 'true', review: v.review }))} />
      <StepDialog open={dlg === 'reopen'} title={`Reopen ${d.number}`} confirm="Reopen" onClose={() => setDlg(null)} inputs={[{ name: 'text', label: 'Reason', type: 'multiline' }]} onConfirm={(v) => run(() => post(`/api/changes/${d.id}/reopen`, v))} />
      <StepDialog open={dlg === 'cancel'} title={`Cancel ${d.number}`} confirm="Cancel change" danger onClose={() => setDlg(null)} inputs={[{ name: 'text', label: 'Reason', type: 'multiline' }]} onConfirm={(v) => run(() => post(`/api/changes/${d.id}/cancel`, v))} />
      <StepDialog open={dlg === 'edit'} title="Edit change" intro={d.status > 0 ? 'Title, type and proposed condition are fixed after approval.' : undefined} confirm="Save" onClose={() => setDlg(null)}
        inputs={[{ name: 'title', label: 'Title', initial: d.title }, { name: 'reason', label: 'Reason', type: 'multiline', initial: d.reason }, { name: 'justification', label: 'Justification', type: 'multiline', initial: d.justification ?? '' },
          { name: 'currentCondition', label: 'Current condition', type: 'multiline', initial: d.currentCondition ?? '' }, { name: 'proposedCondition', label: 'Proposed condition', type: 'multiline', initial: d.proposedCondition ?? '' },
          { name: 'plannedEffectiveOn', label: 'Planned effective date', type: 'date', initial: d.plannedEffectiveOn ?? '' }, { name: 'implementationPlan', label: 'Implementation plan', type: 'multiline', initial: d.implementationPlan ?? '' },
          { name: 'validationPlan', label: 'Validation plan', type: 'multiline', initial: d.validationPlan ?? '' }, { name: 'communicationPlan', label: 'Communication plan', type: 'multiline', initial: d.communicationPlan ?? '' }]}
        onConfirm={(v) => run(() => put(`/api/changes/${d.id}`, { title: v.title, changeType: d.changeType, reason: v.reason, justification: v.justification || null, currentCondition: v.currentCondition || null, proposedCondition: v.proposedCondition || null,
          siteId: d.siteId, departmentId: d.departmentId, ownerId: d.ownerId, plannedEffectiveOn: v.plannedEffectiveOn || null, costImpact: d.costImpact, scheduleImpact: d.scheduleImpact, implementationPlan: v.implementationPlan || null,
          validationPlan: v.validationPlan || null, communicationPlan: v.communicationPlan || null, rowVersion: d.rowVersion }))} />
      {signing && approval && <SignDialog approvalId={approval.id} recordNumber={approval.recordNumber} stepName={approval.stepName} onClose={() => { setSigning(false); refresh(); }} />}
    </ModuleWorkspace>
  );
}

function AddItemDialog({ open, changeId, onClose }: { open: boolean; changeId: string; onClose: () => void }) {
  const cats = useCategories();
  const [v, setV] = useState({ category: 'Document', description: '', requiredAction: '' });
  const [rec, setRec] = useState<PickedRecord | null>(null);
  const [error, setError] = useState<string | null>(null);
  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>Add affected item</DialogTitle>
      <DialogContent><Stack gap={2} sx={{ pt: 1 }}>
        {error && <Alert severity="error">{error}</Alert>}
        <TextField size="small" select label="Category" value={v.category} onChange={(e) => setV({ ...v, category: e.target.value })}>{(cats.data ?? []).map((c) => <MenuItem key={c} value={c}>{c}</MenuItem>)}</TextField>
        <TextField size="small" label="What is affected" value={v.description} onChange={(e) => setV({ ...v, description: e.target.value })} />
        <TextField size="small" label="Required action" multiline minRows={2} value={v.requiredAction} onChange={(e) => setV({ ...v, requiredAction: e.target.value })} />
        <RecordPicker label="Existing record (optional)" onChange={setRec} />
      </Stack></DialogContent>
      <DialogActions><Button onClick={onClose}>Cancel</Button><Button variant="contained" onClick={async () => {
        setError(null);
        try { await post(`/api/changes/${changeId}/items`, { ...v, requiredAction: v.requiredAction || null, recordType: rec?.type ?? null, recordId: rec?.id ?? null }); setV({ category: v.category, description: '', requiredAction: '' }); setRec(null); onClose(); }
        catch (e) { setError((e as Error).message); }
      }}>Add</Button></DialogActions>
    </Dialog>
  );
}

function CompleteItemDialog({ open, changeId, item, onClose }: { open: boolean; changeId: string; item: Item | null; onClose: () => void }) {
  const [notes, setNotes] = useState('');
  const [ev, setEv] = useState<PickedRecord | null>(null);
  const [error, setError] = useState<string | null>(null);
  const needs = item && ['Document', 'Drawing', 'Inspection plan', 'Training'].includes(item.category);
  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>Complete: {item?.description}</DialogTitle>
      <DialogContent><Stack gap={2} sx={{ pt: 1 }}>
        {error && <Alert severity="error">{error}</Alert>}
        {needs && <Alert severity="info">Link the new {item!.category.toLowerCase()} record (pick the document or plan, then the revision) as evidence.</Alert>}
        <RecordPicker label={needs ? 'Evidence record' : 'Evidence record (optional)'} revisions onChange={setEv} />
        <TextField size="small" label="Completion notes" multiline minRows={3} value={notes} onChange={(e) => setNotes(e.target.value)} />
      </Stack></DialogContent>
      <DialogActions><Button onClick={onClose}>Cancel</Button><Button variant="contained" onClick={async () => {
        setError(null);
        try { await post(`/api/changes/${changeId}/items/${item!.id}/complete`, { notes, evidenceType: ev?.type ?? null, evidenceId: ev?.id ?? null }); setNotes(''); setEv(null); onClose(); }
        catch (e) { setError((e as Error).message); }
      }}>Complete</Button></DialogActions>
    </Dialog>
  );
}
