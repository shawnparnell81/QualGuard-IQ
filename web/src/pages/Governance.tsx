import { useState } from 'react';
import { Alert, Box, Button, Chip, Paper, Stack, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { get, post } from '../api';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, fmtDate, fmtDateTime } from '../components/Status';
import { Facts, Section, StepDialog, useRunner, useSites, useUsers } from '../components/Kit';
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

export function RetentionPage() {
  const qc = useQueryClient();
  const q = useQuery({ queryKey: ['retention'], queryFn: () => get<any[]>('/api/governance/retention/policies') });
  const types = useQuery({ queryKey: ['retention-types'], queryFn: () => get<string[]>('/api/governance/retention/record-types') });
  const decisions = useQuery({ queryKey: ['retention-decisions'], queryFn: () => get<any[]>('/api/governance/retention/decisions') });
  const [open, setOpen] = useState(false); const [due, setDue] = useState<string | null>(null);
  const dueQ = useQuery({ queryKey: ['retention-due', due], enabled: !!due, queryFn: () => get<any[]>(`/api/governance/retention/policies/${due}/due`) });
  const [tab, setTab] = useState('policies');
  return (
    <ModuleWorkspace module={mod('governance')} crumbs={[{ label: 'Governance', to: '/governance' }, { label: 'Retention' }]} title="Record retention"
      tabs={[{ label: 'Policies', value: 'policies' }, { label: 'Decisions log', value: 'log' }]} tab={tab} onTab={setTab} actions={<Button variant="contained" onClick={() => setOpen(true)}>New policy</Button>}>
      <Alert severity="info" sx={{ mb: 2 }}>Retention never deletes records automatically. Records past their period are listed for a recorded decision.</Alert>
      {tab === 'policies' && <DataTable data={q.data ?? []} onRowClick={(r) => setDue(r.id)} empty="No retention policies."
        columns={[{ header: 'Name', accessorKey: 'name' }, { header: 'Record type', accessorKey: 'recordType' }, { header: 'Retain (years)', accessorKey: 'retainYears' }, { header: 'Then', accessorKey: 'action', cell: (x) => retentionActions[x.getValue()] }, { header: 'Active', accessorKey: 'isActive', cell: (x) => (x.getValue() ? 'Yes' : 'No') }]} />}
      {tab === 'policies' && due && <Box sx={{ mt: 2 }}><Section title={`Due for decision (${dueQ.data?.length ?? 0})`}><DataTable data={dueQ.data ?? []} empty="Nothing past its retention period." columns={[{ header: 'Record', accessorKey: 'number', cell: num }, { header: 'Type', accessorKey: 'recordType' }, { header: 'Created', accessorKey: 'createdAt', cell: (x) => fmtDate(x.getValue()) }]} /></Section></Box>}
      {tab === 'log' && <DataTable data={decisions.data ?? []} empty="No decisions recorded." columns={[{ header: 'Record type', accessorKey: 'recordType' }, { header: 'Decision', accessorKey: 'decision', cell: (x) => ['Archived', 'Disposal approved', 'Extended', 'Legal hold'][x.getValue()] }, { header: 'Reason', accessorKey: 'reason' }, { header: 'When', accessorKey: 'createdAt', cell: (x) => fmtDateTime(x.getValue()) }]} />}
      <StepDialog open={open} onClose={() => setOpen(false)} title="New retention policy" confirm="Create"
        inputs={[{ name: 'name', label: 'Name' }, { name: 'type', label: 'Record type', type: 'select', options: (types.data ?? []).map((t) => [t, t]) }, { name: 'years', label: 'Retain for (years)', initial: '10' }, { name: 'action', label: 'Then', type: 'select', options: retentionActions.map((a, i) => [i, a]), initial: '0' }]}
        onConfirm={async (v) => { await post('/api/governance/retention/policies', { name: v.name, recordType: v.type, retainYears: Number(v.years), action: Number(v.action), isActive: true }); qc.invalidateQueries({ queryKey: ['retention'] }); }} />
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

export function ApiAccessPage() {
  const qc = useQueryClient();
  const users = useUsers();
  const sites = useSites();
  const [tab, setTab] = useState('clients');
  const clients = useQuery({ queryKey: ['api-clients'], queryFn: () => get<any[]>('/api/admin/api-clients') });
  const channels = useQuery({ queryKey: ['channels'], queryFn: () => get<any[]>('/api/admin/notification-channels') });
  const [dlg, setDlg] = useState<null | 'client' | 'channel'>(null);
  const [secret, setSecret] = useState<any>(null);
  const refresh = () => { qc.invalidateQueries({ queryKey: ['api-clients'] }); qc.invalidateQueries({ queryKey: ['channels'] }); };
  const r = useRunner(refresh);
  const name = (id: string) => users.data?.find((u) => u.id === id)?.displayName ?? id;
  return (
    <ModuleWorkspace module={mod('governance')} crumbs={[{ label: 'Governance', to: '/governance' }, { label: 'API & channels' }]} title="Public API and notification channels"
      tabs={[{ label: 'API clients', value: 'clients' }, { label: 'Notification channels', value: 'channels' }]} tab={tab} onTab={setTab}
      actions={<Button variant="contained" onClick={() => setDlg(tab === 'clients' ? 'client' : 'channel')}>{tab === 'clients' ? 'New API client' : 'New channel'}</Button>}>
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      {secret && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setSecret(null)}>Copy now — the secret is shown once and stored only as a hash.<br />Client ID: <b style={{ fontFamily: mono }}>{secret.clientId}</b><br />API key (X-Api-Key): <b style={{ fontFamily: mono }}>{secret.apiKey}</b></Alert>}
      {tab === 'clients' && <DataTable data={clients.data ?? []} empty="No API clients."
        columns={[{ header: 'Name', accessorKey: 'name' }, { header: 'Client ID', accessorKey: 'clientId', cell: num }, { header: 'Secret', accessorKey: 'secretHint', cell: (x) => `…${x.getValue()}` }, { header: 'Acts as', accessorKey: 'serviceUserId', cell: (x) => name(x.getValue()) },
          { header: 'Limit / min', accessorKey: 'rateLimitPerMinute' }, { header: 'Last used', accessorKey: 'lastUsedAt', cell: (x) => fmtDateTime(x.getValue()) }, { header: 'Enabled', accessorKey: 'enabled', cell: (x) => (x.getValue() ? 'Yes' : 'No') },
          { header: '', id: 'a', cell: (x) => <><Button size="small" onClick={() => r.run(async () => setSecret(await post(`/api/admin/api-clients/${x.row.original.id}/rotate`)))}>Rotate</Button><Button size="small" onClick={() => r.run(() => post(`/api/admin/api-clients/${x.row.original.id}/enabled`, { enabled: !x.row.original.enabled }))}>{x.row.original.enabled ? 'Disable' : 'Enable'}</Button></> }]} />}
      {tab === 'channels' && <DataTable data={channels.data ?? []} empty="No channels. Channels stay disabled until a test message succeeds."
        columns={[{ header: 'Name', accessorKey: 'name' }, { header: 'Kind', accessorKey: 'kind', cell: (x) => channelKinds[x.getValue()] }, { header: 'Scope', accessorKey: 'scope', cell: (x) => scopes[x.getValue()] }, { header: 'Target', accessorKey: 'target' }, { header: 'Enabled', accessorKey: 'enabled', cell: (x) => (x.getValue() ? 'Yes' : 'No') },
          { header: '', id: 'a', cell: (x) => <Button size="small" onClick={() => r.run(() => post(`/api/admin/notification-channels/${x.row.original.id}/test`, { enableOnSuccess: true }))}>Send test</Button> }]} />}
      <StepDialog open={dlg === 'client'} onClose={() => setDlg(null)} title="New API client" confirm="Create" intro="The client acts with the permissions of its service user (X-Api-Key header or OAuth client credentials at /api/oauth/token)."
        inputs={[{ name: 'name', label: 'Name' }, { name: 'user', label: 'Service user', type: 'select', options: (users.data ?? []).map((u) => [u.id, u.displayName]) }, { name: 'rate', label: 'Requests per minute', initial: '120' }]}
        onConfirm={async (v) => { setSecret(await post('/api/admin/api-clients', { name: v.name, serviceUserId: v.user, rateLimitPerMinute: Number(v.rate), expiresOn: null })); refresh(); }} />
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
