import { Fragment, useState } from 'react';
import { Alert, Box, Button, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { del, get, post, put } from '../api';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { fmtDateTime } from '../components/Status';
import { Section, StepDialog, useRunner, useUsers } from '../components/Kit';
import { useAuth } from '../auth';
import { mod } from './Enterprise';

const P = () => mod('platform');
const crumbs = (label: string) => [{ label: 'Platform', to: '/platform' }, { label }];

export const deliveryModes = ['Secure link', 'PDF attachment', 'Excel attachment', 'CSV attachment'];
const sources = ['Saved report', 'Board report'];
const boardKinds: [string, string][] = [['executive', 'Executive quality report'], ['board', 'Board quality report'], ['strategic-risk', 'Strategic risk report']];
const runStatus = ['Delivered', 'Partially delivered', 'Failed', 'Skipped'];
const runColor: ('success' | 'warning' | 'error' | 'default')[] = ['success', 'warning', 'error', 'default'];
const triggers = ['Scheduled', 'Manual', 'Retry'];
const editionStatus = ['Draft', 'Approved', 'Rejected'];
const zones = (() => { try { return (Intl as unknown as { supportedValuesOf: (k: string) => string[] }).supportedValuesOf('timeZone'); } catch { return ['UTC', 'America/New_York', 'America/Chicago', 'America/Denver', 'America/Los_Angeles', 'Europe/London', 'Europe/Berlin', 'Asia/Tokyo']; } })();

export interface ScheduleRow { id: string; name?: string; source: number; savedReportId?: string; report?: string; boardReportKind?: string; cron: string; timeZone: string; recipients: string[]; recipientRoleIds: string[]; deliveryMode: number; generateDraftEdition: boolean; maxAttempts: number;
  paused: boolean; pausedAt?: string; pauseReason?: string; nextRunAt?: string; lastRunAt?: string; lastStatus?: string; failedAttempts: number; retryAt?: string; ownerId: string; owner?: string }
interface RecipientRow { userId: string; name?: string; outcome: number; reason?: string; downloadedAt?: string }
interface RunRow { id: string; trigger: number; attempt: number; status: number; deliveryMode: number; startedAt: string; completedAt?: string; recipients: number; delivered: number; excluded: number; error?: string; note?: string; editionVersion?: number; artifactId?: string; nextAttemptAt?: string; recipientDetail: RecipientRow[] }
interface Policy { attachmentsEnabled: boolean; allowPdf: boolean; allowXlsx: boolean; allowCsv: boolean; allowConfidential: boolean; maxAttachmentBytes: number; retentionDays: number; allowedEmailDomains: string[] }
interface Edition { id: string; kind: string; version: number; title: string; status: number; generatedAt: string; generatedBy?: string; scopeAllSites: boolean; scopeSites: number; approvedAt?: string; approvedBy?: string; notes?: string; confidential: boolean }

/** Schedule editor shared by the schedules page and the report designer. */
export function ScheduleDialog({ open, onClose, existing, savedReportId, onSaved }: { open: boolean; onClose: () => void; existing?: ScheduleRow | null; savedReportId?: string | null; onSaved: () => void }) {
  const users = useUsers();
  const groups = useQuery({ queryKey: ['report-groups'], queryFn: () => get<{ id: string; name: string; members: number }[]>('/api/reports/distribution/groups'), enabled: open });
  const reports = useQuery({ queryKey: ['saved-reports'], queryFn: () => get<{ id: string; name: string; shared: boolean; confidential: boolean }[]>('/api/designer/reports'), enabled: open });
  const policy = useQuery({ queryKey: ['report-policy'], queryFn: () => get<Policy>('/api/reports/distribution/policy'), enabled: open });
  const blank = { name: '', source: 0, savedReportId: savedReportId ?? '', boardReportKind: 'board', cron: '0 7 * * 1', timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC', recipients: [] as string[], recipientRoleIds: [] as string[], deliveryMode: 0, generateDraftEdition: false, maxAttempts: 3 };
  const [v, setV] = useState(blank);
  const [error, setError] = useState<string | null>(null);
  const p = policy.data;
  const modeOk = (m: number) => m === 0 || (!!p?.attachmentsEnabled && [false, p.allowPdf, p.allowXlsx, p.allowCsv][m]);
  const save = async () => {
    setError(null);
    const body = { ...v, savedReportId: v.source === 0 ? v.savedReportId || null : null, boardReportKind: v.source === 1 ? v.boardReportKind : null };
    try { if (existing) await put(`/api/reports/distribution/schedules/${existing.id}`, body); else await post('/api/reports/distribution/schedules', body); onSaved(); onClose(); } catch (e) { setError((e as Error).message); }
  };
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="md" TransitionProps={{ onEnter: () => { setError(null); setV(existing ? { name: existing.name ?? '', source: existing.source, savedReportId: existing.savedReportId ?? '', boardReportKind: existing.boardReportKind ?? 'board', cron: existing.cron, timeZone: existing.timeZone, recipients: existing.recipients, recipientRoleIds: existing.recipientRoleIds, deliveryMode: existing.deliveryMode, generateDraftEdition: existing.generateDraftEdition, maxAttempts: existing.maxAttempts } : blank); } }}>
      <DialogTitle>{existing ? 'Edit schedule' : 'New report schedule'}</DialogTitle>
      <DialogContent>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>Each run checks every recipient's permissions. With the secure link (default) people open the report under their own access; attached files and board editions go only to people whose access covers all of the content.</Typography>
        {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
        <Stack gap={2} sx={{ pt: 1 }}>
          <TextField label="Name" value={v.name} onChange={(e) => setV({ ...v, name: e.target.value })} inputProps={{ maxLength: 200 }} />
          <Stack direction={{ xs: 'column', sm: 'row' }} gap={2}>
            <TextField select label="Report source" value={v.source} onChange={(e) => setV({ ...v, source: Number(e.target.value), deliveryMode: 0 })} sx={{ minWidth: 180 }}>{sources.map((s, i) => <MenuItem key={s} value={i}>{s}</MenuItem>)}</TextField>
            {v.source === 0
              ? <TextField select fullWidth label="Saved report" value={v.savedReportId} onChange={(e) => setV({ ...v, savedReportId: e.target.value })}>{(reports.data ?? []).map((r) => <MenuItem key={r.id} value={r.id}>{r.name}{r.shared ? '' : ' (not shared)'}{r.confidential ? ' · confidential' : ''}</MenuItem>)}</TextField>
              : <TextField select fullWidth label="Board report" value={v.boardReportKind} onChange={(e) => setV({ ...v, boardReportKind: e.target.value })}>{boardKinds.map(([k, l]) => <MenuItem key={k} value={k}>{l}</MenuItem>)}</TextField>}
          </Stack>
          <Stack direction={{ xs: 'column', sm: 'row' }} gap={2}>
            <TextField label="Frequency (cron: minute hour day month weekday)" value={v.cron} onChange={(e) => setV({ ...v, cron: e.target.value })} helperText="0 7 * * 1 = Mondays 07:00 · 0 6 1 * * = 1st of the month 06:00" fullWidth />
            <TextField select label="Time zone" value={v.timeZone} onChange={(e) => setV({ ...v, timeZone: e.target.value })} sx={{ minWidth: 240 }} SelectProps={{ MenuProps: { PaperProps: { sx: { maxHeight: 360 } } } }}>{(zones.includes(v.timeZone) ? zones : [v.timeZone, ...zones]).map((z) => <MenuItem key={z} value={z}>{z}</MenuItem>)}</TextField>
          </Stack>
          <TextField select label="People" value={v.recipients} SelectProps={{ multiple: true }} onChange={(e) => setV({ ...v, recipients: ([] as string[]).concat(e.target.value as unknown as string[]) })}>{(users.data ?? []).map((u) => <MenuItem key={u.id} value={u.id}>{u.displayName}</MenuItem>)}</TextField>
          <TextField select label="Recipient groups (roles)" value={v.recipientRoleIds} SelectProps={{ multiple: true }} helperText="Every active member at the time of each run, if their permissions allow." onChange={(e) => setV({ ...v, recipientRoleIds: ([] as string[]).concat(e.target.value as unknown as string[]) })}>{(groups.data ?? []).map((g) => <MenuItem key={g.id} value={g.id}>{g.name} ({g.members})</MenuItem>)}</TextField>
          <Stack direction={{ xs: 'column', sm: 'row' }} gap={2}>
            <TextField select label="Delivery" value={v.deliveryMode} onChange={(e) => setV({ ...v, deliveryMode: Number(e.target.value) })} sx={{ minWidth: 220 }}
              helperText={p?.attachmentsEnabled ? 'Attachments need reports.export and are kept for the retention period.' : 'Attachments are switched off in the delivery policy.'}>
              {deliveryModes.map((m, i) => (v.source === 1 && i === 2) ? null : <MenuItem key={m} value={i} disabled={!modeOk(i)}>{m}</MenuItem>)}</TextField>
            <TextField select label="Attempts on failure" value={v.maxAttempts} onChange={(e) => setV({ ...v, maxAttempts: Number(e.target.value) })} sx={{ minWidth: 180 }}>{[1, 2, 3, 4, 5].map((n) => <MenuItem key={n} value={n}>{n}</MenuItem>)}</TextField>
          </Stack>
          {v.source === 1 && <FormControlLabel control={<Checkbox checked={v.generateDraftEdition} onChange={(e) => setV({ ...v, generateDraftEdition: e.target.checked })} />} label="Generate a new draft edition on each run for approval (recipients always get the latest approved edition)" />}
        </Stack>
      </DialogContent>
      <DialogActions><Button onClick={onClose}>Cancel</Button><Button variant="contained" onClick={save}>{existing ? 'Save' : 'Create schedule'}</Button></DialogActions>
    </Dialog>
  );
}

function History({ id }: { id: string }) {
  const q = useQuery({ queryKey: ['schedule-history', id], queryFn: () => get<RunRow[]>(`/api/reports/distribution/schedules/${id}/history`) });
  const [open, setOpen] = useState<string | null>(null);
  if (q.isLoading) return <Typography variant="body2">Loading…</Typography>;
  if (!q.data?.length) return <Typography variant="body2" color="text.secondary">No runs yet.</Typography>;
  return (
    <Table size="small" aria-label="Distribution history">
      <TableHead><TableRow><TableCell>Started</TableCell><TableCell>Trigger</TableCell><TableCell>Result</TableCell><TableCell>Delivered</TableCell><TableCell>Excluded</TableCell><TableCell>Details</TableCell></TableRow></TableHead>
      <TableBody>{q.data.map((r) => <Fragment key={r.id}>
        <TableRow hover onClick={() => setOpen(open === r.id ? null : r.id)} sx={{ cursor: 'pointer' }}>
          <TableCell>{fmtDateTime(r.startedAt)}</TableCell><TableCell>{triggers[r.trigger]}{r.attempt > 1 ? ` (attempt ${r.attempt})` : ''}</TableCell>
          <TableCell><Chip size="small" color={runColor[r.status]} label={runStatus[r.status]} /></TableCell><TableCell>{r.delivered}</TableCell><TableCell>{r.excluded}</TableCell>
          <TableCell>{r.error ?? r.note ?? (r.editionVersion ? `Edition ${r.editionVersion}` : '')}{r.artifactId ? ` · ${deliveryModes[r.deliveryMode]}` : ''}{r.nextAttemptAt ? ` · retry ${fmtDateTime(r.nextAttemptAt)}` : ''}</TableCell>
        </TableRow>
        {open === r.id && <TableRow><TableCell colSpan={6}>
          {r.recipientDetail.map((x) => <Typography key={x.userId} variant="body2">{x.name ?? x.userId}: {x.outcome === 0 ? 'delivered' : `excluded — ${x.reason}`}{x.downloadedAt ? ` · file opened ${fmtDateTime(x.downloadedAt)}` : ''}</Typography>)}
          {r.artifactId && <Button size="small" href={`/api/reports/distribution/files/${r.artifactId}`}>Download file</Button>}
        </TableCell></TableRow>}
      </Fragment>)}</TableBody>
    </Table>
  );
}

function SchedulesTab({ all }: { all: boolean }) {
  const qc = useQueryClient();
  const q = useQuery({ queryKey: ['report-schedules', all], queryFn: () => get<ScheduleRow[]>(`/api/reports/distribution/schedules?all=${all}`) });
  const [edit, setEdit] = useState<ScheduleRow | null | undefined>(undefined);
  const [pause, setPause] = useState<string | null>(null);
  const [sel, setSel] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const refresh = () => { qc.invalidateQueries({ queryKey: ['report-schedules'] }); qc.invalidateQueries({ queryKey: ['schedule-history'] }); };
  const r = useRunner(refresh);
  return (
    <>
      <Stack direction="row" sx={{ mb: 2 }}><Button variant="contained" onClick={() => setEdit(null)}>New schedule</Button></Stack>
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      {msg && <Alert severity="info" sx={{ mb: 2 }} onClose={() => setMsg(null)}>{msg}</Alert>}
      {q.data?.length === 0 && <Typography color="text.secondary">No schedules yet.</Typography>}
      <Stack gap={1.5}>{(q.data ?? []).map((s) => (
        <Paper key={s.id} sx={{ p: 1.5 }} data-testid="schedule-card">
          <Stack direction={{ xs: 'column', md: 'row' }} gap={1} alignItems={{ md: 'center' }}>
            <Box sx={{ flex: 1 }}>
              <Typography component="div" sx={{ fontWeight: 600 }}>{s.name ?? s.report ?? 'Schedule'} {s.paused ? <Chip size="small" label="Paused" sx={{ ml: 1 }} /> : null}</Typography>
              <Typography variant="body2" color="text.secondary">{s.source === 0 ? s.report : boardKinds.find((b) => b[0] === s.boardReportKind)?.[1]} · {s.cron} ({s.timeZone}) · {deliveryModes[s.deliveryMode]} · {s.recipients.length} people, {s.recipientRoleIds.length} groups{all && s.owner ? ` · owner ${s.owner}` : ''}</Typography>
              <Typography variant="body2">{s.paused ? `Paused ${fmtDateTime(s.pausedAt)}${s.pauseReason ? ` — ${s.pauseReason}` : ''}` : `Next run ${fmtDateTime(s.nextRunAt)}`}{s.retryAt ? ` · retry ${fmtDateTime(s.retryAt)}` : ''}{s.lastStatus ? ` · last: ${s.lastStatus}` : ''}</Typography>
            </Box>
            <Stack direction="row" gap={1} flexWrap="wrap">
              <Button size="small" disabled={r.busy} onClick={() => r.run(async () => { const x = await post<RunRow>(`/api/reports/distribution/schedules/${s.id}/run`); setMsg(`Run finished: ${runStatus[x.status]}${x.error ? ` — ${x.error}` : x.note ? ` — ${x.note}` : ''}`); setSel(s.id); })}>Run now</Button>
              {s.paused ? <Button size="small" onClick={() => r.run(() => post(`/api/reports/distribution/schedules/${s.id}/resume`))}>Resume</Button> : <Button size="small" onClick={() => setPause(s.id)}>Pause</Button>}
              <Button size="small" onClick={() => setEdit(s)}>Edit</Button>
              <Button size="small" onClick={() => setSel(sel === s.id ? null : s.id)}>History</Button>
              <Button size="small" color="error" onClick={() => { if (confirm('Delete this schedule and its history?')) r.run(() => del(`/api/reports/distribution/schedules/${s.id}`)); }}>Delete</Button>
            </Stack>
          </Stack>
          {sel === s.id && <Box sx={{ mt: 1 }}><History id={s.id} /></Box>}
        </Paper>))}</Stack>
      <ScheduleDialog open={edit !== undefined} existing={edit} onClose={() => setEdit(undefined)} onSaved={refresh} />
      <StepDialog open={!!pause} onClose={() => setPause(null)} title="Pause schedule" confirm="Pause" intro="No runs or retries happen while paused. Runs missed while paused are not sent afterwards."
        inputs={[{ name: 'reason', label: 'Reason (optional)' }]} onConfirm={async (v) => { await post(`/api/reports/distribution/schedules/${pause}/pause`, { reason: v.reason || null }); refresh(); }} />
    </>
  );
}

function EditionsTab() {
  const qc = useQueryClient(); const { can } = useAuth();
  const q = useQuery({ queryKey: ['board-editions'], queryFn: () => get<Edition[]>('/api/enterprise/board-editions') });
  const [decide, setDecide] = useState<{ id: string; approve: boolean } | null>(null);
  const r = useRunner(() => qc.invalidateQueries({ queryKey: ['board-editions'] }));
  return (
    <>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>An edition freezes a board report at generation time. Only approved editions are distributed; the person who generated an edition cannot approve it.</Typography>
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      <Stack direction="row" gap={1} sx={{ mb: 2 }}>{boardKinds.map(([k, l]) => <Button key={k} variant="outlined" size="small" disabled={r.busy} onClick={() => r.run(() => post(`/api/enterprise/board-editions/${k}`))}>Generate {l}</Button>)}</Stack>
      <Table size="small" aria-label="Board report editions">
        <TableHead><TableRow><TableCell>Report</TableCell><TableCell>Edition</TableCell><TableCell>Status</TableCell><TableCell>Generated</TableCell><TableCell>Decision</TableCell><TableCell /></TableRow></TableHead>
        <TableBody>{(q.data ?? []).map((e) => <TableRow key={e.id}>
          <TableCell>{e.title}{e.confidential ? <Chip size="small" label="Confidential" sx={{ ml: 1 }} /> : null}</TableCell><TableCell>v{e.version}</TableCell>
          <TableCell><Chip size="small" color={e.status === 1 ? 'success' : e.status === 2 ? 'error' : 'default'} label={editionStatus[e.status]} /></TableCell>
          <TableCell>{fmtDateTime(e.generatedAt)} · {e.generatedBy} · {e.scopeAllSites ? 'all sites' : `${e.scopeSites} site(s)`}</TableCell>
          <TableCell>{e.approvedAt ? `${e.approvedBy}, ${fmtDateTime(e.approvedAt)}: ${e.notes ?? ''}` : ''}</TableCell>
          <TableCell sx={{ whiteSpace: 'nowrap' }}><Button size="small" href={`/api/enterprise/board-editions/${e.id}/pdf`} target="_blank">PDF</Button>
            {e.status === 0 && can('reports.approve') && <><Button size="small" onClick={() => setDecide({ id: e.id, approve: true })}>Approve</Button><Button size="small" color="error" onClick={() => setDecide({ id: e.id, approve: false })}>Reject</Button></>}</TableCell>
        </TableRow>)}</TableBody>
      </Table>
      {q.data?.length === 0 && <Typography color="text.secondary" sx={{ mt: 1 }}>No editions yet.</Typography>}
      <StepDialog open={!!decide} onClose={() => setDecide(null)} title={decide?.approve ? 'Approve edition' : 'Reject edition'} confirm={decide?.approve ? 'Approve' : 'Reject'} danger={!decide?.approve}
        intro={decide?.approve ? 'Approved editions are released to board report schedules.' : 'Rejected editions are never distributed.'} inputs={[{ name: 'notes', label: 'Notes (required)', type: 'multiline' }]}
        onConfirm={async (v) => { await post(`/api/enterprise/board-editions/${decide!.id}/${decide!.approve ? 'approve' : 'reject'}`, { notes: v.notes ?? '' }); qc.invalidateQueries({ queryKey: ['board-editions'] }); }} />
    </>
  );
}

function PolicyTab() {
  const qc = useQueryClient(); const { can } = useAuth();
  const q = useQuery({ queryKey: ['report-policy'], queryFn: () => get<Policy>('/api/reports/distribution/policy') });
  const [v, setV] = useState<Policy | null>(null);
  const [domains, setDomains] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const r = useRunner(() => { qc.invalidateQueries({ queryKey: ['report-policy'] }); setSaved(true); });
  const p = v ?? q.data; const admin = can('platform.admin');
  if (!p) return null;
  const set = (x: Partial<Policy>) => { setSaved(false); setV({ ...p, ...x }); };
  const box = (k: keyof Policy, label: string) => <FormControlLabel control={<Checkbox disabled={!admin} checked={!!p[k]} onChange={(e) => set({ [k]: e.target.checked } as Partial<Policy>)} />} label={label} />;
  return (
    <Section title="Report delivery policy">
      <Alert severity="info" sx={{ mb: 2 }}>The secure link is always the default: recipients sign in and see only what their permissions allow. Attached files leave the system, so they are off until enabled here. Use a mail server with TLS (Email:UseTls); attachments are never mailed without TLS. Confidential reports are not attached unless allowed below.</Alert>
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      {saved && <Alert severity="success" sx={{ mb: 2 }}>Policy saved.</Alert>}
      <Stack gap={1}>
        {box('attachmentsEnabled', 'Allow scheduled reports to be attached')}
        <Stack direction="row" gap={2} sx={{ pl: 3 }}>{box('allowPdf', 'PDF')}{box('allowXlsx', 'Excel')}{box('allowCsv', 'CSV')}</Stack>
        {box('allowConfidential', 'Allow confidential reports (including board reports) to be attached')}
        <Stack direction={{ xs: 'column', sm: 'row' }} gap={2}>
          <TextField disabled={!admin} type="number" label="Maximum attachment size (KB)" value={Math.round(p.maxAttachmentBytes / 1024)} onChange={(e) => set({ maxAttachmentBytes: Number(e.target.value) * 1024 })} helperText="Larger files are sent as a secure link instead." />
          <TextField disabled={!admin} type="number" label="Keep attached files (days)" value={p.retentionDays} onChange={(e) => set({ retentionDays: Number(e.target.value) })} helperText="Files are purged afterwards; the history stays." />
        </Stack>
        <TextField disabled={!admin} label="Allowed recipient email domains for attachments" value={domains ?? p.allowedEmailDomains.join(', ')} onChange={(e) => { setSaved(false); setDomains(e.target.value); }} helperText="Comma separated, e.g. example.com. Blank = any internal user." />
        {admin && <Box><Button variant="contained" disabled={r.busy} onClick={() => r.run(async () => { await put('/api/reports/distribution/policy', { ...p, allowedEmailDomains: (domains ?? p.allowedEmailDomains.join(',')).split(',').map((d) => d.trim()).filter(Boolean) }); setV(null); setDomains(null); })}>Save policy</Button></Box>}
        {!admin && <Typography variant="body2" color="text.secondary">Only administrators with platform.admin can change the policy.</Typography>}
      </Stack>
    </Section>
  );
}

/** Scheduled report distribution: schedules, history, board report editions and the delivery policy. */
export function ReportSchedules() {
  const { can } = useAuth();
  const [tab, setTab] = useState('mine');
  const tabs = [{ value: 'mine', label: 'My schedules' }, ...(can('platform.admin') ? [{ value: 'all', label: 'All schedules' }] : []), ...(can('enterprise.view') ? [{ value: 'editions', label: 'Board editions' }] : []), { value: 'policy', label: 'Delivery policy' }];
  return (
    <ModuleWorkspace module={P()} crumbs={crumbs('Report schedules')} title="Report schedules" tabs={tabs} tab={tab} onTab={setTab}>
      {tab === 'mine' && <SchedulesTab all={false} />}
      {tab === 'all' && <SchedulesTab all />}
      {tab === 'editions' && <EditionsTab />}
      {tab === 'policy' && <PolicyTab />}
    </ModuleWorkspace>
  );
}
