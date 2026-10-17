import { useEffect, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, MenuItem, Paper, Stack, TextField, Typography } from '@mui/material';
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
import { useWork } from './Home';
import { FormRenderer, type FieldDef } from './FormBuilder';
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

interface FormDetail { id: string; number: string; title: string; templateId: string; templateNumber: string; templateRevision: string; status: number; submittedAt: string | null; rowVersion: number; fields: FieldDef[]; values: Record<string, string | null>; canEdit: boolean }

export function FormRecordDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const [tab, setTab] = useState('form');
  const [values, setValues] = useState<Record<string, string | null>>({});
  const [saved, setSaved] = useState(false);
  const [signing, setSigning] = useState(false);
  const rec = useQuery({ queryKey: ['formrecord', id], queryFn: () => get<FormDetail>(`/api/forms/records/${id}`) });
  const work = useWork();
  const refresh = () => { qc.invalidateQueries({ queryKey: ['formrecord', id] }); qc.invalidateQueries({ queryKey: ['work'] }); };
  const { error, run } = useRunner(refresh);
  useEffect(() => { if (rec.data) setValues(rec.data.values); }, [rec.data]);
  const d = rec.data;
  if (!d) return <ModuleWorkspace module={mod()} crumbs={[{ label: 'Form records', to: '/forms/records' }]} title="Loading">{rec.error ? <Alert severity="error">{(rec.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const approval = work.data?.approvals.find((a) => a.recordId === d.id);
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Form records', to: '/forms/records' }, { label: d.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{d.number}</span><span>{d.title}</span><StatusChip status={formStatus[d.status]} /></Stack>}
      tabs={[{ label: 'Form', value: 'form' }, { label: 'Approvals', value: 'approvals' }, { label: 'Files & history', value: 'more' }]} tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1}>
        {approval && <Button variant="contained" color="secondary" onClick={() => setSigning(true)}>Review and sign</Button>}
        {d.canEdit && <Button variant="outlined" onClick={() => run(async () => { const r = await put<{ values: Record<string, string | null> }>(`/api/forms/records/${d.id}`, { values, rowVersion: d.rowVersion }); setValues(r.values); setSaved(true); })}>Save draft</Button>}
        {d.canEdit && <Button variant="contained" onClick={() => run(async () => { await put(`/api/forms/records/${d.id}`, { values, rowVersion: d.rowVersion }); await post(`/api/forms/records/${d.id}/submit`); })}>Submit</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {saved && !error && <Alert severity="success" sx={{ mb: 2 }} onClose={() => setSaved(false)}>Draft saved. Calculated fields were updated.</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: '260px 1fr', gap: 3 }}>
        <Facts items={[['Template', `${d.templateNumber} revision ${d.templateRevision}`], ['Status', formStatus[d.status]], ['Submitted', fmtDateTime(d.submittedAt)]]} />
        <Box sx={{ minWidth: 0 }}>
          {tab === 'form' && <Paper sx={{ p: 3 }}><FormRenderer fields={d.fields} values={values} onChange={setValues} readOnly={!d.canEdit} /></Paper>}
          {tab === 'approvals' && <ApprovalsPanel type="FormRecord" id={d.id} />}
          {tab === 'more' && <AttachmentsAndHistory type="FormRecord" id={d.id} canEdit={d.canEdit} />}
        </Box>
      </Box>
      {signing && approval && <SignDialog approvalId={approval.id} recordNumber={approval.recordNumber} stepName={approval.stepName} onClose={() => { setSigning(false); refresh(); }} />}
    </ModuleWorkspace>
  );
}
