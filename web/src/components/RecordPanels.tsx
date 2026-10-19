import { setupOptions } from './SetupLink';
import { useRef, useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { Alert, Autocomplete, Box, Button, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, IconButton, Link, MenuItem, Paper, Stack, TextField, Tooltip, Typography } from '@mui/material';
import UploadFileOutlined from '@mui/icons-material/UploadFileOutlined';
import LockOutlined from '@mui/icons-material/LockOutlined';
import DeleteOutline from '@mui/icons-material/DeleteOutline';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm, Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useAuth } from '../auth';
import { z } from 'zod';
import type { ColumnDef } from '@tanstack/react-table';
import { api, del, get, post, put } from '../api';
import { DataTable } from './DataTable';
import { StatusChip, actionStatus, approvalStatus, fmtDate, fmtDateTime } from './Status';
import { mono } from '../theme';

const base = (type: string, id: string) => `/api/records/${type}/${id}`;

interface FileRow { id: string; fileName: string; sizeBytes: number; sha256: string; version: number; isLocked: boolean; isArchived: boolean; createdAt: string; contentType: string; retainUntil?: string | null; classification?: number; kind?: string }

export function FilesPanel({ type, id, canEdit, onChanged }: { type: string; id: string; canEdit: boolean; onChanged?: () => void }) {
  const qc = useQueryClient();
  const [history, setHistory] = useState(false);
  const [classification, setClassification] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const input = useRef<HTMLInputElement>(null);
  const replaceId = useRef<string | null>(null);
  const files = useQuery({ queryKey: ['files', type, id, history], queryFn: () => get<FileRow[]>(`${base(type, id)}/files?history=${history}`) });
  const upload = async (f: File) => {
    setError(null);
    const form = new FormData();
    form.append('file', f);
    form.append('classification', String(classification));
    if (replaceId.current) form.append('replacesId', replaceId.current);
    try { await api('POST', `${base(type, id)}/files`, form); qc.invalidateQueries({ queryKey: ['files', type, id] }); onChanged?.(); }
    catch (e) { setError((e as Error).message); }
    replaceId.current = null;
  };
  const cols: ColumnDef<FileRow, any>[] = [
    { header: 'File', accessorKey: 'fileName', cell: (c) => (
      <Stack direction="row" gap={1} alignItems="center">
        {c.row.original.isLocked && <Tooltip title="Locked: part of an approved revision"><LockOutlined fontSize="inherit" color="action" /></Tooltip>}
        <Link href={`/api/files/${c.row.original.id}?inline=true`} target="_blank" rel="noopener">{c.getValue()}</Link>
      </Stack>) },
    { header: 'Version', accessorKey: 'version' },
    { header: 'Size', accessorKey: 'sizeBytes', cell: (c) => `${(c.getValue() / 1024).toFixed(1)} KB` },
    { header: 'SHA-256', accessorKey: 'sha256', cell: (c) => <Tooltip title={c.getValue()}><span style={{ fontFamily: mono, fontSize: 12 }}>{String(c.getValue()).slice(0, 12)}…</span></Tooltip> },
    { header: 'Uploaded', accessorKey: 'createdAt', cell: (c) => fmtDateTime(c.getValue()) },
    { header: 'Retained until', accessorKey: 'retainUntil', cell: (c) => c.getValue() ?? '' },
    { header: '', id: 'act', cell: (c) => canEdit && !c.row.original.isLocked && !c.row.original.isArchived ? (
      <Stack direction="row">
        <Button size="small" onClick={() => { replaceId.current = c.row.original.id; input.current?.click(); }}>New version</Button>
        <IconButton size="small" aria-label="Remove file" onClick={async () => { await post(`/api/files/${c.row.original.id}/archive`); qc.invalidateQueries({ queryKey: ['files', type, id] }); onChanged?.(); }}><DeleteOutline fontSize="small" /></IconButton>
      </Stack>) : null },
  ];
  return (
    <Stack gap={1.5}>
      <Stack direction="row" gap={1} alignItems="center">
        {canEdit && <Button variant="outlined" startIcon={<UploadFileOutlined />} onClick={() => { replaceId.current = null; input.current?.click(); }}>Upload file</Button>}
        {canEdit && <TextField select size="small" label="Classification" value={classification} onChange={(e) => setClassification(Number(e.target.value))} sx={{ minWidth: 150 }}>{['Internal', 'Confidential', 'Restricted', 'Public'].map((l, i) => <MenuItem key={l} value={i}>{l}</MenuItem>)}</TextField>}
        <FormControlLabel control={<Checkbox size="small" checked={history} onChange={(e) => setHistory(e.target.checked)} />} label="Show earlier versions" />
        <input ref={input} type="file" hidden onChange={(e) => { const f = e.target.files?.[0]; if (f) upload(f); e.target.value = ''; }} />
      </Stack>
      {error && <Alert severity="error">{error}</Alert>}
      <DataTable data={files.data ?? []} columns={cols} empty="No files attached." />
    </Stack>
  );
}

interface CommentRow { id: string; authorName: string; body: string; createdAt: string }
export function CommentsPanel({ type, id }: { type: string; id: string }) {
  const qc = useQueryClient();
  const [body, setBody] = useState('');
  const comments = useQuery({ queryKey: ['comments', type, id], queryFn: () => get<CommentRow[]>(`${base(type, id)}/comments`) });
  const add = useMutation({ mutationFn: () => post(`${base(type, id)}/comments`, { body }), onSuccess: () => { setBody(''); qc.invalidateQueries({ queryKey: ['comments', type, id] }); } });
  return (
    <Stack gap={1.5} sx={{ maxWidth: 760 }}>
      {(comments.data ?? []).map((c) => (
        <Paper key={c.id} sx={{ p: 1.5 }}>
          <Typography variant="caption" color="text.secondary">{c.authorName} · {fmtDateTime(c.createdAt)}</Typography>
          <Typography sx={{ whiteSpace: 'pre-wrap' }}>{c.body}</Typography>
        </Paper>
      ))}
      {comments.data?.length === 0 && <Typography color="text.secondary">No comments yet.</Typography>}
      <TextField multiline minRows={2} label="Add a comment" value={body} onChange={(e) => setBody(e.target.value)} />
      <Box><Button variant="contained" disabled={!body.trim() || add.isPending} onClick={() => add.mutate()}>Post comment</Button></Box>
    </Stack>
  );
}

interface LinkRow { id: string; direction: string; linkType: string; type: string; recordId: string; number: string; title: string; status: string | null; url: string }
interface Hit { recordType: string; recordId: string; number: string; title: string }
export function LinksPanel({ type, id, canEdit }: { type: string; id: string; canEdit: boolean }) {
  const qc = useQueryClient();
  const [q, setQ] = useState('');
  const [target, setTarget] = useState<Hit | null>(null);
  const [linkType, setLinkType] = useState('Related');
  const [error, setError] = useState<string | null>(null);
  const links = useQuery({ queryKey: ['links', type, id], queryFn: () => get<LinkRow[]>(`${base(type, id)}/links`) });
  const hits = useQuery({ queryKey: ['linksearch', q], queryFn: () => get<Hit[]>(`/api/search?q=${encodeURIComponent(q)}&limit=10`), enabled: q.length >= 2 });
  const cols: ColumnDef<LinkRow, any>[] = [
    { header: 'Record', accessorKey: 'number', cell: (c) => <Link component={RouterLink} to={c.row.original.url}>{c.getValue()}</Link> },
    { header: 'Title', accessorKey: 'title' },
    { header: 'Type', accessorKey: 'type' },
    { header: 'Relationship', accessorKey: 'linkType', cell: (c) => `${c.row.original.direction === 'To' ? '' : 'Linked from: '}${c.getValue()}` },
    { header: 'Status', accessorKey: 'status', cell: (c) => c.getValue() ? <StatusChip status={c.getValue()} /> : null },
    { header: '', id: 'x', cell: (c) => canEdit ? <IconButton size="small" aria-label="Remove link" onClick={async () => { await del(`${base(type, id)}/links/${c.row.original.id}`); qc.invalidateQueries({ queryKey: ['links', type, id] }); }}><DeleteOutline fontSize="small" /></IconButton> : null },
  ];
  return (
    <Stack gap={1.5}>
      {canEdit && (
        <Stack direction="row" gap={1} alignItems="center">
          <Autocomplete sx={{ width: 420 }} size="small" options={hits.data ?? []} value={target} onChange={(_, v) => setTarget(v)}
            getOptionLabel={(o) => `${o.number}  ${o.title}`} filterOptions={(x) => x} onInputChange={(_, v) => setQ(v)}
            renderInput={(p) => <TextField {...p} label="Find a record to link" />} />
          <TextField size="small" select label="Relationship" value={linkType} onChange={(e) => setLinkType(e.target.value)} sx={{ width: 180 }}>
            {['Related', 'References', 'Caused by', 'Resolves', 'Supersedes'].map((t) => <MenuItem key={t} value={t}>{t}</MenuItem>)}
          </TextField>
          <Button variant="outlined" disabled={!target} onClick={async () => {
            setError(null);
            try { await post(`${base(type, id)}/links`, { toType: target!.recordType, toId: target!.recordId, linkType }); setTarget(null); qc.invalidateQueries({ queryKey: ['links', type, id] }); }
            catch (e) { setError((e as Error).message); }
          }}>Link</Button>
        </Stack>
      )}
      {error && <Alert severity="error">{error}</Alert>}
      <DataTable data={links.data ?? []} columns={cols} empty="No linked records." />
    </Stack>
  );
}

interface ActionRow { id: string; number: string; title: string; actionType: string; assigneeName: string; dueDate: string; status: number; isOverdue: boolean; requiresVerification: boolean; completionNotes: string | null; assigneeId: string; completedById: string | null }
const actionSchema = z.object({
  title: z.string().trim().min(1, 'Required').max(300),
  description: z.string().max(4000).optional(),
  actionType: z.string().min(1),
  assigneeId: z.string().uuid('Choose an assignee'),
  dueDate: z.string().regex(/^\d{4}-\d{2}-\d{2}$/, 'Choose a date'),
  requiresVerification: z.boolean(),
});
type ActionForm = z.infer<typeof actionSchema>;

/**
 * canEdit: may add actions now (record still open). canVerifyAs: may verify completed actions (edit rights on the source,
 * even after new actions are no longer allowed). Defaults to canEdit.
 */
export function ActionsPanel({ type, id, canEdit, canVerifyAs, onChanged }: { type: string; id: string; canEdit: boolean; canVerifyAs?: boolean; onChanged?: () => void }) {
  const qc = useQueryClient();
  const { me } = useAuth();
  const canEditSource = canVerifyAs ?? canEdit;
  const [step, setStep] = useState<{ row: ActionRow; kind: 'complete' | 'verify' } | null>(null);
  const [stepNotes, setStepNotes] = useState('');
  const [stepEffective, setStepEffective] = useState(true);
  const [stepError, setStepError] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const actions = useQuery({ queryKey: ['actions', type, id], queryFn: () => get<ActionRow[]>(`${base(type, id)}/actions`) });
  const users = useQuery({ queryKey: ['userlookup'], queryFn: () => get<{ id: string; displayName: string }[]>('/api/users/lookup'), enabled: open });
  const { control, register, handleSubmit, reset, formState: { errors } } = useForm<ActionForm>({
    resolver: zodResolver(actionSchema), defaultValues: { title: '', description: '', actionType: 'Corrective', assigneeId: '', dueDate: '', requiresVerification: true },
  });
  const submit = handleSubmit(async (v) => {
    setError(null);
    try { await post(`${base(type, id)}/actions`, v); setOpen(false); reset(); qc.invalidateQueries({ queryKey: ['actions', type, id] }); }
    catch (e) { setError((e as Error).message); }
  });
  const cols: ColumnDef<ActionRow, any>[] = [
    { header: 'Number', accessorKey: 'number', cell: (c) => <span style={{ fontFamily: mono }}>{c.getValue()}</span> },
    { header: 'Action', accessorKey: 'title' },
    { header: 'Type', accessorKey: 'actionType' },
    { header: 'Assignee', accessorKey: 'assigneeName' },
    { header: 'Due', accessorKey: 'dueDate', cell: (c) => <Typography color={c.row.original.isOverdue ? 'error' : undefined} fontSize="inherit">{fmtDate(c.getValue())}</Typography> },
    { header: 'Status', accessorKey: 'status', cell: (c) => <StatusChip status={actionStatus[c.getValue()]} /> },
    { header: '', id: 'do', cell: (c) => {
      const a = c.row.original;
      if (a.status <= 1 && (a.assigneeId === me?.id || canEdit)) return <Button size="small" onClick={() => setStep({ row: a, kind: 'complete' })}>Complete</Button>;
      if (a.status === 2 && a.requiresVerification && canEditSource && a.completedById !== me?.id) return <Button size="small" color="secondary" onClick={() => setStep({ row: a, kind: 'verify' })}>Verify</Button>;
      return null;
    } },
  ];
  const finish = async () => {
    if (!step) return;
    setStepError(null);
    try {
      if (step.kind === 'complete') await post(`/api/actions/${step.row.id}/complete`, { notes: stepNotes });
      else await post(`/api/actions/${step.row.id}/verify`, { effective: stepEffective, notes: stepNotes });
      setStep(null); setStepNotes(''); qc.invalidateQueries({ queryKey: ['actions', type, id] }); qc.invalidateQueries({ queryKey: ['work'] }); onChanged?.();
    } catch (e) { setStepError((e as Error).message); }
  };
  return (
    <Stack gap={1.5}>
      {canEdit && <Box><Button variant="outlined" onClick={() => setOpen(true)}>Add action</Button></Box>}
      <Dialog open={!!step} onClose={() => setStep(null)} maxWidth="sm" fullWidth>
        <DialogTitle>{step?.kind === 'complete' ? `Complete ${step?.row.number}` : `Verify ${step?.row.number}`}</DialogTitle>
        <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
          {stepError && <Alert severity="error">{stepError}</Alert>}
          {step?.kind === 'verify' && <><Typography variant="body2">Completion notes: {step.row.completionNotes}</Typography>
            <TextField select label="Effective?" value={stepEffective ? 'yes' : 'no'} onChange={(e) => setStepEffective(e.target.value === 'yes')}><MenuItem value="yes">Yes, verified</MenuItem><MenuItem value="no">No, reopen</MenuItem></TextField></>}
          <TextField label={step?.kind === 'complete' ? 'Completion notes' : 'Verification notes'} multiline minRows={3} value={stepNotes} onChange={(e) => setStepNotes(e.target.value)} />
        </DialogContent>
        <DialogActions><Button onClick={() => setStep(null)}>Cancel</Button><Button variant="contained" disabled={!stepNotes.trim()} onClick={finish}>{step?.kind === 'complete' ? 'Mark complete' : 'Record verification'}</Button></DialogActions>
      </Dialog>
      <DataTable data={actions.data ?? []} columns={cols} empty="No actions." />
      <Dialog open={open} onClose={() => setOpen(false)} maxWidth="sm" fullWidth>
        <DialogTitle>New action</DialogTitle>
        <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
          {error && <Alert severity="error">{error}</Alert>}
          <TextField label="Title" {...register('title')} error={!!errors.title} helperText={errors.title?.message} />
          <TextField label="Description" multiline minRows={2} {...register('description')} />
          <Controller name="actionType" control={control} render={({ field }) => (
            <TextField select label="Type" {...field}>{['Containment', 'Corrective', 'Preventive', 'Improvement', 'Follow-up', 'General'].map((t) => <MenuItem key={t} value={t}>{t}</MenuItem>)}</TextField>)} />
          <Controller name="assigneeId" control={control} render={({ field }) => (
            <TextField select label="Assignee" {...field} error={!!errors.assigneeId} helperText={errors.assigneeId?.message}>
              {(users.data ?? []).map((u) => <MenuItem key={u.id} value={u.id}>{u.displayName}</MenuItem>)}
              {setupOptions('user', users.data)}
            </TextField>)} />
          <TextField type="date" label="Due date" slotProps={{ inputLabel: { shrink: true } }} {...register('dueDate')} error={!!errors.dueDate} helperText={errors.dueDate?.message} />
          <Controller name="requiresVerification" control={control} render={({ field }) => (
            <FormControlLabel control={<Checkbox checked={field.value} onChange={(e) => field.onChange(e.target.checked)} />} label="Requires effectiveness verification" />)} />
        </DialogContent>
        <DialogActions><Button onClick={() => setOpen(false)}>Cancel</Button><Button variant="contained" onClick={submit}>Create action</Button></DialogActions>
      </Dialog>
    </Stack>
  );
}

interface Decision { stepOrder: number; stepName: string; signerName: string; approved: boolean; meaning: string; comment: string | null; signedAt: string }
interface ApprovalRow { id: string; status: number; currentStep: number; submittedAt: string; decisions: Decision[] }
export function ApprovalsPanel({ type, id }: { type: string; id: string }) {
  const approvals = useQuery({ queryKey: ['approvals', type, id], queryFn: () => get<ApprovalRow[]>(`${base(type, id)}/approvals`) });
  if (!approvals.data?.length) return <Typography color="text.secondary">Not submitted for approval yet.</Typography>;
  return (
    <Stack gap={1.5}>
      {approvals.data.map((a) => (
        <Paper key={a.id} sx={{ p: 2 }}>
          <Stack direction="row" gap={1} alignItems="center" sx={{ mb: 1 }}>
            <StatusChip status={approvalStatus[a.status]} /><Typography variant="body2" color="text.secondary">Submitted {fmtDateTime(a.submittedAt)}</Typography>
          </Stack>
          {a.decisions.map((d, i) => (
            <Typography key={i} variant="body2">
              <b>{d.signerName}</b> signed step {d.stepOrder} ({d.stepName}) meaning <b>{d.meaning}</b> on {fmtDateTime(d.signedAt)}{d.comment ? ` — "${d.comment}"` : ''}
            </Typography>
          ))}
          {a.decisions.length === 0 && <Typography variant="body2" color="text.secondary">Waiting for step {a.currentStep}.</Typography>}
        </Paper>
      ))}
    </Stack>
  );
}

interface HistoryRow { occurredAt: string; userName: string; action: string; description: string; changes: string | null; reason: string | null }
export function HistoryPanel({ type, id }: { type: string; id: string }) {
  const h = useQuery({ queryKey: ['history', type, id], queryFn: () => get<HistoryRow[]>(`${base(type, id)}/history`) });
  const cols: ColumnDef<HistoryRow, any>[] = [
    { header: 'When', accessorKey: 'occurredAt', cell: (c) => fmtDateTime(c.getValue()) },
    { header: 'Who', accessorKey: 'userName' },
    { header: 'What', accessorKey: 'action' },
    { header: 'Description', accessorKey: 'description' },
  ];
  return <DataTable data={h.data ?? []} columns={cols} empty="No history." />;
}

interface CfDef { key: string; label: string; fieldType: string; isRequired: boolean; options: string[] }
export function CustomFieldsPanel({ type, id, canEdit }: { type: string; id: string; canEdit: boolean }) {
  const qc = useQueryClient();
  const cf = useQuery({ queryKey: ['cf', type, id], queryFn: () => get<{ fields: CfDef[]; values: Record<string, string> }>(`${base(type, id)}/custom-fields`) });
  const [vals, setVals] = useState<Record<string, string> | null>(null);
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);
  if (!cf.data?.fields.length) return null;
  const v = vals ?? cf.data.values;
  return (
    <Paper sx={{ p: 2 }}>
      <Typography variant="h3" sx={{ mb: 1.5 }}>Additional fields</Typography>
      {msg && <Alert severity={msg.ok ? 'success' : 'error'} sx={{ mb: 1 }}>{msg.text}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: 2 }}>
        {cf.data.fields.map((f) => (
          <TextField key={f.key} label={f.label} required={f.isRequired} disabled={!canEdit} size="small"
            select={f.fieldType === 'Choice' || f.fieldType === 'Boolean'} type={f.fieldType === 'Date' ? 'date' : f.fieldType === 'Number' ? 'number' : 'text'}
            slotProps={{ inputLabel: { shrink: true } }} value={v[f.key] ?? ''} onChange={(e) => setVals({ ...v, [f.key]: e.target.value })}>
            {f.fieldType === 'Choice' && f.options.map((o) => <MenuItem key={o} value={o}>{o}</MenuItem>)}
            {f.fieldType === 'Boolean' && ['true', 'false'].map((o) => <MenuItem key={o} value={o}>{o === 'true' ? 'Yes' : 'No'}</MenuItem>)}
          </TextField>
        ))}
      </Box>
      {canEdit && <Button sx={{ mt: 2 }} variant="outlined" onClick={async () => {
        try { await put(`${base(type, id)}/custom-fields`, v); setMsg({ ok: true, text: 'Saved.' }); qc.invalidateQueries({ queryKey: ['cf', type, id] }); }
        catch (e) { setMsg({ ok: false, text: (e as Error).message }); }
      }}>Save fields</Button>}
    </Paper>
  );
}

/** Free-form tags on any record; anyone who can view sees them, editors add and remove. */
export function TagsPanel({ type, id, canEdit }: { type: string; id: string; canEdit: boolean }) {
  const qc = useQueryClient();
  const [name, setName] = useState('');
  const [error, setError] = useState<string | null>(null);
  const tags = useQuery({ queryKey: ['tags', type, id], queryFn: () => get<string[]>(`${base(type, id)}/tags`) });
  const change = async (f: () => Promise<unknown>) => { setError(null); try { await f(); qc.invalidateQueries({ queryKey: ['tags', type, id] }); } catch (e) { setError((e as Error).message); } };
  return (
    <Stack gap={1}>
      <Typography variant="subtitle2">Tags</Typography>
      {error && <Alert severity="error">{error}</Alert>}
      <Stack direction="row" gap={0.75} flexWrap="wrap" alignItems="center">
        {(tags.data ?? []).map((t) => <Chip key={t} size="small" label={t} onDelete={canEdit ? () => change(() => del(`${base(type, id)}/tags/${encodeURIComponent(t)}`)) : undefined} />)}
        {!tags.data?.length && <Typography variant="body2" color="text.secondary">No tags.</Typography>}
        {canEdit && <Box component="form" onSubmit={(e) => { e.preventDefault(); if (name.trim()) change(async () => { await post(`${base(type, id)}/tags`, { name: name.trim() }); setName(''); }); }}>
          <TextField size="small" placeholder="Add tag" value={name} onChange={(e) => setName(e.target.value)} slotProps={{ htmlInput: { maxLength: 50, 'aria-label': 'Add tag' } }} sx={{ width: 160 }} />
        </Box>}
      </Stack>
    </Stack>
  );
}
