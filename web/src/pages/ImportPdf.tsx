import { useEffect, useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Alert, Box, Button, Checkbox, Chip, FormControlLabel, MenuItem, Paper, Stack, Switch, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { get, getBlob, put } from '../api';
import { mono } from '../theme';

export interface ConfidenceSummary { level: string; score: number; high: number; medium: number; low: number }
export interface PdfPage { number: number; width: number; height: number; orientation: string; textSource: string; status: string; statusDetail: string | null; ocrConfidence: number | null; words: number; images: number; confidence: ConfidenceSummary; detections: number }
export interface PdfSummary {
  kind: 'pdf' | 'image'; title: string | null; producer: string | null; toolVersion: string; fields: number; security: string[]; warnings: string[]; hasXfa: boolean; signed: boolean;
  images: { page: number; index: number; width: number; height: number; box: number[]; format: string }[];
  ocr: { provider: string; configured: boolean; renderer: boolean }; confidence: ConfidenceSummary; pages: PdfPage[];
  sections: { id: string; label: string; page: number; confidence: ConfidenceSummary }[];
}
export interface PdfDetection {
  id: string; kind: string; label: string; type: string; page: number; box: number[]; confidence: number; level: string; source: string; reason: string; options: string[] | null;
  fieldName: string | null; value: string | null; required: boolean; columns: { label: string; type: string; total: boolean }[] | null; rows: string[][] | null; include: boolean; confirmed: boolean; role: string | null;
}
export interface PdfSettings { pages?: string | null; table?: number | null; title?: string | null; ocr?: boolean | null; documentType?: string | null }

const LEVEL_COLOR: Record<string, 'success' | 'warning' | 'error' | 'default'> = { high: 'success', medium: 'warning', low: 'error', unmapped: 'default' };
const LEVEL_LABEL: Record<string, string> = { high: 'High', medium: 'Medium', low: 'Low', unmapped: 'Unmapped' };
export const ConfidenceChip = ({ level, score }: { level: string; score?: number }) =>
  <Chip size="small" color={LEVEL_COLOR[level] ?? 'default'} variant={level === 'high' ? 'outlined' : 'filled'} label={`${LEVEL_LABEL[level] ?? level}${score !== undefined ? ` · ${Math.round(score * 100)}%` : ''}`} />;

const KIND_LABEL: Record<string, string> = { field: 'Field', checkbox: 'Checkbox', table: 'Table', signature: 'Signature area', section: 'Section', instruction: 'Instructions' };
const TYPES = [['text', 'Text'], ['longtext', 'Long text'], ['number', 'Number'], ['integer', 'Whole number'], ['date', 'Date'], ['time', 'Time'], ['boolean', 'Checkbox'], ['choice', 'Dropdown'], ['radio', 'Options'], ['passfail', 'Pass / fail']];
const STATUS_LABEL: Record<string, string> = { read: 'Text read', recognized: 'Recognized (OCR)', 'needs-ocr': 'Needs OCR', 'ocr-failed': 'OCR failed', skipped: 'Skipped' };

/** The original PDF (or scanned image) beside the converted form; the file itself is shown, nothing in it is run. */
export function OriginalPdfPreview({ jobId }: { jobId: string }) {
  const [file, setFile] = useState<{ url: string; image: boolean } | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    let u: string | null = null;
    getBlob(`/api/imports/${jobId}/original-preview`).then((b) => { u = URL.createObjectURL(b); setFile({ url: u, image: b.type.startsWith('image/') }); }).catch((e) => setError((e as Error).message));
    return () => { if (u) URL.revokeObjectURL(u); };
  }, [jobId]);
  if (error) return <Alert severity="info">{error}</Alert>;
  if (!file) return <Typography color="text.secondary">Loading the original…</Typography>;
  return file.image
    ? <Box component="img" src={file.url} alt="Original scanned page" sx={{ width: '100%', maxHeight: 640, objectFit: 'contain', bgcolor: '#fff', border: 1, borderColor: 'divider' }} />
    : <iframe title="Original document" src={`${file.url}#navpanes=0&view=FitH`} style={{ width: '100%', height: 640, border: 0, background: '#fff' }} />;
}

/** Pages with how their text was obtained, OCR status and confidence; embedded pictures; files derived from the original. */
export function PdfPagesPanel({ jobId, pdf }: { jobId: string; pdf: PdfSummary }) {
  const derived = useQuery({ queryKey: ['import-derived', jobId], queryFn: () => get<{ id: string; fileName: string; category: string; contentType: string; sizeBytes: number; createdAt: string }[]>(`/api/imports/${jobId}/derived`) });
  return (
    <Stack gap={2}>
      {pdf.security.length > 0 && <Alert severity="warning"><strong>Nothing active in this PDF is run or retrieved.</strong> {pdf.security.slice(0, 6).join(' ')}{pdf.security.length > 6 ? ` (and ${pdf.security.length - 6} more)` : ''}</Alert>}
      {pdf.warnings.map((w) => <Alert key={w} severity="info">{w}</Alert>)}
      {!pdf.ocr.configured && pdf.pages.some((p) => p.status === 'needs-ocr') && <Alert severity="info">OCR is not configured on this server, so pages without a text layer were not recognized. Keep the file as a controlled document, or ask an administrator to configure an OCR provider.</Alert>}
      <Stack direction="row" gap={1} flexWrap="wrap" alignItems="center">
        <Chip size="small" variant="outlined" label={`${pdf.pages.length} page(s)`} />
        <Chip size="small" variant="outlined" label={`${pdf.fields} fillable fields`} />
        <Chip size="small" variant="outlined" label={`${pdf.images.length} pictures`} />
        {pdf.hasXfa && <Chip size="small" color="warning" label="XFA form" />}
        {pdf.signed && <Chip size="small" color="info" label="Digitally signed" />}
        <Chip size="small" variant="outlined" label={`OCR: ${pdf.ocr.configured ? pdf.ocr.provider : 'not configured'}`} />
        <Typography variant="body2">Document confidence</Typography><ConfidenceChip level={pdf.confidence.level} score={pdf.confidence.score} />
        <Typography variant="body2" color="text.secondary">Reader {pdf.toolVersion}</Typography>
      </Stack>
      <Paper variant="outlined" sx={{ overflowX: 'auto' }}>
        <Table size="small" aria-label="Pages">
          <TableHead><TableRow>{['Page', 'Size', 'Text', 'Status', 'OCR confidence', 'Detected', 'Confidence'].map((h) => <TableCell key={h} sx={{ fontWeight: 600 }}>{h}</TableCell>)}</TableRow></TableHead>
          <TableBody>{pdf.pages.map((p) => (
            <TableRow key={p.number}>
              <TableCell>{p.number}</TableCell>
              <TableCell>{Math.round(p.width)} × {Math.round(p.height)} pt, {p.orientation}</TableCell>
              <TableCell>{p.textSource === 'text' ? `Text layer (${p.words} words)` : p.textSource === 'ocr' ? `Recognized (${p.words} words)` : 'None (scanned)'}</TableCell>
              <TableCell><Chip size="small" color={p.status === 'read' || p.status === 'recognized' ? 'success' : 'warning'} variant="outlined" label={STATUS_LABEL[p.status] ?? p.status} />{p.statusDetail && <Typography variant="caption" display="block">{p.statusDetail}</Typography>}</TableCell>
              <TableCell>{p.ocrConfidence != null ? `${Math.round(p.ocrConfidence * 100)}%` : '—'}</TableCell>
              <TableCell>{p.detections}</TableCell>
              <TableCell><ConfidenceChip level={p.confidence.level} /></TableCell>
            </TableRow>))}</TableBody>
        </Table>
      </Paper>
      {pdf.sections.length > 0 && <Stack direction="row" gap={1} flexWrap="wrap" alignItems="center" aria-label="Section confidence">
        <Typography variant="body2">Sections:</Typography>
        {pdf.sections.map((s) => <Chip key={s.id} size="small" variant="outlined" color={LEVEL_COLOR[s.confidence.level] ?? 'default'} label={`${s.label} · ${LEVEL_LABEL[s.confidence.level] ?? s.confidence.level}`} />)}
      </Stack>}
      {pdf.images.length > 0 && <Paper variant="outlined" sx={{ p: 1.5 }}>
        <Typography variant="subtitle2" gutterBottom>Pictures</Typography>
        <Stack direction="row" gap={1} flexWrap="wrap">{pdf.images.map((i) => <Button key={`${i.page}-${i.index}`} size="small" variant="outlined" href={`/api/imports/${jobId}/pdf-images/${i.page}/${i.index}`}>Page {i.page} picture {i.index} ({i.width} × {i.height})</Button>)}</Stack>
      </Paper>}
      {(derived.data ?? []).length > 0 && <Paper variant="outlined" sx={{ overflowX: 'auto' }}>
        <Table size="small" aria-label="Derived files">
          <TableHead><TableRow>{['File', 'Kind', 'Size'].map((h) => <TableCell key={h} sx={{ fontWeight: 600 }}>{h}</TableCell>)}</TableRow></TableHead>
          <TableBody>{derived.data!.map((f) => <TableRow key={f.id}><TableCell><a href={`/api/imports/${jobId}/derived/${f.id}`}>{f.fileName}</a></TableCell><TableCell>{f.category}</TableCell><TableCell>{(f.sizeBytes / 1024).toFixed(1)} KB</TableCell></TableRow>)}</TableBody>
        </Table>
      </Paper>}
    </Stack>
  );
}

type Edit = { include?: boolean; label?: string; type?: string; options?: string; confirmed?: boolean };
const needs = (d: PdfDetection) => d.include && !d.confirmed && (d.level !== 'high' || d.source === 'ocr');

/** Review of everything detected: keep or leave out, correct label, type and options, and confirm. Medium, low and recognized items must be confirmed before submitting. */
export function DetectionReview({ jobId, detections, required, editable, onDone }: { jobId: string; detections: PdfDetection[]; required: number; editable: boolean; onDone: (e?: string) => void }) {
  const [edits, setEdits] = useState<Record<string, Edit>>({});
  const [onlyOpen, setOnlyOpen] = useState(required > 0);
  const rows = useMemo(() => detections.filter((d) => !onlyOpen || needs(d)), [detections, onlyOpen]);
  const set = (id: string, patch: Edit) => setEdits((e) => ({ ...e, [id]: { ...e[id], ...patch } }));
  const save = async (confirmAll: boolean) => {
    try {
      const items = Object.entries(edits).map(([id, e]) => ({ id, include: e.include ?? null, label: e.label ?? null, type: e.type ?? null,
        options: e.options !== undefined ? e.options.split(',').map((o) => o.trim()).filter(Boolean) : null, confirmed: e.confirmed ?? null }));
      await put(`/api/imports/${jobId}/detections`, { items, confirmAll });
      setEdits({}); onDone();
    } catch (e) { onDone((e as Error).message); }
  };
  return (
    <Stack gap={1.5}>
      <Alert severity={required > 0 ? 'warning' : 'success'}>{required > 0
        ? `${required} detection(s) need confirmation: everything with medium or low confidence, and everything read by OCR. Correct what is wrong, leave out what is not part of the form, then confirm.`
        : 'Every detection has been reviewed or has high confidence.'}</Alert>
      <Stack direction="row" gap={2} alignItems="center" flexWrap="wrap">
        <FormControlLabel control={<Switch checked={onlyOpen} onChange={(e) => setOnlyOpen(e.target.checked)} />} label="Only those needing confirmation" />
        {editable && <><Button variant="outlined" disabled={!Object.keys(edits).length} onClick={() => save(false)}>Save review</Button>
          <Button variant="contained" onClick={() => save(true)}>Confirm all</Button></>}
      </Stack>
      <Paper variant="outlined" sx={{ overflowX: 'auto' }}>
        <Table size="small" aria-label="Detections">
          <TableHead><TableRow>{['Keep', 'Kind', 'Label', 'Type', 'Options or columns', 'Page', 'Confidence', 'Found by', 'Why', 'Confirmed'].map((h) => <TableCell key={h} sx={{ fontWeight: 600 }}>{h}</TableCell>)}</TableRow></TableHead>
          <TableBody>{rows.map((d) => {
            const e = edits[d.id] ?? {};
            const include = e.include ?? d.include; const confirmed = e.confirmed ?? d.confirmed;
            const open = include && !confirmed && (d.level !== 'high' || d.source === 'ocr');
            const typed = d.kind === 'field';
            return (
              <TableRow key={d.id} sx={{ bgcolor: open ? 'rgba(237,108,2,0.06)' : undefined, opacity: include ? 1 : 0.55 }}>
                <TableCell><Checkbox size="small" checked={include} disabled={!editable} onChange={(x) => set(d.id, { include: x.target.checked })} inputProps={{ 'aria-label': `Keep ${d.label}` }} /></TableCell>
                <TableCell>{KIND_LABEL[d.kind] ?? d.kind}</TableCell>
                <TableCell><TextField size="small" value={e.label ?? d.label} disabled={!editable || d.kind === 'instruction'} onChange={(x) => set(d.id, { label: x.target.value })} inputProps={{ 'aria-label': `Label for ${d.label}` }} sx={{ minWidth: 200 }} /></TableCell>
                <TableCell>{typed ? <TextField select size="small" value={e.type ?? d.type} disabled={!editable} onChange={(x) => set(d.id, { type: x.target.value })} sx={{ minWidth: 130 }} SelectProps={{ SelectDisplayProps: { 'aria-label': `Type for ${d.label}` } as object }}>
                  {TYPES.map(([v, l]) => <MenuItem key={v} value={v}>{l}</MenuItem>)}</TextField> : d.kind === 'checkbox' ? 'Checkbox' : '—'}</TableCell>
                <TableCell sx={{ fontSize: 12, maxWidth: 240 }}>{d.kind === 'table'
                  ? (d.columns ?? []).map((c) => `${c.label}${c.total ? ' (total)' : ''}`).join(' · ')
                  : typed && ['choice', 'radio'].includes(e.type ?? d.type)
                    ? <TextField size="small" value={e.options ?? (d.options ?? []).join(', ')} disabled={!editable} onChange={(x) => set(d.id, { options: x.target.value })} inputProps={{ 'aria-label': `Options for ${d.label}` }} />
                    : d.role ? `signs as ${d.role}` : d.value ? `value: ${d.value}` : ''}</TableCell>
                <TableCell>{d.page}</TableCell>
                <TableCell sx={{ whiteSpace: 'nowrap' }}><ConfidenceChip level={d.level} score={d.confidence} /></TableCell>
                <TableCell sx={{ whiteSpace: 'nowrap' }}>{d.source === 'form-field' ? <span>Fillable field <span style={{ fontFamily: mono, fontSize: 11 }}>{d.fieldName}</span></span> : d.source === 'ocr' ? 'Recognized text' : 'Text layer'}</TableCell>
                <TableCell sx={{ fontSize: 12, minWidth: 200, maxWidth: 300 }}>{d.reason}</TableCell>
                <TableCell><Checkbox size="small" checked={confirmed} disabled={!editable} onChange={(x) => set(d.id, { confirmed: x.target.checked })} inputProps={{ 'aria-label': `Confirm ${d.label}` }} /></TableCell>
              </TableRow>);
          })}</TableBody>
        </Table>
      </Paper>
    </Stack>
  );
}

/** PDF settings: title and OCR for conversions; pages and table for data extraction. */
export function PdfSettingsPanel({ jobId, mode, pdf, detections, current, editable, onDone }: { jobId: string; mode: string; pdf: PdfSummary; detections: PdfDetection[]; current: PdfSettings; editable: boolean; onDone: (e?: string) => void }) {
  const [title, setTitle] = useState(current.title ?? '');
  const [pages, setPages] = useState(current.pages ?? '');
  const [table, setTable] = useState(current.table ? String(current.table) : '');
  const [ocr, setOcr] = useState(current.ocr !== false);
  const tables = detections.filter((d) => d.kind === 'table' && d.include && (d.rows?.length ?? 0) > 0);
  const save = async () => {
    try {
      await put(`/api/imports/${jobId}/pdf-settings`, { pages: mode === 'records' && pages.trim() ? pages.trim() : null, table: mode === 'records' && table ? Number(table) : null, title: title.trim() || null, ocr, documentType: null });
      onDone();
    } catch (e) { onDone((e as Error).message); }
  };
  return (
    <Paper variant="outlined" sx={{ p: 2 }}>
      <Typography variant="subtitle1" gutterBottom>{pdf.kind === 'image' ? 'Scanned image settings' : 'PDF settings'}</Typography>
      <Stack direction="row" gap={2} flexWrap="wrap" alignItems="center">
        {mode === 'records' && pdf.kind === 'pdf' && <TextField size="small" label="Pages" placeholder={`1-${pdf.pages.length}`} helperText="e.g. 1-3, 5; empty for all" value={pages} disabled={!editable} onChange={(e) => setPages(e.target.value)} sx={{ width: 170 }} />}
        {mode === 'records' && <TextField select size="small" label="Extract from" value={table} disabled={!editable} onChange={(e) => setTable(e.target.value)} sx={{ minWidth: 280 }}>
          <MenuItem value="">{pdf.fields > 0 ? 'Fillable fields (one record)' : 'First table'}</MenuItem>
          {tables.map((t, k) => <MenuItem key={t.id} value={String(k + 1)}>Table {k + 1} on page {t.page} ({t.rows!.length} rows): {t.label}</MenuItem>)}</TextField>}
        {mode !== 'records' && <TextField size="small" label={mode === 'template' ? 'Form title' : 'Document title'} value={title} disabled={!editable} onChange={(e) => setTitle(e.target.value)} sx={{ minWidth: 280 }} />}
        {mode !== 'document' && <FormControlLabel control={<Switch checked={ocr} disabled={!editable || !pdf.ocr.configured} onChange={(e) => setOcr(e.target.checked)} />}
          label={pdf.ocr.configured ? 'Recognize pages without text (OCR)' : 'OCR is not configured on this server'} />}
        {editable && <Button variant="outlined" onClick={save}>Apply</Button>}
      </Stack>
    </Paper>
  );
}
