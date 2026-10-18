import { useMemo, useState } from 'react';
import { Alert, Box, Button, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, Divider, FormControlLabel, IconButton, List, ListItemButton, ListItemText, MenuItem, Paper, Stack, TextField, Tooltip, Typography } from '@mui/material';
import ArrowUpward from '@mui/icons-material/ArrowUpward';
import ArrowDownward from '@mui/icons-material/ArrowDownward';
import DeleteOutline from '@mui/icons-material/DeleteOutline';
import { post, postBlob } from '../api';
import { FormView } from './FormView';
import type { BandText, ColumnDef, Condition, FormDef, FormTheme, PageSetup, SectionDef, SectionStyle, SignatureDef, TableDef, TableTotal } from './definition';
import { formThemes, roleLabel } from './definition';

/** Every field type the server accepts, with the name shown in the designer. */
export const allFieldTypes: [string, string][] = [
  ['text', 'Short text'], ['longtext', 'Long text'], ['number', 'Number'], ['integer', 'Whole number'], ['currency', 'Currency'], ['percent', 'Percent'],
  ['measurement', 'Measurement (nominal and limits)'], ['calc', 'Calculated'], ['date', 'Date'], ['time', 'Time'], ['datetime', 'Date and time'],
  ['boolean', 'Checkbox (yes / no)'], ['choice', 'Dropdown list'], ['radio', 'Option buttons'], ['multichoice', 'Multiple choice'], ['passfail', 'Pass / fail'],
  ['user', 'Person'], ['site', 'Site'], ['department', 'Department'], ['part', 'Part'], ['product', 'Product'], ['supplier', 'Supplier'], ['customer', 'Customer'],
  ['equipment', 'Equipment'], ['record', 'Linked record'], ['attachment', 'File attachment'], ['photo', 'Photo'], ['instruction', 'Instruction text'], ['email', 'Email'], ['url', 'Web address'],
];
const optionTypes = ['choice', 'radio', 'multichoice'];
const numericTypes = ['number', 'integer', 'currency', 'percent', 'measurement', 'calc'];
const operators = ['equals', 'notEquals', 'greaterThan', 'greaterOrEqual', 'lessThan', 'lessOrEqual', 'contains', 'in', 'isEmpty', 'isNotEmpty'];
const totalFunctions = ['sum', 'average', 'min', 'max', 'count', 'stdev', 'range'];
/** Starting wording for the meaning of each signature role; the designer can change it. */
export const roleMeaning: Record<string, string> = {
  prepared: 'I prepared this record', performed: 'I performed the work recorded here', reviewed: 'I reviewed this record', verified: 'I verified the results recorded here',
  approved: 'I approve this record', witness: 'I witnessed the work recorded here', customer: 'Accepted on behalf of the customer', supplier: 'Confirmed on behalf of the supplier',
};
const keyOf = (s: string) => s.toLowerCase().replace(/[^a-z0-9_]/g, '_').replace(/^[^a-z]+/, '').slice(0, 60);
const num = (v: string) => (v === '' ? null : Number(v));

type Sel = { kind: 'page' } | { kind: 'section' | 'field' | 'table' | 'signature'; key: string };

/** Turns a version 1 field list into a sectioned definition so it can be designed with sections, tables and signatures. */
export function upgradeToV2(d: FormDef): FormDef {
  if (d.schemaVersion !== 1) return d;
  return { ...d, schemaVersion: 2, sections: d.fields.some((f) => !f.section) ? [{ key: 'main', title: 'Form', columns: 2 }] : d.sections, fields: d.fields.map((f) => ({ ...f, section: f.section ?? 'main' })) };
}

/** Removes empty optional values so the stored definition stays small and readable. */
export function cleanDefinition(d: FormDef): FormDef {
  const strip = <T extends object>(o: T): T => Object.fromEntries(Object.entries(o).filter(([, v]) => v !== undefined && v !== null && v !== '' && !(Array.isArray(v) && v.length === 0))) as T;
  const col = (c: ColumnDef): ColumnDef => strip({ ...c, columns: c.columns?.map(col) });
  return { ...d, schemaVersion: 2, sections: d.sections.map(strip), fields: d.fields.map(col), tables: d.tables.map((t) => strip({ ...t, columns: t.columns.map(col) })), signatures: d.signatures.map(strip), page: strip(d.page) as PageSetup };
}

function unique(base: string, taken: string[]) { let k = base, i = 2; while (taken.includes(k)) k = `${base}_${i++}`; return k; }

/**
 * Form template designer for sectioned forms: structure on the left, a live working preview in the middle (enter sample values and
 * calculate on the server), properties of the selected item on the right, and a print preview of the standardized or original output.
 */
export function FormDesigner({ value, onChange }: { value: FormDef; onChange: (d: FormDef) => void }) {
  const d = value;
  const [sel, setSel] = useState<Sel>({ kind: 'page' });
  const [sample, setSample] = useState<Record<string, string | null>>({});
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const [pdf, setPdf] = useState<string | null>(null);
  const allKeys = useMemo(() => [...d.fields.map((f) => f.key), ...d.tables.map((t) => t.key), ...d.sections.map((s) => s.key)], [d]);
  const firstSection = d.sections[0]?.key ?? null;
  const currentSection = sel.kind === 'section' ? sel.key : sel.kind !== 'page' ? ([...d.fields, ...d.tables, ...d.signatures].find((x) => x.key === sel.key)?.section ?? firstSection) : firstSection;

  const add = (kind: 'section' | 'field' | 'table' | 'signature') => {
    if (kind === 'section') { const key = unique(`section_${d.sections.length + 1}`, allKeys); onChange({ ...d, sections: [...d.sections, { key, title: 'New section', columns: 2 }] }); setSel({ kind, key }); }
    if (kind === 'field') { const key = unique(`field_${d.fields.length + 1}`, allKeys); onChange({ ...d, fields: [...d.fields, { key, label: 'New field', type: 'text', section: currentSection }] }); setSel({ kind, key }); }
    if (kind === 'table') { const key = unique(`table_${d.tables.length + 1}`, allKeys); onChange({ ...d, tables: [...d.tables, { key, title: 'New table', section: currentSection, minRows: 0, maxRows: 500, columns: [{ key: 'item', label: 'Item', type: 'text' }], repeatHeaderOnEachPage: true }] }); setSel({ kind, key }); }
    if (kind === 'signature') { const key = unique(`sign_${d.signatures.length + 1}`, d.signatures.map((s) => s.key)); onChange({ ...d, signatures: [...d.signatures, { key, label: 'Approved by', role: 'approved', meaning: roleMeaning.approved, section: currentSection, order: d.signatures.length + 1, required: true }] }); setSel({ kind, key }); }
  };
  const listOf = (kind: 'section' | 'field' | 'table' | 'signature') => ({ section: d.sections, field: d.fields, table: d.tables, signature: d.signatures }[kind]) as { key: string }[];
  const setList = (kind: 'section' | 'field' | 'table' | 'signature', list: unknown[]) => onChange({ ...d, ...({ section: { sections: list }, field: { fields: list }, table: { tables: list }, signature: { signatures: list } }[kind]) } as FormDef);
  const move = (kind: 'section' | 'field' | 'table' | 'signature', key: string, dir: -1 | 1) => {
    const l = [...listOf(kind)]; const i = l.findIndex((x) => x.key === key); const j = i + dir;
    if (i < 0 || j < 0 || j >= l.length) return; [l[i], l[j]] = [l[j], l[i]]; setList(kind, l);
  };
  const remove = (kind: 'section' | 'field' | 'table' | 'signature', key: string) => {
    if (kind === 'section') {
      const parent = d.sections.find((s) => s.key === key)?.parent ?? null;
      onChange({ ...d, sections: d.sections.filter((s) => s.key !== key).map((s) => (s.parent === key ? { ...s, parent } : s)), fields: d.fields.map((f) => (f.section === key ? { ...f, section: parent } : f)), tables: d.tables.map((t) => (t.section === key ? { ...t, section: parent } : t)), signatures: d.signatures.map((s) => (s.section === key ? { ...s, section: parent } : s)) });
    } else setList(kind, listOf(kind).filter((x) => x.key !== key));
    setSel({ kind: 'page' });
  };
  const patch = <T,>(kind: 'section' | 'field' | 'table' | 'signature', key: string, p: Partial<T>) => {
    if (kind !== 'signature' && 'key' in p && p.key !== key) { // renaming: keep references in sections, formulas are left to the server check
      const nk = p.key as string;
      const re = (s?: string | null) => (s === key ? nk : s);
      const next = { ...d, sections: d.sections.map((s) => ({ ...s, parent: re(s.parent) })), fields: d.fields.map((f) => ({ ...f, section: re(f.section) })), tables: d.tables.map((t) => ({ ...t, section: re(t.section) })), signatures: d.signatures.map((s) => ({ ...s, section: re(s.section) })) };
      const l = ({ section: next.sections, field: next.fields, table: next.tables }[kind] as { key: string }[]).map((x) => (x.key === key ? { ...x, ...p } : x));
      onChange({ ...next, ...({ section: { sections: l }, field: { fields: l }, table: { tables: l } }[kind]) } as FormDef);
      setSel({ kind, key: nk });
      return;
    }
    setList(kind, listOf(kind).map((x) => (x.key === key ? { ...x, ...p } : x)));
    if ('key' in p) setSel({ kind, key: p.key as string });
  };

  const check = async () => {
    setMsg(null);
    try { const r = await post<{ values: Record<string, string | null>; fields: number; tables: number; sections: number; signatures: number }>('/api/forms/preview/evaluate', { fieldSchema: JSON.stringify(cleanDefinition(d)), values: sample });
      setSample(r.values); setMsg({ ok: true, text: `The form is valid: ${r.sections} sections, ${r.fields} fields, ${r.tables} tables, ${r.signatures} signature blocks. Calculated values were updated in the preview.` }); }
    catch (e) { setMsg({ ok: false, text: (e as Error).message }); }
  };
  const printPreview = async (style: 'standardized' | 'original') => {
    setMsg(null);
    try { const b = await postBlob('/api/forms/preview/pdf', { fieldSchema: JSON.stringify(cleanDefinition(d)), style, values: sample }); setPdf(URL.createObjectURL(b)); }
    catch (e) { setMsg({ ok: false, text: (e as Error).message }); }
  };

  // ---- structure tree
  const Item = ({ kind, k, label, depth, chip }: { kind: 'section' | 'field' | 'table' | 'signature'; k: string; label: string; depth: number; chip?: string }) => {
    const active = sel.kind === kind && sel.key === k;
    return (
      <ListItemButton dense selected={active} onClick={() => setSel({ kind, key: k })} sx={{ pl: 1 + depth * 1.5, pr: 0.5, gap: 0.5 }} aria-label={`${kind} ${label}`}>
        <ListItemText primary={label || k} secondary={chip} slotProps={{ primary: { noWrap: true, fontSize: 13, fontWeight: kind === 'section' ? 600 : 400 }, secondary: { fontSize: 11 } }} />
        {active && <>
          <Tooltip title="Move up"><IconButton size="small" aria-label="Move up" onClick={(e) => { e.stopPropagation(); move(kind, k, -1); }}><ArrowUpward sx={{ fontSize: 16 }} /></IconButton></Tooltip>
          <Tooltip title="Move down"><IconButton size="small" aria-label="Move down" onClick={(e) => { e.stopPropagation(); move(kind, k, 1); }}><ArrowDownward sx={{ fontSize: 16 }} /></IconButton></Tooltip>
          <Tooltip title="Remove"><IconButton size="small" aria-label="Remove" onClick={(e) => { e.stopPropagation(); remove(kind, k); }}><DeleteOutline sx={{ fontSize: 16 }} /></IconButton></Tooltip>
        </>}
      </ListItemButton>
    );
  };
  const typeName = (t: string) => allFieldTypes.find((x) => x[0] === t)?.[1] ?? t;
  const Contents = ({ section, depth }: { section: string | null; depth: number }) => {
    const inSec = (s?: string | null) => (section === null ? !s || !d.sections.some((x) => x.key === s) : s === section);
    return <>
      {d.fields.filter((f) => inSec(f.section)).map((f) => <Item key={`f-${f.key}`} kind="field" k={f.key} label={f.label} depth={depth} chip={typeName(f.type)} />)}
      {d.tables.filter((t) => inSec(t.section)).map((t) => <Item key={`t-${t.key}`} kind="table" k={t.key} label={t.title} depth={depth} chip={`Table, ${t.columns.length} columns`} />)}
      {d.signatures.filter((s) => inSec(s.section)).map((s) => <Item key={`s-${s.key}`} kind="signature" k={s.key} label={s.label} depth={depth} chip={`Signature, ${roleLabel[s.role] ?? s.role}`} />)}
      {section !== null && d.sections.filter((s) => s.parent === section).map((s) => <Box key={s.key}><Item kind="section" k={s.key} label={s.title} depth={depth} chip={s.repeatable ? 'Repeatable section' : 'Section'} /><Contents section={s.key} depth={depth + 1} /></Box>)}
    </>;
  };

  return (
    <Box>
      <Stack direction="row" gap={1} flexWrap="wrap" sx={{ mb: 1.5 }}>
        <Button size="small" variant="outlined" onClick={() => add('section')}>Add section</Button>
        <Button size="small" variant="outlined" onClick={() => add('field')}>Add field</Button>
        <Button size="small" variant="outlined" onClick={() => add('table')}>Add table</Button>
        <Button size="small" variant="outlined" onClick={() => add('signature')}>Add signature block</Button>
        <Box sx={{ flex: 1 }} />
        <Button size="small" onClick={check}>Check and calculate</Button>
        <Button size="small" onClick={() => printPreview('standardized')}>Print preview</Button>
        {d.layout && <Button size="small" onClick={() => printPreview('original')}>Print preview, original look</Button>}
      </Stack>
      {msg && <Alert severity={msg.ok ? 'success' : 'error'} sx={{ mb: 1.5 }} onClose={() => setMsg(null)}>{msg.text}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '220px minmax(0, 1fr) 300px', xl: '260px minmax(0, 1fr) 360px' }, gap: 1.5, alignItems: 'start' }}>
        <Paper variant="outlined" sx={{ maxHeight: 'calc(100vh - 160px)', overflow: 'auto', position: { md: 'sticky' }, top: 0 }} aria-label="Form structure" component="nav">
          <List dense disablePadding>
            <ListItemButton dense selected={sel.kind === 'page'} onClick={() => setSel({ kind: 'page' })}><ListItemText primary="Page and print setup" slotProps={{ primary: { fontSize: 13, fontWeight: 600 } }} /></ListItemButton>
            <Divider />
            <Contents section={null} depth={0} />
            {d.sections.filter((s) => !s.parent || !d.sections.some((x) => x.key === s.parent)).map((s) => <Box key={s.key}><Item kind="section" k={s.key} label={s.title} depth={0} chip={s.repeatable ? 'Repeatable section' : 'Section'} /><Contents section={s.key} depth={1} /></Box>)}
          </List>
        </Paper>
        <Paper variant="outlined" sx={{ p: 2, minWidth: 0 }} aria-label="Live preview">
          <Typography variant="overline" color="text.secondary">Live preview: enter sample values to try the form</Typography>
          {d.fields.length + d.tables.length === 0 ? <Typography color="text.secondary" sx={{ mt: 1 }}>Add a field or table to start.</Typography>
            : <FormView def={d} values={sample} onChange={setSample} readOnly={false} />}
        </Paper>
        <Paper variant="outlined" sx={{ p: 1.5 }} aria-label="Properties">
          {sel.kind === 'page' && <PageProps page={d.page} onChange={(page) => onChange({ ...d, page })} />}
          {sel.kind === 'section' && (() => { const s = d.sections.find((x) => x.key === sel.key); return s ? <SectionProps s={s} d={d} onChange={(p) => patch<SectionDef>('section', s.key, p)} /> : null; })()}
          {sel.kind === 'field' && (() => { const f = d.fields.find((x) => x.key === sel.key); return f ? <FieldProps f={f} d={d} onChange={(p) => patch<ColumnDef>('field', f.key, p)} /> : null; })()}
          {sel.kind === 'table' && (() => { const t = d.tables.find((x) => x.key === sel.key); return t ? <TableProps t={t} d={d} onChange={(p) => patch<TableDef>('table', t.key, p)} /> : null; })()}
          {sel.kind === 'signature' && (() => { const s = d.signatures.find((x) => x.key === sel.key); return s ? <SignatureProps s={s} d={d} onChange={(p) => patch<SignatureDef>('signature', s.key, p)} /> : null; })()}
        </Paper>
      </Box>
      <Dialog open={!!pdf} onClose={() => { if (pdf) URL.revokeObjectURL(pdf); setPdf(null); }} maxWidth="lg" fullWidth>
        <DialogTitle>Print preview</DialogTitle>
        <DialogContent>{pdf && <Box component="iframe" title="Print preview" src={pdf} sx={{ width: '100%', height: '75vh', border: 0 }} />}</DialogContent>
        <DialogActions><Button onClick={() => { if (pdf) URL.revokeObjectURL(pdf); setPdf(null); }}>Close</Button></DialogActions>
      </Dialog>
    </Box>
  );
}

// ---------------------------------------------------------------- property panels

const T = (p: { label: string; value: string | number | null | undefined; onChange: (v: string) => void; multiline?: boolean; type?: string; helperText?: string; select?: [string, string][] }) => (
  <TextField size="small" fullWidth label={p.label} value={p.value ?? ''} type={p.type} multiline={p.multiline} minRows={p.multiline ? 2 : undefined} helperText={p.helperText} select={!!p.select}
    onChange={(e) => p.onChange(e.target.value)}>{p.select?.map(([v, l]) => <MenuItem key={v} value={v}>{l}</MenuItem>)}</TextField>
);
const Check = ({ label, checked, onChange }: { label: string; checked?: boolean; onChange: (v: boolean) => void }) => <FormControlLabel sx={{ m: 0 }} control={<Checkbox size="small" checked={!!checked} onChange={(e) => onChange(e.target.checked)} />} label={<Typography variant="body2">{label}</Typography>} />;
const sectionChoices = (d: FormDef, exclude?: string): [string, string][] => [['', 'Not in a section'], ...d.sections.filter((s) => s.key !== exclude).map((s) => [s.key, s.title] as [string, string])];

function ConditionEditor({ label, c, d, onChange }: { label: string; c?: Condition | null; d: FormDef; onChange: (c: Condition | null) => void }) {
  return (
    <Box>
      <Typography variant="caption" color="text.secondary">{label}</Typography>
      <Stack gap={1} sx={{ mt: 0.5 }}>
        <T label="When field" value={c?.field ?? ''} select={[['', 'Always'], ...d.fields.map((f) => [f.key, f.label || f.key] as [string, string])]} onChange={(v) => onChange(v ? { field: v, operator: c?.operator ?? 'equals', value: c?.value ?? '' } : null)} />
        {c && <Stack direction="row" gap={1}><T label="Is" value={c.operator} select={operators.map((o) => [o, o.replace(/([A-Z])/g, ' $1').toLowerCase()])} onChange={(v) => onChange({ ...c, operator: v })} />
          {!['isEmpty', 'isNotEmpty'].includes(c.operator) && <T label="Value" value={c.value ?? ''} onChange={(v) => onChange({ ...c, value: v })} />}</Stack>}
      </Stack>
    </Box>
  );
}

function Band({ label, b, onChange }: { label: string; b?: BandText | null; onChange: (b: BandText | null) => void }) {
  const set = (k: keyof BandText, v: string) => { const n = { ...(b ?? {}), [k]: v || null }; onChange(n.left || n.center || n.right ? n : null); };
  return <Box><Typography variant="caption" color="text.secondary">{label}</Typography><Stack gap={1} sx={{ mt: 0.5 }}><T label="Left" value={b?.left} onChange={(v) => set('left', v)} /><T label="Centre" value={b?.center} onChange={(v) => set('center', v)} /><T label="Right" value={b?.right} onChange={(v) => set('right', v)} /></Stack></Box>;
}

function PageProps({ page, onChange }: { page: PageSetup; onChange: (p: PageSetup) => void }) {
  const set = (p: Partial<PageSetup>) => onChange({ ...page, ...p });
  return (
    <Stack gap={1.5}>
      <Typography variant="subtitle2">Page and print setup</Typography>
      <Stack direction="row" gap={1}><T label="Paper" value={page.size} select={['Letter', 'Legal', 'A4', 'A3', 'Tabloid'].map((x) => [x, x])} onChange={(v) => set({ size: v })} />
        <T label="Orientation" value={page.orientation} select={[['portrait', 'Portrait'], ['landscape', 'Landscape']]} onChange={(v) => set({ orientation: v as PageSetup['orientation'] })} /></Stack>
      <Stack direction="row" gap={1}><T label="Margins (mm)" type="number" value={page.marginMm} onChange={(v) => set({ marginMm: Number(v) })} /><T label="Font size (pt)" type="number" value={page.fontSize} onChange={(v) => set({ fontSize: Number(v) })} /></Stack>
      <T label="Default print output" value={page.printStyle} select={[['standardized', 'QualGuard IQ standardized'], ['original', 'Original look (converted files)']]} onChange={(v) => set({ printStyle: v as PageSetup['printStyle'] })} />
      <T label="Form look" value={page.theme ?? 'report'} select={formThemes} onChange={(v) => set({ theme: v as FormTheme })} helperText="Changes how sections, tables and signatures are drawn on screen and on paper; fields and rules behave the same." />
      <T label="Watermark" value={page.watermark} onChange={(v) => set({ watermark: v || null })} helperText="Status watermarks such as DRAFT are added automatically." />
      <T label="Confidentiality" value={page.confidentiality ?? 'Internal'} onChange={(v) => set({ confidentiality: v })} />
      <Band label="Header" b={page.header} onChange={(header) => set({ header })} />
      <Band label="Footer" b={page.footer} onChange={(footer) => set({ footer })} />
      <Typography variant="caption" color="text.secondary">Header and footer tokens: {'{template} {revision} {record} {title} {page} {pages} {site} {printed} {printedBy} {status} {confidentiality}'}</Typography>
    </Stack>
  );
}

function SectionProps({ s, d, onChange }: { s: SectionDef; d: FormDef; onChange: (p: Partial<SectionDef>) => void }) {
  return (
    <Stack gap={1.5}>
      <Typography variant="subtitle2">Section</Typography>
      <T label="Title" value={s.title} onChange={(v) => onChange({ title: v })} />
      <T label="Key" value={s.key} onChange={(v) => onChange({ key: keyOf(v) })} />
      <T label="Inside section" value={s.parent ?? ''} select={sectionChoices(d, s.key)} onChange={(v) => onChange({ parent: v || null })} />
      <T label="Instructions" multiline value={s.instructions} onChange={(v) => onChange({ instructions: v || null })} />
      <T label="Columns" value={s.columns} select={[['1', '1'], ['2', '2'], ['3', '3'], ['4', '4']]} onChange={(v) => onChange({ columns: Number(v) })} />
      <Check label="Repeatable (filled in once per item)" checked={s.repeatable} onChange={(v) => onChange({ repeatable: v })} />
      {s.repeatable && <Stack direction="row" gap={1}><T label="Item label" value={s.repeatLabel} onChange={(v) => onChange({ repeatLabel: v || null })} /><T label="Min" type="number" value={s.minRepeat} onChange={(v) => onChange({ minRepeat: num(v) })} /><T label="Max" type="number" value={s.maxRepeat} onChange={(v) => onChange({ maxRepeat: num(v) })} /></Stack>}
      <ConditionEditor label="Show this section" c={s.visibleWhen} d={d} onChange={(c) => onChange({ visibleWhen: c })} />
      <T label="Print orientation" value={s.orientation ?? ''} select={[['', 'Same as page'], ['portrait', 'Portrait'], ['landscape', 'Landscape']]} onChange={(v) => onChange({ orientation: v || null })} />
      <Check label="Start on a new page" checked={s.pageBreakBefore} onChange={(v) => onChange({ pageBreakBefore: v })} />
      <Check label="Keep together on one page" checked={s.keepTogether} onChange={(v) => onChange({ keepTogether: v })} />
      <T label="Section style" value={s.style ?? ''} select={[['', 'Follow the form look'], ['standard', 'Plain heading'], ['panel', 'Panel'], ['callout', 'Call-out'], ['titleblock', 'Title block'], ['steps', 'Numbered step'], ['checklist', 'Checklist card'], ['ledger', 'Ledger (small print)'], ['signoff', 'Sign-off block']]} onChange={(v) => onChange({ style: (v || null) as SectionStyle | null })} />
    </Stack>
  );
}

function FieldProps({ f, d, onChange, inTable }: { f: ColumnDef; d: FormDef; onChange: (p: Partial<ColumnDef>) => void; inTable?: boolean }) {
  const isNum = numericTypes.includes(f.type);
  return (
    <Stack gap={1.5}>
      {!inTable && <Typography variant="subtitle2">Field</Typography>}
      <T label="Label" value={f.label} onChange={(v) => onChange({ label: v })} />
      <T label="Key" value={f.key} onChange={(v) => onChange({ key: keyOf(v) })} helperText="Used in formulas and exports." />
      <T label="Type" value={f.type} select={inTable ? [...allFieldTypes, ['table', 'Nested table']] : allFieldTypes} onChange={(v) => onChange({ type: v })} />
      {!inTable && <T label="Section" value={f.section ?? ''} select={sectionChoices(d)} onChange={(v) => onChange({ section: v || null })} />}
      {f.type !== 'calc' && f.type !== 'instruction' && <Check label="Required" checked={f.required} onChange={(v) => onChange({ required: v })} />}
      {f.type === 'calc' && <>
        <T label="Formula" multiline value={f.formula} onChange={(v) => onChange({ formula: v })} helperText="For example (d1 + d2) / 2, ROUND(qty * price, 2), IF(x > 5, &quot;High&quot;, &quot;Low&quot;), SUM(table.column), COUNTIF(table.result, &quot;FAIL&quot;)." />
        <T label="True / false result words" value={(f.resultLabels ?? []).join(', ')} onChange={(v) => { const p = v.split(',').map((x) => x.trim()).filter(Boolean); onChange({ resultLabels: p.length === 2 ? p : null }); }} helperText="Two words, e.g. PASS, FAIL" />
      </>}
      {optionTypes.includes(f.type) && <T label="Options (one per line)" multiline value={(f.options ?? []).join('\n')} onChange={(v) => onChange({ options: v.split('\n').map((x) => x.trim()).filter(Boolean) })} />}
      {isNum && <Stack direction="row" gap={1}>
        {f.type !== 'calc' && <T label={f.type === 'measurement' ? 'Lower limit' : 'Min'} type="number" value={f.min} onChange={(v) => onChange({ min: num(v) })} />}
        {f.type !== 'calc' && <T label={f.type === 'measurement' ? 'Upper limit' : 'Max'} type="number" value={f.max} onChange={(v) => onChange({ max: num(v) })} />}
        <T label="Decimals" type="number" value={f.decimals} onChange={(v) => onChange({ decimals: num(v) })} />
      </Stack>}
      {f.type === 'measurement' && <T label="Nominal" type="number" value={f.nominal} onChange={(v) => onChange({ nominal: num(v) })} />}
      {isNum && <T label="Unit" value={f.unit} onChange={(v) => onChange({ unit: v || null })} />}
      {['attachment', 'photo'].includes(f.type) && <T label="Accepted file types" value={(f.accept ?? []).join(', ')} onChange={(v) => onChange({ accept: v.split(',').map((x) => x.trim()).filter(Boolean) })} helperText=".pdf, .xlsx, .docx, .jpg" />}
      {['text', 'longtext'].includes(f.type) && <T label="Maximum length" type="number" value={f.maxLength} onChange={(v) => onChange({ maxLength: num(v) })} />}
      <T label={f.type === 'instruction' ? 'Instruction text' : 'Help text'} multiline value={f.help} onChange={(v) => onChange({ help: v || null })} />
      {!inTable && f.type !== 'instruction' && <T label="Width (of 12 columns)" type="number" value={f.width} onChange={(v) => onChange({ width: num(v) })} />}
      {!inTable && <ConditionEditor label="Show this field" c={f.visibleWhen} d={d} onChange={(c) => onChange({ visibleWhen: c })} />}
      {f.sourceLocation && <Chip size="small" label={`From ${f.sourceLocation}`} />}
    </Stack>
  );
}

function ColumnsEditor({ cols, d, onChange, depth = 0 }: { cols: ColumnDef[]; d: FormDef; onChange: (c: ColumnDef[]) => void; depth?: number }) {
  const [open, setOpen] = useState<number | null>(null);
  const set = (i: number, p: Partial<ColumnDef>) => onChange(cols.map((c, j) => (j === i ? { ...c, ...p } : c)));
  return (
    <Stack gap={0.5}>
      {cols.map((c, i) => (
        <Paper key={i} variant="outlined" sx={{ p: 0.75 }}>
          <Stack direction="row" alignItems="center" gap={0.5}>
            <Typography variant="body2" sx={{ flex: 1, cursor: 'pointer' }} noWrap onClick={() => setOpen(open === i ? null : i)}>{c.label || c.key} <Typography component="span" variant="caption" color="text.secondary">{c.type}</Typography></Typography>
            <IconButton size="small" aria-label="Move column up" disabled={i === 0} onClick={() => { const n = [...cols]; [n[i - 1], n[i]] = [n[i], n[i - 1]]; onChange(n); }}><ArrowUpward sx={{ fontSize: 16 }} /></IconButton>
            <IconButton size="small" aria-label="Remove column" onClick={() => onChange(cols.filter((_, j) => j !== i))}><DeleteOutline sx={{ fontSize: 16 }} /></IconButton>
          </Stack>
          {open === i && <Box sx={{ mt: 1 }}>
            <FieldProps f={c} d={d} inTable onChange={(p) => set(i, p)} />
            {c.type === 'table' && depth < 2 && <Box sx={{ mt: 1, pl: 1, borderLeft: 2, borderColor: 'divider' }}><Typography variant="caption">Nested table columns</Typography><ColumnsEditor cols={c.columns ?? []} d={d} depth={depth + 1} onChange={(n) => set(i, { columns: n })} /></Box>}
          </Box>}
        </Paper>
      ))}
      <Box><Button size="small" onClick={() => { onChange([...cols, { key: unique(`col_${cols.length + 1}`, cols.map((c) => c.key)), label: 'New column', type: 'text' }]); setOpen(cols.length); }}>Add column</Button></Box>
    </Stack>
  );
}

function TableProps({ t, d, onChange }: { t: TableDef; d: FormDef; onChange: (p: Partial<TableDef>) => void }) {
  const colChoices: [string, string][] = t.columns.map((c) => [c.key, c.label || c.key]);
  return (
    <Stack gap={1.5}>
      <Typography variant="subtitle2">Table</Typography>
      <T label="Title" value={t.title} onChange={(v) => onChange({ title: v })} />
      <T label="Key" value={t.key} onChange={(v) => onChange({ key: keyOf(v) })} />
      <T label="Section" value={t.section ?? ''} select={sectionChoices(d)} onChange={(v) => onChange({ section: v || null })} />
      <T label="Instructions" multiline value={t.instructions} onChange={(v) => onChange({ instructions: v || null })} />
      <Typography variant="caption" color="text.secondary">Columns</Typography>
      <ColumnsEditor cols={t.columns} d={d} onChange={(columns) => onChange({ columns })} />
      <T label="Fixed rows (one per line, leave empty for rows added while filling in)" multiline value={(t.fixedRows ?? []).join('\n')} onChange={(v) => { const r = v.split('\n').map((x) => x.trim()).filter(Boolean); onChange({ fixedRows: r.length ? r : null }); }} />
      {!t.fixedRows?.length && <Stack direction="row" gap={1}><T label="Min rows" type="number" value={t.minRows} onChange={(v) => onChange({ minRows: Number(v) || 0 })} /><T label="Max rows" type="number" value={t.maxRows} onChange={(v) => onChange({ maxRows: Number(v) || 500 })} /></Stack>}
      <T label="Header groups (label: span, one per line)" multiline value={(t.headerGroups ?? []).map((g) => `${g.label}: ${g.span}`).join('\n')}
        onChange={(v) => { const g = v.split('\n').map((l) => l.trim()).filter(Boolean).map((l) => { const m = l.match(/^(.*?)(?::\s*(\d+))?$/)!; return { label: m[1].trim(), span: Number(m[2] ?? 1) }; }); onChange({ headerGroups: g.length ? g : null }); }} />
      <Typography variant="caption" color="text.secondary">Totals</Typography>
      {(t.totals ?? []).map((tot, i) => (
        <Stack key={i} direction="row" gap={1} alignItems="center">
          <T label="Column" value={tot.column} select={colChoices} onChange={(v) => onChange({ totals: t.totals!.map((x, j) => (j === i ? { ...x, column: v } : x)) })} />
          <T label="Function" value={tot.function} select={totalFunctions.map((f) => [f, f])} onChange={(v) => onChange({ totals: t.totals!.map((x, j) => (j === i ? { ...x, function: v } : x)) })} />
          <IconButton size="small" aria-label="Remove total" onClick={() => onChange({ totals: t.totals!.filter((_, j) => j !== i) })}><DeleteOutline sx={{ fontSize: 16 }} /></IconButton>
        </Stack>
      ))}
      <Box><Button size="small" disabled={!t.columns.length} onClick={() => onChange({ totals: [...(t.totals ?? []), { column: t.columns[0].key, function: 'sum' } as TableTotal] })}>Add total</Button></Box>
      <T label="Running totals for" value={(t.runningTotals ?? []).join(', ')} onChange={(v) => { const r = v.split(',').map((x) => x.trim()).filter(Boolean); onChange({ runningTotals: r.length ? r : null }); }} helperText="Column keys, comma separated" />
      <T label="Subtotals by column" value={t.subtotalBy ?? ''} select={[['', 'No subtotals'], ...colChoices]} onChange={(v) => onChange({ subtotalBy: v || null })} />
      <Check label="Repeat the header on each printed page" checked={t.repeatHeaderOnEachPage ?? true} onChange={(v) => onChange({ repeatHeaderOnEachPage: v })} />
      <ConditionEditor label="Show this table" c={t.visibleWhen} d={d} onChange={(c) => onChange({ visibleWhen: c })} />
    </Stack>
  );
}

function SignatureProps({ s, d, onChange }: { s: SignatureDef; d: FormDef; onChange: (p: Partial<SignatureDef>) => void }) {
  return (
    <Stack gap={1.5}>
      <Typography variant="subtitle2">Signature block</Typography>
      <T label="Label" value={s.label} onChange={(v) => onChange({ label: v })} />
      <T label="Key" value={s.key} onChange={(v) => onChange({ key: keyOf(v) })} />
      <T label="Role" value={s.role} select={Object.entries(roleLabel)} onChange={(v) => onChange({ role: v, meaning: !s.meaning || Object.values(roleMeaning).includes(s.meaning) ? roleMeaning[v] : s.meaning })} helperText="Prepared, performed, verified and witness are signed before submission; the others after." />
      <T label="Meaning of the signature" multiline value={s.meaning} onChange={(v) => onChange({ meaning: v })} />
      <T label="Section" value={s.section ?? ''} select={sectionChoices(d)} onChange={(v) => onChange({ section: v || null })} />
      <Stack direction="row" gap={1}><T label="Order" type="number" value={s.order} onChange={(v) => onChange({ order: Number(v) || 0 })} /><Check label="Required" checked={s.required} onChange={(v) => onChange({ required: v })} /></Stack>
      <T label="Permission needed to sign" value={s.requiredPermission} onChange={(v) => onChange({ requiredPermission: v || null })} helperText="From the permission catalog, e.g. forms.approve. Empty uses the default for the role." />
      <T label="Must be a different person from" value={(s.distinctFrom ?? []).join(', ')} select={undefined} onChange={(v) => { const r = v.split(',').map((x) => x.trim()).filter(Boolean); onChange({ distinctFrom: r.length ? r : null }); }} helperText={`Other block keys: ${d.signatures.filter((x) => x.key !== s.key).map((x) => x.key).join(', ') || 'none'}`} />
    </Stack>
  );
}
