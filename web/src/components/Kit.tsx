import { useState, type ReactNode } from 'react';
import { Alert, Box, Button, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Paper, Stack, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useForm, Controller, type FieldValues, type Path, type DefaultValues } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import type { z } from 'zod';
import { get } from '../api';
import { SitePicker } from './SitePicker';

export interface Site { id: string; code: string; name: string; isArchived: boolean; departments: { id: string; code: string; name: string }[] }
export const useSites = () => useQuery({ queryKey: ['sites'], queryFn: () => get<Site[]>('/api/organization/sites') });
/* Record type display labels: identifiers stay unchanged; the server supplies labels (configurable) and this fallback mirrors its rules. */
const typeDefaults: Record<string, string> = { ChangeRequest: 'Change Request', Fai: 'First Article Inspection', AqlInspection: 'AQL Inspection', SupplierCorrectiveAction: 'Supplier Corrective Action', ManagementReview: 'Management Review', Ncr: 'Nonconformance Report', Capa: 'Corrective and Preventive Action', FormRecord: 'Form Record' };
const acronyms = new Set(['aql', 'fai', 'ncr', 'capa', 'ppap', 'apqp', 'fmea', 'pfmea', 'dfmea', 'spc', 'msa', 'lpa', 'scar', 'rma', 'mrb', 'ecr', 'ecn', 'eco', 'bol', 'po', 'erp', 'mes', 'coq', 'sop', 'kpi', 'dcr', 'id', 'qr', '8d']);
export const humanizeType = (t?: string | null) => {
  if (!t) return 'Record';
  if (typeDefaults[t]) return typeDefaults[t];
  const words = t.match(/[A-Z]{2,}(?=[A-Z][a-z]|\d|$)|[A-Z]?[a-z]+|[A-Z]+|\d+[A-Za-z]?/g) ?? [t];
  return words.map((w) => (acronyms.has(w.toLowerCase()) ? w.toUpperCase() : w[0].toUpperCase() + w.slice(1))).join(' ');
};
export function useTypeLabel() {
  const q = useQuery({ queryKey: ['record-types'], staleTime: 3_600_000, queryFn: () => get<{ type: string; label: string }[]>('/api/record-types') });
  const map = new Map((q.data ?? []).map((x) => [x.type.toLowerCase(), x.label]));
  return (t?: string | null) => (t && map.get(t.toLowerCase())) || humanizeType(t);
}
export const useUsers = () => useQuery({ queryKey: ['userlookup'], queryFn: () => get<{ id: string; displayName: string }[]>('/api/users/lookup') });

/** Label/value list used in record side panels: a ruled two-column sheet with labels on the left. */
export function Facts({ items, children }: { items: [string, ReactNode][]; children?: ReactNode }) {
  return (
    <Paper sx={{ alignSelf: 'start', overflow: 'hidden' }}>
      <PanelHead title="Details" />
      <Box component="dl" sx={{ m: 0, display: 'grid', gridTemplateColumns: 'minmax(96px, 38%) 1fr' }}>
        {items.map(([k, v]) => (
          <Box key={k} sx={{ display: 'contents', '& > *': { px: 2, py: 0.9, borderTop: 1, borderColor: 'divider' } }}>
            <Typography component="dt" variant="caption" color="text.secondary" sx={{ fontWeight: 600, bgcolor: 'background.band' }}>{k}</Typography>
            <Typography component="dd" variant="body2" sx={{ m: 0, whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>{v ?? '—'}</Typography>
          </Box>
        ))}
      </Box>
      {children && <Box sx={{ px: 2, pb: 2 }}>{children}</Box>}
    </Paper>
  );
}

/** Panel header band: small uppercase label on a tinted strip with a rule underneath, optional action on the right. */
export function PanelHead({ title, action }: { title: ReactNode; action?: ReactNode }) {
  return (
    <Stack direction="row" alignItems="center" gap={1} sx={{ px: 2, minHeight: 38, bgcolor: 'background.band', borderBottom: 1, borderColor: 'divider' }}>
      <Typography variant="overline" component="h3" color="text.secondary" sx={{ flex: 1 }}>{title}</Typography>
      {action}
    </Stack>
  );
}

export function Section({ title, children, action }: { title: string; children: ReactNode; action?: ReactNode }) {
  return (
    <Paper sx={{ overflow: 'hidden' }}>
      <PanelHead title={title} action={action} />
      <Box sx={{ p: 2 }}>{children}</Box>
    </Paper>
  );
}

export function Prose({ label, text }: { label: string; text?: string | null }) {
  if (!text) return null;
  return <Box sx={{ mb: 1.5 }}><Typography variant="caption" color="text.secondary">{label}</Typography><Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>{text}</Typography></Box>;
}

/** Runs a mutation and keeps its error message for display. */
export function useRunner(after: () => void) {
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const run = async (fn: () => Promise<unknown>) => {
    setError(null); setBusy(true);
    try { await fn(); after(); } catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  };
  return { error, busy, run, setError };
}

export interface FieldSpec<T> { name: Path<T>; label: string; type?: 'text' | 'multiline' | 'number' | 'date' | 'select' | 'site' | 'department' | 'user' | 'lookup' | 'heading'; options?: [string | number, string][]; full?: boolean; helper?: string; lookup?: LookupKind }

/** Master-record pickers. Lists come from the API and are filtered by the user's permissions there. */
export type LookupKind = 'product' | 'part' | 'supplier' | 'customer';
const lookupUrl: Record<LookupKind, string> = { product: '/api/master/products', part: '/api/master/parts', supplier: '/api/suppliers', customer: '/api/master/customers' };
type LookupRow = { id: string; number?: string; partNumber?: string; code?: string; name?: string; legalName?: string; revision?: string };
export const lookupLabel = (r: LookupRow) => `${r.number ?? r.partNumber ?? r.code ?? ''} — ${r.name ?? r.legalName ?? ''}${r.revision ? ` (rev ${r.revision})` : ''}`;
export function useLookup(kind: LookupKind | undefined, enabled = true) {
  return useQuery({
    queryKey: ['lookup', kind], enabled: !!kind && enabled, retry: false,
    queryFn: async () => { try { return (await get<{ items: LookupRow[] }>(`${lookupUrl[kind!]}?pageSize=200`)).items; } catch { return [] as LookupRow[]; } },
  });
}
function LookupField(props: Record<string, unknown> & { kind: LookupKind }) {
  const { kind, ...p } = props;
  const rows = useLookup(kind);
  return <TextField {...(p as object)} select><MenuItem value="">None</MenuItem>{(rows.data ?? []).map((r) => <MenuItem key={r.id} value={r.id}>{lookupLabel(r)}</MenuItem>)}</TextField>;
}

/** Create form driven by a Zod schema (validated with React Hook Form) and a field list. */
export function SchemaForm<S extends z.ZodType<FieldValues, FieldValues>>({ schema, fields, defaults, submitLabel, onSubmit, onCancel }: {
  schema: S; fields: FieldSpec<z.input<S>>[]; defaults: DefaultValues<z.input<S>>; submitLabel: string;
  onSubmit: (v: z.output<S>) => Promise<void>; onCancel?: () => void;
}) {
  const sites = useSites();
  const users = useUsers();
  const [error, setError] = useState<string | null>(null);
  const { control, handleSubmit, watch, formState: { errors, isSubmitting } } = useForm<z.input<S>, unknown, z.output<S>>({ resolver: zodResolver(schema as never) as never, defaultValues: defaults });
  const site = sites.data?.find((s) => s.id === (watch('siteId' as Path<z.input<S>>) as unknown as string));
  const submit = handleSubmit(async (v) => { setError(null); try { await onSubmit(v); } catch (e) { setError((e as Error).message); } });
  return (
    <Paper component="form" onSubmit={submit} sx={{ p: 3, maxWidth: 820, display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}>
      {error && <Alert severity="error" sx={{ gridColumn: '1 / -1' }}>{error}</Alert>}
      {fields.map((f) => f.type === 'heading'
        ? <Typography key={'h-' + f.label} variant="overline" color="text.secondary" sx={{ gridColumn: '1 / -1', mt: 1 }}>{f.label}</Typography>
        : (
        <Controller key={f.name} name={f.name} control={control} render={({ field }) => {
          const err = (errors as Record<string, { message?: string }>)[f.name as string];
          const p = { ...field, value: field.value ?? '', label: f.label, error: !!err, helperText: err?.message ?? f.helper, sx: f.full || f.type === 'multiline' ? { gridColumn: '1 / -1' } : undefined };
          switch (f.type) {
            case 'multiline': return <TextField {...p} multiline minRows={3} />;
            case 'number': return <TextField {...p} type="number" />;
            case 'date': return <TextField {...p} type="date" slotProps={{ inputLabel: { shrink: true } }} />;
            case 'select': return <TextField {...p} select>{(f.options ?? []).map(([v, l]) => <MenuItem key={v} value={v}>{l}</MenuItem>)}</TextField>;
            case 'site': return <TextField {...p} select>{(sites.data ?? []).filter((s) => !s.isArchived).map((s) => <MenuItem key={s.id} value={s.id}>{s.code} — {s.name}</MenuItem>)}</TextField>;
            case 'department': return <TextField {...p} select disabled={!site}><MenuItem value="">None</MenuItem>{(site?.departments ?? []).map((d) => <MenuItem key={d.id} value={d.id}>{d.code} — {d.name}</MenuItem>)}</TextField>;
            case 'lookup': return <LookupField {...p} kind={f.lookup!} />;
            case 'user': return <TextField {...p} select><MenuItem value="">Me</MenuItem>{(users.data ?? []).map((u) => <MenuItem key={u.id} value={u.id}>{u.displayName}</MenuItem>)}</TextField>;
            default: return <TextField {...p} />;
          }
        }} />
      ))}
      <Stack direction="row" gap={1} sx={{ gridColumn: '1 / -1' }}>
        <Button type="submit" variant="contained" disabled={isSubmitting}>{submitLabel}</Button>
        {onCancel && <Button onClick={onCancel}>Cancel</Button>}
      </Stack>
    </Paper>
  );
}

/** Small dialog with free-form inputs for a state transition. */
export function StepDialog({ open, title, intro, inputs, confirm, onClose, onConfirm, danger }: {
  open: boolean; title: string; intro?: string; confirm: string; danger?: boolean;
  inputs: { name: string; label: string; type?: 'text' | 'multiline' | 'date' | 'select' | 'multiselect' | 'password' | 'checkbox' | 'users' | 'site'; options?: [string | number, string][]; initial?: string; helper?: string; permission?: string }[];
  onClose: () => void; onConfirm: (v: Record<string, string>) => Promise<void>;
}) {
  const [v, setV] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const users = useUsers();
  const val = (n: string, i?: string) => v[n] ?? i ?? '';
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm" TransitionProps={{ onEnter: () => { setV({}); setError(null); } }}>
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        {intro && <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>{intro}</Typography>}
        {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
        <Stack gap={2} sx={{ pt: 1 }}>
          {inputs.map((i) => i.type === 'site'
            ? <SitePicker key={i.name} label={i.label} helperText={i.helper} permission={i.permission ?? ''} value={val(i.name, i.initial)} onChange={(id) => setV({ ...v, [i.name]: id ?? '' })} />
            : i.type === 'users'
            ? <TextField key={i.name} select label={i.label} helperText={i.helper} value={val(i.name, i.initial).split(',').filter(Boolean)} slotProps={{ select: { multiple: true } }}
                onChange={(e) => setV({ ...v, [i.name]: ([] as string[]).concat(e.target.value as unknown as string[]).join(',') })}>
                {(users.data ?? []).map((u) => <MenuItem key={u.id} value={u.id}>{u.displayName}</MenuItem>)}</TextField>
            : i.type === 'multiselect'
            ? <TextField key={i.name} select label={i.label} helperText={i.helper} value={val(i.name, i.initial).split(',').filter(Boolean)} slotProps={{ select: { multiple: true } }}
                onChange={(e) => setV({ ...v, [i.name]: ([] as string[]).concat(e.target.value as unknown as string[]).join(',') })}>
                {(i.options ?? []).map(([o, l]) => <MenuItem key={o} value={String(o)}>{l}</MenuItem>)}</TextField>
            : i.type === 'select'
            ? <TextField key={i.name} select label={i.label} value={val(i.name, i.initial)} onChange={(e) => setV({ ...v, [i.name]: e.target.value })}>{(i.options ?? []).map(([o, l]) => <MenuItem key={o} value={String(o)}>{l}</MenuItem>)}</TextField>
            : i.type === 'checkbox'
              ? <TextField key={i.name} select label={i.label} value={val(i.name, i.initial ?? 'false')} onChange={(e) => setV({ ...v, [i.name]: e.target.value })}><MenuItem value="true">Yes</MenuItem><MenuItem value="false">No</MenuItem></TextField>
              : <TextField key={i.name} label={i.label} helperText={i.helper} type={i.type === 'date' ? 'date' : i.type === 'password' ? 'password' : 'text'} multiline={i.type === 'multiline'} minRows={i.type === 'multiline' ? 3 : undefined}
                  value={val(i.name, i.initial)} onChange={(e) => setV({ ...v, [i.name]: e.target.value })} slotProps={i.type === 'date' ? { inputLabel: { shrink: true } } : undefined} />)}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" color={danger ? 'error' : 'primary'} onClick={async () => {
          const all = Object.fromEntries(inputs.map((i) => [i.name, val(i.name, i.initial ?? (i.type === 'checkbox' ? 'false' : ''))]));
          try { await onConfirm(all); onClose(); } catch (e) { setError((e as Error).message); }
        }}>{confirm}</Button>
      </DialogActions>
    </Dialog>
  );
}
