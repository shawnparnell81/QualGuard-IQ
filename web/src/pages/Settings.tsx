import { useEffect, useState, type ReactNode } from 'react';
import { Link as RouterLink, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle, FormControlLabel, Link, MenuItem, Paper, Stack, Switch, Table, TableBody, TableCell, TableHead, TableRow, TextField, ToggleButton, ToggleButtonGroup, Typography } from '@mui/material';
import ApartmentOutlined from '@mui/icons-material/ApartmentOutlined';
import ImageOutlined from '@mui/icons-material/ImageOutlined';
import NumbersOutlined from '@mui/icons-material/NumbersOutlined';
import LockOutlined from '@mui/icons-material/LockOutlined';
import StorageOutlined from '@mui/icons-material/StorageOutlined';
import PaletteOutlined from '@mui/icons-material/PaletteOutlined';
import NotificationsNoneOutlined from '@mui/icons-material/NotificationsNoneOutlined';
import DrawOutlined from '@mui/icons-material/DrawOutlined';
import RestoreFromTrashOutlined from '@mui/icons-material/RestoreFromTrashOutlined';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api, del, get, post, put } from '../api';
import { useAuth } from '../auth';
import { useColorMode, type ColorPreference } from '../colorMode';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { fmtDateTime, setDateFormat } from '../components/Status';

const mod = modules.find((m) => m.key === 'admin')!;
const zones: string[] = (Intl as unknown as { supportedValuesOf?: (k: string) => string[] }).supportedValuesOf?.('timeZone') ?? ['America/New_York', 'America/Chicago', 'America/Denver', 'America/Los_Angeles', 'UTC'];
const sample = new Date(2026, 2, 7);
/** Date formats the app can show, with how 7 March 2026 looks in each. */
export const dateFormats: [string, string][] = [
  ['auto', `Browser default (${sample.toLocaleDateString()})`], ['yyyy-MM-dd', '2026-03-07'], ['MM/dd/yyyy', '03/07/2026'],
  ['dd/MM/yyyy', '07/03/2026'], ['dd-MMM-yyyy', '07-Mar-2026'], ['dd.MM.yyyy', '07.03.2026'],
];

function Card({ icon, title, intro, children, action }: { icon: ReactNode; title: string; intro?: ReactNode; children: ReactNode; action?: ReactNode }) {
  return (
    <Paper component="section" aria-label={title} sx={{ p: { xs: 2, sm: 3 } }}>
      <Stack direction="row" alignItems="center" gap={1.5} sx={{ mb: intro ? 0.5 : 2 }} flexWrap="wrap">
        <Box sx={{ color: 'primary.main', display: 'flex' }} aria-hidden>{icon}</Box>
        <Typography variant="h3" component="h2" sx={{ flex: 1, minWidth: 180 }}>{title}</Typography>
        {action}
      </Stack>
      {intro && <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>{intro}</Typography>}
      {children}
    </Paper>
  );
}
const Row = ({ label, value }: { label: string; value: ReactNode }) => (
  <Stack direction="row" justifyContent="space-between" gap={2} sx={{ py: 0.75, borderBottom: 1, borderColor: 'divider' }}>
    <Typography variant="body2">{label}</Typography><Typography variant="body2" component="div" fontWeight={600} textAlign="right">{value}</Typography>
  </Stack>
);
const yes = (b: boolean) => <Chip size="small" color={b ? 'success' : 'default'} label={b ? 'On' : 'Off'} />;

interface OrgSettings { applicationName: string; organizationName: string; primaryColor: string | null; timeZone: string; loginHistoryEnabled: boolean; rowVersion: number; dateFormat: string | null; emailNotificationsEnabled: boolean | null; hasLogo: boolean }
interface Numbering { recordType: string; label: string; prefix: string; padding: number; resetYearly: boolean; nextValue: number; example: string }
interface SystemStatus {
  storage: { provider: string; endpoint: string | null; bucket: string | null };
  scanning: { enabled: boolean; provider: string; reachable: boolean | null; message: string | null };
  signIn: { mfaRequiredForStaff: boolean; mfaRequiredForPortal: boolean; trustedDevicesEnabled: boolean; trustedDeviceDays: number; sessionAbsoluteHours: number; idleTimeoutHours: number; passwordMinLength: number; lockoutAttempts: number; lockoutMinutes: number };
  email: { configured: boolean; from: string | null };
}

const tabs = [
  { value: 'general', label: 'General' }, { value: 'logo', label: 'Logo' }, { value: 'numbering', label: 'Numbering' },
  { value: 'security', label: 'Sign-in & sessions' }, { value: 'system', label: 'Storage & scanning' },
];

/** Organization settings: what the whole installation uses. Only settings the app acts on are shown. */
export function OrganizationSettingsPage() {
  const [params, setParams] = useSearchParams();
  const tab = tabs.some((t) => t.value === params.get('tab')) ? params.get('tab')! : 'general';
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Settings' }]} title="Organization settings" tabs={tabs} tab={tab} onTab={(v) => setParams({ tab: v }, { replace: true })} record={false}>
      {tab === 'general' && <GeneralTab />}
      {tab === 'logo' && <LogoTab />}
      {tab === 'numbering' && <NumberingTab />}
      {tab === 'security' && <SecurityTab />}
      {tab === 'system' && <SystemTab />}
    </ModuleWorkspace>
  );
}

function GeneralTab() {
  const qc = useQueryClient();
  const s = useQuery({ queryKey: ['settings'], queryFn: () => get<OrgSettings>('/api/organization/settings') });
  const sites = useQuery({ queryKey: ['sites'], queryFn: () => get<{ id: string; code: string; name: string; timeZone: string; isArchived: boolean }[]>('/api/organization/sites') });
  const [v, setV] = useState<OrgSettings | null>(null);
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const cur = v ?? s.data;
  if (!cur) return null;
  const set = (patch: Partial<OrgSettings>) => { setMsg(null); setV({ ...cur, ...patch }); };
  const save = async () => {
    try {
      await put('/api/organization/settings', cur);
      setV(null); setMsg({ ok: true, text: 'Settings saved.' });
      if (cur.dateFormat) setDateFormat(cur.dateFormat);
      qc.invalidateQueries({ queryKey: ['settings'] }); qc.invalidateQueries({ queryKey: ['branding'] }); qc.invalidateQueries({ queryKey: ['preferences'] });
    } catch (e) { setMsg({ ok: false, text: (e as Error).message }); }
  };
  return (
    <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', lg: '3fr 2fr' }, alignItems: 'start' }}>
      <Card icon={<ApartmentOutlined />} title="Organization" intro="Names and regional settings used on every page, printout and export.">
        <Box sx={{ display: 'grid', gap: 2 }}>
          {msg && <Alert severity={msg.ok ? 'success' : 'error'} data-testid="settings-msg">{msg.text}</Alert>}
          <TextField label="Organization name" value={cur.organizationName} onChange={(e) => set({ organizationName: e.target.value })} />
          <TextField label="Application name" value={cur.applicationName} onChange={(e) => set({ applicationName: e.target.value })} helperText="Shown in the browser tab and on sign-in." />
          <TextField select label="Default time zone" value={cur.timeZone} onChange={(e) => set({ timeZone: e.target.value })} helperText="Used where a site has no time zone of its own.">
            {(zones.includes(cur.timeZone) ? zones : [cur.timeZone, ...zones]).map((z) => <MenuItem key={z} value={z}>{z}</MenuItem>)}
          </TextField>
          <TextField select label="Date format" value={cur.dateFormat ?? 'auto'} onChange={(e) => set({ dateFormat: e.target.value })} helperText="How dates show on screen. People can choose their own in My settings." slotProps={{ select: { SelectDisplayProps: { 'aria-label': 'Date format' } as object } }}>
            {dateFormats.map(([k, l]) => <MenuItem key={k} value={k}>{l}</MenuItem>)}
          </TextField>
          <TextField label="Brand color (#RRGGBB, blank for the standard theme)" value={cur.primaryColor ?? ''} onChange={(e) => set({ primaryColor: e.target.value || null })} />
          <FormControlLabel control={<Switch checked={cur.emailNotificationsEnabled ?? true} onChange={(e) => set({ emailNotificationsEnabled: e.target.checked })} />}
            label="Send e-mail copies of notifications (assignments, approvals, due dates)" />
          <FormControlLabel control={<Switch checked={cur.loginHistoryEnabled} onChange={(e) => set({ loginHistoryEnabled: e.target.checked })} />} label="Show login history to administrators" />
          <Box><Button variant="contained" onClick={save} disabled={!v} data-testid="settings-save">Save</Button></Box>
        </Box>
      </Card>
      <Card icon={<ApartmentOutlined />} title="Sites" intro="Each site has its own code, address and time zone." action={<Button size="small" component={RouterLink} to="/admin/sites">Manage sites</Button>}>
        {(sites.data ?? []).filter((x) => !x.isArchived).map((x) => <Row key={x.id} label={`${x.code} — ${x.name}`} value={x.timeZone} />)}
      </Card>
    </Box>
  );
}

function LogoTab() {
  const qc = useQueryClient();
  const s = useQuery({ queryKey: ['settings'], queryFn: () => get<OrgSettings>('/api/organization/settings') });
  const [stamp, setStamp] = useState(0);
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const done = (text: string) => { setMsg({ ok: true, text }); setStamp(Date.now()); qc.invalidateQueries({ queryKey: ['settings'] }); qc.invalidateQueries({ queryKey: ['branding'] }); };
  const upload = async (f: File) => {
    setMsg(null);
    const fd = new FormData(); fd.append('file', f);
    try { await api('POST', '/api/settings/logo', fd); done('Logo saved. It now shows in the top bar, on sign-in and on printouts.'); }
    catch (e) { setMsg({ ok: false, text: (e as Error).message }); }
  };
  return (
    <Card icon={<ImageOutlined />} title="Organization logo" intro="PNG or JPEG, up to 512 KB. A JPEG logo is also placed on PDF printouts and exports.">
      {msg && <Alert severity={msg.ok ? 'success' : 'error'} sx={{ mb: 2 }} data-testid="logo-msg">{msg.text}</Alert>}
      <Box sx={{ border: 1, borderColor: 'divider', borderRadius: 1, p: 2, mb: 2, minHeight: 96, display: 'grid', placeItems: 'center', bgcolor: 'background.default' }}>
        {s.data?.hasLogo ? <Box component="img" src={`/api/settings/logo?v=${stamp}`} alt="Organization logo" data-testid="logo-preview" sx={{ maxHeight: 80, maxWidth: '100%' }} />
          : <Typography color="text.secondary">No logo yet. The QualGuard IQ mark is used.</Typography>}
      </Box>
      <Stack direction="row" gap={1}>
        <Button variant="contained" component="label">Choose image<input hidden type="file" accept="image/png,image/jpeg" data-testid="logo-file" onChange={(e) => { const f = e.target.files?.[0]; if (f) upload(f); e.target.value = ''; }} /></Button>
        {s.data?.hasLogo && <Button color="error" onClick={async () => { await del('/api/settings/logo'); done('Logo removed.'); }}>Remove logo</Button>}
      </Stack>
    </Card>
  );
}

function NumberingTab() {
  const qc = useQueryClient();
  const q = useQuery({ queryKey: ['numbering'], queryFn: () => get<Numbering[]>('/api/settings/numbering') });
  const [edit, setEdit] = useState<Numbering | null>(null);
  const [error, setError] = useState<string | null>(null);
  const preview = edit ? `${edit.prefix}${edit.resetYearly ? `${new Date().getFullYear()}-` : ''}${String(edit.nextValue).padStart(edit.padding, '0')}` : '';
  return (
    <Card icon={<NumbersOutlined />} title="Numbering formats" intro="How new record numbers are built. Numbers already given out never change.">
      <Table size="small" data-export="off">
        <TableHead><TableRow><TableCell>Record type</TableCell><TableCell>Prefix</TableCell><TableCell>Digits</TableCell><TableCell>Restart each year</TableCell><TableCell>Next number</TableCell><TableCell /></TableRow></TableHead>
        <TableBody>
          {(q.data ?? []).map((n) => (
            <TableRow key={n.recordType}>
              <TableCell>{n.label}</TableCell><TableCell>{n.prefix}</TableCell><TableCell>{n.padding}</TableCell><TableCell>{n.resetYearly ? 'Yes' : 'No'}</TableCell>
              <TableCell><Typography variant="body2" fontWeight={600}>{n.example}</Typography></TableCell>
              <TableCell align="right"><Button size="small" onClick={() => { setError(null); setEdit(n); }} aria-label={`Change ${n.label} numbering`}>Change</Button></TableCell>
            </TableRow>
          ))}
          {q.data?.length === 0 && <TableRow><TableCell colSpan={6}><Typography color="text.secondary">Numbering formats appear here once the first record of a type is created.</Typography></TableCell></TableRow>}
        </TableBody>
      </Table>
      <Dialog open={!!edit} onClose={() => setEdit(null)} maxWidth="xs" fullWidth>
        <DialogTitle>{edit?.label} numbering</DialogTitle>
        {edit && <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
          {error && <Alert severity="error">{error}</Alert>}
          <TextField label="Prefix" value={edit.prefix} onChange={(e) => setEdit({ ...edit, prefix: e.target.value })} />
          <TextField type="number" label="Digits" value={edit.padding} onChange={(e) => setEdit({ ...edit, padding: Number(e.target.value) })} slotProps={{ htmlInput: { min: 1, max: 10 } }} />
          <FormControlLabel control={<Switch checked={edit.resetYearly} onChange={(e) => setEdit({ ...edit, resetYearly: e.target.checked })} />} label="Put the year in the number and restart at 1 each year" />
          <Alert severity="info">The next number will be <b>{preview}</b>.</Alert>
        </DialogContent>}
        <DialogActions>
          <Button onClick={() => setEdit(null)}>Cancel</Button>
          <Button variant="contained" onClick={async () => {
            if (!edit) return;
            try { await put(`/api/settings/numbering/${encodeURIComponent(edit.recordType)}`, { prefix: edit.prefix, padding: edit.padding, resetYearly: edit.resetYearly }); setEdit(null); qc.invalidateQueries({ queryKey: ['numbering'] }); }
            catch (e) { setError((e as Error).message); }
          }}>Save</Button>
        </DialogActions>
      </Dialog>
    </Card>
  );
}

const useSystem = () => useQuery({ queryKey: ['system-status'], queryFn: () => get<SystemStatus>('/api/settings/system') });

function SecurityTab() {
  const q = useSystem();
  const s = q.data?.signIn;
  if (!s) return null;
  return (
    <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, alignItems: 'start' }}>
      <Card icon={<LockOutlined />} title="Two-step verification" action={<Button size="small" component={RouterLink} to="/admin/sign-in-security">Change</Button>}>
        <Row label="Required for staff" value={yes(s.mfaRequiredForStaff)} />
        <Row label="Required for portal accounts" value={yes(s.mfaRequiredForPortal)} />
        <Row label="Remember trusted browsers" value={s.trustedDevicesEnabled ? `${s.trustedDeviceDays} days` : 'Off'} />
      </Card>
      <Card icon={<LockOutlined />} title="Sessions and passwords" intro="Set in the installation's configuration file; shown here for reference.">
        <Row label="Signed out after" value={`${s.sessionAbsoluteHours} hours`} />
        <Row label="Signed out when idle for" value={`${s.idleTimeoutHours} hours`} />
        <Row label="Minimum password length" value={`${s.passwordMinLength} characters`} />
        <Row label="Account locked after" value={`${s.lockoutAttempts} wrong passwords, for ${s.lockoutMinutes} minutes`} />
      </Card>
    </Box>
  );
}

function SystemTab() {
  const q = useSystem();
  const d = q.data;
  if (!d) return null;
  return (
    <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, alignItems: 'start' }}>
      <Card icon={<StorageOutlined />} title="File storage" intro="Where attachments, documents and imports are kept. Read-only.">
        <Row label="Storage" value={d.storage.provider} />
        <Row label="Server" value={d.storage.endpoint ?? '—'} />
        <Row label="Bucket" value={d.storage.bucket ?? '—'} />
      </Card>
      <Card icon={<StorageOutlined />} title="Virus scanning of uploads" intro="Read-only.">
        <Row label="Scanning" value={yes(d.scanning.enabled)} />
        <Row label="Scanner" value={d.scanning.provider} />
        <Row label="Answering" value={d.scanning.reachable === null ? '—' : d.scanning.reachable ? 'Yes' : 'No'} />
        {d.scanning.message && <Alert severity={d.scanning.reachable === false ? 'error' : 'info'} sx={{ mt: 1 }}>{d.scanning.message}</Alert>}
      </Card>
      <Card icon={<NotificationsNoneOutlined />} title="Outgoing e-mail" intro="Read-only.">
        <Row label="E-mail sending" value={d.email.configured ? 'Configured' : 'Not configured'} />
        <Row label="Sent from" value={d.email.from ?? '—'} />
      </Card>
    </Box>
  );
}

interface Prefs { emailNotifications: boolean; dateFormat: string | null; theme: string | null; organizationDateFormat: string; organizationEmailEnabled: boolean; effectiveDateFormat: string }

/** The signed-in person's own settings. */
export function MySettingsPage() {
  const qc = useQueryClient();
  const { me } = useAuth();
  const { preference, setPreference } = useColorMode();
  const q = useQuery({ queryKey: ['preferences'], queryFn: () => get<Prefs>('/api/account/preferences') });
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const p = q.data;
  const save = async (patch: Partial<Prefs>) => {
    if (!p) return;
    const next = { ...p, ...patch };
    await qc.cancelQueries({ queryKey: ['preferences'] }); // a load still in flight would put the old value back
    qc.setQueryData(['preferences'], next);
    try {
      const r = await put<Prefs>('/api/account/preferences', { emailNotifications: next.emailNotifications, dateFormat: next.dateFormat, theme: next.theme });
      qc.setQueryData(['preferences'], r); setDateFormat(r.effectiveDateFormat);
      setMsg({ ok: true, text: 'Saved.' });
    } catch (e) { qc.setQueryData(['preferences'], p); setMsg({ ok: false, text: (e as Error).message }); }
  };
  return (
    <Box sx={{ maxWidth: 960, mx: 'auto', p: { xs: 2, sm: 4 }, display: 'grid', gap: 2.5 }}>
      <Box>
        <Typography variant="h1">My settings</Typography>
        <Typography color="text.secondary">{me?.displayName} · {me?.email}</Typography>
      </Box>
      {msg && <Alert severity={msg.ok ? 'success' : 'error'} onClose={() => setMsg(null)} data-testid="prefs-msg">{msg.text}</Alert>}
      <Card icon={<PaletteOutlined />} title="Appearance" intro="Kept with your account, so it follows you to other computers.">
        <ToggleButtonGroup exclusive value={preference} onChange={(_, v: ColorPreference | null) => { if (v) { setPreference(v); save({ theme: v }); } }} aria-label="Theme">
          <ToggleButton value="light">Light</ToggleButton><ToggleButton value="dark">Dark</ToggleButton><ToggleButton value="system">Match device</ToggleButton>
        </ToggleButtonGroup>
        {p && <TextField select label="Date format" value={p.dateFormat ?? ''} onChange={(e) => save({ dateFormat: e.target.value || null })} sx={{ mt: 2, minWidth: 280, display: 'flex' }}
          slotProps={{ select: { displayEmpty: true, SelectDisplayProps: { 'aria-label': 'My date format' } as object }, inputLabel: { shrink: true } }}>
          <MenuItem value="">Organization default ({dateFormats.find(([k]) => k === p.organizationDateFormat)?.[1] ?? p.organizationDateFormat})</MenuItem>
          {dateFormats.map(([k, l]) => <MenuItem key={k} value={k}>{l}</MenuItem>)}
        </TextField>}
      </Card>
      <Card icon={<NotificationsNoneOutlined />} title="Notifications" intro="Notifications always appear under the bell at the top of the page.">
        {p && <FormControlLabel control={<Switch checked={p.emailNotifications} disabled={!p.organizationEmailEnabled} onChange={(e) => save({ emailNotifications: e.target.checked })} />}
          label="Also send them to me by e-mail" />}
        {p && !p.organizationEmailEnabled && <Alert severity="info" sx={{ mt: 1 }}>Your organization has turned off e-mail notifications for everyone.</Alert>}
        <Box sx={{ mt: 1 }}><Link component={RouterLink} to="/account/messaging">Text message and push notification settings</Link></Box>
      </Card>
      <Card icon={<DrawOutlined />} title="Password, two-step verification and signatures">
        <Typography variant="body2" sx={{ mb: 1 }}>
          Electronic signatures are confirmed by entering your password again when you sign, so your signature is protected by your password and two-step verification. There is no separate signature PIN.
        </Typography>
        <Button variant="outlined" component={RouterLink} to="/account/security">Change password or two-step verification</Button>
      </Card>
    </Box>
  );
}

interface Deleted { id: string; recordType: string; typeLabel: string; recordId: string; number: string; title: string; status: string | null; reason: string; deletedByName: string; deletedAt: string; wasLocked: boolean; restoredAt: string | null; restoredByName: string | null; restoreReason: string | null }

/** Records people deleted, with why, and a way to bring each back. */
export function DeletedRecordsPage() {
  const qc = useQueryClient();
  const [q, setQ] = useState('');
  const [history, setHistory] = useState(false);
  const [restore, setRestore] = useState<Deleted | null>(null);
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState<string | null>(null);
  const [term, setTerm] = useState('');
  useEffect(() => { const t = setTimeout(() => setTerm(q.trim()), 250); return () => clearTimeout(t); }, [q]);
  const list = useQuery({ queryKey: ['deleted-records', term, history], queryFn: () => get<Deleted[]>(`/api/deleted-records?q=${encodeURIComponent(term)}&includeRestored=${history}`) });
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Deleted records' }]} title="Deleted records" record={false}>
      <Typography color="text.secondary" sx={{ mb: 2 }}>
        Deleted records are hidden from every list, search and report but nothing is erased. You see the kinds of records you are allowed to delete.
      </Typography>
      {done && <Alert severity="success" sx={{ mb: 2 }} onClose={() => setDone(null)} data-testid="restore-done">{done}</Alert>}
      <Stack direction="row" gap={2} sx={{ mb: 2 }} flexWrap="wrap" alignItems="center">
        <TextField size="small" label="Search" value={q} onChange={(e) => setQ(e.target.value)} sx={{ width: 280 }} />
        <FormControlLabel control={<Switch checked={history} onChange={(e) => setHistory(e.target.checked)} />} label="Include restored" />
      </Stack>
      <Paper>
        <Table size="small">
          <TableHead><TableRow><TableCell>Record</TableCell><TableCell>Title</TableCell><TableCell>Deleted</TableCell><TableCell>By</TableCell><TableCell>Reason</TableCell><TableCell>Restored</TableCell><TableCell /></TableRow></TableHead>
          <TableBody>
            {(list.data ?? []).map((d) => (
              <TableRow key={d.id} data-testid="deleted-row">
                <TableCell>{d.typeLabel} {d.number}{d.wasLocked && <Chip size="small" label="was closed or signed" sx={{ ml: 1 }} />}</TableCell>
                <TableCell>{d.title}</TableCell>
                <TableCell>{fmtDateTime(d.deletedAt)}</TableCell>
                <TableCell>{d.deletedByName}</TableCell>
                <TableCell sx={{ maxWidth: 320 }}>{d.reason}</TableCell>
                <TableCell>{d.restoredAt ? `${fmtDateTime(d.restoredAt)} by ${d.restoredByName ?? ''}${d.restoreReason ? ` — ${d.restoreReason}` : ''}` : ''}</TableCell>
                <TableCell align="right">{!d.restoredAt && <Button size="small" startIcon={<RestoreFromTrashOutlined />} onClick={() => { setRestore(d); setReason(''); setError(null); }} data-testid="restore-open">Restore</Button>}</TableCell>
              </TableRow>
            ))}
            {list.data?.length === 0 && <TableRow><TableCell colSpan={7}><Typography color="text.secondary" sx={{ py: 3, textAlign: 'center' }}>No deleted records.</Typography></TableCell></TableRow>}
          </TableBody>
        </Table>
      </Paper>
      <Dialog open={!!restore} onClose={() => setRestore(null)} maxWidth="sm" fullWidth>
        <DialogTitle>Restore {restore?.typeLabel} {restore?.number}?</DialogTitle>
        <DialogContent>
          <DialogContentText sx={{ mb: 2 }}>It comes back exactly as it was when deleted, in every list and search. The restore is recorded in the audit trail.</DialogContentText>
          {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
          <TextField fullWidth multiline minRows={2} label="Reason for restoring" required value={reason} onChange={(e) => setReason(e.target.value)} helperText="Required. At least 5 characters."
            slotProps={{ htmlInput: { maxLength: 2000, 'data-testid': 'restore-reason' } }} />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setRestore(null)}>Cancel</Button>
          <Button variant="contained" disabled={reason.trim().length < 5} data-testid="restore-confirm" onClick={async () => {
            if (!restore) return;
            try {
              await post(`/api/deleted-records/${restore.id}/restore`, { reason: reason.trim() });
              setDone(`Restored ${restore.typeLabel} ${restore.number}.`); setRestore(null); await qc.invalidateQueries();
            } catch (e) { setError((e as Error).message); }
          }}>Restore</Button>
        </DialogActions>
      </Dialog>
    </ModuleWorkspace>
  );
}
