import { useEffect, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Paper, Stack, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { get, post, put } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, formStatus, fmtDateTime } from '../components/Status';
import { ApprovalsPanel, AttachmentsAndHistory } from '../components/RecordTabs';
import { Facts, useRunner, useSites } from '../components/Kit';
import { SignDialog } from '../components/SignDialog';
import { useWork } from '../work';
import { type FieldDef } from './FormBuilder';
import { FormView } from '../forms/FormView';
import { FormSignDialog } from '../forms/FormSignDialog';
import { parseDefinition, type FormDef } from '../forms/definition';
import { mono } from '../theme';

const mod = () => modules.find((m) => m.key === 'documents')!;
interface RecordRow { id: string; number: string; title: string; templateNumber: string; templateRevision: string; status: number; createdAt: string; submittedAt: string | null; siteCode: string }

export function FormRecordList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const status = p.get('status') ?? '';
  const list = useQuery({ queryKey: ['formrecords', status], queryFn: () => get<{ items: RecordRow[]; total: number }>(`/api/forms/records?pageSize=200${status ? `&status=${status}` : ''}`) });
  const cols: ColumnDef<RecordRow, any>[] = [
    { header: 'Record', accessorKey: 'number', cell: (x) => <span style={{ fontFamily: mono }}>{x.getValue()}</span> },
    { header: 'Title', accessorKey: 'title' },
    { header: 'Template', accessorFn: (r) => `${r.templateNumber} rev ${r.templateRevision}` },
    { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={formStatus[x.getValue()]} /> },
    { header: 'Site', accessorKey: 'siteCode' },
    { header: 'Started', accessorKey: 'createdAt', cell: (x) => fmtDateTime(x.getValue()) },
    { header: 'Submitted', accessorKey: 'submittedAt', cell: (x) => fmtDateTime(x.getValue()) },
  ];
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Form records' }]} title="Form records"
      tabs={[{ label: 'All', value: '' }, { label: 'Drafts', value: '0' }, { label: 'Submitted', value: '1' }, { label: 'Approved', value: '2' }, { label: 'Returned', value: '3' }]}
      tab={status} onTab={(v) => setP(v ? { status: v } : {})}
      actions={can('forms.edit') ? <Button variant="contained" onClick={() => nav('/forms/records/new')}>Fill in a form</Button> : undefined}>
      <DataTable data={list.data?.items ?? []} columns={cols} onRowClick={(r) => nav(`/forms/records/${r.id}`)} empty="No form records yet." />
    </ModuleWorkspace>
  );
}

export function FormRecordNew() {
  const nav = useNavigate();
  const sites = useSites();
  const templates = useQuery({ queryKey: ['efftemplates'], queryFn: () => get<{ id: string; number: string; title: string; siteId: string; revision: string }[]>('/api/forms/templates/effective') });
  const [tpl, setTpl] = useState('');
  const [site, setSite] = useState('');
  const [title, setTitle] = useState('');
  const { error, run } = useRunner(() => {});
  useEffect(() => { const t = templates.data?.find((x) => x.id === tpl); if (t && !site) setSite(t.siteId); }, [tpl, templates.data, site]);
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Form records', to: '/forms/records' }, { label: 'New' }]} title="Fill in a controlled form">
      <Paper sx={{ p: 3, maxWidth: 640 }}>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>Only templates with an effective revision are listed. The record stays bound to the revision in force when it is started.</Typography>
        {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
        <Stack gap={2}>
          <TextField select label="Template" value={tpl} onChange={(e) => setTpl(e.target.value)}>
            {(templates.data ?? []).map((t) => <MenuItem key={t.id} value={t.id}>{t.number} rev {t.revision} — {t.title}</MenuItem>)}
          </TextField>
          <TextField select label="Site" value={site} onChange={(e) => setSite(e.target.value)}>
            {(sites.data ?? []).filter((s) => !s.isArchived).map((s) => <MenuItem key={s.id} value={s.id}>{s.code} — {s.name}</MenuItem>)}
          </TextField>
          <TextField label="Record title (optional)" value={title} onChange={(e) => setTitle(e.target.value)} />
          <Box><Button variant="contained" disabled={!tpl || !site} onClick={() => run(async () => {
            const r = await post<{ id: string }>('/api/forms/records', { templateId: tpl, siteId: site, departmentId: null, title: title || null });
            nav(`/forms/records/${r.id}`);
          })}>Start record</Button></Box>
        </Stack>
      </Paper>
    </ModuleWorkspace>
  );
}

interface FormDetail { id: string; number: string; title: string; templateId: string; templateNumber: string; templateRevision: string; status: number; submittedAt: string | null; rowVersion: number; siteId: string; fields: FieldDef[]; definition?: FormDef; values: Record<string, string | null>; canEdit: boolean;
  signatures?: { id: string; key: string; label: string; role: string; meaning: string; signerName: string; signedAt: string; verification: string; comment: string | null; valid: boolean }[] }

/** Signature roles signed while the record is completed, before submission (matches the server). */
const preSubmit = ['prepared', 'performed', 'verified', 'witness'];

export function FormRecordDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const [tab, setTab] = useState('form');
  const [values, setValues] = useState<Record<string, string | null>>({});
  const [saved, setSaved] = useState(false);
  const [signing, setSigning] = useState(false);
  const [signBlock, setSignBlock] = useState<string | null>(null);
  const [filling, setFilling] = useState(false);
  const [filled, setFilled] = useState<number | null>(null);
  const rec = useQuery({ queryKey: ['formrecord', id], queryFn: () => get<FormDetail>(`/api/forms/records/${id}`) });
  const work = useWork();
  const refresh = () => { qc.invalidateQueries({ queryKey: ['formrecord', id] }); qc.invalidateQueries({ queryKey: ['work'] }); };
  const { error, run } = useRunner(refresh);
  useEffect(() => { if (rec.data) setValues(rec.data.values); }, [rec.data]);
  const d = rec.data;
  if (!d) return <ModuleWorkspace module={mod()} crumbs={[{ label: 'Form records', to: '/forms/records' }]} title="Loading">{rec.error ? <Alert severity="error">{(rec.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const approval = work.data?.approvals.find((a) => a.recordId === d.id);
  const def = parseDefinition(d.definition ?? d.fields);
  const block = signBlock ? def.signatures.find((b) => b.key === signBlock) : undefined;
  const dirty = JSON.stringify(values) !== JSON.stringify(d.values);
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Form records', to: '/forms/records' }, { label: d.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{d.number}</span><span>{d.title}</span><StatusChip status={formStatus[d.status]} /></Stack>}
      tabs={[{ label: 'Form', value: 'form' }, { label: 'Approvals', value: 'approvals' }, { label: 'Files & history', value: 'more' }]} tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1}>
        <Button variant="outlined" href={`/api/forms/records/${d.id}/pdf?style=standardized`} target="_blank">Print</Button>
        {def.layout && <Button variant="outlined" href={`/api/forms/records/${d.id}/pdf?style=original`} target="_blank">Print original look</Button>}
        {approval && <Button variant="contained" color="secondary" onClick={() => setSigning(true)}>Review and sign</Button>}
        {d.canEdit && <Button variant="outlined" onClick={() => setFilling(true)}>Fill from record</Button>}
        {d.canEdit && <Button variant="outlined" onClick={() => run(async () => { const r = await put<{ values: Record<string, string | null> }>(`/api/forms/records/${d.id}`, { values, rowVersion: d.rowVersion }); setValues(r.values); setSaved(true); })}>Save draft</Button>}
        {d.canEdit && <Button variant="contained" onClick={() => run(async () => { await put(`/api/forms/records/${d.id}`, { values, rowVersion: d.rowVersion }); await post(`/api/forms/records/${d.id}/submit`); })}>Submit</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {saved && !error && <Alert severity="success" sx={{ mb: 2 }} onClose={() => setSaved(false)}>Draft saved. Calculated fields were updated.</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: '260px 1fr', gap: 3 }}>
        <Facts items={[['Template', `${d.templateNumber} revision ${d.templateRevision}`], ['Status', formStatus[d.status]], ['Submitted', fmtDateTime(d.submittedAt)]]} />
        <Box sx={{ minWidth: 0 }}>
          {tab === 'form' && <Paper sx={{ p: 3 }}><FormView def={def} values={values} onChange={setValues} readOnly={!d.canEdit} recordType="FormRecord" recordId={d.id} siteId={d.siteId}
            signatures={d.signatures ?? []} onSign={setSignBlock} canSign={(k) => { const b = def.signatures.find((x) => x.key === k); return !!b && (preSubmit.includes(b.role) ? d.canEdit : d.status === 1); }} /></Paper>}
          {tab === 'approvals' && <ApprovalsPanel type="FormRecord" id={d.id} />}
          {tab === 'more' && <AttachmentsAndHistory type="FormRecord" id={d.id} canEdit={d.canEdit} />}
        </Box>
      </Box>
      {block && <FormSignDialog block={block} recordNumber={d.number} onClose={() => setSignBlock(null)} onSign={async (password, mfaCode, comment) => {
        if (dirty && d.canEdit) { await put(`/api/forms/records/${d.id}`, { values, rowVersion: d.rowVersion }); await qc.refetchQueries({ queryKey: ['formrecord', id] }); }
        await post(`/api/forms/records/${d.id}/signatures/${block.key}`, { password, mfaCode, comment });
        refresh();
      }} />}
      {filling && <FillFromDialog recordId={d.id} onClose={() => setFilling(false)} onFilled={(n) => { setFilling(false); refresh(); setFilled(n); }} dirty={dirty} />}
      {filled !== null && <Alert severity={filled > 0 ? 'success' : 'info'} sx={{ mt: 2 }} onClose={() => setFilled(null)}>{filled > 0 ? `Filled ${filled} ${filled === 1 ? 'entry' : 'entries'} from the chosen record.` : 'Nothing to fill: the chosen record has no matching entries, or they are already filled in.'}</Alert>}
      {signing && approval && <SignDialog approvalId={approval.id} recordNumber={approval.recordNumber} stepName={approval.stepName} onClose={() => { setSigning(false); refresh(); }} />}
    </ModuleWorkspace>
  );
}

/** Picks another form record (such as a PPAP common data page or a characteristics list) and copies its shared entries into this draft. */
function FillFromDialog({ recordId, onClose, onFilled, dirty }: { recordId: string; onClose: () => void; onFilled: (n: number) => void; dirty: boolean }) {
  const [q, setQ] = useState('');
  const [source, setSource] = useState('');
  const [overwrite, setOverwrite] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const list = useQuery({ queryKey: ['formrecords-fill', q], queryFn: () => get<{ items: RecordRow[] }>(`/api/forms/records?pageSize=50${q ? `&q=${encodeURIComponent(q)}` : ''}`) });
  const options = (list.data?.items ?? []).filter((r) => r.id !== recordId);
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="fill-title">
      <DialogTitle id="fill-title">Fill from another record</DialogTitle>
      <DialogContent>
        <Stack gap={2} sx={{ mt: 1 }}>
          <Typography variant="body2" color="text.secondary">Entries with the same name are copied into this draft, such as part, customer and supplier details from a PPAP common data page, or characteristic rows from a characteristics list. Each record keeps its own number, title, status and dates.</Typography>
          {dirty && <Alert severity="warning">Save your changes first; filling reloads the draft.</Alert>}
          <TextField size="small" label="Search records" value={q} onChange={(e) => setQ(e.target.value)} />
          <TextField size="small" select label="Copy from" value={source} onChange={(e) => setSource(e.target.value)}>
            {options.map((r) => <MenuItem key={r.id} value={r.id}>{r.number} · {r.title} ({r.templateNumber})</MenuItem>)}
          </TextField>
          <FormControlLabel control={<Checkbox checked={overwrite} onChange={(e) => setOverwrite(e.target.checked)} />} label="Replace entries that are already filled in" />
          {err && <Alert severity="error">{err}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" disabled={!source || dirty} onClick={async () => {
          try { const r = await post<{ filled: number }>(`/api/forms/records/${recordId}/prefill`, { sourceType: 'FormRecord', sourceId: source, overwrite }); onFilled(r.filled); }
          catch (e) { setErr((e as Error).message); }
        }}>Fill</Button>
      </DialogActions>
    </Dialog>
  );
}
