import { Fragment, useMemo, useState } from 'react';
import { Box, Button, Checkbox, Chip, FormControlLabel, IconButton, MenuItem, Paper, Radio, RadioGroup, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, ToggleButton, ToggleButtonGroup, Tooltip, Typography } from '@mui/material';
import DeleteOutline from '@mui/icons-material/DeleteOutline';
import Add from '@mui/icons-material/Add';
import TableRowsOutlined from '@mui/icons-material/TableRowsOutlined';
import { useQuery } from '@tanstack/react-query';
import { get } from '../api';
import { lookupLabel, useLookup, useSites, useUsers, type LookupKind } from '../components/Kit';
import { alpha, useTheme, type Theme } from '@mui/material/styles';
import { mono } from '../theme';
import { hiddenSections, matches, readRows, readTable, roleLabel, totalLabel, writeRows, type ColumnDef, type FormDef, type FormTheme, type Row, type SectionDef, type SectionStyle, type TableDef } from './definition';

type Values = Record<string, string | null>;
export interface SignatureState { key: string; signerName?: string | null; signedAt?: string | null; meaning?: string | null; valid?: boolean }
interface Ctx { theme?: FormTheme; readOnly: boolean; recordType?: string; recordId?: string; siteId?: string | null; signatures?: SignatureState[]; onSign?: (key: string) => void; canSign?: (key: string) => boolean }

const numeric = new Set(['number', 'integer', 'currency', 'percent', 'measurement', 'calc']);

/** Shows a stored value in read-only places (tables, print preview). */
export function displayValue(f: ColumnDef, v: string | null | undefined): string {
  if (v == null || v === '') return '';
  if (f.type === 'boolean') return v === 'true' ? 'Yes' : 'No';
  if (f.type === 'passfail') return v === 'na' ? 'N/A' : v === 'pass' ? 'Pass' : 'Fail';
  if (f.type === 'multichoice') { try { return (JSON.parse(v) as string[]).join(', '); } catch { return v; } }
  if (f.type === 'currency') { const n = Number(v); return isNaN(n) ? v : n.toLocaleString(undefined, { minimumFractionDigits: f.decimals ?? 2, maximumFractionDigits: f.decimals ?? 2 }); }
  if (f.type === 'percent') return `${v}%`;
  return v;
}

function RefSelect({ kind, ...p }: { kind: string; value: string; onChange: (v: string) => void; label: string; disabled: boolean; required: boolean; siteId?: string | null }) {
  const users = useUsers();
  const sites = useSites();
  const lookupKind = (['part', 'product', 'supplier', 'customer'].includes(kind) ? kind : undefined) as LookupKind | undefined;
  const lk = useLookup(lookupKind);
  const equipment = useQuery({ queryKey: ['eqlookup'], enabled: kind === 'equipment', retry: false, queryFn: async () => { try { const r = await get<unknown>('/api/calibration/equipment'); return (Array.isArray(r) ? r : (r as { items: unknown[] }).items) as { id: string; number: string; name: string }[]; } catch { return []; } } });
  let options: [string, string][] = [];
  if (kind === 'user') options = (users.data ?? []).map((u) => [u.id, u.displayName]);
  else if (kind === 'site') options = (sites.data ?? []).filter((s) => !s.isArchived).map((s) => [s.id, `${s.code} — ${s.name}`]);
  else if (kind === 'department') options = (sites.data ?? []).filter((s) => !p.siteId || s.id === p.siteId).flatMap((s) => s.departments.map((d) => [d.id, `${s.code} / ${d.code} — ${d.name}`] as [string, string]));
  else if (kind === 'equipment') options = (equipment.data ?? []).map((e) => [e.id, `${e.number} — ${e.name}`]);
  else if (lookupKind) options = (lk.data ?? []).map((r) => [r.id, lookupLabel(r)]);
  if (kind === 'record') return <TextField size="small" fullWidth label={p.label} required={p.required} disabled={p.disabled} value={p.value} onChange={(e) => p.onChange(e.target.value)} helperText="Record id" />;
  return (
    <TextField size="small" fullWidth select label={p.label} required={p.required} disabled={p.disabled} value={p.value} onChange={(e) => p.onChange(e.target.value)}>
      <MenuItem value="">—</MenuItem>
      {options.map(([id, l]) => <MenuItem key={id} value={id}>{l}</MenuItem>)}
      {p.value && !options.some(([id]) => id === p.value) && <MenuItem value={p.value}>{p.value}</MenuItem>}
    </TextField>
  );
}

function FilePick({ ctx, value, onChange, label, required, accept }: { ctx: Ctx; value: string; onChange: (v: string | null) => void; label: string; required: boolean; accept?: string[] | null }) {
  const files = useQuery({ queryKey: ['files', ctx.recordType, ctx.recordId, false], enabled: !!ctx.recordId, queryFn: () => get<{ id: string; fileName: string }[]>(`/api/records/${ctx.recordType}/${ctx.recordId}/files?history=false`) });
  let picked: string[] = [];
  try { picked = value ? (value.startsWith('[') ? JSON.parse(value) : [value]) : []; } catch { picked = []; }
  const ok = (n: string) => !accept?.length || accept.some((a) => n.toLowerCase().endsWith(a));
  return (
    <TextField size="small" fullWidth select label={label} required={required} disabled={ctx.readOnly} value={picked}
      slotProps={{ select: { multiple: true, renderValue: (s) => (s as string[]).map((id) => files.data?.find((f) => f.id === id)?.fileName ?? id).join(', ') } }}
      helperText={files.data?.length ? 'Files attached to this record (Files & history tab)' : 'Attach files on the Files & history tab first'}
      onChange={(e) => { const v = e.target.value as unknown as string[]; onChange(v.length ? JSON.stringify(v) : null); }}>
      {(files.data ?? []).filter((f) => ok(f.fileName)).map((f) => <MenuItem key={f.id} value={f.id}>{f.fileName}</MenuItem>)}
    </TextField>
  );
}

/** One input for any field type. Calculated values come from the server and are read-only. */
export function FieldInput({ f, value, onChange, ctx, dense, label: labelOverride }: { f: ColumnDef; value: string | null | undefined; onChange: (v: string | null) => void; ctx: Ctx; dense?: boolean; label?: string }) {
  const v = value ?? '';
  const label = dense ? '' : (labelOverride ?? `${f.label}${f.unit ? ` (${f.unit})` : ''}`);
  const common = { size: 'small' as const, fullWidth: true, label, required: !!f.required, disabled: ctx.readOnly, placeholder: f.placeholder ?? undefined, helperText: dense ? undefined : f.help ?? undefined, slotProps: { htmlInput: { 'aria-label': f.label } } };
  const set = (x: string) => onChange(x === '' ? null : x);
  switch (f.type) {
    case 'instruction': return <Typography variant="body2" sx={{ p: 1.5, bgcolor: 'action.hover', borderLeft: 3, borderColor: 'primary.main', whiteSpace: 'pre-wrap' }}>{f.help ?? f.label}</Typography>;
    case 'longtext': return <TextField {...common} multiline minRows={dense ? 1 : 3} value={v} onChange={(e) => set(e.target.value)} />;
    case 'number': case 'integer': case 'currency': case 'percent':
      return <TextField {...common} type="number" value={v} onChange={(e) => set(e.target.value)} slotProps={{ htmlInput: { min: f.min ?? undefined, max: f.max ?? undefined, step: f.type === 'integer' ? 1 : 'any', 'aria-label': f.label } }} />;
    case 'measurement': {
      const n = Number(v); const out = v !== '' && !isNaN(n) && ((f.min != null && n < f.min) || (f.max != null && n > f.max));
      return <TextField {...common} type="number" value={v} error={out} onChange={(e) => set(e.target.value)}
        helperText={dense ? undefined : `${f.nominal != null ? `Nominal ${f.nominal}` : ''}${f.min != null || f.max != null ? `  Limits ${f.min ?? '—'} to ${f.max ?? '—'}` : ''}${out ? '  Out of tolerance' : ''}`}
        slotProps={{ htmlInput: { step: 'any', 'aria-label': f.label } }} />;
    }
    case 'date': return <TextField {...common} type="date" value={v} onChange={(e) => set(e.target.value)} slotProps={{ inputLabel: { shrink: true }, htmlInput: { 'aria-label': f.label } }} />;
    case 'time': return <TextField {...common} type="time" value={v} onChange={(e) => set(e.target.value)} slotProps={{ inputLabel: { shrink: true }, htmlInput: { 'aria-label': f.label } }} />;
    case 'datetime': return <TextField {...common} type="datetime-local" value={v ? v.replace('Z', '').slice(0, 16) : ''} onChange={(e) => set(e.target.value ? new Date(e.target.value).toISOString() : '')} slotProps={{ inputLabel: { shrink: true }, htmlInput: { 'aria-label': f.label } }} />;
    case 'boolean': return <FormControlLabel disabled={ctx.readOnly} control={<Checkbox checked={v === 'true'} onChange={(e) => onChange(e.target.checked ? 'true' : 'false')} slotProps={{ input: { 'aria-label': f.label } }} />} label={label} />;
    case 'passfail':
      return (
        <Stack gap={0.5}>
          {!dense && <Typography variant="caption" color="text.secondary">{label}{f.required ? ' *' : ''}</Typography>}
          <ToggleButtonGroup size="small" exclusive value={v} disabled={ctx.readOnly} aria-label={f.label} onChange={(_, x) => onChange(x ?? null)}>
            <ToggleButton value="pass" color="success">Pass</ToggleButton><ToggleButton value="fail" color="error">Fail</ToggleButton><ToggleButton value="na">N/A</ToggleButton>
          </ToggleButtonGroup>
        </Stack>
      );
    case 'radio':
      return (
        <Stack gap={0.25}>
          {!dense && <Typography variant="caption" color="text.secondary">{label}{f.required ? ' *' : ''}</Typography>}
          <RadioGroup row value={v} onChange={(e) => set(e.target.value)} aria-label={f.label}>
            {(f.options ?? []).map((o) => <FormControlLabel key={o} value={o} disabled={ctx.readOnly} control={<Radio size="small" />} label={o} />)}
          </RadioGroup>
        </Stack>
      );
    case 'choice': return <TextField {...common} select value={v} onChange={(e) => set(e.target.value)}><MenuItem value="">—</MenuItem>{(f.options ?? []).map((o) => <MenuItem key={o} value={o}>{o}</MenuItem>)}</TextField>;
    case 'multichoice': {
      let picked: string[] = []; try { picked = v ? JSON.parse(v) : []; } catch { picked = []; }
      return <TextField {...common} select value={picked} slotProps={{ select: { multiple: true, renderValue: (s) => (s as string[]).join(', ') } }} onChange={(e) => { const x = e.target.value as unknown as string[]; onChange(x.length ? JSON.stringify(x) : null); }}>{(f.options ?? []).map((o) => <MenuItem key={o} value={o}>{o}</MenuItem>)}</TextField>;
    }
    case 'calc': return <TextField {...common} disabled value={v} helperText={dense ? undefined : 'Calculated when saved'} slotProps={{ input: { sx: { fontFamily: mono, bgcolor: 'action.hover' } } }} />;
    case 'attachment': case 'photo': return <FilePick ctx={ctx} value={v} onChange={onChange} label={label} required={!!f.required} accept={f.type === 'photo' ? ['.jpg', '.jpeg', '.png', '.heic', '.webp'] : f.accept} />;
    case 'user': case 'site': case 'department': case 'part': case 'product': case 'supplier': case 'customer': case 'equipment': case 'record':
      return <RefSelect kind={f.type} value={v} onChange={set} label={label} disabled={ctx.readOnly} required={!!f.required} siteId={ctx.siteId} />;
    case 'email': return <TextField {...common} type="email" value={v} onChange={(e) => set(e.target.value)} />;
    case 'url': return <TextField {...common} type="url" value={v} onChange={(e) => set(e.target.value)} />;
    default: return <TextField {...common} value={v} onChange={(e) => set(e.target.value)} />;
  }
}

function visibleFieldKeys(fields: ColumnDef[], values: Values): Set<string> {
  let shown = new Set(fields.map((f) => f.key));
  for (let i = 0; i < fields.length; i++) {
    const next = new Set([...shown].filter((k) => { const f = fields.find((x) => x.key === k)!; return !f.visibleWhen || (shown.has(f.visibleWhen.field) && matches(f.visibleWhen, values)); }));
    if (next.size === shown.size) break; shown = next;
  }
  return shown;
}

function FieldGrid({ fields, values, set, columns, ctx, boxed }: { fields: ColumnDef[]; values: Values; set: (k: string, v: string | null) => void; columns: number; ctx: Ctx; boxed?: boolean }) {
  const shown = visibleFieldKeys(fields, values);
  // three or more columns only on wide screens (1536px and up); narrower screens get two so long labels are not cut off
  const narrow = Math.min(columns, 2);
  // a title block draws every field in a ruled cell, like the header of an engineering drawing
  return (
    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: `repeat(${narrow * 6}, minmax(0, 1fr))`, xl: `repeat(${columns * 6}, minmax(0, 1fr))` }, gap: boxed ? 0 : 2, ...(boxed ? { borderTop: 1, borderLeft: 1, borderColor: 'divider' } : {}) }}>
      {fields.filter((f) => shown.has(f.key)).map((f) => {
        const span = (cols: number) => f.type === 'longtext' || f.type === 'instruction' ? cols * 6 : Math.min(cols * 6, f.width ? Math.max(1, Math.round((f.width / 12) * cols * 6)) : 6);
        return <Box key={f.key} sx={{ gridColumn: { xs: '1 / -1', sm: `span ${span(narrow)}`, xl: `span ${span(columns)}` }, minWidth: 0,
 ...(boxed ? { borderRight: 1, borderBottom: 1, borderColor: 'divider', p: 1, '& .MuiOutlinedInput-notchedOutline': { borderColor: 'transparent' }, '&:focus-within': { bgcolor: 'action.hover' } } : {}) }}><FieldInput f={f} value={values[f.key]} onChange={(v) => set(f.key, v)} ctx={ctx} /></Box>;
      })}
    </Box>
  );
}

function NestedTable({ col, value, onChange, ctx }: { col: ColumnDef; value: string | null | undefined; onChange: (v: string | null) => void; ctx: Ctx }) {
  const [open, setOpen] = useState(false);
  const rows = readRows(value);
  if (!open) return <Button size="small" startIcon={<TableRowsOutlined />} onClick={() => setOpen(true)}>{rows.length} {rows.length === 1 ? 'row' : 'rows'}</Button>;
  const t: TableDef = { key: col.key, title: col.label, columns: col.columns ?? [], minRows: 0, maxRows: 200 };
  return (
    <Box sx={{ minWidth: 360 }}>
      <GridTable t={t} value={value} onChange={onChange} ctx={ctx} compact />
      <Button size="small" onClick={() => setOpen(false)}>Close</Button>
    </Box>
  );
}

/** Header group spans widened by any running-total columns that sit under the group. */
function groupSpans(t: TableDef) {
  const cols = [...(t.fixedRows ? ['__label'] : []), ...t.columns.map((c) => c.key)];
  let at = 0;
  return (t.headerGroups ?? []).map((g) => {
    const covered = cols.slice(at, at + g.span); at += g.span;
    return { label: g.label, span: g.span + covered.filter((k) => t.runningTotals?.includes(k)).length };
  });
}

const headCellBase = { fontSize: 12, py: 0.75, whiteSpace: 'nowrap' } as const;
/** Table header by form family: tinted heads in the family's edge colour, solid edge-colour heads with an accent rule on worksheets and certificates. */
const headFor = (t: Theme, theme: FormTheme) => {
  const { edge, accent } = t.qg.families[theme] ?? t.qg.families.report;
  if (theme === 'worksheet' || theme === 'certificate')
    return { bgcolor: edge, group: alpha(edge, 0.85), color: t.palette.getContrastText(edge), line: '1px solid rgba(255,255,255,0.22)', under: `2px solid ${accent}` };
  return { bgcolor: alpha(edge, 0.08), group: alpha(edge, 0.15), color: edge, line: `1px solid ${alpha(edge, 0.14)}`, under: `2px solid ${alpha(edge, 0.25)}` };
};

/** Table block: grouped headers, fixed or dynamic rows, nested tables, running totals, totals and subtotals. */
export function GridTable({ t, value, onChange, ctx, compact }: { t: TableDef; value: string | null | undefined; onChange: (v: string | null) => void; ctx: Ctx; compact?: boolean }) {
  const tv = readTable(value);
  const fixed = t.fixedRows ?? null;
  const rows: Row[] = fixed ? fixed.map((_, i) => tv.rows[i] ?? {}) : tv.rows;
  const write = (next: Row[]) => onChange(next.length || fixed ? writeRows(next) : null);
  const setCell = (i: number, k: string, v: string | null) => write(rows.map((r, j) => (j === i ? { ...r, [k]: v } : r)));
  const running = t.runningTotals ?? [];
  const showTotals = (t.totals?.length ?? 0) > 0;
  const th = useTheme();
  const hf = headFor(th, ctx.theme ?? 'report');
  const headCell = { ...headCellBase, bgcolor: hf.bgcolor, color: hf.color, borderRight: hf.line, fontWeight: 700, borderBottom: hf.under };
  const square = ctx.theme === 'certificate' || ctx.theme === 'register';
  return (
    <Box sx={{ overflowX: 'auto', border: 1, borderColor: 'divider', borderRadius: square ? '4px' : `${th.qg.form.radius * 0.6}px`, bgcolor: th.qg.glass.table }}>
      <Table size="small" aria-label={t.title} sx={{ '& td': { py: 0.5, px: 0.75, verticalAlign: 'top', borderRight: 1, borderColor: 'divider' } }}>
        <TableHead>
          {t.headerGroups && (
            <TableRow>
              {!fixed && !compact && <TableCell sx={{ ...headCell, bgcolor: hf.group }} />}
              {groupSpans(t).map((g, i) => <TableCell key={i} colSpan={g.span} align="center" sx={{ ...headCell, bgcolor: hf.group }}>{g.label}</TableCell>)}
              {!ctx.readOnly && !fixed && <TableCell sx={{ ...headCell, bgcolor: hf.group }} />}
            </TableRow>
          )}
          <TableRow>
            {fixed && <TableCell sx={headCell}>Item</TableCell>}
            {!fixed && !compact && <TableCell sx={{ ...headCell, width: 36 }}>#</TableCell>}
            {t.columns.map((c) => (
              <Fragment key={c.key}>
                <TableCell sx={headCell} align={numeric.has(c.type) ? 'right' : 'left'}>{c.label}{c.unit ? ` (${c.unit})` : ''}{c.required ? ' *' : ''}</TableCell>
                {running.includes(c.key) && <TableCell sx={headCell} align="right">Running {c.label.toLowerCase()}</TableCell>}
              </Fragment>
            ))}
            {!ctx.readOnly && !fixed && <TableCell sx={{ ...headCell, width: 40 }} />}
          </TableRow>
        </TableHead>
        <TableBody>
          {rows.map((r, i) => (
            <TableRow key={i} hover>
              {fixed && <TableCell sx={{ fontWeight: 500, bgcolor: 'action.hover', minWidth: 140 }}>{fixed[i]}</TableCell>}
              {!fixed && !compact && <TableCell sx={{ color: 'text.secondary', fontFamily: mono, fontSize: 12 }}>{i + 1}</TableCell>}
              {t.columns.map((c) => (
                <Fragment key={c.key}>
                  <TableCell sx={{ minWidth: c.type === 'longtext' ? 220 : c.type === 'passfail' ? 170 : 110 }} align={numeric.has(c.type) ? 'right' : 'left'}>
                    {c.type === 'table' ? <NestedTable col={c} value={r[c.key]} onChange={(v) => setCell(i, c.key, v)} ctx={ctx} />
                      : c.type === 'calc' || ctx.readOnly ? <Typography variant="body2" sx={{ fontFamily: numeric.has(c.type) ? mono : undefined, color: r[c.key] === 'FAIL' ? 'error.main' : undefined }}>{displayValue(c, r[c.key])}</Typography>
                      : <FieldInput f={c} value={r[c.key]} onChange={(v) => setCell(i, c.key, v)} ctx={ctx} dense />}
                  </TableCell>
                  {running.includes(c.key) && <TableCell align="right" sx={{ fontFamily: mono, color: 'text.secondary' }}>{r[`${c.key}_running`] ?? ''}</TableCell>}
                </Fragment>
              ))}
              {!ctx.readOnly && !fixed && <TableCell><Tooltip title="Remove row"><IconButton size="small" aria-label={`Remove row ${i + 1}`} onClick={() => write(rows.filter((_, j) => j !== i))}><DeleteOutline fontSize="small" /></IconButton></Tooltip></TableCell>}
            </TableRow>
          ))}
          {rows.length === 0 && <TableRow><TableCell colSpan={t.columns.length + 3} sx={{ color: 'text.secondary', fontStyle: 'italic' }}>No rows yet.</TableCell></TableRow>}
          {(tv.subtotals ?? []).map((s, i) => (
            <TableRow key={`sub-${i}`} sx={{ bgcolor: 'action.hover' }}>
              {(fixed || !compact) && <TableCell sx={{ fontWeight: 600 }}>Subtotal</TableCell>}
              {t.columns.map((c) => {
                const tot = t.totals?.find((x) => x.column === c.key);
                return <Fragment key={c.key}><TableCell align="right" sx={{ fontFamily: mono, fontWeight: 600 }}>{c.key === t.subtotalBy ? s.group : tot ? s[`${tot.function}_${c.key}`] ?? '' : ''}</TableCell>{running.includes(c.key) && <TableCell />}</Fragment>;
              })}
              {!ctx.readOnly && !fixed && <TableCell />}
            </TableRow>
          ))}
          {showTotals && (
            <TableRow sx={{ '& td': { borderTop: 2, borderTopColor: 'primary.main' } }}>
              {(fixed || !compact) && <TableCell sx={{ fontWeight: 700 }}>Totals</TableCell>}
              {t.columns.map((c) => {
                const tots = (t.totals ?? []).filter((x) => x.column === c.key);
                return <Fragment key={c.key}><TableCell align="right">{tots.map((x) => <Box key={x.function} sx={{ fontFamily: mono, fontWeight: 700, fontSize: 13 }}><Box component="span" sx={{ color: 'text.secondary', fontWeight: 400, fontFamily: 'inherit', mr: 0.5 }}>{totalLabel(x)}</Box>{tv.totals?.[`${x.function}_${c.key}`] ?? '—'}</Box>)}</TableCell>{running.includes(c.key) && <TableCell />}</Fragment>;
              })}
              {!ctx.readOnly && !fixed && <TableCell />}
            </TableRow>
          )}
        </TableBody>
      </Table>
      {!ctx.readOnly && !fixed && rows.length < t.maxRows && (
        <Box sx={{ p: 0.75, borderTop: 1, borderColor: 'divider' }}><Button size="small" startIcon={<Add />} onClick={() => write([...rows, {}])}>Add row</Button></Box>
      )}
    </Box>
  );
}

function SignatureLine({ s, ctx }: { s: FormDef['signatures'][number]; ctx: Ctx }) {
  const mine = (ctx.signatures ?? []).filter((x) => x.key === s.key);
  const st = mine.find((x) => x.valid !== false);
  const lapsed = !st ? mine.filter((x) => x.valid === false).at(-1) : undefined;
  const signed = !!st?.signedAt;
  return (
    <Paper variant="outlined" sx={{ p: 1.5, display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '160px 1fr auto' }, gap: 1.5, alignItems: 'center', borderLeft: 3, borderLeftColor: signed ? 'success.main' : lapsed ? 'warning.main' : 'divider' }} aria-label={`Signature ${s.label}`}>
      <Box>
        <Typography variant="body2" fontWeight={600}>{s.label}{s.required ? ' *' : ''}</Typography>
        <Typography variant="caption" color="text.secondary">{roleLabel[s.role] ?? s.role}</Typography>
      </Box>
      <Box sx={{ minWidth: 0 }}>
        {signed
          ? <><Typography variant="body2" sx={{ fontFamily: '"Segoe Script", "Brush Script MT", cursive', fontSize: 18, color: 'primary.dark' }}>{st!.signerName}</Typography>
              <Typography variant="caption" color="text.secondary">{new Date(st!.signedAt!).toLocaleString()} · {st!.meaning ?? s.meaning}</Typography></>
          : <><Box sx={{ borderBottom: 1, borderColor: 'text.disabled', height: 22, maxWidth: 360 }} /><Typography variant="caption" color="text.secondary">{s.meaning}</Typography>
              {lapsed && <Typography variant="caption" color="warning.dark" sx={{ display: 'block' }}>Signed by {lapsed.signerName} on {new Date(lapsed.signedAt!).toLocaleString()}; the content changed afterwards, so it must be signed again.</Typography>}</>}
      </Box>
      {!signed && ctx.onSign && ctx.canSign?.(s.key) && <Button size="small" variant="outlined" onClick={() => ctx.onSign!(s.key)}>Sign</Button>}
      {signed && <Chip size="small" color="success" label="Signed" />}
      {!signed && lapsed && !(ctx.onSign && ctx.canSign?.(s.key)) && <Chip size="small" color="warning" label="Invalidated" />}
    </Paper>
  );
}

/** Certificate, worksheet and checklist forms sign in a row of boxes, like the authorization panel of a printed warrant. */
function SignatureBox({ s, ctx }: { s: FormDef['signatures'][number]; ctx: Ctx }) {
  const qg = useTheme().qg;
  const edge = (qg.families[ctx.theme ?? 'report'] ?? qg.families.report).edge;
  const mine = (ctx.signatures ?? []).filter((x) => x.key === s.key);
  const st = mine.find((x) => x.valid !== false);
  const lapsed = !st ? mine.filter((x) => x.valid === false).at(-1) : undefined;
  const signed = !!st?.signedAt;
  return (
    <Box aria-label={`Signature ${s.label}`} sx={{ border: 1, borderColor: signed ? 'success.main' : 'text.disabled', borderStyle: signed ? 'solid' : 'dashed', p: 1.5, display: 'flex', flexDirection: 'column', gap: 0.75, minHeight: 132,
      ...(ctx.theme === 'certificate' ? { borderStyle: 'solid', borderColor: signed ? 'success.main' : 'divider', borderRadius: '4px', bgcolor: 'background.paper', borderBottom: 3, borderBottomColor: edge } : {}) }}>
      <Typography variant="overline" sx={{ lineHeight: 1.4, color: 'text.secondary' }}>{roleLabel[s.role] ?? s.role}</Typography>
      <Typography variant="body2" fontWeight={700}>{s.label}{s.required ? ' *' : ''}</Typography>
      <Box sx={{ flex: 1, display: 'flex', alignItems: 'flex-end', borderBottom: 1, borderColor: 'text.disabled', minHeight: 30 }}>
        {signed && <Typography sx={{ fontFamily: '"Segoe Script", "Brush Script MT", cursive', fontSize: 18, color: 'primary.dark' }}>{st!.signerName}</Typography>}
      </Box>
      <Typography variant="caption" color="text.secondary">{signed ? `${new Date(st!.signedAt!).toLocaleString()} · ${st!.meaning ?? s.meaning}` : s.meaning}</Typography>
      {lapsed && <Typography variant="caption" color="warning.dark">Signed by {lapsed.signerName}; changed afterwards, so it must be signed again.</Typography>}
      <Stack direction="row" gap={1}>
        {!signed && ctx.onSign && ctx.canSign?.(s.key) && <Button size="small" variant="outlined" onClick={() => ctx.onSign!(s.key)}>Sign</Button>}
        {signed && <Chip size="small" color="success" label="Signed" />}
        {!signed && lapsed && !(ctx.onSign && ctx.canSign?.(s.key)) && <Chip size="small" color="warning" label="Invalidated" />}
      </Stack>
    </Box>
  );
}

function RepeatSection({ s, fields, value, onChange, ctx }: { s: SectionDef; fields: ColumnDef[]; value: string | null | undefined; onChange: (v: string | null) => void; ctx: Ctx }) {
  const items = readRows(value);
  const max = s.maxRepeat ?? 200;
  const label = s.repeatLabel ?? 'Entry';
  const write = (next: Row[]) => onChange(next.length ? writeRows(next) : null);
  return (
    <Stack gap={1.5}>
      {items.map((it, i) => (
        <Paper key={i} variant="outlined" sx={{ p: 2 }}>
          <Stack direction="row" alignItems="center" sx={{ mb: 1.5 }}>
            <Typography variant="subtitle2" sx={{ flex: 1 }}>{label} {i + 1}</Typography>
            {!ctx.readOnly && <Tooltip title={`Remove ${label.toLowerCase()} ${i + 1}`}><IconButton size="small" aria-label={`Remove ${label} ${i + 1}`} onClick={() => write(items.filter((_, j) => j !== i))}><DeleteOutline fontSize="small" /></IconButton></Tooltip>}
          </Stack>
          <FieldGrid fields={fields} values={it} set={(k, v) => write(items.map((x, j) => (j === i ? { ...x, [k]: v } : x)))} columns={s.columns} ctx={ctx} />
        </Paper>
      ))}
      {items.length === 0 && <Typography variant="body2" color="text.secondary" sx={{ fontStyle: 'italic' }}>No {label.toLowerCase()} added{s.minRepeat ? ` (at least ${s.minRepeat} needed)` : ''}.</Typography>}
      {!ctx.readOnly && items.length < max && <Box><Button size="small" startIcon={<Add />} onClick={() => write([...items, {}])}>Add {label.toLowerCase()}</Button></Box>}
    </Stack>
  );
}

/** The section style each look uses when a section does not name its own. */
const sectionDefault: Record<FormTheme, SectionStyle> = { report: 'standard', worksheet: 'panel', checklist: 'checklist', certificate: 'titleblock', investigation: 'steps', register: 'ledger', plan: 'steps' };

/** Renders a full form definition for data entry or review: numbered sections, field grids, tables, repeated sections and signature lines. */
export function FormView({ def, values, onChange, readOnly, recordType, recordId, siteId, signatures, onSign, canSign }: {
  def: FormDef; values: Values; onChange: (v: Values) => void; readOnly: boolean; recordType?: string; recordId?: string; siteId?: string | null;
  signatures?: SignatureState[]; onSign?: (key: string) => void; canSign?: (key: string) => boolean;
}) {
  const theme: FormTheme = def.page?.theme ?? 'report';
  const ctx: Ctx = { theme, readOnly, recordType, recordId, siteId, signatures, onSign, canSign };
  const set = (k: string, v: string | null) => onChange({ ...values, [k]: v });
  const hidden = hiddenSections(def, values);
  const t = useTheme();
  const qg = t.qg;
  const R = `${qg.form.radius}px`;
  // the family's palette (Settings › Appearance): edge for bars, badges and rules, accent for numbers and callouts
  const { edge, accent } = qg.families[theme] ?? qg.families.report;
  const onEdge = t.palette.getContrastText(edge);
  const darkMode = t.palette.mode === 'dark';
  const bandBg = edge;
  const soft = { bgcolor: qg.glass.section, boxShadow: darkMode ? 'none' : '0 4px 18px rgba(32,56,120,0.06)' };
  const roots = useMemo(() => {
    const secs = def.sections.length ? def.sections : [];
    return secs.filter((s) => !s.parent || !secs.some((x) => x.key === s.parent));
  }, [def.sections]);
  const loose = def.fields.filter((f) => !f.section || !def.sections.some((s) => s.key === f.section));
  const looseTables = def.tables.filter((t) => !t.section || !def.sections.some((s) => s.key === t.section));
  const looseSigs = def.signatures.filter((s) => !s.section || !def.sections.some((x) => x.key === s.section));

  const renderSection = (s: SectionDef, number: string, depth: number): React.ReactNode => {
    if (hidden.has(s.key)) return null;
    const fields = def.fields.filter((f) => f.section === s.key);
    const tables = def.tables.filter((t) => t.section === s.key && (!t.visibleWhen || matches(t.visibleWhen, values)));
    const sigs = def.signatures.filter((x) => x.section === s.key).sort((a, b) => a.order - b.order);
    const children = def.sections.filter((c) => c.parent === s.key);
    const style: SectionStyle = s.style ?? (depth > 0 ? 'standard' : sectionDefault[theme]);
    const boxedSigs = theme === 'certificate' || theme === 'worksheet' || theme === 'checklist' || style === 'signoff' && theme !== 'report' && theme !== 'investigation';
    const body = (
      <>
        {s.instructions && <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5, whiteSpace: 'pre-wrap', ...(style === 'ledger' ? { fontSize: 12.5 } : {}) }}>{s.instructions}</Typography>}
        {s.repeatable
          ? <RepeatSection s={s} fields={fields} value={values[s.key]} onChange={(v) => set(s.key, v)} ctx={ctx} />
          : fields.length > 0 && <FieldGrid fields={fields} values={values} set={set} columns={s.columns} ctx={ctx} boxed={style === 'titleblock'} />}
        {tables.map((t) => (
          <Box key={t.key} sx={{ mt: 2 }}>
            <Typography variant="subtitle2" sx={{ mb: 0.5, ...(theme === 'register' ? { textTransform: 'uppercase', letterSpacing: 0.6, fontSize: 12 } : {}) }}>{t.title}</Typography>
            {t.instructions && <Typography variant="caption" color="text.secondary" component="p" sx={{ mb: 0.75 }}>{t.instructions}</Typography>}
            <GridTable t={t} value={values[t.key]} onChange={(v) => set(t.key, v)} ctx={ctx} />
          </Box>
        ))}
        {sigs.length > 0 && (boxedSigs
          ? <Box data-signature-band={theme === 'certificate' || undefined} sx={{ mt: 2, display: 'grid', gridTemplateColumns: { xs: '1fr', sm: `repeat(${Math.min(3, sigs.length)}, 1fr)` }, gap: 1.5,
              ...(theme === 'certificate' && style !== 'signoff' ? { p: 1.5, bgcolor: alpha(edge, darkMode ? 0.1 : 0.06), borderTop: `3px double ${accent}` } : {}) }}>{sigs.map((x) => <SignatureBox key={x.key} s={x} ctx={ctx} />)}</Box>
          : <Stack gap={1} sx={{ mt: 2 }}>{sigs.map((x) => <SignatureLine key={x.key} s={x} ctx={ctx} />)}</Stack>)}
        {children.map((c, i) => <Box key={c.key} sx={{ mt: 2, pl: { sm: 2 }, borderLeft: { sm: 1 }, borderColor: 'divider' }}>{renderSection(c, `${number}${i + 1}.`, depth + 1)}</Box>)}
      </>
    );
    const chips = <>{s.repeatable && <Chip size="small" variant="outlined" label="Repeatable" />}{s.orientation === 'landscape' && <Chip size="small" variant="outlined" label="Prints landscape" />}</>;
    const title = (variant: 'h3' | 'subtitle1', sx?: object) => <Typography id={`sec-${s.key}`} variant={variant} component="h2" sx={{ fontWeight: 600, ...sx }}>{s.title}</Typography>;
    const wrap = (sx: object, children: React.ReactNode) => <Box key={s.key} component="section" aria-labelledby={`sec-${s.key}`} data-section-style={style} sx={{ mb: depth === 0 ? 3 : 2, ...sx }}>{children}</Box>;
    if (depth > 0 || style === 'standard')
      return wrap({}, <>
        <Stack direction="row" alignItems="baseline" gap={1.25} sx={depth === 0 ? { pb: 0.5, mb: 1.5, backgroundImage: `linear-gradient(90deg, ${accent}, ${edge})`, backgroundSize: '100% 2px', backgroundRepeat: 'no-repeat', backgroundPosition: 'bottom' } : { mb: 1 }}>
          <Typography component="span" sx={{ fontFamily: mono, fontWeight: 700, color: accent, fontSize: depth === 0 ? 15 : 13 }}>{number}</Typography>
          {title(depth === 0 ? 'h3' : 'subtitle1')}{chips}
        </Stack>{body}</>);
    switch (style) {
      case 'callout':
        return wrap({ bgcolor: qg.form.callout, borderLeft: 4, borderColor: accent, p: 2, borderRadius: `0 ${R} ${R} 0` }, <>
          <Stack direction="row" alignItems="baseline" gap={1} sx={{ mb: 1 }}><Typography component="span" sx={{ fontFamily: mono, fontWeight: 700, color: 'text.secondary', fontSize: 13 }}>{number}</Typography>{title('subtitle1')}{chips}</Stack>{body}</>);
      case 'titleblock':
        // certificate: a formal ruled title block, an accent-tinted head with spaced capitals over an edge-colour rule; elsewhere a solid edge-colour band
        return theme === 'certificate'
          ? wrap({ border: 1, borderColor: alpha(accent, 0.45), borderRadius: '4px', overflow: 'hidden', bgcolor: darkMode ? 'rgba(30,33,39,0.85)' : '#FFFFFF' }, <>
              <Stack direction="row" alignItems="center" gap={1.25} sx={{ px: 1.75, py: 1, bgcolor: alpha(accent, darkMode ? 0.14 : 0.07), borderBottom: `2px solid ${edge}` }}>
                <Typography component="span" sx={{ fontFamily: mono, fontWeight: 700, fontSize: 13, color: accent }}>{number}</Typography>{title('subtitle1', { textTransform: 'uppercase', letterSpacing: 1.6, fontSize: 13.5, fontWeight: 700, color: accent })}{chips}
              </Stack><Box sx={{ p: 0 }}>{body}</Box></>)
          : wrap({ border: 2, borderColor: bandBg, borderRadius: R, overflow: 'hidden', ...soft }, <>
              <Stack direction="row" alignItems="center" gap={1.25} sx={{ px: 1.5, py: 1, background: bandBg, color: onEdge, borderBottom: 2, borderColor: bandBg }}>
                <Typography component="span" sx={{ fontFamily: mono, fontWeight: 700, fontSize: 13, opacity: 0.85 }}>{number}</Typography>{title('subtitle1', { textTransform: 'uppercase', letterSpacing: 1, fontSize: 14 })}{chips}
              </Stack><Box sx={{ p: 0 }}>{body}</Box></>);
      case 'panel':
        return wrap({ border: 1, borderColor: 'divider', borderRadius: R, overflow: 'hidden', ...soft }, <>
          <Stack direction="row" alignItems="center" gap={1.25} sx={{ px: 2, py: 1, background: theme === 'worksheet' ? bandBg : 'action.hover', color: theme === 'worksheet' ? onEdge : 'text.primary', ...(theme === 'worksheet' ? { borderBottom: `3px solid ${edge}` } : {}) }}>
            <Typography component="span" sx={{ fontFamily: mono, fontWeight: 700, fontSize: 13 }}>{number}</Typography>{title('subtitle1', { textTransform: theme === 'worksheet' ? 'uppercase' : 'none', letterSpacing: theme === 'worksheet' ? 0.8 : 0 })}{chips}
          </Stack><Box sx={{ p: 2 }}>{body}</Box></>);
      case 'steps':
        return wrap({ display: 'grid', gridTemplateColumns: '44px 1fr', columnGap: 2 }, <>
          <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
            <Box sx={{ width: 36, height: 36, borderRadius: '50%', background: theme === 'plan' ? edge : `linear-gradient(135deg, ${accent} 0%, ${edge} 100%)`, boxShadow: `0 6px 14px ${alpha(edge, 0.3)}`, color: '#fff', display: 'grid', placeItems: 'center', fontFamily: mono, fontWeight: 700, fontSize: 14 }}>{number.replace(/\.$/, '')}</Box>
            <Box sx={{ flex: 1, width: 2, bgcolor: 'divider', mt: 0.5 }} />
          </Box>
          <Box sx={{ minWidth: 0, pb: 1 }}><Stack direction="row" alignItems="center" gap={1} sx={{ minHeight: 36, mb: 1 }}>{title('h3')}{chips}</Stack>{body}</Box></>);
      case 'checklist':
        return wrap({ border: 1, borderColor: 'divider', borderLeft: 6, borderLeftColor: edge, borderRadius: R, p: 2, ...soft }, <>
          <Stack direction="row" alignItems="center" gap={1.25} sx={{ mb: 1.5 }}>
            <Chip size="small" label={number.replace(/\.$/, '')} sx={{ fontFamily: mono, fontWeight: 700, bgcolor: edge, color: onEdge }} />{title('subtitle1')}{chips}
          </Stack>{body}</>);
      case 'ledger':
        return wrap({ borderTop: 1, borderColor: 'divider', pt: 1.5, color: 'text.secondary' }, <>
          <Stack direction="row" gap={1} alignItems="baseline" sx={{ mb: 1 }}><Typography variant="overline" component="span" sx={{ fontFamily: mono }}>{number}</Typography><Typography variant="overline" id={`sec-${s.key}`} component="h2">{s.title}</Typography>{chips}</Stack>{body}</>);
      case 'signoff':
        // certificate: the signature band, an edge-tinted strip between double accent rules with the title centred
        return theme === 'certificate'
          ? wrap({ borderTop: `3px double ${accent}`, borderBottom: `3px double ${accent}`, bgcolor: alpha(edge, darkMode ? 0.1 : 0.06), px: 2, py: 2 }, <>
              <Stack direction="row" alignItems="baseline" gap={1.25} sx={{ mb: 1.5, justifyContent: 'center' }}>
                {title('h3', { textTransform: 'uppercase', letterSpacing: 2, color: accent })}
              </Stack>{body}</>)
          : wrap({ border: 1, borderColor: 'divider', p: 2, borderRadius: R, ...soft }, <>
              <Stack direction="row" alignItems="baseline" gap={1.25} sx={{ mb: 1.5 }}>
                <Typography component="span" sx={{ fontFamily: mono, fontWeight: 700, color: accent, fontSize: 15 }}>{number}</Typography>
                {title('h3')}
              </Stack>{body}</>);
      default: return wrap({}, body);
    }
  };

  // certificates sit in a bordered frame: accent outer rule, fine edge-colour inner rule and an accent-to-edge strip across the top
  const frame = theme === 'certificate' ? {
    position: 'relative', border: `2px solid ${alpha(accent, 0.55)}`, borderRadius: '6px', p: { xs: 2, sm: 3 }, pt: { xs: 3, sm: 4 },
    boxShadow: `inset 0 0 0 5px ${darkMode ? '#202328' : '#FFFFFF'}, inset 0 0 0 6px ${alpha(edge, 0.55)}`, bgcolor: darkMode ? 'rgba(30,33,39,0.6)' : 'rgba(255,255,255,0.85)',
    '&::before': { content: '""', position: 'absolute', left: 0, right: 0, top: 0, height: 6, background: `linear-gradient(90deg, ${accent}, ${edge})`, borderRadius: '4px 4px 0 0' },
  } : {};
  return (
    <Box data-form-theme={theme} sx={frame}>
      {loose.length > 0 && <Box sx={{ mb: 3 }}><FieldGrid fields={loose} values={values} set={set} columns={2} ctx={ctx} /></Box>}
      {looseTables.map((t) => <Box key={t.key} sx={{ mb: 3 }}><Typography variant="subtitle2" sx={{ mb: 0.5 }}>{t.title}</Typography><GridTable t={t} value={values[t.key]} onChange={(v) => set(t.key, v)} ctx={ctx} /></Box>)}
      {roots.map((s, i) => renderSection(s, `${i + 1}.`, 0))}
      {looseSigs.length > 0 && <Box sx={{ mt: 2 }}><Typography variant="h3" component="h2" sx={{ mb: 1, borderBottom: 2, borderColor: 'primary.main', pb: 0.5 }}>Signatures</Typography><Stack gap={1}>{looseSigs.sort((a, b) => a.order - b.order).map((x) => <SignatureLine key={x.key} s={x} ctx={ctx} />)}</Stack></Box>}
    </Box>
  );
}
