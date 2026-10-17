import { useEffect, useState } from 'react';
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, IconButton, Link, MenuItem, Paper, Stack, TextField, Typography } from '@mui/material';
import DeleteOutline from '@mui/icons-material/DeleteOutline';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { z } from 'zod';
import { useForm, useFieldArray, Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { get, post, put } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { localDate, StatusChip, auditStatus, checklistResult, findingStatus, findingType, fmtDate } from '../components/Status';
import { ActionsPanel, AttachmentsAndHistory } from '../components/RecordTabs';
import { Facts, Prose, Section, StepDialog, useRunner, useSites, useUsers } from '../components/Kit';
import { mono } from '../theme';

const mod = () => modules.find((m) => m.key === 'audits')!;
const num = (x: { getValue: () => unknown }) => <span style={{ fontFamily: mono }}>{String(x.getValue())}</span>;
interface AuditRow { id: string; number: string; title: string; auditType: string; status: number; plannedStart: string; plannedEnd: string; siteCode: string; leadName: string; findings: number }

export function AuditList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const status = p.get('status') ?? '';
  const list = useQuery({ queryKey: ['audits', status], queryFn: () => get<{ items: AuditRow[] }>(`/api/audits?pageSize=200${status ? `&status=${status}` : ''}`) });
  const cols: ColumnDef<AuditRow, any>[] = [
    { header: 'Audit', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' }, { header: 'Type', accessorKey: 'auditType' },
    { header: 'Dates', accessorFn: (r) => `${fmtDate(r.plannedStart)} – ${fmtDate(r.plannedEnd)}` },
    { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={auditStatus[x.getValue()]} /> },
    { header: 'Findings', accessorKey: 'findings' }, { header: 'Lead auditor', accessorKey: 'leadName' }, { header: 'Site', accessorKey: 'siteCode' },
  ];
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Audit program' }]} title="Audit program"
      tabs={[{ label: 'All', value: '' }, { label: 'Planned', value: '0' }, { label: 'In progress', value: '1' }, { label: 'Reporting', value: '2' }, { label: 'Closed', value: '3' }]} tab={status} onTab={(v) => setP(v ? { status: v } : {})}
      actions={can('audits.edit') ? <Button variant="contained" component={RouterLink} to="/audits/new">Plan audit</Button> : undefined}>
      <DataTable data={list.data?.items ?? []} columns={cols} onRowClick={(r) => nav(`/audits/${r.id}`)} empty="No audits planned." />
    </ModuleWorkspace>
  );
}

interface FindingRow { id: string; number: string; auditId: string; auditNumber: string; findingType: number; clause: string | null; statement: string; status: number; responseDueOn: string; responsibleName: string }
export function FindingList() {
  const nav = useNavigate();
  const [p, setP] = useSearchParams();
  const all = p.get('all') === '1';
  const list = useQuery({ queryKey: ['findings', all], queryFn: () => get<{ items: FindingRow[] }>(`/api/audits/findings?pageSize=200&openOnly=${!all}`) });
  const cols: ColumnDef<FindingRow, any>[] = [
    { header: 'Finding', accessorKey: 'number', cell: num }, { header: 'Audit', accessorKey: 'auditNumber', cell: num },
    { header: 'Type', accessorKey: 'findingType', cell: (x) => findingType[x.getValue()] }, { header: 'Clause', accessorKey: 'clause' }, { header: 'Statement', accessorKey: 'statement' },
    { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={findingStatus[x.getValue()]} /> }, { header: 'Response due', accessorKey: 'responseDueOn', cell: (x) => fmtDate(x.getValue()) },
    { header: 'Responsible', accessorKey: 'responsibleName' },
  ];
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Findings' }]} title="Audit findings" tabs={[{ label: 'Open', value: '0' }, { label: 'All', value: '1' }]} tab={all ? '1' : '0'} onTab={(v) => setP(v === '1' ? { all: '1' } : {})}>
      <DataTable data={list.data?.items ?? []} columns={cols} onRowClick={(r) => nav(`/audits/findings/${r.id}`)} empty="No findings." />
    </ModuleWorkspace>
  );
}

const auditSchema = z.object({
  title: z.string().trim().min(1, 'Required').max(300), auditType: z.string().min(1), scope: z.string().trim().min(1, 'Required').max(4000), criteria: z.string().max(4000).optional(),
  siteId: z.string().uuid('Choose a site'), leadAuditorId: z.string().uuid('Choose the lead auditor'), plannedStart: z.string().min(10, 'Required'), plannedEnd: z.string().min(10, 'Required'),
  checklist: z.array(z.object({ clause: z.string().max(40).optional(), question: z.string().trim().min(1, 'Required').max(2000) })).min(1, 'Add at least one question'),
}).refine((v) => v.plannedEnd >= v.plannedStart, { message: 'End must be on or after start', path: ['plannedEnd'] });
type AuditForm = z.input<typeof auditSchema>;

export function AuditNew() {
  const nav = useNavigate();
  const sites = useSites();
  const users = useUsers();
  const [error, setError] = useState<string | null>(null);
  const today = localDate();
  const { register, control, handleSubmit, formState: { errors, isSubmitting } } = useForm<AuditForm>({ resolver: zodResolver(auditSchema),
    defaultValues: { title: '', auditType: 'Internal', scope: '', criteria: 'ISO 9001:2015', siteId: '', leadAuditorId: '', plannedStart: today, plannedEnd: today, checklist: [{ clause: '', question: '' }] } });
  const items = useFieldArray({ control, name: 'checklist' });
  const submit = handleSubmit(async (v) => {
    setError(null);
    try { const r = await post<{ id: string }>('/api/audits', { ...v, departmentId: null }); nav(`/audits/${r.id}`); } catch (e) { setError((e as Error).message); }
  });
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Audit program', to: '/audits' }, { label: 'New' }]} title="Plan an audit">
      <Paper component="form" onSubmit={submit} sx={{ p: 3, maxWidth: 860, display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}>
        {error && <Alert severity="error" sx={{ gridColumn: '1 / -1' }}>{error}</Alert>}
        <TextField label="Title" sx={{ gridColumn: '1 / -1' }} {...register('title')} error={!!errors.title} helperText={errors.title?.message} />
        <Controller name="auditType" control={control} render={({ field }) => <TextField select label="Type" {...field}>{['Internal', 'Process', 'Product', 'Supplier', 'Layered process'].map((t) => <MenuItem key={t} value={t}>{t}</MenuItem>)}</TextField>} />
        <TextField label="Criteria" {...register('criteria')} />
        <TextField label="Scope" multiline minRows={2} sx={{ gridColumn: '1 / -1' }} {...register('scope')} error={!!errors.scope} helperText={errors.scope?.message} />
        <Controller name="siteId" control={control} render={({ field }) => <TextField select label="Site" {...field} error={!!errors.siteId} helperText={errors.siteId?.message}>{(sites.data ?? []).filter((s) => !s.isArchived).map((s) => <MenuItem key={s.id} value={s.id}>{s.code} — {s.name}</MenuItem>)}</TextField>} />
        <Controller name="leadAuditorId" control={control} render={({ field }) => <TextField select label="Lead auditor" {...field} error={!!errors.leadAuditorId} helperText={errors.leadAuditorId?.message}>{(users.data ?? []).map((u) => <MenuItem key={u.id} value={u.id}>{u.displayName}</MenuItem>)}</TextField>} />
        <TextField type="date" label="Start" {...register('plannedStart')} slotProps={{ inputLabel: { shrink: true } }} />
        <TextField type="date" label="End" {...register('plannedEnd')} error={!!errors.plannedEnd} helperText={errors.plannedEnd?.message} slotProps={{ inputLabel: { shrink: true } }} />
        <Box sx={{ gridColumn: '1 / -1' }}>
          <Typography variant="subtitle2" sx={{ mb: 1 }}>Checklist</Typography>
          <Stack gap={1}>
            {items.fields.map((f, i) => (
              <Stack key={f.id} direction="row" gap={1}>
                <TextField size="small" label="Clause" sx={{ width: 110 }} {...register(`checklist.${i}.clause`)} />
                <TextField size="small" label={`Question ${i + 1}`} sx={{ flex: 1 }} {...register(`checklist.${i}.question`)} error={!!errors.checklist?.[i]?.question} />
                <IconButton aria-label={`Remove question ${i + 1}`} onClick={() => items.remove(i)} disabled={items.fields.length === 1}><DeleteOutline /></IconButton>
              </Stack>
            ))}
          </Stack>
          <Button size="small" sx={{ mt: 1 }} onClick={() => items.append({ clause: '', question: '' })}>Add question</Button>
        </Box>
        <Stack direction="row" gap={1} sx={{ gridColumn: '1 / -1' }}><Button type="submit" variant="contained" disabled={isSubmitting}>Create audit</Button><Button onClick={() => nav('/audits')}>Cancel</Button></Stack>
      </Paper>
    </ModuleWorkspace>
  );
}

interface ChecklistItem { id: string; sequence: number; clause: string | null; question: string; result: number; evidence: string | null }
interface AuditDetailDto { id: string; number: string; title: string; auditType: string; scope: string; criteria: string | null; leadName: string; plannedStart: string; plannedEnd: string; status: number; summary: string | null; checklist: ChecklistItem[];
  findings: { id: string; number: string; findingType: number; clause: string | null; statement: string; status: number; responseDueOn: string; responsibleName: string }[]; siteCode: string; canEdit: boolean; canClose: boolean; allowed: string[] }

export function AuditDetail() {
  const { id } = useParams();
  const nav = useNavigate();
  const qc = useQueryClient();
  const users = useUsers();
  const [tab, setTab] = useState('checklist');
  const [dlg, setDlg] = useState<string | null>(null);
  const [rows, setRows] = useState<ChecklistItem[]>([]);
  const q = useQuery({ queryKey: ['audit', id], queryFn: () => get<AuditDetailDto>(`/api/audits/${id}`) });
  const { error, run } = useRunner(() => qc.invalidateQueries({ queryKey: ['audit', id] }));
  useEffect(() => { if (q.data) setRows(q.data.checklist); }, [q.data]);
  const d = q.data;
  if (!d) return <ModuleWorkspace module={mod()} crumbs={[{ label: 'Audit program', to: '/audits' }]} title="Loading">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const s = d.status;
  const editing = d.allowed.includes('checklist');
  const allAssessed = rows.every((r) => r.result !== 0);
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Audit program', to: '/audits' }, { label: d.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{d.number}</span><span>{d.title}</span><StatusChip status={auditStatus[s]} /></Stack>}
      tabs={[{ label: 'Checklist', value: 'checklist' }, { label: `Findings (${d.findings.length})`, value: 'findings' }, { label: 'Report', value: 'report' }, { label: 'Files & history', value: 'more' }]} tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1}>
        {d.allowed.includes('start') && <Button variant="contained" onClick={() => run(() => post(`/api/audits/${d.id}/start`))}>Start audit</Button>}
        {editing && <Button variant="outlined" onClick={() => run(() => put(`/api/audits/${d.id}/checklist`, rows.map((r) => ({ itemId: r.id, result: r.result, evidence: r.evidence }))))}>Save checklist</Button>}
        {d.allowed.includes('finding') && <Button variant="outlined" onClick={() => setDlg('finding')}>Raise finding</Button>}
        {editing && allAssessed && <Button variant="contained" onClick={() => setDlg('report')}>Move to reporting</Button>}
        {d.allowed.includes('close') && <Button variant="contained" color="secondary" onClick={() => run(() => post(`/api/audits/${d.id}/close`))}>Close audit</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: '280px 1fr', gap: 3 }}>
        <Facts items={[['Type', d.auditType], ['Lead auditor', d.leadName], ['Dates', `${fmtDate(d.plannedStart)} – ${fmtDate(d.plannedEnd)}`], ['Criteria', d.criteria ?? '—'], ['Scope', d.scope], ['Site', d.siteCode]]} />
        <Box sx={{ minWidth: 0 }}>
          {tab === 'checklist' && <Stack gap={1}>
            {rows.map((r, i) => (
              <Paper key={r.id} sx={{ p: 2, display: 'grid', gridTemplateColumns: '1fr 200px', gap: 1.5 }}>
                <Box><Typography variant="caption" color="text.secondary">{r.sequence}. {r.clause ? `Clause ${r.clause}` : ''}</Typography><Typography variant="body2">{r.question}</Typography></Box>
                <TextField select size="small" label="Result" value={r.result} disabled={!editing} onChange={(e) => setRows(rows.map((x, j) => j === i ? { ...x, result: Number(e.target.value) } : x))}>
                  {checklistResult.map((c, k) => <MenuItem key={k} value={k}>{c}</MenuItem>)}
                </TextField>
                <TextField size="small" label="Evidence" sx={{ gridColumn: '1 / -1' }} value={r.evidence ?? ''} disabled={!editing} onChange={(e) => setRows(rows.map((x, j) => j === i ? { ...x, evidence: e.target.value } : x))} />
              </Paper>
            ))}
          </Stack>}
          {tab === 'findings' && <DataTable data={d.findings} onRowClick={(f) => nav(`/audits/findings/${f.id}`)} empty="No findings raised." columns={[
            { header: 'Finding', accessorKey: 'number', cell: num }, { header: 'Type', accessorKey: 'findingType', cell: (x) => findingType[x.getValue() as number] },
            { header: 'Clause', accessorKey: 'clause' }, { header: 'Statement', accessorKey: 'statement' },
            { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={findingStatus[x.getValue() as number]} /> },
            { header: 'Due', accessorKey: 'responseDueOn', cell: (x) => fmtDate(x.getValue() as string) }, { header: 'Responsible', accessorKey: 'responsibleName' }]} />}
          {tab === 'report' && <Section title="Audit summary">{d.summary ? <Prose label="Summary" text={d.summary} /> : 'The summary is written when the audit moves to reporting.'}</Section>}
          {tab === 'more' && <AttachmentsAndHistory type="Audit" id={d.id} canEdit={d.canEdit && s < 3} />}
        </Box>
      </Box>
      <StepDialog open={dlg === 'finding'} title="Raise a finding" confirm="Raise finding" onClose={() => setDlg(null)}
        inputs={[{ name: 'findingType', label: 'Type', type: 'select', initial: '1', options: findingType.map((f, i) => [i, f]) },
          { name: 'checklistItemId', label: 'Checklist item (optional)', type: 'select', options: [['', 'None'], ...d.checklist.map((c) => [c.id, `${c.sequence}. ${c.question.slice(0, 60)}`] as [string, string])] },
          { name: 'clause', label: 'Clause' }, { name: 'statement', label: 'Statement of nonconformity / observation', type: 'multiline' }, { name: 'evidence', label: 'Objective evidence', type: 'multiline' },
          { name: 'responsibleId', label: 'Responsible', type: 'select', options: (users.data ?? []).map((u) => [u.id, u.displayName]) },
          { name: 'responseDueOn', label: 'Response due', type: 'date', initial: localDate(30) }]}
        onConfirm={(v) => run(() => post(`/api/audits/${d.id}/findings`, { ...v, findingType: Number(v.findingType), checklistItemId: v.checklistItemId || null, clause: v.clause || null, evidence: v.evidence || null }))} />
      <StepDialog open={dlg === 'report'} title="Move to reporting" intro="Every checklist item must be assessed. Findings can still be raised during reporting." confirm="Move to reporting" onClose={() => setDlg(null)}
        inputs={[{ name: 'text', label: 'Audit summary', type: 'multiline' }]}
        onConfirm={async (v) => { await put(`/api/audits/${d.id}/checklist`, rows.map((r) => ({ itemId: r.id, result: r.result, evidence: r.evidence }))); await run(() => post(`/api/audits/${d.id}/reporting`, v)); }} />
    </ModuleWorkspace>
  );
}

interface FindingDto { id: string; number: string; auditId: string; auditNumber: string; findingType: number; clause: string | null; statement: string; evidence: string | null; status: number; responseDueOn: string; verificationNotes: string | null; responsibleName: string; canEdit: boolean; allowed: string[] }
export function FindingDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const [tab, setTab] = useState('actions');
  const [dlg, setDlg] = useState(false);
  const q = useQuery({ queryKey: ['finding', id], queryFn: () => get<FindingDto>(`/api/audits/findings/${id}`) });
  const { error, run } = useRunner(() => qc.invalidateQueries({ queryKey: ['finding', id] }));
  const f = q.data;
  if (!f) return <ModuleWorkspace module={mod()} crumbs={[{ label: 'Findings', to: '/audits/findings' }]} title="Loading">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Findings', to: '/audits/findings' }, { label: f.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{f.number}</span><span>{findingType[f.findingType]}</span><StatusChip status={findingStatus[f.status]} /></Stack>}
      tabs={[{ label: 'Corrective actions', value: 'actions' }, { label: 'Files & history', value: 'more' }]} tab={tab} onTab={setTab}
      actions={f.allowed.includes('verify') ? <Button variant="contained" onClick={() => setDlg(true)}>Verify finding</Button> : undefined}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: '320px 1fr', gap: 3 }}>
        <Facts items={[['Audit', <Link component={RouterLink} to={`/audits/${f.auditId}`} sx={{ fontFamily: mono }}>{f.auditNumber}</Link>], ['Clause', f.clause ?? '—'], ['Statement', f.statement], ['Evidence', f.evidence ?? '—'],
          ['Responsible', f.responsibleName], ['Response due', fmtDate(f.responseDueOn)], ['Verification', f.verificationNotes ?? '—']]} />
        <Box sx={{ minWidth: 0 }}>
          {tab === 'actions' && <ActionsPanel type="AuditFinding" id={f.id} canEdit={f.allowed.includes('actions')} canVerifyAs={f.canEdit} onChanged={() => qc.invalidateQueries({ queryKey: ['finding', id] })} />}
          {tab === 'more' && <AttachmentsAndHistory type="AuditFinding" id={f.id} canEdit={f.canEdit && f.status < 3} />}
        </Box>
      </Box>
      <StepDialog open={dlg} title="Verify finding" intro="All corrective actions must be completed and independently verified. Nonconformities need at least one action." confirm="Verify" onClose={() => setDlg(false)}
        inputs={[{ name: 'text', label: 'Verification notes', type: 'multiline' }]} onConfirm={(v) => run(() => post(`/api/audits/findings/${f.id}/verify`, v))} />
    </ModuleWorkspace>
  );
}
