import { useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { Alert, Box, Button, Chip, Grid, Link, MenuItem, Paper, Stack, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { get, post } from '../api';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { fmtDate, fmtDateTime } from '../components/Status';
import { Section, StepDialog, useRunner, useTypeLabel, useUsers } from '../components/Kit';
import { useAuth } from '../auth';
import { mono } from '../theme';
import { mod, num, Tile, SiteSelect, useSite } from './Enterprise';
import { ScheduleDialog } from './ReportSchedules';

/* eslint-disable @typescript-eslint/no-explicit-any */
const P = () => mod('platform');
const crumbs = (label: string) => [{ label: 'Platform', to: '/platform' }, { label }];
const iso = (d: Date) => d.toISOString().slice(0, 10);
const addDays = (n: number) => { const d = new Date(); d.setDate(d.getDate() + n); return d; };
const kindColor: Record<string, string> = { Ncr: 'error.main', Capa: 'warning.main', Audit: 'primary.main', Approval: 'secondary.main', Calibration: 'info.main', Training: 'success.main' };

export function MyWorkspace() {
  const q = useQuery({ queryKey: ['platform-my'], queryFn: () => get<any>('/api/platform/my') });
  const d = q.data; const [tab, setTab] = useState('queue');
  return (
    <ModuleWorkspace module={P()} crumbs={crumbs('My workspace')} title="My workspace" tabs={[{ value: 'queue', label: 'Unified queue' }, { value: 'overdue', label: 'Overdue' }, { value: 'soon', label: 'Due in 7 days' }]} tab={tab} onTab={setTab}>
      {d && <>
        <Grid container spacing={2} sx={{ mb: 2 }}>
          <Grid size={{ xs: 6, md: 3 }}><Tile label="Open tasks" value={d.counts.tasks} /></Grid>
          <Grid size={{ xs: 6, md: 3 }}><Tile label="Overdue" value={d.counts.overdue} color={d.counts.overdue ? 'error.main' : 'success.main'} /></Grid>
          <Grid size={{ xs: 6, md: 3 }}><Tile label="Due soon" value={d.counts.dueSoon} color="warning.main" /></Grid>
          <Grid size={{ xs: 6, md: 3 }}><Tile label="Unread notifications" value={d.counts.notifications} /></Grid>
        </Grid>
        <Stack gap={1}>{(tab === 'queue' ? d.queue : tab === 'overdue' ? d.overdue : d.dueSoon).map((x: any, i: number) =>
          <Paper key={i} sx={{ p: 1.25, display: 'flex', gap: 2, alignItems: 'center', borderLeft: 4, borderColor: kindColor[x.kind] ?? 'grey.400' }}>
            <Chip size="small" label={x.kind} /><Typography sx={{ fontFamily: mono, minWidth: 110 }}>{x.number}</Typography>
            <Link component={RouterLink} to={x.url} sx={{ flex: 1 }}>{x.title}</Link><Typography variant="caption">{x.due ? fmtDate(x.due) : 'No due date'}</Typography></Paper>)}
          {d.queue.length === 0 && <Typography color="text.secondary">Nothing assigned to you.</Typography>}</Stack>
      </>}
    </ModuleWorkspace>
  );
}

export function ActivityStream() {
  const [type, setType] = useState(''); const [rt, setRt] = useState('');
  const q = useQuery({ queryKey: ['activity', type, rt], queryFn: () => get<any[]>(`/api/platform/activity?take=200${type ? `&type=${type}` : ''}${rt ? `&recordType=${rt}` : ''}`) });
  const types = useQuery({ queryKey: ['event-types'], queryFn: () => get<string[]>('/api/platform/events/types') });
  return (
    <ModuleWorkspace module={P()} crumbs={crumbs('Activity stream')} title="Activity stream">
      <Stack direction="row" gap={2} sx={{ mb: 2 }}>
        <TextField select size="small" label="Event" value={type} onChange={(e) => setType(e.target.value)} sx={{ minWidth: 220 }}><MenuItem value="">All events</MenuItem>{(types.data ?? []).map((t) => <MenuItem key={t} value={t}>{t}</MenuItem>)}</TextField>
        <TextField size="small" label="Record type" value={rt} onChange={(e) => setRt(e.target.value)} placeholder="e.g. Ncr" />
      </Stack>
      <Box sx={{ borderLeft: 2, borderColor: 'divider', pl: 2 }}>{(q.data ?? []).map((e) => <Box key={e.id} sx={{ mb: 1.5, position: 'relative' }}>
        <Box sx={{ position: 'absolute', left: -21, top: 6, width: 8, height: 8, borderRadius: '50%', bgcolor: e.type.includes('approved') ? 'success.main' : e.type.includes('rejected') ? 'error.main' : 'primary.main' }} />
        <Typography variant="caption" color="text.secondary">{fmtDateTime(e.at ?? e.occurredAt)} · {e.userName ?? 'system'} · <span style={{ fontFamily: mono }}>{e.type}</span></Typography>
        <Typography component="div" variant="body2">{e.recordType && <Chip size="small" label={e.recordType} sx={{ mr: 1 }} />}{e.description}</Typography></Box>)}
        {q.data?.length === 0 && <Typography color="text.secondary">No activity you can see yet.</Typography>}</Box>
    </ModuleWorkspace>
  );
}

export function QualityCalendar() {
  const [from, setFrom] = useState(iso(addDays(-7))); const [to, setTo] = useState(iso(addDays(45)));
  const q = useQuery({ queryKey: ['calendar', from, to], queryFn: () => get<any[]>(`/api/platform/calendar?from=${from}&to=${to}`) });
  const byDate = (q.data ?? []).reduce((a: Record<string, any[]>, x) => { (a[x.date] ??= []).push(x); return a; }, {});
  return (
    <ModuleWorkspace module={P()} crumbs={crumbs('Calendar')} title="Quality calendar">
      <Stack direction="row" gap={2} sx={{ mb: 2 }}><TextField size="small" type="date" label="From" value={from} onChange={(e) => setFrom(e.target.value)} InputLabelProps={{ shrink: true }} /><TextField size="small" type="date" label="To" value={to} onChange={(e) => setTo(e.target.value)} InputLabelProps={{ shrink: true }} /></Stack>
      <Grid container spacing={1.5}>{Object.keys(byDate).sort().map((d) => <Grid key={d} size={{ xs: 12, sm: 6, md: 3 }}><Paper sx={{ p: 1.25, height: '100%' }}>
        <Typography variant="overline">{fmtDate(d)}</Typography>
        {byDate[d].map((x: any, i: number) => <Box key={i} sx={{ borderLeft: 3, borderColor: kindColor[x.kind] ?? 'grey.400', pl: 1, mb: 0.75 }}><Typography variant="caption" color="text.secondary">{x.kind}</Typography><br /><Link component={RouterLink} to={x.url} variant="body2">{x.number} {x.title}</Link></Box>)}</Paper></Grid>)}</Grid>
      {q.data?.length === 0 && <Typography color="text.secondary">Nothing scheduled in this range.</Typography>}
    </ModuleWorkspace>
  );
}

export function Resources() {
  const [site, setSite] = useSite(); const qc = useQueryClient(); const [open, setOpen] = useState(false);
  const from = iso(new Date()); const to = iso(addDays(30));
  const q = useQuery({ queryKey: ['resources', site], enabled: !!site, queryFn: () => get<any>(`/api/platform/resources?siteId=${site}&from=${from}&to=${to}`) });
  const users = useUsers();
  return (
    <ModuleWorkspace module={P()} crumbs={crumbs('Resources')} title="Resource capacity (next 30 days)" actions={<Button variant="contained" disabled={!site} onClick={() => setOpen(true)}>Record absence</Button>}>
      <Box sx={{ mb: 2, maxWidth: 320 }}><SiteSelect value={site} onChange={setSite} /></Box>
      {q.data && <><Section title="People"><DataTable data={q.data.people} columns={[{ header: 'Name', accessorKey: 'name' }, { header: 'Role', accessorKey: 'role' }, { header: 'Open tasks', accessorKey: 'openTasks' }, { header: 'Audits', accessorKey: 'auditsInRange' },
        { header: 'Absences', id: 'a', cell: (x) => (x.row.original as any).absences.map((a: any) => <Chip key={a.id} size="small" sx={{ mr: .5 }} label={`${fmtDate(a.from)}–${fmtDate(a.to)} ${a.reason}`} />) }]} /></Section>
        <Section title="Equipment"><DataTable data={q.data.equipment} columns={[{ header: 'ID', accessorKey: 'number', cell: num }, { header: 'Name', accessorKey: 'name' }, { header: 'Status', accessorKey: 'status' }, { header: 'Calibration due', accessorKey: 'nextDueOn', cell: (x) => fmtDate(x.getValue() as string) }]} /></Section></>}
      <StepDialog open={open} onClose={() => setOpen(false)} title="Record absence" confirm="Save" intro="Absences reduce availability on the capacity board and calendar."
        inputs={[{ name: 'who', label: 'Person', type: 'select', options: (users.data ?? []).map((u) => [u.id, u.displayName]) }, { name: 'from', label: 'From', type: 'date' }, { name: 'to', label: 'To', type: 'date' }, { name: 'reason', label: 'Reason' }]}
        onConfirm={async (v) => { await post('/api/platform/resources/absences', { kind: 0, resourceId: v.who, from: v.from, to: v.to, reason: v.reason, siteId: site }); qc.invalidateQueries({ queryKey: ['resources'] }); }} />
    </ModuleWorkspace>
  );
}

export function Genealogy() {
  const [type, setType] = useState('Ncr'); const [id, setId] = useState(''); const [go, setGo] = useState<[string, string] | null>(null);
  const q = useQuery({ queryKey: ['genealogy', go], enabled: !!go, queryFn: () => get<any>(`/api/platform/genealogy/${go![0]}/${go![1]}`) });
  const depths = q.data ? Array.from(new Set<number>(q.data.nodes.map((n: any) => n.depth))).sort() : [];
  return (
    <ModuleWorkspace module={P()} crumbs={crumbs('Genealogy')} title="Record genealogy">
      <Stack direction="row" gap={2} sx={{ mb: 2 }}><TextField size="small" label="Record type" value={type} onChange={(e) => setType(e.target.value)} /><TextField size="small" label="Record id" value={id} onChange={(e) => setId(e.target.value)} sx={{ minWidth: 340 }} /><Button variant="contained" onClick={() => setGo([type, id.trim()])}>Trace</Button></Stack>
      {q.error && <Alert severity="error">{String((q.error as Error).message)}</Alert>}
      {q.data && <Stack direction="row" gap={2} sx={{ overflowX: 'auto' }}>{depths.map((d) => <Stack key={d} gap={1} sx={{ minWidth: 220 }}><Typography variant="overline">{d === 0 ? 'Origin' : `Hop ${d}`}</Typography>
        {q.data.nodes.filter((n: any) => n.depth === d).map((n: any) => <Paper key={n.type + n.id} sx={{ p: 1, bgcolor: n.restricted ? 'action.disabledBackground' : undefined }}><Typography variant="caption">{n.type}</Typography><br />
          {n.restricted ? <Typography variant="body2" color="text.secondary">Restricted record</Typography> : <Link component={RouterLink} to={n.url} variant="body2">{n.number} {n.title}</Link>}</Paper>)}</Stack>)}</Stack>}
    </ModuleWorkspace>
  );
}

const reportKinds = ['Tabular', 'Matrix', 'KPI', 'Executive'];
export function ReportDesigner() {
  const qc = useQueryClient(); const { can } = useAuth();
  const list = useQuery({ queryKey: ['saved-reports'], queryFn: () => get<any[]>('/api/designer/reports') });
  const defs = useQuery({ queryKey: ['report-defs'], queryFn: () => get<any[]>('/api/reports') });
  const [sel, setSel] = useState<string | null>(null); const [open, setOpen] = useState(false); const [sched, setSched] = useState(false);
  const run = useQuery({ queryKey: ['saved-report-run', sel], enabled: !!sel, queryFn: () => get<any>(`/api/designer/reports/${sel}/run`) });
  const r = useRunner(() => qc.invalidateQueries({ queryKey: ['saved-reports'] }));
  const x = run.data;
  return (
    <ModuleWorkspace module={P()} crumbs={crumbs('Report designer')} title="Report designer" actions={<Button variant="contained" onClick={() => setOpen(true)}>New report</Button>}>
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      <Stack direction={{ xs: 'column', md: 'row' }} gap={2}>
        <Stack gap={1} sx={{ width: { md: 280 } }}>{(list.data ?? []).map((s) => <Paper key={s.id} onClick={() => setSel(s.id)} sx={{ p: 1.25, cursor: 'pointer', borderLeft: 4, borderColor: sel === s.id ? 'primary.main' : 'divider' }}>
          <Typography variant="body2" sx={{ fontWeight: 600 }}>{s.name}</Typography><Typography variant="caption">{reportKinds[s.kind]} · {s.reportKey}{s.shared ? ' · shared' : ''}</Typography></Paper>)}
          {list.data?.length === 0 && <Typography color="text.secondary">No saved reports yet.</Typography>}</Stack>
        <Box sx={{ flex: 1 }}>{x && <Section title={`${x.name} — ${x.title}`} action={<Button size="small" onClick={() => setSched(true)}>Schedule</Button>}>
          {x.matrix ? <Box sx={{ overflowX: 'auto' }}><table style={{ borderCollapse: 'collapse', width: '100%' }}><thead><tr><th style={{ textAlign: 'left' }}>{x.matrix.rowField} \ {x.matrix.columnField}</th>{x.matrix.columns.map((c: string) => <th key={c}>{c}</th>)}<th>Total</th></tr></thead>
            <tbody>{x.matrix.rows.map((rw: string, i: number) => <tr key={rw}><td>{rw}</td>{x.matrix.cells[i].map((v: number, j: number) => <td key={j} style={{ textAlign: 'center', background: v ? 'rgba(0,128,128,.12)' : undefined }}>{v}</td>)}<td style={{ textAlign: 'center', fontWeight: 600 }}>{x.matrix.rowTotals[i]}</td></tr>)}</tbody></table></Box>
            : <DataTable data={x.rows} columns={x.columns.map((c: any) => ({ header: c.label, accessorKey: c.key }))} />}
        </Section>}</Box>
      </Stack>
      <StepDialog open={open} onClose={() => setOpen(false)} title="New report" confirm="Save" intro="Start from any standard report you can access, then choose columns, a filter and an optional pivot."
        inputs={[{ name: 'name', label: 'Name' }, { name: 'reportKey', label: 'Source report', type: 'select', options: (defs.data ?? []).map((d) => [d.key, d.title]) }, { name: 'kind', label: 'Layout', type: 'select', options: reportKinds.map((k, i) => [i, k]), initial: '0' },
          { name: 'columns', label: 'Columns (comma separated keys, blank = all)' }, { name: 'filterColumn', label: 'Filter column' }, { name: 'filterValue', label: 'Filter value' }, { name: 'rowField', label: 'Pivot rows' }, { name: 'columnField', label: 'Pivot columns' },
          ...(can('reports.share') ? [{ name: 'shared', label: 'Share with colleagues', type: 'checkbox' as const }] : []), { name: 'confidential', label: 'Confidential (never attached to scheduled emails unless policy allows)', type: 'checkbox' as const }]}
        onConfirm={async (v) => { const s = await post<any>('/api/designer/reports', { name: v.name, reportKey: v.reportKey, kind: Number(v.kind), columns: v.columns ? String(v.columns).split(',').map((c) => c.trim()).filter(Boolean) : [], siteId: null, rangeDays: 90, filterColumn: v.filterColumn || null, filterValue: v.filterValue || null, rowField: v.rowField || null, columnField: v.columnField || null, valueField: null, shared: v.shared === 'true', confidential: v.confidential === 'true' }); qc.invalidateQueries({ queryKey: ['saved-reports'] }); setSel(s.id); }} />
      <ScheduleDialog open={sched} onClose={() => setSched(false)} savedReportId={sel} onSaved={() => undefined} />
    </ModuleWorkspace>
  );
}

const widgetKinds = ['KPI', 'Report', 'Count', 'Matrix'];
export function DashboardDesigner() {
  const qc = useQueryClient();
  const list = useQuery({ queryKey: ['dashboards'], queryFn: () => get<any[]>('/api/designer/dashboards') });
  const reports = useQuery({ queryKey: ['saved-reports'], queryFn: () => get<any[]>('/api/designer/reports') });
  const [sel, setSel] = useState<string | null>(null); const [open, setOpen] = useState(false);
  const data = useQuery({ queryKey: ['dashboard-data', sel], enabled: !!sel, queryFn: () => get<any>(`/api/designer/dashboards/${sel}/data`) });
  return (
    <ModuleWorkspace module={P()} crumbs={crumbs('Dashboards')} title="Dashboard designer" actions={<Button variant="contained" onClick={() => setOpen(true)}>New dashboard</Button>}
      tabs={(list.data ?? []).map((d) => ({ value: d.id, label: d.name + (d.isHome ? ' ★' : '') }))} tab={sel ?? (list.data?.[0]?.id ?? '')} onTab={setSel}>
      {!sel && list.data?.[0] && <Button onClick={() => setSel(list.data![0].id)}>Open {list.data[0].name}</Button>}
      <Grid container spacing={2}>{(data.data?.widgets ?? []).map((w: any, i: number) => <Grid key={i} size={{ xs: 12, md: Math.min(12, Math.max(3, w.width ?? 4)) }}><Paper sx={{ p: 2, height: '100%' }}>
        <Typography variant="overline">{w.title}</Typography>
        {w.error ? <Alert severity="warning">{w.error}</Alert> : w.kind === 'Count' ? <Typography variant="h3">{w.data.count}</Typography>
          : w.kind === 'Kpi' ? <Typography variant="h4">{w.data.latest ?? '—'} <Typography component="span" variant="body2">{w.data.unit} (target {w.data.target})</Typography></Typography>
          : w.data?.rows ? <DataTable data={w.data.rows} columns={w.data.columns.map((c: any) => ({ header: c.label, accessorKey: c.key }))} /> : <Typography variant="h4">{w.data?.total}</Typography>}
      </Paper></Grid>)}</Grid>
      <StepDialog open={open} onClose={() => setOpen(false)} title="New dashboard" confirm="Create" intro="Start with an open-record counter; add a saved report widget if you choose one."
        inputs={[{ name: 'name', label: 'Name' }, { name: 'count', label: 'Count open records of type', initial: 'Ncr' }, { name: 'report', label: 'Saved report widget (optional)', type: 'select', options: [['', 'None'], ...(reports.data ?? []).map((r) => [r.id, r.name] as [string, string])] }, { name: 'home', label: 'Use as my home dashboard', type: 'checkbox' }]}
        onConfirm={async (v) => { const widgets: any[] = [{ title: `Open ${v.count}`, kind: 2, source: v.count, width: 4 }]; if (v.report) widgets.push({ title: 'Report', kind: 1, source: v.report, width: 8 });
          const d = await post<any>('/api/designer/dashboards', { name: v.name, shared: false, isHome: !!v.home, widgets }); qc.invalidateQueries({ queryKey: ['dashboards'] }); setSel(d.id); }} />
      <Typography variant="caption" color="text.secondary">Widget types: {widgetKinds.join(', ')}</Typography>
    </ModuleWorkspace>
  );
}

export function Webhooks() {
  const qc = useQueryClient(); const [open, setOpen] = useState(false); const [secret, setSecret] = useState<string | null>(null); const [sel, setSel] = useState<string | null>(null);
  const q = useQuery({ queryKey: ['webhooks'], queryFn: () => get<any[]>('/api/platform/webhooks') });
  const types = useQuery({ queryKey: ['event-types'], queryFn: () => get<string[]>('/api/platform/events/types') });
  const del = useQuery({ queryKey: ['deliveries', sel], enabled: !!sel, queryFn: () => get<any[]>(`/api/platform/webhooks/${sel}/deliveries`) });
  const r = useRunner(() => qc.invalidateQueries({ queryKey: ['webhooks'] }));
  return (
    <ModuleWorkspace module={P()} crumbs={crumbs('Webhooks')} title="Webhooks and event bus" actions={<Stack direction="row" gap={1}><Button onClick={() => r.run(() => post('/api/platform/events/pump'))}>Process events now</Button><Button variant="contained" onClick={() => setOpen(true)}>Add webhook</Button></Stack>}>
      {secret && <Alert severity="success" sx={{ mb: 2 }} onClose={() => setSecret(null)}>Signing secret (shown once): <code>{secret}</code></Alert>}
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      <DataTable data={q.data ?? []} empty="No webhooks." columns={[{ header: 'Name', accessorKey: 'name' }, { header: 'URL', accessorKey: 'url', cell: num }, { header: 'Events', id: 'e', cell: (x) => (x.row.original as any).events.join(', ') },
        { header: 'Delivered', accessorKey: 'delivered' }, { header: 'Pending', accessorKey: 'pending' }, { header: 'Abandoned', accessorKey: 'failed' },
        { header: '', id: 'act', cell: (x) => { const w = x.row.original as any; return <Stack direction="row" gap={0.5}><Button size="small" onClick={() => setSel(w.id)}>Deliveries</Button><Button size="small" onClick={() => r.run(() => post(`/api/platform/webhooks/${w.id}/enabled`, { enabled: !w.enabled }))}>{w.enabled ? 'Disable' : 'Enable'}</Button></Stack>; } }]} />
      {sel && <Section title="Delivery log"><DataTable data={del.data ?? []} columns={[{ header: 'Event', accessorKey: 'eventId' }, { header: 'Status', accessorKey: 'status', cell: (x) => ['Pending', 'Delivered', 'Failed', 'Abandoned'][x.getValue() as number] }, { header: 'Attempts', accessorKey: 'attempts' }, { header: 'Last error', accessorKey: 'lastError' }]} /></Section>}
      <StepDialog open={open} onClose={() => setOpen(false)} title="Add webhook" confirm="Create" intro="Payloads are signed with HMAC-SHA256 in the X-QualGuard-Signature header. HTTPS only."
        inputs={[{ name: 'name', label: 'Name' }, { name: 'url', label: 'HTTPS URL' }, { name: 'event', label: 'Event', type: 'select', options: (types.data ?? []).map((t) => [t, t]) }, { name: 'recordTypes', label: 'Record types (comma separated, optional)' }]}
        onConfirm={async (v) => { const w = await post<any>('/api/platform/webhooks', { name: v.name, url: v.url, events: [v.event], recordTypes: v.recordTypes ? String(v.recordTypes).split(',').map((s) => s.trim()) : null }); setSecret(w.secret); qc.invalidateQueries({ queryKey: ['webhooks'] }); }} />
    </ModuleWorkspace>
  );
}

export function PlatformHealth() {
  const q = useQuery({ queryKey: ['platform-health'], queryFn: () => get<any>('/api/platform/health'), refetchInterval: 30000 });
  const d = q.data;
  return (
    <ModuleWorkspace module={P()} crumbs={crumbs('System health')} title="System health and observability">
      {q.error && <Alert severity="error">{String((q.error as Error).message)}</Alert>}
      {d && <><Grid container spacing={2} sx={{ mb: 2 }}>
        <Grid size={{ xs: 6, md: 3 }}><Tile label="Database latency" value={`${d.database.latencyMs} ms`} color={d.database.latencyMs < 50 ? 'success.main' : 'warning.main'} /></Grid>
        <Grid size={{ xs: 6, md: 3 }}><Tile label="Indexed records" value={d.search.indexedRecords} hint={`${d.search.latencyMs} ms`} /></Grid>
        <Grid size={{ xs: 6, md: 3 }}><Tile label="Event bus lag" value={d.eventBus.lag} color={d.eventBus.lag > 500 ? 'warning.main' : 'success.main'} hint={`${d.eventBus.events} events`} /></Grid>
        <Grid size={{ xs: 6, md: 3 }}><Tile label="Overdue jobs" value={d.backgroundJobs.overdue} color={d.backgroundJobs.overdue ? 'error.main' : 'success.main'} /></Grid></Grid>
        <Section title="API traffic"><DataTable data={d.api} columns={[{ header: 'Area', accessorKey: 'area', cell: num }, { header: 'Requests', accessorKey: 'requests' }, { header: 'Errors', accessorKey: 'errors' }, { header: 'Avg ms', accessorKey: 'avgMs' }, { header: 'Max ms', accessorKey: 'maxMs' }]} /></Section>
        <Typography variant="caption" color="text.secondary">Uptime {d.process.uptimeMinutes} min · {d.process.workingSetMb} MB · v{d.process.version}</Typography></>}
    </ModuleWorkspace>
  );
}

export function SecurityOps() {
  const q = useQuery({ queryKey: ['security-ops'], queryFn: () => get<any>('/api/platform/security') });
  const d = q.data;
  return (
    <ModuleWorkspace module={P()} crumbs={crumbs('Security operations')} title="Security operations">
      {d && <>{d.risks.map((r: string, i: number) => <Alert key={i} severity="warning" sx={{ mb: 1 }}>{r}</Alert>)}
        <Grid container spacing={2} sx={{ mb: 2 }}><Grid size={{ xs: 6, md: 3 }}><Tile label="Sign-ins (24 h)" value={d.signIns24h} /></Grid><Grid size={{ xs: 6, md: 3 }}><Tile label="Failed sign-ins (24 h)" value={d.failedSignIns24h} color={d.failedSignIns24h ? 'error.main' : 'success.main'} /></Grid>
          <Grid size={{ xs: 6, md: 3 }}><Tile label="Privileged people" value={d.privileged.length} /></Grid></Grid>
        <Section title="Privileged access"><DataTable data={d.privileged} columns={[{ header: 'Name', accessorKey: 'name' }, { header: 'Email', accessorKey: 'email' }, { header: 'Permissions', id: 'p', cell: (x) => (x.row.original as any).permissions.join(', ') }]} /></Section>
        <Section title="Recent security events"><DataTable data={d.recent} columns={[{ header: 'When', accessorKey: 'occurredAt', cell: (x) => fmtDateTime(x.getValue() as string) }, { header: 'Event', accessorKey: 'eventType' }, { header: 'User', accessorKey: 'userName' }, { header: 'IP', accessorKey: 'ipAddress' }, { header: 'OK', accessorKey: 'succeeded', cell: (x) => (x.getValue() ? '✓' : '✗') }]} /></Section></>}
    </ModuleWorkspace>
  );
}

export function ModuleRegistry() {
  const typeLabel = useTypeLabel();
  const q = useQuery({ queryKey: ['platform-modules'], queryFn: () => get<any[]>('/api/platform/modules') });
  return (
    <ModuleWorkspace module={P()} crumbs={crumbs('Modules')} title="Module registry">
      <Grid container spacing={1.5}>{(q.data ?? []).map((m) => <Grid key={m.module} size={{ xs: 12, sm: 6, md: 4 }}><Paper sx={{ p: 1.5 }}>
        <Stack direction="row" justifyContent="space-between"><Typography sx={{ fontWeight: 600 }}>{m.module}</Typography><Chip size="small" color="success" label={`v${m.version}`} /></Stack>
        <Typography variant="caption" color="text.secondary">Depends on {m.dependsOn.join(', ')}</Typography>
        <Box sx={{ mt: 1 }}>{m.recordTypes.map((t: string) => <Chip key={t} size="small" variant="outlined" label={typeLabel(t)} sx={{ mr: .5, mb: .5 }} />)}</Box></Paper></Grid>)}</Grid>
    </ModuleWorkspace>
  );
}
