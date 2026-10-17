import { useState } from 'react';
import { Alert, Box, Button, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { get, post, put } from '../api';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { fmtDateTime } from '../components/Status';
import { Section, useRunner } from '../components/Kit';
import { useAuth } from '../auth';
import { mod } from './Enterprise';

const channels = ['SMS', 'Push'];
const adapters: [number, string][] = [[1, 'Generic HTTPS gateway'], [0, 'Test fixture (sandbox only)'], [10, 'Twilio (slot, not implemented)'], [11, 'Firebase Cloud Messaging (slot, not implemented)'], [12, 'Apple Push (slot, not implemented)']];
const envs = ['Sandbox', 'Production'];
const states = ['Queued', 'Sent', 'Delivered', 'Failed', 'Skipped'];
const stateColor: ('default' | 'info' | 'success' | 'error' | 'warning')[] = ['default', 'info', 'success', 'error', 'warning'];

interface ProviderRow { id: string; name: string; channel: number; adapter: number; adapterDescription: string; canSend: boolean; environment: number; endpoint?: string; sender?: string; secretReference?: string; secretStored: boolean; callbackTokenSet: boolean;
  enabled: boolean; rateLimitPerMinute: number; maxAttempts: number; requireOptIn: boolean; keywords: string[]; templateId?: string; lastTestAt?: string; lastTestOk?: boolean; lastTestMessage?: string; testCurrent: boolean; productionApprovedAt?: string; canEnable: boolean; enableBlocker?: string }
interface Template { id: string; name: string; channel: number; text: string; pushTitle?: string; maxLength: number }
interface DeliveryRow { id: string; providerId: string; maskedAddress: string; text: string; state: number; attempts: number; nextAttemptAt?: string; failureReason?: string; providerMessageId?: string; isTest: boolean; createdAt: string; sentAt?: string; deliveredAt?: string }
interface SubscriptionRow { id: string; channel: number; address: string; label?: string; optedInAt: string; optedOutAt?: string }

function ProviderDialog({ open, existing, templates, onClose, onSaved }: { open: boolean; existing: ProviderRow | null; templates: Template[]; onClose: () => void; onSaved: () => void }) {
  const init = () => existing ? { ...existing, endpoint: existing.endpoint ?? '', sender: existing.sender ?? '', secretReference: existing.secretReference === 'stored' ? '' : existing.secretReference ?? '', keywords: existing.keywords.join(', '), templateId: existing.templateId ?? '' }
    : { name: '', channel: 0, adapter: 1, environment: 0, endpoint: '', sender: '', secretReference: '', rateLimitPerMinute: 30, maxAttempts: 3, requireOptIn: true, keywords: '', templateId: '' };
  const [v, setV] = useState(init);
  const [secret, setSecret] = useState(''); const [token, setToken] = useState('');
  const [error, setError] = useState<string | null>(null);
  const set = (x: Partial<ReturnType<typeof init>>) => setV({ ...v, ...x });
  const save = async () => {
    setError(null);
    try {
      const body = { name: v.name, channel: v.channel, adapter: v.adapter, environment: v.environment, endpoint: v.endpoint || null, sender: v.sender || null,
        secretReference: v.secretReference || (existing?.secretReference === 'stored' ? 'stored' : null), rateLimitPerMinute: Number(v.rateLimitPerMinute), maxAttempts: Number(v.maxAttempts), requireOptIn: v.requireOptIn,
        keywords: v.keywords.split(',').map((k) => k.trim()).filter(Boolean), templateId: v.templateId || null };
      const id = existing ? (await put(`/api/messaging/providers/${existing.id}`, body), existing.id) : (await post<{ id: string }>('/api/messaging/providers', body)).id;
      if (secret || token) await put(`/api/messaging/providers/${id}/secrets`, { secret: secret || null, callbackToken: token || null });
      onSaved(); onClose();
    } catch (e) { setError((e as Error).message); }
  };
  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth TransitionProps={{ onEnter: () => { setV(init()); setSecret(''); setToken(''); setError(null); } }}>
      <DialogTitle>{existing ? 'Edit provider' : 'Add SMS or push provider'}</DialogTitle>
      <DialogContent>
        <Stack gap={2} sx={{ mt: 1 }}>
          {error && <Alert severity="error">{error}</Alert>}
          <Alert severity="info">Any change to the channel, adapter, environment, endpoint, sender or secret disables the provider until the connection test passes again (and, for production, a second person approves it).</Alert>
          <TextField label="Name" value={v.name} onChange={(e) => set({ name: e.target.value })} required />
          <Stack direction="row" gap={2}>
            <TextField select fullWidth label="Channel" value={v.channel} onChange={(e) => set({ channel: Number(e.target.value) })}>{channels.map((c, i) => <MenuItem key={c} value={i}>{c}</MenuItem>)}</TextField>
            <TextField select fullWidth label="Environment" value={v.environment} onChange={(e) => set({ environment: Number(e.target.value) })}>{envs.map((c, i) => <MenuItem key={c} value={i}>{c}</MenuItem>)}</TextField>
          </Stack>
          <TextField select label="Adapter" value={v.adapter} onChange={(e) => set({ adapter: Number(e.target.value) })}>{adapters.map(([k, l]) => <MenuItem key={k} value={k}>{l}</MenuItem>)}</TextField>
          {v.adapter === 1 && <TextField label="Gateway endpoint (HTTPS)" value={v.endpoint} onChange={(e) => set({ endpoint: e.target.value })} helperText="POST target for messages; the test calls {endpoint}/health." />}
          <TextField label={v.channel === 0 ? 'Sender number or ID' : 'Application ID'} value={v.sender} onChange={(e) => set({ sender: e.target.value })} />
          <TextField label="Secret from configuration (optional)" value={v.secretReference} onChange={(e) => set({ secretReference: e.target.value })} placeholder="config:Messaging:GatewayKey" helperText="Read from deployment configuration, or leave blank and store a secret below." />
          <TextField type="password" label={existing?.secretStored ? 'Replace stored secret' : 'Store secret'} value={secret} onChange={(e) => setSecret(e.target.value)} helperText="Encrypted at rest and never shown again." autoComplete="new-password" />
          <TextField type="password" label={existing?.callbackTokenSet ? 'Replace callback token' : 'Callback token (for delivery receipts)'} value={token} onChange={(e) => setToken(e.target.value)} helperText="At least 24 characters; the gateway sends it as X-Callback-Token." autoComplete="new-password" />
          <Stack direction="row" gap={2}>
            <TextField type="number" fullWidth label="Messages per minute" value={v.rateLimitPerMinute} onChange={(e) => set({ rateLimitPerMinute: Number(e.target.value) })} />
            <TextField type="number" fullWidth label="Attempts" value={v.maxAttempts} onChange={(e) => set({ maxAttempts: Number(e.target.value) })} helperText="Retries after 1, 5, 15, 60 min" />
          </Stack>
          <TextField label="Only notifications whose title contains (comma separated)" value={v.keywords} onChange={(e) => set({ keywords: e.target.value })} helperText="Blank sends every notification to opted-in users." />
          <TextField select label="Template" value={v.templateId} onChange={(e) => set({ templateId: e.target.value })}><MenuItem value="">Default ({'{app}: {title} {link}'})</MenuItem>{templates.filter((t) => t.channel === v.channel).map((t) => <MenuItem key={t.id} value={t.id}>{t.name}</MenuItem>)}</TextField>
          <FormControlLabel control={<Checkbox checked={v.requireOptIn} disabled />} label="Only users who opted in themselves receive messages" />
        </Stack>
      </DialogContent>
      <DialogActions><Button onClick={onClose}>Cancel</Button><Button variant="contained" onClick={save} disabled={!v.name.trim()}>Save</Button></DialogActions>
    </Dialog>
  );
}

function ProvidersTab() {
  const qc = useQueryClient(); const { can } = useAuth();
  const q = useQuery({ queryKey: ['msg-providers'], queryFn: () => get<ProviderRow[]>('/api/messaging/providers') });
  const t = useQuery({ queryKey: ['msg-templates'], queryFn: () => get<Template[]>('/api/messaging/templates') });
  const [edit, setEdit] = useState<ProviderRow | null | undefined>(undefined);
  const [msg, setMsg] = useState<string | null>(null);
  const [testTo, setTestTo] = useState<Record<string, string>>({});
  const refresh = () => qc.invalidateQueries({ queryKey: ['msg-providers'] });
  const r = useRunner(refresh);
  return (
    <>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>Notifications are copied by SMS or push only to people who opted in under their account menu. A provider stays off until its current configuration passes a connection test; production providers also need approval by a second person with integrations.approve.</Typography>
      {q.error && <Alert severity="error" sx={{ mb: 2 }}>{(q.error as Error).message}</Alert>}
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      {msg && <Alert severity="info" sx={{ mb: 2 }} onClose={() => setMsg(null)}>{msg}</Alert>}
      <Button variant="contained" sx={{ mb: 2 }} onClick={() => setEdit(null)}>Add provider</Button>
      {q.data?.length === 0 && <Typography color="text.secondary">No providers configured.</Typography>}
      <Stack gap={1.5}>{(q.data ?? []).map((p) => (
        <Paper key={p.id} variant="outlined" sx={{ p: 2 }} data-testid={`provider-${p.name}`}>
          <Stack direction={{ xs: 'column', md: 'row' }} gap={1}>
            <Box sx={{ flex: 1 }}>
              <Typography sx={{ fontWeight: 600 }}>{p.name} <Chip size="small" label={p.enabled ? 'Enabled' : 'Disabled'} color={p.enabled ? 'success' : 'default'} sx={{ ml: 1 }} /> <Chip size="small" label={envs[p.environment]} variant="outlined" /></Typography>
              <Typography variant="body2" color="text.secondary">{channels[p.channel]} · {p.adapterDescription}</Typography>
              <Typography variant="body2">{p.lastTestAt ? `Last test ${fmtDateTime(p.lastTestAt)}: ${p.lastTestOk ? 'passed' : 'failed'}${p.testCurrent ? '' : ' (configuration changed since)'} — ${p.lastTestMessage ?? ''}` : 'Not tested yet.'}</Typography>
              {p.environment === 1 && <Typography variant="body2">{p.productionApprovedAt ? `Production approved ${fmtDateTime(p.productionApprovedAt)}` : 'Production approval pending.'}</Typography>}
              {!p.enabled && p.enableBlocker && <Typography variant="body2" color="warning.main">{p.enableBlocker}</Typography>}
              <Typography variant="body2" color="text.secondary">{p.rateLimitPerMinute}/min · {p.maxAttempts} attempts · secret {p.secretStored ? 'stored' : p.secretReference ? `from ${p.secretReference}` : 'none'} · receipts {p.callbackTokenSet ? `POST /api/messaging/callbacks/${p.id}` : 'off'}{p.keywords.length ? ` · only: ${p.keywords.join(', ')}` : ''}</Typography>
            </Box>
            <Stack gap={1} alignItems="flex-end">
              <Stack direction="row" gap={1} flexWrap="wrap">
                <Button size="small" disabled={r.busy} onClick={() => r.run(async () => { const x = await post<{ ok: boolean; message: string }>(`/api/messaging/providers/${p.id}/test`); setMsg(`${p.name}: ${x.ok ? 'test passed' : 'test failed'} — ${x.message}`); })}>Test connection</Button>
                {p.enabled ? <Button size="small" onClick={() => r.run(() => post(`/api/messaging/providers/${p.id}/enabled`, { enabled: false }))}>Disable</Button>
                  : <Button size="small" disabled={!p.canEnable || r.busy} onClick={() => r.run(() => post(`/api/messaging/providers/${p.id}/enabled`, { enabled: true }))}>Enable</Button>}
                {p.environment === 1 && !p.productionApprovedAt && can('integrations.approve') && <Button size="small" disabled={!p.testCurrent || r.busy} onClick={() => r.run(() => post(`/api/messaging/providers/${p.id}/production-approval`))}>Approve for production</Button>}
                <Button size="small" onClick={() => setEdit(p)}>Edit</Button>
              </Stack>
              {p.testCurrent && p.lastTestOk && <Stack direction="row" gap={1}>
                <TextField size="small" label={p.channel === 0 ? 'Send test to +1…' : 'Send test to device token'} value={testTo[p.id] ?? ''} onChange={(e) => setTestTo({ ...testTo, [p.id]: e.target.value })} />
                <Button size="small" disabled={!testTo[p.id] || r.busy} onClick={() => r.run(async () => { const d = await post<DeliveryRow>(`/api/messaging/providers/${p.id}/test-message`, { address: testTo[p.id] }); setMsg(`Test message ${states[d.state].toLowerCase()}${d.failureReason ? ` — ${d.failureReason}` : ''}`); })}>Send test</Button>
              </Stack>}
            </Stack>
          </Stack>
        </Paper>))}</Stack>
      <ProviderDialog open={edit !== undefined} existing={edit ?? null} templates={t.data ?? []} onClose={() => setEdit(undefined)} onSaved={refresh} />
    </>
  );
}

function TemplatesTab() {
  const qc = useQueryClient();
  const q = useQuery({ queryKey: ['msg-templates'], queryFn: () => get<Template[]>('/api/messaging/templates') });
  const blank = { id: '', name: '', channel: 0, text: '{app}: {title} {link}', pushTitle: '', maxLength: 320 };
  const [v, setV] = useState<typeof blank | null>(null);
  const r = useRunner(() => { qc.invalidateQueries({ queryKey: ['msg-templates'] }); setV(null); });
  return (
    <Section title="Message templates" action={<Button onClick={() => setV(blank)}>New template</Button>}>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>Placeholders: {'{app}'}, {'{title}'}, {'{body}'}, {'{link}'}. Messages longer than the limit are shortened.</Typography>
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      <Table size="small" aria-label="Message templates"><TableHead><TableRow><TableCell>Name</TableCell><TableCell>Channel</TableCell><TableCell>Text</TableCell><TableCell>Limit</TableCell><TableCell /></TableRow></TableHead>
        <TableBody>{(q.data ?? []).map((t) => <TableRow key={t.id}><TableCell>{t.name}</TableCell><TableCell>{channels[t.channel]}</TableCell><TableCell>{t.pushTitle ? <b>{t.pushTitle} · </b> : null}{t.text}</TableCell><TableCell>{t.maxLength}</TableCell>
          <TableCell><Button size="small" onClick={() => setV({ ...t, pushTitle: t.pushTitle ?? '' })}>Edit</Button></TableCell></TableRow>)}</TableBody></Table>
      {v && <Stack gap={2} sx={{ mt: 2, maxWidth: 640 }}>
        <TextField label="Name" value={v.name} onChange={(e) => setV({ ...v, name: e.target.value })} />
        <TextField select label="Channel" value={v.channel} onChange={(e) => setV({ ...v, channel: Number(e.target.value) })}>{channels.map((c, i) => <MenuItem key={c} value={i}>{c}</MenuItem>)}</TextField>
        {v.channel === 1 && <TextField label="Push title" value={v.pushTitle} onChange={(e) => setV({ ...v, pushTitle: e.target.value })} />}
        <TextField multiline minRows={2} label="Text" value={v.text} onChange={(e) => setV({ ...v, text: e.target.value })} />
        <TextField type="number" label="Maximum length" value={v.maxLength} onChange={(e) => setV({ ...v, maxLength: Number(e.target.value) })} />
        <Stack direction="row" gap={1}><Button variant="contained" disabled={r.busy} onClick={() => r.run(() => { const body = { name: v.name, channel: v.channel, text: v.text, pushTitle: v.pushTitle || null, maxLength: v.maxLength }; return v.id ? put(`/api/messaging/templates/${v.id}`, body) : post('/api/messaging/templates', body); })}>Save template</Button><Button onClick={() => setV(null)}>Cancel</Button></Stack>
      </Stack>}
    </Section>
  );
}

function DeliveriesTab() {
  const [provider, setProvider] = useState(''); const [state, setState] = useState('');
  const p = useQuery({ queryKey: ['msg-providers'], queryFn: () => get<ProviderRow[]>('/api/messaging/providers') });
  const q = useQuery({ queryKey: ['msg-deliveries', provider, state], queryFn: () => get<DeliveryRow[]>(`/api/messaging/deliveries?${new URLSearchParams({ ...(provider ? { providerId: provider } : {}), ...(state ? { state } : {}) })}`), refetchInterval: 15000 });
  const name = (id: string) => p.data?.find((x) => x.id === id)?.name ?? '';
  return (
    <>
      <Stack direction="row" gap={2} sx={{ mb: 2 }}>
        <TextField select size="small" label="Provider" value={provider} onChange={(e) => setProvider(e.target.value)} sx={{ minWidth: 220 }}><MenuItem value="">All</MenuItem>{(p.data ?? []).map((x) => <MenuItem key={x.id} value={x.id}>{x.name}</MenuItem>)}</TextField>
        <TextField select size="small" label="State" value={state} onChange={(e) => setState(e.target.value)} sx={{ minWidth: 160 }}><MenuItem value="">All</MenuItem>{states.map((s, i) => <MenuItem key={s} value={String(i)}>{s}</MenuItem>)}</TextField>
      </Stack>
      <Table size="small" aria-label="Delivery log"><TableHead><TableRow><TableCell>Created</TableCell><TableCell>Provider</TableCell><TableCell>To</TableCell><TableCell>Message</TableCell><TableCell>State</TableCell><TableCell>Attempts</TableCell><TableCell>Detail</TableCell></TableRow></TableHead>
        <TableBody>{(q.data ?? []).map((d) => <TableRow key={d.id}>
          <TableCell>{fmtDateTime(d.createdAt)}</TableCell><TableCell>{name(d.providerId)}{d.isTest ? <Chip size="small" label="Test" sx={{ ml: 1 }} /> : null}</TableCell><TableCell>{d.maskedAddress}</TableCell>
          <TableCell sx={{ maxWidth: 360 }}>{d.text}</TableCell><TableCell><Chip size="small" color={stateColor[d.state]} label={states[d.state]} /></TableCell><TableCell>{d.attempts}</TableCell>
          <TableCell>{d.failureReason ?? ''}{d.nextAttemptAt ? ` · retry ${fmtDateTime(d.nextAttemptAt)}` : ''}{d.deliveredAt ? `Delivered ${fmtDateTime(d.deliveredAt)}` : d.sentAt ? `Sent ${fmtDateTime(d.sentAt)}` : ''}</TableCell>
        </TableRow>)}</TableBody></Table>
      {q.data?.length === 0 && <Typography color="text.secondary" sx={{ mt: 1 }}>No messages yet.</Typography>}
    </>
  );
}

/** SMS and push providers, templates and the delivery log (api.admin). */
export function MessagingAdmin() {
  const [tab, setTab] = useState('providers');
  return (
    <ModuleWorkspace module={mod('platform')} crumbs={[{ label: 'Platform', to: '/platform' }, { label: 'SMS and push' }]} title="SMS and push" tabs={[{ value: 'providers', label: 'Providers' }, { value: 'templates', label: 'Templates' }, { value: 'log', label: 'Delivery log' }]} tab={tab} onTab={setTab}>
      {tab === 'providers' && <ProvidersTab />}
      {tab === 'templates' && <TemplatesTab />}
      {tab === 'log' && <DeliveriesTab />}
    </ModuleWorkspace>
  );
}

/** Each person opts in to SMS or push copies of their notifications. */
export function MyMessaging() {
  const qc = useQueryClient();
  const q = useQuery({ queryKey: ['my-messaging'], queryFn: () => get<SubscriptionRow[]>('/api/messaging/me') });
  const [v, setV] = useState({ channel: 0, address: '', label: '' });
  const [consent, setConsent] = useState(false);
  const r = useRunner(() => { qc.invalidateQueries({ queryKey: ['my-messaging'] }); setV({ channel: 0, address: '', label: '' }); setConsent(false); });
  return (
    <Box sx={{ maxWidth: 1000, mx: 'auto', p: 4 }}>
      <Typography variant="h1" sx={{ mb: 2 }}>Text and push notifications</Typography>
      <Typography color="text.secondary" sx={{ mb: 2 }}>Get a copy of your notifications by text message or on a device. Your organization decides which notifications are sent; message and data rates may apply. You can opt out at any time.</Typography>
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      <Paper sx={{ p: 2.5, mb: 3 }}>
        <Stack direction={{ xs: 'column', md: 'row' }} gap={2}>
          <TextField select label="Channel" value={v.channel} onChange={(e) => setV({ ...v, channel: Number(e.target.value) })} sx={{ minWidth: 140 }}>{channels.map((c, i) => <MenuItem key={c} value={i}>{c}</MenuItem>)}</TextField>
          <TextField fullWidth label={v.channel === 0 ? 'Mobile number (+15551234567)' : 'Device token'} value={v.address} onChange={(e) => setV({ ...v, address: e.target.value })} />
          <TextField label="Label" value={v.label} onChange={(e) => setV({ ...v, label: e.target.value })} placeholder="Work phone" />
        </Stack>
        <FormControlLabel sx={{ mt: 1 }} control={<Checkbox checked={consent} onChange={(e) => setConsent(e.target.checked)} />} label="I agree to receive notification messages at this address." />
        <Box><Button variant="contained" disabled={!consent || !v.address.trim() || r.busy} onClick={() => r.run(() => post('/api/messaging/me', { channel: v.channel, address: v.address.trim(), label: v.label || null }))}>Opt in</Button></Box>
      </Paper>
      <Table size="small" aria-label="My subscriptions"><TableHead><TableRow><TableCell>Channel</TableCell><TableCell>Address</TableCell><TableCell>Label</TableCell><TableCell>Status</TableCell><TableCell /></TableRow></TableHead>
        <TableBody>{(q.data ?? []).map((s) => <TableRow key={s.id}><TableCell>{channels[s.channel]}</TableCell><TableCell>{s.address}</TableCell><TableCell>{s.label}</TableCell>
          <TableCell>{s.optedOutAt ? `Opted out ${fmtDateTime(s.optedOutAt)}` : `Opted in ${fmtDateTime(s.optedInAt)}`}</TableCell>
          <TableCell>{!s.optedOutAt && <Button size="small" color="error" onClick={() => r.run(() => post(`/api/messaging/me/${s.id}/opt-out`))}>Opt out</Button>}</TableCell></TableRow>)}</TableBody></Table>
      {q.data?.length === 0 && <Typography color="text.secondary" sx={{ mt: 1 }}>You have not opted in to any text or push notifications.</Typography>}
    </Box>
  );
}
