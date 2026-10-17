import { useState } from 'react';
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Pagination, Paper, Stack, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm, Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import type { ColumnDef } from '@tanstack/react-table';
import { get, post, put } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, docStatus, revStatus, fmtDate } from '../components/Status';
import { ActionsPanel, ApprovalsPanel, CommentsPanel, CustomFieldsPanel, FilesPanel, HistoryPanel, LinksPanel } from '../components/RecordPanels';
import { useWork } from './Home';
import { SignDialog } from '../components/SignDialog';
import { mono } from '../theme';
import { FieldBuilder, SchemaPreview, parseFields, serializeFields, type FieldDef } from './FormBuilder';

type Kind = 0 | 1 | 2;
const cfg = (kind: Kind) => kind === 2
  ? { module: modules.find((m) => m.key === 'documents')!, path: '/forms/templates', noun: 'form template', plural: 'Form templates', docType: 'FormTemplate', revType: 'FormTemplateRevision', edit: 'forms.edit',
      types: ['Inspection Record', 'Checklist', 'Log', 'Report', 'Request'] }
  : kind === 1
  ? { module: modules.find((m) => m.key === 'engineering')!, path: '/drawings', noun: 'drawing', plural: 'Drawings', docType: 'Drawing', revType: 'DrawingRevision', edit: 'drawings.edit',
      types: ['Assembly Drawing', 'Detail Drawing', 'Layout', 'Schematic', 'Specification Control Drawing'] }
  : { module: modules.find((m) => m.key === 'documents')!, path: '/documents', noun: 'document', plural: 'Documents', docType: 'Document', revType: 'DocumentRevision', edit: 'documents.edit',
      types: ['Policy', 'Manual', 'Procedure', 'Work Instruction', 'Specification', 'Form', 'Standard', 'Plan'] };

interface ListItem { id: string; number: string; title: string; documentType: string; status: number; currentRevision: string | null; effectiveDate: string | null; siteCode: string; partNumber: string | null; nextReviewDue: string | null; ownerName: string }
interface Rev { id: string; revision: string; sequence: number; status: number; changeSummary: string; effectiveDate: string | null; supersededOn: string | null; approvedAt: string | null; trainingRequired: boolean; rowVersion: number; fieldSchema?: string | null; readyToSubmit: boolean }
interface Detail { id: string; kind: Kind; number: string; title: string; documentType: string; status: number; siteId: string; ownerName: string; reviewIntervalMonths: number; nextReviewDue: string | null; partNumber: string | null; drawingSize: string | null; sheetCount: number | null; revisions: Rev[]; canEdit: boolean; canApprove: boolean; rowVersion: number }

export function DocumentList({ kind }: { kind: Kind }) {
  const c = cfg(kind);
  const nav = useNavigate();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const [q, setQ] = useState(p.get('q') ?? '');
  const status = p.get('status');
  const page = Number(p.get('page') ?? 1);
  const list = useQuery({
    queryKey: ['docs', kind, status, p.get('q'), page],
    queryFn: () => get<{ items: ListItem[]; total: number; pageSize: number }>(`/api/documents?kind=${kind}&pageSize=50&page=${page}${status ? `&status=${status}` : ''}${p.get('q') ? `&q=${encodeURIComponent(p.get('q')!)}` : ''}`),
  });
  const cols: ColumnDef<ListItem, any>[] = [
    { header: 'Number', accessorKey: 'number', cell: (x) => <span style={{ fontFamily: mono }}>{x.getValue()}</span> },
    { header: 'Title', accessorKey: 'title' },
    ...(kind === 1 ? [{ header: 'Part number', accessorKey: 'partNumber' } as ColumnDef<ListItem, any>] : []),
    { header: 'Type', accessorKey: 'documentType' },
    { header: 'Rev', accessorKey: 'currentRevision', cell: (x) => x.getValue() ?? '—' },
    { header: 'Effective', accessorKey: 'effectiveDate', cell: (x) => fmtDate(x.getValue()) },
    { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={docStatus[x.getValue()]} /> },
    { header: 'Site', accessorKey: 'siteCode' },
    { header: 'Owner', accessorKey: 'ownerName' },
    { header: 'Review due', accessorKey: 'nextReviewDue', cell: (x) => fmtDate(x.getValue()) },
  ];
  const tabs = [{ label: 'All', value: '' }, { label: 'Effective', value: '1' }, { label: 'Drafts', value: '0' }, { label: 'Obsolete', value: '2' }];
  return (
    <ModuleWorkspace module={c.module} crumbs={[{ label: kind === 0 ? 'Library' : c.plural }]} title={c.plural} tabs={tabs} tab={status ?? ''}
      onTab={(v) => { const n = new URLSearchParams(p); if (v) n.set('status', v); else n.delete('status'); n.delete('page'); setP(n); }}
      actions={can(c.edit) ? <Button variant="contained" component={RouterLink} to={`${c.path}/new`}>New {c.noun}</Button> : undefined}>
      <Stack direction="row" gap={1} component="form" sx={{ mb: 2 }} onSubmit={(e) => { e.preventDefault(); const n = new URLSearchParams(p); if (q) n.set('q', q); else n.delete('q'); n.delete('page'); setP(n); }}>
        <TextField size="small" placeholder={`Filter by number, title${kind === 1 ? ' or part number' : ''}`} value={q} onChange={(e) => setQ(e.target.value)} sx={{ width: 380 }} />
        <Button type="submit" variant="outlined">Filter</Button>
      </Stack>
      <DataTable data={list.data?.items ?? []} columns={cols} onRowClick={(r) => nav(`${c.path}/${r.id}`)} empty={`No ${c.plural.toLowerCase()} found.`} />
      {list.data && list.data.total > list.data.pageSize && (
        <Pagination sx={{ mt: 2 }} count={Math.ceil(list.data.total / list.data.pageSize)} page={page} onChange={(_, v) => { const n = new URLSearchParams(p); n.set('page', String(v)); setP(n); }} />
      )}
    </ModuleWorkspace>
  );
}

const createSchema = z.object({
  title: z.string().trim().min(1, 'Required').max(300),
  documentType: z.string().min(1, 'Required'),
  siteId: z.string().uuid('Choose a site'),
  departmentId: z.string().optional(),
  reviewIntervalMonths: z.coerce.number().int().min(1).max(120),
  initialRevision: z.string().trim().max(10).optional(),
  partNumber: z.string().max(60).optional(),
  drawingSize: z.string().max(10).optional(),
  sheetCount: z.union([z.literal(''), z.coerce.number().int().positive()]).optional(),
  changeSummary: z.string().max(4000).optional(),
});
type CreateForm = z.input<typeof createSchema>;
interface Site { id: string; code: string; name: string; isArchived: boolean; departments: { id: string; code: string; name: string }[] }

export function DocumentNew({ kind }: { kind: Kind }) {
  const c = cfg(kind);
  const nav = useNavigate();
  const [error, setError] = useState<string | null>(null);
  const sites = useQuery({ queryKey: ['sites'], queryFn: () => get<Site[]>('/api/organization/sites') });
  const { register, control, handleSubmit, watch, formState: { errors, isSubmitting } } = useForm<CreateForm>({
    resolver: zodResolver(createSchema), defaultValues: { title: '', documentType: '', siteId: '', departmentId: '', reviewIntervalMonths: 24, initialRevision: 'A', changeSummary: 'Initial release' },
  });
  const site = sites.data?.find((s) => s.id === watch('siteId'));
  const submit = handleSubmit(async (v) => {
    setError(null);
    try {
      const parsed = createSchema.parse(v);
      const id = await post<string>('/api/documents', { ...parsed, kind, departmentId: parsed.departmentId || null, sheetCount: parsed.sheetCount === '' ? null : parsed.sheetCount ?? null });
      nav(`${c.path}/${id}`);
    } catch (e) { setError((e as Error).message); }
  });
  return (
    <ModuleWorkspace module={c.module} crumbs={[{ label: kind === 0 ? 'Library' : c.plural, to: c.path }, { label: 'New' }]} title={`New ${c.noun}`}>
      <Paper component="form" onSubmit={submit} sx={{ p: 3, maxWidth: 760, display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}>
        {error && <Alert severity="error" sx={{ gridColumn: '1 / -1' }}>{error}</Alert>}
        <TextField label="Title" sx={{ gridColumn: '1 / -1' }} {...register('title')} error={!!errors.title} helperText={errors.title?.message} />
        <Controller name="documentType" control={control} render={({ field }) => (
          <TextField select label="Type" {...field} error={!!errors.documentType} helperText={errors.documentType?.message}>{c.types.map((t) => <MenuItem key={t} value={t}>{t}</MenuItem>)}</TextField>)} />
        <TextField label="Review interval (months)" type="number" {...register('reviewIntervalMonths')} error={!!errors.reviewIntervalMonths} />
        <Controller name="siteId" control={control} render={({ field }) => (
          <TextField select label="Site" {...field} error={!!errors.siteId} helperText={errors.siteId?.message}>
            {(sites.data ?? []).filter((s) => !s.isArchived).map((s) => <MenuItem key={s.id} value={s.id}>{s.code} — {s.name}</MenuItem>)}
          </TextField>)} />
        <Controller name="departmentId" control={control} render={({ field }) => (
          <TextField select label="Department (optional)" {...field} disabled={!site}>
            <MenuItem value="">None</MenuItem>{(site?.departments ?? []).map((d) => <MenuItem key={d.id} value={d.id}>{d.code} — {d.name}</MenuItem>)}
          </TextField>)} />
        <TextField label="Initial revision" {...register('initialRevision')} />
        {kind === 1 && <TextField label="Part number" {...register('partNumber')} />}
        {kind === 1 && <TextField label="Drawing size" {...register('drawingSize')} />}
        {kind === 1 && <TextField label="Sheets" type="number" {...register('sheetCount')} />}
        <TextField label="Change summary" multiline minRows={2} sx={{ gridColumn: '1 / -1' }} {...register('changeSummary')} />
        <Stack direction="row" gap={1} sx={{ gridColumn: '1 / -1' }}>
          <Button type="submit" variant="contained" disabled={isSubmitting}>Create {c.noun}</Button>
          <Button component={RouterLink} to={c.path}>Cancel</Button>
        </Stack>
      </Paper>
    </ModuleWorkspace>
  );
}

export function DocumentDetail({ kind }: { kind: Kind }) {
  const c = cfg(kind);
  const { id } = useParams();
  const [p] = useSearchParams();
  const qc = useQueryClient();
  const [tab, setTab] = useState('revisions');
  const [revId, setRevId] = useState<string | null>(p.get('rev'));
  const [error, setError] = useState<string | null>(null);
  const [revise, setRevise] = useState(false);
  const [summary, setSummary] = useState('');
  const [obsolete, setObsolete] = useState(false);
  const [reason, setReason] = useState('');
  const [signing, setSigning] = useState(false);
  const work = useWork();
  const doc = useQuery({ queryKey: ['doc', id], queryFn: () => get<Detail>(`/api/documents/${id}`) });
  const d = doc.data;
  if (!d) return <ModuleWorkspace module={c.module} crumbs={[{ label: kind === 0 ? 'Library' : c.plural, to: c.path }]} title="Loading">{doc.error ? <Alert severity="error">{(doc.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const rev = d.revisions.find((r) => r.id === revId) ?? d.revisions[0];
  const effective = d.revisions.find((r) => r.status === 3);
  const draftEditable = d.canEdit && rev.status === 0;
  const pendingApproval = work.data?.approvals.find((a) => a.recordId === rev.id);
  const refresh = () => qc.invalidateQueries({ queryKey: ['doc', id] });
  const run = async (f: () => Promise<unknown>) => { setError(null); try { await f(); refresh(); qc.invalidateQueries({ queryKey: ['work'] }); } catch (e) { setError((e as Error).message); } };

  const revCols: ColumnDef<Rev, any>[] = [
    { header: 'Rev', accessorKey: 'revision', cell: (x) => <b style={{ fontFamily: mono }}>{x.getValue()}</b> },
    { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={revStatus[x.getValue()]} /> },
    { header: 'Change summary', accessorKey: 'changeSummary' },
    { header: 'Effective', accessorKey: 'effectiveDate', cell: (x) => fmtDate(x.getValue()) },
    { header: 'Superseded', accessorKey: 'supersededOn', cell: (x) => fmtDate(x.getValue()) },
  ];
  const tabs = [
    { label: 'Revisions', value: 'revisions' }, { label: 'Files', value: 'files' }, { label: 'Approvals', value: 'approvals' },
    { label: 'Actions', value: 'actions' }, { label: 'Links', value: 'links' }, { label: 'Comments', value: 'comments' }, { label: 'History', value: 'history' },
  ];
  return (
    <ModuleWorkspace module={c.module} crumbs={[{ label: kind === 0 ? 'Library' : c.plural, to: c.path }, { label: d.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{d.number}</span><span>{d.title}</span><StatusChip status={docStatus[d.status]} /></Stack>}
      tabs={tabs} tab={tab} onTab={setTab}
      actions={
        <Stack direction="row" gap={1}>
          {pendingApproval && <Button variant="contained" color="secondary" onClick={() => setSigning(true)}>Review and sign</Button>}
          {draftEditable && rev.readyToSubmit && <Button variant="contained" onClick={() => run(() => post(`/api/documents/revisions/${rev.id}/submit`))}>Submit Rev {rev.revision} for approval</Button>}
          {d.canEdit && d.status === 1 && !d.revisions.some((r) => r.status <= 1) && <Button variant="outlined" onClick={() => { setSummary(''); setRevise(true); }}>Start new revision</Button>}
          {d.canApprove && d.status === 1 && !d.revisions.some((r) => r.status <= 1) && <Button color="error" onClick={() => { setReason(''); setObsolete(true); }}>Make obsolete</Button>}
        </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {draftEditable && !rev.readyToSubmit && <Alert severity="info" sx={{ mb: 2 }}>{kind === 2 ? 'Define and save the form fields' : 'Attach the controlled file'} on revision {rev.revision} before submitting it for approval.</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: '280px 1fr', gap: 3 }}>
        <Paper sx={{ p: 2, alignSelf: 'start' }}>
          <Typography variant="overline" color="text.secondary">Details</Typography>
          {[
            ['Type', d.documentType], ['Owner', d.ownerName], ['Current revision', effective ? `${effective.revision} (effective ${fmtDate(effective.effectiveDate)})` : 'None released'],
            ['Review every', `${d.reviewIntervalMonths} months`], ['Next review', fmtDate(d.nextReviewDue)],
            ...(kind === 1 ? [['Part number', d.partNumber ?? '—'], ['Size / sheets', `${d.drawingSize ?? '—'} / ${d.sheetCount ?? '—'}`]] : []),
          ].map(([k, v]) => (
            <Box key={k} sx={{ py: 0.75, borderTop: 1, borderColor: 'divider' }}>
              <Typography variant="caption" color="text.secondary">{k}</Typography><Typography variant="body2">{v}</Typography>
            </Box>
          ))}
          <Box sx={{ mt: 2 }}><CustomFieldsPanel type={c.docType} id={d.id} canEdit={d.canEdit && d.status !== 2} /></Box>
        </Paper>
        <Box sx={{ minWidth: 0 }}>
          {tab !== 'revisions' && ['files', 'approvals'].includes(tab) && (
            <TextField select size="small" label="Revision" value={rev.id} onChange={(e) => setRevId(e.target.value)} sx={{ mb: 2, width: 260 }}>
              {d.revisions.map((r) => <MenuItem key={r.id} value={r.id}>Rev {r.revision} — {revStatus[r.status]}</MenuItem>)}
            </TextField>
          )}
          {tab === 'revisions' && (
            <Stack gap={2}>
              <DataTable data={d.revisions} columns={revCols} onRowClick={(r) => { setRevId(r.id); setTab('files'); }} />
              {draftEditable && <DraftEditor rev={rev} onSaved={refresh} formTemplate={kind === 2} />}
              {kind === 2 && !draftEditable && rev.fieldSchema && <SchemaPreview schema={rev.fieldSchema} />}
            </Stack>
          )}
          {tab === 'files' && <FilesPanel type={c.revType} id={rev.id} canEdit={draftEditable} />}
          {tab === 'approvals' && <ApprovalsPanel type={c.revType} id={rev.id} />}
          {tab === 'actions' && <ActionsPanel type={c.docType} id={d.id} canEdit={d.canEdit} />}
          {tab === 'links' && <LinksPanel type={c.docType} id={d.id} canEdit={d.canEdit} />}
          {tab === 'comments' && <CommentsPanel type={c.docType} id={d.id} />}
          {tab === 'history' && <HistoryPanel type={c.docType} id={d.id} />}
        </Box>
      </Box>
      <Dialog open={revise} onClose={() => setRevise(false)} fullWidth maxWidth="sm">
        <DialogTitle>Start a new revision</DialogTitle>
        <DialogContent><Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>The current effective revision stays in force until the new one is approved and released.</Typography>
          <TextField fullWidth multiline minRows={3} label="What is changing and why" value={summary} onChange={(e) => setSummary(e.target.value)} /></DialogContent>
        <DialogActions><Button onClick={() => setRevise(false)}>Cancel</Button>
          <Button variant="contained" disabled={!summary.trim()} onClick={() => { setRevise(false); run(async () => { const nid = await post<string>(`/api/documents/${d.id}/revisions`, { changeSummary: summary }); setRevId(nid); }); }}>Create draft</Button></DialogActions>
      </Dialog>
      <Dialog open={obsolete} onClose={() => setObsolete(false)} fullWidth maxWidth="sm">
        <DialogTitle>Make {d.number} obsolete</DialogTitle>
        <DialogContent><TextField fullWidth multiline minRows={3} label="Reason" value={reason} onChange={(e) => setReason(e.target.value)} sx={{ mt: 1 }} /></DialogContent>
        <DialogActions><Button onClick={() => setObsolete(false)}>Cancel</Button>
          <Button color="error" variant="contained" disabled={!reason.trim()} onClick={() => { setObsolete(false); run(() => post(`/api/documents/${d.id}/obsolete`, { reason })); }}>Make obsolete</Button></DialogActions>
      </Dialog>
      {signing && pendingApproval && <SignDialog approvalId={pendingApproval.id} recordNumber={pendingApproval.recordNumber} stepName={pendingApproval.stepName} onClose={() => { setSigning(false); refresh(); }} />}
    </ModuleWorkspace>
  );
}

function DraftEditor({ rev, onSaved, formTemplate }: { rev: Rev; onSaved: () => void; formTemplate?: boolean }) {
  const [fields, setFields] = useState<FieldDef[]>(() => parseFields(rev.fieldSchema));
  const [summary, setSummary] = useState(rev.changeSummary);
  const [eff, setEff] = useState(rev.effectiveDate ?? '');
  const [training, setTraining] = useState(rev.trainingRequired);
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);
  return (
    <Paper sx={{ p: 2, maxWidth: 720 }}>
      <Typography variant="h3" sx={{ mb: 1.5 }}>Draft revision {rev.revision}</Typography>
      {msg && <Alert severity={msg.ok ? 'success' : 'error'} sx={{ mb: 1.5 }}>{msg.text}</Alert>}
      <Stack gap={2}>
        <TextField label="Change summary" multiline minRows={2} value={summary} onChange={(e) => setSummary(e.target.value)} />
        <TextField label="Effective date (blank = on approval)" type="date" value={eff} onChange={(e) => setEff(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} sx={{ width: 280 }} />
        <FormControlLabel control={<Checkbox checked={training} onChange={(e) => setTraining(e.target.checked)} />} label="Training required when released" />
        {formTemplate && <FieldBuilder fields={fields} onChange={setFields} />}
        <Box><Button variant="outlined" onClick={async () => {
          try { await put(`/api/documents/revisions/${rev.id}`, { changeSummary: summary, effectiveDate: eff || null, trainingRequired: training, rowVersion: rev.rowVersion, fieldSchema: formTemplate ? serializeFields(fields) : undefined }); setMsg({ ok: true, text: 'Saved.' }); onSaved(); }
          catch (e) { setMsg({ ok: false, text: (e as Error).message }); }
        }}>Save draft</Button></Box>
      </Stack>
    </Paper>
  );
}
