import { useMemo, useState } from 'react';
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, IconButton, List, ListItemButton, ListItemText, MenuItem, Pagination, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Tooltip, Typography } from '@mui/material';
import StarIcon from '@mui/icons-material/Star';
import StarBorderIcon from '@mui/icons-material/StarBorder';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { get, post, put, del } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { StatusChip, docStatus, revStatus, fmtDate } from '../components/Status';
import { mono } from '../theme';

interface Item { id: string; number: string; title: string; category: string | null; categoryId: string | null; module: string | null; industryPack: string | null; siteCode: string; ownerName: string;
  status: number; currentRevision: string | null; currentRevisionStatus: number | null; effectiveRevision: string | null; effectiveDate: string | null; reviewDate: string | null;
  source: number; confidentiality: string; favorite: boolean; lastUsed: string | null; usage: number }
interface Cat { id: string; code: string; name: string; parentId: string | null; count: number; isActive: boolean }
interface SavedView { id: string; name: string; settings: string; isDefault: boolean }
interface Rev { id: string; revision: string; status: number }

const VIEWS: [string, string][] = [['library', 'Template library'], ['mine', 'My templates'], ['organization', 'Organization templates'], ['site', 'Site templates'],
  ['industry', 'Industry packs'], ['imported', 'Imported templates'], ['draft', 'Draft templates'], ['review', 'Templates under review'], ['effective', 'Effective templates'], ['retired', 'Retired templates']];
const SOURCES = ['Native', 'Excel', 'Word', 'CSV', 'PDF', 'Package', 'Clone'];
const COLUMNS: [string, string][] = [['category', 'Category'], ['revision', 'Revision'], ['effective', 'Effective'], ['review', 'Review date'], ['site', 'Site'], ['owner', 'Owner'],
  ['source', 'Source'], ['industry', 'Industry pack'], ['lastUsed', 'Last used'], ['usage', 'Uses'], ['confidentiality', 'Confidentiality']];
const DEFAULT_COLS = ['category', 'revision', 'effective', 'review', 'site', 'owner', 'lastUsed'];

export function TemplateLibrary() {
  const nav = useNavigate();
  const qc = useQueryClient();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const view = p.get('view') ?? 'library';
  const page = Number(p.get('page') ?? 1);
  const [q, setQ] = useState(p.get('q') ?? '');
  const [cols, setCols] = useState<string[]>(() => { try { return JSON.parse(localStorage.getItem('tl.cols') ?? '') } catch { return DEFAULT_COLS; } });
  const [colDlg, setColDlg] = useState(false);
  const [saveDlg, setSaveDlg] = useState(false);
  const [viewName, setViewName] = useState('');
  const [selected, setSelected] = useState<Item | null>(null);
  const set = (k: string, v: string | null) => { const n = new URLSearchParams(p); if (v) n.set(k, v); else n.delete(k); if (k !== 'page') n.delete('page'); setP(n); };
  const qs = useMemo(() => { const n = new URLSearchParams(p); n.set('pageSize', '25'); return n.toString(); }, [p]);
  const list = useQuery({ queryKey: ['template-library', qs], queryFn: () => get<{ items: Item[]; total: number; pageSize: number }>(`/api/template-library?${qs}`) });
  const cats = useQuery({ queryKey: ['template-categories'], queryFn: () => get<Cat[]>('/api/template-library/categories') });
  const views = useQuery({ queryKey: ['template-views'], queryFn: () => get<SavedView[]>('/api/template-library/views') });
  const toggleFav = async (it: Item) => { await put(`/api/template-library/${it.id}/favorite`, { favorite: !it.favorite }); qc.invalidateQueries({ queryKey: ['template-library'] }); };
  const applyView = (v: SavedView) => { const s = JSON.parse(v.settings) as { query?: string; cols?: string[] }; setP(new URLSearchParams(s.query ?? '')); if (s.cols) setCols(s.cols); };
  const show = (c: string) => cols.includes(c);
  const sortBy = (k: string) => { const n = new URLSearchParams(p); if (n.get('sort') === k) n.set('desc', n.get('desc') === 'true' ? 'false' : 'true'); else { n.set('sort', k); n.delete('desc'); } setP(n); };
  const Head = ({ k, label }: { k?: string; label: string }) => <TableCell onClick={k ? () => sortBy(k) : undefined} sx={{ cursor: k ? 'pointer' : undefined, fontWeight: 600, whiteSpace: 'nowrap' }}>{label}{k && p.get('sort') === k ? (p.get('desc') === 'true' ? ' ▼' : ' ▲') : ''}</TableCell>;

  return (
    <ModuleWorkspace module={modules.find((m) => m.key === 'documents')!} crumbs={[{ label: 'Template library' }]} title="Enterprise template library"
      actions={can('forms.edit') ? <Button variant="contained" component={RouterLink} to="/forms/templates/new">Create template</Button> : undefined}>
      <Stack direction={{ xs: 'column', md: 'row' }} gap={2} alignItems="flex-start">
        <Paper variant="outlined" sx={{ width: { xs: '100%', md: 240 }, flexShrink: 0 }}>
          <List dense aria-label="Template views">
            {VIEWS.map(([k, label]) => <ListItemButton key={k} selected={view === k} onClick={() => set('view', k === 'library' ? null : k)}><ListItemText primary={label} /></ListItemButton>)}
            <ListItemButton component={RouterLink} to="/forms/records"><ListItemText primary="Template usage" secondary="Form records" /></ListItemButton>
            <ListItemButton component={RouterLink} to="/forms/marketplace"><ListItemText primary="Template marketplace" secondary="Industry packs and library templates" /></ListItemButton>
          </List>
          {!!views.data?.length && <>
            <Typography variant="overline" sx={{ px: 2 }}>Saved views</Typography>
            <List dense>{views.data.map((v) => <ListItemButton key={v.id} onClick={() => applyView(v)}><ListItemText primary={v.name} secondary={v.isDefault ? 'Default' : undefined} />
              <IconButton size="small" aria-label={`Delete view ${v.name}`} onClick={async (e) => { e.stopPropagation(); await del(`/api/template-library/views/${v.id}`); views.refetch(); }}>×</IconButton></ListItemButton>)}</List>
          </>}
        </Paper>
        <Box sx={{ flex: 1, minWidth: 0, width: '100%' }}>
          <Stack direction="row" gap={1} flexWrap="wrap" component="form" sx={{ mb: 2 }} onSubmit={(e) => { e.preventDefault(); set('q', q || null); }}>
            <TextField size="small" label="Search templates" placeholder="Number, title or description" value={q} onChange={(e) => setQ(e.target.value)} sx={{ width: 280 }} />
            <TextField size="small" select label="Category" value={p.get('categoryId') ?? ''} onChange={(e) => set('categoryId', e.target.value || null)} sx={{ width: 220 }}>
              <MenuItem value="">All categories</MenuItem>
              {(cats.data ?? []).filter((c) => c.isActive).map((c) => <MenuItem key={c.id} value={c.id}>{c.name}{c.count ? ` (${c.count})` : ''}</MenuItem>)}
            </TextField>
            <TextField size="small" select label="Status" value={p.get('status') ?? ''} onChange={(e) => set('status', e.target.value || null)} sx={{ width: 150 }}>
              <MenuItem value="">Any</MenuItem>{docStatus.map((s, i) => <MenuItem key={i} value={String(i)}>{s}</MenuItem>)}
            </TextField>
            <TextField size="small" select label="Source" value={p.get('source') ?? ''} onChange={(e) => set('source', e.target.value || null)} sx={{ width: 140 }}>
              <MenuItem value="">Any</MenuItem>{SOURCES.map((s, i) => <MenuItem key={i} value={String(i)}>{s}</MenuItem>)}
            </TextField>
            <FormControlLabel control={<Checkbox checked={p.get('favorites') === 'true'} onChange={(e) => set('favorites', e.target.checked ? 'true' : null)} />} label="Favorites" />
            <Button type="submit" variant="outlined">Search</Button>
            <Button onClick={() => setColDlg(true)}>Columns</Button>
            <Button onClick={() => setSaveDlg(true)}>Save view</Button>
          </Stack>
          <Paper variant="outlined" sx={{ overflowX: 'auto' }}>
            <Table size="small" aria-label="Templates">
              <TableHead><TableRow>
                <TableCell padding="checkbox" />
                <Head k="number" label="Number" /><Head k="title" label="Title" />
                {show('category') && <Head label="Category" />}{show('revision') && <Head label="Revision" />}
                <Head k="status" label="Status" />
                {show('effective') && <Head k="effective" label="Effective" />}{show('review') && <Head k="review" label="Review date" />}
                {show('site') && <Head label="Site" />}{show('owner') && <Head label="Owner" />}{show('source') && <Head label="Source" />}
                {show('industry') && <Head label="Industry pack" />}{show('lastUsed') && <Head k="lastUsed" label="Last used" />}{show('usage') && <Head k="usage" label="Uses" />}
                {show('confidentiality') && <Head label="Confidentiality" />}
              </TableRow></TableHead>
              <TableBody>
                {list.data?.items.map((it) => (
                  <TableRow key={it.id} hover sx={{ cursor: 'pointer' }} onClick={() => setSelected(it)}>
                    <TableCell padding="checkbox" onClick={(e) => e.stopPropagation()}>
                      <Tooltip title={it.favorite ? 'Remove from favorites' : 'Add to favorites'}><IconButton size="small" aria-label={it.favorite ? `Unfavorite ${it.number}` : `Favorite ${it.number}`} onClick={() => toggleFav(it)}>{it.favorite ? <StarIcon color="warning" fontSize="small" /> : <StarBorderIcon fontSize="small" />}</IconButton></Tooltip>
                    </TableCell>
                    <TableCell sx={{ fontFamily: mono }}>{it.number}</TableCell><TableCell>{it.title}</TableCell>
                    {show('category') && <TableCell>{it.category ?? '—'}</TableCell>}
                    {show('revision') && <TableCell>{it.currentRevision ?? '—'}{it.currentRevisionStatus != null && it.currentRevision !== it.effectiveRevision ? ` (${revStatus[it.currentRevisionStatus]})` : ''}</TableCell>}
                    <TableCell><StatusChip status={docStatus[it.status]} /></TableCell>
                    {show('effective') && <TableCell>{it.effectiveRevision ? `${it.effectiveRevision} · ${fmtDate(it.effectiveDate)}` : '—'}</TableCell>}
                    {show('review') && <TableCell>{fmtDate(it.reviewDate)}</TableCell>}
                    {show('site') && <TableCell>{it.siteCode}</TableCell>}{show('owner') && <TableCell>{it.ownerName}</TableCell>}
                    {show('source') && <TableCell>{SOURCES[it.source]}</TableCell>}{show('industry') && <TableCell>{it.industryPack ?? '—'}</TableCell>}
                    {show('lastUsed') && <TableCell>{it.lastUsed ? fmtDate(it.lastUsed) : 'Never'}</TableCell>}{show('usage') && <TableCell>{it.usage}</TableCell>}
                    {show('confidentiality') && <TableCell>{it.confidentiality}</TableCell>}
                  </TableRow>))}
                {list.data && !list.data.items.length && <TableRow><TableCell colSpan={20}><Typography color="text.secondary">No templates in this view.</Typography></TableCell></TableRow>}
              </TableBody>
            </Table>
          </Paper>
          {list.data && <Stack direction="row" alignItems="center" gap={2} sx={{ mt: 1 }}>
            <Typography variant="body2" color="text.secondary">{list.data.total} template{list.data.total === 1 ? '' : 's'}</Typography>
            {list.data.total > list.data.pageSize && <Pagination count={Math.ceil(list.data.total / list.data.pageSize)} page={page} onChange={(_, v) => set('page', String(v))} />}
          </Stack>}
        </Box>
      </Stack>

      <Dialog open={colDlg} onClose={() => setColDlg(false)}>
        <DialogTitle>Columns</DialogTitle>
        <DialogContent>{COLUMNS.map(([k, label]) => <FormControlLabel key={k} sx={{ display: 'flex' }} label={label}
          control={<Checkbox checked={show(k)} onChange={(e) => { const n = e.target.checked ? [...cols, k] : cols.filter((c) => c !== k); setCols(n); localStorage.setItem('tl.cols', JSON.stringify(n)); }} />} />)}</DialogContent>
        <DialogActions><Button onClick={() => setColDlg(false)}>Done</Button></DialogActions>
      </Dialog>
      <Dialog open={saveDlg} onClose={() => setSaveDlg(false)}>
        <DialogTitle>Save this view</DialogTitle>
        <DialogContent><TextField autoFocus fullWidth sx={{ mt: 1 }} label="View name" value={viewName} onChange={(e) => setViewName(e.target.value)} /></DialogContent>
        <DialogActions><Button onClick={() => setSaveDlg(false)}>Cancel</Button>
          <Button variant="contained" disabled={!viewName.trim()} onClick={async () => { await post('/api/template-library/views', { name: viewName.trim(), settings: JSON.stringify({ query: p.toString(), cols }), isDefault: false }); setSaveDlg(false); setViewName(''); views.refetch(); }}>Save</Button></DialogActions>
      </Dialog>
      {selected && <TemplateActions item={selected} cats={cats.data ?? []} onClose={() => setSelected(null)} onOpen={() => nav(`/forms/templates/${selected.id}`)}
        onChanged={() => qc.invalidateQueries({ queryKey: ['template-library'] })} />}
    </ModuleWorkspace>
  );
}

function TemplateActions({ item, cats, onClose, onOpen, onChanged }: { item: Item; cats: Cat[]; onClose: () => void; onOpen: () => void; onChanged: () => void }) {
  const { can } = useAuth();
  const nav = useNavigate();
  const [tab, setTab] = useState<'actions' | 'classify' | 'compare' | 'usage' | 'history'>('actions');
  const [error, setError] = useState<string | null>(null);
  const [cloneTitle, setCloneTitle] = useState(`${item.title} (copy)`);
  const [cls, setCls] = useState({ categoryId: item.categoryId ?? '', moduleKey: item.module ?? '', industryPack: item.industryPack ?? '', description: '', confidentiality: item.confidentiality });
  const [cmp, setCmp] = useState({ from: '', to: '' });
  const [reason, setReason] = useState('');
  const detail = useQuery({ queryKey: ['doc', item.id], queryFn: () => get<{ siteId: string; revisions: Rev[] }>(`/api/documents/${item.id}`) });
  const usage = useQuery({ queryKey: ['tpl-usage', item.id], enabled: tab === 'usage', queryFn: () => get<{ records: number; open: number; lastUsed: string | null; byRevision: { revision: string; records: number }[] }>(`/api/template-library/${item.id}/usage`) });
  const hist = useQuery({ queryKey: ['tpl-hist', item.id], enabled: tab === 'history', queryFn: () => get<{ at: string; user: string; action: string; description: string; reason: string | null }[]>(`/api/template-library/${item.id}/history`) });
  const diff = useQuery({ queryKey: ['tpl-cmp', item.id, cmp], enabled: tab === 'compare' && !!cmp.from && !!cmp.to, queryFn: () => get<{ from: string; to: string; fields: { key: string; change: string; before: string | null; after: string | null }[] }>(`/api/template-library/${item.id}/compare?from=${cmp.from}&to=${cmp.to}`) });
  const run = async (f: () => Promise<unknown>) => { setError(null); try { await f(); onChanged(); } catch (e) { setError((e as Error).message); } };
  const revs = detail.data?.revisions ?? [];
  return (
    <Dialog open onClose={onClose} maxWidth="md" fullWidth>
      <DialogTitle><span style={{ fontFamily: mono }}>{item.number}</span> · {item.title}</DialogTitle>
      <DialogContent>
        <Stack direction="row" gap={1} sx={{ mb: 2 }} flexWrap="wrap">
          {(['actions', 'classify', 'compare', 'usage', 'history'] as const).map((t) => <Button key={t} size="small" variant={tab === t ? 'contained' : 'outlined'} onClick={() => setTab(t)}>
            {{ actions: 'Actions', classify: 'Classification', compare: 'Compare revisions', usage: 'Usage', history: 'Audit history' }[t]}</Button>)}
        </Stack>
        {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
        {tab === 'actions' && <Stack gap={2}>
          <Typography variant="body2">Create, revise, submit, approve, print and preview from the template page. Approved revisions cannot be changed; completed forms stay tied to the revision they used.</Typography>
          <Stack direction="row" gap={1} flexWrap="wrap">
            <Button variant="contained" onClick={onOpen}>Open template</Button>
            {can('forms.edit') && <Button onClick={() => nav(`/forms/records/new?template=${item.id}`)}>Fill in</Button>}
          </Stack>
          {can('forms.edit') && detail.data && <Stack direction="row" gap={1} alignItems="center">
            <TextField size="small" label="Clone as" value={cloneTitle} onChange={(e) => setCloneTitle(e.target.value)} sx={{ flex: 1 }} />
            <Button onClick={() => run(async () => { const id = await post<string>(`/api/template-library/${item.id}/clone`, { title: cloneTitle, siteId: detail.data!.siteId, departmentId: null }); nav(`/forms/templates/${id}`); })}>Clone</Button>
          </Stack>}
          {item.status === 2 && can('forms.approve') && <Stack direction="row" gap={1} alignItems="center">
            <TextField size="small" label="Reason to restore" value={reason} onChange={(e) => setReason(e.target.value)} sx={{ flex: 1 }} />
            <Button disabled={!reason.trim()} onClick={() => run(async () => { await post(`/api/template-library/${item.id}/restore`, { reason }); onClose(); })}>Restore</Button>
          </Stack>}
        </Stack>}
        {tab === 'classify' && <Stack gap={2}>
          <TextField select label="Category" value={cls.categoryId} onChange={(e) => setCls({ ...cls, categoryId: e.target.value })}>
            <MenuItem value="">None</MenuItem>{cats.filter((c) => c.isActive).map((c) => <MenuItem key={c.id} value={c.id}>{c.name}</MenuItem>)}</TextField>
          <TextField label="Module" value={cls.moduleKey} onChange={(e) => setCls({ ...cls, moduleKey: e.target.value })} />
          <TextField label="Industry pack" value={cls.industryPack} onChange={(e) => setCls({ ...cls, industryPack: e.target.value })} />
          <TextField label="Description" multiline minRows={2} value={cls.description} onChange={(e) => setCls({ ...cls, description: e.target.value })} />
          <TextField select label="Confidentiality" value={cls.confidentiality} onChange={(e) => setCls({ ...cls, confidentiality: e.target.value })}>
            {['Public', 'Internal', 'Confidential', 'Restricted'].map((m) => <MenuItem key={m} value={m}>{m}</MenuItem>)}</TextField>
          <Box><Button variant="contained" disabled={!can('forms.edit')} onClick={() => run(() => put(`/api/template-library/${item.id}/classification`, { ...cls, categoryId: cls.categoryId || null, retentionPolicyId: null }))}>Save classification</Button></Box>
        </Stack>}
        {tab === 'compare' && <Stack gap={2}>
          <Stack direction="row" gap={1}>
            {(['from', 'to'] as const).map((k) => <TextField key={k} select size="small" label={k === 'from' ? 'From revision' : 'To revision'} value={cmp[k]} onChange={(e) => setCmp({ ...cmp, [k]: e.target.value })} sx={{ width: 200 }}>
              {revs.map((r) => <MenuItem key={r.id} value={r.id}>{r.revision} · {revStatus[r.status]}</MenuItem>)}</TextField>)}
          </Stack>
          {diff.data && (diff.data.fields.length ? <Table size="small" aria-label="Revision differences"><TableHead><TableRow><TableCell>Item</TableCell><TableCell>Change</TableCell><TableCell>{diff.data.from}</TableCell><TableCell>{diff.data.to}</TableCell></TableRow></TableHead>
            <TableBody>{diff.data.fields.map((f) => <TableRow key={f.key}><TableCell sx={{ fontFamily: mono }}>{f.key}</TableCell><TableCell>{f.change}</TableCell>
              <TableCell sx={{ fontFamily: mono, fontSize: 12, wordBreak: 'break-all' }}>{f.before ?? '—'}</TableCell><TableCell sx={{ fontFamily: mono, fontSize: 12, wordBreak: 'break-all' }}>{f.after ?? '—'}</TableCell></TableRow>)}</TableBody></Table>
            : <Typography>No differences in the form definition.</Typography>)}
        </Stack>}
        {tab === 'usage' && usage.data && <Stack gap={1}>
          <Typography>{usage.data.records} form record{usage.data.records === 1 ? '' : 's'}, {usage.data.open} not yet submitted. Last used {usage.data.lastUsed ? fmtDate(usage.data.lastUsed) : 'never'}.</Typography>
          {usage.data.byRevision.map((r) => <Typography key={r.revision} variant="body2">Revision {r.revision}: {r.records}</Typography>)}
        </Stack>}
        {tab === 'history' && <Table size="small" aria-label="Audit history"><TableBody>{hist.data?.map((h, i) => <TableRow key={i}><TableCell>{new Date(h.at).toLocaleString()}</TableCell><TableCell>{h.user}</TableCell><TableCell>{h.action}</TableCell><TableCell>{h.description}{h.reason ? ` — ${h.reason}` : ''}</TableCell></TableRow>)}</TableBody></Table>}
      </DialogContent>
      <DialogActions><Button onClick={onClose}>Close</Button></DialogActions>
    </Dialog>
  );
}
