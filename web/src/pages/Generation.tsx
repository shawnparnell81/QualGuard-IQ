import { Fragment, useState } from 'react';
import { Alert, Box, Button, Checkbox, Chip, FormControlLabel, MenuItem, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link as RouterLink } from 'react-router-dom';
import { get, post, put } from '../api';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { fmtDateTime } from '../components/Status';
import { Section, useRunner } from '../components/Kit';
import { useAuth } from '../auth';
import { mod } from './Enterprise';

const modes = ['Rule-based only', 'External providers configured but not used', 'Approved functions may use an enabled provider'];
const kinds = ['Test fixture (sandbox only)', 'External HTTPS service', 'Local model server'];
const outcomes = ['Generated', 'Rule-based', 'Fell back', 'Blocked'];
const reviews = ['Pending review', 'Accepted', 'Discarded', '—'];

interface Settings { mode: number; timeoutSeconds: number; storeContent: boolean; contentRetentionDays: number; metadataRetentionDays: number; redactionPatterns: string[]; redactEmails: boolean; redactPhoneNumbers: boolean; maxSourceRecords: number }
interface ProviderRow { id: string; name: string; kind: number; endpoint?: string; model?: string; secretReference?: string; secretStored: boolean; enabled: boolean; lastTestAt?: string; lastTestOk?: boolean; lastTestMessage?: string; testCurrent: boolean; approvedAt?: string; needsApproval: boolean; canEnable: boolean; enableBlocker?: string }
interface FunctionRow { function: string; label: string; description: string; enabled: boolean; providerId?: string; allowConfidential: boolean }
interface LogRow { id: string; function: string; user?: string; recordType?: string; recordId?: string; providerName?: string; model?: string; outcome: number; fallbackReason?: string; createdAt: string; durationMs: number; sourceCount: number; excludedSources: number; redactions: number; injectionFlags: number; promptChars: number; responseChars: number; promptText?: string; responseText?: string; contentPurgedAt?: string; review: number }
export interface Source { ref: string; type: string; id?: string; label: string; url?: string }
export interface GenerationResult { logId: string; function: string; text: string; generated: boolean; label: string; method: string; sources: Source[]; notes: string[]; reviewRequired: boolean; review: number }

function SettingsTab() {
  const qc = useQueryClient();
  const q = useQuery({ queryKey: ['gen-settings'], queryFn: () => get<Settings>('/api/generation/settings') });
  const [v, setV] = useState<Settings | null>(null); const [patterns, setPatterns] = useState<string | null>(null); const [saved, setSaved] = useState(false);
  const r = useRunner(() => { qc.invalidateQueries({ queryKey: ['gen-settings'] }); setV(null); setPatterns(null); setSaved(true); });
  const s = v ?? q.data;
  if (!s) return null;
  const set = (x: Partial<Settings>) => { setSaved(false); setV({ ...s, ...x }); };
  return (
    <Section title="Generated content">
      <Alert severity="info" sx={{ mb: 2 }}>The built-in rule-based features always work and are the fallback. Generated drafts are labelled, cite the records they came from, need review by the person who asked, and never approve, close or move a record.</Alert>
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      {saved && <Alert severity="success" sx={{ mb: 2 }}>Settings saved.</Alert>}
      <Stack gap={2} sx={{ maxWidth: 720 }}>
        <TextField select label="Mode" value={s.mode} onChange={(e) => set({ mode: Number(e.target.value) })}>{modes.map((m, i) => <MenuItem key={m} value={i}>{m}</MenuItem>)}</TextField>
        <Stack direction="row" gap={2}>
          <TextField type="number" fullWidth label="Timeout (seconds)" value={s.timeoutSeconds} onChange={(e) => set({ timeoutSeconds: Number(e.target.value) })} helperText="Then the rule-based result is shown." />
          <TextField type="number" fullWidth label="Source records per request" value={s.maxSourceRecords} onChange={(e) => set({ maxSourceRecords: Number(e.target.value) })} />
        </Stack>
        <FormControlLabel control={<Checkbox checked={s.redactEmails} onChange={(e) => set({ redactEmails: e.target.checked })} />} label="Redact email addresses" />
        <FormControlLabel control={<Checkbox checked={s.redactPhoneNumbers} onChange={(e) => set({ redactPhoneNumbers: e.target.checked })} />} label="Redact phone numbers" />
        <TextField multiline minRows={2} label="Extra redaction patterns (one regular expression per line)" value={patterns ?? s.redactionPatterns.join('\n')} onChange={(e) => { setSaved(false); setPatterns(e.target.value); }} helperText="For example customer program codes. Passwords, keys and tokens are always redacted." />
        <FormControlLabel control={<Checkbox checked={s.storeContent} onChange={(e) => set({ storeContent: e.target.checked })} />} label="Keep the redacted prompt and the response with the request log" />
        <Stack direction="row" gap={2}>
          <TextField type="number" fullWidth label="Keep prompt/response text (days)" value={s.contentRetentionDays} onChange={(e) => set({ contentRetentionDays: Number(e.target.value) })} />
          <TextField type="number" fullWidth label="Keep request metadata (days)" value={s.metadataRetentionDays} onChange={(e) => set({ metadataRetentionDays: Number(e.target.value) })} />
        </Stack>
        <Box><Button variant="contained" disabled={r.busy} onClick={() => r.run(() => put('/api/generation/settings', { ...s, redactionPatterns: (patterns ?? s.redactionPatterns.join('\n')).split('\n').map((x) => x.trim()).filter(Boolean) }))}>Save settings</Button></Box>
      </Stack>
    </Section>
  );
}

function ProvidersTab() {
  const qc = useQueryClient(); const { can } = useAuth();
  const q = useQuery({ queryKey: ['gen-providers'], queryFn: () => get<ProviderRow[]>('/api/generation/providers') });
  const blank = { id: '', name: '', kind: 1, endpoint: '', model: '', secretReference: '', secret: '' };
  const [v, setV] = useState<typeof blank | null>(null); const [msg, setMsg] = useState<string | null>(null);
  const refresh = () => qc.invalidateQueries({ queryKey: ['gen-providers'] });
  const r = useRunner(refresh);
  return (
    <>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>A provider is off until its current configuration passes a connection test; external services also need approval by a second person with integrations.approve. Any change turns it off again. The request contract is in docs/setup/generation-providers.md.</Typography>
      {q.error && <Alert severity="error" sx={{ mb: 2 }}>{(q.error as Error).message}</Alert>}
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      {msg && <Alert severity="info" sx={{ mb: 2 }} onClose={() => setMsg(null)}>{msg}</Alert>}
      {!v && <Button variant="contained" sx={{ mb: 2 }} onClick={() => setV(blank)}>Add provider</Button>}
      {v && <Paper variant="outlined" sx={{ p: 2, mb: 2 }}><Stack gap={2} sx={{ maxWidth: 640 }}>
        <TextField label="Name" value={v.name} onChange={(e) => setV({ ...v, name: e.target.value })} />
        <TextField select label="Kind" value={v.kind} onChange={(e) => setV({ ...v, kind: Number(e.target.value) })}>{kinds.map((k, i) => <MenuItem key={k} value={i}>{k}</MenuItem>)}</TextField>
        {v.kind !== 0 && <TextField label="Endpoint" value={v.endpoint} onChange={(e) => setV({ ...v, endpoint: e.target.value })} helperText={v.kind === 1 ? 'HTTPS only.' : 'HTTP is allowed on loopback or private addresses.'} />}
        <TextField label="Model" value={v.model} onChange={(e) => setV({ ...v, model: e.target.value })} />
        <TextField label="Secret from configuration (optional)" value={v.secretReference} onChange={(e) => setV({ ...v, secretReference: e.target.value })} placeholder="config:Generation:ApiKey" />
        <TextField type="password" label="Or store a secret" value={v.secret} onChange={(e) => setV({ ...v, secret: e.target.value })} helperText="Encrypted, never shown again, sent only as the Authorization header." autoComplete="new-password" />
        <Stack direction="row" gap={1}><Button variant="contained" disabled={!v.name.trim() || r.busy} onClick={() => r.run(async () => {
          const body = { name: v.name, kind: v.kind, endpoint: v.endpoint || null, model: v.model || null, secretReference: v.secretReference || null };
          const id = v.id ? (await put(`/api/generation/providers/${v.id}`, body), v.id) : (await post<{ id: string }>('/api/generation/providers', body)).id;
          if (v.secret) await put(`/api/generation/providers/${id}/secret`, { secret: v.secret });
          setV(null);
        })}>Save provider</Button><Button onClick={() => setV(null)}>Cancel</Button></Stack>
      </Stack></Paper>}
      <Stack gap={1.5}>{(q.data ?? []).map((p) => <Paper key={p.id} variant="outlined" sx={{ p: 2 }} data-testid={`gen-provider-${p.name}`}>
        <Stack direction={{ xs: 'column', md: 'row' }} gap={1}>
          <Box sx={{ flex: 1 }}>
            <Typography sx={{ fontWeight: 600 }}>{p.name} <Chip size="small" label={p.enabled ? 'Enabled' : 'Disabled'} color={p.enabled ? 'success' : 'default'} sx={{ ml: 1 }} /></Typography>
            <Typography variant="body2" color="text.secondary">{kinds[p.kind]}{p.endpoint ? ` · ${p.endpoint}` : ''}{p.model ? ` · ${p.model}` : ''} · secret {p.secretStored ? 'stored' : p.secretReference ? `from ${p.secretReference}` : 'none'}</Typography>
            <Typography variant="body2">{p.lastTestAt ? `Last test ${fmtDateTime(p.lastTestAt)}: ${p.lastTestOk ? 'passed' : 'failed'}${p.testCurrent ? '' : ' (configuration changed since)'} — ${p.lastTestMessage ?? ''}` : 'Not tested yet.'}</Typography>
            {p.needsApproval && <Typography variant="body2">{p.approvedAt ? `Approved ${fmtDateTime(p.approvedAt)}` : 'Approval pending.'}</Typography>}
            {!p.enabled && p.enableBlocker && <Typography variant="body2" color="warning.main">{p.enableBlocker}</Typography>}
          </Box>
          <Stack direction="row" gap={1} flexWrap="wrap" alignItems="flex-start">
            <Button size="small" disabled={r.busy} onClick={() => r.run(async () => { const x = await post<{ ok: boolean; message: string }>(`/api/generation/providers/${p.id}/test`); setMsg(`${p.name}: ${x.ok ? 'test passed' : 'test failed'} — ${x.message}`); })}>Test connection</Button>
            {p.enabled ? <Button size="small" onClick={() => r.run(() => post(`/api/generation/providers/${p.id}/enabled`, { enabled: false }))}>Disable</Button>
              : <Button size="small" disabled={!p.canEnable || r.busy} onClick={() => r.run(() => post(`/api/generation/providers/${p.id}/enabled`, { enabled: true }))}>Enable</Button>}
            {p.needsApproval && !p.approvedAt && can('integrations.approve') && <Button size="small" disabled={!p.testCurrent || r.busy} onClick={() => r.run(() => post(`/api/generation/providers/${p.id}/approval`))}>Approve</Button>}
            <Button size="small" onClick={() => setV({ id: p.id, name: p.name, kind: p.kind, endpoint: p.endpoint ?? '', model: p.model ?? '', secretReference: p.secretReference === 'stored' ? '' : p.secretReference ?? '', secret: '' })}>Edit</Button>
          </Stack>
        </Stack>
      </Paper>)}</Stack>
      {q.data?.length === 0 && <Typography color="text.secondary">No providers configured.</Typography>}
    </>
  );
}

function FunctionsTab() {
  const qc = useQueryClient();
  const q = useQuery({ queryKey: ['gen-functions'], queryFn: () => get<FunctionRow[]>('/api/generation/functions') });
  const p = useQuery({ queryKey: ['gen-providers'], queryFn: () => get<ProviderRow[]>('/api/generation/providers') });
  const r = useRunner(() => qc.invalidateQueries({ queryKey: ['gen-functions'] }));
  const save = (f: FunctionRow, x: Partial<FunctionRow>) => r.run(() => put(`/api/generation/functions/${f.function}`, { enabled: x.enabled ?? f.enabled, providerId: (x.providerId ?? f.providerId) || null, allowConfidential: x.allowConfidential ?? f.allowConfidential }));
  return (
    <>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>Each function stays rule-based until it is enabled here with a provider and the organization mode allows approved functions. Records classified Restricted are never sent; Confidential ones only where allowed.</Typography>
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      <Table size="small" aria-label="Functions"><TableHead><TableRow><TableCell>Function</TableCell><TableCell>Provider</TableCell><TableCell>Confidential records</TableCell><TableCell>Enabled</TableCell></TableRow></TableHead>
        <TableBody>{(q.data ?? []).map((f) => <TableRow key={f.function}>
          <TableCell><b>{f.label}</b><Typography variant="caption" display="block" color="text.secondary">{f.description}</Typography></TableCell>
          <TableCell><TextField select size="small" label="Provider" value={f.providerId ?? ''} onChange={(e) => save(f, { providerId: e.target.value })} sx={{ minWidth: 200 }}><MenuItem value="">None</MenuItem>{(p.data ?? []).map((x) => <MenuItem key={x.id} value={x.id}>{x.name}{x.enabled ? '' : ' (disabled)'}</MenuItem>)}</TextField></TableCell>
          <TableCell><Checkbox inputProps={{ 'aria-label': `Allow confidential for ${f.label}` }} checked={f.allowConfidential} onChange={(e) => save(f, { allowConfidential: e.target.checked })} /></TableCell>
          <TableCell><Checkbox inputProps={{ 'aria-label': `Enable ${f.label}` }} checked={f.enabled} disabled={!f.providerId} onChange={(e) => save(f, { enabled: e.target.checked })} /></TableCell>
        </TableRow>)}</TableBody></Table>
    </>
  );
}

function LogTab() {
  const [outcome, setOutcome] = useState('');
  const [open, setOpen] = useState<string | null>(null);
  const q = useQuery({ queryKey: ['gen-log', outcome], queryFn: () => get<LogRow[]>(`/api/generation/requests${outcome ? `?outcome=${outcome}` : ''}`) });
  return (
    <>
      <TextField select size="small" label="Outcome" value={outcome} onChange={(e) => setOutcome(e.target.value)} sx={{ mb: 2, minWidth: 180 }}><MenuItem value="">All</MenuItem>{outcomes.map((o, i) => <MenuItem key={o} value={String(i)}>{o}</MenuItem>)}</TextField>
      <Table size="small" aria-label="Request log"><TableHead><TableRow><TableCell>When</TableCell><TableCell>Function</TableCell><TableCell>User</TableCell><TableCell>Provider</TableCell><TableCell>Outcome</TableCell><TableCell>Sources</TableCell><TableCell>Safety</TableCell><TableCell>Review</TableCell></TableRow></TableHead>
        <TableBody>{(q.data ?? []).map((l) => <Fragment key={l.id}>
          <TableRow hover onClick={() => setOpen(open === l.id ? null : l.id)} sx={{ cursor: 'pointer' }}>
            <TableCell>{fmtDateTime(l.createdAt)}</TableCell><TableCell>{l.function}</TableCell><TableCell>{l.user}</TableCell><TableCell>{l.providerName ?? '—'}{l.model ? ` (${l.model})` : ''}</TableCell>
            <TableCell><Chip size="small" label={outcomes[l.outcome]} color={l.outcome === 0 ? 'info' : l.outcome === 3 ? 'warning' : 'default'} />{l.fallbackReason ? <Typography variant="caption" display="block">{l.fallbackReason}</Typography> : null}</TableCell>
            <TableCell>{l.sourceCount}{l.excludedSources ? ` (+${l.excludedSources} withheld)` : ''}</TableCell>
            <TableCell>{l.redactions} redacted · {l.injectionFlags} instructions removed · {l.durationMs} ms</TableCell><TableCell>{reviews[l.review]}</TableCell>
          </TableRow>
          {open === l.id && <TableRow><TableCell colSpan={8}>
            {l.promptText || l.responseText ? <Stack gap={1}><Typography variant="caption">Prompt (redacted)</Typography><Box component="pre" sx={{ whiteSpace: 'pre-wrap', fontSize: 12, m: 0 }}>{l.promptText}</Box><Typography variant="caption">Response</Typography><Box component="pre" sx={{ whiteSpace: 'pre-wrap', fontSize: 12, m: 0 }}>{l.responseText}</Box></Stack>
              : <Typography variant="body2" color="text.secondary">{l.contentPurgedAt ? `Content purged ${fmtDateTime(l.contentPurgedAt)} (retention).` : 'Content is not stored; only hashes and counts are kept.'} {l.promptChars} prompt / {l.responseChars} response characters.</Typography>}
          </TableCell></TableRow>}
        </Fragment>)}</TableBody></Table>
      {q.data?.length === 0 && <Typography color="text.secondary" sx={{ mt: 1 }}>No requests yet.</Typography>}
    </>
  );
}

/** Administration of the optional generation boundary (platform.admin). */
export function GenerationAdmin() {
  const [tab, setTab] = useState('settings');
  return (
    <ModuleWorkspace module={mod('platform')} crumbs={[{ label: 'Platform', to: '/platform' }, { label: 'Generated content' }]} title="Generated content" tabs={[{ value: 'settings', label: 'Settings' }, { value: 'providers', label: 'Providers' }, { value: 'functions', label: 'Functions' }, { value: 'log', label: 'Request log' }]} tab={tab} onTab={setTab}>
      {tab === 'settings' && <SettingsTab />}
      {tab === 'providers' && <ProvidersTab />}
      {tab === 'functions' && <FunctionsTab />}
      {tab === 'log' && <LogTab />}
    </ModuleWorkspace>
  );
}

/** "Draft with provider" panel; renders nothing unless the function is available to the organization. */
export function GeneratedDraft({ fn, recordType, recordId, month }: { fn: string; recordType?: string; recordId?: string; month?: string }) {
  const avail = useQuery({ queryKey: ['gen-availability'], queryFn: () => get<Record<string, boolean>>('/api/generation/availability') });
  const [res, setRes] = useState<GenerationResult | null>(null);
  const [review, setReview] = useState<number | null>(null);
  const r = useRunner(() => undefined);
  if (!avail.data?.[fn]) return null;
  return (
    <Section title="Generated draft">
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      {!res && <Button variant="outlined" disabled={r.busy} onClick={() => r.run(async () => { setReview(null); setRes(await post<GenerationResult>('/api/generation/generate', { function: fn, recordType: recordType ?? null, recordId: recordId ?? null, month: month ?? null })); })}>{r.busy ? 'Generating…' : 'Generate draft'}</Button>}
      {res && <Stack gap={1.5}>
        <Alert severity={res.generated ? 'warning' : 'info'}>{res.label}</Alert>
        {res.notes.map((n) => <Typography key={n} variant="body2" color="text.secondary">{n}</Typography>)}
        <Box sx={{ whiteSpace: 'pre-wrap' }} data-testid="generated-text">{res.text}</Box>
        <Typography variant="caption" color="text.secondary">{res.method}</Typography>
        <Stack direction="row" gap={1} flexWrap="wrap">{res.sources.map((s) => s.url ? <Chip key={s.ref} size="small" component={RouterLink} to={s.url} clickable label={`[${s.ref}] ${s.label}`} /> : <Chip key={s.ref} size="small" label={`[${s.ref}] ${s.label}`} />)}</Stack>
        {res.reviewRequired && (review === null
          ? <Stack direction="row" gap={1}><Button size="small" variant="contained" onClick={() => r.run(async () => setReview((await post<{ review: number }>(`/api/generation/requests/${res.logId}/review`, { accepted: true })).review))}>Mark as reviewed and used</Button>
              <Button size="small" onClick={() => r.run(async () => setReview((await post<{ review: number }>(`/api/generation/requests/${res.logId}/review`, { accepted: false })).review))}>Discard</Button></Stack>
          : <Typography variant="body2">{review === 1 ? 'Recorded as reviewed. Copy what you need into the record yourself; nothing was changed automatically.' : 'Discarded.'}</Typography>)}
        <Box><Button size="small" onClick={() => { setRes(null); setReview(null); }}>New draft</Button></Box>
      </Stack>}
    </Section>
  );
}
