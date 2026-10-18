import { useEffect, useState } from 'react';
import { Alert, Box, Button, Chip, MenuItem, Paper, Stack, Tab, Table, TableBody, TableCell, TableHead, TableRow, Tabs, TextField, Tooltip, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { get, post, postBlob, put } from '../api';
import { FormView } from '../forms/FormView';
import { parseDefinition, type CellStyle } from '../forms/definition';
import { mono } from '../theme';

export interface FormulaInfo { location: string; formula: string; cached: string | null; classes: string[]; functions: string[]; dependencies: string[]; warning: string | null; translation: string | null }
export interface SheetInfo { name: string; index: number; visibility: string; rows: number; cols: number; cells: number; formulas: number; validations: number; conditionals: number; tables: number; merges: number; images: number; charts: number; pivots: number; comments: number; hyperlinks: number; protected: boolean; printArea: string | null; orientation: string }
export interface WorkbookSummary { format: string; calcMode: string; hasMacros: boolean; sheets: SheetInfo[]; names: { name: string; refersTo: string; scope: string | null; visible: boolean }[]; externalLinks: string[]; ddeLinks: string[]; warnings: string[]; fonts: string[]; formulaClasses: Record<string, number>; formulaTotal: number; formulas: FormulaInfo[]; security: string[] }
export interface FidelityItem { area: string; location: string; status: string; detail: string }
export interface FidelityReport { source: string; toolVersion: string; evaluationVersion: string; items: FidelityItem[]; formulas: { total: number; converted: number; preserved: number; notConverted: number }; missingFonts: string[]; fields: number; tables: number; signatures: number; rules: number }
export interface Conversion { definition: unknown; report: FidelityReport; map: { source: string; target: string; kind: string; label?: string | null }[]; formulas: FormulaInfo[] }
interface GridCell { r: number; c: number; text: string | null; formula: string | null; validation: boolean; name: string | null; rowSpan: number; colSpan: number; style: CellStyle | null; locked: boolean; comment: string | null }
interface Grid { sheet: string; r1: number; c1: number; r2: number; c2: number; widths: number[]; heights: number[]; cells: GridCell[]; truncated: boolean; sheets: string[] }

const CLASS_LABEL: Record<string, string> = { server: 'Evaluated', preserved: 'Kept, not evaluated', unsupported: 'Unsupported', external: 'External reference', macro: 'Macro-dependent', volatile: 'Volatile', review: 'Needs review' };
const STATUS_COLOR: Record<string, 'success' | 'info' | 'warning' | 'error' | 'default'> = { converted: 'success', approximated: 'info', preserved: 'default', 'not-converted': 'warning' };
const col = (n: number) => { let s = ''; while (n > 0) { const m = (n - 1) % 26; s = String.fromCharCode(65 + m) + s; n = Math.floor((n - 1) / 26); } return s; };
const border = (b?: string | null) => (!b ? undefined : `${b === 'medium' ? 2 : b === 'thick' || b === 'double' ? 3 : 1}px ${b === 'dashed' ? 'dashed' : b === 'dotted' ? 'dotted' : b === 'double' ? 'double' : 'solid'} #555`);

/** Secured grid of one worksheet of the preserved original: values (formula results as stored), merges, sizes, styles and indicators. */
export function WorkbookGrid({ jobId, sheet, onSheet }: { jobId: string; sheet?: string; onSheet?: (s: string) => void }) {
  const g = useQuery({ queryKey: ['import-grid', jobId, sheet ?? ''], queryFn: () => get<Grid>(`/api/imports/${jobId}/workbook${sheet ? `?sheet=${encodeURIComponent(sheet)}` : ''}`) });
  if (!g.data) return <Typography color="text.secondary">Loading the workbook…</Typography>;
  const d = g.data;
  const at = new Map(d.cells.map((c) => [`${c.r}:${c.c}`, c]));
  const covered = new Set<string>();
  d.cells.forEach((c) => { for (let r = c.r; r < c.r + c.rowSpan; r++) for (let k = c.c; k < c.c + c.colSpan; k++) if (r !== c.r || k !== c.c) covered.add(`${r}:${k}`); });
  const rows = Array.from({ length: d.r2 - d.r1 + 1 }, (_, i) => d.r1 + i);
  const cols = Array.from({ length: d.c2 - d.c1 + 1 }, (_, i) => d.c1 + i);
  return (
    <Box>
      {d.sheets.length > 1 && <Tabs value={d.sheet} onChange={(_, v) => onSheet?.(v)} variant="scrollable" sx={{ minHeight: 32, mb: 1 }} aria-label="Worksheets">
        {d.sheets.map((s) => <Tab key={s} value={s} label={s} sx={{ minHeight: 32, py: 0.5, textTransform: 'none' }} />)}</Tabs>}
      <Box sx={{ overflow: 'auto', maxHeight: 640, border: 1, borderColor: 'divider', bgcolor: '#fff' }}>
        <table aria-label={`Worksheet ${d.sheet}`} style={{ borderCollapse: 'collapse', tableLayout: 'fixed', fontSize: 12, color: '#111' }}>
          <colgroup><col style={{ width: 34 }} />{d.widths.map((w, i) => <col key={i} style={{ width: Math.max(8, w * 1.33) }} />)}</colgroup>
          <thead><tr><th style={{ background: '#eef1f5' }} />{cols.map((c) => <th key={c} style={{ background: '#eef1f5', fontWeight: 500, border: '1px solid #dde2ea' }}>{col(c)}</th>)}</tr></thead>
          <tbody>{rows.map((r, ri) => (
            <tr key={r} style={{ height: Math.max(14, d.heights[ri] * 1.33) }}>
              <th style={{ background: '#eef1f5', fontWeight: 500, border: '1px solid #dde2ea' }}>{r}</th>
              {cols.map((c) => {
                const k = `${r}:${c}`;
                if (covered.has(k)) return null;
                const x = at.get(k);
                const s = x?.style;
                const title = [x?.formula && `Formula ${x.formula}`, x?.validation && 'Data validation', x?.name && `Named range ${x.name}`, x?.comment && `Note: ${x.comment}`].filter(Boolean).join(' · ');
                return (
                  <td key={c} rowSpan={x?.rowSpan ?? 1} colSpan={x?.colSpan ?? 1} title={title || undefined} data-cell={`${col(c)}${r}`}
                    style={{ position: 'relative', overflow: 'hidden', whiteSpace: s?.wrap ? 'normal' : 'nowrap', padding: '1px 3px',
                      border: '1px solid #eceff3', borderTop: border(s?.borderTop), borderBottom: border(s?.borderBottom), borderLeft: border(s?.borderLeft), borderRight: border(s?.borderRight),
                      background: s?.fill ?? undefined, color: s?.colour ?? undefined, fontWeight: s?.bold ? 700 : undefined, fontStyle: s?.italic ? 'italic' : undefined,
                      textDecoration: s?.underline ? 'underline' : undefined, fontSize: s?.size ? s.size * 1.1 : undefined, textAlign: s?.align ?? undefined,
                      verticalAlign: s?.vAlign === 'middle' ? 'middle' : s?.vAlign === 'top' ? 'top' : 'bottom', fontFamily: s?.font === 'serif' ? 'Georgia, serif' : s?.font === 'mono' ? mono : undefined,
                      outline: x?.name ? '1px dotted #1257C4' : undefined }}>
                    {x?.text}
                    {x?.formula && <span aria-label="formula" style={{ position: 'absolute', top: 0, left: 0, borderTop: '6px solid #1257C4', borderRight: '6px solid transparent' }} />}
                    {x?.validation && <span aria-label="validation" style={{ position: 'absolute', bottom: 0, right: 1, fontSize: 9, color: '#1257C4' }}>▾</span>}
                  </td>);
              })}
            </tr>))}</tbody>
        </table>
      </Box>
      <Stack direction="row" gap={2} sx={{ mt: 0.5 }}>
        <Typography variant="caption"><span style={{ color: '#1257C4' }}>◤</span> formula</Typography>
        <Typography variant="caption"><span style={{ color: '#1257C4' }}>▾</span> data validation</Typography>
        <Typography variant="caption"><span style={{ outline: '1px dotted #1257C4' }}>&nbsp;&nbsp;&nbsp;</span> named range</Typography>
        {d.truncated && <Typography variant="caption" color="warning.main">Preview limited to the first rows and columns.</Typography>}
      </Stack>
    </Box>
  );
}

/** Side-by-side: the original worksheet and the converted form (interactive, or as it prints in the original look). */
export function ConversionPreview({ jobId, conversion }: { jobId: string; conversion: Conversion }) {
  const def = parseDefinition(conversion.definition as object);
  const [sheet, setSheet] = useState<string | undefined>();
  const [values, setValues] = useState<Record<string, string | null>>({});
  const [computed, setComputed] = useState<Record<string, string | null> | null>(null);
  const [view, setView] = useState<'form' | 'print'>('form');
  const [pdf, setPdf] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const schema = JSON.stringify(conversion.definition);
  const calc = async () => { setError(null); try { setComputed((await post<{ values: Record<string, string | null> }>('/api/forms/preview/evaluate', { fieldSchema: schema, values })).values); } catch (e) { setError((e as Error).message); } };
  useEffect(() => {
    if (view !== 'print') return;
    let url: string | null = null;
    postBlob('/api/forms/preview/pdf', { fieldSchema: schema, style: def.layout ? 'original' : 'standardized', values }).then((b) => { url = URL.createObjectURL(b); setPdf(url); }).catch((e) => setError((e as Error).message));
    return () => { if (url) URL.revokeObjectURL(url); };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [view]);
  return (
    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '1fr 1fr' }, gap: 2 }}>
      <Paper variant="outlined" sx={{ p: 1.5, minWidth: 0 }}>
        <Typography variant="subtitle1" gutterBottom>Original workbook</Typography>
        <WorkbookGrid jobId={jobId} sheet={sheet} onSheet={setSheet} />
      </Paper>
      <Paper variant="outlined" sx={{ p: 1.5, minWidth: 0 }}>
        <Stack direction="row" alignItems="center" gap={1} sx={{ mb: 1 }}>
          <Typography variant="subtitle1" sx={{ flex: 1 }}>Converted form</Typography>
          <Tabs value={view} onChange={(_, v) => setView(v)} sx={{ minHeight: 32 }}>
            <Tab value="form" label="Interactive" sx={{ minHeight: 32, py: 0.5 }} /><Tab value="print" label="Print layout" sx={{ minHeight: 32, py: 0.5 }} />
          </Tabs>
        </Stack>
        {error && <Alert severity="error" sx={{ mb: 1 }}>{error}</Alert>}
        {view === 'form' && <>
          <Alert severity="info" sx={{ mb: 1 }}>Try values here; calculated fields work as they did in the workbook. Nothing is saved.</Alert>
          <Box sx={{ maxHeight: 640, overflow: 'auto', pr: 1 }}><FormView def={def} values={{ ...values, ...(computed ?? {}) }} onChange={(v) => { setValues(v); setComputed(null); }} readOnly={false} /></Box>
          <Button variant="outlined" size="small" sx={{ mt: 1 }} onClick={calc}>Calculate</Button>
        </>}
        {view === 'print' && (pdf ? <iframe title="Converted form print layout" src={pdf} style={{ width: '100%', height: 640, border: 0 }} /> : <Typography color="text.secondary">Rendering…</Typography>)}
      </Paper>
    </Box>
  );
}

export function FidelityReportView({ report, map }: { report: FidelityReport; map?: Conversion['map'] }) {
  const [filter, setFilter] = useState('issues');
  const items = report.items.filter((i) => filter === 'all' || (filter === 'issues' ? i.status !== 'converted' : i.status === filter));
  return (
    <Stack gap={2}>
      <Stack direction="row" gap={1} flexWrap="wrap">
        <Chip label={`${report.fields} fields`} /><Chip label={`${report.tables} tables`} /><Chip label={`${report.signatures} signature blocks`} /><Chip label={`${report.rules} formatting rules`} />
        <Chip color="success" label={`${report.formulas.converted} of ${report.formulas.total} formulas live`} />
        {report.formulas.preserved > 0 && <Chip label={`${report.formulas.preserved} kept with stored result`} />}
        {report.formulas.notConverted > 0 && <Chip color="warning" label={`${report.formulas.notConverted} formulas not converted`} />}
        {report.missingFonts.length > 0 && <Chip color="info" label={`Fonts substituted: ${report.missingFonts.join(', ')}`} />}
      </Stack>
      <TextField select size="small" label="Show" value={filter} onChange={(e) => setFilter(e.target.value)} sx={{ width: 240 }}>
        <MenuItem value="issues">Not converted exactly</MenuItem><MenuItem value="not-converted">Not converted</MenuItem><MenuItem value="approximated">Approximated</MenuItem>
        <MenuItem value="preserved">Kept in the original only</MenuItem><MenuItem value="converted">Converted</MenuItem><MenuItem value="all">Everything</MenuItem>
      </TextField>
      <Paper variant="outlined" sx={{ overflowX: 'auto' }}>
        <Table size="small" aria-label="Fidelity report">
          <TableHead><TableRow>{['Result', 'Area', 'Where', 'Detail'].map((h) => <TableCell key={h} sx={{ fontWeight: 600 }}>{h}</TableCell>)}</TableRow></TableHead>
          <TableBody>{items.slice(0, 500).map((i, k) => <TableRow key={k}>
            <TableCell><Chip size="small" color={STATUS_COLOR[i.status] ?? 'default'} label={i.status.replace('-', ' ')} /></TableCell>
            <TableCell>{i.area}</TableCell><TableCell sx={{ fontFamily: mono, fontSize: 12 }}>{i.location}</TableCell><TableCell>{i.detail}</TableCell></TableRow>)}
            {!items.length && <TableRow><TableCell colSpan={4}>Nothing in this category.</TableCell></TableRow>}</TableBody>
        </Table>
      </Paper>
      {map && map.length > 0 && <>
        <Typography variant="subtitle1">Field mapping report</Typography>
        <Paper variant="outlined" sx={{ overflowX: 'auto', maxHeight: 360 }}>
          <Table size="small" stickyHeader aria-label="Field mapping report">
            <TableHead><TableRow>{['Source', 'Became', 'Kind', 'Label'].map((h) => <TableCell key={h} sx={{ fontWeight: 600 }}>{h}</TableCell>)}</TableRow></TableHead>
            <TableBody>{map.map((m, k) => <TableRow key={k}><TableCell sx={{ fontFamily: mono, fontSize: 12 }}>{m.source}</TableCell><TableCell sx={{ fontFamily: mono, fontSize: 12 }}>{m.target}</TableCell><TableCell>{m.kind}</TableCell><TableCell>{m.label}</TableCell></TableRow>)}</TableBody>
          </Table>
        </Paper>
      </>}
    </Stack>
  );
}

export function WorkbookInventory({ wb }: { wb: WorkbookSummary }) {
  return (
    <Stack gap={2}>
      {wb.security.length > 0 && <Alert severity="warning"><strong>Nothing active in this workbook is run.</strong> {wb.security.slice(0, 6).join(' ')}{wb.security.length > 6 ? ` (and ${wb.security.length - 6} more)` : ''}</Alert>}
      <Typography variant="body2">{wb.format.toUpperCase()} · calculation {wb.calcMode} · {wb.formulaTotal} formulas · {wb.names.length} named ranges{wb.hasMacros ? ' · contains macros (ignored)' : ''}</Typography>
      <Paper variant="outlined" sx={{ overflowX: 'auto' }}>
        <Table size="small" aria-label="Worksheets">
          <TableHead><TableRow>{['#', 'Worksheet', 'Visibility', 'Used range', 'Formulas', 'Validation', 'Conditional formats', 'Tables', 'Merged', 'Charts', 'Pivot tables', 'Images', 'Notes', 'Protected', 'Print area'].map((h) => <TableCell key={h} sx={{ fontWeight: 600 }}>{h}</TableCell>)}</TableRow></TableHead>
          <TableBody>{wb.sheets.map((s) => <TableRow key={s.name}>
            <TableCell>{s.index + 1}</TableCell><TableCell>{s.name}</TableCell><TableCell>{s.visibility}</TableCell><TableCell>{s.rows} × {s.cols}</TableCell><TableCell>{s.formulas}</TableCell>
            <TableCell>{s.validations}</TableCell><TableCell>{s.conditionals}</TableCell><TableCell>{s.tables}</TableCell><TableCell>{s.merges}</TableCell><TableCell>{s.charts}</TableCell>
            <TableCell>{s.pivots}</TableCell><TableCell>{s.images}</TableCell><TableCell>{s.comments}</TableCell><TableCell>{s.protected ? 'Yes' : 'No'}</TableCell><TableCell sx={{ fontFamily: mono }}>{s.printArea ?? '—'}</TableCell></TableRow>)}</TableBody>
        </Table>
      </Paper>
      {wb.names.length > 0 && <Stack direction="row" gap={1} flexWrap="wrap">{wb.names.map((n) => <Tooltip key={n.name + (n.scope ?? '')} title={n.refersTo}><Chip size="small" variant="outlined" label={`${n.name}${n.scope ? ` (${n.scope})` : ''}`} /></Tooltip>)}</Stack>}
      <Stack direction="row" gap={1} flexWrap="wrap">{Object.entries(wb.formulaClasses).map(([k, v]) => <Chip key={k} size="small" label={`${CLASS_LABEL[k] ?? k}: ${v}`} color={k === 'server' ? 'success' : k === 'volatile' || k === 'review' ? 'info' : 'warning'} />)}</Stack>
      <Paper variant="outlined" sx={{ overflowX: 'auto', maxHeight: 420 }}>
        <Table size="small" stickyHeader aria-label="Formula inventory">
          <TableHead><TableRow>{['Cell', 'Formula', 'Stored result', 'Classification', 'Note'].map((h) => <TableCell key={h} sx={{ fontWeight: 600 }}>{h}</TableCell>)}</TableRow></TableHead>
          <TableBody>{wb.formulas.slice(0, 1000).map((f) => <TableRow key={f.location}>
            <TableCell sx={{ fontFamily: mono, fontSize: 12 }}>{f.location}</TableCell><TableCell sx={{ fontFamily: mono, fontSize: 12, maxWidth: 380, wordBreak: 'break-all' }}>{f.formula}</TableCell>
            <TableCell>{f.cached}</TableCell><TableCell>{f.classes.map((c) => CLASS_LABEL[c] ?? c).join(', ')}</TableCell><TableCell>{f.warning}</TableCell></TableRow>)}</TableBody>
        </Table>
      </Paper>
    </Stack>
  );
}

/** Worksheet, header row and title settings for an Excel import; changing them inspects or converts the workbook again. */
export function ExcelSettingsPanel({ jobId, mode, sheets, current, editable, onDone }: { jobId: string; mode: string; sheets: SheetInfo[]; current: { sheet?: string | null; headerRow?: number | null; sheets?: string[] | null; title?: string | null; sheetUsed?: string | null; headerUsed?: number | null }; editable: boolean; onDone: (e?: string) => void }) {
  const [sheet, setSheet] = useState(current.sheet ?? current.sheetUsed ?? '');
  const [header, setHeader] = useState<string>(String(current.headerRow ?? current.headerUsed ?? ''));
  const [picked, setPicked] = useState<string[]>(current.sheets ?? []);
  const [title, setTitle] = useState(current.title ?? '');
  const save = async () => {
    try {
      await put(`/api/imports/${jobId}/excel-settings`, mode === 'template'
        ? { sheets: picked.length ? picked : null, title: title.trim() || null }
        : { sheet: sheet || null, headerRow: header ? Number(header) : null, title: title.trim() || null });
      onDone();
    } catch (e) { onDone((e as Error).message); }
  };
  return (
    <Paper variant="outlined" sx={{ p: 2 }}>
      <Typography variant="subtitle1" gutterBottom>Workbook settings</Typography>
      <Stack direction="row" gap={2} flexWrap="wrap" alignItems="center">
        {mode === 'template' ? <>
          <TextField select size="small" label="Worksheets to convert" SelectProps={{ multiple: true }} value={picked} disabled={!editable} onChange={(e) => setPicked(e.target.value as unknown as string[])} sx={{ minWidth: 260 }} helperText="Empty: every visible worksheet">
            {sheets.map((s) => <MenuItem key={s.name} value={s.name}>{s.name}{s.visibility !== 'visible' ? ` (${s.visibility})` : ''}</MenuItem>)}</TextField>
        </> : <>
          <TextField select size="small" label="Worksheet" value={sheet} disabled={!editable} onChange={(e) => setSheet(e.target.value)} sx={{ minWidth: 200 }}>
            {sheets.map((s) => <MenuItem key={s.name} value={s.name}>{s.name}</MenuItem>)}</TextField>
          <TextField size="small" type="number" label="Header row" value={header} disabled={!editable} onChange={(e) => setHeader(e.target.value)} sx={{ width: 120 }} />
        </>}
        {mode !== 'records' && mode !== 'master-list' && <TextField size="small" label={mode === 'template' ? 'Form title' : 'Document title'} value={title} disabled={!editable} onChange={(e) => setTitle(e.target.value)} sx={{ minWidth: 280 }} />}
        {editable && <Button variant="outlined" onClick={save}>Apply</Button>}
      </Stack>
    </Paper>
  );
}
