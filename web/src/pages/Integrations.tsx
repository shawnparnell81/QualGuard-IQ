import { useState } from 'react';
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Chip, MenuItem, Paper, Stack, Switch, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { get, post, put } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, fmtDateTime } from '../components/Status';
import { Facts, Section, StepDialog, useRunner, useSites } from '../components/Kit';
import { mono } from '../theme';

const mod = modules.find((m) => m.key === 'integrations')!;
export const erpEntities = ['Item', 'ItemRevision', 'Bom', 'Customer', 'Supplier', 'WorkCenter', 'Employee', 'ProductionOrder', 'Routing', 'PurchaseOrder', 'Receipt', 'Lot', 'Serial', 'InventoryBalance', 'ProductionOrderLine', 'MachineCenter', 'ItemLedgerEntry', 'CapacityLedgerEntry', 'OutputJournalLine', 'ConsumptionJournalLine'];
const syncMode = ['Disabled', 'Auto', 'Approval required'];
const conflict = ['ERP wins', 'QMS wins', 'Manual'];
const runStatus = ['Running', 'Succeeded', 'Failed', 'PartiallySucceeded'];
export const changeStatus = ['Pending', 'PendingApproval', 'Applied', 'Rejected', 'Failed', 'Superseded'];
const systems: [string, string][] = [['NetSuite', 'Oracle NetSuite'], ['Sap', 'SAP'], ['Oracle', 'Oracle Fusion Cloud SCM'], ['Dynamics', 'Microsoft Dynamics 365'], ['Acumatica', 'Acumatica'], ['Infor', 'Infor'], ['Epicor', 'Epicor Kinetic'], ['Plex', 'Plex Smart Manufacturing'], ['DelmiaWorks', 'DELMIAworks (IQMS)'], ['BcManufacturing', 'Business Central manufacturing'], ['CustomRest', 'Generic REST'], ['CsvExchange', 'CSV exchange']];
/** Wire values of the server's ErpSystem enum. */
const sysCode: Record<string, number> = { NetSuite: 0, Sap: 1, Oracle: 2, Dynamics: 3, Acumatica: 4, Infor: 5, Epicor: 6, Plex: 7, DelmiaWorks: 8, CustomRest: 20, CsvExchange: 21 };
const sysLabel = (s: string) => systems.find(([k]) => k === s)?.[1] ?? s;
const mnum = (x: { getValue: () => unknown }) => <span style={{ fontFamily: mono }}>{String(x.getValue() ?? '')}</span>;

interface Unverified { topic: string; detail: string; source: string }
interface Caps { edition: string; read: number[]; write: number[]; delta: boolean; auth: string; api: string; paging: string; rateLimits: string; events: string; defaultConcurrency: number; defaultRequestsPerMinute: number; doc: string }
interface Adapter { system: string; editions: { edition: string; capabilities: Caps; unverified: Unverified[]; setupNotes: string }[] }
interface Conn { id: string; name: string; system: string; edition: string; baseUrl: string; account?: string; company?: string; clientId?: string; username?: string; options: Record<string, unknown>; hasSecret: boolean; hasSecondarySecret: boolean; secretSetAt?: string; enabled: boolean; allowWrites: boolean; maxConcurrency: number; requestsPerMinute: number; inventorySystemOfRecord: boolean; setupNotes: string; adminNotes?: string; lastTestAt?: string; lastTestOk?: boolean; lastTestMessage?: string; environment?: string; siteId: string; departmentId?: string }
interface Mapping { id: string; entity: number; direction: number; mode: number; conflict: number; cron: string; nextRunAt?: string; fieldMap: string; validationRules: string; version: number }
interface Run { id: string; entity: number; startedAt: string; finishedAt?: string; status: number; read: number; created: number; updated: number; unchanged: number; queued: number; rejected: number; requests: number; error?: string; trigger: string }
interface Change { id: string; number: string; connectionId: string; entity: number; externalId: string; action: string; before?: string; after: string; validationErrors?: string; status: number; localType?: string; localId?: string; decisionNote?: string; appliedAt?: string; createdAt: string }

const environments = ['Sandbox', 'Test', 'Production'];
const gateNames: Record<number, string> = { 1: 'Connection', 2: 'Authentication', 3: 'Read', 4: 'Write', 5: 'Production approval', 6: 'Assumption' };
const gateColor = (s: string): 'success' | 'error' | 'default' | 'warning' => (s === 'Passed' || s === 'Confirmed' ? 'success' : s === 'Failed' || s === 'Rejected' ? 'error' : s === 'Not applicable' ? 'default' : 'warning');
interface Validation { environment: number; fixtureTested: string; gates: { gate: number; status: string; at?: string; by?: string; environment?: number; notes?: string; required: boolean }[];
  assumptions: { topic: string; detail: string; source: string; key: boolean; status: string; at?: string; by?: string; notes?: string }[]; supportedEntities: number[]; unsupportedEntities: number[];
  checklist: { step: string; text: string; gate?: number }[]; canEnable: boolean; blockers: string[]; productionApproved: boolean; lastTestAt?: string; lastTestedBy?: string; disclaimer: string }

/** Live-validation gates for the current configuration, unverified vendor assumptions and the guided checklist. */
function ValidationPanel({ id, edit }: { id: string; edit: boolean }) {
  const qc = useQueryClient();
  const { can } = useAuth();
  const q = useQuery({ queryKey: ['erp-validation', id], queryFn: () => get<Validation>(`/api/integrations/${id}/validation`) });
  const [dlg, setDlg] = useState<null | { topic: string } | 'approve'>(null);
  const refresh = () => { qc.invalidateQueries({ queryKey: ['erp-validation', id] }); qc.invalidateQueries({ queryKey: ['connection', id] }); };
  const v = q.data;
  if (!v) return null;
  return (
    <Stack gap={2} data-testid="erp-validation">
      <Alert severity={v.canEnable ? 'success' : 'warning'} data-testid="erp-gate-summary">{v.canEnable ? 'The validation gate passes for the current configuration.' : <>Cannot be enabled yet: {v.blockers.join(' ')}</>}</Alert>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2 }}>
        <Section title={`Gates · ${environments[v.environment]}`} action={can('integrations.approve') && environments[v.environment] === 'Production' && !v.productionApproved && <Button size="small" variant="contained" onClick={() => setDlg('approve')}>Approve for production</Button>}>
          <Table size="small"><TableBody>
            <TableRow><TableCell>Fixture tested</TableCell><TableCell><Chip size="small" variant="outlined" label="Fixtures only" /></TableCell><TableCell><Typography variant="caption">{v.fixtureTested}</Typography></TableCell></TableRow>
            {v.gates.map((g) => <TableRow key={g.gate} data-testid="erp-gate"><TableCell>{gateNames[g.gate]}{g.required && <Typography component="span" variant="caption" color="text.secondary"> · required</Typography>}</TableCell>
              <TableCell><Chip size="small" color={gateColor(g.status)} label={g.status} /></TableCell>
              <TableCell><Typography variant="caption">{g.at ? `${fmtDateTime(g.at)} · ${g.by ?? ''}${g.environment != null ? ` · ${environments[g.environment]}` : ''}` : ''}</Typography><Typography variant="caption" display="block" color="text.secondary">{g.notes}</Typography></TableCell></TableRow>)}
          </TableBody></Table>
          <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: 1 }}>{v.disclaimer}</Typography>
        </Section>
        <Section title="Entities">
          <Facts items={[['Supported (read)', v.supportedEntities.map((e) => erpEntities[e]).join(', ') || 'None'], ['Not supported', v.unsupportedEntities.map((e) => erpEntities[e]).join(', ') || 'None'], ['Writes', 'None (read-only adapters)']]} />
        </Section>
      </Box>
      <Section title={`Unverified assumptions (${v.assumptions.filter((a) => a.status !== 'Confirmed').length} open)`}>
        <Table size="small"><TableBody>{v.assumptions.map((a) => <TableRow key={a.topic} data-testid="erp-assumption">
          <TableCell sx={{ width: 220 }}><Typography variant="body2" sx={{ fontWeight: 600 }}>{a.topic}</Typography>{a.key && <Chip size="small" variant="outlined" color="warning" label="Key — needed for production" sx={{ mt: 0.5 }} />}</TableCell>
          <TableCell><Typography variant="body2" color="text.secondary">{a.detail}</Typography>{a.notes && <Typography variant="caption" display="block">{a.status} by {a.by} {fmtDateTime(a.at)}: {a.notes}</Typography>}</TableCell>
          <TableCell sx={{ width: 120 }}><Chip size="small" color={gateColor(a.status)} label={a.status} /></TableCell>
          <TableCell sx={{ width: 100 }}>{edit && <Button size="small" onClick={() => setDlg({ topic: a.topic })}>Record</Button>}</TableCell></TableRow>)}
          {v.assumptions.length === 0 && <TableRow><TableCell><Typography color="text.secondary">Nothing flagged.</Typography></TableCell></TableRow>}</TableBody></Table>
      </Section>
      <Section title="Live-validation checklist">
        <Box component="ol" sx={{ m: 0, pl: 2.5 }}>{v.checklist.map((c) => { const g = c.gate ? v.gates.find((x) => x.gate === c.gate) : undefined;
          return <li key={c.step}><Typography variant="body2"><b>{c.step}.</b> {c.text} {g && <Chip size="small" color={gateColor(g.status)} label={g.status} sx={{ ml: 0.5 }} />}</Typography></li>; })}</Box>
      </Section>
      <StepDialog open={!!dlg && typeof dlg === 'object'} title={`Record: ${(dlg as { topic: string } | null)?.topic ?? ''}`} confirm="Record" onClose={() => setDlg(null)}
        intro="Confirm only what you checked against the customer's system after a passing read test. Say what you checked and where."
        inputs={[{ name: 'result', label: 'Result', type: 'select', options: [[3, 'Confirmed'], [4, 'Rejected (does not hold)'], [5, 'Withdraw earlier result']], initial: '3' }, { name: 'notes', label: 'Notes', type: 'multiline' }]}
        onConfirm={async (x) => { await post(`/api/integrations/${id}/validation/assumptions`, { topic: (dlg as { topic: string }).topic, result: Number(x.result), notes: x.notes }); refresh(); }} />
      <StepDialog open={dlg === 'approve'} title="Approve for production" confirm="Approve" onClose={() => setDlg(null)}
        intro="You confirm the gates passed against the production system and the key assumptions were checked. You cannot approve a test you ran yourself."
        inputs={[{ name: 'notes', label: 'Approval notes', type: 'multiline' }]}
        onConfirm={async (x) => { await post(`/api/integrations/${id}/validation/production-approval`, { notes: x.notes }); refresh(); }} />
    </Stack>
  );
}

const useAdapters = () => useQuery({ queryKey: ['adapters'], queryFn: () => get<Adapter[]>('/api/integrations/adapters') });

export function ConnectionList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const q = useQuery({ queryKey: ['connections'], queryFn: () => get<Conn[]>('/api/integrations') });
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Connections' }]} title="ERP & MES connections"
      actions={can('integrations.edit') ? <Button variant="contained" component={RouterLink} to="/integrations/new">New connection</Button> : undefined}>
      <Alert severity="info" sx={{ mb: 2 }}>Connections start switched off and read-only. Synced changes wait in the change queue for approval before they touch quality records.</Alert>
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(300px, 1fr))', gap: 1.5 }}>
        {(q.data ?? []).map((c) => <Paper key={c.id} onClick={() => nav(`/integrations/${c.id}`)} sx={{ p: 2, cursor: 'pointer', borderTop: 4, borderTopColor: c.enabled ? 'primary.main' : 'divider', '&:hover': { bgcolor: '#FAF8F3' } }}>
          <Stack direction="row" justifyContent="space-between" alignItems="center"><Typography variant="h3">{c.name}</Typography><Chip size="small" color={c.enabled ? 'success' : 'default'} label={c.enabled ? 'On' : 'Off'} /></Stack>
          <Typography variant="body2" color="text.secondary">{sysLabel(c.system)} · {c.edition}</Typography>
          <Typography variant="caption" sx={{ fontFamily: mono, display: 'block', mt: 1, overflow: 'hidden', textOverflow: 'ellipsis' }}>{c.baseUrl || '—'}</Typography>
          <Stack direction="row" gap={1} sx={{ mt: 1 }}>
            <Chip size="small" variant="outlined" label={c.hasSecret ? 'Secret set' : 'No secret'} color={c.hasSecret ? 'default' : 'warning'} />
            {c.lastTestAt && <Chip size="small" variant="outlined" color={c.lastTestOk ? 'success' : 'error'} label={c.lastTestOk ? 'Test passed' : 'Test failed'} />}
            <Chip size="small" variant="outlined" label={c.environment ?? 'Sandbox'} />
          </Stack>
        </Paper>)}
      </Box>
      {q.data && q.data.length === 0 && <Typography color="text.secondary" sx={{ mt: 2 }}>No connections yet.</Typography>}
    </ModuleWorkspace>
  );
}

function ConnectionForm({ initial, onSaved }: { initial?: Conn; onSaved: (id: string) => void }) {
  const adapters = useAdapters();
  const sites = useSites();
  const [v, setV] = useState({ name: initial?.name ?? '', system: initial?.system ?? 'NetSuite', edition: initial?.edition ?? '', baseUrl: initial?.baseUrl ?? '', account: initial?.account ?? '', company: initial?.company ?? '', clientId: initial?.clientId ?? '', username: initial?.username ?? '',
    options: JSON.stringify(initial?.options ?? {}, null, 2), maxConcurrency: String(initial?.maxConcurrency ?? ''), requestsPerMinute: String(initial?.requestsPerMinute ?? ''), adminNotes: initial?.adminNotes ?? '', siteId: initial?.siteId ?? '', environment: initial?.environment ?? 'Sandbox' });
  const [error, setError] = useState<string | null>(null);
  const set = (k: string) => (e: { target: { value: string } }) => setV({ ...v, [k]: e.target.value });
  const ad = adapters.data?.find((a) => a.system === v.system);
  const ed = ad?.editions.find((e) => e.edition === v.edition) ?? ad?.editions[0];
  return (
    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '1fr 380px' }, gap: 2 }}>
      <Paper sx={{ p: 3, display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2, alignSelf: 'start' }}>
        {error && <Alert severity="error" sx={{ gridColumn: '1 / -1' }}>{error}</Alert>}
        <TextField label="Name" value={v.name} onChange={set('name')} sx={{ gridColumn: '1 / -1' }} />
        <TextField select label="System" value={v.system} disabled={!!initial} onChange={(e) => setV({ ...v, system: e.target.value, edition: '' })}>{systems.map(([k, l]) => <MenuItem key={k} value={k}>{l}</MenuItem>)}</TextField>
        <TextField select label="Edition" value={ed?.edition ?? ''} onChange={set('edition')}>{(ad?.editions ?? []).map((e) => <MenuItem key={e.edition} value={e.edition}>{e.edition}</MenuItem>)}</TextField>
        <TextField label="Base URL" value={v.baseUrl} onChange={set('baseUrl')} sx={{ gridColumn: '1 / -1' }} helperText="https only" />
        <TextField label="Account / tenant" value={v.account} onChange={set('account')} />
        <TextField label="Company / database" value={v.company} onChange={set('company')} />
        <TextField label="Client id" value={v.clientId} onChange={set('clientId')} />
        <TextField label="User name" value={v.username} onChange={set('username')} />
        <TextField label="Max concurrency" type="number" value={v.maxConcurrency} onChange={set('maxConcurrency')} helperText={ed ? `Default ${ed.capabilities.defaultConcurrency}` : undefined} />
        <TextField label="Requests per minute" type="number" value={v.requestsPerMinute} onChange={set('requestsPerMinute')} helperText={ed ? `Default ${ed.capabilities.defaultRequestsPerMinute}` : undefined} />
        <TextField label="Options (JSON)" multiline minRows={4} value={v.options} onChange={set('options')} sx={{ gridColumn: '1 / -1', '& textarea': { fontFamily: mono, fontSize: 12.5 } }} />
        <TextField select label="Site" value={v.siteId} onChange={set('siteId')}>{(sites.data ?? []).filter((s) => !s.isArchived).map((s) => <MenuItem key={s.id} value={s.id}>{s.code} — {s.name}</MenuItem>)}</TextField>
        <TextField label="Admin notes" value={v.adminNotes} onChange={set('adminNotes')} />
        <TextField select label="Environment" value={v.environment} onChange={set('environment')} helperText="Changing it puts every validation gate back to not run.">{environments.map((e) => <MenuItem key={e} value={e}>{e}</MenuItem>)}</TextField>
        <Stack direction="row" gap={1} sx={{ gridColumn: '1 / -1' }}>
          <Button variant="contained" onClick={async () => {
            setError(null);
            let options: unknown;
            try { options = JSON.parse(v.options || '{}'); } catch { setError('Options must be valid JSON.'); return; }
            const body = { name: v.name, system: sysCode[v.system], edition: ed?.edition ?? null, baseUrl: v.baseUrl || null, account: v.account || null, company: v.company || null, clientId: v.clientId || null, username: v.username || null, options,
              maxConcurrency: v.maxConcurrency ? Number(v.maxConcurrency) : null, requestsPerMinute: v.requestsPerMinute ? Number(v.requestsPerMinute) : null, inventorySystemOfRecord: initial?.inventorySystemOfRecord ?? false, adminNotes: v.adminNotes || null, siteId: v.siteId, departmentId: null, environment: environments.indexOf(v.environment) };
            try { const r = initial ? await put<Conn>(`/api/integrations/${initial.id}`, body) : await post<Conn>('/api/integrations', body); onSaved(r.id); }
            catch (e) { setError((e as Error).message); }
          }}>{initial ? 'Save settings' : 'Create connection (off)'}</Button>
        </Stack>
      </Paper>
      {ed && <AdapterNotes caps={ed.capabilities} unverified={ed.unverified} notes={ed.setupNotes} />}
    </Box>
  );
}

function AdapterNotes({ caps, unverified, notes }: { caps: Caps; unverified: Unverified[]; notes: string }) {
  return (
    <Stack gap={2}>
      <Section title="What this adapter does">
        <Facts items={[['Auth', caps.auth], ['API', caps.api], ['Reads', caps.read.map((e) => erpEntities[e]).join(', ')], ['Writes', caps.write.length ? caps.write.map((e) => erpEntities[e]).join(', ') : 'None (read-only)'], ['Delta', caps.delta ? 'Yes' : 'Full read + hash skip'], ['Paging', caps.paging], ['Rate limits', caps.rateLimits], ['Events', caps.events], ['Research', caps.doc]]} />
      </Section>
      {notes && <Section title="Setup notes"><Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>{notes}</Typography></Section>}
      <Paper sx={{ p: 2, borderLeft: 4, borderLeftColor: 'warning.main' }}>
        <Typography variant="overline" color="warning.main">Unverified ({unverified.length})</Typography>
        {unverified.map((u) => <Box key={u.topic} sx={{ py: 0.75, borderTop: 1, borderColor: 'divider' }}><Typography variant="body2" sx={{ fontWeight: 600 }}>{u.topic}</Typography><Typography variant="body2" color="text.secondary">{u.detail}</Typography></Box>)}
        {unverified.length === 0 && <Typography variant="body2" color="text.secondary">Nothing flagged.</Typography>}
      </Paper>
    </Stack>
  );
}

export function ConnectionNew() {
  const nav = useNavigate();
  return <ModuleWorkspace module={mod} crumbs={[{ label: 'Connections', to: '/integrations' }, { label: 'New' }]} title="New connection"><ConnectionForm onSaved={(id) => nav(`/integrations/${id}`)} /></ModuleWorkspace>;
}

export function AdapterCatalog() {
  const q = useAdapters();
  const [sel, setSel] = useState('NetSuite');
  const a = q.data?.find((x) => x.system === sel);
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Supported systems' }]} title="Supported systems"
      tabs={(q.data ?? []).map((x) => ({ label: sysLabel(x.system), value: x.system }))} tab={sel} onTab={setSel}>
      <Stack gap={3}>{(a?.editions ?? []).map((e) => <Box key={e.edition}><Typography variant="h3" sx={{ mb: 1 }}>{sysLabel(sel)} · {e.edition}</Typography><AdapterNotes caps={e.capabilities} unverified={e.unverified} notes={e.setupNotes} /></Box>)}</Stack>
    </ModuleWorkspace>
  );
}

export function ConnectionDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const { can } = useAuth();
  const [p, setP] = useSearchParams();
  const tab = p.get('tab') ?? 'overview';
  const [dlg, setDlg] = useState<'secret' | 'csv' | null>(null);
  const [csvEntity, setCsvEntity] = useState(0);
  const [test, setTest] = useState<{ ok: boolean; message: string } | null>(null);
  const c = useQuery({ queryKey: ['connection', id], queryFn: () => get<Conn>(`/api/integrations/${id}`) });
  const adapters = useAdapters();
  const maps = useQuery({ queryKey: ['mappings', id], queryFn: () => get<Mapping[]>(`/api/integrations/${id}/mappings`), enabled: tab === 'mappings' });
  const runs = useQuery({ queryKey: ['runs', id], queryFn: () => get<Run[]>(`/api/integrations/${id}/runs`), enabled: tab === 'runs' });
  const refresh = () => { qc.invalidateQueries({ queryKey: ['connection', id] }); qc.invalidateQueries({ queryKey: ['mappings', id] }); qc.invalidateQueries({ queryKey: ['runs', id] }); qc.invalidateQueries({ queryKey: ['erp-validation', id] }); };
  const { error, busy, run } = useRunner(refresh);
  const [lastRun, setLastRun] = useState<string | null>(null);
  const x = c.data;
  if (!x) return <ModuleWorkspace module={mod} crumbs={[{ label: 'Connections', to: '/integrations' }]} title="Loading…">{c.error ? <Alert severity="error">{(c.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  const ed = adapters.data?.find((a) => a.system === x.system)?.editions.find((e) => e.edition === x.edition);
  const edit = can('integrations.edit');
  const readable = ed?.capabilities.read ?? [];
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Connections', to: '/integrations' }, { label: x.name }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span>{x.name}</span><Chip size="small" color={x.enabled ? 'success' : 'default'} label={x.enabled ? 'On' : 'Off'} /><Typography color="text.secondary">{sysLabel(x.system)} · {x.edition}</Typography></Stack>}
      tabs={[{ label: 'Overview', value: 'overview' }, { label: 'Settings', value: 'settings' }, { label: 'Mappings', value: 'mappings' }, { label: 'Sync runs', value: 'runs' }, { label: 'Live validation', value: 'validation' }, { label: 'Unverified', value: 'notes' }]} tab={tab} onTab={(t) => setP({ tab: t })}
      actions={edit ? <Stack direction="row" gap={1} alignItems="center">
        <Button variant="outlined" onClick={() => setDlg('secret')}>{x.hasSecret ? 'Replace secret' : 'Set secret'}</Button>
        <Button variant="outlined" disabled={busy} onClick={() => run(async () => setTest(await post<{ ok: boolean; message: string }>(`/api/integrations/${id}/test`)))}>Test connection</Button>
        <Stack direction="row" alignItems="center"><Switch checked={x.enabled} onChange={(e) => run(() => post(`/api/integrations/${id}/enabled`, { enabled: e.target.checked }))} slotProps={{ input: { 'aria-label': 'Enabled' } }} /><Typography variant="body2">{x.enabled ? 'Enabled' : 'Disabled'}</Typography></Stack>
      </Stack> : undefined}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {test && <Alert severity={test.ok ? 'success' : 'error'} sx={{ mb: 2 }} onClose={() => setTest(null)}>{test.message}</Alert>}
      {lastRun && <Alert severity="info" sx={{ mb: 2 }} onClose={() => setLastRun(null)}>{lastRun}</Alert>}
      {tab === 'overview' && <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2 }}>
        <Facts items={[['Base URL', <span style={{ fontFamily: mono }}>{x.baseUrl || '—'}</span>], ['Account / tenant', x.account ?? '—'], ['Company', x.company ?? '—'], ['Client id', x.clientId ?? '—'], ['User', x.username ?? '—'],
          ['Secret', x.hasSecret ? `Set ${fmtDateTime(x.secretSetAt)} (never shown)` : 'Not set'], ['Second secret', x.hasSecondarySecret ? 'Set' : '—'], ['Writes', x.allowWrites ? 'Allowed' : 'Read-only'], ['Throttle', `${x.maxConcurrency} parallel · ${x.requestsPerMinute}/min`],
          ['Last test', x.lastTestAt ? `${fmtDateTime(x.lastTestAt)} · ${x.lastTestOk ? 'passed' : 'failed'}` : 'Never'], ['Test message', x.lastTestMessage ?? '—']]} />
        <Section title="Change queue" action={<Button size="small" component={RouterLink} to={`/integrations/changes?connectionId=${x.id}`}>Open</Button>}>
          <Typography variant="body2" color="text.secondary">New and changed ERP records are queued here. Submit them for approval, and they apply once approved.</Typography>
        </Section>
      </Box>}
      {tab === 'settings' && <ConnectionForm initial={x} onSaved={refresh} />}
      {tab === 'mappings' && <Paper variant="outlined"><Table size="small">
        <TableHead><TableRow><TableCell>Entity</TableCell><TableCell>Mode</TableCell><TableCell>Conflicts</TableCell><TableCell>Schedule</TableCell><TableCell>Next run</TableCell><TableCell /></TableRow></TableHead>
        <TableBody>{readable.map((e) => { const m = maps.data?.find((y) => y.entity === e); return <MappingRow key={e} entity={e} m={m} edit={edit} csv={x.system === 'CsvExchange'}
          onSave={(body) => run(() => put(`/api/integrations/${id}/mappings/${erpEntities[e]}`, body))}
          onRun={() => run(async () => { const r = await post<{ status: number; read: number; created: number; updated: number; unchanged: number; queued: number; rejected: number; error?: string }>(`/api/integrations/${id}/mappings/${erpEntities[e]}/run`);
            setLastRun(`${erpEntities[e]}: ${runStatus[r.status]} · read ${r.read}, queued ${r.queued}, unchanged ${r.unchanged}, rejected ${r.rejected}${r.error ? ` · ${r.error}` : ''}`); })}
          onCsv={() => { setCsvEntity(e); setDlg('csv'); }} />; })}
          {readable.length === 0 && <TableRow><TableCell colSpan={6}><Typography color="text.secondary">This edition documents no readable entities.</Typography></TableCell></TableRow>}
        </TableBody></Table></Paper>}
      {tab === 'runs' && <DataTable data={runs.data ?? []} empty="No sync runs yet."
        columns={[{ header: 'Started', accessorKey: 'startedAt', cell: (v) => fmtDateTime(v.getValue()) }, { header: 'Entity', accessorFn: (r) => erpEntities[r.entity] }, { header: 'Trigger', accessorKey: 'trigger' },
          { header: 'Status', accessorFn: (r) => runStatus[r.status], cell: (v) => <StatusChip status={v.getValue() === 'PartiallySucceeded' ? 'Conditional' : v.getValue()} /> },
          { header: 'Read / queued / same / rejected', accessorFn: (r) => `${r.read} / ${r.queued} / ${r.unchanged} / ${r.rejected}`, cell: mnum }, { header: 'Requests', accessorKey: 'requests' }, { header: 'Error', accessorKey: 'error' }]} />}
      {tab === 'validation' && <ValidationPanel id={x.id} edit={edit} />}
      {tab === 'notes' && ed && <AdapterNotes caps={ed.capabilities} unverified={ed.unverified} notes={x.setupNotes} />}
      <StepDialog open={dlg === 'secret'} title="Set secrets" confirm="Save" onClose={() => setDlg(null)} intro="Secrets are encrypted and can never be read back. Leave a field blank to keep the stored value."
        inputs={[{ name: 'secret', label: 'Secret / private key / API key / password', type: 'password' }, { name: 'secondary', label: 'Second secret (if the vendor needs one)', type: 'password' }]}
        onConfirm={async (v) => { await put(`/api/integrations/${id}/secrets`, { secret: v.secret || null, secondarySecret: v.secondary || null }); refresh(); }} />
      <StepDialog open={dlg === 'csv'} title={`Import ${erpEntities[csvEntity]} CSV`} confirm="Import" onClose={() => setDlg(null)} intro="Header row, then one row per record. Rows go to the change queue."
        inputs={[{ name: 'content', label: 'CSV content', type: 'multiline' }]}
        onConfirm={async (v) => { const r = await post<{ read: number; queued: number; rejected: number }>(`/api/integrations/${id}/mappings/${erpEntities[csvEntity]}/csv`, { content: v.content }); setLastRun(`CSV: read ${r.read}, queued ${r.queued}, rejected ${r.rejected}`); refresh(); }} />
    </ModuleWorkspace>
  );
}

function MappingRow({ entity, m, edit, csv, onSave, onRun, onCsv }: { entity: number; m?: Mapping; edit: boolean; csv: boolean; onSave: (b: unknown) => void; onRun: () => void; onCsv: () => void }) {
  const [mode, setMode] = useState(m?.mode ?? 0);
  const [cf, setCf] = useState(m?.conflict ?? 2);
  const [cron, setCron] = useState(m?.cron ?? '0 * * * *');
  const dirty = !m || mode !== m.mode || cf !== m.conflict || cron !== m.cron;
  return (
    <TableRow>
      <TableCell sx={{ fontWeight: 600 }}>{erpEntities[entity]}</TableCell>
      <TableCell><TextField select size="small" variant="standard" value={mode} disabled={!edit} onChange={(e) => setMode(Number(e.target.value))}>{syncMode.map((s, i) => <MenuItem key={s} value={i}>{s}</MenuItem>)}</TextField></TableCell>
      <TableCell><TextField select size="small" variant="standard" value={cf} disabled={!edit} onChange={(e) => setCf(Number(e.target.value))}>{conflict.map((s, i) => <MenuItem key={s} value={i}>{s}</MenuItem>)}</TextField></TableCell>
      <TableCell><TextField size="small" variant="standard" value={cron} disabled={!edit} onChange={(e) => setCron(e.target.value)} sx={{ '& input': { fontFamily: mono } }} /></TableCell>
      <TableCell>{fmtDateTime(m?.nextRunAt)}</TableCell>
      <TableCell><Stack direction="row" gap={0.5}>
        {edit && dirty && <Button size="small" variant="contained" onClick={() => onSave({ direction: 0, mode, conflict: cf, cron, fieldMap: null, validationRules: null })}>Save</Button>}
        {edit && m && !csv && <Button size="small" onClick={onRun}>Run now</Button>}
        {edit && m && csv && <Button size="small" onClick={onCsv}>Import CSV</Button>}
      </Stack></TableCell>
    </TableRow>
  );
}

function JsonDiff({ before, after }: { before?: string; after: string }) {
  const b = before ? (JSON.parse(before) as Record<string, unknown>) : {};
  const a = JSON.parse(after) as Record<string, unknown>;
  const keys = Array.from(new Set([...Object.keys(b), ...Object.keys(a)]));
  return <Table size="small"><TableHead><TableRow><TableCell>Field</TableCell><TableCell>Current</TableCell><TableCell>From ERP</TableCell></TableRow></TableHead>
    <TableBody>{keys.map((k) => { const ch = JSON.stringify(b[k]) !== JSON.stringify(a[k]); return <TableRow key={k} sx={{ bgcolor: ch ? '#FBF3E8' : undefined }}>
      <TableCell sx={{ fontFamily: mono }}>{k}</TableCell><TableCell sx={{ fontFamily: mono }}>{b[k] === undefined ? '—' : String(JSON.stringify(b[k]))}</TableCell><TableCell sx={{ fontFamily: mono, fontWeight: ch ? 600 : undefined }}>{a[k] === undefined ? '—' : String(JSON.stringify(a[k]))}</TableCell></TableRow>; })}</TableBody></Table>;
}

export function ChangeQueue() {
  const nav = useNavigate();
  const [p] = useSearchParams();
  const status = p.get('status') ?? '0';
  const cid = p.get('connectionId');
  const conns = useQuery({ queryKey: ['connections'], queryFn: () => get<Conn[]>('/api/integrations') });
  const q = useQuery({ queryKey: ['changes', status, cid], queryFn: () => get<Change[]>(`/api/integrations/changes?status=${status}${cid ? `&connectionId=${cid}` : ''}`) });
  const cname = (id: string) => conns.data?.find((c) => c.id === id)?.name ?? '';
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Change queue' }]} title={`Change queue · ${changeStatus[Number(status)]?.replace('PendingApproval', 'in approval') ?? ''}`}>
      <DataTable data={q.data ?? []} onRowClick={(r) => nav(`/integrations/changes/${r.id}`)} empty="Nothing in this view."
        columns={[{ header: 'Change', accessorKey: 'number', cell: mnum }, { header: 'Connection', accessorFn: (r) => cname(r.connectionId) }, { header: 'Entity', accessorFn: (r) => erpEntities[r.entity] },
          { header: 'Action', accessorKey: 'action', cell: (v) => <Chip size="small" variant="outlined" label={v.getValue()} color={v.getValue() === 'Create' ? 'info' : 'warning'} /> }, { header: 'External id', accessorKey: 'externalId', cell: mnum },
          { header: 'Problems', accessorKey: 'validationErrors' }, { header: 'Queued', accessorKey: 'createdAt', cell: (v) => fmtDateTime(v.getValue()) }]} />
    </ModuleWorkspace>
  );
}

export function ErpChangeDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const { can } = useAuth();
  const q = useQuery({ queryKey: ['change', id], queryFn: () => get<Change>(`/api/integrations/changes/${id}`) });
  const { error, busy, run } = useRunner(() => qc.invalidateQueries({ queryKey: ['change', id] }));
  const c = q.data;
  if (!c) return <ModuleWorkspace module={mod} crumbs={[{ label: 'Change queue', to: '/integrations/changes' }]} title="Loading…">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Change queue', to: '/integrations/changes' }, { label: c.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{c.number}</span><span>{c.action} {erpEntities[c.entity]}</span><StatusChip status={changeStatus[c.status]} /></Stack>}
      actions={<Stack direction="row" gap={1}>
        {can('integrations.edit') && c.status === 0 && !c.validationErrors && <Button variant="contained" disabled={busy} onClick={() => run(() => post(`/api/integrations/changes/${id}/submit`))}>Submit for approval</Button>}
        {can('integrations.edit') && c.status === 4 && <Button variant="outlined" disabled={busy} onClick={() => run(() => post(`/api/integrations/changes/${id}/retry`))}>Retry</Button>}
        {c.status === 1 && <Button component={RouterLink} to="/my-work">Go to approvals</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {c.validationErrors && <Alert severity="warning" sx={{ mb: 2 }}>{c.validationErrors}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '300px 1fr' }, gap: 2 }}>
        <Facts items={[['External id', <span style={{ fontFamily: mono }}>{c.externalId}</span>], ['Local record', c.localType && c.localId ? <RouterLink to={`/records/${c.localType}/${c.localId}`}>{c.localType}</RouterLink> : 'New'], ['Queued', fmtDateTime(c.createdAt)], ['Applied', fmtDateTime(c.appliedAt)], ['Decision', c.decisionNote ?? '—']]} />
        <Section title="Field changes"><JsonDiff before={c.before} after={c.after} /></Section>
      </Box>
    </ModuleWorkspace>
  );
}
