import { useMemo, useState, type ReactNode } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, Box, Button, Checkbox, Chip, FormControlLabel, MenuItem, Paper, Stack, Switch, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { get, put } from '../api';
import { mono } from '../theme';
import { ConfidenceChip } from './ImportPdf';
import { useAuth } from '../auth';

/* ------------------------------------------------------------------ workbench */

interface Step { number: number; name: string; status: 'done' | 'pending' | 'attention'; content: Record<string, unknown> }
interface Target { key: string; name: string; needsTemplate: boolean; masterList: boolean }
type Any = Record<string, any>;

const STEP_COLOR: Record<string, 'success' | 'warning' | 'default'> = { done: 'success', attention: 'warning', pending: 'default' };
const STEP_LABEL: Record<string, string> = { done: 'Done', attention: 'Needs attention', pending: 'Not yet' };

const Facts = ({ rows }: { rows: [string, ReactNode][] }) => (
  <Box component="dl" sx={{ display: 'grid', gridTemplateColumns: 'max-content 1fr', columnGap: 2, rowGap: 0.5, m: 0, fontSize: 14 }}>
    {rows.filter(([, v]) => v !== null && v !== undefined && v !== '').map(([k, v]) => <Box key={k} sx={{ display: 'contents' }}><Box component="dt" sx={{ color: 'text.secondary' }}>{k}</Box><Box component="dd" sx={{ m: 0 }}>{v}</Box></Box>)}
  </Box>
);

const SmallTable = ({ label, head, rows }: { label: string; head: string[]; rows: ReactNode[][] }) => rows.length === 0 ? null : (
  <Paper variant="outlined" sx={{ overflowX: 'auto', mt: 1 }}>
    <Table size="small" aria-label={label}>
      <TableHead><TableRow>{head.map((h) => <TableCell key={h} sx={{ fontWeight: 600 }}>{h}</TableCell>)}</TableRow></TableHead>
      <TableBody>{rows.slice(0, 50).map((r, i) => <TableRow key={i}>{r.map((c, k) => <TableCell key={k} sx={{ fontSize: 13 }}>{c}</TableCell>)}</TableRow>)}</TableBody>
    </Table>
  </Paper>
);

/** The eleven steps of the mapping workbench, each worked out from the import: what is imported, where it goes, what changes, what is unsupported, and what happened. */
export function WorkbenchPanel({ jobId, editable, onDone }: { jobId: string; editable: boolean; onDone: (e?: string) => void }) {
  const qc = useQueryClient();
  const wb = useQuery({ queryKey: ['import-workbench', jobId], queryFn: () => get<Step[]>(`/api/imports/${jobId}/workbench`) });
  if (!wb.data) return <Typography color="text.secondary">Loading the workbench…</Typography>;
  const s = (n: number) => wb.data!.find((x) => x.number === n)!.content as Any;
  const done = (e?: string) => { qc.invalidateQueries({ queryKey: ['import-workbench', jobId] }); onDone(e); };
  return (
    <Stack gap={1.5} aria-label="Import workbench">
      {wb.data.map((st) => (
        <Paper key={st.number} variant="outlined" sx={{ p: 1.5, borderLeft: 4, borderLeftColor: st.status === 'done' ? 'success.main' : st.status === 'attention' ? 'warning.main' : 'divider' }} aria-label={`Step ${st.number} ${st.name}`}>
          <Stack direction="row" gap={1.5} alignItems="center" sx={{ mb: 1 }}>
            <Box sx={{ width: 26, height: 26, borderRadius: '50%', bgcolor: st.status === 'done' ? 'success.main' : st.status === 'attention' ? 'warning.main' : 'grey.400', color: '#fff', display: 'grid', placeItems: 'center', fontSize: 13, fontWeight: 600 }}>{st.number}</Box>
            <Typography variant="subtitle1" sx={{ fontWeight: 600 }}>{st.name}</Typography>
            <Chip size="small" variant="outlined" color={STEP_COLOR[st.status]} label={STEP_LABEL[st.status]} />
          </Stack>
          <StepBody n={st.number} c={st.content as Any} all={s} jobId={jobId} editable={editable} onDone={done} />
        </Paper>))}
    </Stack>
  );
}

function StepBody({ n, c, all, jobId, editable, onDone }: { n: number; c: Any; all: (n: number) => Any; jobId: string; editable: boolean; onDone: (e?: string) => void }) {
  switch (n) {
    case 1: return <Facts rows={[['File', `${c.fileName} (${c.fileType}, ${(c.size / 1024).toFixed(1)} KB)`], ['SHA-256', <span style={{ fontFamily: mono, fontSize: 12 }}>{c.hash}</span>],
      ['Security inspection', c.security === 'passed' ? 'Passed' : c.security === 'rejected' ? 'Rejected' : 'Pending'], ['Malware scan', c.malwareScan], ['Security warnings', c.securityWarnings],
      ['Worksheets', c.sheets?.join(', ')], ['Pages', c.pages], ['Detected', (c.detected as string[]).join(' · ')], ['Original', c.preserved]]} />;
    case 2: return <PurposeEditor jobId={jobId} purpose={c} target={all(3)} editable={editable} onDone={onDone} />;
    case 3: return <Facts rows={[['Module', c.module], ['Record type', c.recordType], ['Template', c.template], ['Revision', c.revision], ['Existing records', c.existingRecords === 'update' ? 'Updated when they match' : c.existingRecords === 'skip' ? 'Skipped when they match' : null], ['Attached to', c.attachTo]]} />;
    case 4: return <Box>
      <Facts rows={[['Pages', c.pages], ['Worksheets', c.worksheets?.join(', ')], ['Worksheet used', c.sheetUsed], ['Header row', c.headerRow], ['Header', c.headers?.join(' · ')], ['Footer', c.footer?.join(' · ')]]} />
      <SmallTable label="Sections" head={['Section', 'Repeating', 'Conditional']} rows={(c.sections ?? []).map((x: Any) => [x.title, x.repeatable ? 'Yes' : 'No', x.conditional ? 'Yes' : 'No'])} />
      <SmallTable label="Tables" head={['Table', 'Columns', 'Rows']} rows={(c.tables ?? []).map((x: Any) => [x.title ?? x.key, x.columns, x.repeating ? 'Repeating' : 'Fixed'])} />
      <SmallTable label="Repeating regions" head={['Table', 'Where', 'First row', 'Rows']} rows={(c.repeatingRegions ?? []).map((x: Any) => [x.table, x.sheet, x.firstRow, x.rows])} />
    </Box>;
    case 5: return <Box>
      {(c.unmappedRequired ?? []).length > 0 && <Alert severity="warning" sx={{ mb: 1 }}>Required fields with no column: {c.unmappedRequired.join(', ')}</Alert>}
      <SmallTable label="Field mapping" head={c.conversion ? ['Source', 'Field', 'Type', 'Validation'] : ['Source', 'Field', 'Type', 'Transformation', 'Default', 'Validation', 'Confidence']}
        rows={(c.fields ?? []).map((f: Any) => c.conversion ? [f.source, f.label, f.dataType, f.validation] : [f.source, f.target ?? 'Not imported', f.dataType, f.transformation, f.default, f.validation, f.confidence])} />
    </Box>;
    case 6: return (c.references ?? []).length === 0 ? <Typography variant="body2" color="text.secondary">No field refers to other records.</Typography>
      : <SmallTable label="Relationships" head={['Field', 'Refers to', 'Matched by', 'Values', 'Matched', 'Missing']}
        rows={c.references.map((r: Any) => [r.field, r.type, r.matching, r.distinct ?? '—', r.matched ?? '—', r.missingCount ? `${r.missingCount}: ${(r.missing ?? []).join(', ')}` : r.missingCount === 0 ? 'None' : '—'])} />;
    case 7: return <Box>
      <Stack direction="row" gap={1} flexWrap="wrap">
        {[['Errors', c.errors], ['Warnings', c.warnings], ['Duplicates', c.duplicates], ['Unsupported content', c.unsupported], ['Missing references', c.missingReferences], ['Formulas not converted', c.invalidFormulas], ['Mappings without permission', c.unauthorizedMappings], ['To confirm', c.toConfirm]]
          .map(([k, v]) => <Chip key={k as string} size="small" variant="outlined" color={Number(v) > 0 ? 'warning' : 'default'} label={`${k}: ${v}`} />)}
      </Stack>
      <SmallTable label="Issues by code" head={['Code', 'Count', 'Severity']} rows={(c.byCode ?? []).map((x: Any) => [<span style={{ fontFamily: mono }}>{x.code}</span>, x.count, x.severity])} />
    </Box>;
    case 8: return <Box>
      <Facts rows={[['Before', c.before], ['After', c.after], ['To create', c.create], ['To update', c.update], ['To skip', c.skip], ['With errors', c.error]]} />
      <SmallTable label="Changes to existing records" head={['Row', 'Field', 'Now', 'After']}
        rows={(c.updates ?? []).flatMap((u: Any) => Object.entries(u.after ?? {}).filter(([k, v]) => (u.before ?? {})[k] !== v).map(([k, v]) => [u.row, k, (u.before ?? {})[k] ?? '', v as string]))} />
    </Box>;
    case 9: return <Facts rows={[['Approval', c.required ? 'Required' : 'Not required'], ['Rule', c.rule], ['Approver needs', (c.permissions ?? []).join(', ')], ['Fidelity report', c.fidelityAcceptance ? 'The approver must accept it' : null],
      ['Approved by', c.approvedBy], ['Approved', c.approvedAt ? new Date(c.approvedAt).toLocaleString() : null], ['Waiting', c.waiting ? 'Awaiting approval' : null]]} />;
    case 10: return <Facts rows={[['Execution', c.transactional ? 'In one transaction: a failing row keeps nothing' : null], ['Processing', c.background ? 'In the background (large file)' : 'Immediately after approval'], ['Progress', `${c.progress}%`], ['Status', c.status]]} />;
    case 11: return <Box>
      <Facts rows={[['Created', c.imported], ['Updated', c.updated], ['Skipped', c.skipped], ['Failed', c.failed], ['Warnings', c.warnings], ['Rollback', c.rollback], ['Rolled back', c.rolledBack], ['Audit entries', c.auditEntries], ['Document', c.documentNumber]]} />
      {c.resultPackage && <Button sx={{ mt: 1 }} variant="outlined" href={`/api/imports/${jobId}/result-package`}>Download result package</Button>}
    </Box>;
    default: return null;
  }
}

function PurposeEditor({ jobId, purpose, target, editable, onDone }: { jobId: string; purpose: Any; target: Any; editable: boolean; onDone: (e?: string) => void }) {
  const targets = useQuery({ queryKey: ['import-targets'], queryFn: () => get<Target[]>('/api/imports/targets') });
  const { can } = useAuth();
  const templates = useQuery({ queryKey: ['import-templates'], queryFn: () => get<{ items: { id: string; number: string; title: string }[] }>('/api/template-library?view=effective&pageSize=200') });
  const [p, setP] = useState<string>(purpose.purpose);
  const [module, setModule] = useState<string>(target.moduleKey ?? 'form-records');
  const [templateId, setTemplateId] = useState<string>(target.templateId ?? '');
  const [existing, setExisting] = useState<string>(target.existingRecords ?? 'skip');
  const [revision, setRevision] = useState<string>(target.templateRevisionMode ?? 'new-template');
  const [number, setNumber] = useState<string>(target.attachTo ?? '');
  const [busy, setBusy] = useState(false);
  const records = ['historical-records', 'transactional-records', 'master-list'].includes(p);
  const t = targets.data?.find((x) => x.key === module);
  const choices = (targets.data ?? []).filter((x) => !!x.masterList === (p === 'master-list'));
  const pick = (v: string) => { setP(v); const want = v === 'master-list'; if (t && !!t.masterList !== want) setModule((targets.data ?? []).find((x) => !!x.masterList === want)?.key ?? ''); };
  const apply = async () => {
    setBusy(true);
    try {
      await put(`/api/imports/${jobId}/purpose`, {
        purpose: p, targetModule: records ? module : null, targetTemplateId: (records && t?.needsTemplate) || (p === 'controlled-form-template' && revision === 'new-revision') ? templateId || null : null,
        templateRevision: p === 'controlled-form-template' ? revision : null, existingRecords: records ? existing : null, recordNumber: p === 'attachment-package' ? number : null,
      });
      onDone();
    } catch (e) { onDone((e as Error).message); } finally { setBusy(false); }
  };
  return (
    <Stack gap={1.5}>
      <Typography variant="body2">Imported as: <strong>{purpose.label}</strong></Typography>
      {editable && <Stack direction={{ xs: 'column', md: 'row' }} gap={1.5} flexWrap="wrap" alignItems="flex-start">
        <TextField select size="small" label="Import as" value={p} onChange={(e) => pick(e.target.value)} sx={{ minWidth: 230 }} SelectProps={{ SelectDisplayProps: { 'aria-label': 'Import purpose' } as object }}>
          {(purpose.options as Any[]).map((o) => <MenuItem key={o.key} value={o.key} disabled={!o.available}>{o.label}</MenuItem>)}</TextField>
        {records && <TextField select size="small" label="Into" value={choices.some((x) => x.key === module) ? module : ''} onChange={(e) => setModule(e.target.value)} sx={{ minWidth: 240 }}>
          {choices.map((x) => <MenuItem key={x.key} value={x.key}>{x.name}</MenuItem>)}</TextField>}
        {((records && t?.needsTemplate) || (p === 'controlled-form-template' && revision === 'new-revision')) && <TextField select size="small" label="Form template" value={templateId} onChange={(e) => setTemplateId(e.target.value)} sx={{ minWidth: 260 }} SelectProps={{ SelectDisplayProps: { 'aria-label': 'Target template' } as object }}>
          {(templates.data?.items ?? []).map((x) => <MenuItem key={x.id} value={x.id}>{x.number} · {x.title}</MenuItem>)}</TextField>}
        {records && <TextField select size="small" label="Existing records" value={existing} onChange={(e) => setExisting(e.target.value)} sx={{ minWidth: 220 }} SelectProps={{ SelectDisplayProps: { 'aria-label': 'Existing records' } as object }}>
          <MenuItem value="skip">Skip records that already exist</MenuItem>{(can('imports.update_records') || existing === 'update') && <MenuItem value="update">Update records that already exist</MenuItem>}</TextField>}
        {p === 'controlled-form-template' && <TextField select size="small" label="Becomes" value={revision} onChange={(e) => setRevision(e.target.value)} sx={{ minWidth: 240 }}>
          <MenuItem value="new-template">A new form template</MenuItem><MenuItem value="new-revision">A new revision of an existing template</MenuItem></TextField>}
        {p === 'attachment-package' && <TextField size="small" label="Record number" value={number} onChange={(e) => setNumber(e.target.value)} helperText="The file is attached to this record" />}
        <Button variant="outlined" disabled={busy} onClick={apply}>Apply</Button>
      </Stack>}
      {editable && p === 'historical-records' && <Typography variant="body2" color="text.secondary" data-testid="historical-hint">Historical records arrive marked Imported (read-only): they can be printed, exported and deleted, but not edited, signed or sent for approval. Records that already exist and are updated stay editable.</Typography>}
      {editable && p === 'reference-content' && <Typography variant="body2" color="text.secondary">Reference content becomes a reference copy: stored and controlled with a history, without approval or a revision workflow.</Typography>}
    </Stack>
  );
}

/* ------------------------------------------------------------------ review for Excel and Word conversions */

export interface ReviewItem {
  id: string; kind: string; label: string; type: string; suggested: string | null; suggestedCandidate: string | null; candidate: string; location: string; page: number; confidence: number; level: string;
  source: string; reason: string; options: string[] | null; columns: { label: string; type: string; total: boolean }[] | null; role: string | null; formula: string | null; include: boolean; confirmed: boolean;
  canExclude: boolean; typed: boolean; mustConfirm: boolean;
}
type Edit = { include?: boolean; label?: string; type?: string; options?: string; confirmed?: boolean };
const KIND: Record<string, string> = { field: 'Field', checkbox: 'Checkbox', column: 'Table column', table: 'Table', formula: 'Formula', signature: 'Signature block', approval: 'Approval block', section: 'Section', instruction: 'Instructions', conditional: 'Conditional area' };
const TYPES = [['text', 'Text'], ['longtext', 'Long text'], ['integer', 'Whole number'], ['number', 'Number'], ['currency', 'Currency'], ['percent', 'Percentage'], ['date', 'Date'], ['datetime', 'Date and time'], ['time', 'Time'],
  ['boolean', 'Checkbox'], ['radio', 'Options'], ['choice', 'Dropdown'], ['multichoice', 'Multiple choice'], ['user', 'User'], ['department', 'Department'], ['site', 'Site'], ['product', 'Product'], ['part', 'Part'],
  ['supplier', 'Supplier'], ['customer', 'Customer'], ['equipment', 'Equipment'], ['record', 'Related record'], ['attachment', 'Attachment'], ['image', 'Photo'], ['signature', 'Signature'], ['passfail', 'Pass / fail']];
const typeLabel = (t: string) => TYPES.find(([v]) => v === t)?.[1] ?? t;

/** Everything detected in an Excel or Word conversion: keep or leave out, correct, use a suggestion, confirm. Low-confidence and unconverted items must be confirmed before submitting. */
export function ConversionReview({ jobId, items, required, editable, onDone }: { jobId: string; items: ReviewItem[]; required: number; editable: boolean; onDone: (e?: string) => void }) {
  const [edits, setEdits] = useState<Record<string, Edit>>({});
  const [onlyOpen, setOnlyOpen] = useState(required > 0);
  const rows = useMemo(() => items.filter((d) => !onlyOpen || (d.include && d.mustConfirm && !d.confirmed)), [items, onlyOpen]);
  const set = (id: string, patch: Edit) => setEdits((e) => ({ ...e, [id]: { ...e[id], ...patch } }));
  const save = async (confirmAll: boolean) => {
    try {
      const list = Object.entries(edits).map(([id, e]) => ({ id, include: e.include ?? null, label: e.label ?? null, type: e.type ?? null,
        options: e.options !== undefined ? e.options.split(',').map((o) => o.trim()).filter(Boolean) : null, confirmed: e.confirmed ?? null }));
      await put(`/api/imports/${jobId}/detections`, { items: list, confirmAll });
      setEdits({}); onDone();
    } catch (e) { onDone((e as Error).message); }
  };
  const suggestions = items.filter((i) => i.suggested && i.include).length;
  return (
    <Stack gap={1.5}>
      <Alert severity={required > 0 ? 'warning' : 'success'}>{required > 0
        ? `${required} item(s) need confirmation: everything with low confidence and everything that was not converted. Correct what is wrong, leave out what is not part of the form, then confirm.`
        : 'Every item has been reviewed or converted with enough confidence.'}{suggestions > 0 ? ` ${suggestions} type suggestion(s) are shown; none is applied unless you use it.` : ''}</Alert>
      <Stack direction="row" gap={2} alignItems="center" flexWrap="wrap">
        <FormControlLabel control={<Switch checked={onlyOpen} onChange={(e) => setOnlyOpen(e.target.checked)} />} label="Only those needing confirmation" />
        {editable && <><Button variant="outlined" disabled={!Object.keys(edits).length} onClick={() => save(false)}>Save review</Button>
          <Button variant="contained" onClick={() => save(true)}>Confirm all</Button></>}
      </Stack>
      <Paper variant="outlined" sx={{ overflowX: 'auto' }}>
        <Table size="small" aria-label="Conversion review">
          <TableHead><TableRow>{['Keep', 'Kind', 'Label', 'Type', 'Suggestion', 'Where', 'Confidence', 'Why', 'Confirmed'].map((h) => <TableCell key={h} sx={{ fontWeight: 600 }}>{h}</TableCell>)}</TableRow></TableHead>
          <TableBody>{rows.map((d) => {
            const e = edits[d.id] ?? {};
            const include = e.include ?? d.include; const confirmed = e.confirmed ?? d.confirmed; const type = e.type ?? d.type;
            const open = include && d.mustConfirm && !confirmed;
            return (
              <TableRow key={d.id} sx={{ bgcolor: open ? 'rgba(237,108,2,0.06)' : undefined, opacity: include ? 1 : 0.55 }}>
                <TableCell><Checkbox size="small" checked={include} disabled={!editable || !d.canExclude} onChange={(x) => set(d.id, { include: x.target.checked })} inputProps={{ 'aria-label': `Keep ${d.label}` }} /></TableCell>
                <TableCell>{KIND[d.kind] ?? d.kind}</TableCell>
                <TableCell><TextField size="small" value={e.label ?? d.label} disabled={!editable || !d.typed} onChange={(x) => set(d.id, { label: x.target.value })} inputProps={{ 'aria-label': `Label for ${d.label}` }} sx={{ minWidth: 180 }} />
                  {d.formula && <Typography variant="caption" display="block" sx={{ fontFamily: mono }}>{d.formula}</Typography>}
                  {d.kind === 'table' && <Typography variant="caption" display="block">{(d.columns ?? []).map((c) => `${c.label}${c.total ? ' (total)' : ''}`).join(' · ')}</Typography>}</TableCell>
                <TableCell>{d.typed ? <TextField select size="small" value={type} disabled={!editable} onChange={(x) => set(d.id, { type: x.target.value })} sx={{ minWidth: 140 }} SelectProps={{ SelectDisplayProps: { 'aria-label': `Type for ${d.label}` } as object }}>
                  {(TYPES.some(([v]) => v === type) ? TYPES : [...TYPES, [type, type]]).map(([v, l]) => <MenuItem key={v} value={v}>{l}</MenuItem>)}</TextField> : d.candidate}
                  {d.typed && ['choice', 'radio', 'multichoice'].includes(type) && <TextField size="small" sx={{ mt: 0.5, minWidth: 160 }} placeholder="Options, comma separated" value={e.options ?? (d.options ?? []).join(', ')} disabled={!editable} onChange={(x) => set(d.id, { options: x.target.value })} inputProps={{ 'aria-label': `Options for ${d.label}` }} />}</TableCell>
                <TableCell>{d.suggested && d.suggested !== type ? <Stack direction="row" gap={0.5} alignItems="center"><span>{d.suggestedCandidate ?? typeLabel(d.suggested)}</span>
                  {editable && <Button size="small" onClick={() => set(d.id, { type: d.suggested! })} aria-label={`Use suggestion for ${d.label}`}>Use</Button>}</Stack> : '—'}</TableCell>
                <TableCell sx={{ fontFamily: mono, fontSize: 12 }}>{d.location}</TableCell>
                <TableCell><ConfidenceChip level={d.level} score={d.confidence} /></TableCell>
                <TableCell sx={{ fontSize: 12, maxWidth: 260 }}>{d.reason}</TableCell>
                <TableCell><Checkbox size="small" checked={confirmed} disabled={!editable} onChange={(x) => set(d.id, { confirmed: x.target.checked })} inputProps={{ 'aria-label': `Confirm ${d.label}` }} /></TableCell>
              </TableRow>);
          })}
          {!rows.length && <TableRow><TableCell colSpan={9}><Typography color="text.secondary">Nothing to show.</Typography></TableCell></TableRow>}</TableBody>
        </Table>
      </Paper>
    </Stack>
  );
}

/* ------------------------------------------------------------------ form family and pipeline */

interface Family { key: string; name: string; moduleKey: string; elements: string[] }
export interface ProfileResult { key: string; name: string; module: string; found: number; missing: number; elements: { element: string; fieldKey: string | null; fieldLabel: string | null; type: string; signature: boolean }[] }

/** Which kind of form the conversion is (NCR, CAPA, calibration, supplier, audit, APQP, PPAP, or general): the template is filed with that module and checked for the usual elements. */
export function ConversionFamily({ jobId, current, profile, editable, onDone }: { jobId: string; current: string | null; profile: ProfileResult | undefined; editable: boolean; onDone: (e?: string) => void }) {
  const families = useQuery({ queryKey: ['conversion-targets'], queryFn: () => get<Family[]>('/api/imports/conversion-targets') });
  const change = async (v: string) => { try { await put(`/api/imports/${jobId}/conversion-target`, { target: v || null }); onDone(); } catch (e) { onDone((e as Error).message); } };
  return (
    <Paper variant="outlined" sx={{ p: 1.5 }}>
      <Stack direction="row" gap={2} alignItems="center" flexWrap="wrap">
        <TextField select size="small" label="Form family" value={current ?? ''} disabled={!editable} onChange={(e) => change(e.target.value)} sx={{ minWidth: 220 }} SelectProps={{ SelectDisplayProps: { 'aria-label': 'Form family' } as object }}>
          <MenuItem value="">General form</MenuItem>{(families.data ?? []).map((f) => <MenuItem key={f.key} value={f.key}>{f.name}</MenuItem>)}</TextField>
        {profile && <Typography variant="body2">{profile.found} of {profile.found + profile.missing} usual elements found; the template is filed with {profile.module}.</Typography>}
      </Stack>
      {profile && <Stack direction="row" gap={0.5} flexWrap="wrap" sx={{ mt: 1 }} aria-label="Family elements">
        {profile.elements.map((e) => <Chip key={e.element} size="small" variant="outlined" color={e.fieldKey ? 'success' : 'warning'} label={`${e.element}${e.fieldKey ? ` → ${e.fieldLabel}` : ': missing'}`} />)}
      </Stack>}
    </Paper>
  );
}

interface PipelineStep { number: number; name: string; status: string; detail: string }
const PIPE_COLOR: Record<string, 'success' | 'warning' | 'error' | 'default' | 'info'> = { done: 'success', attention: 'warning', failed: 'error', pending: 'default', 'not-applicable': 'default', current: 'info' };
const PIPE_LABEL: Record<string, string> = { done: 'Done', attention: 'Needs attention', failed: 'Failed', pending: 'Not yet', 'not-applicable': 'Not applicable', current: 'In progress' };

/** The conversion pipeline from inspecting the file to the approved draft template, with what each step found. */
export function PipelinePanel({ jobId }: { jobId: string }) {
  const p = useQuery({ queryKey: ['import-pipeline', jobId], queryFn: () => get<PipelineStep[]>(`/api/imports/${jobId}/pipeline`) });
  return (
    <Paper variant="outlined" sx={{ overflowX: 'auto' }}>
      <Table size="small" aria-label="Conversion pipeline">
        <TableHead><TableRow>{['#', 'Step', 'Status', 'Detail'].map((h) => <TableCell key={h} sx={{ fontWeight: 600 }}>{h}</TableCell>)}</TableRow></TableHead>
        <TableBody>{(p.data ?? []).map((s) => <TableRow key={s.number}><TableCell>{s.number}</TableCell><TableCell>{s.name}</TableCell>
          <TableCell><Chip size="small" variant="outlined" color={PIPE_COLOR[s.status] ?? 'default'} label={PIPE_LABEL[s.status] ?? s.status} /></TableCell><TableCell sx={{ fontSize: 13 }}>{s.detail}</TableCell></TableRow>)}</TableBody>
      </Table>
    </Paper>
  );
}
