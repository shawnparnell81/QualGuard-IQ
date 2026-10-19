import { useEffect, useRef, useState, type ReactElement, type RefObject } from 'react';
import { Link as RouterLink, useLocation, useNavigate } from 'react-router-dom';
import { Alert, Box, Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle, Divider, GlobalStyles, ListItemIcon, ListItemText, Menu, MenuItem, Snackbar, TextField, Tooltip, Typography } from '@mui/material';
import PrintOutlined from '@mui/icons-material/PrintOutlined';
import PictureAsPdfOutlined from '@mui/icons-material/PictureAsPdfOutlined';
import SaveAltOutlined from '@mui/icons-material/SaveAltOutlined';
import DeleteOutline from '@mui/icons-material/DeleteOutline';
import UploadFileOutlined from '@mui/icons-material/UploadFileOutlined';
import TableViewOutlined from '@mui/icons-material/TableViewOutlined';
import DescriptionOutlined from '@mui/icons-material/DescriptionOutlined';
import GridOnOutlined from '@mui/icons-material/GridOnOutlined';
import ExpandMore from '@mui/icons-material/ExpandMore';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError, get, getBlob, post, postBlob } from '../api';
import { useAuth, useBranding } from '../auth';
import { fileName, formatLabel, saveFile, type SaveFormat } from './saveFile';
import { findTables, readFilters, readTable } from './tableText';

export interface DeleteCheck { deletable: boolean; canDelete: boolean; locked: boolean; reason: string | null; deletePermission: string | null }
export interface RecordCapabilities { type: string; typeLabel: string; id: string; number: string; title: string; status: string | null; url: string; delete: DeleteCheck }

const GUID = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi;

/** The record a page shows, found from the id in its address (no per-page wiring). Null on list and setup pages. */
export function usePageRecord(explicit?: { type: string; id: string } | false) {
  const { pathname } = useLocation();
  const ids = explicit === false ? [] : pathname.match(GUID) ?? [];
  const id = explicit ? explicit.id : ids[ids.length - 1];
  return useQuery({
    queryKey: ['page-record', explicit ? explicit.type : pathname, id],
    queryFn: () => explicit
      ? get<RecordCapabilities>(`/api/records/${encodeURIComponent(explicit.type)}/${explicit.id}/capabilities`)
      : get<RecordCapabilities>(`/api/records/locate/${id}?path=${encodeURIComponent(pathname)}`),
    enabled: !!id, retry: false, staleTime: 30_000,
  });
}

/** List pages that can be filled from a file: [page, import address, button label, permission needed]. */
const importLinks: [RegExp, string, string, string][] = [
  [/^\/ncr$/, '/imports?target=log-ncrs', 'Import NCRs', 'ncr.edit'],
  [/^\/capa$/, '/imports?target=log-capas', 'Import CAPAs', 'capa.edit'],
  [/^\/calibration\/equipment$/, '/imports?target=master-equipment', 'Import equipment', 'calibration.edit'],
  [/^\/suppliers$/, '/imports?target=master-suppliers', 'Import suppliers', 'suppliers.edit'],
  [/^\/products\/parts$/, '/imports?target=master-parts', 'Import parts', 'masterdata.edit'],
  [/^\/products\/items$/, '/imports?target=master-products', 'Import products', 'masterdata.edit'],
  [/^\/products\/customers$/, '/imports?target=master-customers', 'Import customers', 'masterdata.edit'],
  [/^\/training\/(status|courses)$/, '/imports?target=log-training', 'Import training records', 'training.edit'],
  [/^\/documents$/, '/imports?target=master-documents', 'Import document list', 'documents.edit'],
  [/^\/documents$/, '/imports?mode=document', 'Import Word document', 'documents.edit'],
  [/^\/drawings$/, '/imports?target=master-drawings', 'Import drawing list', 'drawings.edit'],
  [/^\/forms\/templates$/, '/imports?mode=template', 'Import from Word or Excel', 'forms.edit'],
];

const errorText = (e: unknown) => e instanceof ApiError ? e.message : 'Something went wrong. Try again.';
const formats: [SaveFormat, string, ReactElement][] = [
  ['pdf', 'PDF (.pdf)', <PictureAsPdfOutlined fontSize="small" />],
  ['xlsx', 'Excel (.xlsx)', <GridOnOutlined fontSize="small" />],
  ['docx', 'Word (.docx)', <DescriptionOutlined fontSize="small" />],
  ['csv', 'CSV (.csv)', <TableViewOutlined fontSize="small" />],
];

/**
 * Print, PDF, Save as and Delete for the page in a module workspace.
 * Record pages get the record's own PDF and exports; list pages export the table on screen.
 */
export function PageTools({ area, title, record }: { area: RefObject<HTMLElement | null>; title: string; record?: { type: string; id: string } | false }) {
  const rec = usePageRecord(record);
  const loc = useLocation();
  const { can } = useAuth();
  const [hasTable, setHasTable] = useState(false);
  const [menu, setMenu] = useState<HTMLElement | null>(null);
  const [busy, setBusy] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [notice, setNotice] = useState<{ ok: boolean; text: string } | null>(null);

  useEffect(() => {
    const el = area.current; if (!el) return;
    let frame = 0;
    const check = () => { cancelAnimationFrame(frame); frame = requestAnimationFrame(() => setHasTable(findTables(el).length > 0)); };
    check();
    const mo = new MutationObserver(check);
    mo.observe(el, { childList: true, subtree: true });
    return () => { mo.disconnect(); cancelAnimationFrame(frame); };
  }, [area, loc.pathname]);

  const r = rec.data;
  const imports = !r ? importLinks.filter(([re, , , perm]) => re.test(loc.pathname) && can(perm)) : [];

  const run = async (format: SaveFormat) => {
    setMenu(null);
    setBusy(true);
    try {
      let result;
      if (r) {
        const base = `${r.number} ${r.title}`.trim() || r.typeLabel;
        result = await saveFile(fileName(base, format), format, () => getBlob(`/api/records/${encodeURIComponent(r.type)}/${r.id}/export?format=${format}`));
      } else {
        const t = findTables(area.current)[0];
        if (!t) { setNotice({ ok: false, text: 'There is no list on this page to save.' }); return; }
        const data = readTable(t);
        result = await saveFile(fileName(title, format), format, () => postBlob('/api/export/table', { title, format, columns: data.columns, rows: data.rows, filters: readFilters(area.current) }));
      }
      if (result !== 'cancelled') setNotice({ ok: true, text: result === 'saved' ? `Saved as ${formatLabel(format)}.` : `Downloaded the ${formatLabel(format)}.` });
    } catch (e) { setNotice({ ok: false, text: errorText(e) }); }
    finally { setBusy(false); }
  };

  const showSave = !!r || hasTable;
  return (
    <Box className="qg-no-print" data-testid="page-tools" sx={{ display: 'flex', gap: 1, flexWrap: 'wrap', alignItems: 'center' }}>
      <Tooltip title={r ? 'Print this record as shown' : 'Print this page'}>
        <Button size="small" variant="outlined" startIcon={<PrintOutlined />} onClick={() => window.print()} data-testid="tool-print">Print</Button>
      </Tooltip>
      {r && (
        <Button size="small" variant="outlined" startIcon={<PictureAsPdfOutlined />} disabled={busy} onClick={() => run('pdf')} data-testid="tool-pdf">Download PDF</Button>
      )}
      {showSave && (
        <>
          <Button size="small" variant="outlined" startIcon={<SaveAltOutlined />} endIcon={<ExpandMore />} disabled={busy} onClick={(e) => setMenu(e.currentTarget)}
            aria-haspopup="menu" data-testid="tool-save-as">{r ? 'Save as' : 'Export list'}</Button>
          <Menu anchorEl={menu} open={!!menu} onClose={() => setMenu(null)}>
            <Typography variant="caption" color="text.secondary" sx={{ px: 2, py: 0.5, display: 'block' }}>
              {r ? 'Save this record to your computer' : 'Save the list as shown (filters and columns)'}
            </Typography>
            <Divider />
            {formats.map(([f, label, icon]) => (
              <MenuItem key={f} onClick={() => run(f)} data-testid={`save-${f}`}><ListItemIcon>{icon}</ListItemIcon><ListItemText primary={label} /></MenuItem>
            ))}
          </Menu>
        </>
      )}
      {imports.map(([, to, label]) => (
        <Button key={to} size="small" variant="outlined" startIcon={<UploadFileOutlined />} component={RouterLink} to={to} data-testid="tool-import">{label}</Button>
      ))}
      {r?.delete.deletable && (
        <Tooltip title={r.delete.canDelete ? (r.delete.locked ? 'This record is closed, approved or signed' : '') : r.delete.reason ?? ''}>
          <span>
            <Button size="small" variant="outlined" color="error" startIcon={<DeleteOutline />} disabled={!r.delete.canDelete} onClick={() => setDeleting(true)} data-testid="tool-delete">Delete</Button>
          </span>
        </Tooltip>
      )}
      {r && deleting && <DeleteDialog rec={r} onClose={() => setDeleting(false)} />}
      <Snackbar open={!!notice} autoHideDuration={5000} onClose={() => setNotice(null)} anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}>
        {notice ? <Alert severity={notice.ok ? 'success' : 'error'} onClose={() => setNotice(null)} data-testid="tool-notice">{notice.text}</Alert> : undefined}
      </Snackbar>
    </Box>
  );
}

/** Asks why, then deletes. The record leaves every list but stays restorable from Deleted records. */
function DeleteDialog({ rec, onClose }: { rec: RecordCapabilities; onClose: () => void }) {
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const nav = useNavigate();
  const qc = useQueryClient();
  const loc = useLocation();
  const ok = reason.trim().length >= 5;
  const name = `${rec.typeLabel} ${rec.number}`.trim();
  const go = async () => {
    setBusy(true); setError(null);
    try {
      await post(`/api/records/${encodeURIComponent(rec.type)}/${rec.id}/delete`, { reason: reason.trim() });
      await qc.invalidateQueries();
      const back = loc.pathname.replace(/\/[^/]*$/, '') || '/';
      nav(back, { state: { notice: `Deleted ${name}. It can be restored from Deleted records.` } });
    } catch (e) { setError(errorText(e)); setBusy(false); }
  };
  return (
    <Dialog open onClose={busy ? undefined : onClose} maxWidth="sm" fullWidth>
      <DialogTitle>Delete {name}?</DialogTitle>
      <DialogContent>
        <DialogContentText sx={{ mb: 2 }}>
          “{rec.title}” will disappear from lists, searches and reports. Nothing is erased: the deletion is kept in the audit trail and people allowed to delete these records can restore it from Deleted records.
        </DialogContentText>
        {rec.delete.locked && <Alert severity="warning" sx={{ mb: 2 }}>This record is closed, approved or signed. Only delete it if it was created by mistake.</Alert>}
        {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
        <TextField autoFocus fullWidth multiline minRows={3} label="Reason for deleting" required value={reason} onChange={(e) => setReason(e.target.value)}
          helperText="Required. At least 5 characters. Shown in the audit trail." slotProps={{ htmlInput: { maxLength: 2000, 'data-testid': 'delete-reason' } }} />
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>Cancel</Button>
        <Button color="error" variant="contained" disabled={!ok || busy} onClick={go} data-testid="delete-confirm">Delete</Button>
      </DialogActions>
    </Dialog>
  );
}

/** Shows a message passed by the page that sent the person here (for example after a delete). */
export function ArrivalNotice() {
  const loc = useLocation();
  const nav = useNavigate();
  const text = (loc.state as { notice?: string } | null)?.notice;
  const [shown, setShown] = useState<string | null>(null);
  const seen = useRef<string | null>(null);
  useEffect(() => {
    if (text && seen.current !== loc.key) { seen.current = loc.key; setShown(text); nav(loc.pathname + loc.search, { replace: true, state: null }); }
  }, [text, loc.key]);
  return (
    <Snackbar open={!!shown} autoHideDuration={6000} onClose={() => setShown(null)} anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}>
      {shown ? <Alert severity="success" onClose={() => setShown(null)} data-testid="arrival-notice">{shown}</Alert> : undefined}
    </Snackbar>
  );
}

/** What a printout looks like: page content only, on plain paper colours, with a heading saying what and when. */
export function PrintStyles() {
  return (
    <GlobalStyles styles={(t) => ({
      '.qg-print-only': { display: 'none' },
      '@media print': {
        '@page': { margin: '14mm 12mm' },
        'html, body, #root': { background: t.palette.common.white, color: t.palette.common.black },
        'header.MuiAppBar-root, aside, nav, .qg-no-print, [role="tablist"], .MuiBreadcrumbs-root, .MuiSnackbar-root, .MuiFab-root, button:not(.qg-print-keep)': { display: 'none !important' },
        '.qg-print-only': { display: 'block' },
        'main, main *': { boxShadow: 'none !important' },
        '.MuiPaper-root': { background: `${t.palette.common.white} !important`, color: `${t.palette.common.black} !important`, border: `1px solid ${t.palette.grey[300]}` },
        '.MuiTableCell-root': { color: `${t.palette.common.black} !important`, borderColor: `${t.palette.grey[300]} !important` },
        'table, tr, img': { breakInside: 'avoid' },
        '.MuiTableContainer-root': { overflow: 'visible !important', maxHeight: 'none !important' },
        'a': { color: 'inherit', textDecoration: 'none' },
      },
    })} />
  );
}

/** Heading printed above the page: organization, page title, who printed it and when. */
export function PrintHeading({ title }: { title: string }) {
  const { me } = useAuth();
  const { data: brand } = useBranding();
  return (
    <Box className="qg-print-only" sx={{ mb: 2, pb: 1, borderBottom: 1, borderColor: 'divider' }} data-testid="print-heading">
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 2 }}>
        <Box>
          <Typography variant="subtitle2">{brand?.organizationName ?? ''}</Typography>
          <Typography variant="h6">{title}</Typography>
        </Box>
        {brand?.hasLogo && <Box component="img" src="/api/settings/logo" alt="" sx={{ maxHeight: 40, maxWidth: 160 }} />}
      </Box>
      <Typography variant="caption">Printed {new Date().toLocaleString()} by {me?.displayName ?? ''}. Uncontrolled when printed unless stamped otherwise.</Typography>
    </Box>
  );
}

/** Any delete permission the person holds (they can see Deleted records for those modules). */
export const useCanSeeDeleted = () => {
  const { me } = useAuth();
  return !!me?.grants.some((g) => g.permission.endsWith('.delete') || g.permission === 'sequfloh.delete_records');
};

