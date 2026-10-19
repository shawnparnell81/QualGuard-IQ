import { setupOptions } from '../components/SetupLink';
import { useState } from 'react';
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Pagination, Paper, Stack, TextField, Typography } from '@mui/material';
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
import { ActionsPanel, ApprovalsPanel, CommentsPanel, CustomFieldsPanel, FilesPanel, HistoryPanel, LinksPanel, TagsPanel } from '../components/RecordPanels';
import { useWork } from '../work';
import { SignDialog } from '../components/SignDialog';
import { mono } from '../theme';
import { FieldBuilder, SchemaPreview, parseFields, serializeFields, type FieldDef } from './FormBuilder';
import { FormView } from '../forms/FormView';
import { FormDesigner, cleanDefinition, upgradeToV2 } from '../forms/FormDesigner';
import { isV2, parseDefinition, withFamily, type FormDef, type FormTheme } from '../forms/definition';

type Kind = 0 | 1 | 2;
const cfg = (kind: Kind) => kind === 2
  ? { module: modules.find((m) => m.key === 'documents')!, path: '/forms/templates', noun: 'form template', plural: 'Form templates', docType: 'FormTemplate', revType: 'FormTemplateRevision', edit: 'forms.edit',
      types: ['Inspection Record', 'Checklist', 'Log', 'Report', 'Request'] }
  : kind === 1
  ? { module: modules.find((m) => m.key === 'engineering')!, path: '/drawings', noun: 'drawing', plural: 'Drawings', docType: 'Drawing', revType: 'DrawingRevision', edit: 'drawings.edit',
      types: ['Assembly Drawing', 'Detail Drawing', 'Layout', 'Schematic', 'Specification Control Drawing'] }
  : { module: modules.find((m) => m.key === 'documents')!, path: '/documents', noun: 'document', plural: 'Documents', docType: 'Document', revType: 'DocumentRevision', edit: 'documents.edit',
      types: ['Policy', 'Manual', 'Procedure', 'Work Instruction', 'Specification', 'Form', 'Standard', 'Plan'] };

interface ListItem { id: string; number: string; title: string; documentType: string; status: number; currentRevision: string | null; effectiveDate: string | null; siteCode: string; partNumber: string | null; nextReviewDue: string | null; ownerName: string; isReference?: boolean }
interface Rev { id: string; revision: string; sequence: number; status: number; changeSummary: string; effectiveDate: string | null; supersededOn: string | null; approvedAt: string | null; trainingRequired: boolean; rowVersion: number; fieldSchema?: string | null; readyToSubmit: boolean }
interface Detail { id: string; kind: Kind; number: string; title: string; documentType: string; status: number; siteId: string; ownerName: string; reviewIntervalMonths: number; nextReviewDue: string | null; partNumber: string | null; drawingSize: string | null; sheetCount: number | null; revisions: Rev[]; canEdit: boolean; canApprove: boolean; rowVersion: number; formFamily?: FormTheme | null; isReference?: boolean }

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
    { header: 'Type', accessorKey: 'documentType', cell: (x) => <Stack direction="row" gap={1} alignItems="center"><span>{x.getValue()}</span>{x.row.original.isReference && <ReferenceChip />}</Stack> },
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
  const library = useQuery({ queryKey: ['document-types', kind], queryFn: () => get<{ name: string }[]>(`/api/document-types?kind=${kind}`) });
  const typeNames = library.data ? library.data.map((t) => t.name) : c.types;
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
          <TextField select label="Type" {...field} error={!!errors.documentType} helperText={errors.documentType?.message}>{typeNames.map((t) => <MenuItem key={t} value={t}>{t}</MenuItem>)}{setupOptions('documentType', typeNames)}</TextField>)} />
        <TextField label="Review interval (months)" type="number" {...register('reviewIntervalMonths')} error={!!errors.reviewIntervalMonths} />
        <Controller name="siteId" control={control} render={({ field }) => (
          <TextField select label="Site" {...field} error={!!errors.siteId} helperText={errors.siteId?.message}>
            {(sites.data ?? []).filter((s) => !s.isArchived).map((s) => <MenuItem key={s.id} value={s.id}>{s.code} — {s.name}</MenuItem>)}
          </TextField>)} />
        <Controller name="departmentId" control={control} render={({ field }) => (
          <TextField select label="Department (optional)" {...field} disabled={!site}>
            <MenuItem value="">None</MenuItem>{(site?.departments ?? []).map((d) => <MenuItem key={d.id} value={d.id}>{d.code} — {d.name}</MenuItem>)}
          {site && setupOptions('department', site.departments)}</TextField>)} />
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
  const [replace, setReplace] = useState(false);
  const [edition, setEdition] = useState('');
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
    { label: d.isReference ? 'Copies' : 'Revisions', value: 'revisions' }, { label: 'Files', value: 'files' }, ...(d.isReference ? [] : [{ label: 'Approvals', value: 'approvals' }]),
    { label: 'Actions', value: 'actions' }, { label: 'Links', value: 'links' }, { label: 'Comments', value: 'comments' }, { label: 'History', value: 'history' },
  ];
  return (
    <ModuleWorkspace module={c.module} crumbs={[{ label: kind === 0 ? 'Library' : c.plural, to: c.path }, { label: d.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{d.number}</span><span>{d.title}</span><StatusChip status={docStatus[d.status]} />{d.isReference && <ReferenceChip />}</Stack>}
      tabs={tabs} tab={tab} onTab={setTab}
      actions={
        <Stack direction="row" gap={1}>
          {pendingApproval && <Button variant="contained" color="secondary" onClick={() => setSigning(true)}>Review and sign</Button>}
          {d.isReference ? <>
            {draftEditable && rev.readyToSubmit && <Button variant="contained" data-testid="release-reference" onClick={() => run(() => post(`/api/documents/revisions/${rev.id}/release-reference`))}>Release copy {rev.revision}</Button>}
            {d.canEdit && d.status === 1 && !d.revisions.some((r) => r.status === 0) && <Button variant="outlined" data-testid="replace-reference" onClick={() => { setSummary(''); setEdition(''); setReplace(true); }}>Replace copy</Button>}
          </> : <>
            {draftEditable && rev.readyToSubmit && <Button variant="contained" onClick={() => run(() => post(`/api/documents/revisions/${rev.id}/submit`))}>Submit Rev {rev.revision} for approval</Button>}
            {d.canEdit && d.status === 1 && !d.revisions.some((r) => r.status <= 1) && <Button variant="outlined" onClick={() => { setSummary(''); setRevise(true); }}>Start new revision</Button>}
          </>}
          {d.canApprove && d.status === 1 && !d.revisions.some((r) => r.status <= 1) && <Button color="error" onClick={() => { setReason(''); setObsolete(true); }}>Make obsolete</Button>}
        </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {d.isReference && <Alert severity="info" icon={false} data-testid="reference-notice" sx={{ mb: 2, borderLeft: 4, borderColor: 'info.main' }}>
        <b>Reference copy.</b> Kept and controlled here for reference, with every change in the history. It isn't sent for approval and has no revision workflow: when a newer edition comes out, use Replace copy and the earlier copy is kept as superseded.
      </Alert>}
      {draftEditable && !rev.readyToSubmit && <Alert severity="info" sx={{ mb: 2 }}>{d.isReference ? `Attach the reference file to copy ${rev.revision} before releasing it.` : `${kind === 2 ? 'Define and save the form fields' : 'Attach the controlled file'} on revision ${rev.revision} before submitting it for approval.`}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: '280px 1fr', gap: 3 }}>
        <Paper sx={{ p: 2, alignSelf: 'start' }}>
          <Typography variant="overline" color="text.secondary">Details</Typography>
          {[
            ['Type', d.documentType], ['Owner', d.ownerName], ['Current revision', effective ? `${effective.revision} (effective ${fmtDate(effective.effectiveDate)})` : 'None released'],
            ...(d.isReference ? [['Control', 'Reference copy (no approval)']] : [['Review every', `${d.reviewIntervalMonths} months`], ['Next review', fmtDate(d.nextReviewDue)]]),
            ...(kind === 1 ? [['Part number', d.partNumber ?? '—'], ['Size / sheets', `${d.drawingSize ?? '—'} / ${d.sheetCount ?? '—'}`]] : []),
          ].map(([k, v]) => (
            <Box key={k} sx={{ py: 0.75, borderTop: 1, borderColor: 'divider' }}>
              <Typography variant="caption" color="text.secondary">{k}</Typography><Typography variant="body2">{v}</Typography>
            </Box>
          ))}
          <Box sx={{ mt: 2 }}><TagsPanel type={c.docType} id={d.id} canEdit={d.canEdit && d.status !== 2} /></Box>
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
              {draftEditable && <DraftEditor rev={rev} onSaved={refresh} formTemplate={kind === 2} family={d.formFamily} />}
              {kind === 2 && rev.fieldSchema && <Stack direction="row" gap={1}>
                <Button variant="outlined" size="small" href={`/api/forms/templates/revisions/${rev.id}/pdf?style=standardized`} target="_blank">Print rev {rev.revision}</Button>
                {parseDefinition(rev.fieldSchema).layout && <Button variant="outlined" size="small" href={`/api/forms/templates/revisions/${rev.id}/pdf?style=original`} target="_blank">Print original look</Button>}
                {rev.status === 3 && <OfflineWorkbook templateId={d.id} siteId={d.siteId} />}
              </Stack>}
              {kind === 2 && !draftEditable && rev.fieldSchema && (isV2(rev.fieldSchema) ? <Paper sx={{ p: 3 }}><FormView def={withFamily(parseDefinition(rev.fieldSchema), d.formFamily)} values={{}} onChange={() => {}} readOnly /></Paper> : <SchemaPreview schema={rev.fieldSchema} />)}
            </Stack>
          )}
          {tab === 'files' && <FilesPanel type={c.revType} id={rev.id} canEdit={draftEditable} onChanged={refresh} />}
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
      <Dialog open={replace} onClose={() => setReplace(false)} fullWidth maxWidth="sm">
        <DialogTitle>Replace the reference copy</DialogTitle>
        <DialogContent><Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>File a newer edition. The current copy stays in use until you attach the new file and release it, then it is kept as superseded.</Typography>
          <Stack gap={2}>
            <TextField label="Edition or issue (optional)" value={edition} onChange={(e) => setEdition(e.target.value)} helperText="For example 2024 or Issue 7. Left empty, the next label is used." slotProps={{ htmlInput: { maxLength: 10 } }} />
            <TextField fullWidth multiline minRows={2} label="Note (optional)" value={summary} onChange={(e) => setSummary(e.target.value)} />
          </Stack></DialogContent>
        <DialogActions><Button onClick={() => setReplace(false)}>Cancel</Button>
          <Button variant="contained" onClick={() => { setReplace(false); run(async () => { const nid = await post<string>(`/api/documents/${d.id}/replace-reference`, { revision: edition || null, changeSummary: summary }); setRevId(nid); setTab('files'); }); }}>Add newer copy</Button></DialogActions>
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

function DraftEditor({ rev, onSaved, formTemplate, family }: { rev: Rev; onSaved: () => void; formTemplate?: boolean; family?: FormTheme | null }) {
  const [designer, setDesigner] = useState(() => isV2(rev.fieldSchema));
  const [designing, setDesigning] = useState(false);
  const [def, setDef] = useState<FormDef>(() => upgradeToV2(parseDefinition(rev.fieldSchema)));
  const [fields, setFields] = useState<FieldDef[]>(() => (isV2(rev.fieldSchema) ? [] : parseFields(rev.fieldSchema)));
  const [summary, setSummary] = useState(rev.changeSummary);
  const [eff, setEff] = useState(rev.effectiveDate ?? '');
  const [training, setTraining] = useState(rev.trainingRequired);
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const schema = () => (!formTemplate ? undefined : designer ? JSON.stringify(cleanDefinition(def)) : serializeFields(fields));
  return (
    <Paper sx={{ p: 2, maxWidth: formTemplate && designer ? 'none' : 720 }}>
      <Typography variant="h3" sx={{ mb: 1.5 }}>Draft revision {rev.revision}</Typography>
      {msg && <Alert severity={msg.ok ? 'success' : 'error'} sx={{ mb: 1.5 }}>{msg.text}</Alert>}
      <Stack gap={2}>
        <Stack direction={{ xs: 'column', md: 'row' }} gap={2} alignItems={{ md: 'center' }}>
          <TextField label="Change summary" multiline minRows={2} value={summary} onChange={(e) => setSummary(e.target.value)} sx={{ flex: 1, maxWidth: 680 }} />
          <TextField label="Effective date (blank = on approval)" type="date" value={eff} onChange={(e) => setEff(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} sx={{ width: 280 }} />
          <FormControlLabel control={<Checkbox checked={training} onChange={(e) => setTraining(e.target.checked)} />} label="Training required when released" />
        </Stack>
        {formTemplate && !designer && <>
          <FieldBuilder fields={fields} onChange={setFields} />
          <Box><Button size="small" variant="outlined" onClick={() => { setDef(upgradeToV2(parseDefinition(serializeFields(fields)))); setDesigner(true); setDesigning(true); }}>Use the sectioned designer</Button>
            <Typography variant="caption" color="text.secondary" sx={{ ml: 1 }}>Adds sections, tables, signature blocks and page setup; the fields above are kept.</Typography></Box>
        </>}
        {formTemplate && designer && <>
          <Box><Button variant="contained" onClick={() => setDesigning(true)}>Open designer</Button>
            <Typography variant="caption" color="text.secondary" sx={{ ml: 1 }}>{def.sections.length} sections, {def.fields.length} fields, {def.tables.length} tables, {def.signatures.length} signature blocks. Save the draft after designing.</Typography></Box>
          {def.fields.length + def.tables.length > 0 && <Paper variant="outlined" sx={{ p: 2 }}><FormView def={withFamily(def, family)} values={{}} onChange={() => {}} readOnly /></Paper>}
          <Dialog open={designing} fullScreen onClose={() => setDesigning(false)} aria-labelledby="designer-title">
            <Stack direction="row" alignItems="center" gap={2} sx={{ px: 2, py: 1, borderBottom: 1, borderColor: 'divider' }}>
              <Typography id="designer-title" variant="h3" sx={{ flex: 1 }}>Form designer: revision {rev.revision}</Typography>
              <Button variant="contained" onClick={() => setDesigning(false)}>Done</Button>
            </Stack>
            <Box sx={{ p: 2, overflow: 'auto' }}><FormDesigner value={def} onChange={setDef} /></Box>
          </Dialog>
        </>}
        <Box><Button variant="outlined" onClick={async () => {
          try { await put(`/api/documents/revisions/${rev.id}`, { changeSummary: summary, effectiveDate: eff || null, trainingRequired: training, rowVersion: rev.rowVersion, fieldSchema: schema() }); setMsg({ ok: true, text: 'Saved.' }); onSaved(); }
          catch (e) { setMsg({ ok: false, text: (e as Error).message }); }
        }}>Save draft</Button></Box>
      </Stack>
    </Paper>
  );
}

interface DocType { id: string; kind: Kind; name: string; description: string | null; isSystem: boolean; isActive: boolean; sortOrder: number; isReference?: boolean }
const kindNames = ['Documents', 'Drawings', 'Form templates'];
/** Governed document type library: shipped types (seeded once, never duplicated) plus your own; rename or retire without losing history. */
export function DocumentTypesPage() {
  const qc = useQueryClient();
  const [kind, setKind] = useState<Kind>(0);
  const q = useQuery({ queryKey: ['document-types-admin', kind], queryFn: () => get<DocType[]>(`/api/document-types?kind=${kind}&includeInactive=true`) });
  const [edit, setEdit] = useState<Partial<DocType> | null>(null);
  const [error, setError] = useState<string | null>(null);
  const save = async () => {
    setError(null);
    try {
      if (edit!.id) await put(`/api/document-types/${edit!.id}`, { name: edit!.name, description: edit!.description || null, isActive: edit!.isActive ?? true, sortOrder: edit!.sortOrder ?? 0, isReference: kind === 0 ? !!edit!.isReference : undefined });
      else await post('/api/document-types', { kind, name: edit!.name, description: edit!.description || null, isReference: kind === 0 && !!edit!.isReference });
      setEdit(null); qc.invalidateQueries({ queryKey: ['document-types-admin'] }); qc.invalidateQueries({ queryKey: ['document-types'] });
    } catch (e) { setError((e as Error).message); }
  };
  return (
    <ModuleWorkspace module={modules.find((m) => m.key === 'documents')!} crumbs={[{ label: 'Document types' }]} title="Document type library"
      tabs={kindNames.map((l, i) => ({ value: String(i), label: l }))} tab={String(kind)} onTab={(t) => setKind(Number(t) as Kind)}
      actions={<Button variant="contained" onClick={() => { setError(null); setEdit({ name: '', description: '' }); }}>Add type</Button>}>
      <Typography color="text.secondary" sx={{ mb: 2 }}>Renaming a type renames it on every document filed under it. Retired types stay on existing documents but cannot be chosen for new ones.</Typography>
      <DataTable data={q.data ?? []} onRowClick={(r) => { setError(null); setEdit({ ...r }); }} columns={[
        { header: 'Type', accessorKey: 'name' },
        { header: 'Source', accessorKey: 'isSystem', cell: (c) => (c.getValue() ? 'Shipped' : 'Your organization') },
        { header: 'State', accessorKey: 'isActive', cell: (c) => (c.getValue() ? 'In use' : 'Retired') },
        ...(kind === 0 ? [{ header: 'Control', accessorKey: 'isReference', cell: (c: { getValue: () => unknown }) => (c.getValue() ? 'Reference copy' : 'Approval and revisions') }] : []),
        { header: 'Description', accessorKey: 'description' },
      ]} />
      <Dialog open={!!edit} onClose={() => setEdit(null)} fullWidth maxWidth="sm">
        <DialogTitle>{edit?.id ? 'Edit document type' : `New ${kindNames[kind].toLowerCase()} type`}</DialogTitle>
        <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
          {error && <Alert severity="error">{error}</Alert>}
          <TextField label="Name" value={edit?.name ?? ''} onChange={(e) => setEdit({ ...edit, name: e.target.value })} />
          <TextField label="Description" value={edit?.description ?? ''} onChange={(e) => setEdit({ ...edit, description: e.target.value })} />
          {edit?.id && <FormControlLabel control={<Checkbox checked={edit.isActive ?? true} onChange={(e) => setEdit({ ...edit, isActive: e.target.checked })} />} label="In use for new documents" />}
          {kind === 0 && <FormControlLabel control={<Checkbox checked={!!edit?.isReference} onChange={(e) => setEdit({ ...edit, isReference: e.target.checked })} />}
            label="Reference copy: stored and controlled with a history, but no approval or revision workflow (standards, customer and external documents)" />}
        </DialogContent>
        <DialogActions><Button onClick={() => setEdit(null)}>Cancel</Button><Button variant="contained" disabled={!edit?.name?.trim()} onClick={save}>Save</Button></DialogActions>
      </Dialog>
    </ModuleWorkspace>
  );
}

/** Excel round trip: a protected workbook of the effective revision for offline completion, and its reimport as a draft record. */
function OfflineWorkbook({ templateId, siteId }: { templateId: string; siteId: string }) {
  const nav = useNavigate();
  const { can } = useAuth();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const submit = async (f: File | null | undefined) => {
    if (!f) return;
    setError(null); setBusy(true);
    try {
      const fd = new FormData(); fd.append('file', f); fd.append('siteId', siteId);
      const r = await post<{ recordId: string }>(`/api/forms/templates/${templateId}/workbook`, fd);
      nav(`/forms/records/${r.recordId}`);
    } catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  };
  return (
    <>
      <Button variant="outlined" size="small" href={`/api/forms/templates/${templateId}/workbook`}>Download offline workbook</Button>
      {can('forms.edit') && <Button variant="outlined" size="small" component="label" disabled={busy}>Submit completed workbook
        <input hidden type="file" accept=".xlsx" data-testid="offline-workbook" onChange={(e) => submit(e.target.files?.[0])} /></Button>}
      {error && <Alert severity="error" sx={{ py: 0 }}>{error}</Alert>}
    </>
  );
}

/** Marks a reference copy (standard, customer or external document): controlled and audited, but no approval or revision workflow. */
function ReferenceChip() {
  return <Chip size="small" variant="outlined" color="info" label="Reference copy" data-testid="reference-chip" sx={{ fontWeight: 600 }} />;
}
