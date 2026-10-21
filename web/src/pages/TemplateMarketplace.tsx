import { setupOptions } from '../components/SetupLink';
import { useMemo, useRef, useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { Alert, Box, Button, Card, CardActions, CardContent, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, LinearProgress, Link, List, ListItemButton, ListItemText, MenuItem, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api, get, post, postBlob } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { FormView } from '../forms/FormView';
import { parseDefinition } from '../forms/definition';
import { mono } from '../theme';

interface Pkg { key: string; version: string; name: string; description: string; origin: number; integrity: number; categories: string[]; templateCount: number; installedCount: number; installedAt: string | null; packageId: string | null }
interface Tpl { key: string; title: string; category: string; categoryName: string; moduleKey: string; purpose: string; fieldCount: number; tableCount: number; signatureCount: number; packs: string[]; tags: string[]; installedDocumentId: string | null; installedNumber: string | null }
interface Cat { code: string; name: string; moduleKey: string; description: string; count: number }
interface Preview { key: string; title: string; category: string; purpose: string; scope: string; instructions: string; workflow: string; retention: string; definition: unknown }
interface Site { id: string; code: string; name: string }
interface Result { packageKey: string; version: string; installed: number; alreadyInstalled: number; templates: { key: string; title: string; documentId: string; number: string; alreadyInstalled: boolean }[] }
interface LibItem { id: string; number: string; title: string; category: string | null }

const ORIGIN = ['Built-in', 'Imported', 'Exported'];

/** Template marketplace: industry packs, the template library, install as draft form templates, package export and import. */
export function TemplateMarketplace() {
  const qc = useQueryClient();
  const { can } = useAuth();
  const [tab, setTab] = useState('packs');
  const [pack, setPack] = useState<Pkg | null>(null);
  const [cat, setCat] = useState<string>('');
  const [q, setQ] = useState('');
  const [search, setSearch] = useState('');
  const [picked, setPicked] = useState<string[]>([]);
  const [preview, setPreview] = useState<string | null>(null);
  const [install, setInstall] = useState<{ key: string; name: string; keys?: string[]; count: number } | null>(null);
  const [exportOpen, setExportOpen] = useState(false);
  const [msg, setMsg] = useState<{ ok: boolean; text: string; result?: Result } | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);

  const packs = useQuery({ queryKey: ['template-packages'], queryFn: () => get<Pkg[]>('/api/v1/template-packages') });
  const cats = useQuery({ queryKey: ['template-library-categories'], queryFn: () => get<Cat[]>('/api/v1/template-packages/library/categories') });
  const libQs = useMemo(() => { const n = new URLSearchParams(); if (cat) n.set('category', cat); if (search) n.set('q', search); return n.toString(); }, [cat, search]);
  const lib = useQuery({ queryKey: ['template-library-items', libQs], queryFn: () => get<Tpl[]>(`/api/v1/template-packages/library/templates?${libQs}`), enabled: tab === 'library' });
  const detail = useQuery({ queryKey: ['template-package', pack?.key, pack?.version], queryFn: () => get<{ package: Pkg; templates: Tpl[] }>(`/api/v1/template-packages/${pack!.key}?version=${encodeURIComponent(pack!.version)}`), enabled: !!pack });
  const refresh = () => { qc.invalidateQueries({ queryKey: ['template-packages'] }); qc.invalidateQueries({ queryKey: ['template-library-items'] }); qc.invalidateQueries({ queryKey: ['template-package'] }); qc.invalidateQueries({ queryKey: ['template-library'] }); };

  const importFile = async (f: File) => {
    setMsg(null);
    const fd = new FormData(); fd.append('file', f);
    try { const p = await api<Pkg>('POST', '/api/v1/template-packages/import', fd); setMsg({ ok: true, text: `Imported package ${p.name} ${p.version} with ${p.templateCount} template${p.templateCount === 1 ? '' : 's'}. Every definition matched its recorded hash and passed validation. Nothing is installed until you install it.` }); refresh(); }
    catch (e) { setMsg({ ok: false, text: (e as Error).message }); }
  };

  const tplTable = (rows: Tpl[], selectable: boolean) => (
    <Paper variant="outlined" sx={{ overflowX: 'auto' }}>
      <Table size="small" aria-label="Library templates">
        <TableHead><TableRow>
          {selectable && <TableCell padding="checkbox" />}
          <TableCell sx={{ fontWeight: 600 }}>Template</TableCell><TableCell sx={{ fontWeight: 600 }}>Category</TableCell>
          <TableCell sx={{ fontWeight: 600 }} align="right">Fields</TableCell><TableCell sx={{ fontWeight: 600 }} align="right">Tables</TableCell><TableCell sx={{ fontWeight: 600 }} align="right">Signatures</TableCell>
          <TableCell sx={{ fontWeight: 600 }}>Installed as</TableCell>
        </TableRow></TableHead>
        <TableBody>
          {rows.map((t) => (
            <TableRow key={t.key} hover sx={{ cursor: 'pointer' }} onClick={() => setPreview(t.key)}>
              {selectable && <TableCell padding="checkbox" onClick={(e) => e.stopPropagation()}>
                <Checkbox size="small" inputProps={{ 'aria-label': `Select ${t.title}` }} disabled={!!t.installedDocumentId} checked={picked.includes(t.key)} onChange={(e) => setPicked(e.target.checked ? [...picked, t.key] : picked.filter((k) => k !== t.key))} />
              </TableCell>}
              <TableCell><Typography variant="body2" fontWeight={600}>{t.title}</Typography>{t.purpose && <Typography variant="caption" color="text.secondary" sx={{ display: '-webkit-box', WebkitLineClamp: 2, WebkitBoxOrient: 'vertical', overflow: 'hidden', maxWidth: 560 }}>{t.purpose}</Typography>}</TableCell>
              <TableCell>{t.categoryName}</TableCell>
              <TableCell align="right">{t.fieldCount}</TableCell><TableCell align="right">{t.tableCount}</TableCell><TableCell align="right">{t.signatureCount}</TableCell>
              <TableCell onClick={(e) => e.stopPropagation()}>{t.installedDocumentId ? <Link component={RouterLink} to={`/forms/templates/${t.installedDocumentId}`} sx={{ fontFamily: mono }}>{t.installedNumber}</Link> : <Typography variant="body2" color="text.secondary">Not installed</Typography>}</TableCell>
            </TableRow>))}
          {!rows.length && <TableRow><TableCell colSpan={7}><Typography color="text.secondary">No templates.</Typography></TableCell></TableRow>}
        </TableBody>
      </Table>
    </Paper>
  );

  return (
    <ModuleWorkspace module={modules.find((m) => m.key === 'documents')!} crumbs={[{ label: 'Template library', to: '/forms/library' }, { label: 'Template marketplace' }]} title="Template marketplace"
      tabs={[{ label: 'Industry packs', value: 'packs' }, { label: 'Template library', value: 'library' }]} tab={tab} onTab={(v) => { setTab(v); setPicked([]); }}
      actions={can('templates.export') ? <Stack direction="row" gap={1}>
        <Button variant="outlined" onClick={() => fileRef.current?.click()}>Import package</Button>
        <Button variant="contained" onClick={() => setExportOpen(true)}>Export package</Button>
        <input ref={fileRef} type="file" hidden accept=".qgpkg,.zip" aria-label="Package file" onChange={(e) => { const f = e.target.files?.[0]; e.target.value = ''; if (f) importFile(f); }} />
      </Stack> : undefined}>
      {msg && <Alert severity={msg.ok ? 'success' : 'error'} sx={{ mb: 2 }} onClose={() => setMsg(null)}>{msg.text}
        {msg.result && <Box sx={{ mt: 1 }}>{msg.result.templates.slice(0, 12).map((t) => <Chip key={t.key} size="small" sx={{ mr: 0.5, mb: 0.5 }} component={RouterLink} to={`/forms/templates/${t.documentId}`} clickable label={`${t.number} · ${t.title}${t.alreadyInstalled ? ' (already installed)' : ''}`} />)}{msg.result.templates.length > 12 && <Typography variant="caption"> and {msg.result.templates.length - 12} more</Typography>}</Box>}
      </Alert>}

      {tab === 'packs' && <>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>Installing creates draft form templates under document control, filed by category and industry pack. Each one goes through review and approval before it can be used. Templates already installed are not duplicated.</Typography>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: '1fr 1fr', lg: '1fr 1fr 1fr' }, gap: 2 }}>
          {(packs.data ?? []).map((p) => (
            <Card key={`${p.key}-${p.version}-${p.origin}`} variant="outlined" sx={{ display: 'flex', flexDirection: 'column', borderColor: pack?.key === p.key && pack.version === p.version ? 'primary.main' : undefined }}>
              <CardContent sx={{ flex: 1 }}>
                <Stack direction="row" justifyContent="space-between" alignItems="flex-start" gap={1}>
                  <Typography variant="h6" component="h2">{p.name}</Typography>
                  <Chip size="small" label={ORIGIN[p.origin]} color={p.origin === 0 ? 'primary' : 'default'} variant={p.origin === 0 ? 'filled' : 'outlined'} />
                </Stack>
                <Typography variant="caption" color="text.secondary" sx={{ fontFamily: mono }}>{p.key} · {p.version}</Typography>
                <Typography variant="body2" sx={{ mt: 1 }}>{p.description}</Typography>
                {p.templateCount > 0 ? <Box sx={{ mt: 2 }}>
                  <Stack direction="row" justifyContent="space-between"><Typography variant="caption">{p.installedCount} of {p.templateCount} installed</Typography>{p.origin === 1 && <Typography variant="caption" color="success.main">Hashes verified</Typography>}</Stack>
                  <LinearProgress variant="determinate" value={Math.round((p.installedCount / p.templateCount) * 100)} sx={{ mt: 0.5, height: 6, borderRadius: 3 }} aria-label={`${p.name} installed`} />
                </Box> : <Alert severity="info" sx={{ mt: 2 }}>No templates are defined for this pack yet.</Alert>}
              </CardContent>
              <CardActions>
                <Button size="small" disabled={!p.templateCount} onClick={() => setPack(p)}>View templates</Button>
                {can('templates.install') && p.origin !== 2 && <Button size="small" variant="contained" disabled={!p.templateCount || p.installedCount >= p.templateCount} onClick={() => setInstall({ key: p.key, name: p.name, count: p.templateCount - p.installedCount })}>Install pack</Button>}
              </CardActions>
            </Card>))}
        </Box>
        {pack && <Box sx={{ mt: 3 }}>
          <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 1 }}>
            <Typography variant="h6" component="h2">{pack.name} templates</Typography>
            <Stack direction="row" gap={1}>
              {can('templates.install') && pack.origin !== 2 && <Button size="small" variant="outlined" disabled={!picked.length} onClick={() => setInstall({ key: pack.key, name: pack.name, keys: picked, count: picked.length })}>Install selected ({picked.length})</Button>}
              <Button size="small" onClick={() => { setPack(null); setPicked([]); }}>Close</Button>
            </Stack>
          </Stack>
          {detail.isLoading ? <LinearProgress /> : tplTable(detail.data?.templates ?? [], can('templates.install') && pack.origin !== 2)}
        </Box>}
      </>}

      {tab === 'library' && <Stack direction={{ xs: 'column', md: 'row' }} gap={2} alignItems="flex-start">
        <Paper variant="outlined" sx={{ width: { xs: '100%', md: 260 }, flexShrink: 0 }}>
          <List dense aria-label="Library categories">
            <ListItemButton selected={!cat} onClick={() => setCat('')}><ListItemText primary="All categories" secondary={`${(cats.data ?? []).reduce((a, c) => a + c.count, 0)} templates`} /></ListItemButton>
            {(cats.data ?? []).map((c) => <ListItemButton key={c.code} selected={cat === c.code} onClick={() => setCat(c.code)}><ListItemText primary={c.name} secondary={`${c.count} templates`} /></ListItemButton>)}
          </List>
        </Paper>
        <Box sx={{ flex: 1, minWidth: 0, width: '100%' }}>
          <Stack direction="row" gap={1} component="form" sx={{ mb: 2 }} onSubmit={(e) => { e.preventDefault(); setSearch(q.trim()); }}>
            <TextField size="small" label="Search the library" placeholder="Title, purpose or tag" value={q} onChange={(e) => setQ(e.target.value)} sx={{ width: 320 }} />
            <Button type="submit" variant="outlined">Search</Button>
            {can('templates.install') && <Button variant="contained" disabled={!picked.length} onClick={() => setInstall({ key: 'library', name: 'Template library', keys: picked, count: picked.length })}>Install selected ({picked.length})</Button>}
          </Stack>
          {lib.isLoading ? <LinearProgress /> : tplTable(lib.data ?? [], can('templates.install'))}
        </Box>
      </Stack>}

      {preview && <PreviewDialog keyName={preview} onClose={() => setPreview(null)} />}
      {install && <InstallDialog target={install} onClose={() => setInstall(null)} onDone={(r) => { setInstall(null); setPicked([]); refresh(); setMsg({ ok: true, text: `Installed ${r.installed} draft form template${r.installed === 1 ? '' : 's'}${r.alreadyInstalled ? `; ${r.alreadyInstalled} already installed` : ''}. Review and approve them in the template library before use.`, result: r }); }} />}
      {exportOpen && <ExportDialog onClose={() => setExportOpen(false)} onDone={(t) => { setExportOpen(false); refresh(); setMsg({ ok: true, text: t }); }} />}
    </ModuleWorkspace>
  );
}

function PreviewDialog({ keyName, onClose }: { keyName: string; onClose: () => void }) {
  const p = useQuery({ queryKey: ['template-preview', keyName], queryFn: () => get<Preview>(`/api/v1/template-packages/library/templates/${keyName}`) });
  const [values, setValues] = useState<Record<string, string | null>>({});
  const def = useMemo(() => p.data ? parseDefinition(p.data.definition as object) : null, [p.data]);
  return (
    <Dialog open onClose={onClose} maxWidth="xl" fullWidth aria-labelledby="tpl-preview-title">
      <DialogTitle id="tpl-preview-title">{p.data?.title ?? 'Template'}{p.data && <Typography variant="body2" color="text.secondary">{p.data.category}</Typography>}</DialogTitle>
      <DialogContent dividers>
        {!p.data ? <LinearProgress /> : <Stack direction={{ xs: 'column', lg: 'row' }} gap={3} alignItems="flex-start">
          <Box sx={{ width: { xs: '100%', lg: 340 }, flexShrink: 0 }}>
            {([['Purpose', p.data.purpose], ['Scope', p.data.scope], ['Instructions', p.data.instructions], ['Workflow', p.data.workflow], ['Retention', p.data.retention]] as const).map(([k, v]) =>
              <Box key={k} sx={{ mb: 1.5 }}><Typography variant="overline">{k}</Typography><Typography variant="body2">{v}</Typography></Box>)}
          </Box>
          <Paper variant="outlined" sx={{ flex: 1, minWidth: 0, p: 2, maxHeight: '70vh', overflow: 'auto' }} aria-label="Form preview">
            {def && <FormView def={def} values={values} onChange={setValues} readOnly={false} />}
          </Paper>
        </Stack>}
      </DialogContent>
      <DialogActions><Button onClick={onClose}>Close</Button></DialogActions>
    </Dialog>
  );
}

function InstallDialog({ target, onClose, onDone }: { target: { key: string; name: string; keys?: string[]; count: number }; onClose: () => void; onDone: (r: Result) => void }) {
  const sites = useQuery({ queryKey: ['sites'], queryFn: () => get<Site[]>('/api/organization/sites') });
  const [siteId, setSiteId] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const site = siteId || sites.data?.[0]?.id || '';
  const go = async () => {
    setBusy(true); setErr(null);
    try { onDone(await post<Result>(`/api/v1/template-packages/${target.key}/install`, { siteId: site, templateKeys: target.keys ?? null })); }
    catch (e) { setErr((e as Error).message); } finally { setBusy(false); }
  };
  return (
    <Dialog open onClose={onClose} maxWidth="sm" fullWidth aria-labelledby="install-title">
      <DialogTitle id="install-title">Install from {target.name}</DialogTitle>
      <DialogContent>
        {err && <Alert severity="error" sx={{ mb: 2 }}>{err}</Alert>}
        <Typography variant="body2" sx={{ mb: 2 }}>{target.count} template{target.count === 1 ? '' : 's'} will be created as draft form templates owned by you. Templates already installed are skipped.</Typography>
        <TextField select fullWidth label="Site" value={site} onChange={(e) => setSiteId(e.target.value)}>
          {(sites.data ?? []).map((s) => <MenuItem key={s.id} value={s.id}>{s.code} · {s.name}</MenuItem>)}
          {setupOptions('site', sites.data)}
        </TextField>
        {busy && <LinearProgress sx={{ mt: 2 }} />}
      </DialogContent>
      <DialogActions><Button onClick={onClose}>Cancel</Button><Button variant="contained" disabled={!site || busy} onClick={go}>Install</Button></DialogActions>
    </Dialog>
  );
}

function ExportDialog({ onClose, onDone }: { onClose: () => void; onDone: (text: string) => void }) {
  const [q, setQ] = useState('');
  const [search, setSearch] = useState('');
  const [ids, setIds] = useState<string[]>([]);
  const [key, setKey] = useState('');
  const [name, setName] = useState('');
  const [version, setVersion] = useState('1.0');
  const [description, setDescription] = useState('');
  const [err, setErr] = useState<string | null>(null);
  const list = useQuery({ queryKey: ['template-library', 'export', search], queryFn: () => get<{ items: LibItem[] }>(`/api/template-library?pageSize=100&q=${encodeURIComponent(search)}`) });
  const go = async () => {
    setErr(null);
    try {
      const blob = await postBlob('/api/v1/template-packages/export', { packageKey: key, name, version, description: description || null, documentIds: ids });
      const a = document.createElement('a'); a.href = URL.createObjectURL(blob); a.download = `${key}-${version}.qgpkg`; a.click(); URL.revokeObjectURL(a.href);
      onDone(`Exported ${ids.length} template${ids.length === 1 ? '' : 's'} as ${key} ${version}.`);
    } catch (e) { setErr((e as Error).message); }
  };
  return (
    <Dialog open onClose={onClose} maxWidth="md" fullWidth aria-labelledby="export-title">
      <DialogTitle id="export-title">Export template package</DialogTitle>
      <DialogContent>
        {err && <Alert severity="error" sx={{ mb: 2 }}>{err}</Alert>}
        <Stack direction={{ xs: 'column', sm: 'row' }} gap={2} sx={{ mt: 1, mb: 2 }}>
          <TextField label="Package key" value={key} onChange={(e) => setKey(e.target.value.toLowerCase())} helperText="Lowercase letters, numbers and hyphens" sx={{ flex: 1 }} />
          <TextField label="Name" value={name} onChange={(e) => setName(e.target.value)} sx={{ flex: 2 }} />
          <TextField label="Version" value={version} onChange={(e) => setVersion(e.target.value)} sx={{ width: 120 }} />
        </Stack>
        <TextField label="Description" value={description} onChange={(e) => setDescription(e.target.value)} fullWidth multiline minRows={2} sx={{ mb: 2 }} />
        <Stack direction="row" gap={1} component="form" sx={{ mb: 1 }} onSubmit={(e) => { e.preventDefault(); setSearch(q.trim()); }}>
          <TextField size="small" label="Find templates" value={q} onChange={(e) => setQ(e.target.value)} sx={{ width: 300 }} /><Button type="submit" variant="outlined">Search</Button>
          <Typography variant="body2" sx={{ alignSelf: 'center' }}>{ids.length} selected</Typography>
        </Stack>
        <Paper variant="outlined" sx={{ maxHeight: 300, overflow: 'auto' }}>
          <Table size="small" aria-label="Templates to export"><TableBody>
            {(list.data?.items ?? []).map((t) => <TableRow key={t.id} hover>
              <TableCell padding="checkbox"><Checkbox size="small" inputProps={{ 'aria-label': `Export ${t.number}` }} checked={ids.includes(t.id)} onChange={(e) => setIds(e.target.checked ? [...ids, t.id] : ids.filter((x) => x !== t.id))} /></TableCell>
              <TableCell sx={{ fontFamily: mono }}>{t.number}</TableCell><TableCell>{t.title}</TableCell><TableCell>{t.category ?? '—'}</TableCell>
            </TableRow>)}
          </TableBody></Table>
        </Paper>
      </DialogContent>
      <DialogActions><Button onClick={onClose}>Cancel</Button><Button variant="contained" disabled={!key || !name || !version || !ids.length} onClick={go}>Export</Button></DialogActions>
    </Dialog>
  );
}
