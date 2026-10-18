import { useState, type DragEvent } from 'react';
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Checkbox, Chip, FormControlLabel, LinearProgress, MenuItem, Pagination, Paper, Stack, Tab, Table, TableBody, TableCell, TableHead, TableRow, Tabs, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { get, post, put } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { fmtDate } from '../components/Status';
import { mono } from '../theme';
import { ConversionPreview, ExcelSettingsPanel, FidelityReportView, WorkbookGrid, WorkbookInventory, type Conversion, type WorkbookSummary } from './ImportWorkbook';

export const IMPORT_STATUS = ['Uploaded', 'Security scan pending', 'Security scan failed', 'Inspecting', 'Parsing', 'Mapping required', 'Preview ready', 'Validation failed',
  'Awaiting approval', 'Importing', 'Imported', 'Partially imported', 'Completed with warnings', 'Failed', 'Canceled', 'Rolled back', 'Archived'];
const statusColor = (s: number): 'default' | 'success' | 'warning' | 'error' | 'info' =>
  [10].includes(s) ? 'success' : [11, 12, 7].includes(s) ? 'warning' : [2, 13].includes(s) ? 'error' : [8, 6].includes(s) ? 'info' : 'default';
const SEVERITY = ['Info', 'Warning', 'Error'];
const CONFIDENCE = ['Unmapped', 'Low', 'Medium', 'High', 'Manual'];
const docsModule = () => modules.find((m) => m.key === 'documents')!;

interface Job { id: string; number: string; originalFileName: string; fileType: string; importMode: string; targetModule: string | null; status: number; progress: number; totalRows: number; importedRows: number; warningCount: number; errorCount: number; submittedAt: string; submittedBy: string; siteCode: string }
interface Site { id: string; code: string; name: string }
interface Target { key: string; name: string; needsTemplate: boolean }

export function ImportCenter() {
  const nav = useNavigate();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const view = p.get('view') ?? 'all';
  const page = Number(p.get('page') ?? 1);
  const statusFor: Record<string, string> = { review: '8', failed: '13', done: '10', archived: '16' };
  const list = useQuery({
    queryKey: ['imports', view, page],
    queryFn: () => get<{ items: Job[]; total: number; pageSize: number }>(`/api/imports?page=${page}&pageSize=25${statusFor[view] ? `&status=${statusFor[view]}` : ''}${view === 'warnings' ? '&warnings=true' : ''}`),
    refetchInterval: 5000,
  });
  const tabs = [{ label: 'Import jobs', value: 'all' }, { label: 'Conversion warnings', value: 'warnings' }, { label: 'Awaiting approval', value: 'review' }, { label: 'Imported', value: 'done' }, { label: 'Failed', value: 'failed' }, { label: 'Migration history', value: 'archived' }];
  return (
    <ModuleWorkspace module={docsModule()} crumbs={[{ label: 'Import Center' }]} title="Import Center" tabs={tabs} tab={view}
      onTab={(v) => setP(v === 'all' ? {} : { view: v })}>
      {can('imports.edit') && <UploadPanel onStarted={(id) => nav(`/imports/${id}`)} />}
      <Paper variant="outlined" sx={{ overflowX: 'auto', mt: 2 }}>
        <Table size="small" aria-label="Import jobs">
          <TableHead><TableRow>{['Import', 'File', 'Type', 'Into', 'Status', 'Rows', 'Warnings', 'Errors', 'Submitted', 'By', 'Site'].map((h) => <TableCell key={h} sx={{ fontWeight: 600 }}>{h}</TableCell>)}</TableRow></TableHead>
          <TableBody>
            {list.data?.items.map((j) => (
              <TableRow key={j.id} hover sx={{ cursor: 'pointer' }} onClick={() => nav(`/imports/${j.id}`)}>
                <TableCell sx={{ fontFamily: mono }}>{j.number}</TableCell><TableCell>{j.originalFileName}</TableCell><TableCell>{j.fileType.toUpperCase()}</TableCell>
                <TableCell>{j.targetModule ?? '—'}</TableCell>
                <TableCell><Chip size="small" color={statusColor(j.status)} label={IMPORT_STATUS[j.status]} /></TableCell>
                <TableCell>{j.importedRows ? `${j.importedRows} / ${j.totalRows}` : j.totalRows}</TableCell><TableCell>{j.warningCount}</TableCell><TableCell>{j.errorCount}</TableCell>
                <TableCell>{fmtDate(j.submittedAt)}</TableCell><TableCell>{j.submittedBy}</TableCell><TableCell>{j.siteCode}</TableCell>
              </TableRow>))}
            {list.data && !list.data.items.length && <TableRow><TableCell colSpan={11}><Typography color="text.secondary">No imports here.</Typography></TableCell></TableRow>}
          </TableBody>
        </Table>
      </Paper>
      {list.data && list.data.total > list.data.pageSize && <Pagination sx={{ mt: 2 }} count={Math.ceil(list.data.total / list.data.pageSize)} page={page} onChange={(_, v) => { const n = new URLSearchParams(p); n.set('page', String(v)); setP(n); }} />}
    </ModuleWorkspace>
  );
}

function UploadPanel({ onStarted }: { onStarted: (id: string) => void }) {
  const sites = useQuery({ queryKey: ['sites'], queryFn: () => get<Site[]>('/api/organization/sites') });
  const targets = useQuery({ queryKey: ['import-targets'], queryFn: () => get<Target[]>('/api/imports/targets') });
  const templates = useQuery({ queryKey: ['import-templates'], queryFn: () => get<{ items: { id: string; number: string; title: string }[] }>('/api/template-library?view=effective&pageSize=200') });
  const [siteId, setSiteId] = useState('');
  const [target, setTarget] = useState('form-records');
  const [mode, setMode] = useState('records');
  const [templateId, setTemplateId] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const [over, setOver] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const site = siteId || sites.data?.[0]?.id || '';
  const drop = (e: DragEvent) => { e.preventDefault(); setOver(false); const f = e.dataTransfer.files?.[0]; if (f) setFile(f); };
  const start = async () => {
    if (!file) return;
    setError(null); setBusy(true);
    try {
      const fd = new FormData();
      fd.append('file', file); fd.append('siteId', site); fd.append('mode', mode);
      if (mode === 'records') { fd.append('targetModule', target); if (templateId) fd.append('targetTemplateId', templateId); }
      onStarted(await post<string>('/api/imports', fd));
    } catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  };
  const t = mode === 'records' ? targets.data?.find((x) => x.key === target) : undefined;
  const isBook = !!file && /\.(xlsx|xlsm|xls)$/i.test(file.name);
  return (
    <Paper variant="outlined" sx={{ p: 2 }}>
      <Typography variant="h6" gutterBottom>Import data</Typography>
      <Box onDragOver={(e) => { e.preventDefault(); setOver(true); }} onDragLeave={() => setOver(false)} onDrop={drop} aria-label="Drop a file here"
        sx={{ border: '2px dashed', borderColor: over ? 'primary.main' : 'divider', borderRadius: 1, p: 3, textAlign: 'center', bgcolor: over ? 'action.hover' : undefined, mb: 2 }}>
        <Typography>{file ? `${file.name} (${(file.size / 1024).toFixed(1)} KB)` : 'Drop a CSV or Excel file here, or choose one.'}</Typography>
        <Button component="label" sx={{ mt: 1 }}>Choose file<input hidden type="file" data-testid="import-file" accept=".csv,.txt,.tsv,.xlsx,.xlsm,.xls" onChange={(e) => { const f = e.target.files?.[0] ?? null; setFile(f); if (f && !/\.(xlsx|xlsm|xls)$/i.test(f.name)) setMode('records'); }} /></Button>
      </Box>
      <Stack direction={{ xs: 'column', md: 'row' }} gap={2}>
        <TextField select size="small" label="Site" value={site} onChange={(e) => setSiteId(e.target.value)} sx={{ minWidth: 180 }}>
          {(sites.data ?? []).map((s) => <MenuItem key={s.id} value={s.id}>{s.code} · {s.name}</MenuItem>)}</TextField>
        <TextField select size="small" label="What to do" value={mode} onChange={(e) => setMode(e.target.value)} sx={{ minWidth: 260 }} SelectProps={{ SelectDisplayProps: { 'aria-label': 'Import mode' } as object }}>
          <MenuItem value="records">Import rows as records</MenuItem>
          <MenuItem value="template" disabled={!isBook}>Convert to an electronic form</MenuItem>
          <MenuItem value="document" disabled={!isBook}>Keep the workbook as a controlled document</MenuItem>
        </TextField>
        {mode === 'records' && <TextField select size="small" label="Import into" value={target} onChange={(e) => setTarget(e.target.value)} sx={{ minWidth: 200 }}>
          {(targets.data ?? []).map((x) => <MenuItem key={x.key} value={x.key}>{x.name}</MenuItem>)}</TextField>}
        {t?.needsTemplate && <TextField select size="small" label="Form template" value={templateId} onChange={(e) => setTemplateId(e.target.value)} sx={{ minWidth: 280 }}>
          {(templates.data?.items ?? []).map((x) => <MenuItem key={x.id} value={x.id}>{x.number} · {x.title}</MenuItem>)}</TextField>}
        <Button variant="contained" disabled={!file || !site || busy || (t?.needsTemplate && !templateId)} onClick={start}>Upload and inspect</Button>
      </Stack>
      {error && <Alert severity="error" sx={{ mt: 2 }}>{error}</Alert>}
    </Paper>
  );
}

interface Mapping { id: string; sourceLocation: string; sourceName: string; sourceType: string; targetField: string | null; targetType: string | null; transformationRule: string | null; mappingConfidence: number; userConfirmed: boolean; defaultValue: string | null; requiredValue: boolean }
interface Issue { id: string; sourceLocation: string; rowNumber: number | null; severity: number; issueCode: string; description: string; suggestedResolution: string | null; userResolution: string | null }
interface Hist { eventDate: string; event: string; actorName: string; previousValue: string | null; newValue: string | null; message: string | null }
interface Detail { id: string; number: string; originalFileName: string; fileType: string; importMode: string; fileSize: number; fileHash: string; targetModule: string | null; status: number; progress: number; totalRows: number; importedRows: number;
  warningCount: number; errorCount: number; submittedById: string; submittedBy: string; submittedAt: string; approvedAt: string | null; rowVersion: number; background: boolean;
  settings: { excel?: { sheet?: string | null; headerRow?: number | null; sheets?: string[] | null; title?: string | null }; csv?: { encoding: string; delimiter: string; hasHeader: boolean; headerRow: number; dateFormat: string | null; decimalSeparator: string; emptyValue: string }; duplicateKeys?: string[] };
  analysis: { workbook?: WorkbookSummary; conversion?: Conversion; sheet?: string; headerRow?: number; columns?: { location: string; name: string; inferredType: string; blank: number; samples: string[] }[]; sampleRows?: string[][] };
  summary: { dryRun?: { rows: number; valid: number; invalid: number; duplicates: number; existingDuplicates: number; preview: { row: number; values: Record<string, string | null>; problems: string[]; duplicate: boolean }[] }; imported?: number; skippedInvalid?: number; skippedDuplicates?: number; documentId?: string; documentNumber?: string; kind?: string };
  mappings: Mapping[]; issues: Issue[]; history: Hist[]; targetFields: { key: string; label: string; type: string; required: boolean }[] }

const DATE_FORMATS = ['yyyy-MM-dd', 'MM/dd/yyyy', 'M/d/yyyy', 'dd/MM/yyyy', 'd/M/yyyy', 'dd.MM.yyyy', 'dd-MMM-yyyy', 'yyyy/MM/dd'];

export function ImportJobDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const { can, me } = useAuth();
  const [tabSel, setTab] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [edits, setEdits] = useState<Record<string, Partial<Mapping>>>({});
  const [skipInvalid, setSkipInvalid] = useState(false);
  const [reason, setReason] = useState('');
  const job = useQuery({ queryKey: ['import', id], queryFn: () => get<Detail>(`/api/imports/${id}`), refetchInterval: (q) => [3, 4, 9].includes(q.state.data?.status ?? -1) ? 2000 : false });
  const j = job.data;
  const refresh = () => { qc.invalidateQueries({ queryKey: ['import', id] }); qc.invalidateQueries({ queryKey: ['imports'] }); };
  const run = async (f: () => Promise<unknown>) => { setError(null); try { await f(); setEdits({}); refresh(); } catch (e) { setError((e as Error).message); } };
  if (!j) return null;
  const whole = j.importMode === 'template' || j.importMode === 'document';
  const book = ['xlsx', 'xlsm', 'xls'].includes(j.fileType);
  const tab = tabSel ?? (j.analysis.conversion ? 'conversion' : whole ? 'preview' : 'mapping');
  const editable = [5, 6, 7].includes(j.status);
  const m = (x: Mapping) => ({ ...x, ...edits[x.sourceLocation] });
  const csv = j.settings.csv;
  const saveMapping = () => run(() => put(`/api/imports/${id}/mapping`, { targetModule: null, targetTemplateId: null, rowVersion: j.rowVersion,
    mappings: j.mappings.map((x) => { const v = m(x); return { sourceLocation: v.sourceLocation, targetField: v.targetField || null, transformationRule: v.transformationRule || null, defaultValue: v.defaultValue || null, requiredValue: v.requiredValue }; }) }));
  const setCsv = (patch: Record<string, unknown>) => run(() => put(`/api/imports/${id}/settings`, patch));
  const dry = j.summary.dryRun;
  return (
    <ModuleWorkspace module={docsModule()} crumbs={[{ label: 'Import Center', to: '/imports' }, { label: j.number }]}
      title={<span><span style={{ fontFamily: mono }}>{j.number}</span> · {j.originalFileName}</span>}
      actions={<Stack direction="row" gap={1}>
        <Button href={`/api/imports/${id}/original`}>Download original</Button>
        {j.errorCount > 0 && <Button href={`/api/imports/${id}/error-rows`}>Export error rows</Button>}
      </Stack>}>
      <Stack direction="row" gap={2} alignItems="center" flexWrap="wrap" sx={{ mb: 2 }}>
        <Chip color={statusColor(j.status)} label={IMPORT_STATUS[j.status]} />
        <Typography variant="body2">{j.fileType.toUpperCase()} · {(j.fileSize / 1024).toFixed(1)} KB · {j.analysis.conversion ? `${j.analysis.conversion.report.fields} fields, ${j.analysis.conversion.report.tables} tables` : j.importMode === 'document' ? `${j.analysis.workbook?.sheets.length ?? 0} worksheets` : `${j.totalRows} rows`} · uploaded by {j.submittedBy} {fmtDate(j.submittedAt)}</Typography>
        <Typography variant="body2" sx={{ fontFamily: mono }} title="SHA-256 of the preserved original">{j.fileHash.slice(0, 16)}…</Typography>
        {j.background && [3, 4, 9].includes(j.status) && <Typography variant="body2">Large file: processing in the background.</Typography>}
      </Stack>
      {[3, 4, 9].includes(j.status) && <LinearProgress variant="determinate" value={j.progress} sx={{ mb: 2 }} aria-label="Progress" />}
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {j.status === 2 && <Alert severity="error" sx={{ mb: 2 }}>The file did not pass the security inspection and will not be processed.</Alert>}

      <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ mb: 2 }}>
        {j.analysis.conversion && <Tab value="conversion" label="Side-by-side preview" />}
        {j.analysis.conversion && <Tab value="fidelity" label="Fidelity report" />}
        {!whole && <Tab value="mapping" label="Settings and mapping" />}
        <Tab value="preview" label={whole ? 'Check and approve' : 'Preview and validation'} />
        {j.analysis.workbook && <Tab value="workbook" label="Workbook" />}
        <Tab value="issues" label={`Issues (${j.issues.length})`} /><Tab value="history" label="History" />
      </Tabs>

      {book && j.analysis.workbook && (tab === 'mapping' || tab === 'conversion' || (whole && tab === 'preview')) && <Box sx={{ mb: 2 }}>
        <ExcelSettingsPanel key={JSON.stringify(j.settings.excel ?? {})} jobId={j.id} mode={j.importMode} sheets={j.analysis.workbook.sheets} editable={editable && can('imports.edit')}
          current={{ ...(j.settings.excel ?? {}), sheetUsed: j.analysis.sheet, headerUsed: j.analysis.headerRow }} onDone={(e) => { if (e) setError(e); else refresh(); }} />
      </Box>}
      {tab === 'conversion' && j.analysis.conversion && <ConversionPreview jobId={j.id} conversion={j.analysis.conversion} />}
      {tab === 'fidelity' && j.analysis.conversion && <FidelityReportView report={j.analysis.conversion.report} map={j.analysis.conversion.map} />}
      {tab === 'workbook' && j.analysis.workbook && <Stack gap={2}><WorkbookInventory wb={j.analysis.workbook} />{!j.analysis.conversion && <Paper variant="outlined" sx={{ p: 1.5 }}><WorkbookGrid jobId={j.id} sheet={j.analysis.sheet} /></Paper>}</Stack>}
      {tab === 'mapping' && <Stack gap={2}>
        {csv && <Paper variant="outlined" sx={{ p: 2 }}>
          <Typography variant="subtitle1" gutterBottom>File settings</Typography>
          <Stack direction="row" gap={2} flexWrap="wrap">
            <TextField select size="small" label="Encoding" value={csv.encoding} disabled={!editable} onChange={(e) => setCsv({ encoding: e.target.value })} sx={{ width: 140 }}>
              {['utf-8', 'utf-16le', 'utf-16be', 'latin-1'].map((x) => <MenuItem key={x} value={x}>{x.toUpperCase()}</MenuItem>)}</TextField>
            <TextField select size="small" label="Delimiter" value={csv.delimiter === '\t' ? 'tab' : csv.delimiter} disabled={!editable} onChange={(e) => setCsv({ delimiter: e.target.value })} sx={{ width: 140 }}>
              {[[',', 'Comma'], [';', 'Semicolon'], ['tab', 'Tab'], ['|', 'Pipe']].map(([v, l]) => <MenuItem key={v} value={v}>{l}</MenuItem>)}</TextField>
            <FormControlLabel control={<Checkbox checked={csv.hasHeader} disabled={!editable} onChange={(e) => setCsv({ hasHeader: e.target.checked })} />} label="First row is a header" />
            <TextField size="small" type="number" label="Header row" value={csv.headerRow} disabled={!editable} onChange={(e) => setCsv({ headerRow: Number(e.target.value) })} sx={{ width: 110 }} />
            <TextField select size="small" label="Date format" value={csv.dateFormat ?? ''} disabled={!editable} onChange={(e) => setCsv({ dateFormat: e.target.value })} sx={{ width: 160 }}>
              <MenuItem value="">Detect</MenuItem>{DATE_FORMATS.map((f) => <MenuItem key={f} value={f}>{f}</MenuItem>)}</TextField>
            <TextField select size="small" label="Decimal separator" value={csv.decimalSeparator} disabled={!editable} onChange={(e) => setCsv({ decimalSeparator: e.target.value })} sx={{ width: 160 }}>
              <MenuItem value=".">Point (1.5)</MenuItem><MenuItem value=",">Comma (1,5)</MenuItem></TextField>
            <TextField select size="small" label="Empty values" value={csv.emptyValue} disabled={!editable} onChange={(e) => setCsv({ emptyValue: e.target.value })} sx={{ width: 180 }}>
              <MenuItem value="null">Leave empty</MenuItem><MenuItem value="empty">Keep as empty text</MenuItem><MenuItem value="default">Use the default</MenuItem></TextField>
            <TextField select size="small" label="Duplicate check on" SelectProps={{ multiple: true }} value={j.settings.duplicateKeys ?? []} disabled={!editable} onChange={(e) => setCsv({ duplicateKeys: e.target.value as unknown as string[] })} sx={{ minWidth: 200 }}>
              {j.targetFields.map((f) => <MenuItem key={f.key} value={f.key}>{f.label}</MenuItem>)}</TextField>
          </Stack>
        </Paper>}
        <Paper variant="outlined" sx={{ overflowX: 'auto' }}>
          <Table size="small" aria-label="Column mapping">
            <TableHead><TableRow>{['Column', 'Heading', 'Detected type', 'Samples', 'Maps to', 'Confidence', 'Transformations', 'Default', 'Required'].map((h) => <TableCell key={h} sx={{ fontWeight: 600 }}>{h}</TableCell>)}</TableRow></TableHead>
            <TableBody>{j.mappings.map((x) => { const v = m(x); const col = j.analysis.columns?.find((c) => c.location === x.sourceLocation); const set = (patch: Partial<Mapping>) => setEdits({ ...edits, [x.sourceLocation]: { ...edits[x.sourceLocation], ...patch } }); return (
              <TableRow key={x.id}>
                <TableCell sx={{ fontFamily: mono }}>{x.sourceLocation}</TableCell><TableCell>{x.sourceName}</TableCell><TableCell>{x.sourceType}</TableCell>
                <TableCell sx={{ maxWidth: 200, fontSize: 12 }}>{col?.samples.slice(0, 3).join(' · ')}</TableCell>
                <TableCell><TextField select size="small" value={v.targetField ?? ''} disabled={!editable} onChange={(e) => set({ targetField: e.target.value || null })} sx={{ minWidth: 170 }} SelectProps={{ SelectDisplayProps: { 'aria-label': `Map ${x.sourceName}` } as object }}>
                  <MenuItem value="">Do not import</MenuItem>{j.targetFields.map((f) => <MenuItem key={f.key} value={f.key}>{f.label}{f.required ? ' *' : ''}</MenuItem>)}</TextField></TableCell>
                <TableCell>{CONFIDENCE[x.mappingConfidence]}</TableCell>
                <TableCell><TextField size="small" placeholder="trim|upper" value={v.transformationRule ?? ''} disabled={!editable} onChange={(e) => set({ transformationRule: e.target.value })} inputProps={{ 'aria-label': `Transformations for ${x.sourceName}` }} /></TableCell>
                <TableCell><TextField size="small" value={v.defaultValue ?? ''} disabled={!editable} onChange={(e) => set({ defaultValue: e.target.value })} sx={{ width: 120 }} inputProps={{ 'aria-label': `Default for ${x.sourceName}` }} /></TableCell>
                <TableCell><Checkbox checked={v.requiredValue} disabled={!editable} onChange={(e) => set({ requiredValue: e.target.checked })} inputProps={{ 'aria-label': `${x.sourceName} required` }} /></TableCell>
              </TableRow>); })}</TableBody>
          </Table>
        </Paper>
        <Typography variant="body2" color="text.secondary">Transformations run in order, separated by |: trim, upper, lower, collapse, date:dd/MM/yyyy, map:{'{"Y":"pass"}'}, prefix:TEXT, suffix:TEXT.</Typography>
        {editable && can('imports.edit') && <Stack direction="row" gap={1}>
          <Button variant="outlined" onClick={saveMapping}>Save mapping</Button>
          <Button variant="contained" onClick={() => run(async () => { if (Object.keys(edits).length) await put(`/api/imports/${id}/mapping`, { targetModule: null, targetTemplateId: null, rowVersion: j.rowVersion, mappings: j.mappings.map((x) => { const v = m(x); return { sourceLocation: v.sourceLocation, targetField: v.targetField || null, transformationRule: v.transformationRule || null, defaultValue: v.defaultValue || null, requiredValue: v.requiredValue }; }) }); await post(`/api/imports/${id}/validate`); setTab('preview'); })}>Validate (dry run)</Button>
        </Stack>}
      </Stack>}

      {tab === 'preview' && <Stack gap={2}>
        {whole && j.status !== 10 && j.status !== 12 && <Alert severity="info">{j.importMode === 'template'
          ? 'After approval the converted form becomes a new draft form template, with the original workbook kept on its revision. Review the side-by-side preview and the fidelity report first.'
          : 'After approval the workbook becomes a new controlled document (draft revision) with the file exactly as uploaded.'}</Alert>}
        {j.summary.documentId && <Alert severity="success">{j.importMode === 'template' ? 'Draft form template' : 'Controlled document'} <RouterLink to={`/${j.importMode === 'template' ? 'forms/templates' : 'documents'}/${j.summary.documentId}`}>{j.summary.documentNumber}</RouterLink> created.</Alert>}
        {whole && editable && can('imports.edit') && !dry && <Box><Button variant="outlined" onClick={() => run(() => post(`/api/imports/${id}/validate`))}>Check</Button></Box>}
        {whole ? null : dry ? <Alert severity={dry.invalid ? 'warning' : 'success'}>Dry run: {dry.valid} of {dry.rows} rows ready to import · {dry.invalid} with problems · {dry.duplicates} repeated in the file · {dry.existingDuplicates} already exist.</Alert>
          : <Alert severity="info">Validate the mapping to see a preview. Nothing is imported until someone approves it.</Alert>}
        {j.summary.imported !== undefined && !whole && <Alert severity="success">{j.summary.imported} records created{j.summary.skippedInvalid ? `, ${j.summary.skippedInvalid} rows skipped` : ''}{j.summary.skippedDuplicates ? `, ${j.summary.skippedDuplicates} duplicates skipped` : ''}.</Alert>}
        {dry && !whole && <Paper variant="outlined" sx={{ overflowX: 'auto' }}>
          <Table size="small" aria-label="Preview">
            <TableHead><TableRow><TableCell>Row</TableCell>{j.targetFields.filter((f) => j.mappings.some((x) => x.targetField === f.key)).map((f) => <TableCell key={f.key}>{f.label}</TableCell>)}<TableCell>Result</TableCell></TableRow></TableHead>
            <TableBody>{dry.preview.map((r) => <TableRow key={r.row} sx={{ bgcolor: r.problems.length ? 'error.50' : undefined }}>
              <TableCell>{r.row}</TableCell>{j.targetFields.filter((f) => j.mappings.some((x) => x.targetField === f.key)).map((f) => <TableCell key={f.key}>{r.values[f.key] ?? ''}</TableCell>)}
              <TableCell>{r.problems.length ? r.problems.join(' ') : r.duplicate ? 'Duplicate (skipped)' : 'Ready'}</TableCell></TableRow>)}</TableBody>
          </Table>
        </Paper>}
        {[6, 7].includes(j.status) && can('imports.edit') && <Stack direction="row" gap={2} alignItems="center">
          {j.status === 7 && <FormControlLabel control={<Checkbox checked={skipInvalid} onChange={(e) => setSkipInvalid(e.target.checked)} />} label="Import only the rows without problems" />}
          <Button variant="contained" disabled={j.status === 7 && !skipInvalid} onClick={() => run(() => post(`/api/imports/${id}/submit`, { skipInvalidRows: skipInvalid }))}>Submit for approval</Button>
        </Stack>}
        {j.status === 8 && <Stack direction="row" gap={1} alignItems="center">
          {can('imports.approve') && me?.id !== j.submittedById ? <>
            <Button variant="contained" onClick={() => run(() => post(`/api/imports/${id}/approve`))}>Approve and import</Button>
            <TextField size="small" label="Reason to return" value={reason} onChange={(e) => setReason(e.target.value)} />
            <Button disabled={!reason.trim()} onClick={() => run(() => post(`/api/imports/${id}/return`, { reason }))}>Return</Button>
          </> : <Typography>Waiting for approval by someone other than the uploader.</Typography>}
        </Stack>}
        {[10, 11, 12].includes(j.status) && can('imports.approve') && <Stack direction="row" gap={1} alignItems="center">
          <TextField size="small" label="Reason to roll back" value={reason} onChange={(e) => setReason(e.target.value)} />
          <Button color="error" disabled={!reason.trim()} onClick={() => run(() => post(`/api/imports/${id}/rollback`, { reason }))}>Roll back</Button>
        </Stack>}
        <Stack direction="row" gap={1}>
          {[0, 1, 3, 4, 5, 6, 7, 8].includes(j.status) && can('imports.edit') && <Button onClick={() => run(() => post(`/api/imports/${id}/cancel`))}>Cancel import</Button>}
          {[2, 10, 11, 12, 13, 14, 15].includes(j.status) && can('imports.edit') && <Button onClick={() => run(() => post(`/api/imports/${id}/archive`))}>Archive</Button>}
        </Stack>
      </Stack>}

      {tab === 'issues' && <Paper variant="outlined" sx={{ overflowX: 'auto' }}><Table size="small" aria-label="Issues">
        <TableHead><TableRow>{['Severity', 'Where', 'Code', 'Problem', 'Suggested resolution', 'Resolution'].map((h) => <TableCell key={h} sx={{ fontWeight: 600 }}>{h}</TableCell>)}</TableRow></TableHead>
        <TableBody>{j.issues.map((i) => <TableRow key={i.id}><TableCell>{SEVERITY[i.severity]}</TableCell><TableCell>{i.sourceLocation}</TableCell><TableCell sx={{ fontFamily: mono }}>{i.issueCode}</TableCell>
          <TableCell>{i.description}</TableCell><TableCell>{i.suggestedResolution}</TableCell>
          <TableCell>{i.userResolution ?? (can('imports.edit') && <Button size="small" onClick={() => { const r = window.prompt('How was this resolved?'); if (r) run(() => put(`/api/imports/${id}/issues/${i.id}`, { resolution: r })); }}>Resolve</Button>)}</TableCell></TableRow>)}
          {!j.issues.length && <TableRow><TableCell colSpan={6}>No issues.</TableCell></TableRow>}</TableBody></Table></Paper>}

      {tab === 'history' && <Paper variant="outlined"><Table size="small" aria-label="History"><TableBody>{j.history.map((h, k) => <TableRow key={k}>
        <TableCell>{new Date(h.eventDate).toLocaleString()}</TableCell><TableCell>{h.actorName}</TableCell>
        <TableCell>{h.event === 'StatusChanged' ? `${IMPORT_STATUS[statusIndex(h.previousValue)] ?? h.previousValue} → ${IMPORT_STATUS[statusIndex(h.newValue)] ?? h.newValue}` : h.event}</TableCell><TableCell>{h.message}</TableCell></TableRow>)}</TableBody></Table></Paper>}
      <Box sx={{ mt: 2 }}><Button component={RouterLink} to="/imports">Back to Import Center</Button></Box>
    </ModuleWorkspace>
  );
}

const STATUS_KEYS = ['Uploaded', 'SecurityScanPending', 'SecurityScanFailed', 'Inspecting', 'Parsing', 'MappingRequired', 'PreviewReady', 'ValidationFailed', 'AwaitingApproval', 'Importing', 'Imported', 'PartiallyImported', 'CompletedWithWarnings', 'Failed', 'Canceled', 'RolledBack', 'Archived'];
const statusIndex = (k: string | null) => (k ? STATUS_KEYS.indexOf(k) : -1);
