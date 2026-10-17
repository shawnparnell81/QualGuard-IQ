import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Alert, Box, Button, Chip, MenuItem, Paper, Stack, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { del, get, post, put } from '../api';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, fmtDate, fmtDateTime } from '../components/Status';
import { Facts, Prose, Section, StepDialog, lookupLabel, useLookup, useRunner, useSites, useUsers } from '../components/Kit';
import { mono } from '../theme';
import { useAuth } from '../auth';
import { Loading, mod, num } from './Enterprise';

/* eslint-disable @typescript-eslint/no-explicit-any */
const sources = ['Customer', 'Internal', 'Regulatory'];
const reqStatus = ['Draft', 'Approved', 'Obsolete'];
const vstate = ['NotStarted', 'InProgress', 'Passed', 'Failed'];
const reviewKinds = ['Concept', 'Feasibility', 'Design', 'Validation', 'Release'];
const reviewStatus = ['Planned', 'Held', 'PendingApproval', 'Approved', 'Rejected', 'Cancelled'];
const vColor = (s: string) => (s === 'Passed' ? 'success' : s === 'Failed' ? 'error' : s === 'InProgress' ? 'warning' : 'default') as any;
const crumbs = (label: string) => [{ label: 'Engineering', to: '/engineering' }, { label }];

/* ---------- requirements + traceability matrix ---------- */
export function RequirementList() {
  const nav = useNavigate(); const qc = useQueryClient(); const sites = useSites();
  const products = useLookup('product'); const customers = useLookup('customer');
  const [tab, setTab] = useState('list'); const [open, setOpen] = useState(false);
  const q = useQuery({ queryKey: ['requirements'], queryFn: () => get<any[]>('/api/engineering/requirements') });
  const tr = useQuery({ queryKey: ['traceability'], enabled: tab === 'matrix', queryFn: () => get<any>('/api/engineering/traceability') });
  return (
    <ModuleWorkspace module={mod('engineering')} crumbs={[{ label: 'Engineering' }]} title="Product requirements"
      tabs={[{ label: 'Register', value: 'list' }, { label: 'Traceability matrix', value: 'matrix' }]} tab={tab} onTab={setTab}
      actions={<Button variant="contained" onClick={() => setOpen(true)}>New requirement</Button>}>
      {tab === 'list' && <DataTable data={q.data ?? []} onRowClick={(r) => nav(`/engineering/requirements/${r.id}`)} empty="No requirements captured."
        columns={[{ header: 'Number', accessorKey: 'number', cell: num }, { header: 'Requirement', accessorKey: 'title' }, { header: 'Source', accessorKey: 'source', cell: (x) => sources[x.getValue()] },
          { header: 'Rev', accessorKey: 'revision' }, { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={reqStatus[x.getValue()]} /> },
          { header: 'Verification', accessorKey: 'verification', cell: (x) => <Chip size="small" variant="outlined" color={vColor(vstate[x.getValue()])} label={vstate[x.getValue()]} /> },
          { header: 'Validation', accessorKey: 'validation', cell: (x) => <Chip size="small" variant="outlined" color={vColor(vstate[x.getValue()])} label={vstate[x.getValue()]} /> }]} />}
      {tab === 'matrix' && tr.data && <>
        <Stack direction="row" gap={1} sx={{ mb: 1.5 }}>
          <Chip label={`${tr.data.total} requirements`} /><Chip color="success" label={`${tr.data.fullyTraced} fully traced`} /><Chip label={`${tr.data.verified} verified`} /><Chip label={`${tr.data.validated} validated`} />
        </Stack>
        <Box sx={{ overflowX: 'auto' }}>
          <Box component="table" data-testid="trace-matrix" sx={{ borderCollapse: 'collapse', fontSize: 13, '& td, & th': { border: 1, borderColor: 'divider', p: 0.75 } }}>
            <thead><tr><th>Requirement</th>{tr.data.columns.map((c: string) => <th key={c}>{c}</th>)}<th>V</th><th>V&amp;V</th></tr></thead>
            <tbody>{tr.data.rows.map((r: any) => (
              <tr key={r.id} style={{ cursor: 'pointer' }} onClick={() => nav(`/engineering/requirements/${r.id}`)}>
                <td><span style={{ fontFamily: mono }}>{r.number}</span> {r.title}</td>
                {tr.data.columns.map((c: string) => <Box component="td" key={c} sx={{ bgcolor: r.links[c].length ? 'rgba(0,137,123,.12)' : 'rgba(198,40,40,.06)', textAlign: 'center' }}>{r.links[c].join(', ') || '—'}</Box>)}
                <td><Chip size="small" color={vColor(r.verification)} label={r.verification} /></td><td><Chip size="small" color={vColor(r.validation)} label={r.validation} /></td>
              </tr>))}</tbody>
          </Box>
        </Box></>}
      <StepDialog open={open} onClose={() => setOpen(false)} title="New requirement" confirm="Create"
        inputs={[{ name: 'title', label: 'Title' }, { name: 'text', label: 'Requirement text', type: 'multiline' }, { name: 'source', label: 'Source', type: 'select', options: sources.map((s, i) => [i, s]), initial: '0' },
          { name: 'site', label: 'Site', type: 'select', options: (sites.data ?? []).map((s) => [s.id, `${s.code} — ${s.name}`]) },
          { name: 'product', label: 'Product', type: 'select', options: [['', 'None'], ...(products.data ?? []).map((p) => [p.id, lookupLabel(p)] as [string, string])] },
          { name: 'customer', label: 'Customer', type: 'select', options: [['', 'None'], ...(customers.data ?? []).map((p) => [p.id, lookupLabel(p)] as [string, string])] }, { name: 'ref', label: 'Customer / standard reference' }]}
        onConfirm={async (v) => { const r = await post<any>('/api/engineering/requirements', { title: v.title, text: v.text, source: Number(v.source), siteId: v.site, productId: v.product || null, customerId: v.customer || null, reference: v.ref || null }); qc.invalidateQueries({ queryKey: ['requirements'] }); nav(`/engineering/requirements/${r.id}`); }} />
    </ModuleWorkspace>
  );
}

export function RequirementDetail() {
  const { id } = useParams(); const qc = useQueryClient(); const { can } = useAuth();
  const q = useQuery({ queryKey: ['requirement', id], queryFn: () => get<any>(`/api/engineering/requirements/${id}`) });
  const targets = useQuery({ queryKey: ['trace-targets'], queryFn: () => get<Record<string, string[]>>('/api/engineering/trace-targets') });
  const [dlg, setDlg] = useState<null | 'trace' | 'revise'>(null); const [target, setTarget] = useState('');
  const refresh = () => qc.invalidateQueries({ queryKey: ['requirement', id] });
  const r = useRunner(refresh);
  if (!q.data) return <Loading m="engineering" crumbs={crumbs('Requirement')} q={q} />;
  const d = q.data; const req = d.requirement;
  return (
    <ModuleWorkspace module={mod('engineering')} crumbs={crumbs(req.number)} title={`${req.number} · ${req.title}`}
      actions={<Stack direction="row" gap={1}>{req.status === 0 && can('engineering.approve') && <Button variant="outlined" onClick={() => r.run(() => post(`/api/engineering/requirements/${id}/approve`))}>Approve</Button>}<Button variant="outlined" onClick={() => setDlg('revise')}>Revise</Button><Button variant="contained" onClick={() => setDlg('trace')}>Add trace link</Button></Stack>}>
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      <Stack direction={{ xs: 'column', md: 'row' }} gap={2}>
        <Box sx={{ flex: 2 }}>
          <Section title="Requirement"><Facts items={[['Source', sources[req.source]], ['Revision', req.revision], ['Status', <StatusChip key="s" status={reqStatus[req.status]} />], ['Reference', req.reference ?? '—'],
            ['Verification', <Chip key="v" size="small" color={vColor(vstate[req.verification])} label={vstate[req.verification]} />], ['Validation', <Chip key="w" size="small" color={vColor(vstate[req.validation])} label={vstate[req.validation]} />]]} />
            <Prose label="Text" text={req.text} /></Section>
          <Box sx={{ mt: 2 }}><Section title="Trace links">
            <DataTable data={d.traces} empty="Not traced to anything yet."
              columns={[{ header: 'Target', accessorKey: 'target' }, { header: 'Record', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' }, { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={x.getValue()} /> },
                { header: '', id: 'x', cell: (x) => <Button size="small" color="error" onClick={() => r.run(() => del(`/api/engineering/requirements/${id}/trace/${(x.row.original as any).id}`))}>Remove</Button> }]} />
          </Section></Box>
        </Box>
        <Box sx={{ flex: 1 }}>
          <Section title="Coverage">
            <Stack gap={0.75}>{Object.entries(d.coverage).map(([k, v]) => <Paper key={k} variant="outlined" sx={{ p: 1, display: 'flex', justifyContent: 'space-between', borderLeft: 4, borderColor: v ? 'success.main' : 'error.light' }}><Typography variant="body2">{k}</Typography><Typography variant="caption">{v ? 'linked' : 'gap'}</Typography></Paper>)}</Stack>
          </Section>
          <Box sx={{ mt: 2 }}><Section title="Revision history">
            <Stack gap={1}>{d.revisions.map((v: any) => <Box key={v.id} sx={{ borderLeft: 2, borderColor: 'primary.main', pl: 1 }}><Typography variant="body2" sx={{ fontWeight: 600 }}>Rev {v.revision} · {fmtDateTime(v.createdAt)}</Typography><Typography variant="caption">{v.reason}</Typography></Box>)}</Stack>
          </Section></Box>
        </Box>
      </Stack>
      <StepDialog open={dlg === 'trace'} onClose={() => setDlg(null)} title="Trace requirement" confirm="Link" intro="Link the requirement to the record that realizes, verifies or affects it. Use the record ID from its page."
        inputs={[{ name: 'target', label: 'Target', type: 'select', options: Object.keys(targets.data ?? {}).map((k) => [k, k]) },
          { name: 'type', label: 'Record type', type: 'select', options: (targets.data?.[target] ?? Object.values(targets.data ?? {}).flat()).map((t) => [t, t]) }, { name: 'rid', label: 'Record ID' }]}
        onConfirm={async (v) => { setTarget(v.target); await post(`/api/engineering/requirements/${id}/trace`, { target: v.target, recordType: v.type, recordId: v.rid.trim() }); refresh(); }} />
      <StepDialog open={dlg === 'revise'} onClose={() => setDlg(null)} title="Revise requirement" confirm="Save revision" intro="A revision returns the requirement to draft and keeps the previous text in history."
        inputs={[{ name: 'title', label: 'Title', initial: req.title }, { name: 'text', label: 'Text', type: 'multiline', initial: req.text }, { name: 'reason', label: 'Reason for change' }]}
        onConfirm={async (v) => { await post(`/api/engineering/requirements/${id}/revise`, { title: v.title, text: v.text, reason: v.reason }); refresh(); }} />
    </ModuleWorkspace>
  );
}

/* ---------- design reviews: a gate board by kind ---------- */
export function DesignReviewList() {
  const nav = useNavigate(); const qc = useQueryClient(); const sites = useSites(); const products = useLookup('product');
  const q = useQuery({ queryKey: ['design-reviews'], queryFn: () => get<any[]>('/api/engineering/reviews') });
  const [open, setOpen] = useState(false);
  return (
    <ModuleWorkspace module={mod('engineering')} crumbs={crumbs('Design reviews')} title="Design reviews" actions={<Button variant="contained" onClick={() => setOpen(true)}>Plan review</Button>}>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(5, 1fr)' }, gap: 1.5 }}>
        {reviewKinds.map((k, i) => (
          <Paper key={k} variant="outlined" sx={{ p: 1, bgcolor: 'grey.50', minHeight: 160 }} data-testid={`gate-${k}`}>
            <Typography variant="overline" sx={{ fontWeight: 700 }}>{k}</Typography>
            <Stack gap={1}>{(q.data ?? []).filter((r) => r.kind === i).map((r) => (
              <Paper key={r.id} sx={{ p: 1, cursor: 'pointer', borderTop: 3, borderColor: r.status === 3 ? 'success.main' : r.status === 4 ? 'error.main' : 'primary.main' }} onClick={() => nav(`/engineering/reviews/${r.id}`)}>
                <Typography variant="caption" sx={{ fontFamily: mono }}>{r.number}</Typography><Typography variant="body2">{r.title}</Typography><StatusChip status={reviewStatus[r.status]} />
              </Paper>))}</Stack>
          </Paper>))}
      </Box>
      <StepDialog open={open} onClose={() => setOpen(false)} title="Plan design review" confirm="Plan"
        inputs={[{ name: 'title', label: 'Title' }, { name: 'kind', label: 'Gate', type: 'select', options: reviewKinds.map((k, i) => [i, k]), initial: '2' }, { name: 'site', label: 'Site', type: 'select', options: (sites.data ?? []).map((s) => [s.id, `${s.code} — ${s.name}`]) },
          { name: 'product', label: 'Product', type: 'select', options: [['', 'None'], ...(products.data ?? []).map((p) => [p.id, lookupLabel(p)] as [string, string])] }]}
        onConfirm={async (v) => { const r = await post<any>('/api/engineering/reviews', { title: v.title, kind: Number(v.kind), siteId: v.site, productId: v.product || null }); qc.invalidateQueries({ queryKey: ['design-reviews'] }); nav(`/engineering/reviews/${r.id}`); }} />
    </ModuleWorkspace>
  );
}

export function DesignReviewDetail() {
  const { id } = useParams(); const qc = useQueryClient(); const users = useUsers();
  const q = useQuery({ queryKey: ['design-review', id], queryFn: () => get<any>(`/api/engineering/reviews/${id}`) });
  const [dlg, setDlg] = useState<null | 'finding' | 'hold' | 'action'>(null);
  const refresh = () => qc.invalidateQueries({ queryKey: ['design-review', id] });
  const r = useRunner(refresh);
  if (!q.data) return <Loading m="engineering" crumbs={crumbs('Design review')} q={q} />;
  const d = q.data; const rv = d.review;
  return (
    <ModuleWorkspace module={mod('engineering')} crumbs={[{ label: 'Engineering', to: '/engineering' }, { label: 'Design reviews', to: '/engineering/reviews' }, { label: rv.number }]} title={`${rv.number} · ${rv.title}`}
      actions={<Stack direction="row" gap={1}>
        <Button variant="outlined" onClick={() => setDlg('finding')}>Add finding</Button><Button variant="outlined" onClick={() => setDlg('action')}>Add action</Button>
        {rv.status === 0 && <Button variant="outlined" onClick={() => setDlg('hold')}>Record meeting</Button>}
        {rv.status === 1 && <Button variant="contained" onClick={() => r.run(() => post(`/api/engineering/reviews/${id}/submit`))}>Submit for approval</Button>}</Stack>}>
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      <Facts items={[['Gate', d.kind], ['Status', <StatusChip key="s" status={d.status} />], ['Held on', fmtDate(rv.heldOn) || '—'], ['Decision', rv.decision ?? '—'], ['Open design risks', String(d.openRisks)]]} />
      <Stack direction={{ xs: 'column', md: 'row' }} gap={2} sx={{ mt: 2 }}>
        <Box sx={{ flex: 1 }}><Section title="Findings">
          <Stack gap={1}>{d.findings.length === 0 && <Typography variant="body2" color="text.secondary">No findings.</Typography>}
            {d.findings.map((f: any) => <Paper key={f.id} variant="outlined" sx={{ p: 1, display: 'flex', gap: 1, alignItems: 'center', opacity: f.resolved ? 0.6 : 1 }}>
              {f.isRisk && <Chip size="small" color="error" label="Risk" />}<Typography variant="body2" sx={{ flex: 1, textDecoration: f.resolved ? 'line-through' : 'none' }}>{f.text}</Typography>
              {!f.resolved && <Button size="small" onClick={() => r.run(() => post(`/api/engineering/reviews/${id}/findings/${f.id}/resolve`))}>Resolve</Button>}</Paper>)}</Stack>
        </Section></Box>
        <Box sx={{ flex: 1 }}><Section title="Actions"><DataTable data={d.actions} empty="No actions." columns={[{ header: 'Number', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' }, { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={String(x.getValue())} /> }]} /></Section></Box>
      </Stack>
      <StepDialog open={dlg === 'finding'} onClose={() => setDlg(null)} title="Add finding" confirm="Add" inputs={[{ name: 'text', label: 'Finding', type: 'multiline' }, { name: 'risk', label: 'Design risk (must be resolved before approval)', type: 'checkbox' }]}
        onConfirm={async (v) => { await post(`/api/engineering/reviews/${id}/findings`, { text: v.text, isRisk: v.risk === 'true' }); refresh(); }} />
      <StepDialog open={dlg === 'hold'} onClose={() => setDlg(null)} title="Record review meeting" confirm="Save" inputs={[{ name: 'on', label: 'Held on', type: 'date' }, { name: 'decision', label: 'Decision', type: 'multiline' }]}
        onConfirm={async (v) => { await post(`/api/engineering/reviews/${id}/hold`, { heldOn: v.on, decision: v.decision }); refresh(); }} />
      <StepDialog open={dlg === 'action'} onClose={() => setDlg(null)} title="Add action" confirm="Add"
        inputs={[{ name: 'title', label: 'Action' }, { name: 'who', label: 'Assignee', type: 'select', options: (users.data ?? []).map((u) => [u.id, u.displayName]) }, { name: 'due', label: 'Due', type: 'date' }]}
        onConfirm={async (v) => { await post(`/api/engineering/reviews/${id}/actions`, { title: v.title, assigneeId: v.who, dueOn: v.due }); refresh(); }} />
    </ModuleWorkspace>
  );
}

/* ---------- BOM quality ---------- */
export function BomQuality() {
  const qc = useQueryClient(); const parts = useLookup('part'); const suppliers = useLookup('supplier');
  const [part, setPart] = useState(''); const [tab, setTab] = useState('1'); const [open, setOpen] = useState(false);
  const q = useQuery({ queryKey: ['bom', part, tab], enabled: !!part, queryFn: () => get<any>(`/api/engineering/parts/${part}/bom?type=${tab}`) });
  const h = useQuery({ queryKey: ['bom-history', part], enabled: !!part && tab === 'h', queryFn: () => get<any[]>(`/api/engineering/parts/${part}/bom/history`) });
  const r = useRunner(() => qc.invalidateQueries({ queryKey: ['bom', part] }));
  const lines = q.data?.lines ?? [];
  return (
    <ModuleWorkspace module={mod('engineering')} crumbs={crumbs('BOM quality')} title="Bill of materials quality"
      tabs={[{ label: 'Engineering BOM', value: '1' }, { label: 'Manufacturing BOM', value: '0' }, { label: 'Change history', value: 'h' }]} tab={tab} onTab={setTab}
      actions={part && tab !== 'h' ? <Button variant="contained" onClick={() => setOpen(true)}>Add line</Button> : undefined}>
      <TextField select size="small" label="Parent part" value={part} onChange={(e) => setPart(e.target.value)} sx={{ minWidth: 320, mb: 2 }}>
        {(parts.data ?? []).map((p) => <MenuItem key={p.id} value={p.id}>{lookupLabel(p)}</MenuItem>)}</TextField>
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      {part && tab !== 'h' && q.data && <Stack direction={{ xs: 'column', md: 'row' }} gap={2}>
        <Box sx={{ flex: 2 }}><DataTable data={lines} empty="No lines."
          columns={[{ header: 'Component', accessorKey: 'component', cell: num }, { header: 'Rev', accessorKey: 'componentRevision' }, { header: 'Qty', accessorKey: 'quantity' }, { header: 'Operation', accessorKey: 'operation' }, { header: 'Supplier', accessorKey: 'supplier' },
            { header: '', id: 'x', cell: (x) => <Button size="small" color="error" onClick={() => r.run(() => put(`/api/engineering/parts/${part}/bom?type=${tab}`, lines.filter((l: any) => l.id !== (x.row.original as any).id).map((l: any) => ({ componentPartId: l.componentPartId, componentRevision: l.componentRevision, quantity: l.quantity, operation: l.operation, supplierId: l.supplierId }))))}>Remove</Button> }]} /></Box>
        <Box sx={{ flex: 1 }}><Section title={`Validation (${q.data.issues.length})`}>
          {q.data.issues.length === 0 ? <Alert severity="success">No issues found.</Alert> : <Stack gap={1}>{q.data.issues.map((i: any, n: number) => <Alert key={n} severity={i.severity === 'Error' ? 'error' : 'warning'} data-testid="bom-issue"><b>{i.component}</b>: {i.issue}</Alert>)}</Stack>}
        </Section></Box></Stack>}
      {part && tab === 'h' && <DataTable data={h.data ?? []} empty="No changes recorded." columns={[{ header: 'When', accessorKey: 'occurredAt', cell: (x) => fmtDateTime(x.getValue()) }, { header: 'Action', accessorKey: 'action' }, { header: 'By', accessorKey: 'userName' }, { header: 'Description', accessorKey: 'description' }]} />}
      <StepDialog open={open} onClose={() => setOpen(false)} title="Add BOM line" confirm="Add"
        inputs={[{ name: 'comp', label: 'Component', type: 'select', options: (parts.data ?? []).filter((p) => p.id !== part).map((p) => [p.id, lookupLabel(p)]) }, { name: 'rev', label: 'Component revision' }, { name: 'qty', label: 'Quantity', initial: '1' }, { name: 'op', label: 'Operation' },
          { name: 'sup', label: 'Supplier', type: 'select', options: [['', 'None'], ...(suppliers.data ?? []).map((p) => [p.id, lookupLabel(p)] as [string, string])] }]}
        onConfirm={async (v) => { await put(`/api/engineering/parts/${part}/bom?type=${tab}`, [...lines.map((l: any) => ({ componentPartId: l.componentPartId, componentRevision: l.componentRevision, quantity: l.quantity, operation: l.operation, supplierId: l.supplierId })), { componentPartId: v.comp, componentRevision: v.rev || null, quantity: Number(v.qty), operation: v.op || null, supplierId: v.sup || null }]); qc.invalidateQueries({ queryKey: ['bom', part] }); }} />
    </ModuleWorkspace>
  );
}

/* ---------- configuration: variants ---------- */
export function Variants() {
  const qc = useQueryClient(); const products = useLookup('product');
  const [product, setProduct] = useState(''); const [open, setOpen] = useState(false);
  const q = useQuery({ queryKey: ['variants', product], enabled: !!product, queryFn: () => get<any[]>(`/api/engineering/products/${product}/variants`) });
  const split = (s: string) => s.split(',').map((x) => x.trim()).filter(Boolean);
  return (
    <ModuleWorkspace module={mod('engineering')} crumbs={crumbs('Variants')} title="Product configuration and variants" actions={product ? <Button variant="contained" onClick={() => setOpen(true)}>New variant</Button> : undefined}>
      <TextField select size="small" label="Product" value={product} onChange={(e) => setProduct(e.target.value)} sx={{ minWidth: 320, mb: 2 }}>
        {(products.data ?? []).map((p) => <MenuItem key={p.id} value={p.id}>{lookupLabel(p)}</MenuItem>)}</TextField>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(3, 1fr)' }, gap: 1.5 }}>
        {(q.data ?? []).map((v) => <Paper key={v.id} sx={{ p: 1.5, borderTop: 3, borderColor: 'secondary.main' }}>
          <Typography sx={{ fontFamily: mono, fontWeight: 700 }}>{v.code} <Typography component="span" variant="caption">rev {v.revision}</Typography></Typography><Typography variant="body2">{v.name}</Typography>
          <Stack direction="row" gap={0.5} flexWrap="wrap" sx={{ my: 1 }}>{(v.options ?? []).map((o: string) => <Chip key={o} size="small" label={o} />)}{(v.features ?? []).map((o: string) => <Chip key={o} size="small" variant="outlined" label={o} />)}</Stack>
          {v.manufacturingImpact && <Typography variant="caption" color="text.secondary">Manufacturing impact: {v.manufacturingImpact}</Typography>}</Paper>)}
      </Box>
      <StepDialog open={open} onClose={() => setOpen(false)} title="New variant" confirm="Create"
        inputs={[{ name: 'code', label: 'Code' }, { name: 'name', label: 'Name' }, { name: 'opts', label: 'Options (comma separated)' }, { name: 'feat', label: 'Features (comma separated)' }, { name: 'impact', label: 'Manufacturing impact', type: 'multiline' }]}
        onConfirm={async (v) => { await post(`/api/engineering/products/${product}/variants`, { code: v.code, name: v.name, options: split(v.opts ?? ''), features: split(v.feat ?? ''), manufacturingImpact: v.impact || null }); qc.invalidateQueries({ queryKey: ['variants', product] }); }} />
    </ModuleWorkspace>
  );
}
