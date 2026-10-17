import { useState } from 'react';
import { Alert, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Paper, Radio, RadioGroup, Stack, Switch, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { api, get } from '../api';
import { DataTable } from './DataTable';
import { StatusChip, fmtDateTime } from './Status';
import { mono } from '../theme';

/* eslint-disable @typescript-eslint/no-explicit-any */
interface Column { key: string; label: string; required: boolean; help: string }
interface RowResult { row: number; status: 'Accepted' | 'Rejected' | 'Duplicate'; errors: string[]; warnings: string[]; values: Record<string, string>; calculated: Record<string, any> }
export interface ImportResult { importId: string; status: string; dryRun: boolean; documentId: string; documentNumber: string; documentRevision: number; revisionCreated: boolean; mode: string;
  totalRows: number; accepted: number; rejected: number; duplicates: number; mapping: Record<string, string | null>; unmappedColumns: string[]; missingRequired: string[]; rows: RowResult[]; message?: string }

const tone: Record<string, 'success' | 'error' | 'warning'> = { Accepted: 'success', Rejected: 'error', Duplicate: 'warning' };

/**
 * Bulk line import for a PFMEA or control plan revision: choose a file, check the column mapping and the row-by-row preview (a dry run
 * that writes nothing), then import. Released revisions are never changed — importing into one creates the next revision first.
 */
export function LineImportDialog({ open, onClose, kind, docId, released, label, onDone }: {
  open: boolean; onClose: () => void; kind: 'pfmea' | 'plans'; docId: string; released: boolean; label: string; onDone: (r: ImportResult) => void;
}) {
  const cols = useQuery({ queryKey: ['import-cols', kind], queryFn: () => get<Column[]>(`/api/apqp/${kind}/import/columns`) });
  const [file, setFile] = useState<File | null>(null);
  const [mode, setMode] = useState('Append');
  const [reason, setReason] = useState('');
  const [mapping, setMapping] = useState<Record<string, string>>({});
  const [preview, setPreview] = useState<ImportResult | null>(null);
  const [partial, setPartial] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const reset = () => { setFile(null); setMode('Append'); setReason(''); setMapping({}); setPreview(null); setPartial(false); setError(null); };
  const send = async (dryRun: boolean, map = mapping) => {
    if (!file) return;
    const f = new FormData();
    f.append('file', file); f.append('dryRun', String(dryRun)); f.append('mode', mode); f.append('allowPartial', String(partial));
    if (Object.keys(map).length) f.append('mapping', JSON.stringify(map));
    if (released) { f.append('createRevision', 'true'); f.append('revisionReason', reason); }
    setBusy(true); setError(null);
    try {
      const r = await api<ImportResult>('POST', `/api/apqp/${kind}/${docId}/import`, f);
      if (dryRun) setPreview(r);
      else if (r.status === 'Committed') { onDone(r); reset(); onClose(); }
      else setPreview(r);
    } catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  };
  const headers = preview ? [...new Set([...Object.values(preview.mapping).filter(Boolean) as string[], ...preview.unmappedColumns])] : [];
  const remap = (key: string, col: string) => { const m = { ...Object.fromEntries(Object.entries(preview?.mapping ?? {}).map(([k, v]) => [k, v ?? ''])), ...mapping, [key]: col }; setMapping(m); void send(true, m); };
  return (
    <Dialog open={open} onClose={() => { reset(); onClose(); }} fullWidth maxWidth="lg">
      <DialogTitle>Import {label} lines</DialogTitle>
      <DialogContent>
        {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
        <Stack gap={2} sx={{ pt: 1 }}>
          <Stack direction="row" gap={2} alignItems="center" flexWrap="wrap">
            <Button variant="outlined" component="label">{file ? 'Choose another file' : 'Choose file (.xlsx or .csv)'}
              <input hidden type="file" accept=".xlsx,.xlsm,.csv" data-testid="import-file" onChange={(e) => { setFile(e.target.files?.[0] ?? null); setPreview(null); setMapping({}); }} /></Button>
            {file && <Typography sx={{ fontFamily: mono }}>{file.name}</Typography>}
            <Box sx={{ flex: 1 }} />
            <Typography variant="body2">Template:</Typography>
            <Button size="small" href={`/api/apqp/${kind}/import/template?format=xlsx`}>Excel</Button>
            <Button size="small" href={`/api/apqp/${kind}/import/template?format=csv`}>CSV</Button>
          </Stack>
          <RadioGroup row value={mode} onChange={(e) => { setMode(e.target.value); setPreview(null); }}>
            <FormControlLabel value="Append" control={<Radio />} label="Add to the existing lines" />
            <FormControlLabel value="Replace" control={<Radio />} label="Replace all lines of the draft" />
          </RadioGroup>
          {released && <><Alert severity="info">This revision is released and never changes. Importing creates the next draft revision (with the lines copied) and adds the imported lines to it, in one step.</Alert>
            <TextField label="Change reason for the new revision" value={reason} onChange={(e) => setReason(e.target.value)} multiline minRows={2} /></>}
          {preview && <>
            {preview.message && <Alert severity={preview.status === 'Refused' ? 'warning' : 'info'}>{preview.message}</Alert>}
            <Stack direction="row" gap={1} data-testid="import-summary">
              <Chip label={`${preview.totalRows} rows`} /><Chip color="success" label={`${preview.accepted} ready`} />
              <Chip color={preview.rejected ? 'error' : 'default'} label={`${preview.rejected} rejected`} /><Chip color={preview.duplicates ? 'warning' : 'default'} label={`${preview.duplicates} duplicates`} />
              <Chip variant="outlined" label={`Into ${preview.documentNumber} rev ${preview.documentRevision}${released ? ' → next revision' : ''}`} />
            </Stack>
            <Paper variant="outlined" sx={{ p: 1.5 }}>
              <Typography variant="subtitle2" sx={{ mb: 1 }}>Column mapping</Typography>
              <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(230px, 1fr))', gap: 1.5 }}>
                {(cols.data ?? []).map((c) => <TextField key={c.key} select size="small" label={`${c.label}${c.required ? ' *' : ''}`} helperText={c.help} error={c.required && !preview.mapping[c.key]}
                  value={mapping[c.key] ?? preview.mapping[c.key] ?? ''} onChange={(e) => remap(c.key, e.target.value)} slotProps={{ formHelperText: { sx: { m: 0, fontSize: 11 } } }}>
                  <MenuItem value="">— not imported —</MenuItem>{headers.map((h) => <MenuItem key={h} value={h}>{h}</MenuItem>)}</TextField>)}
              </Box>
              {preview.unmappedColumns.length > 0 && <Typography variant="caption" color="text.secondary">Columns not imported: {preview.unmappedColumns.join(', ')}</Typography>}
            </Paper>
            <Paper variant="outlined" sx={{ maxHeight: 340, overflow: 'auto' }}>
              <Table size="small" stickyHeader data-testid="import-rows">
                <TableHead><TableRow><TableCell>Row</TableCell><TableCell>Outcome</TableCell><TableCell>Line</TableCell>{kind === 'pfmea' && <><TableCell>RPN</TableCell><TableCell>AP</TableCell></>}<TableCell>Errors and notes</TableCell></TableRow></TableHead>
                <TableBody>{preview.rows.map((r) => <TableRow key={r.row}>
                  <TableCell sx={{ fontFamily: mono }}>{r.row}</TableCell><TableCell><Chip size="small" color={tone[r.status]} label={r.status} /></TableCell>
                  <TableCell sx={{ maxWidth: 320 }}>{Object.values(r.values).filter(Boolean).slice(0, 4).join(' · ')}</TableCell>
                  {kind === 'pfmea' && <><TableCell sx={{ fontFamily: mono }}>{r.calculated?.rpn ?? '—'}</TableCell><TableCell>{r.calculated?.actionPriority ?? '—'}</TableCell></>}
                  <TableCell>{[...r.errors, ...r.warnings].map((m, i) => <Typography key={i} variant="caption" display="block" color={r.errors.includes(m) ? 'error' : 'text.secondary'}>{m}</Typography>)}</TableCell>
                </TableRow>)}</TableBody>
              </Table>
            </Paper>
            {preview.rejected > 0 && <Stack direction="row" gap={2} alignItems="center">
              <FormControlLabel control={<Switch checked={partial} onChange={(e) => setPartial(e.target.checked)} />} label={`Import the ${preview.accepted} valid rows and leave the rejected ones out`} />
              <Button size="small" href={`/api/apqp/imports/${preview.importId}/rejected.csv`}>Download rejected rows</Button></Stack>}
          </>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={() => { reset(); onClose(); }}>Cancel</Button>
        <Button disabled={!file || busy || (released && !reason.trim())} onClick={() => send(true)}>Preview</Button>
        <Button variant="contained" disabled={!preview || busy || preview.accepted === 0 || preview.missingRequired.length > 0 || (preview.rejected > 0 && !partial) || (released && !reason.trim())} onClick={() => send(false)}>
          Import {preview ? preview.accepted : ''} line{preview?.accepted === 1 ? '' : 's'}</Button>
      </DialogActions>
    </Dialog>
  );
}

/** Imports and dry runs for every revision of the document, with the rejected-row download. */
export function ImportHistory({ kind, docId }: { kind: 'pfmea' | 'plans'; docId: string }) {
  const q = useQuery({ queryKey: ['apqp-imports', kind, docId], queryFn: () => get<any[]>(`/api/apqp/${kind}/${docId}/imports`) });
  return <DataTable data={q.data ?? []} empty="No imports yet." columns={[
    { header: 'When', accessorKey: 'importedAt', cell: (x) => fmtDateTime(x.getValue()) }, { header: 'By', accessorKey: 'importedBy' }, { header: 'File', accessorKey: 'fileName' },
    { header: 'Rev', accessorKey: 'documentRevision' }, { header: 'Mode', accessorKey: 'mode' }, { header: 'Result', accessorKey: 'status', cell: (x) => <StatusChip status={x.getValue() === 'DryRun' ? 'Preview' : x.getValue()} /> },
    { header: 'Rows', accessorFn: (r) => `${r.accepted} ok · ${r.rejected} rejected · ${r.duplicates} duplicate` },
    { header: '', id: 'd', cell: (x) => (x.row.original.rejected + x.row.original.duplicates > 0 ? <Button size="small" href={`/api/apqp/imports/${x.row.original.id}/rejected.csv`}>Rejected rows</Button> : null) }]} />;
}
