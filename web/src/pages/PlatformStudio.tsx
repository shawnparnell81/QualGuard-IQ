import { useMemo, useState } from 'react';
import { Alert, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Paper, Stack, Switch, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { get, post, put } from '../api';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { mono } from '../theme';
import { mod } from './Enterprise';

/* eslint-disable @typescript-eslint/no-explicit-any */
const P = () => mod('platform');
const crumbs = (label: string) => [{ label: 'Platform', to: '/platform' }, { label }];
const opLabel: Record<string, string> = { equals: '=', notEquals: '≠', greaterThan: '>', greaterOrEqual: '≥', lessThan: '<', lessOrEqual: '≤', contains: 'contains', in: 'is one of', isEmpty: 'is empty', isNotEmpty: 'is set' };

interface WfStep { order: number; name: string; requiredPermission: string; signatureMeaning: string; requiredSkillId?: string | null; requireMfa?: boolean; stage: number; conditionField?: string | null; conditionOperator?: string | null; conditionValue?: string | null; escalateAfterHours?: number | null; escalationPermission?: string | null }
interface WfRoute { id?: string; recordType: string; name: string; version?: number; preventSelfApproval: boolean; steps: WfStep[]; siteId?: string | null }

/** No-code workflow designer on the shared approval engine: stages run in sequence, steps within a stage run in parallel, and conditions make steps apply only to matching records. */
export function WorkflowDesigner() {
  const qc = useQueryClient();
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
      tabs={corporate.map((r) => ({ value: r.recordType, label: r.recordType }))} tab={current} onTab={(t) => { setType(t); setPreview(null); setEditing(null); }}
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
          <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>Enter a {current} record id to see which steps the live version would apply to it.</Typography>
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
