import { useState } from 'react';
import { Alert, Box, Button, Chip, MenuItem, Paper, Stack, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { get, post, put } from '../api';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, fmtDate, fmtDateTime } from '../components/Status';
import { Facts, Section, StepDialog, useRunner, useSites, useTypeLabel, useUsers } from '../components/Kit';
import { mono } from '../theme';
import { mod, num } from './Enterprise';

/* eslint-disable @typescript-eslint/no-explicit-any */
const continuityKinds = ['Backup verification', 'Disaster recovery test', 'Recovery audit', 'System readiness test'];
const continuityResults = ['Planned', 'Passed', 'PassedWithIssues', 'Failed'];
const channelKinds = ['Email', 'SMS gateway', 'Teams', 'Mobile push'];
const scopes = ['Site', 'Region', 'Corporate'];
const retentionActions = ['Archive', 'Dispose', 'Review'];

export function Part11Readiness() {
  const q = useQuery({ queryKey: ['part11'], queryFn: () => get<any>('/api/governance/part11') });
  const checks = useQuery({ queryKey: ['system-checks'], queryFn: () => get<any>('/api/governance/system-checks') });
  return (
    <ModuleWorkspace module={mod('governance')} crumbs={[{ label: 'Governance' }]} title="21 CFR Part 11 readiness">
      {q.error && <Alert severity="error">{(q.error as Error).message}</Alert>}
      {q.data && <Stack gap={1}>{q.data.checks.map((c: any) => (
        <Paper key={c.control} data-testid="p11" sx={{ p: 1.5, display: 'flex', gap: 2, alignItems: 'center', borderLeft: 4, borderColor: c.ok ? 'success.main' : 'warning.main' }}>
          <Typography sx={{ fontFamily: mono, minWidth: 260, fontWeight: 600 }}>{c.control}</Typography><Typography variant="body2" sx={{ flex: 1 }}>{c.evidence}</Typography><Chip size="small" color={c.ok ? 'success' : 'warning'} label={c.ok ? 'Evidence present' : 'Action needed'} />
        </Paper>))}
        {q.data.brokenSignatures.length > 0 && <Alert severity="error">{q.data.brokenSignatures.length} signature(s) failed re-computation.</Alert>}</Stack>}
      {checks.data && <Box sx={{ mt: 2.5 }}><Section title="System checks"><Facts items={Object.entries(checks.data).map(([k, v]) => [k, typeof v === 'object' ? JSON.stringify(v) : String(v)])} /></Section></Box>}
    </ModuleWorkspace>
  );
}

const retentionStatus = ['No policy', 'Retained', 'Due', 'On hold', 'Conflict', 'Decided'];
const classifications = ['Public', 'Internal', 'Confidential', 'Restricted'];
const blankPolicy = { name: '', recordType: '', module: '', productFamily: '', regulatoryCategory: '', siteId: '', confidentiality: '', priority: '100', retainYears: '10', action: '0' };

export function RetentionPage() {
  const qc = useQueryClient();
  const q = useQuery({ queryKey: ['retention'], queryFn: () => get<any[]>('/api/governance/retention/policies') });
  const types = useQuery({ queryKey: ['retention-types'], queryFn: () => get<string[]>('/api/governance/retention/record-types') });
  const modulesQ = useQuery({ queryKey: ['retention-modules'], queryFn: () => get<{ recordType: string; module: string }[]>('/api/governance/retention/modules') });
  const decisions = useQuery({ queryKey: ['retention-decisions'], queryFn: () => get<any[]>('/api/governance/retention/decisions') });
  const sites = useSites();
  const typeLabel = useTypeLabel();
  const [edit, setEdit] = useState<null | { id?: string; v: typeof blankPolicy }>(null); const [due, setDue] = useState<string | null>(null);
  const dueQ = useQuery({ queryKey: ['retention-due', due], enabled: !!due, queryFn: () => get<any[]>(`/api/governance/retention/policies/${due}/due`) });
  const versions = useQuery({ queryKey: ['retention-versions', due], enabled: !!due, queryFn: () => get<any[]>(`/api/governance/retention/policies/${due}/versions`) });
  const [tab, setTab] = useState('policies');
  const [run, setRun] = useState<{ recordType: string; siteId: string; status: string } | null>(null);
  const evalQ = useQuery({ queryKey: ['retention-eval', run], enabled: !!run, queryFn: () => get<any>(`/api/governance/retention/evaluate?${new URLSearchParams(Object.entries(run ?? {}).filter(([, x]) => x !== '') as [string, string][]).toString()}&pageSize=200`) });
  const [form, setForm] = useState({ recordType: '', siteId: '', status: '' });
  const siteName = (id?: string) => (id ? sites.data?.find((s) => s.id === id)?.name ?? '' : '');
  const filters = (r: any) => [r.module && `module ${r.module}`, r.productFamily && `family ${r.productFamily}`, r.regulatoryCategory && `category ${r.regulatoryCategory}`, r.siteId && `site ${siteName(r.siteId)}`,
    r.departmentId && 'department', r.customerId && 'customer', r.supplierId && 'supplier', r.confidentiality != null && classifications[r.confidentiality]].filter(Boolean).join(' · ') || 'All records';
  const modules = [...new Set((modulesQ.data ?? []).map((m) => m.module))];
  const save = async (v: typeof blankPolicy, id?: string) => {
    const body = { name: v.name, recordType: v.recordType || null, module: v.module || null, productFamily: v.productFamily || null, regulatoryCategory: v.regulatoryCategory || null, siteId: v.siteId || null,
      confidentiality: v.confidentiality === '' ? null : Number(v.confidentiality), priority: Number(v.priority || 100), retainYears: Number(v.retainYears), action: Number(v.action), isActive: true };
    if (id) await put(`/api/governance/retention/policies/${id}`, body); else await post('/api/governance/retention/policies', body);
    qc.invalidateQueries({ queryKey: ['retention'] }); qc.invalidateQueries({ queryKey: ['retention-versions'] }); setEdit(null);
  };
  return (
    <ModuleWorkspace module={mod('governance')} crumbs={[{ label: 'Governance', to: '/governance' }, { label: 'Retention' }]} title="Record retention"
      tabs={[{ label: 'Policies', value: 'policies' }, { label: 'Dry run', value: 'dryrun' }, { label: 'Decisions log', value: 'log' }]} tab={tab} onTab={setTab} actions={<Button variant="contained" onClick={() => setEdit({ v: blankPolicy })}>New policy</Button>}>
      <Alert severity="info" sx={{ mb: 2 }}>Retention never deletes records automatically. When several policies match a record, the lowest priority number wins, then the most specific policy; ties that disagree are flagged as conflicts and kept for review. Legal holds always override.</Alert>
      {tab === 'policies' && <DataTable data={q.data ?? []} onRowClick={(r) => setDue(r.id)} empty="No retention policies."
        columns={[{ header: 'Name', accessorKey: 'name' }, { header: 'Record type', accessorKey: 'recordType', cell: (x) => (x.getValue() ? typeLabel(x.getValue()) : 'Any') }, { header: 'Filters', id: 'f', accessorFn: filters },
          { header: 'Priority', accessorKey: 'priority', cell: num }, { header: 'Retain (years)', accessorKey: 'retainYears' }, { header: 'Then', accessorKey: 'action', cell: (x) => retentionActions[x.getValue()] },
          { header: 'Version', accessorKey: 'version', cell: num }, { header: 'Active', accessorKey: 'isActive', cell: (x) => (x.getValue() ? 'Yes' : 'No') },
          { header: '', id: 'e', cell: (x) => <Button size="small" onClick={(e) => { e.stopPropagation(); const r = x.row.original; setEdit({ id: r.id, v: { name: r.name, recordType: r.recordType ?? '', module: r.module ?? '', productFamily: r.productFamily ?? '', regulatoryCategory: r.regulatoryCategory ?? '', siteId: r.siteId ?? '', confidentiality: r.confidentiality == null ? '' : String(r.confidentiality), priority: String(r.priority), retainYears: String(r.retainYears), action: String(r.action) } }); }}>Edit</Button> }]} />}
      {tab === 'policies' && due && <Box sx={{ mt: 2, display: 'grid', gridTemplateColumns: '2fr 1fr', gap: 2 }}>
        <Section title={`Due for decision (${dueQ.data?.length ?? 0})`}><DataTable data={dueQ.data ?? []} empty="Nothing this policy governs is past its retention period." columns={[{ header: 'Record', accessorKey: 'number', cell: num }, { header: 'Type', accessorKey: 'recordType', cell: (x) => typeLabel(x.getValue()) }, { header: 'Created', accessorKey: 'createdAt', cell: (x) => fmtDate(x.getValue()) }]} /></Section>
        <Section title="Policy versions"><DataTable data={versions.data ?? []} empty="No versions." columns={[{ header: 'Version', accessorKey: 'version', cell: num }, { header: 'Saved', accessorKey: 'savedAt', cell: (x) => fmtDateTime(x.getValue()) },
          { header: '', id: 'r', cell: (x) => <Button size="small" onClick={() => { setTab('dryrun'); setRun({ policyId: due, version: String(x.row.original.version) } as any); }}>Reproduce</Button> }]} /></Section>
      </Box>}
      {tab === 'dryrun' && <>
        <Stack direction="row" spacing={1.5} sx={{ mb: 2 }} alignItems="center">
          <TextField select size="small" label="Record type" value={form.recordType} onChange={(e) => setForm({ ...form, recordType: e.target.value })} sx={{ width: 220 }}><MenuItem value="">All types</MenuItem>{(types.data ?? []).map((t) => <MenuItem key={t} value={t}>{typeLabel(t)}</MenuItem>)}</TextField>
          <TextField select size="small" label="Site" value={form.siteId} onChange={(e) => setForm({ ...form, siteId: e.target.value })} sx={{ width: 220 }}><MenuItem value="">All my sites</MenuItem>{(sites.data ?? []).map((s) => <MenuItem key={s.id} value={s.id}>{s.name}</MenuItem>)}</TextField>
          <TextField select size="small" label="Outcome" value={form.status} onChange={(e) => setForm({ ...form, status: e.target.value })} sx={{ width: 180 }}><MenuItem value="">Any</MenuItem>{retentionStatus.map((s, i) => <MenuItem key={s} value={String(i)}>{s}</MenuItem>)}</TextField>
          <Button variant="contained" onClick={() => setRun({ ...form })}>Run dry run</Button>
        </Stack>
        {evalQ.error && <Alert severity="error">{(evalQ.error as Error).message}</Alert>}
        {evalQ.data && <>
          <Stack direction="row" spacing={1} sx={{ mb: 1.5 }} data-testid="retention-summary">
            <Chip label={`${evalQ.data.evaluated} evaluated`} /><Chip color="warning" label={`${evalQ.data.due} due`} /><Chip label={`${evalQ.data.onHold} on hold`} /><Chip color={evalQ.data.conflicts ? 'error' : 'default'} label={`${evalQ.data.conflicts} conflicts`} /><Chip label={`${evalQ.data.noPolicy} without policy`} />
            {evalQ.data.truncated && <Chip color="warning" variant="outlined" label="Limited to the oldest 5,000 records — narrow the filters" />}
          </Stack>
          <DataTable data={evalQ.data.candidates as any[]} empty="No records match." columns={[{ header: 'Record', accessorKey: 'number', cell: num }, { header: 'Type', accessorKey: 'recordType', cell: (x) => typeLabel(x.getValue()) },
            { header: 'Created', accessorKey: 'createdAt', cell: (x) => fmtDate(x.getValue()) }, { header: 'Family', accessorKey: 'productFamily' }, { header: 'Category', accessorKey: 'regulatoryCategory' },
            { header: 'Outcome', accessorKey: 'status', cell: (x) => <StatusChip status={retentionStatus[x.getValue()]} /> }, { header: 'Policy', accessorFn: (r) => (r.policyName ? `${r.policyName} v${r.policyVersion}` : '—') },
            { header: 'Retain until', accessorKey: 'retainUntil', cell: (x) => fmtDate(x.getValue()) }, { header: 'Why', accessorKey: 'explanation' }]} />
        </>}
      </>}
      {tab === 'log' && <DataTable data={decisions.data ?? []} empty="No decisions recorded." columns={[{ header: 'Record', accessorKey: 'recordNumber', cell: num }, { header: 'Record type', accessorKey: 'recordType', cell: (x) => typeLabel(x.getValue()) }, { header: 'Decision', accessorKey: 'decision', cell: (x) => ['Archived', 'Disposal approved', 'Extended', 'Legal hold'][x.getValue()] }, { header: 'Policy version', accessorKey: 'policyVersion', cell: num }, { header: 'Reason', accessorKey: 'reason' }, { header: 'When', accessorKey: 'createdAt', cell: (x) => fmtDateTime(x.getValue()) }]} />}
      <StepDialog open={edit !== null} onClose={() => setEdit(null)} title={edit?.id ? 'Edit retention policy' : 'New retention policy'} confirm={edit?.id ? 'Save as new version' : 'Create'} key={edit?.id ?? 'new'}
        intro="Leave a filter empty to match any record. Filled filters must all match."
        inputs={[{ name: 'name', label: 'Name', initial: edit?.v.name }, { name: 'recordType', label: 'Record type', type: 'select', options: [['', 'Any type'], ...(types.data ?? []).map((t) => [t, typeLabel(t)] as [string, string])], initial: edit?.v.recordType },
          { name: 'module', label: 'Module', type: 'select', options: [['', 'Any module'], ...modules.map((m) => [m, m] as [string, string])], initial: edit?.v.module },
          { name: 'productFamily', label: 'Product family (code or name)', initial: edit?.v.productFamily }, { name: 'regulatoryCategory', label: 'Regulatory category', initial: edit?.v.regulatoryCategory },
          { name: 'siteId', label: 'Site (empty = every site)', type: 'site', permission: 'governance.edit', initial: edit?.v.siteId },
          { name: 'confidentiality', label: 'Confidentiality', type: 'select', options: [['', 'Any'], ...classifications.map((c, i) => [String(i), c] as [string, string])], initial: edit?.v.confidentiality },
          { name: 'priority', label: 'Priority (1 = highest)', initial: edit?.v.priority }, { name: 'retainYears', label: 'Retain for (years)', initial: edit?.v.retainYears },
          { name: 'action', label: 'Then', type: 'select', options: retentionActions.map((a, i) => [String(i), a]), initial: edit?.v.action }]}
        onConfirm={async (v) => save(v as typeof blankPolicy, edit?.id)} />
    </ModuleWorkspace>
  );
}

export function ContinuityPage() {
  const qc = useQueryClient();
  const sites = useSites();
  const q = useQuery({ queryKey: ['continuity'], queryFn: () => get<any[]>('/api/governance/continuity') });
  const [dlg, setDlg] = useState<null | 'new' | { result: string }>(null);
  const refresh = () => qc.invalidateQueries({ queryKey: ['continuity'] });
  return (
    <ModuleWorkspace module={mod('governance')} crumbs={[{ label: 'Governance', to: '/governance' }, { label: 'Business continuity' }]} title="Business continuity exercises" actions={<Button variant="contained" onClick={() => setDlg('new')}>Plan exercise</Button>}>
      <DataTable data={q.data ?? []} empty="No exercises planned."
        columns={[{ header: 'Number', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' }, { header: 'Kind', accessorKey: 'kind', cell: (x) => continuityKinds[x.getValue()] }, { header: 'Planned', accessorKey: 'plannedOn', cell: (x) => fmtDate(x.getValue()) },
          { header: 'Target RTO (min)', accessorKey: 'targetRecoveryMinutes' }, { header: 'Actual (min)', accessorKey: 'recoveryMinutes' }, { header: 'Result', accessorKey: 'result', cell: (x) => <StatusChip status={continuityResults[x.getValue()]} /> },
          { header: '', id: 'a', cell: (x) => x.row.original.result === 0 && <Button size="small" onClick={() => setDlg({ result: x.row.original.id })}>Record result</Button> }]} />
      <StepDialog open={dlg === 'new'} onClose={() => setDlg(null)} title="Plan exercise" confirm="Plan"
        inputs={[{ name: 'kind', label: 'Kind', type: 'select', options: continuityKinds.map((k, i) => [i, k]), initial: '0' }, { name: 'title', label: 'Title' }, { name: 'site', label: 'Site', type: 'select', options: (sites.data ?? []).map((s) => [s.id, `${s.code} — ${s.name}`]) }, { name: 'on', label: 'Planned on', type: 'date' }, { name: 'rto', label: 'Target recovery (minutes)' }]}
        onConfirm={async (v) => { await post('/api/governance/continuity', { kind: Number(v.kind), title: v.title, siteId: v.site, plannedOn: v.on, targetRecoveryMinutes: v.rto ? Number(v.rto) : null }); refresh(); }} />
      <StepDialog open={typeof dlg === 'object' && dlg !== null} onClose={() => setDlg(null)} title="Record result" confirm="Record" intro="System checks are captured automatically with the result."
        inputs={[{ name: 'result', label: 'Result', type: 'select', options: [[1, 'Passed'], [2, 'Passed with issues'], [3, 'Failed']], initial: '1' }, { name: 'rec', label: 'Recovery time (minutes)' }, { name: 'loss', label: 'Data loss (minutes)' }, { name: 'findings', label: 'Findings', type: 'multiline' }]}
        onConfirm={async (v) => { await post(`/api/governance/continuity/${(dlg as any).result}/result`, { result: Number(v.result), recoveryMinutes: v.rec ? Number(v.rec) : null, dataLossMinutes: v.loss ? Number(v.loss) : null, findings: v.findings }); refresh(); }} />
    </ModuleWorkspace>
  );
}

const clientKinds = ['API client', 'MES device'];
const clientStatusColor: Record<string, 'success' | 'default' | 'warning' | 'error'> = { Active: 'success', Disabled: 'default', Expired: 'warning', Revoked: 'error' };
const list = (s?: string) => (s ?? '').split(/[,\n]/).map((x) => x.trim()).filter(Boolean);

export function ApiAccessPage() {
  const qc = useQueryClient();
  const users = useUsers();
  const sites = useSites();
  const [tab, setTab] = useState('clients');
  const clients = useQuery({ queryKey: ['api-clients'], queryFn: () => get<any[]>('/api/admin/api-clients') });
  const mes = useQuery({ queryKey: ['mes-auth-status'], queryFn: () => get<any>('/api/admin/api-clients/mes-status') });
  const catalog = useQuery({ queryKey: ['perm-catalog'], queryFn: () => get<string[]>('/api/admin/roles/permissions') });
  const channels = useQuery({ queryKey: ['channels'], queryFn: () => get<any[]>('/api/admin/notification-channels') });
  const [dlg, setDlg] = useState<null | 'client' | 'device' | 'channel' | { edit: any } | { rotate: any } | { revoke: any }>(null);
  const [secret, setSecret] = useState<any>(null);
  const refresh = () => { qc.invalidateQueries({ queryKey: ['api-clients'] }); qc.invalidateQueries({ queryKey: ['channels'] }); qc.invalidateQueries({ queryKey: ['mes-auth-status'] }); };
  const r = useRunner(refresh);
  const name = (id: string) => users.data?.find((u) => u.id === id)?.displayName ?? id;
  const siteCode = (id: string) => sites.data?.find((s) => s.id === id)?.code ?? id;
  const isDevice = tab === 'devices';
  const rows = (clients.data ?? []).filter((c) => (c.kind === 1) === isDevice);
  const scopeOptions = (catalog.data ?? []).map((p) => [p, p] as [string, string]);
  const siteOptions = (sites.data ?? []).map((s) => [s.id, `${s.code} — ${s.name}`] as [string, string]);
  const restrictionInputs = (c?: any, device?: boolean) => [
    { name: 'scopes', label: device ? 'Permission scopes (required)' : 'Permission scopes (empty = all of the service user’s)', type: 'multiselect' as const, options: scopeOptions, initial: (c?.scopes ?? (device ? ['production.edit'] : [])).join(','), helper: 'A scope never adds permissions: the service user must already hold each one at every selected site.' },
    { name: 'sites', label: device ? 'Sites (required)' : 'Sites (empty = the service user’s sites)', type: 'multiselect' as const, options: siteOptions, initial: (c?.siteIds ?? []).join(',') },
    { name: 'endpoints', label: 'Allowed endpoints (one per line, empty = any)', type: 'multiline' as const, initial: (c?.allowedEndpoints ?? []).join('\n'), helper: device ? 'Defaults to POST /api/mes/events. Write "METHOD /api/path" or "/api/path".' : 'Write "METHOD /api/path" or "/api/path"; a path also allows its sub-paths.' },
    { name: 'rate', label: 'Requests per minute', initial: String(c?.rateLimitPerMinute ?? (device ? 300 : 120)) },
    { name: 'expires', label: device ? 'Expires on (required, within two years)' : 'Expires on (optional)', type: 'date' as const, initial: c?.expiresOn ?? '' }];
  const body = (v: Record<string, string>) => ({ scopes: list(v.scopes), siteIds: list(v.sites), allowedEndpoints: list(v.endpoints), rateLimitPerMinute: Number(v.rate), expiresOn: v.expires || null });
  const edit = typeof dlg === 'object' && dlg && 'edit' in dlg ? dlg.edit : null;
  const rot = typeof dlg === 'object' && dlg && 'rotate' in dlg ? dlg.rotate : null;
  const rev = typeof dlg === 'object' && dlg && 'revoke' in dlg ? dlg.revoke : null;
  return (
    <ModuleWorkspace module={mod('governance')} crumbs={[{ label: 'Governance', to: '/governance' }, { label: 'API & channels' }]} title="Public API, MES devices and notification channels"
      tabs={[{ label: 'API clients', value: 'clients' }, { label: 'MES devices', value: 'devices' }, { label: 'Notification channels', value: 'channels' }]} tab={tab} onTab={setTab}
      actions={<Button variant="contained" onClick={() => setDlg(tab === 'clients' ? 'client' : tab === 'devices' ? 'device' : 'channel')}>{tab === 'clients' ? 'New API client' : tab === 'devices' ? 'Register MES device' : 'New channel'}</Button>}>
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      {secret && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setSecret(null)} data-testid="client-secret">Copy now — the secret is shown once and stored only as a hash. It cannot be retrieved later; rotate to get a new one.<br />Client ID: <b style={{ fontFamily: mono }}>{secret.clientId}</b><br />API key (X-Api-Key): <b style={{ fontFamily: mono }}>{secret.apiKey}</b></Alert>}
      {isDevice && mes.data && <Alert severity={mes.data.requireDeviceClients ? 'success' : 'info'} sx={{ mb: 2 }} data-testid="mes-auth-status">
        {mes.data.activeDevices} active MES device(s). Last 30 days: {mes.data.deviceEventsLast30Days} event(s) from registered devices, {mes.data.legacyEventsLast30Days} from user sessions or user tokens{mes.data.lastLegacyEventAt ? ` (last ${fmtDateTime(mes.data.lastLegacyEventAt)})` : ''}. {mes.data.note}</Alert>}
      {tab !== 'channels' && <DataTable data={rows} empty={isDevice ? 'No MES devices registered.' : 'No API clients.'}
        columns={[{ header: 'Name', accessorKey: 'name' }, { header: 'Client ID', accessorKey: 'clientId', cell: num }, { header: 'Secret', accessorKey: 'secretHint', cell: (x) => `…${x.getValue()}` },
          { header: 'Acts as', accessorKey: 'serviceUserId', cell: (x) => name(x.getValue()) },
          { header: 'Restrictions', id: 'rs', accessorFn: (c) => [c.scopes.length ? c.scopes.join(', ') : 'all service-user permissions', c.siteIds.length ? `sites ${c.siteIds.map(siteCode).join(', ')}` : 'service-user sites', c.allowedEndpoints.length ? c.allowedEndpoints.join(', ') : 'any endpoint'].join(' · ') },
          { header: 'Limit / min', accessorKey: 'rateLimitPerMinute' }, { header: 'Expires', accessorKey: 'expiresOn', cell: (x) => (x.getValue() ? fmtDate(x.getValue()) : '—') },
          { header: 'Last used', id: 'lu', accessorFn: (c) => (c.lastUsedAt ? `${fmtDateTime(c.lastUsedAt)}${c.lastUsedFrom ? ` from ${c.lastUsedFrom}` : ''}` : 'Never') },
          { header: 'Last failure', id: 'lf', accessorFn: (c) => (c.lastFailureAt ? `${fmtDateTime(c.lastFailureAt)} — ${c.lastFailureReason} (${c.failureCount} total)` : '—') },
          { header: 'Status', accessorKey: 'status', cell: (x) => <Chip size="small" color={clientStatusColor[x.getValue()] ?? 'default'} label={x.row.original.previousSecretValidUntil ? `${x.getValue()} · old secret until ${fmtDateTime(x.row.original.previousSecretValidUntil)}` : x.getValue()} /> },
          { header: '', id: 'a', cell: (x) => { const c = x.row.original; return c.status === 'Revoked' ? <Typography variant="caption" color="text.secondary">{c.revokedReason}</Typography> : <Stack direction="row">
            <Button size="small" onClick={() => setDlg({ edit: c })}>Edit</Button><Button size="small" onClick={() => setDlg({ rotate: c })}>Rotate</Button>
            <Button size="small" onClick={() => r.run(() => post(`/api/admin/api-clients/${c.id}/enabled`, { enabled: !c.enabled }))}>{c.enabled ? 'Disable' : 'Enable'}</Button>
            <Button size="small" color="error" onClick={() => setDlg({ revoke: c })}>Revoke</Button></Stack>; } }]} />}
      {tab === 'channels' && <DataTable data={channels.data ?? []} empty="No channels. Channels stay disabled until a test message succeeds."
        columns={[{ header: 'Name', accessorKey: 'name' }, { header: 'Kind', accessorKey: 'kind', cell: (x) => channelKinds[x.getValue()] }, { header: 'Scope', accessorKey: 'scope', cell: (x) => scopes[x.getValue()] }, { header: 'Target', accessorKey: 'target' }, { header: 'Enabled', accessorKey: 'enabled', cell: (x) => (x.getValue() ? 'Yes' : 'No') },
          { header: '', id: 'a', cell: (x) => <Button size="small" onClick={() => r.run(() => post(`/api/admin/notification-channels/${x.row.original.id}/test`, { enableOnSuccess: true }))}>Send test</Button> }]} />}
      <StepDialog open={dlg === 'client' || dlg === 'device'} onClose={() => setDlg(null)} key={String(dlg)} title={dlg === 'device' ? 'Register MES device' : 'New API client'} confirm="Create"
        intro={dlg === 'device' ? 'An MES device sends production events with its own credential (X-Api-Key, or OAuth client credentials at /api/oauth/token). It acts as its service user, narrowed to the scopes, sites and endpoints below.' : 'The client acts as its service user (X-Api-Key header or OAuth client credentials at /api/oauth/token), narrowed by any scopes, sites and endpoints you set.'}
        inputs={[{ name: 'name', label: 'Name' }, { name: 'user', label: 'Service user', type: 'select', options: (users.data ?? []).map((u) => [u.id, u.displayName]) }, ...restrictionInputs(undefined, dlg === 'device')]}
        onConfirm={async (v) => { setSecret(await post('/api/admin/api-clients', { name: v.name, serviceUserId: v.user, kind: dlg === 'device' ? 1 : 0, ...body(v) })); refresh(); }} />
      <StepDialog open={!!edit} onClose={() => setDlg(null)} key={edit?.id ?? 'e'} title={`Edit ${edit ? clientKinds[edit.kind] : ''}`} confirm="Save"
        inputs={[{ name: 'name', label: 'Name', initial: edit?.name }, ...restrictionInputs(edit, edit?.kind === 1)]}
        onConfirm={async (v) => { await put(`/api/admin/api-clients/${edit.id}`, { name: v.name, ...body(v) }); refresh(); }} />
      <StepDialog open={!!rot} onClose={() => setDlg(null)} key={rot?.id ?? 'r'} title={`Rotate secret — ${rot?.name ?? ''}`} confirm="Rotate"
        intro="A new secret is shown once. With a grace period the current secret keeps working until then, so the device can be updated without downtime; 0 stops it at once."
        inputs={[{ name: 'grace', label: 'Grace period (minutes, 0–10,080)', initial: '0' }]}
        onConfirm={async (v) => { setSecret(await post(`/api/admin/api-clients/${rot.id}/rotate`, { graceMinutes: Number(v.grace || 0) })); refresh(); }} />
      <StepDialog open={!!rev} onClose={() => setDlg(null)} key={rev?.id ?? 'v'} title={`Revoke ${rev?.name ?? ''}`} confirm="Revoke permanently" danger
        intro="Revocation is permanent: the key and any access token stop working at once, and the client cannot be enabled or rotated again. Use Disable for a temporary stop."
        inputs={[{ name: 'reason', label: 'Reason', type: 'multiline' }]}
        onConfirm={async (v) => { await post(`/api/admin/api-clients/${rev.id}/revoke`, { reason: v.reason }); refresh(); }} />
      <StepDialog open={dlg === 'channel'} onClose={() => setDlg(null)} title="New notification channel" confirm="Create"
        inputs={[{ name: 'name', label: 'Name' }, { name: 'kind', label: 'Kind', type: 'select', options: channelKinds.map((k, i) => [i, k]), initial: '2' }, { name: 'scope', label: 'Scope', type: 'select', options: scopes.map((k, i) => [i, k]), initial: '0' },
          { name: 'site', label: 'Site (site scope)', type: 'select', options: [['', 'None'], ...(sites.data ?? []).map((s) => [s.id, `${s.code} — ${s.name}`] as [string, string])] }, { name: 'target', label: 'Target (HTTPS webhook or email address)' }]}
        onConfirm={async (v) => { await post('/api/admin/notification-channels', { name: v.name, kind: Number(v.kind), scope: Number(v.scope), siteId: v.site || null, target: v.target }); refresh(); }} />
    </ModuleWorkspace>
  );
}

/* ---------- security governance: access certification and deployments ---------- */
const accessKinds = ['Security review', 'Risk assessment', 'Privileged access review', 'Access certification'];
const deployScopes = ['Single plant', 'Multi-plant', 'Regional', 'Global'];
const deployStatus = ['Planned', 'InProgress', 'Completed', 'RolledBack'];

export function AccessReviews() {
  const qc = useQueryClient();
  const q = useQuery({ queryKey: ['access-reviews'], queryFn: () => get<any[]>('/api/security-governance/reviews') });
  const [sel, setSel] = useState<string | null>(null); const [open, setOpen] = useState(false); const [done, setDone] = useState(false);
  const det = useQuery({ queryKey: ['access-review', sel], enabled: !!sel, queryFn: () => get<any>(`/api/security-governance/reviews/${sel}`) });
  const refresh = () => { qc.invalidateQueries({ queryKey: ['access-reviews'] }); qc.invalidateQueries({ queryKey: ['access-review'] }); };
  const r = useRunner(refresh);
  const d = det.data;
  return (
    <ModuleWorkspace module={mod('governance')} crumbs={[{ label: 'Governance', to: '/governance' }, { label: 'Access reviews' }]} title="Access reviews and certification" actions={<Button variant="contained" onClick={() => setOpen(true)}>Start review</Button>}>
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      <Stack direction={{ xs: 'column', md: 'row' }} gap={2}>
        <Stack gap={1} sx={{ width: { md: 300 } }}>{(q.data ?? []).map((a) => <Paper key={a.id} onClick={() => setSel(a.id)} sx={{ p: 1.25, cursor: 'pointer', borderLeft: 4, borderColor: sel === a.id ? 'primary.main' : a.status === 1 ? 'success.main' : 'warning.main' }}>
          <Typography variant="caption" sx={{ fontFamily: mono }}>{a.number}</Typography><Typography variant="body2" sx={{ fontWeight: 600 }}>{a.title}</Typography><Typography variant="caption">{accessKinds[a.kind]} · {a.status === 1 ? 'Completed' : 'Open'}</Typography></Paper>)}
          {q.data?.length === 0 && <Typography color="text.secondary">No reviews yet.</Typography>}</Stack>
        <Box sx={{ flex: 1 }}>{d && <Section title={`${d.review.number} · ${d.kind}`} action={d.status === 'Open' ? <Button variant="contained" size="small" disabled={d.pending > 0} onClick={() => setDone(true)}>Complete ({d.pending} pending)</Button> : <Chip color="success" label="Completed" />}>
          <DataTable data={d.items} empty="This review has no assignment snapshot."
            columns={[{ header: 'User', accessorKey: 'userName' }, { header: 'Role', accessorKey: 'roleName' }, { header: 'Scope', accessorKey: 'scope' }, { header: 'Privileged', accessorKey: 'privileged', cell: (x) => (x.getValue() ? <Chip size="small" color="error" label="Privileged" /> : '') },
              { header: 'Decision', id: 'd', cell: (x) => { const i = x.row.original as any; return d.status !== 'Open' ? ['Pending', 'Keep', 'Revoke'][i.decision] : <Stack direction="row" gap={0.5}>
                <Button size="small" variant={i.decision === 1 ? 'contained' : 'outlined'} onClick={() => r.run(() => post(`/api/security-governance/reviews/${sel}/items/${i.id}`, { decision: 1, comment: null }))}>Keep</Button>
                <Button size="small" color="error" variant={i.decision === 2 ? 'contained' : 'outlined'} onClick={() => { const c = window.prompt('Reason for revoking'); if (c) r.run(() => post(`/api/security-governance/reviews/${sel}/items/${i.id}`, { decision: 2, comment: c })); }}>Revoke</Button></Stack>; } }]} />
        </Section>}</Box>
      </Stack>
      <StepDialog open={open} onClose={() => setOpen(false)} title="Start review" confirm="Start" intro="Access certification and privileged access reviews take a snapshot of current role assignments for keep / revoke decisions."
        inputs={[{ name: 'title', label: 'Title' }, { name: 'kind', label: 'Kind', type: 'select', options: accessKinds.map((k, i) => [i, k]), initial: '3' }, { name: 'findings', label: 'Notes', type: 'multiline' }]}
        onConfirm={async (v) => { const a = await post<any>('/api/security-governance/reviews', { title: v.title, kind: Number(v.kind), findings: v.findings || null }); refresh(); setSel(a.id); }} />
      <StepDialog open={done} onClose={() => setDone(false)} title="Complete review" confirm="Complete and apply" danger intro="Revoked assignments are removed now, the users' sessions end, and every removal is written to the audit trail."
        inputs={[{ name: 'text', label: 'Conclusion', type: 'multiline' }]} onConfirm={async (v) => { await post(`/api/security-governance/reviews/${sel}/complete`, { text: v.text || null }); refresh(); }} />
    </ModuleWorkspace>
  );
}

export function Deployments() {
  const qc = useQueryClient(); const sites = useSites();
  const q = useQuery({ queryKey: ['deployments'], queryFn: () => get<any[]>('/api/security-governance/deployments') });
  const [open, setOpen] = useState(false);
  const r = useRunner(() => qc.invalidateQueries({ queryKey: ['deployments'] }));
  const next: Record<number, [number, string][]> = { 0: [[1, 'Start rollout']], 1: [[2, 'Mark complete'], [3, 'Roll back']] };
  return (
    <ModuleWorkspace module={mod('governance')} crumbs={[{ label: 'Governance', to: '/governance' }, { label: 'Deployments' }]} title="Release deployments" actions={<Button variant="contained" onClick={() => setOpen(true)}>Plan deployment</Button>}>
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      <DataTable data={q.data ?? []} empty="No deployments planned."
        columns={[{ header: 'Number', accessorKey: 'number', cell: num }, { header: 'Version', accessorKey: 'version', cell: num }, { header: 'Scope', accessorKey: 'scope', cell: (x) => deployScopes[x.getValue()] }, { header: 'Sites', accessorKey: 'siteIds', cell: (x) => (x.getValue() as string[]).length },
          { header: 'Planned', accessorKey: 'plannedOn', cell: (x) => fmtDate(x.getValue()) }, { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={deployStatus[x.getValue()]} /> },
          { header: '', id: 'a', cell: (x) => (next[x.row.original.status] ?? []).map(([s, l]) => <Button key={s} size="small" onClick={() => r.run(() => post(`/api/security-governance/deployments/${x.row.original.id}/status`, { status: s, notes: null }))}>{l}</Button>) }]} />
      <StepDialog open={open} onClose={() => setOpen(false)} title="Plan deployment" confirm="Plan"
        inputs={[{ name: 'version', label: 'Version' }, { name: 'scope', label: 'Scope', type: 'select', options: deployScopes.map((s, i) => [i, s]), initial: '0' }, { name: 'site', label: 'Site (single plant / first site)', type: 'select', options: (sites.data ?? []).map((s) => [s.id, `${s.code} — ${s.name}`]) }, { name: 'on', label: 'Planned on', type: 'date' }, { name: 'notes', label: 'Notes', type: 'multiline' }]}
        onConfirm={async (v) => { await post('/api/security-governance/deployments', { version: v.version, scope: Number(v.scope), siteIds: v.site ? [v.site] : [], plannedOn: v.on || null, notes: v.notes || null }); qc.invalidateQueries({ queryKey: ['deployments'] }); }} />
    </ModuleWorkspace>
  );
}
