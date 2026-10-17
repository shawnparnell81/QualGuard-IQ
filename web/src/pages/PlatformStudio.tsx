import { useMemo, useState } from 'react';
import { Alert, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Paper, Stack, Switch, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { get, put } from '../api';
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
