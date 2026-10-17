import { useState, type ReactNode } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Chip, LinearProgress, MenuItem, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { z } from 'zod';
import { get, post } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, fmtDate, fmtDateTime } from '../components/Status';
import { Facts, SchemaForm, Section, StepDialog, useRunner, useSites, useUsers } from '../components/Kit';
import { mono } from '../theme';

/* eslint-disable @typescript-eslint/no-explicit-any */
export const mod = (k: string) => modules.find((m) => m.key === k)!;
export const num = (x: { getValue: () => unknown }) => <span style={{ fontFamily: mono }}>{String(x.getValue() ?? '')}</span>;
export const pct = (v: number) => `${Number(v).toFixed(1)}%`;
export const rag = (good: boolean, warn: boolean) => (good ? 'success.main' : warn ? 'warning.main' : 'error.main');
const orgLevels = ['Corporate', 'Division', 'Region', 'Business unit', 'Plant', 'Warehouse', 'Laboratory', 'Department'];
const rolloutStatus = ['Planned', 'Adopted', 'Training', 'Verified', 'Waived'];
const skillKinds = ['Skill', 'Qualification', 'Certification', 'Auditor qualification'];
const curriculumKinds = ['Curriculum', 'Training path', 'Certification program', 'Qualification program'];

export function Loading({ m, crumbs, q }: { m: string; crumbs: { label: string; to?: string }[]; q: { error: unknown } }) {
  return <ModuleWorkspace module={mod(m)} crumbs={crumbs} title="Loading">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : <LinearProgress />}</ModuleWorkspace>;
}

/** Key figure tile used in the war room and dashboards. */
export function Tile({ label, value, color, hint }: { label: string; value: ReactNode; color?: string; hint?: string }) {
  return (
    <Paper sx={{ p: 1.75, borderLeft: 4, borderColor: color ?? 'divider' }}>
      <Typography variant="caption" color="text.secondary">{label}</Typography>
      <Typography sx={{ fontSize: 24, fontWeight: 600, fontFamily: mono, lineHeight: 1.2 }}>{value}</Typography>
      {hint && <Typography variant="caption" color="text.secondary">{hint}</Typography>}
    </Paper>
  );
}

export function SiteSelect({ value, onChange, label = 'Site' }: { value: string; onChange: (v: string) => void; label?: string }) {
  const sites = useSites();
  return <TextField select size="small" label={label} value={value} onChange={(e) => onChange(e.target.value)} sx={{ minWidth: 240 }}>
    {(sites.data ?? []).filter((s) => !s.isArchived).map((s) => <MenuItem key={s.id} value={s.id}>{s.code} — {s.name}</MenuItem>)}</TextField>;
}
export function useSite(): [string, (v: string) => void] {
  const sites = useSites();
  const [p, setP] = useSearchParams();
  const v = p.get('site') ?? sites.data?.find((s) => !s.isArchived)?.id ?? '';
  return [v, (x) => setP({ site: x })];
}
export const useCourses = () => {
  const q = useQuery({ queryKey: ['courses-all'], queryFn: () => get<any>('/api/training/courses?pageSize=200'), retry: false });
  return (Array.isArray(q.data) ? q.data : q.data?.items ?? []) as any[];
};

/* ============================== Enterprise ============================== */

export function EnterpriseOverview() {
  const nav = useNavigate();
  const q = useQuery({ queryKey: ['site-metrics'], queryFn: () => get<any[]>('/api/enterprise/site-metrics') });
  const rows = q.data ?? [];
  const tot = (k: string) => rows.reduce((a, r) => a + Number(r[k] ?? 0), 0);
  return (
    <ModuleWorkspace module={mod('enterprise')} crumbs={[{ label: 'Enterprise' }]} title="Enterprise quality overview">
      {q.error && <Alert severity="error">{(q.error as Error).message}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(170px, 1fr))', gap: 1.5, mb: 2.5 }}>
        <Tile label="Sites in scope" value={rows.length} />
        <Tile label="Open NCRs" value={tot('openNcrs')} color={tot('overdueNcrs') ? 'error.main' : 'success.main'} hint={`${tot('overdueNcrs')} overdue`} />
        <Tile label="Open CAPAs" value={tot('openCapas')} color={tot('overdueCapas') ? 'error.main' : 'success.main'} hint={`${tot('overdueCapas')} overdue`} />
        <Tile label="Overdue findings" value={tot('overdueFindings')} color={tot('overdueFindings') ? 'warning.main' : 'success.main'} />
        <Tile label="Training overdue" value={tot('trainingOverdue')} color={tot('trainingOverdue') ? 'warning.main' : 'success.main'} hint={`${tot('trainingAssigned')} open assignments`} />
        <Tile label="High risks" value={tot('highRisks')} color={tot('highRisks') ? 'warning.main' : 'success.main'} />
      </Box>
      <Typography variant="overline" color="text.secondary">Plant scorecards — open a plant's war room</Typography>
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(300px, 1fr))', gap: 1.5, mt: 0.5 }}>
        {rows.map((r) => (
          <Paper key={r.siteId} data-testid="site-card" onClick={() => nav(`/enterprise/war-room/${r.siteId}`)} sx={{ p: 2, cursor: 'pointer', '&:hover': { boxShadow: 3 } }}>
            <Stack direction="row" alignItems="baseline" gap={1}><Typography sx={{ fontFamily: mono, fontWeight: 700 }}>{r.siteCode}</Typography><Typography variant="body2" color="text.secondary" noWrap>{r.siteName}</Typography></Stack>
            <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 1, mt: 1.25 }}>
              {([['NCR', r.openNcrs, r.overdueNcrs === 0, r.overdueNcrs < 3], ['CAPA', r.openCapas, r.overdueCapas === 0, r.overdueCapas < 3], ['Findings', r.overdueFindings, r.overdueFindings === 0, r.overdueFindings < 3],
                ['Training', pct(r.trainingCompliancePct), r.trainingCompliancePct >= 95, r.trainingCompliancePct >= 85], ['First pass', pct(r.firstPassPct), r.firstPassPct >= 98, r.firstPassPct >= 95],
                ['Compliance', pct(r.complianceCoveragePct), r.complianceCoveragePct >= 90, r.complianceCoveragePct >= 70]] as [string, unknown, boolean, boolean][])
                .map(([l, v, g, w]) => <Box key={l} sx={{ borderTop: 3, borderColor: rag(g, w), pt: 0.5 }}><Typography variant="caption" color="text.secondary">{l}</Typography><Typography sx={{ fontFamily: mono, fontWeight: 600 }}>{String(v)}</Typography></Box>)}
            </Box>
          </Paper>
        ))}
      </Box>
    </ModuleWorkspace>
  );
}

export function WarRoom() {
  const { siteId } = useParams();
  const nav = useNavigate();
  const q = useQuery({ queryKey: ['war-room', siteId], queryFn: () => get<any>(`/api/enterprise/war-room/${siteId}`), refetchInterval: 60000 });
  if (!q.data) return <Loading m="enterprise" crumbs={[{ label: 'Enterprise', to: '/enterprise' }, { label: 'War room' }]} q={q} />;
  const m = q.data.metrics;
  const list = (title: string, rows: any[]) => (
    <Paper sx={{ p: 1.5 }}>
      <Typography variant="overline" color="text.secondary">{title} ({rows.length})</Typography>
      {rows.length === 0 ? <Typography variant="body2" color="text.secondary">Nothing open.</Typography> : rows.map((r) => (
        <Stack key={r.id} direction="row" gap={1} alignItems="center" onClick={() => r.url && nav(r.url)} sx={{ py: 0.5, borderTop: 1, borderColor: 'divider', cursor: r.url ? 'pointer' : undefined }}>
          <Typography sx={{ fontFamily: mono, fontSize: 12.5, minWidth: 110 }}>{r.number}</Typography>
          <Typography variant="body2" noWrap sx={{ flex: 1 }}>{r.title}</Typography><StatusChip status={r.status} />
        </Stack>))}
    </Paper>);
  return (
    <ModuleWorkspace module={mod('enterprise')} crumbs={[{ label: 'Enterprise', to: '/enterprise' }, { label: m.siteCode }]} title={`War room — ${m.siteName}`}
      actions={<Typography variant="caption" color="text.secondary">Refreshes every minute · {fmtDateTime(q.data.generatedAt)}</Typography>}>
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(150px, 1fr))', gap: 1.5, mb: 2 }}>
        <Tile label="Open NCRs" value={m.openNcrs} color={rag(m.overdueNcrs === 0, m.overdueNcrs < 3)} hint={`${m.overdueNcrs} overdue`} />
        <Tile label="Open CAPAs" value={m.openCapas} color={rag(m.overdueCapas === 0, m.overdueCapas < 3)} hint={`${m.overdueCapas} overdue`} />
        <Tile label="Open complaints" value={m.openComplaints} color={rag(m.overdueComplaints === 0, m.overdueComplaints < 2)} />
        <Tile label="Open SCARs" value={m.openScars} />
        <Tile label="Scrap qty (30 d)" value={m.scrapQty30d} hint={`${m.scrapEvents30d} events`} />
        <Tile label="First pass (30 d)" value={pct(m.firstPassPct)} color={rag(m.firstPassPct >= 98, m.firstPassPct >= 95)} />
        <Tile label="Training compliance" value={pct(m.trainingCompliancePct)} color={rag(m.trainingCompliancePct >= 95, m.trainingCompliancePct >= 85)} />
        <Tile label="Cost of quality (90 d)" value={Number(m.coq90d).toLocaleString()} />
      </Box>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '1fr 1fr' }, gap: 1.5 }}>
        {list('Nonconformances', q.data.ncrs)}{list('CAPAs', q.data.capas)}{list('Audits', q.data.audits)}{list('Complaints', q.data.complaints)}{list('Supplier corrective actions', q.data.supplierIssues)}
      </Box>
    </ModuleWorkspace>
  );
}

export function OrgStructure() {
  const qc = useQueryClient();
  const { can } = useAuth();
  const sites = useSites();
  const q = useQuery({ queryKey: ['org-rollup'], queryFn: () => get<any[]>('/api/enterprise/rollup') });
  const [open, setOpen] = useState(false);
  const rows = q.data ?? [];
  const children = (pid: string | null) => rows.filter((r) => (r.parentId ?? null) === pid);
  const Node = ({ r, depth }: { r: any; depth: number }): ReactNode => (
    <>
      <TableRow><TableCell sx={{ pl: 2 + depth * 3 }}><b>{r.name}</b> <Typography component="span" sx={{ fontFamily: mono, fontSize: 12 }} color="text.secondary">{r.code}</Typography></TableCell>
        <TableCell>{orgLevels[r.level]}</TableCell><TableCell>{r.siteIds.length}</TableCell><TableCell>{r.totals.openNcrs}</TableCell><TableCell>{r.totals.openCapas}</TableCell><TableCell>{r.totals.trainingOverdue}</TableCell><TableCell>{pct(r.totals.complianceCoveragePct)}</TableCell></TableRow>
      {children(r.id).map((c) => <Node key={c.id} r={c} depth={depth + 1} />)}
    </>);
  return (
    <ModuleWorkspace module={mod('enterprise')} crumbs={[{ label: 'Enterprise', to: '/enterprise' }, { label: 'Organization' }]} title="Organization structure"
      actions={can('admin.organization') && <Button variant="contained" onClick={() => setOpen(true)}>Add unit</Button>}>
      <Paper><Table size="small"><TableHead><TableRow>{['Unit', 'Level', 'Sites', 'Open NCRs', 'Open CAPAs', 'Training overdue', 'Compliance'].map((h) => <TableCell key={h}>{h}</TableCell>)}</TableRow></TableHead>
        <TableBody>{children(null).map((r) => <Node key={r.id} r={r} depth={0} />)}</TableBody></Table>
        {rows.length === 0 && <Typography sx={{ p: 2 }} color="text.secondary">No organization units yet. Add the corporate unit first.</Typography>}</Paper>
      <StepDialog open={open} onClose={() => setOpen(false)} title="Add organization unit" confirm="Add"
        inputs={[{ name: 'code', label: 'Code' }, { name: 'name', label: 'Name' }, { name: 'level', label: 'Level', type: 'select', options: orgLevels.map((l, i) => [i, l]), initial: '0' },
          { name: 'parentId', label: 'Parent', type: 'select', options: [['', 'None (top level)'], ...rows.map((r) => [r.id, `${r.code} — ${r.name}`] as [string, string])] },
          { name: 'siteId', label: 'Site (plants, warehouses, labs)', type: 'select', options: [['', 'None'], ...(sites.data ?? []).map((s) => [s.id, `${s.code} — ${s.name}`] as [string, string])] }]}
        onConfirm={async (v) => { await post('/api/enterprise/units', { code: v.code, name: v.name, level: Number(v.level || 0), parentId: v.parentId || null, siteId: v.siteId || null }); qc.invalidateQueries({ queryKey: ['org-rollup'] }); }} />
    </ModuleWorkspace>
  );
}

export function StandardList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const q = useQuery({ queryKey: ['standards'], queryFn: () => get<any[]>('/api/enterprise/standards') });
  const cols: ColumnDef<any, any>[] = [{ header: 'Number', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' },
    { header: 'Local procedure', accessorKey: 'allowLocalProcedure', cell: (x) => (x.getValue() ? 'Allowed' : 'Adopt as issued') }, { header: 'Published', accessorKey: 'createdAt', cell: (x) => fmtDate(x.getValue()) }];
  return (
    <ModuleWorkspace module={mod('enterprise')} crumbs={[{ label: 'Enterprise', to: '/enterprise' }, { label: 'Corporate standards' }]} title="Corporate standards"
      actions={can('enterprise.edit') && <Button variant="contained" onClick={() => nav('/enterprise/standards/new')}>Publish standard</Button>}>
      <DataTable data={q.data ?? []} columns={cols} onRowClick={(r) => nav(`/enterprise/standards/${r.id}`)} empty="No corporate standards published." />
    </ModuleWorkspace>
  );
}

export function StandardNew() {
  const nav = useNavigate();
  const docs = useQuery({ queryKey: ['docs-effective'], queryFn: () => get<{ items: any[] }>('/api/documents?pageSize=200&status=1') });
  const schema = z.object({ title: z.string().min(1, 'Required'), documentId: z.string().min(1, 'Select the controlled document'), siteId: z.string().min(1, 'Required'), allowLocalProcedure: z.string() });
  return (
    <ModuleWorkspace module={mod('enterprise')} crumbs={[{ label: 'Corporate standards', to: '/enterprise/standards' }, { label: 'Publish' }]} title="Publish corporate standard">
      <SchemaForm schema={schema} defaults={{ title: '', documentId: '', siteId: '', allowLocalProcedure: 'false' }} submitLabel="Publish"
        fields={[{ name: 'title', label: 'Title', full: true }, { name: 'documentId', label: 'Controlled document', type: 'select', full: true, options: (docs.data?.items ?? []).map((d) => [d.id, `${d.number} — ${d.title}`]) },
          { name: 'siteId', label: 'Owning site (corporate)', type: 'site' }, { name: 'allowLocalProcedure', label: 'Plants may add a local procedure', type: 'select', options: [['false', 'No — adopt as issued'], ['true', 'Yes']] }]}
        onSubmit={async (v) => { const r = await post<{ id: string }>('/api/enterprise/standards', { ...v, allowLocalProcedure: v.allowLocalProcedure === 'true' }); nav(`/enterprise/standards/${r.id}`); }} onCancel={() => nav(-1)} />
    </ModuleWorkspace>
  );
}

export function StandardDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const sites = useSites();
  const courseRows = useCourses();
  const std = useQuery({ queryKey: ['standards'], queryFn: () => get<any[]>('/api/enterprise/standards') });
  const ro = useQuery({ queryKey: ['rollouts', id], queryFn: () => get<any[]>(`/api/enterprise/standards/${id}/rollouts`) });
  const [dlg, setDlg] = useState<null | { kind: 'rollout' | 'adopt' | 'verify'; id?: string }>(null);
  const s = std.data?.find((x) => x.id === id);
  const refresh = () => qc.invalidateQueries({ queryKey: ['rollouts', id] });
  if (!s) return <Loading m="enterprise" crumbs={[{ label: 'Corporate standards', to: '/enterprise/standards' }]} q={std} />;
  const siteName = (sid: string) => sites.data?.find((x) => x.id === sid)?.code ?? sid;
  const verified = (ro.data ?? []).filter((r) => r.status === 3).length;
  return (
    <ModuleWorkspace module={mod('enterprise')} crumbs={[{ label: 'Corporate standards', to: '/enterprise/standards' }, { label: s.number }]} title={`${s.number} — ${s.title}`}
      actions={<Button variant="contained" onClick={() => setDlg({ kind: 'rollout' })}>Roll out to a site</Button>}>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'minmax(0, 1fr) 280px' }, gap: 2 }}>
        <Section title={`Site rollout (${verified}/${ro.data?.length ?? 0} verified)`}>
          <LinearProgress variant="determinate" value={ro.data?.length ? (100 * verified) / ro.data.length : 0} sx={{ mb: 2, height: 8, borderRadius: 1 }} />
          <Table size="small"><TableHead><TableRow>{['Site', 'Status', 'Due', 'Local procedure', 'Verified', ''].map((h) => <TableCell key={h}>{h}</TableCell>)}</TableRow></TableHead>
            <TableBody>{(ro.data ?? []).map((r) => (
              <TableRow key={r.id}><TableCell sx={{ fontFamily: mono }}>{siteName(r.siteId)}</TableCell><TableCell><StatusChip status={rolloutStatus[r.status]} /></TableCell><TableCell>{fmtDate(r.dueOn)}</TableCell>
                <TableCell>{r.localDocumentId ? 'Yes' : '—'}</TableCell><TableCell>{fmtDateTime(r.verifiedAt)}</TableCell>
                <TableCell align="right">{r.status <= 1 && <Button size="small" onClick={() => setDlg({ kind: 'adopt', id: r.id })}>Adopt</Button>}
                  {(r.status === 1 || r.status === 2) && <Button size="small" onClick={() => setDlg({ kind: 'verify', id: r.id })}>Verify</Button>}</TableCell></TableRow>))}</TableBody></Table>
        </Section>
        <Facts items={[['Number', s.number], ['Local procedure', s.allowLocalProcedure ? 'Allowed' : 'Adopt as issued'], ['Published', fmtDate(s.createdAt)]]} />
      </Box>
      <StepDialog open={dlg?.kind === 'rollout'} onClose={() => setDlg(null)} title="Roll out to a site" confirm="Roll out" intro="The site's enterprise managers are notified to adopt the standard."
        inputs={[{ name: 'site', label: 'Site', type: 'select', options: (sites.data ?? []).map((x) => [x.id, `${x.code} — ${x.name}`]) }, { name: 'dueOn', label: 'Adopt by', type: 'date' }]}
        onConfirm={async (v) => { await post(`/api/enterprise/standards/${id}/rollouts`, { siteIds: [v.site], dueOn: v.dueOn || null }); refresh(); }} />
      <StepDialog open={dlg?.kind === 'adopt'} onClose={() => setDlg(null)} title="Adopt at site" confirm="Adopt" intro="Pick the rollout training course and who must be trained; assignments are created immediately."
        inputs={[{ name: 'course', label: 'Training course', type: 'select', options: [['', 'No training'], ...courseRows.map((c) => [c.id, `${c.number} — ${c.title}`] as [string, string])] }, { name: 'trainees', label: 'Trainees', type: 'users' }, { name: 'notes', label: 'Notes', type: 'multiline' }]}
        onConfirm={async (v) => { await post(`/api/enterprise/rollouts/${dlg!.id}/adopt`, { localDocumentId: null, trainingCourseId: v.course || null, traineeIds: v.trainees ? v.trainees.split(',') : [], notes: v.notes }); refresh(); }} />
      <StepDialog open={dlg?.kind === 'verify'} onClose={() => setDlg(null)} title="Verify rollout" confirm="Verify" intro="Verification is refused while rollout training is still open."
        inputs={[{ name: 'notes', label: 'Verification notes', type: 'multiline' }]} onConfirm={async (v) => { await post(`/api/enterprise/rollouts/${dlg!.id}/verify`, { notes: v.notes }); refresh(); }} />
    </ModuleWorkspace>
  );
}

/* ============================== Training extensions ============================== */

const cellColor: Record<string, string> = { Qualified: '#2e7d32', Expiring: '#ed6c02', RetrainingDue: '#0288d1', Missing: '#bdbdbd', Expired: '#d32f2f', RetrainingOverdue: '#d32f2f', Inactive: '#9e9e9e' };

export function SkillsMatrix() {
  const [site, setSite] = useSite();
  const { can } = useAuth();
  const qc = useQueryClient();
  const courseRows = useCourses();
  const q = useQuery({ queryKey: ['skills-matrix', site], enabled: !!site, queryFn: () => get<any>(`/api/training/skills-matrix?siteId=${site}`) });
  const gaps = useQuery({ queryKey: ['skill-gaps', site], enabled: !!site, queryFn: () => get<any[]>(`/api/training/gaps?siteId=${site}`) });
  const [tab, setTab] = useState('matrix');
  const [dlg, setDlg] = useState<null | 'skill' | { certify: string }>(null);
  const cell = (u: string, s: string) => q.data?.cells.find((c: any) => c.userId === u && c.skillId === s);
  const refresh = () => { qc.invalidateQueries({ queryKey: ['skills-matrix'] }); qc.invalidateQueries({ queryKey: ['skill-gaps'] }); };
  return (
    <ModuleWorkspace module={mod('training')} crumbs={[{ label: 'Training', to: '/training' }, { label: 'Skills matrix' }]} title="Skills and qualification matrix"
      tabs={[{ label: 'Matrix', value: 'matrix' }, { label: `Gaps (${gaps.data?.length ?? 0})`, value: 'gaps' }, { label: 'Skills', value: 'skills' }]} tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1}><SiteSelect value={site} onChange={setSite} />{can('training.edit') && <Button variant="contained" onClick={() => setDlg('skill')}>New skill</Button>}</Stack>}>
      {tab === 'matrix' && q.data && (
        <Paper sx={{ overflowX: 'auto' }}>
          <Table size="small"><TableHead><TableRow><TableCell>Person</TableCell>{q.data.skills.map((s: any) => <TableCell key={s.id} align="center" sx={{ fontFamily: mono, fontSize: 12 }} title={s.name}>{s.code}</TableCell>)}</TableRow></TableHead>
            <TableBody>{q.data.people.map((p: any) => (
              <TableRow key={p.id}><TableCell>{p.name}</TableCell>
                {q.data.skills.map((s: any) => { const c = cell(p.id, s.id); const req = (q.data.requiredBy[s.id] ?? []).length > 0;
                  return <TableCell key={s.id} align="center" title={c?.reason}><Box data-testid="matrix-cell" sx={{ width: 22, height: 22, borderRadius: '50%', mx: 'auto', bgcolor: cellColor[c?.status] ?? '#eee', border: req ? '2px solid #263238' : 'none' }} /></TableCell>; })}</TableRow>))}</TableBody></Table>
          {q.data.skills.length === 0 && <Typography sx={{ p: 2 }} color="text.secondary">No skills defined for this site yet.</Typography>}
          <Stack direction="row" gap={2} sx={{ p: 1.5, flexWrap: 'wrap' }}>{Object.entries(cellColor).map(([k, c]) => <Stack key={k} direction="row" gap={0.5} alignItems="center"><Box sx={{ width: 12, height: 12, borderRadius: '50%', bgcolor: c }} /><Typography variant="caption">{k}</Typography></Stack>)}<Typography variant="caption" color="text.secondary">Outlined = required at this site</Typography></Stack>
        </Paper>)}
      {tab === 'gaps' && <DataTable data={gaps.data ?? []} empty="No qualification gaps." columns={[{ header: 'Person', accessorKey: 'person' }, { header: 'Skill', accessorKey: 'skill' }, { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={x.getValue()} /> }, { header: 'Reason', accessorKey: 'reason' }, { header: 'Expires', accessorKey: 'expiresOn', cell: (x) => fmtDate(x.getValue()) }]} />}
      {tab === 'skills' && <DataTable data={q.data?.skills ?? []} empty="No skills defined for this site."
        columns={[{ header: 'Code', accessorKey: 'code', cell: num }, { header: 'Name', accessorKey: 'name' }, { header: 'Kind', accessorKey: 'kind', cell: (x) => skillKinds[x.getValue()] }, { header: 'Courses', accessorKey: 'courseIds', cell: (x) => x.getValue().length },
          { header: 'Certification', accessorKey: 'requiresCertification', cell: (x) => (x.getValue() ? 'Required' : '—') }, { header: 'Audit types', accessorKey: 'auditTypes', cell: (x) => x.getValue().join(', ') },
          { header: '', id: 'act', cell: (x) => can('training.edit') && <Button size="small" onClick={() => setDlg({ certify: (x.row.original as any).id })}>Record certification</Button> }]} />}
      <StepDialog open={dlg === 'skill'} onClose={() => setDlg(null)} title="New skill or qualification" confirm="Create"
        inputs={[{ name: 'code', label: 'Code' }, { name: 'name', label: 'Name' }, { name: 'kind', label: 'Kind', type: 'select', options: skillKinds.map((k, i) => [i, k]), initial: '1' },
          { name: 'course', label: 'Qualifying course', type: 'select', options: [['', 'None'], ...courseRows.map((c) => [c.id, `${c.number} — ${c.title}`] as [string, string])] },
          { name: 'cert', label: 'Requires certification', type: 'checkbox' }, { name: 'auditTypes', label: 'Audit types (auditor qualifications, comma separated)' }, { name: 'require', label: 'Required for everyone at this site', type: 'checkbox' }]}
        onConfirm={async (v) => {
          const r = await post<{ id: string }>('/api/training/skills', { code: v.code, name: v.name, kind: Number(v.kind || 1), siteId: site, courseIds: v.course ? [v.course] : [], requiresCertification: v.cert === 'true', auditTypes: v.auditTypes ? v.auditTypes.split(',').map((x) => x.trim()) : [], minimumAudits: 0 });
          if (v.require === 'true') await post(`/api/training/skills/${r.id}/requirements`, { siteId: site });
          refresh();
        }} />
      <StepDialog open={typeof dlg === 'object' && dlg !== null} onClose={() => setDlg(null)} title="Record certification" confirm="Record"
        inputs={[{ name: 'user', label: 'People', type: 'users' }, { name: 'on', label: 'Certified on', type: 'date' }, { name: 'exp', label: 'Expires on', type: 'date' }, { name: 'ref', label: 'Certificate reference' }, { name: 'by', label: 'Issued by' }]}
        onConfirm={async (v) => { for (const u of v.user.split(',').filter(Boolean)) await post(`/api/training/skills/${(dlg as any).certify}/certifications`, { userId: u, certifiedOn: v.on, expiresOn: v.exp || null, reference: v.ref, issuedBy: v.by }); refresh(); }} />
    </ModuleWorkspace>
  );
}

export function CurriculumList() {
  const qc = useQueryClient();
  const { can } = useAuth();
  const sites = useSites();
  const courseRows = useCourses();
  const q = useQuery({ queryKey: ['curricula'], queryFn: () => get<any[]>('/api/training/curricula') });
  const [dlg, setDlg] = useState<null | 'new' | { assign: string }>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const r = useRunner(() => qc.invalidateQueries({ queryKey: ['curricula'] }));
  return (
    <ModuleWorkspace module={mod('training')} crumbs={[{ label: 'Training', to: '/training' }, { label: 'Curricula' }]} title="Curricula, paths and programs"
      actions={can('training.edit') && <Stack direction="row" gap={1}><Button onClick={() => r.run(async () => { const x = await post<any>('/api/training/auto-assign'); setMsg(`${x.assigned} assigned, ${x.refreshers} refreshers.`); })}>Run auto-assignment</Button><Button variant="contained" onClick={() => setDlg('new')}>New curriculum</Button></Stack>}>
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      {msg && <Alert severity="success" sx={{ mb: 2 }} onClose={() => setMsg(null)}>{msg}</Alert>}
      <DataTable data={q.data ?? []} empty="No curricula yet."
        columns={[{ header: 'Number', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' }, { header: 'Kind', accessorKey: 'kind', cell: (x) => curriculumKinds[x.getValue()] }, { header: 'Courses', accessorKey: 'courseIds', cell: (x) => x.getValue().length },
          { header: 'Auto-assign', accessorKey: 'autoAssign', cell: (x) => (x.getValue() ? 'Yes' : 'No') }, { header: '', id: 'a', cell: (x) => can('training.edit') && <Button size="small" onClick={() => setDlg({ assign: x.row.original.id })}>Assign</Button> }]} />
      <StepDialog open={dlg === 'new'} onClose={() => setDlg(null)} title="New curriculum" confirm="Create" intro="Training paths release courses one at a time, in order. Certification programs issue a certification when complete."
        inputs={[{ name: 'title', label: 'Title' }, { name: 'kind', label: 'Kind', type: 'select', options: curriculumKinds.map((k, i) => [i, k]), initial: '0' },
          { name: 'site', label: 'Site', type: 'select', options: (sites.data ?? []).map((s) => [s.id, `${s.code} — ${s.name}`]) },
          { name: 'courses', label: 'Course numbers, in order (comma separated)', helper: courseRows.slice(0, 12).map((c) => c.number).join(', ') }, { name: 'auto', label: 'Auto-assign to people who require it', type: 'checkbox' }]}
        onConfirm={async (v) => {
          const ids = v.courses.split(',').map((n) => courseRows.find((c) => c.number === n.trim())?.id).filter(Boolean);
          await post('/api/training/curricula', { title: v.title, kind: Number(v.kind || 0), siteId: v.site, courseIds: ids, autoAssign: v.auto === 'true', grantsSkillId: null }); qc.invalidateQueries({ queryKey: ['curricula'] });
        }} />
      <StepDialog open={typeof dlg === 'object' && dlg !== null} onClose={() => setDlg(null)} title="Assign curriculum" confirm="Assign" inputs={[{ name: 'users', label: 'People', type: 'users' }]}
        onConfirm={async (v) => { const x = await post<any>(`/api/training/curricula/${(dlg as any).assign}/assign`, { userIds: v.users.split(',').filter(Boolean) }); setMsg(`${x.assigned} assignment(s) created.`); }} />
    </ModuleWorkspace>
  );
}

/* ============================== Audit programs ============================== */

const auditTypes = ['Internal', 'Supplier', 'Layered', 'Process', 'Product', 'System', 'Regulatory', 'Customer'];
const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

export function AuditProgramList() {
  const nav = useNavigate();
  const qc = useQueryClient();
  const { can } = useAuth();
  const sites = useSites();
  const q = useQuery({ queryKey: ['audit-programs'], queryFn: () => get<any[]>('/api/audits/programs') });
  const [open, setOpen] = useState(false);
  return (
    <ModuleWorkspace module={mod('audits')} crumbs={[{ label: 'Audits', to: '/audits' }, { label: 'Annual programs' }]} title="Audit programs"
      actions={can('audits.edit') && <Button variant="contained" onClick={() => setOpen(true)}>New program</Button>}>
      <DataTable data={q.data ?? []} onRowClick={(r) => nav(`/audits/programs/${r.id}`)} empty="No audit programs."
        columns={[{ header: 'Number', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' }, { header: 'Year', accessorKey: 'year' }, { header: 'Approved', accessorKey: 'approvedAt', cell: (x) => (x.getValue() ? fmtDate(x.getValue()) : 'Draft') }]} />
      <StepDialog open={open} onClose={() => setOpen(false)} title="New audit program" confirm="Create"
        inputs={[{ name: 'title', label: 'Title' }, { name: 'year', label: 'Year', initial: String(new Date().getFullYear()) }, { name: 'site', label: 'Site', type: 'select', options: (sites.data ?? []).map((s) => [s.id, `${s.code} — ${s.name}`]) }, { name: 'objectives', label: 'Objectives', type: 'multiline' }]}
        onConfirm={async (v) => { const r = await post<{ id: string }>('/api/audits/programs', { title: v.title, year: Number(v.year), siteId: v.site, objectives: v.objectives }); qc.invalidateQueries({ queryKey: ['audit-programs'] }); nav(`/audits/programs/${r.id}`); }} />
    </ModuleWorkspace>
  );
}

export function AuditProgramDetail() {
  const { id } = useParams();
  const nav = useNavigate();
  const qc = useQueryClient();
  const users = useUsers();
  const q = useQuery({ queryKey: ['audit-program', id], queryFn: () => get<any>(`/api/audits/programs/${id}`) });
  const [dlg, setDlg] = useState<null | 'item' | { schedule: string }>(null);
  const r = useRunner(() => qc.invalidateQueries({ queryKey: ['audit-program', id] }));
  if (!q.data) return <Loading m="audits" crumbs={[{ label: 'Audit programs', to: '/audits/programs' }]} q={q} />;
  const p = q.data.program;
  const name = (uid?: string) => users.data?.find((u) => u.id === uid)?.displayName ?? '—';
  return (
    <ModuleWorkspace module={mod('audits')} crumbs={[{ label: 'Audit programs', to: '/audits/programs' }, { label: p.number }]} title={`${p.number} — ${p.title}`}
      actions={<Stack direction="row" gap={1}><Button onClick={() => setDlg('item')}>Add planned audit</Button>{!p.approvedAt && <Button variant="contained" onClick={() => r.run(() => post(`/api/audits/programs/${id}/approve`))}>Approve program</Button>}</Stack>}>
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      <Paper sx={{ overflowX: 'auto', mb: 2 }}>
        <Table size="small"><TableHead><TableRow><TableCell>Planned audit</TableCell>{months.map((m) => <TableCell key={m} align="center">{m}</TableCell>)}</TableRow></TableHead>
          <TableBody>{q.data.items.map((i: any) => (
            <TableRow key={i.id}><TableCell><b>{i.auditType}</b> · {i.subject}<Typography variant="caption" display="block" color="text.secondary">Lead: {name(i.leadAuditorId)}</Typography></TableCell>
              {months.map((_, mi) => <TableCell key={mi} align="center">{i.plannedMonth === mi + 1 && (i.auditId
                ? <Chip size="small" color="primary" label="Scheduled" onClick={() => nav(`/audits/${i.auditId}`)} />
                : <Chip size="small" variant="outlined" label={p.approvedAt ? 'Schedule' : 'Planned'} onClick={() => p.approvedAt && setDlg({ schedule: i.id })} />)}</TableCell>)}</TableRow>))}</TableBody></Table>
        {q.data.items.length === 0 && <Typography sx={{ p: 2 }} color="text.secondary">No audits planned yet.</Typography>}
      </Paper>
      {!p.approvedAt && <Alert severity="info">Audits can be scheduled once the program is approved. Lead auditors must hold a current auditor qualification for the audit type where one is configured for the site.</Alert>}
      <StepDialog open={dlg === 'item'} onClose={() => setDlg(null)} title="Add planned audit" confirm="Add"
        inputs={[{ name: 'type', label: 'Audit type', type: 'select', options: auditTypes.map((t) => [t, t]), initial: 'Internal' }, { name: 'subject', label: 'Subject / area' }, { name: 'criteria', label: 'Criteria' },
          { name: 'month', label: 'Planned month', type: 'select', options: months.map((m, i) => [i + 1, m]), initial: String(new Date().getMonth() + 1) },
          { name: 'lead', label: 'Lead auditor', type: 'select', options: (users.data ?? []).map((u) => [u.id, u.displayName]) }]}
        onConfirm={async (v) => { await post(`/api/audits/programs/${id}/items`, { auditType: v.type, subject: v.subject, criteria: v.criteria, plannedMonth: Number(v.month), leadAuditorId: v.lead || null }); qc.invalidateQueries({ queryKey: ['audit-program', id] }); }} />
      <StepDialog open={typeof dlg === 'object' && dlg !== null} onClose={() => setDlg(null)} title="Schedule audit" confirm="Schedule"
        inputs={[{ name: 'start', label: 'Start', type: 'date' }, { name: 'end', label: 'End', type: 'date' }, { name: 'scope', label: 'Scope' }, { name: 'q', label: 'First checklist question' }]}
        onConfirm={async (v) => { const a = await post<{ id: string }>(`/api/audits/programs/items/${(dlg as any).schedule}/schedule`, { plannedStart: v.start, plannedEnd: v.end, scope: v.scope, checklist: [{ clause: '', question: v.q || 'Is the process followed as documented?' }] }); nav(`/audits/${a.id}`); }} />
    </ModuleWorkspace>
  );
}
