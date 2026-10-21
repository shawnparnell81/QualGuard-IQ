import { setupOptions } from '../components/SetupLink';
import { useState } from 'react';
import { Alert, Box, Button, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, IconButton, MenuItem, Paper, Stack, Tab, Table, TableBody, TableCell, TableHead, TableRow, Tabs, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { del, get, post, put } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { Section, lookupLabel, useLookup } from '../components/Kit';
import { SitePicker } from '../components/SitePicker';
import { fmtDate, fmtDateTime, localDate } from '../components/Status';
import { mono } from '../theme';
import { useCarriers } from '../warehouse/common';

const mod = modules.find((m) => m.key === 'shipping')!;
export const docKinds = ['Bill of lading', 'Packing list', 'Shipping manifest'];

interface DocSection { type: string; title?: string | null; columns?: string[] | null; text?: string | null; field?: string | null; signatures?: string[] | null; sizeMm?: number | null }
interface DocLayout { page: { size: string; orientation: string; marginMm: number }; header: { title: string; subtitle?: string | null; showLogo: boolean; logoWidthMm: number }; footer: { text?: string | null; pageNumbers: boolean }; sections: DocSection[] }
interface SectionInfo { type: string; label: string; kinds: number[]; columns?: string[] | null; fields?: string[] | null; usesText: boolean; usesSignatures: boolean; usesSize: boolean }
interface Catalog { sections: SectionInfo[]; placeholders: string[] }
interface TemplateRow { id: string; code: string; name: string; kind: number; description?: string; siteId?: string; site?: string; active: boolean; effectiveRevision?: number; pendingRevision?: number; draftRevision?: number;
  customer?: string; carrier?: string; priority?: number; canEdit: boolean; canApprove: boolean }
interface RevisionRow { id: string; revision: number; status: number; state: string; layout: DocLayout; hasLogo: boolean; customerId?: string; customer?: string; carrierId?: string; carrier?: string; priority: number; changeSummary?: string;
  createdAt: string; createdBy?: string; submittedAt?: string; submittedBy?: string; approvedAt?: string; approvedBy?: string; approvalNotes?: string; rejectedReason?: string; effectiveFrom?: string; contentHash?: string }
interface GeneratedRow { id: string; kind: number; reference: string; templateCode?: string; templateRevision?: number; builtIn: boolean; pages: number; pdfSha256: string; generatedAt: string; generatedBy?: string }
interface Choice { kind: number; templateId?: string; templateCode?: string; templateName?: string; revision?: number; builtIn: boolean; reason: string }

const stateColor = (s: string) => (s === 'Effective' ? 'success' : s === 'Pending approval' || s === 'Scheduled' ? 'warning' : s === 'Rejected' ? 'error' : 'default') as 'success' | 'warning' | 'error' | 'default';
const useCatalog = () => useQuery({ queryKey: ['document-sections'], queryFn: () => get<Catalog>('/api/shipping/document-sections'), staleTime: 600_000 });
type Msg = { ok: boolean; text: string } | null;

/** Shipping › Document templates: list, new template, manifest. */
export function ShippingDocumentTemplates() {
  const { can } = useAuth();
  const nav = useNavigate();
  const [tab, setTab] = useState(0);
  const [kind, setKind] = useState('');
  const list = useQuery({ queryKey: ['document-templates', kind], queryFn: () => get<TemplateRow[]>(`/api/shipping/document-templates${kind ? `?kind=${kind}` : ''}`) });
  const [dlg, setDlg] = useState(false);
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Document templates' }]} title="Shipping document templates"
      actions={can('shipping.edit') && tab === 0 && <Button variant="contained" onClick={() => setDlg(true)}>New template</Button>}>
      <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ mb: 2 }}><Tab label="Templates" /><Tab label="Manifest" /></Tabs>
      {tab === 0 && <Stack gap={2}>
        <Alert severity="info">A template applies only once a revision is approved by someone other than the person who prepared it, from its effective date. The most specific template wins: customer, then carrier, then site, then the lowest priority number. With no approved template the built-in layout is used. Every issued document records the revision used.</Alert>
        <TextField select size="small" label="Type" value={kind} onChange={(e) => setKind(e.target.value)} sx={{ maxWidth: 240 }}><MenuItem value="">All types</MenuItem>{docKinds.map((k, i) => <MenuItem key={k} value={String(i)}>{k}</MenuItem>)}</TextField>
        {list.error && <Alert severity="error">{(list.error as Error).message}</Alert>}
        <Paper><Table size="small" aria-label="Document templates"><TableHead><TableRow><TableCell>Code</TableCell><TableCell>Name</TableCell><TableCell>Type</TableCell><TableCell>Site</TableCell><TableCell>Applies to</TableCell><TableCell>Effective</TableCell><TableCell>Open revision</TableCell></TableRow></TableHead>
          <TableBody>{(list.data ?? []).map((t) => <TableRow key={t.id} hover sx={{ cursor: 'pointer', opacity: t.active ? 1 : 0.6 }} onClick={() => nav(`/shipping/document-templates/${t.id}`)}>
            <TableCell sx={{ fontFamily: mono }}><RouterLink to={`/shipping/document-templates/${t.id}`}>{t.code}</RouterLink></TableCell><TableCell>{t.name}{!t.active && ' (inactive)'}</TableCell><TableCell>{docKinds[t.kind]}</TableCell><TableCell>{t.site ?? 'All sites'}</TableCell>
            <TableCell>{t.effectiveRevision ? [t.customer && `Customer ${t.customer}`, t.carrier && `Carrier ${t.carrier}`, `priority ${t.priority}`].filter(Boolean).join(' · ') : '—'}</TableCell>
            <TableCell>{t.effectiveRevision ? `Rev ${t.effectiveRevision}` : 'None'}</TableCell>
            <TableCell>{t.pendingRevision ? <Chip size="small" color="warning" label={`Rev ${t.pendingRevision} pending approval`} /> : t.draftRevision ? <Chip size="small" label={`Rev ${t.draftRevision} draft`} /> : '—'}</TableCell></TableRow>)}
            {list.data?.length === 0 && <TableRow><TableCell colSpan={7}><Typography color="text.secondary">No templates yet. Shipping documents use the built-in layouts.</Typography></TableCell></TableRow>}</TableBody></Table></Paper>
      </Stack>}
      {tab === 1 && <ManifestPanel />}
      {dlg && <NewTemplateDialog onClose={() => setDlg(false)} onCreated={(id) => nav(`/shipping/document-templates/${id}`)} />}
    </ModuleWorkspace>
  );
}

function NewTemplateDialog({ onClose, onCreated }: { onClose: () => void; onCreated: (id: string) => void }) {
  const [v, setV] = useState({ code: '', name: '', kind: '1', description: '', siteId: '' });
  const [err, setErr] = useState<string | null>(null);
  const save = async () => {
    setErr(null);
    try { const r = await post<{ template: { id: string } }>('/api/shipping/document-templates', { code: v.code, name: v.name, kind: Number(v.kind), description: v.description || null, siteId: v.siteId || null }); onCreated(r.template.id); }
    catch (e) { setErr((e as Error).message); }
  };
  return <Dialog open onClose={onClose} maxWidth="sm" fullWidth><DialogTitle>New document template</DialogTitle>
    <DialogContent><Stack gap={2} sx={{ pt: 1 }}>
      {err && <Alert severity="error">{err}</Alert>}
      <TextField size="small" label="Code" value={v.code} onChange={(e) => setV({ ...v, code: e.target.value })} helperText="Letters, digits, - or _" />
      <TextField size="small" label="Name" value={v.name} onChange={(e) => setV({ ...v, name: e.target.value })} />
      <TextField select size="small" label="Document type" value={v.kind} onChange={(e) => setV({ ...v, kind: e.target.value })}>{docKinds.map((k, i) => <MenuItem key={k} value={String(i)}>{k}</MenuItem>)}</TextField>
      <SitePicker permission="shipping.edit" value={v.siteId} onChange={(id) => setV({ ...v, siteId: id ?? '' })} label="Site" helperText="Leave empty for an organization-wide template (needs the permission at every site)." />
      <TextField size="small" label="Description" value={v.description} onChange={(e) => setV({ ...v, description: e.target.value })} multiline minRows={2} />
      <Typography variant="body2" color="text.secondary">Revision 1 starts as a draft copy of the built-in layout.</Typography>
    </Stack></DialogContent>
    <DialogActions><Button onClick={onClose}>Cancel</Button><Button variant="contained" onClick={save}>Create</Button></DialogActions></Dialog>;
}

function ManifestPanel() {
  const carriers = useCarriers();
  const [v, setV] = useState({ siteId: '', carrierId: '', date: localDate() });
  const issued = useQuery({ queryKey: ['manifests', v.siteId], enabled: !!v.siteId, queryFn: () => get<GeneratedRow[]>(`/api/shipping/generated-documents?siteId=${v.siteId}&kind=2`) });
  const [err, setErr] = useState<string | null>(null);
  const qc = useQueryClient();
  const generate = async () => {
    setErr(null);
    const r = await fetch(`/api/shipping/manifest?siteId=${v.siteId}&carrierId=${v.carrierId}&date=${v.date}`, { credentials: 'same-origin' });
    if (!r.ok) { const p = await r.json().catch(() => ({})); setErr(p.title ?? `Request failed (${r.status})`); return; }
    window.open(URL.createObjectURL(await r.blob()), '_blank');
    qc.invalidateQueries({ queryKey: ['manifests', v.siteId] });
  };
  return <Stack gap={2}>
    <Section title="Carrier manifest">
      <Stack gap={1.5}>
        <Typography variant="body2" color="text.secondary">Lists every shipment shipped with one carrier from one site on one day (site time), for the driver to sign at pickup.</Typography>
        {err && <Alert severity="error">{err}</Alert>}
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: '2fr 1fr 1fr auto' }, gap: 1.5, alignItems: 'start' }}>
          <SitePicker permission="shipping.view" value={v.siteId} onChange={(id) => setV({ ...v, siteId: id ?? '' })} label="Site" required />
          <TextField select size="small" label="Carrier" value={v.carrierId} onChange={(e) => setV({ ...v, carrierId: e.target.value })}>{(carriers.data ?? []).map((c) => <MenuItem key={c.id} value={c.id}>{c.name}</MenuItem>)}{setupOptions('carrier', carriers.data)}</TextField>
          <TextField size="small" type="date" label="Ship date" value={v.date} onChange={(e) => setV({ ...v, date: e.target.value })} slotProps={{ inputLabel: { shrink: true } }} />
          <Button variant="contained" disabled={!v.siteId || !v.carrierId || !v.date} onClick={generate}>Generate manifest</Button>
        </Box>
      </Stack>
    </Section>
    {v.siteId && <IssuedTable rows={issued.data ?? []} title="Issued manifests" />}
  </Stack>;
}

function IssuedTable({ rows, title }: { rows: GeneratedRow[]; title: string }) {
  const [rep, setRep] = useState<Record<string, string>>({});
  const reproduce = async (id: string) => {
    try { const r = await post<{ identical: boolean }>(`/api/shipping/generated-documents/${id}/reproduce`); setRep({ ...rep, [id]: r.identical ? 'Reproduced: identical' : 'Reproduced: DIFFERENT' }); }
    catch (e) { setRep({ ...rep, [id]: (e as Error).message }); }
  };
  return <Section title={title}>
    <Box sx={{ overflowX: 'auto' }}><Table size="small" aria-label={title}><TableHead><TableRow><TableCell>Document</TableCell><TableCell>Reference</TableCell><TableCell>Template</TableCell><TableCell>Pages</TableCell><TableCell>Issued</TableCell><TableCell>SHA-256</TableCell><TableCell /></TableRow></TableHead>
      <TableBody>{rows.map((g) => <TableRow key={g.id}><TableCell>{docKinds[g.kind]}</TableCell><TableCell sx={{ fontFamily: mono }}>{g.reference}</TableCell>
        <TableCell>{g.builtIn ? 'Built-in layout' : `${g.templateCode} rev ${g.templateRevision}`}</TableCell><TableCell>{g.pages}</TableCell><TableCell>{fmtDateTime(g.generatedAt)}{g.generatedBy ? ` · ${g.generatedBy}` : ''}</TableCell>
        <TableCell sx={{ fontFamily: mono, fontSize: 12 }} title={g.pdfSha256}>{g.pdfSha256.slice(0, 12)}…</TableCell>
        <TableCell><Stack direction="row" gap={1} alignItems="center"><Button size="small" href={`/api/shipping/generated-documents/${g.id}`} target="_blank">Open</Button><Button size="small" onClick={() => reproduce(g.id)}>Reproduce</Button>
          {rep[g.id] && <Typography variant="caption" color={rep[g.id].endsWith('identical') ? 'success.main' : 'error'}>{rep[g.id]}</Typography>}</Stack></TableCell></TableRow>)}
        {rows.length === 0 && <TableRow><TableCell colSpan={7}><Typography color="text.secondary">None issued yet.</Typography></TableCell></TableRow>}</TableBody></Table></Box>
  </Section>;
}

/** Shipping › Document templates › one template: revisions, editor, preview, approval. */
export function ShippingDocumentTemplate() {
  const { id } = useParams();
  const qc = useQueryClient();
  const q = useQuery({ queryKey: ['document-template', id], queryFn: () => get<{ template: TemplateRow; revisions: RevisionRow[] }>(`/api/shipping/document-templates/${id}`) });
  const [sel, setSel] = useState<string | null>(null);
  const [msg, setMsg] = useState<Msg>(null);
  const [edit, setEdit] = useState(false);
  const refresh = () => { qc.invalidateQueries({ queryKey: ['document-template', id] }); qc.invalidateQueries({ queryKey: ['document-templates'] }); };
  const run = async (f: () => Promise<unknown>, ok: string) => { setMsg(null); try { await f(); setMsg({ ok: true, text: ok }); refresh(); } catch (e) { setMsg({ ok: false, text: (e as Error).message }); } };
  if (!q.data) return <ModuleWorkspace module={mod} crumbs={[{ label: 'Document templates', to: '/shipping/document-templates' }]} title="Template">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const { template: t, revisions } = q.data;
  const rev = revisions.find((r) => r.id === sel) ?? revisions.find((r) => r.status <= 1) ?? revisions[0];
  const open = revisions.some((r) => r.status <= 1);
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Document templates', to: '/shipping/document-templates' }, { label: t.code }]} title={`${t.code} — ${t.name}`}
      actions={t.canEdit && <Stack direction="row" gap={1}><Button onClick={() => setEdit(true)}>Edit details</Button>
        {!open && <Button variant="contained" onClick={() => run(async () => { const r = await post<{ id: string }>(`/api/shipping/document-templates/${t.id}/revisions`); setSel(r.id); }, 'New draft revision started.')}>New revision</Button>}</Stack>}>
      {msg && <Alert severity={msg.ok ? 'success' : 'error'} sx={{ mb: 2 }} onClose={() => setMsg(null)}>{msg.text}</Alert>}
      <Stack direction="row" gap={1} sx={{ mb: 2 }} flexWrap="wrap"><Chip label={docKinds[t.kind]} /><Chip variant="outlined" label={t.site ?? 'All sites'} />{!t.active && <Chip color="default" label="Inactive" />}
        <Chip variant="outlined" color={t.effectiveRevision ? 'success' : 'default'} label={t.effectiveRevision ? `Effective: rev ${t.effectiveRevision}` : 'No effective revision (built-in layout is used)'} /></Stack>
      {t.description && <Typography sx={{ mb: 2 }} color="text.secondary">{t.description}</Typography>}
      <Paper sx={{ mb: 2 }}><Table size="small" aria-label="Revisions"><TableHead><TableRow><TableCell>Rev</TableCell><TableCell>State</TableCell><TableCell>Change</TableCell><TableCell>Applies to</TableCell><TableCell>Effective from</TableCell><TableCell>Prepared</TableCell><TableCell>Approved</TableCell></TableRow></TableHead>
        <TableBody>{revisions.map((r) => <TableRow key={r.id} hover selected={r.id === rev.id} sx={{ cursor: 'pointer' }} onClick={() => setSel(r.id)}>
          <TableCell>{r.revision}</TableCell><TableCell><Chip size="small" color={stateColor(r.state)} label={r.state} /></TableCell><TableCell>{r.changeSummary}{r.rejectedReason ? ` — rejected: ${r.rejectedReason}` : ''}</TableCell>
          <TableCell>{[r.customer && `Customer ${r.customer}`, r.carrier && `Carrier ${r.carrier}`, `priority ${r.priority}`].filter(Boolean).join(' · ')}</TableCell>
          <TableCell>{r.effectiveFrom ? fmtDateTime(r.effectiveFrom) : '—'}</TableCell><TableCell>{r.createdBy} {fmtDate(r.createdAt)}</TableCell>
          <TableCell>{r.approvedBy ? `${r.approvedBy} ${fmtDate(r.approvedAt!)}` : '—'}{r.approvalNotes ? <Typography variant="caption" display="block">{r.approvalNotes}</Typography> : null}</TableCell></TableRow>)}</TableBody></Table></Paper>
      <RevisionPanel key={rev.id} t={t} r={rev} run={run} />
      {edit && <EditTemplateDialog t={t} onClose={() => setEdit(false)} onSaved={() => { setEdit(false); refresh(); }} />}
    </ModuleWorkspace>
  );
}

function EditTemplateDialog({ t, onClose, onSaved }: { t: TemplateRow; onClose: () => void; onSaved: () => void }) {
  const [v, setV] = useState({ name: t.name, description: t.description ?? '', active: t.active });
  const [err, setErr] = useState<string | null>(null);
  return <Dialog open onClose={onClose} maxWidth="sm" fullWidth><DialogTitle>Edit {t.code}</DialogTitle>
    <DialogContent><Stack gap={2} sx={{ pt: 1 }}>{err && <Alert severity="error">{err}</Alert>}
      <TextField size="small" label="Name" value={v.name} onChange={(e) => setV({ ...v, name: e.target.value })} />
      <TextField size="small" label="Description" value={v.description} onChange={(e) => setV({ ...v, description: e.target.value })} multiline minRows={2} />
      <FormControlLabel control={<Checkbox checked={v.active} onChange={(e) => setV({ ...v, active: e.target.checked })} />} label="Active (used when it matches a shipment)" />
    </Stack></DialogContent>
    <DialogActions><Button onClick={onClose}>Cancel</Button><Button variant="contained" onClick={async () => { try { await put(`/api/shipping/document-templates/${t.id}`, { name: v.name, description: v.description || null, active: v.active }); onSaved(); } catch (e) { setErr((e as Error).message); } }}>Save</Button></DialogActions></Dialog>;
}

function RevisionPanel({ t, r, run }: { t: TemplateRow; r: RevisionRow; run: (f: () => Promise<unknown>, ok: string) => Promise<void> }) {
  const cat = useCatalog();
  const customers = useLookup('customer');
  const carriers = useCarriers();
  const draft = r.status === 0 && t.canEdit;
  const [layout, setLayout] = useState<DocLayout>(r.layout);
  const [rule, setRule] = useState({ customerId: r.customerId ?? '', carrierId: r.carrierId ?? '', priority: String(r.priority), changeSummary: r.changeSummary ?? '', effectiveFrom: r.effectiveFrom ? r.effectiveFrom.slice(0, 10) : '' });
  const [decide, setDecide] = useState<'approve' | 'reject' | null>(null);
  const [logoKey, setLogoKey] = useState(0);
  const body = () => ({ layout, customerId: rule.customerId || null, carrierId: rule.carrierId || null, priority: Number(rule.priority), changeSummary: rule.changeSummary || null, effectiveFrom: rule.effectiveFrom ? new Date(rule.effectiveFrom + 'T00:00:00').toISOString() : null });
  const save = () => put(`/api/shipping/document-template-revisions/${r.id}`, body());
  const sections = (cat.data?.sections ?? []).filter((s) => s.kinds.includes(t.kind));
  const setSec = (i: number, s: Partial<DocSection>) => setLayout({ ...layout, sections: layout.sections.map((x, j) => (j === i ? { ...x, ...s } : x)) });
  const move = (i: number, d: number) => { const a = [...layout.sections]; const [x] = a.splice(i, 1); a.splice(i + d, 0, x); setLayout({ ...layout, sections: a }); };
  const upload = async (f: File) => { const fd = new FormData(); fd.append('file', f); await post(`/api/shipping/document-template-revisions/${r.id}/logo`, fd); setLogoKey(logoKey + 1); };
  return <Stack gap={2}>
    <Section title={`Revision ${r.revision} · ${r.state}`} action={<Button variant="outlined" href={`/api/shipping/document-template-revisions/${r.id}/preview`} target="_blank">Preview (sample data)</Button>}>
      <Stack gap={2}>
        {draft && <Typography variant="body2" color="text.secondary">Save the draft before previewing. Placeholders: {(cat.data?.placeholders ?? []).map((p) => `{${p}}`).join(' ')}</Typography>}
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: 'repeat(4, 1fr)' }, gap: 1.5 }}>
          {t.kind !== 2 && <TextField select size="small" label="Customer" disabled={!draft} value={rule.customerId} onChange={(e) => setRule({ ...rule, customerId: e.target.value })}><MenuItem value="">Any customer</MenuItem>{r.customerId && !(customers.data ?? []).some((c) => c.id === r.customerId) && <MenuItem value={r.customerId}>{r.customer}</MenuItem>}{(customers.data ?? []).map((c) => <MenuItem key={c.id} value={c.id}>{lookupLabel(c)}</MenuItem>)}{setupOptions('customer', customers.data)}</TextField>}
          <TextField select size="small" label="Carrier" disabled={!draft} value={rule.carrierId} onChange={(e) => setRule({ ...rule, carrierId: e.target.value })}><MenuItem value="">Any carrier</MenuItem>{r.carrierId && !(carriers.data ?? []).some((c) => c.id === r.carrierId) && <MenuItem value={r.carrierId}>{r.carrier}</MenuItem>}{(carriers.data ?? []).map((c) => <MenuItem key={c.id} value={c.id}>{c.name}</MenuItem>)}{setupOptions('carrier', carriers.data)}</TextField>
          <TextField size="small" type="number" label="Priority" disabled={!draft} value={rule.priority} onChange={(e) => setRule({ ...rule, priority: e.target.value })} helperText="1 is tried first" />
          <TextField size="small" type="date" label="Effective from" disabled={!draft} value={rule.effectiveFrom} onChange={(e) => setRule({ ...rule, effectiveFrom: e.target.value })} slotProps={{ inputLabel: { shrink: true } }} helperText="Empty = on approval" />
        </Box>
        <TextField size="small" label="Change summary" disabled={!draft} value={rule.changeSummary} onChange={(e) => setRule({ ...rule, changeSummary: e.target.value })} />
        <Typography variant="subtitle2">Page, header and footer</Typography>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: 'repeat(4, 1fr)' }, gap: 1.5 }}>
          <TextField select size="small" label="Page size" disabled={!draft} value={layout.page.size} onChange={(e) => setLayout({ ...layout, page: { ...layout.page, size: e.target.value } })}><MenuItem value="Letter">Letter</MenuItem><MenuItem value="A4">A4</MenuItem></TextField>
          <TextField select size="small" label="Orientation" disabled={!draft} value={layout.page.orientation} onChange={(e) => setLayout({ ...layout, page: { ...layout.page, orientation: e.target.value } })}><MenuItem value="portrait">Portrait</MenuItem><MenuItem value="landscape">Landscape</MenuItem></TextField>
          <TextField size="small" type="number" label="Margin (mm)" disabled={!draft} value={layout.page.marginMm} onChange={(e) => setLayout({ ...layout, page: { ...layout.page, marginMm: Number(e.target.value) } })} />
          <TextField size="small" type="number" label="Logo width (mm)" disabled={!draft} value={layout.header.logoWidthMm} onChange={(e) => setLayout({ ...layout, header: { ...layout.header, logoWidthMm: Number(e.target.value) } })} />
          <TextField size="small" label="Header title" disabled={!draft} value={layout.header.title} onChange={(e) => setLayout({ ...layout, header: { ...layout.header, title: e.target.value } })} sx={{ gridColumn: { md: 'span 2' } }} />
          <TextField size="small" label="Header subtitle" disabled={!draft} value={layout.header.subtitle ?? ''} onChange={(e) => setLayout({ ...layout, header: { ...layout.header, subtitle: e.target.value || null } })} sx={{ gridColumn: { md: 'span 2' } }} />
          <TextField size="small" label="Footer text" disabled={!draft} value={layout.footer.text ?? ''} onChange={(e) => setLayout({ ...layout, footer: { ...layout.footer, text: e.target.value || null } })} sx={{ gridColumn: { md: 'span 2' } }} />
          <FormControlLabel control={<Checkbox disabled={!draft} checked={layout.footer.pageNumbers} onChange={(e) => setLayout({ ...layout, footer: { ...layout.footer, pageNumbers: e.target.checked } })} />} label="Page numbers" />
          <FormControlLabel control={<Checkbox disabled={!draft} checked={layout.header.showLogo} onChange={(e) => setLayout({ ...layout, header: { ...layout.header, showLogo: e.target.checked } })} />} label="Show logo" />
        </Box>
        <Stack direction="row" gap={1.5} alignItems="center">
          {r.hasLogo || logoKey > 0 ? <img key={logoKey} src={`/api/shipping/document-template-revisions/${r.id}/logo?v=${logoKey}`} alt="Template logo" style={{ maxHeight: 48, border: '1px solid #ddd' }} /> : <Typography variant="body2" color="text.secondary">No logo.</Typography>}
          {draft && <Button component="label" size="small" variant="outlined">Upload logo (JPEG)<input hidden type="file" accept="image/jpeg" aria-label="Logo file" onChange={(e) => { const f = e.target.files?.[0]; if (f) run(() => upload(f), 'Logo uploaded.'); }} /></Button>}
          {draft && (r.hasLogo || logoKey > 0) && <Button size="small" onClick={() => run(async () => { await del(`/api/shipping/document-template-revisions/${r.id}/logo`); setLogoKey(0); }, 'Logo removed.')}>Remove logo</Button>}
        </Stack>
        <Typography variant="subtitle2">Sections (printed in this order)</Typography>
        <Stack gap={1} aria-label="Sections">
          {layout.sections.map((s, i) => { const info = sections.find((x) => x.type === s.type);
            return <Paper key={i} variant="outlined" sx={{ p: 1.5 }} data-testid={`section-${i}`}><Stack gap={1}>
              <Stack direction="row" alignItems="center" gap={1}><Typography sx={{ flex: 1 }} fontWeight={600}>{i + 1}. {info?.label ?? s.type}</Typography>
                {draft && <><IconButton size="small" aria-label={`Move section ${i + 1} up`} disabled={i === 0} onClick={() => move(i, -1)}>↑</IconButton><IconButton size="small" aria-label={`Move section ${i + 1} down`} disabled={i === layout.sections.length - 1} onClick={() => move(i, 1)}>↓</IconButton>
                  <Button size="small" color="error" onClick={() => setLayout({ ...layout, sections: layout.sections.filter((_, j) => j !== i) })}>Remove</Button></>}</Stack>
              <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: '1fr 2fr' }, gap: 1 }}>
                <TextField size="small" label="Section title" disabled={!draft} value={s.title ?? ''} onChange={(e) => setSec(i, { title: e.target.value || null })} placeholder="Default title" />
                {info?.usesText && <TextField size="small" label={s.type === 'qr' ? 'QR content' : s.type === 'freightTerms' ? 'Default terms (when the shipment has none)' : 'Text'} disabled={!draft} value={s.text ?? ''} onChange={(e) => setSec(i, { text: e.target.value || null })} />}
                {info?.fields && <TextField select size="small" label="Value to encode" disabled={!draft} value={s.field ?? ''} onChange={(e) => setSec(i, { field: e.target.value })}>{info.fields.map((f) => <MenuItem key={f} value={f}>{f}</MenuItem>)}</TextField>}
                {info?.usesSignatures && <TextField size="small" label="Signature blocks (comma separated)" disabled={!draft} value={(s.signatures ?? []).join(', ')} onChange={(e) => setSec(i, { signatures: e.target.value.split(',').map((x) => x.trim()).filter(Boolean) })} />}
              </Box>
              {info?.columns && <Stack direction="row" gap={0.5} flexWrap="wrap">{info.columns.map((c) => { const on = (s.columns?.length ? s.columns : info.columns!).includes(c);
                return <Chip key={c} size="small" label={c} color={on ? 'primary' : 'default'} variant={on ? 'filled' : 'outlined'} disabled={!draft}
                  onClick={() => { const cur = s.columns?.length ? s.columns : info.columns!; setSec(i, { columns: on ? cur.filter((x) => x !== c) : info.columns!.filter((x) => x === c || cur.includes(x)) }); }} />; })}</Stack>}
            </Stack></Paper>; })}
          {draft && <TextField select size="small" label="Add section" value="" onChange={(e) => { const info = sections.find((x) => x.type === e.target.value)!;
            setLayout({ ...layout, sections: [...layout.sections, { type: info.type, field: info.fields?.[0] ?? null, signatures: info.usesSignatures ? ['Shipper', 'Carrier'] : null, text: info.type === 'qr' ? '{shipmentNumber}' : info.type === 'text' ? 'Text' : null }] }); }} sx={{ maxWidth: 360 }}>
            {sections.map((s) => <MenuItem key={s.type} value={s.type}>{s.label}</MenuItem>)}</TextField>}
        </Stack>
        <Stack direction="row" gap={1}>
          {draft && <Button variant="contained" onClick={() => run(save, 'Draft saved.')}>Save draft</Button>}
          {draft && <Button variant="outlined" onClick={() => run(async () => { await save(); await post(`/api/shipping/document-template-revisions/${r.id}/submit`); }, 'Submitted for approval.')}>Submit for approval</Button>}
          {r.status === 1 && t.canApprove && <><Button variant="contained" color="success" onClick={() => setDecide('approve')}>Approve</Button><Button color="error" onClick={() => setDecide('reject')}>Reject</Button></>}
        </Stack>
        {r.contentHash && <Typography variant="caption" color="text.secondary" sx={{ fontFamily: mono }}>Approved content SHA-256 {r.contentHash}</Typography>}
      </Stack>
    </Section>
    {decide && <DecideDialog mode={decide} onClose={() => setDecide(null)} onConfirm={(text, date) => run(async () => {
      if (decide === 'approve') await post(`/api/shipping/document-template-revisions/${r.id}/approve`, { notes: text, effectiveFrom: date ? new Date(date + 'T00:00:00').toISOString() : null });
      else await post(`/api/shipping/document-template-revisions/${r.id}/reject`, { reason: text });
      setDecide(null);
    }, decide === 'approve' ? 'Revision approved.' : 'Revision rejected.')} />}
  </Stack>;
}

function DecideDialog({ mode, onClose, onConfirm }: { mode: 'approve' | 'reject'; onClose: () => void; onConfirm: (text: string, date: string) => void }) {
  const [text, setText] = useState(''); const [date, setDate] = useState('');
  return <Dialog open onClose={onClose} maxWidth="sm" fullWidth><DialogTitle>{mode === 'approve' ? 'Approve revision' : 'Reject revision'}</DialogTitle>
    <DialogContent><Stack gap={2} sx={{ pt: 1 }}>
      {mode === 'approve' && <Typography variant="body2">Preview the revision first. Once approved it cannot be changed; it applies from the effective date.</Typography>}
      <TextField size="small" label={mode === 'approve' ? 'Approval notes' : 'Reason'} value={text} onChange={(e) => setText(e.target.value)} multiline minRows={2} />
      {mode === 'approve' && <TextField size="small" type="date" label="Effective from (optional)" value={date} onChange={(e) => setDate(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />}
    </Stack></DialogContent>
    <DialogActions><Button onClick={onClose}>Cancel</Button><Button variant="contained" onClick={() => onConfirm(text, date)}>{mode === 'approve' ? 'Approve' : 'Reject'}</Button></DialogActions></Dialog>;
}

/** Shipment › Ship & documents: template used per document, freight terms and instructions, issued documents. */
export function ShipmentDocumentsPanel({ shipmentId, status, edit, freightTerms, specialInstructions, onChanged }: { shipmentId: string; status: number; edit: boolean; freightTerms?: string | null; specialInstructions?: string | null; onChanged: () => void }) {
  const qc = useQueryClient();
  const choices = useQuery({ queryKey: ['shipment-doc-choices', shipmentId], queryFn: () => get<Choice[]>(`/api/shipping/shipments/${shipmentId}/document-templates`) });
  const issued = useQuery({ queryKey: ['shipment-docs', shipmentId], queryFn: () => get<GeneratedRow[]>(`/api/shipping/shipments/${shipmentId}/documents`) });
  const [terms, setTerms] = useState({ freightTerms: freightTerms ?? '', specialInstructions: specialInstructions ?? '' });
  const [msg, setMsg] = useState<Msg>(null);
  // links (not script downloads) so the browser opens the PDF; the issued list refreshes once the document has been recorded
  const issuedSoon = () => { for (const ms of [1200, 3500]) setTimeout(() => qc.invalidateQueries({ queryKey: ['shipment-docs', shipmentId] }), ms); };
  const live = status < 5 && status !== 7;
  return <Stack gap={2}>
    {msg && <Alert severity={msg.ok ? 'success' : 'error'} onClose={() => setMsg(null)}>{msg.text}</Alert>}
    <Stack direction="row" gap={1} flexWrap="wrap">
      <Button variant="outlined" href={`/api/shipping/shipments/${shipmentId}/packing-list`} target="_blank" onClick={issuedSoon}>Packing list (PDF)</Button>
      <Button variant="outlined" href={`/api/shipping/shipments/${shipmentId}/bill-of-lading`} target="_blank" onClick={issuedSoon}>Bill of lading (PDF)</Button>
    </Stack>
    <Box>{(choices.data ?? []).map((c) => <Typography key={c.kind} variant="body2" color="text.secondary">{docKinds[c.kind]}: {c.builtIn ? 'built-in layout' : <><b>{c.templateCode}</b> rev {c.revision} ({c.templateName})</>} — {c.reason}</Typography>)}</Box>
    {edit && live ? <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: '1fr 2fr auto' }, gap: 1.5, alignItems: 'start' }}>
      <TextField size="small" label="Freight terms" value={terms.freightTerms} onChange={(e) => setTerms({ ...terms, freightTerms: e.target.value })} placeholder="Prepaid, Collect, Third party" />
      <TextField size="small" label="Special instructions" value={terms.specialInstructions} onChange={(e) => setTerms({ ...terms, specialInstructions: e.target.value })} multiline />
      <Button variant="outlined" onClick={async () => { setMsg(null); try { await put(`/api/shipping/shipments/${shipmentId}/shipping-terms`, { freightTerms: terms.freightTerms || null, specialInstructions: terms.specialInstructions || null }); setMsg({ ok: true, text: 'Shipping terms saved.' }); onChanged(); } catch (e) { setMsg({ ok: false, text: (e as Error).message }); } }}>Save terms</Button>
    </Box> : (freightTerms || specialInstructions) && <Typography variant="body2">Freight terms: {freightTerms ?? '—'} · Instructions: {specialInstructions ?? '—'}</Typography>}
    <IssuedTable rows={issued.data ?? []} title="Issued documents" />
  </Stack>;
}
