import { useMemo, useState } from 'react';
import { Alert, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Paper, Stack, Switch, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { del, get, post, put } from '../api';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { mono } from '../theme';
import { mod } from './Enterprise';
import { useAuth } from '../auth';
import { useTypeLabel, useUsers } from '../components/Kit';

/* eslint-disable @typescript-eslint/no-explicit-any */
const P = () => mod('platform');
const crumbs = (label: string) => [{ label: 'Platform', to: '/platform' }, { label }];
const opLabel: Record<string, string> = { equals: '=', notEquals: '≠', greaterThan: '>', greaterOrEqual: '≥', lessThan: '<', lessOrEqual: '≤', contains: 'contains', in: 'is one of', isEmpty: 'is empty', isNotEmpty: 'is set' };

interface WfStep { order: number; name: string; requiredPermission: string; signatureMeaning: string; requiredSkillId?: string | null; requireMfa?: boolean; stage: number; conditionField?: string | null; conditionOperator?: string | null; conditionValue?: string | null; escalateAfterHours?: number | null; escalationPermission?: string | null }
interface WfRoute { id?: string; recordType: string; name: string; version?: number; preventSelfApproval: boolean; steps: WfStep[]; siteId?: string | null }

/** No-code workflow designer on the shared approval engine: stages run in sequence, steps within a stage run in parallel, and conditions make steps apply only to matching records. */
export function WorkflowDesigner() {
  const qc = useQueryClient(); const typeLabel = useTypeLabel();
  const routes = useQuery({ queryKey: ['routes'], queryFn: () => get<WfRoute[]>('/api/admin/config/approval-routes') });
  const corporate = (routes.data ?? []).filter((r) => !r.siteId);
  const [type, setType] = useState<string>('');
  const [draft, setDraft] = useState<WfRoute | null>(null);
  const [editing, setEditing] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null); const [saved, setSaved] = useState<string | null>(null);
  const [previewId, setPreviewId] = useState(''); const [preview, setPreview] = useState<any[] | null>(null);
  const current = type || corporate[0]?.recordType || '';
  const base = corporate.find((r) => r.recordType === current);
  const route = draft && draft.recordType === current ? draft : base ? structuredClone(base) : null;
  const palette = useQuery({ queryKey: ['wf-fields', current], enabled: !!current, queryFn: () => get<any>(`/api/admin/config/workflow-designer/fields/${current}`) });
  const stages = useMemo(() => [...new Set((route?.steps ?? []).map((s) => s.stage))].sort((a, b) => a - b), [route]);
  const change = (steps: WfStep[]) => { setSaved(null); setDraft({ ...route!, steps }); };
  const addStep = (stage: number) => { const order = Math.max(0, ...route!.steps.map((s) => s.order)) + 1; change([...route!.steps, { order, stage, name: `Step ${order}`, requiredPermission: route!.steps[0]?.requiredPermission ?? '', signatureMeaning: 'Approved' }]); setEditing(order); };
  const step = route?.steps.find((s) => s.order === editing);
  const setStep = (patch: Partial<WfStep>) => change(route!.steps.map((s) => (s.order === editing ? { ...s, ...patch } : s)));
  const save = async () => {
    setError(null);
    try { await put(`/api/admin/config/approval-routes/${route!.recordType}`, { name: route!.name, preventSelfApproval: route!.preventSelfApproval, steps: route!.steps }); setDraft(null); setSaved('Saved as a new version. Approvals in progress finish on the version they started with.'); qc.invalidateQueries({ queryKey: ['routes'] }); }
    catch (e) { setError((e as Error).message); }
  };
  const runPreview = async () => { setError(null); try { setPreview(await get<any[]>(`/api/admin/config/approval-routes/${current}/preview/${previewId.trim()}`)); } catch (e) { setError((e as Error).message); } };
  return (
    <ModuleWorkspace module={P()} crumbs={crumbs('Workflow designer')} title="Workflow designer"
      tabs={corporate.map((r) => ({ value: r.recordType, label: typeLabel(r.recordType) }))} tab={current} onTab={(t) => { setType(t); setPreview(null); setEditing(null); }}
      actions={<Button variant="contained" disabled={!draft} onClick={save}>Publish new version</Button>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {saved && <Alert severity="success" sx={{ mb: 2 }}>{saved}</Alert>}
      {route && <>
        <Stack direction="row" gap={2} alignItems="center" sx={{ mb: 2 }}>
          <TextField size="small" label="Workflow name" value={route.name} onChange={(e) => { setSaved(null); setDraft({ ...route, name: e.target.value }); }} sx={{ minWidth: 320 }} />
          <FormControlLabel control={<Switch checked={route.preventSelfApproval} onChange={(e) => setDraft({ ...route, preventSelfApproval: e.target.checked })} />} label="Submitter cannot approve" />
          <Chip label={`Live version v${base?.version ?? 1}`} variant="outlined" />{draft && <Chip color="warning" label="Unpublished changes" />}
        </Stack>
        <Box sx={{ display: 'flex', gap: 0, overflowX: 'auto', pb: 1, alignItems: 'stretch' }}>
          <Box sx={{ minWidth: 90, display: 'grid', placeItems: 'center' }}><Chip color="primary" label="Submitted" /></Box>
          {stages.map((st, i) => <Box key={st} sx={{ display: 'flex', alignItems: 'stretch' }}>
            <Box sx={{ alignSelf: 'center', px: 1, color: 'text.secondary', fontSize: 22 }}>➜</Box>
            <Paper variant="outlined" sx={{ p: 1.5, minWidth: 250, bgcolor: 'rgba(0,128,128,.04)', borderStyle: 'dashed' }} data-testid={`stage-${i + 1}`}>
              <Typography variant="overline" sx={{ display: 'block' }}>Stage {i + 1}{route.steps.filter((s) => s.stage === st).length > 1 ? ' · parallel (all must sign)' : ''}</Typography>
              <Stack gap={1}>{route.steps.filter((s) => s.stage === st).map((s) => <Paper key={s.order} onClick={() => setEditing(s.order)} sx={{ p: 1.25, cursor: 'pointer', borderLeft: 4, borderColor: s.conditionField ? 'warning.main' : 'primary.main', outline: editing === s.order ? '2px solid' : 'none', outlineColor: 'primary.main' }}>
                <Typography variant="body2" sx={{ fontWeight: 600 }}>{s.name}</Typography>
                <Typography variant="caption" sx={{ fontFamily: mono, display: 'block' }}>{s.requiredPermission || 'choose a permission'} · “{s.signatureMeaning}”</Typography>
                {s.conditionField && <Chip size="small" color="warning" sx={{ mt: .5 }} label={`only if ${s.conditionField} ${opLabel[s.conditionOperator ?? 'equals']} ${s.conditionValue ?? ''}`} />}
                {s.escalateAfterHours && <Chip size="small" sx={{ mt: .5, ml: .5 }} label={`escalate after ${s.escalateAfterHours} h`} />}
              </Paper>)}</Stack>
              <Button size="small" sx={{ mt: 1 }} onClick={() => addStep(st)}>+ Parallel step</Button>
            </Paper></Box>)}
          <Box sx={{ alignSelf: 'center', px: 1, color: 'text.secondary', fontSize: 22 }}>➜</Box>
          <Stack justifyContent="center" gap={1}><Button variant="outlined" onClick={() => addStep(Math.max(0, ...stages) + 1)}>+ Add stage</Button><Chip color="success" label="Approved" /></Stack>
        </Box>
        <Paper sx={{ p: 2, mt: 2 }}>
          <Typography variant="h3" sx={{ mb: 1 }}>Dry run</Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>Enter a {typeLabel(current)} record id to see which steps the live version would apply to it.</Typography>
          <Stack direction="row" gap={1}><TextField size="small" label="Record id" value={previewId} onChange={(e) => setPreviewId(e.target.value)} sx={{ minWidth: 360 }} /><Button onClick={runPreview} disabled={!previewId.trim()}>Preview route</Button></Stack>
          {preview && <Stack direction="row" gap={1} sx={{ mt: 1, flexWrap: 'wrap' }}>{preview.map((p, i) => <Chip key={i} color={p.applies ? 'success' : 'default'} variant={p.applies ? 'filled' : 'outlined'} label={`${p.step}: ${p.applies ? 'applies' : 'skipped'}`} />)}</Stack>}
        </Paper>
      </>}
      {!route && <Typography color="text.secondary">No approval workflows are configured yet.</Typography>}
      <Dialog open={!!step} onClose={() => setEditing(null)} fullWidth maxWidth="sm">
        <DialogTitle>Step settings</DialogTitle>
        {step && <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
          <TextField label="Step name" value={step.name} onChange={(e) => setStep({ name: e.target.value })} />
          <TextField select label="Signer permission" value={step.requiredPermission} onChange={(e) => setStep({ requiredPermission: e.target.value })}>{(palette.data?.permissions ?? []).map((p: string) => <MenuItem key={p} value={p}>{p}</MenuItem>)}</TextField>
          <TextField label="Signature meaning" value={step.signatureMeaning} onChange={(e) => setStep({ signatureMeaning: e.target.value })} />
          <FormControlLabel control={<Switch checked={!!step.requireMfa} onChange={(e) => setStep({ requireMfa: e.target.checked })} />} label="Require MFA-verified signature" />
          <Typography variant="overline">Condition (decision branch)</Typography>
          <Stack direction="row" gap={1}>
            <TextField select size="small" label="Field" value={step.conditionField ?? ''} onChange={(e) => setStep({ conditionField: e.target.value || null, conditionOperator: step.conditionOperator ?? 'equals' })} sx={{ flex: 1 }}><MenuItem value="">Always applies</MenuItem>{(palette.data?.fields ?? []).map((f: any) => <MenuItem key={f.name} value={f.name}>{f.name}</MenuItem>)}</TextField>
            <TextField select size="small" label="Operator" value={step.conditionOperator ?? 'equals'} disabled={!step.conditionField} onChange={(e) => setStep({ conditionOperator: e.target.value })} sx={{ width: 150 }}>{(palette.data?.operators ?? []).map((o: string) => <MenuItem key={o} value={o}>{opLabel[o] ?? o}</MenuItem>)}</TextField>
            {(() => { const f = (palette.data?.fields ?? []).find((x: any) => x.name === step.conditionField); return f?.options
              ? <TextField select size="small" label="Value" value={step.conditionValue ?? ''} disabled={!step.conditionField} onChange={(e) => setStep({ conditionValue: e.target.value })} sx={{ flex: 1 }}>{f.options.map((o: string) => <MenuItem key={o} value={o}>{o}</MenuItem>)}</TextField>
              : <TextField size="small" label="Value" value={step.conditionValue ?? ''} disabled={!step.conditionField} onChange={(e) => setStep({ conditionValue: e.target.value })} sx={{ flex: 1 }} />; })()}
          </Stack>
          <Typography variant="overline">Escalation</Typography>
          <Stack direction="row" gap={1}>
            <TextField size="small" type="number" label="Escalate after (hours)" value={step.escalateAfterHours ?? ''} onChange={(e) => setStep({ escalateAfterHours: e.target.value ? Number(e.target.value) : null })} sx={{ width: 200 }} />
            <TextField select size="small" label="Escalate to permission" value={step.escalationPermission ?? ''} onChange={(e) => setStep({ escalationPermission: e.target.value || null })} sx={{ flex: 1 }}><MenuItem value="">Same signer group</MenuItem>{(palette.data?.permissions ?? []).map((p: string) => <MenuItem key={p} value={p}>{p}</MenuItem>)}</TextField>
          </Stack>
        </DialogContent>}
        <DialogActions>
          <Button color="error" disabled={(route?.steps.length ?? 0) <= 1} onClick={() => { change(route!.steps.filter((s) => s.order !== editing)); setEditing(null); }}>Remove step</Button>
          <Button variant="contained" onClick={() => setEditing(null)}>Done</Button>
        </DialogActions>
      </Dialog>
    </ModuleWorkspace>
  );
}

/* ---------------- Form designer ---------------- */
interface Cond { field: string; operator: string; value?: string | null }
interface Check { operator: string; field?: string | null; value?: string | null; message: string }
interface FField { key: string; label: string; type: string; required?: boolean; options?: string[] | null; min?: number | null; max?: number | null; unit?: string | null; formula?: string | null; section?: string | null;
  visibleWhen?: Cond | null; requiredWhen?: Cond | null; pattern?: string | null; patternMessage?: string | null; checks?: Check[] | null; fromComponent?: string | null }
const ftypes: [string, string, string][] = [['text', 'Short text', 'Aa'], ['longtext', 'Paragraph', '¶'], ['number', 'Number', '#'], ['date', 'Date', '📅'], ['boolean', 'Yes / no', '✓'], ['choice', 'Choice list', '☰'], ['calc', 'Calculated', 'ƒ']];
const ops = ['equals', 'notEquals', 'greaterThan', 'greaterOrEqual', 'lessThan', 'lessOrEqual', 'contains', 'in', 'isEmpty', 'isNotEmpty'];
const clean = (fs: FField[]) => JSON.stringify(fs.map((f) => Object.fromEntries(Object.entries(f).filter(([, v]) => v !== null && v !== undefined && v !== ''))));

function CondEditor({ label, value, keys, onChange }: { label: string; value?: Cond | null; keys: string[]; onChange: (c: Cond | null) => void }) {
  return (
    <Stack direction="row" gap={1}>
      <TextField select size="small" label={label} value={value?.field ?? ''} onChange={(e) => onChange(e.target.value ? { field: e.target.value, operator: value?.operator ?? 'equals', value: value?.value ?? '' } : null)} sx={{ flex: 1 }}>
        <MenuItem value="">Always</MenuItem>{keys.map((k) => <MenuItem key={k} value={k}>{k}</MenuItem>)}</TextField>
      <TextField select size="small" label="is" value={value?.operator ?? 'equals'} disabled={!value} onChange={(e) => onChange({ ...value!, operator: e.target.value })} sx={{ width: 110 }}>{ops.map((o) => <MenuItem key={o} value={o}>{opLabel[o]}</MenuItem>)}</TextField>
      <TextField size="small" label="Value" value={value?.value ?? ''} disabled={!value} onChange={(e) => onChange({ ...value!, value: e.target.value })} sx={{ width: 110 }} />
    </Stack>
  );
}

/** Form designer: palette + canvas + property panel with dynamic rules and validation, live preview evaluated by the server, and the versioned component library. Saves into a form template's draft revision (document control publishes it). */
export function FormDesigner() {
  const qc = useQueryClient();
  const [tab, setTab] = useState('design');
  const templates = useQuery({ queryKey: ['form-templates'], queryFn: () => get<any>('/api/documents?kind=2&pageSize=100&page=1') });
  const library = useQuery({ queryKey: ['form-library'], queryFn: () => get<any[]>('/api/forms/library') });
  const [tplId, setTplId] = useState(''); const tpl = useQuery({ queryKey: ['doc', tplId], enabled: !!tplId, queryFn: () => get<any>(`/api/documents/${tplId}`) });
  const draftRev = tpl.data?.revisions?.find((r: any) => r.status === 0);
  const [fields, setFields] = useState<FField[]>([]); const [loadedFor, setLoadedFor] = useState('');
  if (draftRev && loadedFor !== draftRev.id) { setLoadedFor(draftRev.id); try { setFields(draftRev.fieldSchema ? JSON.parse(draftRev.fieldSchema) : []); } catch { setFields([]); } }
  const [sel, setSel] = useState<number | null>(null); const f = sel != null ? fields[sel] : null;
  const set = (patch: Partial<FField>) => setFields(fields.map((x, i) => (i === sel ? { ...x, ...patch } : x)));
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const [sample, setSample] = useState<Record<string, string>>({}); const [preview, setPreview] = useState<any>(null);
  const [comp, setComp] = useState<{ open: boolean; key: string; name: string; category: string }>({ open: false, key: '', name: '', category: 'General' });
  const keys = fields.map((x) => x.key);
  const add = (type: string) => { const n = fields.length + 1; setFields([...fields, { key: `${type}_${n}`, label: ftypes.find((t) => t[0] === type)![1], type, options: type === 'choice' ? ['Option A', 'Option B'] : undefined, formula: type === 'calc' ? '' : undefined }]); setSel(fields.length); };
  const insert = async (c: any) => { const prefix = window.prompt('Key prefix for the inserted fields (optional)', c.key) ?? ''; try { const fs = await get<FField[]>(`/api/forms/library/${c.id}/insert?prefix=${encodeURIComponent(prefix)}`); setFields([...fields, ...fs]); setMsg({ ok: true, text: `Inserted ${c.name} v${c.version}.` }); } catch (e) { setMsg({ ok: false, text: (e as Error).message }); } };
  const runPreview = async () => { setPreview(await post<any>('/api/forms/library/preview', { fieldsJson: clean(fields), values: sample })); };
  const save = async () => {
    setMsg(null);
    try { await put(`/api/documents/revisions/${draftRev.id}`, { changeSummary: draftRev.changeSummary, effectiveDate: draftRev.effectiveDate, trainingRequired: draftRev.trainingRequired, rowVersion: draftRev.rowVersion, fieldSchema: clean(fields) }); setMsg({ ok: true, text: `Saved to ${tpl.data.number} rev ${draftRev.revision} (draft). Submit it for approval in Documents to publish.` }); qc.invalidateQueries({ queryKey: ['doc', tplId] }); }
    catch (e) { setMsg({ ok: false, text: (e as Error).message }); }
  };
  const saveComponent = async () => { setMsg(null); try { await post('/api/forms/library', { key: comp.key, name: comp.name, category: comp.category, description: null, fieldsJson: clean(fields) }); setComp({ ...comp, open: false }); qc.invalidateQueries({ queryKey: ['form-library'] }); setMsg({ ok: true, text: 'Component saved as a draft in the library.' }); } catch (e) { setMsg({ ok: false, text: (e as Error).message }); } };
  return (
    <ModuleWorkspace module={P()} crumbs={crumbs('Form designer')} title="Form designer" tabs={[{ value: 'design', label: 'Design' }, { value: 'preview', label: 'Live preview' }, { value: 'library', label: 'Component library' }]} tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1}><Button disabled={fields.length === 0} onClick={() => setComp({ ...comp, open: true })}>Save as component</Button><Button variant="contained" disabled={!draftRev} onClick={save}>Save to template draft</Button></Stack>}>
      {msg && <Alert severity={msg.ok ? 'success' : 'error'} sx={{ mb: 2 }} onClose={() => setMsg(null)}>{msg.text}</Alert>}
      <Stack direction="row" gap={2} alignItems="center" sx={{ mb: 2 }}>
        <TextField select size="small" label="Form template" value={tplId} onChange={(e) => { setTplId(e.target.value); setSel(null); }} sx={{ minWidth: 360 }}>
          {(templates.data?.items ?? []).map((t: any) => <MenuItem key={t.id} value={t.id}>{t.number} — {t.title}</MenuItem>)}</TextField>
        {tplId && !draftRev && tpl.data && <Alert severity="info" sx={{ py: 0 }}>No draft revision. Start a new revision in Documents to change this template; you can still design and save components.</Alert>}
        {draftRev && <Chip color="warning" label={`Editing rev ${draftRev.revision} (draft)`} />}
      </Stack>
      {tab === 'design' && <Box sx={{ display: 'grid', gridTemplateColumns: '200px 1fr 360px', gap: 2, alignItems: 'start' }}>
        <Paper sx={{ p: 1.5 }}><Typography variant="overline">Palette</Typography>
          <Stack gap={.75}>{ftypes.map(([t, l, icon]) => <Button key={t} variant="outlined" size="small" sx={{ justifyContent: 'flex-start' }} onClick={() => add(t)}><Box component="span" sx={{ width: 24, fontFamily: mono }}>{icon}</Box>{l}</Button>)}</Stack>
          <Typography variant="overline" sx={{ display: 'block', mt: 2 }}>Library</Typography>
          <Stack gap={.75}>{(library.data ?? []).filter((c) => c.status === 1).map((c) => <Button key={c.id} size="small" sx={{ justifyContent: 'flex-start', textAlign: 'left' }} onClick={() => insert(c)}>⧉ {c.name} v{c.version}</Button>)}
            {(library.data ?? []).filter((c) => c.status === 1).length === 0 && <Typography variant="caption" color="text.secondary">No published components.</Typography>}</Stack></Paper>
        <Paper sx={{ p: 1.5, minHeight: 300, bgcolor: 'rgba(0,128,128,.03)' }}><Typography variant="overline">Canvas · {fields.length} fields</Typography>
          <Stack gap={1}>{fields.map((x, i) => <Paper key={i} onClick={() => setSel(i)} sx={{ p: 1.25, cursor: 'pointer', display: 'flex', gap: 1.5, alignItems: 'center', outline: sel === i ? '2px solid' : 'none', outlineColor: 'primary.main' }}>
            <Box sx={{ width: 28, height: 28, borderRadius: 1, bgcolor: 'primary.main', color: 'primary.contrastText', display: 'grid', placeItems: 'center', fontFamily: mono, fontSize: 13 }}>{ftypes.find((t) => t[0] === x.type)?.[2]}</Box>
            <Box sx={{ flex: 1 }}><Typography variant="body2" sx={{ fontWeight: 600 }}>{x.label}{x.required ? ' *' : ''}</Typography><Typography variant="caption" sx={{ fontFamily: mono }}>{x.key}</Typography></Box>
            {x.visibleWhen && <Chip size="small" color="info" label={`shown if ${x.visibleWhen.field} ${opLabel[x.visibleWhen.operator]} ${x.visibleWhen.value ?? ''}`} />}
            {x.requiredWhen && <Chip size="small" color="warning" label="conditionally required" />}
            {(x.checks?.length || x.pattern) && <Chip size="small" label="validated" />}
            {x.fromComponent && <Chip size="small" variant="outlined" label={x.fromComponent} />}
            <Button size="small" disabled={i === 0} onClick={(e) => { e.stopPropagation(); const n = [...fields]; [n[i - 1], n[i]] = [n[i], n[i - 1]]; setFields(n); setSel(i - 1); }}>↑</Button>
            <Button size="small" color="error" onClick={(e) => { e.stopPropagation(); setFields(fields.filter((_, j) => j !== i)); setSel(null); }}>✕</Button></Paper>)}
            {fields.length === 0 && <Typography color="text.secondary" sx={{ p: 3, textAlign: 'center' }}>Pick a template, then add fields from the palette or insert a library component.</Typography>}</Stack></Paper>
        <Paper sx={{ p: 1.5 }}><Typography variant="overline">Properties</Typography>
          {!f && <Typography variant="body2" color="text.secondary">Select a field on the canvas.</Typography>}
          {f && <Stack gap={1.25}>
            <TextField size="small" label="Label" value={f.label} onChange={(e) => set({ label: e.target.value })} />
            <TextField size="small" label="Key" value={f.key} onChange={(e) => set({ key: e.target.value.toLowerCase().replace(/[^a-z0-9_]/g, '_') })} />
            <TextField size="small" label="Section" value={f.section ?? ''} onChange={(e) => set({ section: e.target.value || null })} />
            <FormControlLabel control={<Switch checked={!!f.required} disabled={f.type === 'calc'} onChange={(e) => set({ required: e.target.checked })} />} label="Always required" />
            {f.type === 'choice' && <TextField size="small" label="Options (comma separated)" value={(f.options ?? []).join(', ')} onChange={(e) => set({ options: e.target.value.split(',').map((s) => s.trim()) })} />}
            {f.type === 'calc' && <TextField size="small" label="Formula" value={f.formula ?? ''} onChange={(e) => set({ formula: e.target.value })} />}
            {f.type === 'number' && <Stack direction="row" gap={1}><TextField size="small" type="number" label="Min" value={f.min ?? ''} onChange={(e) => set({ min: e.target.value === '' ? null : Number(e.target.value) })} /><TextField size="small" type="number" label="Max" value={f.max ?? ''} onChange={(e) => set({ max: e.target.value === '' ? null : Number(e.target.value) })} /><TextField size="small" label="Unit" value={f.unit ?? ''} onChange={(e) => set({ unit: e.target.value || null })} /></Stack>}
            <Typography variant="overline">Dynamic rules</Typography>
            <CondEditor label="Show when" value={f.visibleWhen} keys={keys.filter((k) => k !== f.key)} onChange={(c) => set({ visibleWhen: c })} />
            <CondEditor label="Required when" value={f.requiredWhen} keys={keys.filter((k) => k !== f.key)} onChange={(c) => set({ requiredWhen: c })} />
            <Typography variant="overline">Validation</Typography>
            {(f.type === 'text' || f.type === 'longtext') && <><TextField size="small" label="Pattern (regular expression)" value={f.pattern ?? ''} onChange={(e) => set({ pattern: e.target.value || null })} /><TextField size="small" label="Pattern message" value={f.patternMessage ?? ''} onChange={(e) => set({ patternMessage: e.target.value || null })} /></>}
            {(f.checks ?? []).map((c, i) => <Stack key={i} direction="row" gap={.5}>
              <TextField select size="small" label="Must be" value={c.operator} onChange={(e) => set({ checks: f.checks!.map((x, j) => (j === i ? { ...x, operator: e.target.value } : x)) })} sx={{ width: 90 }}>{ops.slice(0, 8).map((o) => <MenuItem key={o} value={o}>{opLabel[o]}</MenuItem>)}</TextField>
              <TextField select size="small" label="Field" value={c.field ?? ''} onChange={(e) => set({ checks: f.checks!.map((x, j) => (j === i ? { ...x, field: e.target.value || null } : x)) })} sx={{ width: 100 }}><MenuItem value="">value →</MenuItem>{keys.filter((k) => k !== f.key).map((k) => <MenuItem key={k} value={k}>{k}</MenuItem>)}</TextField>
              <TextField size="small" label="Value" value={c.value ?? ''} disabled={!!c.field} onChange={(e) => set({ checks: f.checks!.map((x, j) => (j === i ? { ...x, value: e.target.value } : x)) })} sx={{ width: 70 }} />
              <TextField size="small" label="Message" value={c.message} onChange={(e) => set({ checks: f.checks!.map((x, j) => (j === i ? { ...x, message: e.target.value } : x)) })} sx={{ flex: 1 }} /></Stack>)}
            <Button size="small" onClick={() => set({ checks: [...(f.checks ?? []), { operator: 'greaterOrEqual', field: null, value: '', message: '' }] })}>+ Add check</Button>
          </Stack>}</Paper>
      </Box>}
      {tab === 'preview' && <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}>
        <Paper sx={{ p: 2 }}><Typography variant="overline">Sample entry</Typography>
          <Stack gap={1.25}>{fields.filter((x) => x.type !== 'calc' && (!preview || preview.visible.includes(x.key))).map((x) => x.type === 'choice'
            ? <TextField key={x.key} select size="small" label={x.label} value={sample[x.key] ?? ''} onChange={(e) => setSample({ ...sample, [x.key]: e.target.value })}><MenuItem value="">—</MenuItem>{(x.options ?? []).map((o) => <MenuItem key={o} value={o}>{o}</MenuItem>)}</TextField>
            : <TextField key={x.key} size="small" type={x.type === 'date' ? 'date' : 'text'} InputLabelProps={{ shrink: true }} label={x.label} value={sample[x.key] ?? ''} onChange={(e) => setSample({ ...sample, [x.key]: e.target.value })} />)}</Stack>
          <Button variant="contained" sx={{ mt: 2 }} onClick={runPreview}>Evaluate rules</Button></Paper>
        <Paper sx={{ p: 2 }}><Typography variant="overline">Result</Typography>
          {!preview && <Typography color="text.secondary">Enter sample values and evaluate. Hidden fields disappear from the entry pane.</Typography>}
          {preview && <>{preview.error ? <Alert severity="warning">{preview.error}</Alert> : <Alert severity="success">All rules pass.</Alert>}
            <Typography variant="body2" sx={{ mt: 1 }}>Visible: {preview.visible.join(', ')}</Typography>
            {preview.values && <Box component="pre" sx={{ fontFamily: mono, fontSize: 12 }}>{JSON.stringify(preview.values, null, 2)}</Box>}</>}</Paper>
      </Box>}
      {tab === 'library' && <Stack gap={1}>{(library.data ?? []).map((c) => <Paper key={c.id} sx={{ p: 1.5, display: 'flex', gap: 2, alignItems: 'center' }}>
        <Chip size="small" color={c.status === 1 ? 'success' : 'warning'} label={c.status === 1 ? 'Published' : 'Draft'} /><Typography sx={{ fontWeight: 600, flex: 1 }}>{c.name} <Typography component="span" variant="caption" sx={{ fontFamily: mono }}>{c.key} v{c.version} · {c.category}</Typography></Typography>
        {c.status === 0 && <Button size="small" onClick={async () => { try { await post(`/api/forms/library/${c.id}/publish`); qc.invalidateQueries({ queryKey: ['form-library'] }); } catch (e) { setMsg({ ok: false, text: (e as Error).message }); } }}>Publish</Button>}
        {c.status === 1 && <Button size="small" onClick={async () => { try { await post(`/api/forms/library/${c.id}/new-version`); qc.invalidateQueries({ queryKey: ['form-library'] }); } catch (e) { setMsg({ ok: false, text: (e as Error).message }); } }}>New version</Button>}</Paper>)}
        {library.data?.length === 0 && <Typography color="text.secondary">The library is empty. Design fields and choose “Save as component”.</Typography>}</Stack>}
      <Dialog open={comp.open} onClose={() => setComp({ ...comp, open: false })} fullWidth maxWidth="xs"><DialogTitle>Save as library component</DialogTitle>
        <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}><TextField label="Component key" value={comp.key} onChange={(e) => setComp({ ...comp, key: e.target.value.toLowerCase().replace(/[^a-z0-9_]/g, '_') })} /><TextField label="Name" value={comp.name} onChange={(e) => setComp({ ...comp, name: e.target.value })} /><TextField label="Category" value={comp.category} onChange={(e) => setComp({ ...comp, category: e.target.value })} /></DialogContent>
        <DialogActions><Button onClick={() => setComp({ ...comp, open: false })}>Cancel</Button><Button variant="contained" onClick={saveComponent}>Save draft</Button></DialogActions></Dialog>
    </ModuleWorkspace>
  );
}

/* ---------------- Business rules ---------------- */
const ruleKinds = [{ k: 0, label: 'Validate', verb: 'block submission with', color: 'error.main' }, { k: 1, label: 'Route', verb: 'route to', color: 'primary.main' }, { k: 2, label: 'Escalate', verb: 'escalate to', color: 'warning.main' }, { k: 3, label: 'Automate', verb: 'notify', color: 'success.main' }];
const triggers = ['record.created', 'record.updated', 'record.approved', 'record.rejected', 'workflow.completed', 'workflow.step_signed'];
const blankRule = { name: '', recordType: 'Ncr', kind: 0, trigger: 'record.created', conditions: [{ field: '', operator: 'equals', value: '' }] as Cond[], message: '', targetPermission: '', dueDays: 3, afterHours: 24, siteId: null, priority: 100, enabled: true };

/** Business rule engine: rules read as sentences (WHEN … AND … THEN …) in four lanes; a builder dialog, a dry run against a record, and the firing log. */
export function RulesStudio() {
  const qc = useQueryClient(); const typeLabel = useTypeLabel(); const [tab, setTab] = useState('rules');
  const rules = useQuery({ queryKey: ['rules'], queryFn: () => get<any[]>('/api/platform/rules') });
  const firings = useQuery({ queryKey: ['rule-firings'], enabled: tab === 'log', queryFn: () => get<any[]>('/api/platform/rules/firings') });
  const modules = useQuery({ queryKey: ['platform-modules'], queryFn: () => get<any[]>('/api/platform/modules') });
  const types = [...new Set((modules.data ?? []).flatMap((m: any) => m.recordTypes as string[]))].sort();
  const [edit, setEdit] = useState<any | null>(null); const [error, setError] = useState<string | null>(null);
  const palette = useQuery({ queryKey: ['wf-fields', edit?.recordType], enabled: !!edit?.recordType, queryFn: () => get<any>(`/api/admin/config/workflow-designer/fields/${edit.recordType}`) });
  const perms = useQuery({ queryKey: ['perm-catalog'], queryFn: () => get<string[]>('/api/admin/roles/permissions') });
  const [testing, setTesting] = useState<any | null>(null); const [testId, setTestId] = useState(''); const [testRes, setTestRes] = useState<any>(null);
  const save = async () => {
    setError(null);
    const body = { ...edit, conditions: edit.conditions.filter((c: Cond) => c.field), targetPermission: edit.targetPermission || null, trigger: edit.kind === 3 ? edit.trigger : null, dueDays: edit.kind === 1 ? Number(edit.dueDays) : null, afterHours: edit.kind === 2 ? Number(edit.afterHours) : null };
    try { if (edit.id) await put(`/api/platform/rules/${edit.id}`, body); else await post('/api/platform/rules', body); setEdit(null); qc.invalidateQueries({ queryKey: ['rules'] }); } catch (e) { setError((e as Error).message); }
  };
  const sentence = (r: any) => <>
    <b>WHEN</b> {typeLabel(r.recordType)}{r.kind === 3 ? <> {r.trigger}</> : r.kind === 2 ? <> waits {r.afterHours} h for approval</> : <> is submitted</>}
    {r.conditions.map((c: Cond, i: number) => <span key={i}> <b>{i === 0 ? 'AND' : 'AND'}</b> {c.field} {opLabel[c.operator]} {c.value}</span>)}
    {' '}<b>THEN</b> {ruleKinds[r.kind].verb} {r.kind === 0 ? `“${r.message}”` : r.targetPermission}{r.kind === 1 ? ` (due in ${r.dueDays} d)` : ''}</>;
  return (
    <ModuleWorkspace module={P()} crumbs={crumbs('Business rules')} title="Business rule engine" tabs={[{ value: 'rules', label: 'Rules' }, { value: 'log', label: 'Firing log' }]} tab={tab} onTab={setTab}
      actions={<Button variant="contained" onClick={() => { setError(null); setEdit(structuredClone(blankRule)); }}>New rule</Button>}>
      {tab === 'rules' && <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(4, 1fr)' }, gap: 2 }}>
        {ruleKinds.map((k) => <Box key={k.k}><Typography variant="overline" sx={{ color: k.color, fontWeight: 700 }}>{k.label}</Typography>
          <Stack gap={1}>{(rules.data ?? []).filter((r) => r.kind === k.k).map((r) => <Paper key={r.id} sx={{ p: 1.25, borderTop: 3, borderColor: k.color, opacity: r.enabled ? 1 : .55 }}>
            <Typography variant="body2" sx={{ fontWeight: 600 }}>{r.name}</Typography>
            <Typography variant="caption" sx={{ display: 'block', my: .5, fontFamily: mono }}>{sentence(r)}</Typography>
            <Stack direction="row" gap={.5}><Button size="small" onClick={() => { setError(null); setEdit(structuredClone(r)); }}>Edit</Button><Button size="small" onClick={() => { setTesting(r); setTestRes(null); }}>Dry run</Button>
              <Button size="small" onClick={async () => { await put(`/api/platform/rules/${r.id}`, { ...r, enabled: !r.enabled }); qc.invalidateQueries({ queryKey: ['rules'] }); }}>{r.enabled ? 'Disable' : 'Enable'}</Button></Stack></Paper>)}
            {(rules.data ?? []).filter((r) => r.kind === k.k).length === 0 && <Typography variant="caption" color="text.secondary">No rules.</Typography>}</Stack></Box>)}
      </Box>}
      {tab === 'log' && <Stack gap={.75}>{(firings.data ?? []).map((f) => <Paper key={f.id} sx={{ p: 1, display: 'flex', gap: 2 }}><Typography variant="caption" sx={{ minWidth: 150 }}>{new Date(f.firedAt).toLocaleString()}</Typography>
        <Chip size="small" label={typeLabel(f.recordType)} /><Typography variant="body2">{(rules.data ?? []).find((r) => r.id === f.ruleId)?.name ?? 'rule'} — {f.outcome}</Typography></Paper>)}
        {firings.data?.length === 0 && <Typography color="text.secondary">No rule has fired yet.</Typography>}</Stack>}
      <Dialog open={!!edit} onClose={() => setEdit(null)} fullWidth maxWidth="md"><DialogTitle>{edit?.id ? 'Edit rule' : 'New rule'}</DialogTitle>
        {edit && <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
          {error && <Alert severity="error">{error}</Alert>}
          <TextField label="Rule name" value={edit.name} onChange={(e) => setEdit({ ...edit, name: e.target.value })} />
          <Stack direction="row" gap={1}>{ruleKinds.map((k) => <Chip key={k.k} label={k.label} color={edit.kind === k.k ? 'primary' : 'default'} onClick={() => setEdit({ ...edit, kind: k.k })} />)}</Stack>
          <Stack direction="row" gap={1} alignItems="center"><Typography sx={{ fontWeight: 700 }}>WHEN</Typography>
            <TextField select size="small" label="Record type" value={edit.recordType} onChange={(e) => setEdit({ ...edit, recordType: e.target.value, conditions: [{ field: '', operator: 'equals', value: '' }] })} sx={{ minWidth: 200 }}>{(types.length ? types : [edit.recordType]).map((t) => <MenuItem key={t} value={t}>{typeLabel(t)}</MenuItem>)}</TextField>
            {edit.kind === 3 && <TextField select size="small" label="Event" value={edit.trigger} onChange={(e) => setEdit({ ...edit, trigger: e.target.value })} sx={{ minWidth: 200 }}>{triggers.map((t) => <MenuItem key={t} value={t}>{t}</MenuItem>)}</TextField>}
            {edit.kind === 2 && <TextField size="small" type="number" label="waits (hours)" value={edit.afterHours} onChange={(e) => setEdit({ ...edit, afterHours: e.target.value })} sx={{ width: 140 }} />}
            {edit.kind < 2 && <Typography>is submitted for approval</Typography>}</Stack>
          {edit.conditions.map((c: Cond, i: number) => <Stack key={i} direction="row" gap={1} alignItems="center"><Typography sx={{ fontWeight: 700, width: 48 }}>AND</Typography>
            <TextField select size="small" label="Field" value={c.field} onChange={(e) => setEdit({ ...edit, conditions: edit.conditions.map((x: Cond, j: number) => (j === i ? { ...x, field: e.target.value } : x)) })} sx={{ flex: 1 }}>{(palette.data?.fields ?? []).map((f: any) => <MenuItem key={f.name} value={f.name}>{f.name}</MenuItem>)}</TextField>
            <TextField select size="small" label="is" value={c.operator} onChange={(e) => setEdit({ ...edit, conditions: edit.conditions.map((x: Cond, j: number) => (j === i ? { ...x, operator: e.target.value } : x)) })} sx={{ width: 120 }}>{ops.map((o) => <MenuItem key={o} value={o}>{opLabel[o]}</MenuItem>)}</TextField>
            {(() => { const f = (palette.data?.fields ?? []).find((x: any) => x.name === c.field); const onV = (v: string) => setEdit({ ...edit, conditions: edit.conditions.map((x: Cond, j: number) => (j === i ? { ...x, value: v } : x)) });
              return f?.options ? <TextField select size="small" label="Value" value={c.value ?? ''} onChange={(e) => onV(e.target.value)} sx={{ width: 180 }}>{f.options.map((o: string) => <MenuItem key={o} value={o}>{o}</MenuItem>)}</TextField>
                : f?.type === 'yesno' ? <TextField select size="small" label="Value" value={c.value ?? ''} onChange={(e) => onV(e.target.value)} sx={{ width: 180 }}><MenuItem value="true">Yes</MenuItem><MenuItem value="false">No</MenuItem></TextField>
                : <TextField size="small" label="Value" value={c.value ?? ''} onChange={(e) => onV(e.target.value)} sx={{ width: 180 }} />; })()}
            <Button size="small" color="error" onClick={() => setEdit({ ...edit, conditions: edit.conditions.filter((_: Cond, j: number) => j !== i) })}>✕</Button></Stack>)}
          <Button size="small" sx={{ justifySelf: 'start' }} onClick={() => setEdit({ ...edit, conditions: [...edit.conditions, { field: '', operator: 'equals', value: '' }] })}>+ Condition</Button>
          <Stack direction="row" gap={1} alignItems="center"><Typography sx={{ fontWeight: 700 }}>THEN</Typography><Typography>{ruleKinds[edit.kind].verb}</Typography>
            {edit.kind !== 0 && <TextField select size="small" label="Permission group" value={edit.targetPermission ?? ''} onChange={(e) => setEdit({ ...edit, targetPermission: e.target.value })} sx={{ minWidth: 220 }}>{(perms.data ?? []).map((p) => <MenuItem key={p} value={p}>{p}</MenuItem>)}</TextField>}
            {edit.kind === 1 && <TextField size="small" type="number" label="Due in days" value={edit.dueDays} onChange={(e) => setEdit({ ...edit, dueDays: e.target.value })} sx={{ width: 120 }} />}
            <TextField size="small" label={edit.kind === 0 ? 'Message shown to the submitter' : 'Notification text'} value={edit.message} onChange={(e) => setEdit({ ...edit, message: e.target.value })} sx={{ flex: 1 }} /></Stack>
        </DialogContent>}
        <DialogActions>{edit?.id && <Button color="error" onClick={async () => { await del(`/api/platform/rules/${edit.id}`); setEdit(null); qc.invalidateQueries({ queryKey: ['rules'] }); }}>Delete</Button>}<Button onClick={() => setEdit(null)}>Cancel</Button><Button variant="contained" onClick={save}>Save rule</Button></DialogActions></Dialog>
      <Dialog open={!!testing} onClose={() => setTesting(null)} fullWidth maxWidth="sm"><DialogTitle>Dry run: {testing?.name}</DialogTitle>
        <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
          <Stack direction="row" gap={1}><TextField size="small" label={`${typeLabel(testing?.recordType)} record id`} value={testId} onChange={(e) => setTestId(e.target.value)} sx={{ flex: 1 }} />
            <Button onClick={async () => { try { setTestRes(await get(`/api/platform/rules/${testing.id}/test/${testId.trim()}`)); } catch (e) { setTestRes({ error: (e as Error).message }); } }}>Run</Button></Stack>
          {testRes?.error && <Alert severity="error">{testRes.error}</Alert>}
          {testRes?.conditions && <><Stack gap={.5}>{testRes.conditions.map((c: any, i: number) => <Typography key={i} variant="body2">{c.holds ? '✓' : '✗'} {c.field} {opLabel[c.operator]} {c.value} <Typography component="span" variant="caption">(actual: {c.actual ?? 'empty'})</Typography></Typography>)}</Stack>
            <Alert severity={testRes.matches ? 'warning' : 'info'}>{testRes.outcome}</Alert></>}
        </DialogContent></Dialog>
    </ModuleWorkspace>
  );
}

/* ---------------- Data governance & MDM ---------------- */
const classes = ['Public', 'Internal', 'Confidential', 'Restricted'];
const classColor = ['success', 'info', 'warning', 'error'] as const;
const ruleTypes = ['Required', 'Unique', 'Pattern', 'Allowed values'];
const changeStatus = ['Draft', 'Pending approval', 'Applied', 'Rejected'];

/** Data governance: domain tiles with compliance gauges, stewardship and classification; quality rules, violations and governed change requests per domain. */
export function DataGovernance() {
  const qc = useQueryClient(); const { can } = useAuth(); const users = useUsers();
  const domains = useQuery({ queryKey: ['data-domains'], queryFn: () => get<any[]>('/api/governance/data/domains') });
  const rules = useQuery({ queryKey: ['data-rules'], queryFn: () => get<any[]>('/api/governance/data/rules') });
  const [key, setKey] = useState('Part'); const [tab, setTab] = useState('rules');
  const d = (domains.data ?? []).find((x) => x.key === key);
  const quality = useQuery({ queryKey: ['data-quality', key], enabled: tab === 'violations' && can('platform.admin'), queryFn: () => get<any>(`/api/governance/data/quality/${key}`) });
  const changes = useQuery({ queryKey: ['data-changes', key], enabled: tab === 'changes', queryFn: () => get<any[]>(`/api/governance/data/changes?domain=${key}`) });
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const [rule, setRule] = useState<any | null>(null); const [stew, setStew] = useState<any | null>(null); const [chg, setChg] = useState<any | null>(null);
  const run = async (fn: () => Promise<unknown>, ok: string) => { setMsg(null); try { await fn(); setMsg({ ok: true, text: ok }); qc.invalidateQueries(); } catch (e) { setMsg({ ok: false, text: (e as Error).message }); } };
  return (
    <ModuleWorkspace module={P()} crumbs={crumbs('Data governance')} title="Data governance & master data"
      actions={can('platform.admin') ? <Button variant="contained" onClick={() => run(() => post('/api/governance/data/quality/run'), 'Data quality measured for every domain.')}>Measure data quality</Button> : undefined}>
      {msg && <Alert severity={msg.ok ? 'success' : 'error'} sx={{ mb: 2 }} onClose={() => setMsg(null)}>{msg.text}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'repeat(2, 1fr)', md: 'repeat(6, 1fr)' }, gap: 1.5, mb: 2 }}>
        {(domains.data ?? []).map((x) => { const c = x.latest?.compliance; const pct = c == null ? null : Number(c);
          return <Paper key={x.key} onClick={() => setKey(x.key)} sx={{ p: 1.5, cursor: 'pointer', textAlign: 'center', outline: key === x.key ? '2px solid' : 'none', outlineColor: 'primary.main' }}>
            <Box sx={{ mx: 'auto', width: 74, height: 74, borderRadius: '50%', display: 'grid', placeItems: 'center', background: pct == null ? '#e0e0e0' : `conic-gradient(${pct >= 95 ? '#2e7d32' : pct >= 80 ? '#ed6c02' : '#d32f2f'} ${pct * 3.6}deg, #e0e0e0 0)` }}>
              <Box sx={{ width: 58, height: 58, borderRadius: '50%', bgcolor: 'background.paper', display: 'grid', placeItems: 'center', fontWeight: 700, fontSize: 14 }}>{pct == null ? '—' : `${pct.toFixed(0)}%`}</Box></Box>
            <Typography variant="body2" sx={{ fontWeight: 600, mt: 1 }}>{x.name}</Typography>
            <Chip size="small" color={classColor[x.classification]} label={classes[x.classification]} sx={{ mt: .5 }} />
            <Typography variant="caption" sx={{ display: 'block', mt: .5 }}>Steward: {x.steward ?? 'unassigned'}</Typography>
            <Typography variant="caption" color="text.secondary">{x.rules} rules · {x.pendingChanges} pending</Typography></Paper>; })}
      </Box>
      {d && <Paper sx={{ p: 2 }}>
        <Stack direction="row" alignItems="center" gap={2} sx={{ mb: 1 }}><Typography variant="h3" sx={{ flex: 1 }}>{d.name}</Typography>
          {d.trend?.length > 1 && <Stack direction="row" alignItems="flex-end" gap={.25} sx={{ height: 28 }}>{d.trend.map((t: any, i: number) => <Box key={i} title={`${Number(t.compliance)}%`} sx={{ width: 6, height: `${Math.max(8, Number(t.compliance))}%`, bgcolor: 'primary.main', opacity: .7 }} />)}</Stack>}
          {can('platform.admin') && <Button size="small" onClick={() => setStew({ stewardId: d.stewardId ?? '', ownerId: d.ownerId ?? '', classification: d.classification, description: d.description ?? '' })}>Stewardship</Button>}
          {can('masterdata.edit') && <Button size="small" variant="outlined" onClick={() => setChg({ recordId: '', field: d.fields[0]?.name ?? '', value: '', reason: '' })}>Request change</Button>}</Stack>
        <Stack direction="row" gap={1} sx={{ mb: 2 }}>{[['rules', 'Quality rules'], ['violations', 'Violations'], ['changes', 'Change requests']].map(([v, l]) => <Chip key={v} label={l} color={tab === v ? 'primary' : 'default'} onClick={() => setTab(v)} />)}</Stack>
        {tab === 'rules' && <Stack gap={.75}>{(rules.data ?? []).filter((r) => r.domainKey === key).map((r) => <Stack key={r.id} direction="row" gap={1.5} alignItems="center" sx={{ p: 1, borderBottom: 1, borderColor: 'divider' }}>
          <Typography sx={{ fontFamily: mono, minWidth: 160 }}>{r.field}</Typography><Chip size="small" label={ruleTypes[r.ruleType]} />{r.parameter && <Typography variant="caption" sx={{ fontFamily: mono }}>{r.parameter}</Typography>}
          {r.blocking && <Chip size="small" color="error" variant="outlined" label="blocks changes" />}<Box sx={{ flex: 1 }} />
          {can('platform.admin') && <Button size="small" color="error" onClick={() => run(() => del(`/api/governance/data/rules/${r.id}`), 'Rule removed.')}>Remove</Button>}</Stack>)}
          {can('platform.admin') && <Button size="small" sx={{ alignSelf: 'start' }} onClick={() => setRule({ field: d.fields[0]?.name ?? '', ruleType: 0, parameter: '', blocking: false })}>+ Add rule</Button>}</Stack>}
        {tab === 'violations' && (!can('platform.admin') ? <Typography color="text.secondary">Violation detail is available to platform administrators.</Typography> : quality.data && <>
          <Typography variant="body2" sx={{ mb: 1 }}>{quality.data.records} records · {quality.data.failingRecords} failing · compliance {Number(quality.data.compliance).toFixed(1)}%</Typography>
          <Stack gap={.5}>{quality.data.sample.map((v: any, i: number) => <Stack key={i} direction="row" gap={2} alignItems="center"><Typography sx={{ fontFamily: mono, minWidth: 160 }}>{v.record}</Typography><Typography variant="body2" sx={{ flex: 1 }}>{v.rule}</Typography><Typography variant="caption">{v.value ?? 'empty'}</Typography>
            {can('masterdata.edit') && <Button size="small" onClick={() => setChg({ recordId: v.recordId, field: v.field, value: '', reason: `Fix: ${v.rule}` })}>Propose fix</Button>}</Stack>)}</Stack></>)}
        {tab === 'changes' && <Stack gap={.75}>{(changes.data ?? []).map((c) => <Paper key={c.id} variant="outlined" sx={{ p: 1.25, display: 'flex', gap: 2, alignItems: 'center' }}>
          <Typography sx={{ fontFamily: mono }}>{c.number}</Typography><Typography variant="body2" sx={{ flex: 1 }}>{c.recordLabel}: {Object.entries(c.changes).map(([k, v]) => `${k} ${c.previous?.[k] ?? '∅'} → ${v ?? '∅'}`).join('; ')}</Typography>
          <Chip size="small" color={c.status === 2 ? 'success' : c.status === 3 ? 'error' : 'warning'} label={changeStatus[c.status]} /></Paper>)}
          {changes.data?.length === 0 && <Typography color="text.secondary">No change requests for this domain.</Typography>}</Stack>}
      </Paper>}
      <Dialog open={!!rule} onClose={() => setRule(null)} fullWidth maxWidth="xs"><DialogTitle>Add data quality rule</DialogTitle>
        {rule && <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
          <TextField select label="Field" value={rule.field} onChange={(e) => setRule({ ...rule, field: e.target.value })}>{(d?.fields ?? []).map((f: any) => <MenuItem key={f.name} value={f.name}>{f.name}</MenuItem>)}</TextField>
          <TextField select label="Rule" value={rule.ruleType} onChange={(e) => setRule({ ...rule, ruleType: Number(e.target.value) })}>{ruleTypes.map((t, i) => <MenuItem key={t} value={i}>{t}</MenuItem>)}</TextField>
          {rule.ruleType >= 2 && <TextField label={rule.ruleType === 2 ? 'Regular expression' : 'Allowed values (comma separated)'} value={rule.parameter} onChange={(e) => setRule({ ...rule, parameter: e.target.value })} />}
          <FormControlLabel control={<Switch checked={rule.blocking} onChange={(e) => setRule({ ...rule, blocking: e.target.checked })} />} label="Block change requests that break it" /></DialogContent>}
        <DialogActions><Button onClick={() => setRule(null)}>Cancel</Button><Button variant="contained" onClick={() => run(async () => { await post('/api/governance/data/rules', { domainKey: key, ...rule, parameter: rule.parameter || null }); setRule(null); }, 'Rule added.')}>Add</Button></DialogActions></Dialog>
      <Dialog open={!!stew} onClose={() => setStew(null)} fullWidth maxWidth="xs"><DialogTitle>Stewardship: {d?.name}</DialogTitle>
        {stew && <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
          <TextField select label="Data steward" value={stew.stewardId} onChange={(e) => setStew({ ...stew, stewardId: e.target.value })}><MenuItem value="">Unassigned</MenuItem>{(users.data ?? []).map((u: any) => <MenuItem key={u.id} value={u.id}>{u.displayName}</MenuItem>)}</TextField>
          <TextField select label="Data owner" value={stew.ownerId} onChange={(e) => setStew({ ...stew, ownerId: e.target.value })}><MenuItem value="">Unassigned</MenuItem>{(users.data ?? []).map((u: any) => <MenuItem key={u.id} value={u.id}>{u.displayName}</MenuItem>)}</TextField>
          <TextField select label="Classification" value={stew.classification} onChange={(e) => setStew({ ...stew, classification: Number(e.target.value) })}>{classes.map((c, i) => <MenuItem key={c} value={i}>{c}</MenuItem>)}</TextField>
          <TextField label="Description" multiline minRows={2} value={stew.description} onChange={(e) => setStew({ ...stew, description: e.target.value })} /></DialogContent>}
        <DialogActions><Button onClick={() => setStew(null)}>Cancel</Button><Button variant="contained" onClick={() => run(async () => { await put(`/api/governance/data/domains/${key}`, { ...stew, stewardId: stew.stewardId || null, ownerId: stew.ownerId || null }); setStew(null); }, 'Stewardship saved.')}>Save</Button></DialogActions></Dialog>
      <Dialog open={!!chg} onClose={() => setChg(null)} fullWidth maxWidth="sm"><DialogTitle>Request a governed change</DialogTitle>
        {chg && <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
          <Typography variant="body2" color="text.secondary">The change is applied only after the approval route for master data changes is signed. Blocking data rules are checked first.</Typography>
          <TextField label={`${key} record id`} value={chg.recordId} onChange={(e) => setChg({ ...chg, recordId: e.target.value })} />
          <Stack direction="row" gap={1}><TextField select label="Field" value={chg.field} onChange={(e) => setChg({ ...chg, field: e.target.value })} sx={{ flex: 1 }}>{(d?.fields ?? []).map((f: any) => <MenuItem key={f.name} value={f.name}>{f.name}</MenuItem>)}</TextField>
            <TextField label="New value" value={chg.value} onChange={(e) => setChg({ ...chg, value: e.target.value })} sx={{ flex: 1 }} /></Stack>
          <TextField label="Reason" value={chg.reason} onChange={(e) => setChg({ ...chg, reason: e.target.value })} /></DialogContent>}
        <DialogActions><Button onClick={() => setChg(null)}>Cancel</Button><Button variant="contained" onClick={() => run(async () => { const r = await post<any>('/api/governance/data/changes', { domainKey: key, recordId: chg.recordId.trim(), changes: { [chg.field]: chg.value }, reason: chg.reason }); setChg(null); setTab('changes'); return r; }, 'Change submitted for approval.')}>Submit for approval</Button></DialogActions></Dialog>
    </ModuleWorkspace>
  );
}

const backupKinds = ['Database', 'File store', 'Configuration']; const backupStates = ['Succeeded', 'Failed'];
const verifyLabels = ['Not verified', 'Restore passed', 'Restore failed'];
const fmtSize = (b: number) => (b >= 1 << 30 ? `${(b / (1 << 30)).toFixed(1)} GB` : b >= 1 << 20 ? `${(b / (1 << 20)).toFixed(1)} MB` : `${Math.max(1, Math.round(b / 1024))} KB`);
const ago = (iso?: string | null) => { if (!iso) return 'never'; const h = (Date.now() - new Date(iso).getTime()) / 36e5; return h < 1 ? `${Math.max(1, Math.round(h * 60))} min ago` : h < 48 ? `${Math.round(h)} h ago` : `${Math.round(h / 24)} days ago`; };

/** Recovery center: what can be restored, how recently, whether a restore was proven, and whether each disaster scenario meets its RTO/RPO with a controlled procedure. */
export function RecoveryCenter() {
  const qc = useQueryClient();
  const status = useQuery({ queryKey: ['dr-status'], queryFn: () => get<any>('/api/platform/dr/status') });
  const backups = useQuery({ queryKey: ['dr-backups'], queryFn: () => get<any[]>('/api/platform/dr/backups') });
  const docs = useQuery({ queryKey: ['docs-procedures'], queryFn: () => get<{ items: any[] }>('/api/documents?kind=0&pageSize=200') });
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const [reg, setReg] = useState<any | null>(null); const [ver, setVer] = useState<any | null>(null); const [obj, setObj] = useState<any | null>(null);
  const run = async (fn: () => Promise<unknown>, ok: string) => { setMsg(null); try { await fn(); setMsg({ ok: true, text: ok }); qc.invalidateQueries({ queryKey: ['dr-status'] }); qc.invalidateQueries({ queryKey: ['dr-backups'] }); return true; } catch (e) { setMsg({ ok: false, text: (e as Error).message }); return false; } };
  const fromManifest = (text: string) => {
    try { const m = JSON.parse(text); setReg({ ...reg, manifest: text, kind: 0, label: m.label ?? reg.label, method: 'pg_dump custom format (scripts/backup.sh)', startedAt: m.database?.startedAt ?? reg.startedAt, completedAt: m.database?.completedAt ?? reg.completedAt, sizeBytes: m.database?.sizeBytes ?? reg.sizeBytes, sha256: m.database?.sha256 ?? '', fingerprint: m.fingerprint ?? null }); }
    catch { setReg({ ...reg, manifest: text }); }
  };
  const s = status.data;
  const newReg = () => { const now = new Date().toISOString().slice(0, 16); setReg({ manifest: '', kind: 0, label: '', location: '', method: '', startedAt: now, completedAt: now, sizeBytes: 0, sha256: '', state: 0, fingerprint: null }); };
  const tiles: [string, any, string][] = s ? [['Database backup', s.lastDatabaseBackup, s.lastDatabaseBackup?.completedAt], ['File store backup', s.lastFileStoreBackup, s.lastFileStoreBackup?.completedAt], ['Proven restore', s.lastVerifiedBackup, s.lastVerifiedBackup?.verifiedAt], ['Recovery test', s.lastRecoveryTest, s.lastRecoveryTest?.performedAt]] : [];
  return (
    <ModuleWorkspace module={P()} crumbs={crumbs('Recovery center')} title="Backup & disaster recovery"
      actions={<Stack direction="row" gap={1}><Button onClick={() => setObj({ scenario: '', rtoMinutes: 240, rpoMinutes: 1440, procedureDocumentId: '' })}>Add scenario</Button><Button variant="contained" onClick={newReg}>Register backup</Button></Stack>}>
      {msg && <Alert severity={msg.ok ? 'success' : 'error'} sx={{ mb: 2 }} onClose={() => setMsg(null)}>{msg.text}</Alert>}
      {s && <Paper sx={{ p: 2, mb: 2, borderLeft: 6, borderColor: s.recoverable ? 'success.main' : 'warning.main' }}>
        <Typography variant="h3">{s.recoverable ? 'Recoverable: every scenario meets its objectives' : `${s.gaps.length} recoverability gap${s.gaps.length === 1 ? '' : 's'}`}</Typography>
        {s.gaps.length > 0 && <Box component="ul" sx={{ m: 0, mt: 1, pl: 2.5 }}>{s.gaps.map((g: string) => <li key={g}><Typography variant="body2">{g}</Typography></li>)}</Box>}
      </Paper>}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(4, 1fr)' }, gap: 1.5, mb: 2 }}>
        {tiles.map(([label, v, at]) => <Paper key={label} variant="outlined" sx={{ p: 1.5 }}>
          <Typography variant="overline" color="text.secondary">{label}</Typography>
          <Typography variant="h3" sx={{ color: v ? 'text.primary' : 'error.main' }}>{ago(at)}</Typography>
          <Typography variant="caption" sx={{ fontFamily: mono }}>{v?.label ?? v?.number ?? '—'}</Typography></Paper>)}
      </Box>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '3fr 2fr' }, gap: 2 }}>
        <Paper sx={{ p: 2 }}><Typography variant="h3" sx={{ mb: 1 }}>Backup ledger</Typography>
          {(backups.data ?? []).map((b) => <Stack key={b.id} direction="row" gap={1.5} alignItems="center" sx={{ py: 1, borderBottom: 1, borderColor: 'divider' }}>
            <Box sx={{ width: 10, height: 10, borderRadius: '50%', bgcolor: b.state === 0 ? (b.verification === 1 ? 'success.main' : 'info.main') : 'error.main' }} />
            <Box sx={{ flex: 1, minWidth: 0 }}><Typography sx={{ fontFamily: mono }}>{b.label}</Typography>
              <Typography variant="caption" color="text.secondary">{backupKinds[b.kind]} · {fmtSize(b.sizeBytes)} · {new Date(b.completedAt).toLocaleString()} · {b.method}</Typography>
              {b.sha256 && <Typography variant="caption" sx={{ display: 'block', fontFamily: mono }} color="text.secondary">sha256 {b.sha256.slice(0, 16)}…</Typography>}</Box>
            <Chip size="small" label={b.state === 0 ? verifyLabels[b.verification] : backupStates[b.state]} color={b.state !== 0 || b.verification === 2 ? 'error' : b.verification === 1 ? 'success' : 'default'} variant="outlined" />
            {b.state === 0 && b.verification === 0 && <Button size="small" onClick={() => setVer({ id: b.id, label: b.label, passed: true, notes: '' })}>Record restore test</Button>}</Stack>)}
          {backups.data?.length === 0 && <Typography color="text.secondary">No backups registered yet. Run scripts/backup.sh and register its manifest here.</Typography>}
        </Paper>
        <Paper sx={{ p: 2 }}><Typography variant="h3" sx={{ mb: 1 }}>Disaster scenarios</Typography>
          {(s?.objectives ?? []).map((o: any) => <Paper key={o.id} variant="outlined" sx={{ p: 1.5, mb: 1, borderColor: o.met ? 'success.main' : 'warning.main' }}>
            <Stack direction="row" alignItems="center" gap={1}><Typography sx={{ flex: 1, fontWeight: 600 }}>{o.scenario}</Typography>
              <Chip size="small" label={`RTO ${o.rtoMinutes / 60 >= 1 ? `${o.rtoMinutes / 60} h` : `${o.rtoMinutes} min`}`} /><Chip size="small" label={`RPO ${o.rpoMinutes / 60 >= 1 ? `${o.rpoMinutes / 60} h` : `${o.rpoMinutes} min`}`} />
              <Button size="small" onClick={() => setObj({ id: o.id, scenario: o.scenario, rtoMinutes: o.rtoMinutes, rpoMinutes: o.rpoMinutes, procedureDocumentId: o.procedureDocumentId ?? '' })}>Edit</Button></Stack>
            <Typography variant="caption" color="text.secondary">Procedure: {o.procedure ?? 'none linked'}</Typography>
            {o.issues.map((i: string) => <Typography key={i} variant="body2" color="warning.dark">• {i}</Typography>)}</Paper>)}
          {s?.objectives?.length === 0 && <Typography color="text.secondary">Define each scenario you must recover from (server loss, storage loss, site loss) with its time and data-loss targets.</Typography>}
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>Recovery tests are recorded under Governance › Business continuity.</Typography>
        </Paper>
      </Box>
      <Dialog open={!!reg} onClose={() => setReg(null)} fullWidth maxWidth="sm"><DialogTitle>Register a backup</DialogTitle>
        {reg && <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
          <TextField label="Paste manifest.json from scripts/backup.sh (optional)" multiline minRows={2} value={reg.manifest} onChange={(e) => fromManifest(e.target.value)} InputProps={{ sx: { fontFamily: mono, fontSize: 12 } }} />
          <Stack direction="row" gap={1}><TextField select label="Kind" value={reg.kind} onChange={(e) => setReg({ ...reg, kind: Number(e.target.value) })} sx={{ flex: 1 }}>{backupKinds.map((k, i) => <MenuItem key={k} value={i}>{k}</MenuItem>)}</TextField>
            <TextField select label="Outcome" value={reg.state} onChange={(e) => setReg({ ...reg, state: Number(e.target.value) })} sx={{ flex: 1 }}>{backupStates.map((k, i) => <MenuItem key={k} value={i}>{k}</MenuItem>)}</TextField></Stack>
          <TextField label="Label" value={reg.label} onChange={(e) => setReg({ ...reg, label: e.target.value })} />
          <TextField label="Location" value={reg.location} onChange={(e) => setReg({ ...reg, location: e.target.value })} />
          <TextField label="Method" value={reg.method} onChange={(e) => setReg({ ...reg, method: e.target.value })} />
          <Stack direction="row" gap={1}><TextField type="datetime-local" label="Started" InputLabelProps={{ shrink: true }} value={String(reg.startedAt).slice(0, 16)} onChange={(e) => setReg({ ...reg, startedAt: e.target.value })} sx={{ flex: 1 }} />
            <TextField type="datetime-local" label="Completed" InputLabelProps={{ shrink: true }} value={String(reg.completedAt).slice(0, 16)} onChange={(e) => setReg({ ...reg, completedAt: e.target.value })} sx={{ flex: 1 }} /></Stack>
          <Stack direction="row" gap={1}><TextField type="number" label="Size (bytes)" value={reg.sizeBytes} onChange={(e) => setReg({ ...reg, sizeBytes: Number(e.target.value) })} sx={{ flex: 1 }} />
            <TextField label="SHA-256" value={reg.sha256} onChange={(e) => setReg({ ...reg, sha256: e.target.value })} sx={{ flex: 2 }} InputProps={{ sx: { fontFamily: mono, fontSize: 12 } }} /></Stack>
          {reg.fingerprint && <Typography variant="caption" color="text.secondary">Fingerprint captured: {Object.keys(reg.fingerprint.counts ?? {}).length} tables, migration {reg.fingerprint.migration}</Typography>}</DialogContent>}
        <DialogActions><Button onClick={() => setReg(null)}>Cancel</Button><Button variant="contained" onClick={async () => { const { manifest: _m, ...body } = reg; void _m; if (await run(() => post('/api/platform/dr/backups', { ...body, startedAt: new Date(body.startedAt).toISOString(), completedAt: new Date(body.completedAt).toISOString(), sha256: body.sha256 || null }), 'Backup registered.')) setReg(null); }}>Register</Button></DialogActions></Dialog>
      <Dialog open={!!ver} onClose={() => setVer(null)} fullWidth maxWidth="xs"><DialogTitle>Restore test: {ver?.label}</DialogTitle>
        {ver && <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
          <Typography variant="body2" color="text.secondary">Run scripts/verify-backup.sh against this backup. It restores into a scratch database and compares the schema version and row counts with the manifest.</Typography>
          <TextField select label="Result" value={ver.passed ? 1 : 0} onChange={(e) => setVer({ ...ver, passed: e.target.value === '1' })}><MenuItem value={1}>Passed</MenuItem><MenuItem value={0}>Failed</MenuItem></TextField>
          <TextField label="Script output / notes" multiline minRows={2} value={ver.notes} onChange={(e) => setVer({ ...ver, notes: e.target.value })} /></DialogContent>}
        <DialogActions><Button onClick={() => setVer(null)}>Cancel</Button><Button variant="contained" onClick={async () => { if (await run(() => post(`/api/platform/dr/backups/${ver.id}/verify`, { passed: ver.passed, notes: ver.notes }), 'Restore test recorded.')) setVer(null); }}>Record</Button></DialogActions></Dialog>
      <Dialog open={!!obj} onClose={() => setObj(null)} fullWidth maxWidth="xs"><DialogTitle>{obj?.id ? 'Edit scenario' : 'Add disaster scenario'}</DialogTitle>
        {obj && <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
          <TextField label="Scenario" value={obj.scenario} onChange={(e) => setObj({ ...obj, scenario: e.target.value })} />
          <Stack direction="row" gap={1}><TextField type="number" label="RTO (minutes)" value={obj.rtoMinutes} onChange={(e) => setObj({ ...obj, rtoMinutes: Number(e.target.value) })} /><TextField type="number" label="RPO (minutes)" value={obj.rpoMinutes} onChange={(e) => setObj({ ...obj, rpoMinutes: Number(e.target.value) })} /></Stack>
          <TextField select label="Recovery procedure (controlled document)" value={obj.procedureDocumentId} onChange={(e) => setObj({ ...obj, procedureDocumentId: e.target.value })}><MenuItem value="">None</MenuItem>{(docs.data?.items ?? []).map((d: any) => <MenuItem key={d.id} value={d.id}>{d.number} {d.title}</MenuItem>)}</TextField></DialogContent>}
        <DialogActions><Button onClick={() => setObj(null)}>Cancel</Button><Button variant="contained" onClick={async () => { const body = { scenario: obj.scenario, rtoMinutes: obj.rtoMinutes, rpoMinutes: obj.rpoMinutes, procedureDocumentId: obj.procedureDocumentId || null }; if (await run(() => (obj.id ? put(`/api/platform/dr/objectives/${obj.id}`, body) : post('/api/platform/dr/objectives', body)), 'Scenario saved.')) setObj(null); }}>Save</Button></DialogActions></Dialog>
    </ModuleWorkspace>
  );
}

const extKinds = ['Add-on', 'Quality extension', 'Industry pack']; const extColors = ['#5c6bc0', '#00897b', '#ef6c00'];
const licColor: Record<string, 'default' | 'success' | 'warning' | 'error'> = { Free: 'default', Licensed: 'success', Expired: 'error', Unlicensed: 'warning' };

/** Marketplace: add-ons, quality extensions and industry packs; dependencies on platform modules and other packages; signed licenses; versions. */
export function Marketplace() {
  const qc = useQueryClient();
  const catalog = useQuery({ queryKey: ['marketplace'], queryFn: () => get<any[]>('/api/platform/marketplace') });
  const licenses = useQuery({ queryKey: ['marketplace-licenses'], queryFn: () => get<any[]>('/api/platform/marketplace/licenses') });
  const [kind, setKind] = useState<number | 'installed' | null>(null); const [open, setOpen] = useState<string | null>(null);
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const [licKey, setLicKey] = useState<string | null>(null); const [manifest, setManifest] = useState<string | null>(null);
  const run = async (fn: () => Promise<unknown>, ok: string) => { setMsg(null); try { await fn(); setMsg({ ok: true, text: ok }); qc.invalidateQueries({ queryKey: ['marketplace'] }); qc.invalidateQueries({ queryKey: ['marketplace-licenses'] }); return true; } catch (e) { setMsg({ ok: false, text: (e as Error).message }); return false; } };
  const items = (catalog.data ?? []).filter((p) => kind === null || (kind === 'installed' ? !!p.installed : p.kind === kind));
  const sel = (catalog.data ?? []).find((p) => p.key === open);
  return (
    <ModuleWorkspace module={P()} crumbs={crumbs('Marketplace')} title="Extensions marketplace"
      actions={<Stack direction="row" gap={1}><Button onClick={() => setManifest('')}>Import package</Button><Button variant="contained" onClick={() => setLicKey('')}>Activate license</Button></Stack>}>
      {msg && <Alert severity={msg.ok ? 'success' : 'error'} sx={{ mb: 2 }} onClose={() => setMsg(null)}>{msg.text}</Alert>}
      <Stack direction="row" gap={1} sx={{ mb: 2 }}>
        {[[null, 'All'], ['installed', 'Installed'], [0, 'Add-ons'], [1, 'Quality extensions'], [2, 'Industry packs']].map(([v, l]) => <Chip key={String(l)} label={l as string} color={kind === v ? 'primary' : 'default'} onClick={() => setKind(v as any)} />)}
      </Stack>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(2, 1fr)', xl: 'repeat(3, 1fr)' }, gap: 2, mb: 3 }}>
        {items.map((p) => <Paper key={p.key} sx={{ p: 2, display: 'flex', flexDirection: 'column', gap: 1, borderTop: 4, borderColor: extColors[p.kind] }}>
          <Stack direction="row" alignItems="center" gap={1}><Typography variant="overline" sx={{ color: extColors[p.kind], flex: 1 }}>{extKinds[p.kind]}</Typography><Chip size="small" color={licColor[p.license]} label={p.license === 'Licensed' ? `Licensed to ${new Date(p.licensedUntil).toLocaleDateString()}` : p.license} /></Stack>
          <Typography variant="h3">{p.name}</Typography>
          <Typography variant="caption" color="text.secondary">{p.publisher} · v{p.latestVersion}{p.installed ? ` · installed v${p.installed.version}${p.installed.enabled ? '' : ' (disabled)'}` : ''}</Typography>
          <Typography variant="body2" sx={{ flex: 1 }}>{p.description}</Typography>
          <Stack direction="row" gap={.5} flexWrap="wrap">{p.dependencies.map((d: any) => <Chip key={d.key} size="small" variant="outlined" color={d.satisfied ? 'success' : 'warning'} label={`${d.satisfied ? '✓' : '•'} ${d.key.replace('module:', '')}${d.key.startsWith('module:') ? ' module' : ''}`} />)}</Stack>
          <Stack direction="row" gap={1}>
            {!p.installed && <Button size="small" variant="contained" disabled={p.license === 'Unlicensed' || p.license === 'Expired' || p.dependencies.some((d: any) => !d.satisfied)} onClick={() => run(() => post(`/api/platform/marketplace/${p.key}/install`, {}), `${p.name} installed.`)}>Install</Button>}
            {p.upgradeAvailable && <Button size="small" variant="contained" color="secondary" onClick={() => run(() => post(`/api/platform/marketplace/${p.key}/upgrade`, {}), `${p.name} upgraded to v${p.latestVersion}.`)}>Upgrade to v{p.latestVersion}</Button>}
            {p.installed && <Button size="small" onClick={() => run(() => post(`/api/platform/marketplace/${p.key}/enabled`, { enabled: !p.installed.enabled }), p.installed.enabled ? 'Disabled.' : 'Enabled.')}>{p.installed.enabled ? 'Disable' : 'Enable'}</Button>}
            <Box sx={{ flex: 1 }} /><Button size="small" onClick={() => setOpen(p.key)}>Details</Button></Stack>
        </Paper>)}
      </Box>
      <Paper sx={{ p: 2 }}><Typography variant="h3" sx={{ mb: 1 }}>Licenses</Typography>
        {(licenses.data ?? []).map((l) => { const expired = new Date(l.expiresAt) < new Date(); return <Stack key={l.id} direction="row" gap={2} alignItems="center" sx={{ py: 1, borderBottom: 1, borderColor: 'divider' }}>
          <Typography sx={{ fontFamily: mono, minWidth: 140 }}>{l.licenseId}</Typography><Typography variant="body2" sx={{ flex: 1 }}>{l.licensee} · {l.extensions.includes('*') ? 'all extensions' : l.extensions.join(', ')}</Typography>
          <Typography variant="caption">until {new Date(l.expiresAt).toLocaleDateString()}</Typography>
          <Chip size="small" color={l.revoked || expired ? 'error' : 'success'} label={l.revoked ? 'Revoked' : expired ? 'Expired' : 'Active'} />
          {!l.revoked && <Button size="small" color="error" onClick={() => run(() => post(`/api/platform/marketplace/licenses/${l.id}/revoke`, {}), 'License revoked.')}>Revoke</Button>}</Stack>; })}
        {licenses.data?.length === 0 && <Typography color="text.secondary">No licenses activated. Free extensions can be installed without one.</Typography>}
      </Paper>
      <Dialog open={!!sel} onClose={() => setOpen(null)} fullWidth maxWidth="sm"><DialogTitle>{sel?.name}</DialogTitle>
        {sel && <DialogContent sx={{ display: 'grid', gap: 1.5 }}>
          <Typography variant="body2">{sel.description}</Typography>
          <Typography variant="caption" color="text.secondary">Versions: {sel.versions.join(', ')}{sel.dependents.length ? ` · required by ${sel.dependents.join(', ')}` : ''}</Typography>
          <Typography variant="subtitle2">Installs</Typography>
          {[['Business rules', sel.contents.businessRules], ['Data quality rules', sel.contents.dataRules], ['Form library components (as drafts for approval)', sel.contents.formComponents]].filter(([, v]) => (v as string[]).length).map(([l, v]) => <Box key={l as string}><Typography variant="caption" color="text.secondary">{l as string}</Typography>{(v as string[]).map((x) => <Typography key={x} variant="body2">• {x}</Typography>)}</Box>)}
          {sel.installed && <><Typography variant="subtitle2">Created on this installation</Typography>{sel.installed.items.map((i: any) => <Typography key={i.id} variant="body2" sx={{ fontFamily: mono, fontSize: 12 }}>{i.type}: {i.label}</Typography>)}</>}
        </DialogContent>}
        <DialogActions>{sel?.installed && <Button color="error" onClick={async () => { if (await run(() => del(`/api/platform/marketplace/${sel.key}`), `${sel.name} uninstalled; its configuration was removed.`)) setOpen(null); }}>Uninstall</Button>}<Box sx={{ flex: 1 }} /><Button onClick={() => setOpen(null)}>Close</Button></DialogActions></Dialog>
      <Dialog open={licKey !== null} onClose={() => setLicKey(null)} fullWidth maxWidth="sm"><DialogTitle>Activate a license</DialogTitle>
        <DialogContent sx={{ pt: '8px !important' }}><TextField fullWidth multiline minRows={3} label="License key (QGL1…)" value={licKey ?? ''} onChange={(e) => setLicKey(e.target.value)} InputProps={{ sx: { fontFamily: mono, fontSize: 12 } }} helperText="The key is verified offline against the publisher signature; only its fingerprint is stored." /></DialogContent>
        <DialogActions><Button onClick={() => setLicKey(null)}>Cancel</Button><Button variant="contained" onClick={async () => { if (await run(() => post('/api/platform/marketplace/licenses', { licenseKey: licKey }), 'License activated.')) setLicKey(null); }}>Activate</Button></DialogActions></Dialog>
      <Dialog open={manifest !== null} onClose={() => setManifest(null)} fullWidth maxWidth="md"><DialogTitle>Import a package manifest</DialogTitle>
        <DialogContent sx={{ pt: '8px !important' }}><TextField fullWidth multiline minRows={10} label="manifest.json" value={manifest ?? ''} onChange={(e) => setManifest(e.target.value)} InputProps={{ sx: { fontFamily: mono, fontSize: 12 } }} helperText='{"key","name","kind","publisher","version","description","dependsOn":["module:Quality"],"requiresLicense","content":{"businessRules":[],"dataRules":[],"formComponents":[]}}' /></DialogContent>
        <DialogActions><Button onClick={() => setManifest(null)}>Cancel</Button><Button variant="contained" onClick={async () => { let body: unknown; try { body = JSON.parse(manifest ?? ''); } catch { setMsg({ ok: false, text: 'The manifest is not valid JSON.' }); return; } if (await run(() => post('/api/platform/marketplace/packages', body), 'Package added to the catalog.')) setManifest(null); }}>Import</Button></DialogActions></Dialog>
    </ModuleWorkspace>
  );
}

const fileKinds = ['Drawing', 'Document', 'Image', 'Video', 'Attachment'];
const classNames = ['Internal', 'Confidential', 'Restricted', 'Public'];
const kindGlyph: Record<string, string> = { Drawing: '⌗', Document: '▤', Image: '▣', Video: '▶', Attachment: '◇' };

/** Enterprise content library: every file the user can see across records, by kind, with versions, retention and classification; videos play in place. */
export function ContentLibrary() {
  const qc = useQueryClient(); const { can } = useAuth();
  const [kind, setKind] = useState<string>(''); const [q, setQ] = useState(''); const [sel, setSel] = useState<any | null>(null);
  const [ret, setRet] = useState<any | null>(null); const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const lib = useQuery({ queryKey: ['content-library', kind, q], queryFn: () => get<any[]>(`/api/files?take=200${kind ? `&kind=${kind}` : ''}${q ? `&q=${encodeURIComponent(q)}` : ''}`) });
  const versions = useQuery({ queryKey: ['file-versions', sel?.id], enabled: !!sel, queryFn: () => get<any[]>(`/api/files/${sel.id}/versions`) });
  const counts = useMemo(() => Object.fromEntries(fileKinds.map((k) => [k, (lib.data ?? []).filter((f) => f.kind === k).length])), [lib.data]);
  return (
    <ModuleWorkspace module={mod('documents')} crumbs={[{ label: 'Documents', to: '/documents' }, { label: 'Content library' }]} title="Content library">
      {msg && <Alert severity={msg.ok ? 'success' : 'error'} sx={{ mb: 2 }} onClose={() => setMsg(null)}>{msg.text}</Alert>}
      <Stack direction={{ xs: 'column', md: 'row' }} gap={1.5} alignItems={{ md: 'center' }} sx={{ mb: 2 }}>
        <Stack direction="row" gap={1} flexWrap="wrap" sx={{ flex: 1 }}>
          <Chip label="Everything" color={kind === '' ? 'primary' : 'default'} onClick={() => setKind('')} />
          {fileKinds.map((k) => <Chip key={k} label={`${kindGlyph[k]} ${k}s${kind === '' && counts[k] ? ` · ${counts[k]}` : ''}`} color={kind === k ? 'primary' : 'default'} onClick={() => setKind(k)} />)}
        </Stack>
        <TextField size="small" placeholder="Search file names and descriptions" value={q} onChange={(e) => setQ(e.target.value)} sx={{ minWidth: 280 }} />
      </Stack>
      <Box sx={{ display: 'grid', gridTemplateColumns: sel ? { xs: '1fr', lg: '1fr 1.2fr' } : '1fr', gap: 2 }}>
        <Paper sx={{ p: 1 }}>
          {(lib.data ?? []).map((f) => <Stack key={f.id} direction="row" gap={1.5} alignItems="center" onClick={() => setSel(f)} sx={{ p: 1, cursor: 'pointer', borderRadius: 1, bgcolor: sel?.id === f.id ? 'action.selected' : undefined, '&:hover': { bgcolor: 'action.hover' } }}>
            <Box sx={{ width: 34, height: 34, borderRadius: 1, display: 'grid', placeItems: 'center', bgcolor: f.kind === 'Video' ? '#263238' : 'primary.light', color: '#fff', fontSize: 16 }}>{kindGlyph[f.kind]}</Box>
            <Box sx={{ flex: 1, minWidth: 0 }}><Typography noWrap>{f.fileName}</Typography>
              <Typography variant="caption" color="text.secondary" noWrap>{f.ownerNumber} · {f.ownerTitle}</Typography></Box>
            <Typography variant="caption" sx={{ fontFamily: mono }}>v{f.version}{f.versions > 1 ? `/${f.versions}` : ''}</Typography>
            {f.classification !== 0 && <Chip size="small" variant="outlined" color={f.classification === 2 ? 'error' : f.classification === 1 ? 'warning' : 'default'} label={classNames[f.classification]} />}
            {f.retainUntil && <Chip size="small" variant="outlined" label={`kept to ${f.retainUntil.slice(0, 4)}`} />}
          </Stack>)}
          {lib.data?.length === 0 && <Typography color="text.secondary" sx={{ p: 2 }}>Nothing found. Files are added on the records they belong to.</Typography>}
        </Paper>
        {sel && <Paper sx={{ p: 2 }}>
          <Stack direction="row" alignItems="center" gap={1} sx={{ mb: 1 }}><Typography variant="h3" sx={{ flex: 1 }}>{sel.fileName}</Typography><Button size="small" onClick={() => setSel(null)}>Close</Button></Stack>
          {sel.kind === 'Video' && <Box component="video" key={sel.id} controls preload="metadata" src={`/api/files/${sel.id}?inline=true`} sx={{ width: '100%', maxHeight: 360, bgcolor: '#000', borderRadius: 1, mb: 1 }} />}
          {sel.kind === 'Image' && <Box component="img" src={`/api/files/${sel.id}?inline=true`} alt={sel.fileName} sx={{ maxWidth: '100%', maxHeight: 360, borderRadius: 1, mb: 1 }} />}
          <Box sx={{ display: 'grid', gridTemplateColumns: '140px 1fr', rowGap: .5, mb: 2 }}>
            <Typography variant="caption" color="text.secondary">Record</Typography><Typography variant="body2"><a href={sel.ownerUrl}>{sel.ownerNumber}</a> {sel.ownerTitle}</Typography>
            <Typography variant="caption" color="text.secondary">Kind</Typography><Typography variant="body2">{sel.kind} · {sel.contentType} · {fmtSize(sel.sizeBytes)}</Typography>
            <Typography variant="caption" color="text.secondary">Classification</Typography><Typography variant="body2">{classNames[sel.classification]}{sel.classification === 2 ? ' (opening needs edit rights on the record and is logged)' : sel.classification === 1 ? ' (opening is logged)' : ''}</Typography>
            <Typography variant="caption" color="text.secondary">Retention</Typography><Typography variant="body2">{sel.retainUntil ? `Kept until ${sel.retainUntil}; cannot be removed before then` : 'No retention date'}</Typography>
            <Typography variant="caption" color="text.secondary">Integrity</Typography><Typography variant="body2">SHA-256 checked on every open{sel.isLocked ? ' · locked (approved content)' : ''}</Typography>
          </Box>
          <Stack direction="row" gap={1} sx={{ mb: 2 }}>
            <Button size="small" variant="outlined" href={`/api/files/${sel.id}`}>Download</Button>
            {can('governance.edit') && <Button size="small" onClick={() => setRet({ retainUntil: sel.retainUntil ?? '', reason: '' })}>{sel.retainUntil ? 'Extend retention' : 'Set retention'}</Button>}
          </Stack>
          <Typography variant="subtitle2" sx={{ mb: .5 }}>Version history</Typography>
          {(versions.data ?? []).map((v) => <Stack key={v.id} direction="row" gap={1.5} alignItems="center" sx={{ py: .5, borderBottom: 1, borderColor: 'divider' }}>
            <Typography sx={{ fontFamily: mono, minWidth: 34 }}>v{v.version}</Typography><Typography variant="body2" sx={{ flex: 1 }}>{new Date(v.createdAt).toLocaleString()} · {fmtSize(v.sizeBytes)}</Typography>
            <Typography variant="caption" sx={{ fontFamily: mono }} color="text.secondary">{v.sha256.slice(0, 10)}…</Typography>
            {v.isArchived ? <Chip size="small" label="superseded" /> : <Chip size="small" color="success" label="current" />}
            <Button size="small" href={`/api/files/${v.id}`}>Get</Button></Stack>)}
        </Paper>}
      </Box>
      <Dialog open={!!ret} onClose={() => setRet(null)} fullWidth maxWidth="xs"><DialogTitle>Retention: {sel?.fileName}</DialogTitle>
        {ret && <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
          <Typography variant="body2" color="text.secondary">Applies to every version. Retention can be extended but never shortened.</Typography>
          <TextField type="date" label="Keep until" InputLabelProps={{ shrink: true }} value={ret.retainUntil} onChange={(e) => setRet({ ...ret, retainUntil: e.target.value })} />
          <TextField label="Reason" value={ret.reason} onChange={(e) => setRet({ ...ret, reason: e.target.value })} /></DialogContent>}
        <DialogActions><Button onClick={() => setRet(null)}>Cancel</Button><Button variant="contained" onClick={async () => { setMsg(null); try { await post(`/api/files/${sel.id}/retention`, ret); setMsg({ ok: true, text: 'Retention updated.' }); setSel({ ...sel, retainUntil: ret.retainUntil }); setRet(null); qc.invalidateQueries({ queryKey: ['content-library'] }); } catch (e) { setMsg({ ok: false, text: (e as Error).message }); } }}>Save</Button></DialogActions></Dialog>
    </ModuleWorkspace>
  );
}
