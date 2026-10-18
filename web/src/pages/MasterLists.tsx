import { useEffect, useMemo, useState } from 'react';
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, IconButton, List, ListItemButton, ListItemText, MenuItem, Pagination, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { get, getBlob, post, del } from '../api';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { StatusChip, fmtDate, fmtDateTime } from '../components/Status';
import { mono } from '../theme';

interface ListType { key: string; name: string; module: string; description: string; revisionControlled: boolean; approval: boolean; importTarget: string | null; columns: number; canEdit: boolean }
interface Column { key: string; label: string; type: string }
interface Row { id: string; url: string; active: boolean; values: Record<string, unknown> }
interface Page { type: { key: string; name: string; description: string; columns: Column[]; revisionControlled: boolean; approval: boolean; importTarget: string | null }; total: number; page: number; pageSize: number; rows: Row[]; byStatus: Record<string, number>; inactive: number }
interface SavedView { id: string; name: string; settings: string; isDefault: boolean }
interface Hist { occurredAt: string; userName: string; action: string; description: string }

function cell(v: unknown, type: string) {
  if (v === null || v === undefined || v === '') return '—';
  if (type === 'yesno' || typeof v === 'boolean') return v ? 'Yes' : 'No';
  if (type === 'date') return fmtDate(String(v));
  if (type === 'status') return <StatusChip status={String(v)} />;
  return String(v);
}

/** Governed master lists: each list shows the owning module's records with configurable columns, search, filters, saved views, export and history. */
export function MasterLists() {
  const { type: routeType } = useParams();
  const nav = useNavigate();
  const [p, setP] = useSearchParams();
  const types = useQuery({ queryKey: ['master-list-types'], queryFn: () => get<ListType[]>('/api/master-lists') });
  const type = routeType ?? types.data?.[0]?.key;
  const [q, setQ] = useState(p.get('q') ?? '');
  useEffect(() => setQ(p.get('q') ?? ''), [type, p]);
  const set = (k: string, v: string | null) => { const n = new URLSearchParams(p); if (v) n.set(k, v); else n.delete(k); if (k !== 'page') n.delete('page'); setP(n); };
  const qs = useMemo(() => { const n = new URLSearchParams(p); n.set('pageSize', '50'); return n.toString(); }, [p]);
  const list = useQuery({ queryKey: ['master-list', type, qs], enabled: !!type, queryFn: () => get<Page>(`/api/master-lists/${type}?${qs}`) });
  const views = useQuery({ queryKey: ['master-list-views', type], enabled: !!type, queryFn: () => get<SavedView[]>(`/api/master-lists/${type}/views`) });
  const colKey = `ml.cols.${type}`;
  const [cols, setCols] = useState<string[] | null>(null);
  useEffect(() => { try { setCols(JSON.parse(localStorage.getItem(colKey) ?? 'null')); } catch { setCols(null); } }, [colKey]);
  const [colDlg, setColDlg] = useState(false);
  const [saveDlg, setSaveDlg] = useState(false);
  const [histDlg, setHistDlg] = useState(false);
  const [viewName, setViewName] = useState('');
  const [error, setError] = useState<string | null>(null);
  const history = useQuery({ queryKey: ['master-list-history', type], enabled: histDlg && !!type, queryFn: () => get<Hist[]>(`/api/master-lists/${type}/history?take=200`) });
  const all = list.data?.type.columns ?? [];
  const shown = all.filter((c) => !cols || cols.includes(c.key));
  const current = types.data?.find((t) => t.key === type);
  const sortBy = (k: string) => { const n = new URLSearchParams(p); if (n.get('sort') === k) n.set('desc', n.get('desc') === 'true' ? 'false' : 'true'); else { n.set('sort', k); n.delete('desc'); } setP(n); };
  const exportAs = async (format: 'xlsx' | 'csv') => {
    setError(null);
    try {
      const n = new URLSearchParams(p); n.set('format', format); n.delete('page');
      const blob = await getBlob(`/api/master-lists/${type}/export?${n}`);
      const a = document.createElement('a'); a.href = URL.createObjectURL(blob); a.download = `${list.data?.type.name ?? type}.${format}`; a.click(); URL.revokeObjectURL(a.href);
    } catch (e) { setError((e as Error).message); }
  };
  const applyView = (v: SavedView) => { const s = JSON.parse(v.settings) as { query?: string; cols?: string[] }; setP(new URLSearchParams(s.query ?? '')); if (s.cols) { setCols(s.cols); localStorage.setItem(colKey, JSON.stringify(s.cols)); } };

  return (
    <ModuleWorkspace module={modules.find((m) => m.key === 'documents')!} crumbs={[{ label: 'Master lists', to: '/master-lists' }, ...(list.data ? [{ label: list.data.type.name }] : [])]} title={list.data?.type.name ?? 'Master lists'}
      actions={current?.canEdit && current.importTarget ? <Button variant="contained" component={RouterLink} to={`/imports?target=${current.importTarget}`}>Bulk import or update</Button> : undefined}>
      <Stack direction={{ xs: 'column', md: 'row' }} gap={2} alignItems="flex-start">
        <Paper variant="outlined" sx={{ width: { xs: '100%', md: 260 }, flexShrink: 0 }}>
          <List dense aria-label="Master lists">
            {(types.data ?? []).map((t) => <ListItemButton key={t.key} selected={t.key === type} onClick={() => nav(`/master-lists/${t.key}`)}><ListItemText primary={t.name} secondary={t.module} /></ListItemButton>)}
          </List>
          {!!views.data?.length && <>
            <Typography variant="overline" sx={{ px: 2 }}>Saved views</Typography>
            <List dense>{views.data.map((v) => <ListItemButton key={v.id} onClick={() => applyView(v)}><ListItemText primary={v.name} secondary={v.isDefault ? 'Default' : undefined} />
              <IconButton size="small" aria-label={`Delete view ${v.name}`} onClick={async (e) => { e.stopPropagation(); await del(`/api/master-lists/${type}/views/${v.id}`); views.refetch(); }}>×</IconButton></ListItemButton>)}</List>
          </>}
        </Paper>
        <Box sx={{ flex: 1, minWidth: 0, width: '100%' }}>
          {list.data && <Typography color="text.secondary" sx={{ mb: 1 }}>{list.data.type.description}
            {list.data.type.revisionControlled ? ' Revision controlled.' : ''}{list.data.type.approval ? ' Changes are approved in the module.' : ''}</Typography>}
          <Stack direction="row" gap={1} flexWrap="wrap" component="form" sx={{ mb: 1 }} onSubmit={(e) => { e.preventDefault(); set('q', q || null); }}>
            <TextField size="small" label="Search the list" value={q} onChange={(e) => setQ(e.target.value)} sx={{ width: 260 }} />
            <TextField size="small" select label="Status" value={p.get('status') ?? ''} onChange={(e) => set('status', e.target.value || null)} sx={{ width: 180 }}>
              <MenuItem value="">Any</MenuItem>{Object.keys(list.data?.byStatus ?? {}).filter(Boolean).map((s) => <MenuItem key={s} value={s}>{s}</MenuItem>)}
            </TextField>
            <FormControlLabel control={<Checkbox checked={p.get('includeInactive') === 'true'} onChange={(e) => set('includeInactive', e.target.checked ? 'true' : null)} />} label={`Show inactive${list.data?.inactive ? ` (${list.data.inactive})` : ''}`} />
            <Button type="submit" variant="outlined">Search</Button>
            <Button onClick={() => setColDlg(true)}>Columns</Button>
            <Button onClick={() => setSaveDlg(true)}>Save view</Button>
            <Button onClick={() => exportAs('xlsx')}>Export to Excel</Button>
            <Button onClick={() => exportAs('csv')}>Export CSV</Button>
            <Button onClick={() => setHistDlg(true)}>History</Button>
          </Stack>
          {list.data && <Stack direction="row" gap={1} flexWrap="wrap" sx={{ mb: 1 }} aria-label="Totals by status">
            {Object.entries(list.data.byStatus).map(([s, n]) => <Chip key={s} size="small" variant={p.get('status') === s ? 'filled' : 'outlined'} label={`${s || 'No status'}: ${n}`} onClick={() => set('status', p.get('status') === s ? null : s)} />)}
          </Stack>}
          {error && <Alert severity="error" sx={{ mb: 1 }}>{error}</Alert>}
          {list.isError && <Alert severity="error">{(list.error as Error).message}</Alert>}
          <Paper variant="outlined" sx={{ overflowX: 'auto' }}>
            <Table size="small" aria-label={list.data?.type.name ?? 'Master list'}>
              <TableHead><TableRow>{shown.map((c) => <TableCell key={c.key} onClick={() => sortBy(c.key)} sx={{ cursor: 'pointer', fontWeight: 600, whiteSpace: 'nowrap' }}>{c.label}{p.get('sort') === c.key ? (p.get('desc') === 'true' ? ' ▼' : ' ▲') : ''}</TableCell>)}</TableRow></TableHead>
              <TableBody>
                {list.data?.rows.map((r) => (
                  <TableRow key={r.id} hover sx={{ cursor: 'pointer', opacity: r.active ? 1 : 0.6 }} onClick={() => nav(r.url)}>
                    {shown.map((c, i) => <TableCell key={c.key} sx={i === 0 ? { fontFamily: mono, whiteSpace: 'nowrap' } : undefined}>{cell(r.values[c.key], c.type)}</TableCell>)}
                  </TableRow>))}
                {list.data && !list.data.rows.length && <TableRow><TableCell colSpan={shown.length || 1}><Typography color="text.secondary">Nothing in this list yet.</Typography></TableCell></TableRow>}
              </TableBody>
            </Table>
          </Paper>
          {list.data && <Stack direction="row" alignItems="center" gap={2} sx={{ mt: 1 }}>
            <Typography variant="body2" color="text.secondary">{list.data.total} {list.data.total === 1 ? 'entry' : 'entries'}</Typography>
            {list.data.total > list.data.pageSize && <Pagination count={Math.ceil(list.data.total / list.data.pageSize)} page={list.data.page} onChange={(_, v) => set('page', String(v))} />}
          </Stack>}
        </Box>
      </Stack>

      <Dialog open={colDlg} onClose={() => setColDlg(false)}>
        <DialogTitle>Columns</DialogTitle>
        <DialogContent>{all.map((c) => <FormControlLabel key={c.key} sx={{ display: 'flex' }} label={c.label}
          control={<Checkbox checked={!cols || cols.includes(c.key)} onChange={(e) => { const base = cols ?? all.map((x) => x.key); const n = e.target.checked ? [...base, c.key] : base.filter((k) => k !== c.key); setCols(n); localStorage.setItem(colKey, JSON.stringify(n)); }} />} />)}</DialogContent>
        <DialogActions><Button onClick={() => { setCols(null); localStorage.removeItem(colKey); }}>All columns</Button><Button onClick={() => setColDlg(false)}>Done</Button></DialogActions>
      </Dialog>
      <Dialog open={saveDlg} onClose={() => setSaveDlg(false)}>
        <DialogTitle>Save this view</DialogTitle>
        <DialogContent><TextField autoFocus fullWidth sx={{ mt: 1 }} label="View name" value={viewName} onChange={(e) => setViewName(e.target.value)} /></DialogContent>
        <DialogActions><Button onClick={() => setSaveDlg(false)}>Cancel</Button>
          <Button variant="contained" disabled={!viewName.trim()} onClick={async () => { await post(`/api/master-lists/${type}/views`, { name: viewName.trim(), settings: JSON.stringify({ query: p.toString(), cols }), isDefault: false }); setSaveDlg(false); setViewName(''); views.refetch(); }}>Save</Button></DialogActions>
      </Dialog>
      <Dialog open={histDlg} onClose={() => setHistDlg(false)} maxWidth="md" fullWidth>
        <DialogTitle>History of {list.data?.type.name}</DialogTitle>
        <DialogContent>
          <Table size="small" aria-label="List history"><TableHead><TableRow><TableCell>When</TableCell><TableCell>Who</TableCell><TableCell>What</TableCell><TableCell>Description</TableCell></TableRow></TableHead>
            <TableBody>{(history.data ?? []).map((h, i) => <TableRow key={i}><TableCell sx={{ whiteSpace: 'nowrap' }}>{fmtDateTime(h.occurredAt)}</TableCell><TableCell>{h.userName}</TableCell><TableCell>{h.action}</TableCell><TableCell>{h.description}</TableCell></TableRow>)}
              {history.data && !history.data.length && <TableRow><TableCell colSpan={4}>No changes recorded yet.</TableCell></TableRow>}</TableBody></Table>
        </DialogContent>
        <DialogActions><Button onClick={() => setHistDlg(false)}>Close</Button></DialogActions>
      </Dialog>
    </ModuleWorkspace>
  );
}
