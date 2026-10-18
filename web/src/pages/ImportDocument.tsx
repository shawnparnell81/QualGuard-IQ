import { useEffect, useState } from 'react';
import { Alert, Box, Button, Chip, FormControlLabel, MenuItem, Paper, Stack, Switch, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { getBlob, put } from '../api';
import { mono } from '../theme';
import type { FidelityReport } from './ImportWorkbook';

export interface DocControl { tag: string | null; alias: string | null; type: string; value: string; legacy: boolean }
export interface DocTable { index: number; rows: number; columns: number; header: string }
export interface DocumentSummary {
  title: string | null; paragraphs: number; headings: number; listParagraphs: number; tables: DocTable[]; controls: DocControl[]; sections: number; orientations: string[];
  images: number; footnotes: number; endnotes: number; bookmarks: number; hyperlinks: number; textBoxes: number; comments: number; signatureLines: number;
  header: string | null; footer: string | null; fonts: string[]; security: string[]; warnings: string[]; hasMacros: boolean; toolVersion: string;
  outline: { kind: string; heading: number | null; list: string | null; text: string }[];
}
export interface WordSettings { fillable?: boolean | null; table?: number | null; title?: string | null; documentType?: string | null }

/** The original Word document rendered to PDF by the server (nothing in it is run); falls back to the extracted outline when no converter is configured. */
export function OriginalDocumentPreview({ jobId, doc }: { jobId: string; doc?: DocumentSummary }) {
  const [url, setUrl] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    let u: string | null = null;
    getBlob(`/api/imports/${jobId}/original-preview`).then((b) => { u = URL.createObjectURL(b); setUrl(u); }).catch((e) => setError((e as Error).message));
    return () => { if (u) URL.revokeObjectURL(u); };
  }, [jobId]);
  if (url) return <iframe title="Original document" src={`${url}#navpanes=0&view=FitH`} style={{ width: '100%', height: 640, border: 0, background: '#fff' }} />;
  if (!error) return <Typography color="text.secondary">Rendering the original…</Typography>;
  return (
    <Stack gap={1}>
      <Alert severity="info">{error}</Alert>
      {doc && <DocumentOutline doc={doc} />}
    </Stack>
  );
}

function DocumentOutline({ doc }: { doc: DocumentSummary }) {
  return (
    <Box aria-label="Document outline" sx={{ maxHeight: 560, overflow: 'auto', bgcolor: '#fff', color: '#111', border: 1, borderColor: 'divider', p: 2 }}>
      {doc.outline.map((o, k) => o.kind === 'section'
        ? <Box key={k} sx={{ borderTop: '1px dashed #999', my: 1, fontSize: 11, color: '#666' }}>Section break ({o.text})</Box>
        : o.kind === 'table'
          ? <Box key={k} sx={{ border: '1px solid #888', p: 0.5, my: 0.5, fontSize: 12 }}>Table: {o.text}</Box>
          : <Typography key={k} sx={{ fontSize: o.heading ? 18 - Math.min(o.heading, 4) * 1.5 : 13, fontWeight: o.heading ? 700 : 400, pl: o.list ? 2 : 0 }}>{o.list ? `${o.list} ` : ''}{o.text}</Typography>)}
    </Box>
  );
}

const DIFF_GROUPS: { key: string; label: string; test: (i: FidelityReport['items'][number]) => boolean; color: 'warning' | 'info' | 'default' }[] = [
  { key: 'unsupported', label: 'Unsupported elements', test: (i) => i.status === 'not-converted', color: 'warning' },
  { key: 'fonts', label: 'Missing fonts', test: (i) => i.area === 'font', color: 'info' },
  { key: 'dimensions', label: 'Changed dimensions', test: (i) => i.area === 'print' && i.status === 'approximated', color: 'info' },
  { key: 'reflow', label: 'Reflow warnings', test: (i) => /reflow/i.test(i.detail), color: 'info' },
  { key: 'approximated', label: 'Other approximations', test: (i) => i.status === 'approximated' && i.area !== 'font' && i.area !== 'print' && !/reflow/i.test(i.detail), color: 'default' },
  { key: 'preserved', label: 'Kept in the original only', test: (i) => i.status === 'preserved', color: 'default' },
];

/** Differences between the original and the converted form, grouped the way the fidelity review asks: unsupported elements, missing fonts, changed dimensions, reflow. */
export function DocumentDifferences({ report }: { report: FidelityReport }) {
  return (
    <Paper variant="outlined" sx={{ p: 1.5 }} aria-label="Differences">
      <Typography variant="subtitle1" gutterBottom>Differences from the original</Typography>
      <Stack gap={1}>
        {DIFF_GROUPS.map((g) => {
          const items = report.items.filter(g.test);
          return (
            <Box key={g.key}>
              <Stack direction="row" gap={1} alignItems="center"><Chip size="small" color={items.length ? g.color : 'success'} label={`${g.label}: ${items.length}`} /></Stack>
              {items.slice(0, 50).map((i, k) => (
                <Typography key={k} variant="body2" sx={{ pl: 1, borderLeft: 3, borderColor: g.color === 'warning' ? 'warning.main' : g.color === 'info' ? 'info.main' : 'divider', my: 0.5, bgcolor: g.color === 'warning' ? 'rgba(237,108,2,0.06)' : undefined }}>
                  <span style={{ fontFamily: mono, fontSize: 12 }}>{i.location}</span> · {i.detail}
                </Typography>))}
            </Box>);
        })}
      </Stack>
    </Paper>
  );
}

export function DocumentInventory({ doc }: { doc: DocumentSummary }) {
  return (
    <Stack gap={2}>
      {(doc.security.length > 0 || doc.hasMacros) && <Alert severity="warning"><strong>Nothing active in this document is run or retrieved.</strong> {doc.hasMacros ? 'Contains macros (ignored). ' : ''}{doc.security.slice(0, 6).join(' ')}{doc.security.length > 6 ? ` (and ${doc.security.length - 6} more)` : ''}</Alert>}
      {doc.warnings.map((w) => <Alert key={w} severity="info">{w}</Alert>)}
      <Stack direction="row" gap={1} flexWrap="wrap">
        {[[`${doc.paragraphs} paragraphs`], [`${doc.headings} headings`], [`${doc.listParagraphs} list items`], [`${doc.tables.length} tables`], [`${doc.controls.length} content controls`],
          [`${doc.sections} section(s): ${[...new Set(doc.orientations)].join(', ')}`], [`${doc.images} images`], [`${doc.hyperlinks} hyperlinks`], [`${doc.bookmarks} bookmarks`],
          [`${doc.footnotes} footnotes`], [`${doc.endnotes} endnotes`], [`${doc.signatureLines} signature lines`], [`${doc.textBoxes} text boxes`], [`${doc.comments} comments`]]
          .map(([l]) => <Chip key={l} size="small" variant="outlined" label={l} />)}
      </Stack>
      <Typography variant="body2">Header: {doc.header ?? '—'} · Footer: {doc.footer ?? '—'} · Fonts: {doc.fonts.join(', ') || '—'} · Reader {doc.toolVersion}</Typography>
      {doc.controls.length > 0 && <Paper variant="outlined" sx={{ overflowX: 'auto' }}>
        <Table size="small" aria-label="Content controls">
          <TableHead><TableRow>{['Tag', 'Title', 'Kind', 'Value'].map((h) => <TableCell key={h} sx={{ fontWeight: 600 }}>{h}</TableCell>)}</TableRow></TableHead>
          <TableBody>{doc.controls.map((c, k) => <TableRow key={k}><TableCell sx={{ fontFamily: mono }}>{c.tag ?? '—'}</TableCell><TableCell>{c.alias ?? '—'}</TableCell><TableCell>{c.legacy ? `legacy ${c.type}` : c.type}</TableCell><TableCell>{c.value}</TableCell></TableRow>)}</TableBody>
        </Table>
      </Paper>}
      {doc.tables.length > 0 && <Paper variant="outlined" sx={{ overflowX: 'auto' }}>
        <Table size="small" aria-label="Document tables">
          <TableHead><TableRow>{['#', 'Rows', 'Columns', 'First row'].map((h) => <TableCell key={h} sx={{ fontWeight: 600 }}>{h}</TableCell>)}</TableRow></TableHead>
          <TableBody>{doc.tables.map((t) => <TableRow key={t.index}><TableCell>{t.index}</TableCell><TableCell>{t.rows}</TableCell><TableCell>{t.columns}</TableCell><TableCell>{t.header}</TableCell></TableRow>)}</TableBody>
        </Table>
      </Paper>}
      <DocumentOutline doc={doc} />
    </Stack>
  );
}

/** Word settings: fillable document record (mode B) or electronic form (mode C) and the title; for data extraction (mode D), content controls or a table. */
export function WordSettingsPanel({ jobId, mode, doc, current, editable, onDone }: { jobId: string; mode: string; doc: DocumentSummary; current: WordSettings; editable: boolean; onDone: (e?: string) => void }) {
  const [fillable, setFillable] = useState(current.fillable === true);
  const [title, setTitle] = useState(current.title ?? '');
  const [table, setTable] = useState<string>(current.table ? String(current.table) : '');
  const save = async () => {
    try {
      await put(`/api/imports/${jobId}/word-settings`, { fillable: mode === 'template' ? fillable : null, table: mode === 'records' && table ? Number(table) : null, title: title.trim() || null, documentType: null });
      onDone();
    } catch (e) { onDone((e as Error).message); }
  };
  return (
    <Paper variant="outlined" sx={{ p: 2 }}>
      <Typography variant="subtitle1" gutterBottom>Document settings</Typography>
      <Stack direction="row" gap={2} flexWrap="wrap" alignItems="center">
        {mode === 'template' && <FormControlLabel control={<Switch checked={fillable} disabled={!editable} onChange={(e) => setFillable(e.target.checked)} />}
          label={fillable ? 'Fillable document: only the inputs in the document become fields' : 'Electronic form: tables, placeholders and labels become fields'} />}
        {mode === 'records' && <TextField select size="small" label="Extract from" value={table} disabled={!editable} onChange={(e) => setTable(e.target.value)} sx={{ minWidth: 280 }}>
          <MenuItem value="">{doc.controls.some((c) => c.tag || c.alias) ? 'Named content controls' : 'First table'}</MenuItem>
          {doc.tables.map((t) => <MenuItem key={t.index} value={String(t.index)}>Table {t.index} ({t.rows} rows): {t.header.slice(0, 60)}</MenuItem>)}</TextField>}
        {mode !== 'records' && <TextField size="small" label={mode === 'template' ? 'Form title' : 'Document title'} value={title} disabled={!editable} onChange={(e) => setTitle(e.target.value)} sx={{ minWidth: 280 }} />}
        {editable && <Button variant="outlined" onClick={save}>Apply</Button>}
      </Stack>
    </Paper>
  );
}
