import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Alert, Box, Button, Chip, InputAdornment, Paper, Stack, TextField, Typography } from '@mui/material';
import SearchOutlined from '@mui/icons-material/SearchOutlined';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { get, post, put } from '../api';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, fmtDate } from '../components/Status';
import { Facts, Prose, Section, StepDialog, useRunner, useSites, useUsers } from '../components/Kit';
import { mono } from '../theme';
import { Loading, SiteSelect, Tile, mod, useSite } from './Enterprise';

/* eslint-disable @typescript-eslint/no-explicit-any */
export const knowledgeKinds = ['Lesson learned', 'Best practice', 'Root cause', 'Troubleshooting guide', '5 Why', 'Fishbone'];
const triggers = ['Supplier issue', 'Customer issue', 'Audit finding', 'Production issue'];
const subjects = ['Site', 'Process', 'Supplier'];
const fishboneCats = ['Man', 'Machine', 'Method', 'Material', 'Measurement', 'Environment'];
const K = (label: string) => [{ label: 'Knowledge', to: '/knowledge' }, { label }];
const lines = (s?: string) => (s ?? '').split('\n').map((x) => x.trim()).filter(Boolean);

/** Card-grid knowledge library with free-text similar-issue discovery. */
export function KnowledgeLibrary() {
  const nav = useNavigate(); const qc = useQueryClient(); const sites = useSites();
  const [tab, setTab] = useState('library'); const [q, setQ] = useState(''); const [kind, setKind] = useState<number | null>(null);
  const [text, setText] = useState(''); const [hits, setHits] = useState<any[] | null>(null); const [open, setOpen] = useState(false);
  const list = useQuery({ queryKey: ['knowledge', q, kind], queryFn: () => get<any[]>(`/api/knowledge?q=${encodeURIComponent(q)}${kind != null ? `&kind=${kind}` : ''}`) });
  const r = useRunner(() => undefined);
  return (
    <ModuleWorkspace module={mod('knowledge')} crumbs={[{ label: 'Knowledge' }]} title="Quality knowledge base" tabs={[{ label: 'Library', value: 'library' }, { label: 'Find similar issues', value: 'similar' }]} tab={tab} onTab={setTab}
      actions={<Button variant="contained" onClick={() => setOpen(true)}>New entry</Button>}>
      {tab === 'library' && <>
        <Stack direction="row" gap={1} flexWrap="wrap" sx={{ mb: 2 }} alignItems="center">
          <TextField size="small" placeholder="Search lessons, root causes, tags" value={q} onChange={(e) => setQ(e.target.value)} InputProps={{ startAdornment: <InputAdornment position="start"><SearchOutlined fontSize="small" /></InputAdornment> }} sx={{ minWidth: 320 }} />
          <Chip label="All" color={kind == null ? 'primary' : 'default'} onClick={() => setKind(null)} />
          {knowledgeKinds.map((k, i) => <Chip key={k} label={k} color={kind === i ? 'primary' : 'default'} onClick={() => setKind(i)} />)}
        </Stack>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(3, 1fr)' }, gap: 1.5 }}>
          {(list.data ?? []).map((a) => <Paper key={a.id} sx={{ p: 1.5, cursor: 'pointer', borderTop: 3, borderColor: a.published ? 'primary.main' : 'grey.400' }} onClick={() => nav(`/knowledge/${a.id}`)} data-testid="kb-card">
            <Stack direction="row" justifyContent="space-between"><Typography variant="caption" sx={{ fontFamily: mono }}>{a.number}</Typography><Chip size="small" label={knowledgeKinds[a.kind]} /></Stack>
            <Typography sx={{ fontWeight: 600, my: 0.5 }}>{a.title}</Typography>
            <Typography variant="body2" color="text.secondary" sx={{ display: '-webkit-box', WebkitLineClamp: 3, WebkitBoxOrient: 'vertical', overflow: 'hidden' }}>{a.rootCause ?? a.body}</Typography>
            {!a.published && <Typography variant="caption" color="warning.main">Draft</Typography>}</Paper>)}
          {list.data?.length === 0 && <Typography color="text.secondary">No entries match.</Typography>}
        </Box></>}
      {tab === 'similar' && <Stack gap={2}>
        <Paper variant="outlined" sx={{ p: 2 }}>
          <TextField fullWidth multiline minRows={3} label="Describe the issue" value={text} onChange={(e) => setText(e.target.value)} />
          <Button sx={{ mt: 1 }} variant="contained" disabled={!text.trim()} onClick={() => r.run(async () => setHits(await post<any[]>('/api/knowledge/similar', { text })))}>Find similar</Button>
          <Typography variant="caption" display="block" color="text.secondary">Text similarity over knowledge entries, CAPAs and NCRs you can view. No external service is used.</Typography>
        </Paper>
        {r.error && <Alert severity="error">{r.error}</Alert>}
        {hits && (hits.length === 0 ? <Alert severity="info">Nothing similar found.</Alert> : hits.map((h) => <Paper key={h.type + h.id} sx={{ p: 1.5, display: 'flex', gap: 2, alignItems: 'center', cursor: 'pointer' }} onClick={() => nav(h.url)} data-testid="similar-hit">
          <Box sx={{ width: 54, textAlign: 'center' }}><Typography sx={{ fontFamily: mono, fontWeight: 700 }}>{Math.round(h.similarity * 100)}%</Typography></Box>
          <Box sx={{ flex: 1 }}><Typography variant="body2" component="div"><Chip size="small" label={h.type} sx={{ mr: 1 }} /><b>{h.number}</b> {h.title}</Typography>
            {h.rootCause && <Typography variant="caption">Root cause: {h.rootCause}</Typography>}
            <Stack direction="row" gap={0.5} sx={{ mt: 0.5 }}>{h.sharedTerms.map((t: string) => <Chip key={t} size="small" variant="outlined" label={t} />)}</Stack></Box></Paper>))}
      </Stack>}
      <StepDialog open={open} onClose={() => setOpen(false)} title="New knowledge entry" confirm="Create" intro="For a 5 Why put one why per line. For a fishbone use lines like 'Machine: worn spindle'."
        inputs={[{ name: 'kind', label: 'Kind', type: 'select', options: knowledgeKinds.map((k, i) => [i, k]), initial: '0' }, { name: 'title', label: 'Title' }, { name: 'body', label: 'Content', type: 'multiline' },
          { name: 'analysis', label: 'Whys / fishbone causes', type: 'multiline' }, { name: 'rc', label: 'Root cause' }, { name: 'ca', label: 'Corrective action' }, { name: 'tags', label: 'Tags' },
          { name: 'site', label: 'Site', type: 'select', setup: 'site', options: (sites.data ?? []).map((s) => [s.id, `${s.code} — ${s.name}`]) }]}
        onConfirm={async (v) => {
          const k = Number(v.kind); const an = lines(v.analysis);
          const fish = k === 5 ? Object.fromEntries(fishboneCats.map((c) => [c, an.filter((l) => l.toLowerCase().startsWith(c.toLowerCase() + ':')).map((l) => l.slice(c.length + 1).trim())]).filter(([, x]) => (x as string[]).length)) : null;
          const a = await post<any>('/api/knowledge', { kind: k, title: v.title, body: v.body, siteId: v.site, whys: k === 4 ? an : null, fishbone: fish, rootCause: v.rc || null, correctiveAction: v.ca || null, tags: v.tags || null });
          qc.invalidateQueries({ queryKey: ['knowledge'] }); nav(`/knowledge/${a.id}`);
        }} />
    </ModuleWorkspace>
  );
}

export function KnowledgeDetail() {
  const { id } = useParams(); const nav = useNavigate(); const qc = useQueryClient();
  const q = useQuery({ queryKey: ['knowledge-item', id], queryFn: () => get<any>(`/api/knowledge/${id}`) });
  const r = useRunner(() => qc.invalidateQueries({ queryKey: ['knowledge-item', id] }));
  if (!q.data) return <Loading m="knowledge" crumbs={K('Entry')} q={q} />;
  const a = q.data.article;
  return (
    <ModuleWorkspace module={mod('knowledge')} crumbs={K(a.number)} title={a.title}
      actions={<Button variant={a.published ? 'outlined' : 'contained'} onClick={() => r.run(() => post(`/api/knowledge/${id}/publish`, { published: !a.published }))}>{a.published ? 'Unpublish' : 'Publish'}</Button>}>
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      <Facts items={[['Kind', knowledgeKinds[a.kind]], ['Status', <StatusChip key="p" status={a.published ? 'Published' : 'Draft'} />], ['Source', a.sourceType ? `${a.sourceType}` : 'Written directly'], ['Tags', a.tags ?? '—']]} />
      <Stack direction={{ xs: 'column', md: 'row' }} gap={2} sx={{ mt: 2 }}>
        <Box sx={{ flex: 2 }}>
          <Prose label="Content" text={a.body} />
          {a.whys?.length > 0 && <Section title="5 Why chain"><Stack gap={0.5} data-testid="why-chain">{a.whys.map((w: string, i: number) => <Paper key={i} variant="outlined" sx={{ p: 1, ml: i * 2, borderLeft: 3, borderColor: 'secondary.main' }}><Typography variant="caption" sx={{ fontFamily: mono }}>Why {i + 1}</Typography><Typography variant="body2">{w}</Typography></Paper>)}</Stack></Section>}
          {a.fishbone && Object.keys(a.fishbone).length > 0 && <Section title="Fishbone"><Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 1 }}>{Object.entries(a.fishbone).map(([c, xs]: [string, any]) => <Paper key={c} variant="outlined" sx={{ p: 1 }}><Typography variant="overline">{c}</Typography>{xs.map((x: string) => <Typography key={x} variant="body2">• {x}</Typography>)}</Paper>)}</Box></Section>}
          <Prose label="Root cause" text={a.rootCause} /><Prose label="Corrective action" text={a.correctiveAction} />
        </Box>
        <Box sx={{ flex: 1 }}><Section title="Similar knowledge and issues">
          <Stack gap={1}>{q.data.similar.map((h: any) => <Paper key={h.type + h.id} variant="outlined" sx={{ p: 1, cursor: 'pointer' }} onClick={() => nav(h.url)}><Typography variant="caption">{h.type} · {Math.round(h.similarity * 100)}%</Typography><Typography variant="body2">{h.number} {h.title}</Typography></Paper>)}
            {q.data.similar.length === 0 && <Typography variant="body2" color="text.secondary">Nothing similar.</Typography>}</Stack>
        </Section></Box>
      </Stack>
    </ModuleWorkspace>
  );
}

export function Playbooks() {
  const qc = useQueryClient(); const users = useUsers();
  const q = useQuery({ queryKey: ['playbooks'], queryFn: () => get<any[]>('/api/knowledge/playbooks') });
  const [dlg, setDlg] = useState<null | 'new' | { apply: any }>(null);
  const [done, setDone] = useState<string | null>(null);
  const r = useRunner(() => qc.invalidateQueries({ queryKey: ['playbooks'] }));
  return (
    <ModuleWorkspace module={mod('knowledge')} crumbs={K('Playbooks')} title="Response playbooks" actions={<Button variant="contained" onClick={() => setDlg('new')}>New playbook</Button>}>
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}{done && <Alert severity="success" sx={{ mb: 2 }} onClose={() => setDone(null)}>{done}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(4, 1fr)' }, gap: 1.5 }}>
        {triggers.map((t, i) => <Paper key={t} variant="outlined" sx={{ p: 1, bgcolor: 'grey.50' }}><Typography variant="overline" sx={{ fontWeight: 700 }}>{t}</Typography>
          <Stack gap={1}>{(q.data ?? []).filter((p) => p.trigger === i).map((p) => <Paper key={p.id} sx={{ p: 1.25 }} data-testid="playbook">
            <Typography variant="caption" sx={{ fontFamily: mono }}>{p.number} · rev {p.revision}</Typography><Typography sx={{ fontWeight: 600 }}>{p.title}</Typography>
            <Box component="ol" sx={{ pl: 2.5, my: 0.5 }}>{p.steps.map((s: any, n: number) => <li key={n}><Typography variant="body2">{s.title}{s.dueDays != null ? ` (${s.dueDays} d)` : ''}</Typography></li>)}</Box>
            <Stack direction="row" gap={1}>{p.published ? <Button size="small" variant="contained" onClick={() => setDlg({ apply: p })}>Apply</Button> : <Button size="small" onClick={() => r.run(() => put(`/api/knowledge/playbooks/${p.id}?publish=true`, { title: p.title, trigger: p.trigger, steps: p.steps }))}>Publish</Button>}</Stack>
          </Paper>)}</Stack></Paper>)}
      </Box>
      <StepDialog open={dlg === 'new'} onClose={() => setDlg(null)} title="New playbook" confirm="Create" intro="One step per line: title | guidance | due days."
        inputs={[{ name: 'title', label: 'Title' }, { name: 'trigger', label: 'Trigger', type: 'select', options: triggers.map((t, i) => [i, t]), initial: '0' }, { name: 'steps', label: 'Steps', type: 'multiline' }]}
        onConfirm={async (v) => { await post('/api/knowledge/playbooks', { title: v.title, trigger: Number(v.trigger), steps: lines(v.steps).map((l) => { const [t, g, d] = l.split('|').map((x) => x.trim()); return { title: t, guidance: g || t, dueDays: d ? Number(d) : null }; }) }); qc.invalidateQueries({ queryKey: ['playbooks'] }); }} />
      <StepDialog open={typeof dlg === 'object' && dlg !== null} onClose={() => setDlg(null)} title="Apply playbook" confirm="Create actions" intro="Each step becomes an action item on the record, linked back to the playbook."
        inputs={[{ name: 'type', label: 'Record type', type: 'select', options: [['SupplierCorrectiveAction', 'SCAR'], ['Complaint', 'Complaint'], ['AuditFinding', 'Audit finding'], ['Ncr', 'NCR'], ['Capa', 'CAPA']] }, { name: 'rid', label: 'Record ID' }, { name: 'who', label: 'Assignee', type: 'select', setup: 'user', options: (users.data ?? []).map((u) => [u.id, u.displayName]) }]}
        onConfirm={async (v) => { const res = await post<any>(`/api/knowledge/playbooks/${(dlg as any).apply.id}/apply`, { recordType: v.type, recordId: v.rid.trim(), assigneeId: v.who }); setDone(`${res.actions.length} action item(s) created.`); }} />
    </ModuleWorkspace>
  );
}

export function ProcessMap() {
  const qc = useQueryClient(); const users = useUsers(); const sites = useSites();
  const [site, setSite] = useSite(); const [sel, setSel] = useState<string | null>(null); const [dlg, setDlg] = useState<null | 'new' | 'review' | 'kpi'>(null);
  const q = useQuery({ queryKey: ['processes', site], enabled: !!site, queryFn: () => get<any[]>(`/api/processes?siteId=${site}`) });
  const objs = useQuery({ queryKey: ['objectives-all'], queryFn: () => get<any>('/api/objectives').catch(() => []) });
  const det = useQuery({ queryKey: ['process', sel], enabled: !!sel, queryFn: () => get<any>(`/api/processes/${sel}`) });
  const refresh = () => { qc.invalidateQueries({ queryKey: ['processes'] }); qc.invalidateQueries({ queryKey: ['process'] }); };
  const objList = (Array.isArray(objs.data) ? objs.data : objs.data?.items ?? []) as any[];
  return (
    <ModuleWorkspace module={mod('knowledge')} crumbs={[{ label: 'Processes' }]} title="Business processes" actions={<Stack direction="row" gap={1}><SiteSelect value={site} onChange={setSite} /><Button variant="contained" onClick={() => setDlg('new')}>New process</Button></Stack>}>
      <Stack direction="row" gap={1} sx={{ overflowX: 'auto', pb: 1 }} data-testid="process-chain">
        {(q.data ?? []).map((p, i) => <Stack key={p.id} direction="row" alignItems="center" gap={1}>
          <Paper onClick={() => setSel(p.id)} sx={{ p: 1.5, minWidth: 180, cursor: 'pointer', clipPath: 'polygon(0 0, 90% 0, 100% 50%, 90% 100%, 0 100%, 8% 50%)', pl: 3, bgcolor: sel === p.id ? 'primary.main' : p.reviewOverdue ? 'warning.light' : 'grey.100', color: sel === p.id ? '#fff' : undefined }}>
            <Typography variant="caption" sx={{ fontFamily: mono }}>{p.code}</Typography><Typography variant="body2" sx={{ fontWeight: 600 }}>{p.name}</Typography><Typography variant="caption">{p.metrics.length} KPI · {p.ncrs12m} NCR</Typography></Paper>
          {i < (q.data?.length ?? 0) - 1 && <span />}</Stack>)}
        {q.data?.length === 0 && <Typography color="text.secondary">No processes defined for this site.</Typography>}
      </Stack>
      {det.data && <Box sx={{ mt: 2 }}><Section title={`${det.data.process.code} · ${det.data.process.name}`} action={<Stack direction="row" gap={1}><Button size="small" onClick={() => setDlg('kpi')}>Link KPI</Button><Button size="small" variant="contained" onClick={() => setDlg('review')}>Record review</Button></Stack>}>
        <Facts items={[['Purpose', det.data.process.purpose ?? '—'], ['Last reviewed', fmtDate(det.data.process.lastReviewedOn) || 'never'], ['Review due', fmtDate(det.data.dashboard?.reviewDue)]]} />
        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 1, my: 1.5 }}>{(det.data.dashboard?.metrics ?? []).map((m: any) => <Tile key={m.id} label={m.metricName} value={m.latest ?? '—'} hint={`target ${m.target} · ${m.rating ?? 'no data'}`} color={m.rating === 'Critical' ? 'error.main' : m.rating === 'Warning' ? 'warning.main' : 'success.main'} />)}</Box>
        <DataTable data={det.data.reviews} empty="No reviews." columns={[{ header: 'Reviewed', accessorKey: 'reviewedOn', cell: (x) => fmtDate(x.getValue()) }, { header: 'Outcome', accessorKey: 'outcome' }, { header: 'Notes', accessorKey: 'notes' }]} />
      </Section></Box>}
      <StepDialog open={dlg === 'new'} onClose={() => setDlg(null)} title="New business process" confirm="Create"
        inputs={[{ name: 'code', label: 'Code' }, { name: 'name', label: 'Name' }, { name: 'purpose', label: 'Purpose', type: 'multiline' }, { name: 'site', label: 'Site', type: 'select', setup: 'site', options: (sites.data ?? []).map((s) => [s.id, `${s.code} — ${s.name}`]), initial: site }, { name: 'owner', label: 'Owner', type: 'select', setup: 'user', options: (users.data ?? []).map((u) => [u.id, u.displayName]) }, { name: 'days', label: 'Review interval (days)', initial: '365' }]}
        onConfirm={async (v) => { await post('/api/processes', { code: v.code, name: v.name, purpose: v.purpose || null, siteId: v.site, ownerId: v.owner || null, reviewIntervalDays: Number(v.days) }); refresh(); }} />
      <StepDialog open={dlg === 'review'} onClose={() => setDlg(null)} title="Record process review" confirm="Save" inputs={[{ name: 'on', label: 'Reviewed on', type: 'date' }, { name: 'outcome', label: 'Outcome' }, { name: 'notes', label: 'Notes', type: 'multiline' }]}
        onConfirm={async (v) => { await post(`/api/processes/${sel}/reviews`, { reviewedOn: v.on, outcome: v.outcome, notes: v.notes || null }); refresh(); }} />
      <StepDialog open={dlg === 'kpi'} onClose={() => setDlg(null)} title="Link KPI" confirm="Link" inputs={[{ name: 'o', label: 'Objective / KPI', type: 'select', options: objList.map((o) => [o.id, `${o.number} — ${o.title}`]) }]}
        onConfirm={async (v) => { await post(`/api/processes/${sel}/kpis/${v.o}`); refresh(); }} />
    </ModuleWorkspace>
  );
}

export function Maturity() {
  const qc = useQueryClient(); const sites = useSites();
  const [subject, setSubject] = useState(0); const [open, setOpen] = useState(false);
  const q = useQuery({ queryKey: ['maturity', subject], queryFn: () => get<any>(`/api/maturity?subject=${subject}`) });
  const bench = q.data?.benchmark?.[subjects[subject]];
  const areas = ['Leadership', 'Process control', 'Problem solving', 'Supplier quality', 'Data & digital'];
  return (
    <ModuleWorkspace module={mod('knowledge')} crumbs={[{ label: 'Maturity' }]} title="Quality maturity" tabs={subjects.map((s, i) => ({ label: s, value: String(i) }))} tab={String(subject)} onTab={(t) => setSubject(Number(t))}
      actions={<Button variant="contained" onClick={() => setOpen(true)}>New assessment</Button>}>
      {bench && <Stack direction="row" gap={2} sx={{ mb: 2 }}><Tile label="Assessed" value={bench.count} /><Tile label="Average level (1–5)" value={bench.average} /></Stack>}
      <Stack gap={1}>{(bench?.ranking ?? []).map((r: any) => <Paper key={r.subjectId} sx={{ p: 1.25, display: 'flex', gap: 2, alignItems: 'center' }} data-testid="maturity-row">
        <Typography sx={{ fontFamily: mono, width: 32 }}>#{r.rank}</Typography><Typography sx={{ flex: 1 }}>{r.subjectName}</Typography>
        <Stack direction="row" gap={0.5}>{[1, 2, 3, 4, 5].map((l) => <Box key={l} sx={{ width: 22, height: 22, borderRadius: '50%', bgcolor: l <= Math.round(r.overall) ? 'primary.main' : 'grey.200' }} />)}</Stack>
        <Typography sx={{ fontFamily: mono, width: 40 }}>{r.overall}</Typography><Typography variant="caption">{fmtDate(r.assessedOn)}</Typography></Paper>)}</Stack>
      {!bench && <Typography color="text.secondary">No assessments yet.</Typography>}
      <StepDialog open={open} onClose={() => setOpen(false)} title={`New ${subjects[subject].toLowerCase()} maturity assessment`} confirm="Save" intro="Rate each area from 1 (initial) to 5 (optimizing)."
        inputs={[{ name: 'id', label: subject === 0 ? 'Site' : `${subjects[subject]} ID`, type: subject === 0 ? 'select' : 'text', options: (sites.data ?? []).map((s) => [s.id, `${s.code} — ${s.name}`]) }, { name: 'on', label: 'Assessed on', type: 'date' },
          ...areas.map((a) => ({ name: a, label: a, type: 'select' as const, options: [1, 2, 3, 4, 5].map((n) => [n, String(n)] as [number, string]), initial: '3' }))]}
        onConfirm={async (v) => { await post('/api/maturity', { subject, subjectId: v.id.trim(), assessedOn: v.on, scores: areas.map((a) => ({ area: a, level: Number(v[a] ?? 3) })) }); qc.invalidateQueries({ queryKey: ['maturity'] }); }} />
    </ModuleWorkspace>
  );
}
