import { Box, Button, Checkbox, FormControlLabel, IconButton, MenuItem, Paper, Stack, TextField, Tooltip, Typography } from '@mui/material';
import DeleteOutline from '@mui/icons-material/DeleteOutline';
import ArrowUpward from '@mui/icons-material/ArrowUpward';

export interface FieldDef {
  key: string; label: string; type: string; required?: boolean; options?: string[] | null;
  min?: number | null; max?: number | null; unit?: string | null; formula?: string | null; section?: string | null;
  visibleWhen?: { field: string; operator: string; value?: string | null } | null;
  help?: string | null; placeholder?: string | null; width?: number | null; decimals?: number | null; maxLength?: number | null;
  nominal?: number | null; resultLabels?: string[] | null; accept?: string[] | null; sourceLocation?: string | null;
}
export const fieldTypes = [
  ['text', 'Short text'], ['longtext', 'Long text'], ['number', 'Number'], ['date', 'Date'], ['boolean', 'Yes / no'], ['choice', 'Choice list'], ['calc', 'Calculated'],
];

export const parseFields = (json?: string | null): FieldDef[] => { try { return json ? JSON.parse(json) : []; } catch { return []; } };
export const serializeFields = (f: FieldDef[]) => JSON.stringify(f.map((x) => ({
  ...x, options: x.type === 'choice' ? x.options?.filter(Boolean) : undefined, formula: x.type === 'calc' ? x.formula : undefined,
  min: x.type === 'number' && x.min != null && String(x.min) !== '' ? Number(x.min) : undefined, max: x.type === 'number' && x.max != null && String(x.max) !== '' ? Number(x.max) : undefined,
})));

/** Field designer for a form template revision. The server validates keys, types and formulas when saved. */
export function FieldBuilder({ fields, onChange }: { fields: FieldDef[]; onChange: (f: FieldDef[]) => void }) {
  const set = (i: number, patch: Partial<FieldDef>) => onChange(fields.map((f, j) => (j === i ? { ...f, ...patch } : f)));
  return (
    <Box>
      <Typography variant="subtitle2" sx={{ mb: 1 }}>Fields</Typography>
      <Stack gap={1}>
        {fields.map((f, i) => (
          <Paper key={i} variant="outlined" sx={{ p: 1.5, display: 'grid', gridTemplateColumns: '150px 1fr 150px auto auto', gap: 1, alignItems: 'center' }}>
            <TextField size="small" label="Key" value={f.key} onChange={(e) => set(i, { key: e.target.value.toLowerCase().replace(/[^a-z0-9_]/g, '_') })} />
            <TextField size="small" label="Label" value={f.label} onChange={(e) => set(i, { label: e.target.value })} />
            <TextField size="small" select label="Type" value={f.type} onChange={(e) => set(i, { type: e.target.value })}>
              {fieldTypes.map(([v, l]) => <MenuItem key={v} value={v}>{l}</MenuItem>)}
            </TextField>
            <FormControlLabel sx={{ m: 0 }} control={<Checkbox size="small" checked={!!f.required} disabled={f.type === 'calc'} onChange={(e) => set(i, { required: e.target.checked })} />} label="Required" />
            <Stack direction="row">
              <Tooltip title="Move up"><span><IconButton size="small" disabled={i === 0} onClick={() => { const n = [...fields]; [n[i - 1], n[i]] = [n[i], n[i - 1]]; onChange(n); }}><ArrowUpward fontSize="small" /></IconButton></span></Tooltip>
              <Tooltip title="Remove"><IconButton size="small" onClick={() => onChange(fields.filter((_, j) => j !== i))}><DeleteOutline fontSize="small" /></IconButton></Tooltip>
            </Stack>
            {f.type === 'choice' && <TextField size="small" label="Options (comma separated)" sx={{ gridColumn: '1 / -1' }} value={(f.options ?? []).join(', ')} onChange={(e) => set(i, { options: e.target.value.split(',').map((s) => s.trim()) })} />}
            {f.type === 'calc' && <TextField size="small" label="Formula, e.g. (d1 + d2) / 2" sx={{ gridColumn: '1 / -1' }} value={f.formula ?? ''} onChange={(e) => set(i, { formula: e.target.value })} />}
            {f.type === 'number' && <Stack direction="row" gap={1} sx={{ gridColumn: '1 / -1' }}>
              <TextField size="small" type="number" label="Min" value={f.min ?? ''} onChange={(e) => set(i, { min: e.target.value === '' ? null : Number(e.target.value) })} />
              <TextField size="small" type="number" label="Max" value={f.max ?? ''} onChange={(e) => set(i, { max: e.target.value === '' ? null : Number(e.target.value) })} />
              <TextField size="small" label="Unit" value={f.unit ?? ''} onChange={(e) => set(i, { unit: e.target.value || null })} />
            </Stack>}
          </Paper>
        ))}
      </Stack>
      <Button size="small" sx={{ mt: 1 }} onClick={() => onChange([...fields, { key: `field_${fields.length + 1}`, label: '', type: 'text', required: false }])}>Add field</Button>
    </Box>
  );
}

export function SchemaPreview({ schema }: { schema: string }) {
  const fields = parseFields(schema);
  return (
    <Paper sx={{ p: 2, maxWidth: 720 }}>
      <Typography variant="h3" sx={{ mb: 1 }}>Form fields</Typography>
      {fields.map((f) => (
        <Box key={f.key} sx={{ display: 'flex', gap: 2, py: 0.5, borderTop: 1, borderColor: 'divider', fontSize: 14 }}>
          <Box sx={{ width: 220 }}>{f.label}{f.required ? ' *' : ''}</Box>
          <Box sx={{ color: 'text.secondary' }}>{fieldTypes.find((t) => t[0] === f.type)?.[1]}{f.type === 'calc' ? ` = ${f.formula}` : ''}{f.type === 'choice' ? `: ${f.options?.join(', ')}` : ''}{f.unit ? ` (${f.unit})` : ''}</Box>
        </Box>
      ))}
    </Paper>
  );
}

/** Renders a form record. Calculated fields are read-only and filled from the server response. */
/** Mirrors the server's dynamic visibility rules so hidden fields disappear while typing; the server re-checks on save and submit. */
export function visibleFields(fields: FieldDef[], values: Record<string, string | null>): FieldDef[] {
  const match = (c: NonNullable<FieldDef['visibleWhen']>) => {
    const a = (values[c.field] ?? '').trim(); const b = (c.value ?? '').trim(); const na = Number(a), nb = Number(b); const num = a !== '' && b !== '' && !isNaN(na) && !isNaN(nb);
    const cmp = num ? na - nb : a.localeCompare(b);
    switch (c.operator) {
      case 'equals': return a !== '' && a.toLowerCase() === b.toLowerCase(); case 'notEquals': return a.toLowerCase() !== b.toLowerCase();
      case 'greaterThan': return a !== '' && cmp > 0; case 'greaterOrEqual': return a !== '' && cmp >= 0; case 'lessThan': return a !== '' && cmp < 0; case 'lessOrEqual': return a !== '' && cmp <= 0;
      case 'contains': return a.toLowerCase().includes(b.toLowerCase()); case 'in': return b.split(',').map((x) => x.trim().toLowerCase()).includes(a.toLowerCase());
      case 'isEmpty': return a === ''; case 'isNotEmpty': return a !== ''; default: return true;
    }
  };
  let shown = new Set(fields.map((f) => f.key));
  for (let i = 0; i < fields.length; i++) {
    const next = new Set([...shown].filter((k) => { const f = fields.find((x) => x.key === k)!; return !f.visibleWhen || (shown.has(f.visibleWhen.field) && match(f.visibleWhen)); }));
    if (next.size === shown.size) break; shown = next;
  }
  return fields.filter((f) => shown.has(f.key));
}

export function FormRenderer({ fields, values, onChange, readOnly }: { fields: FieldDef[]; values: Record<string, string | null>; onChange: (v: Record<string, string | null>) => void; readOnly: boolean }) {
  const set = (k: string, v: string | null) => onChange({ ...values, [k]: v });
  return (
    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: '1fr 1fr' }, gap: 2 }}>
      {visibleFields(fields, values).map((f) => {
        const v = values[f.key] ?? '';
        const label = `${f.label}${f.unit ? ` (${f.unit})` : ''}`;
        const common = { key: f.key, label, required: !!f.required, disabled: readOnly, size: 'small' as const };
        switch (f.type) {
          case 'longtext': return <TextField {...common} multiline minRows={3} sx={{ gridColumn: '1 / -1' }} value={v} onChange={(e) => set(f.key, e.target.value)} />;
          case 'number': return <TextField {...common} type="number" value={v} onChange={(e) => set(f.key, e.target.value)} slotProps={{ htmlInput: { min: f.min ?? undefined, max: f.max ?? undefined, step: 'any' } }} />;
          case 'date': return <TextField {...common} type="date" value={v} onChange={(e) => set(f.key, e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />;
          case 'boolean': return <FormControlLabel key={f.key} disabled={readOnly} control={<Checkbox checked={v === 'true'} onChange={(e) => set(f.key, e.target.checked ? 'true' : 'false')} />} label={label} />;
          case 'choice': return <TextField {...common} select value={v} onChange={(e) => set(f.key, e.target.value)}><MenuItem value="">—</MenuItem>{(f.options ?? []).map((o) => <MenuItem key={o} value={o}>{o}</MenuItem>)}</TextField>;
          case 'calc': return <TextField {...common} disabled value={v} helperText={`Calculated: ${f.formula}`} />;
          default: return <TextField {...common} value={v} onChange={(e) => set(f.key, e.target.value)} />;
        }
      })}
    </Box>
  );
}
