import { PageNav } from '../tabs/Workspace';
import { useState, type FormEvent, type ReactNode } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { Alert, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, Divider, Link, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, Typography, useMediaQuery } from '@mui/material';
import type { Theme } from '@mui/material/styles';
import ShieldOutlined from '@mui/icons-material/ShieldOutlined';
import KeyOutlined from '@mui/icons-material/KeyOutlined';
import DevicesOutlined from '@mui/icons-material/DevicesOutlined';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { del, get, post } from '../api';
import { useAuth, useBranding } from '../auth';
import { fmtDateTime } from '../components/Status';
import { AuthenticatorSetup, CodeField, NewPasswordFields, PasswordField, RecoveryCodes, passwordReady, usePasswordPolicy, type MfaSetupInfo } from '../components/SignInParts';

interface Device { id: string; name: string; ipAddress: string | null; createdAt: string; lastUsedAt: string | null; expiresAt: string; isThisBrowser: boolean }
interface Security { mfaEnabled: boolean; mfaEnabledAt: string | null; mfaRequired: boolean; mfaRequiredReason: string | null; recoveryCodesLeft: number; passwordChangedAt: string | null; trustedDevicesEnabled: boolean; trustedDeviceDays: number; trustedDevices: Device[]; isPortal: boolean }

function Section({ icon, title, status, children }: { icon: ReactNode; title: string; status?: ReactNode; children: ReactNode }) {
  return (
    <Paper component="section" aria-labelledby={`sec-${title}`} sx={{ p: { xs: 2, sm: 3 } }}>
      <Stack direction="row" alignItems="center" gap={1.5} sx={{ mb: 2 }} flexWrap="wrap">
        <Box sx={{ color: 'primary.main', display: 'flex' }} aria-hidden>{icon}</Box>
        <Typography variant="h3" component="h2" id={`sec-${title}`} sx={{ flex: 1, minWidth: 180 }}>{title}</Typography>
        {status}
      </Stack>
      {children}
    </Paper>
  );
}

/** The signed-in person's sign-in security: two-step verification, password, trusted browsers. */
export function AccountSecurity() {
  const qc = useQueryClient();
  const { me } = useAuth();
  const appName = useBranding().data?.applicationName ?? 'QualGuard IQ';
  const q = useQuery({ queryKey: ['account-security'], queryFn: () => get<Security>('/api/account/security') });
  const [dialog, setDialog] = useState<null | 'setup' | 'codes' | 'disable'>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const refresh = () => qc.invalidateQueries({ queryKey: ['account-security'] });
  const s = q.data;
  if (!s) return null;
  const done = (text: string) => { setDialog(null); setNotice(text); refresh(); };
  return (
    <Box sx={{ maxWidth: 960, mx: 'auto', p: { xs: 2, sm: 4 }, display: 'grid', gap: 2.5 }}>
      <Box>
        <Box sx={{ mb: 1 }}><PageNav /></Box>
        <Typography variant="h1">Security</Typography>
        <Typography color="text.secondary">How you sign in to {appName}. Changes here are recorded in the audit trail.</Typography>
      </Box>
      {notice && <Alert severity="success" onClose={() => setNotice(null)}>{notice}</Alert>}
      {error && <Alert severity="error" onClose={() => setError(null)}>{error}</Alert>}

      <Section icon={<ShieldOutlined />} title="Two-step verification"
        status={<Stack direction="row" gap={1}>{s.mfaRequired && <Chip size="small" variant="outlined" label="Required" />}
          <Chip size="small" color={s.mfaEnabled ? 'success' : 'default'} label={s.mfaEnabled ? 'On' : 'Off'} /></Stack>}>
        <Typography variant="body2" sx={{ mb: 2 }}>
          {s.mfaEnabled
            ? <>After your password, you enter a code from your authenticator app. Turned on {fmtDateTime(s.mfaEnabledAt)}.</>
            : <>Add a second step to sign-in: a 6-digit code from an authenticator app on your phone. Someone who learns your password still cannot sign in.</>}
        </Typography>
        {s.mfaRequired && s.mfaRequiredReason && <Alert severity="info" sx={{ mb: 2 }}>{s.mfaRequiredReason}</Alert>}
        {s.mfaEnabled && (
          <Alert severity={s.recoveryCodesLeft <= 3 ? 'warning' : 'info'} icon={<KeyOutlined />} sx={{ mb: 2 }}>
            {s.recoveryCodesLeft} of 10 recovery codes left.{s.recoveryCodesLeft <= 3 ? ' Create new ones so you are not locked out if you lose your phone.' : ''}
          </Alert>
        )}
        <Stack direction={{ xs: 'column', sm: 'row' }} gap={1}>
          <Button variant={s.mfaEnabled ? 'outlined' : 'contained'} onClick={() => setDialog('setup')}>{s.mfaEnabled ? 'Replace authenticator app' : 'Set up two-step verification'}</Button>
          {s.mfaEnabled && <Button variant="outlined" onClick={() => setDialog('codes')}>New recovery codes</Button>}
          {s.mfaEnabled && <Button color="error" disabled={s.mfaRequired} onClick={() => setDialog('disable')}>Turn off</Button>}
        </Stack>
      </Section>

      <ChangePassword email={me?.email ?? ''} changedAt={s.passwordChangedAt} onDone={() => { setNotice('Password changed. Other signed-in sessions were ended; this one continues.'); refresh(); }} />

      <Section icon={<DevicesOutlined />} title="Trusted browsers" status={<Chip size="small" label={`${s.trustedDevices.length}`} aria-label={`${s.trustedDevices.length} trusted browsers`} />}>
        <Typography variant="body2" sx={{ mb: 2 }}>
          {s.trustedDevicesEnabled
            ? <>Browsers where you ticked "Trust this browser". They skip the code for {s.trustedDeviceDays} days. Remove any you don't recognise or no longer use.</>
            : <>Your organization has turned off trusted browsers, so a code is asked at every sign-in.</>}
        </Typography>
        {s.trustedDevices.length === 0 ? <Typography variant="body2" color="text.secondary">No trusted browsers.</Typography> : (
          <DevicesTable devices={s.trustedDevices} onRemove={async (d) => { setError(null); try { await del(`/api/account/trusted-devices/${d.id}`); done(`${d.name} is no longer trusted.`); } catch (e) { setError((e as Error).message); } }} />
        )}
        {s.trustedDevices.length > 0 && (
          <Button sx={{ mt: 2 }} color="error" onClick={async () => { setError(null); try { const r = await post<{ revoked: number }>('/api/account/trusted-devices/revoke-all'); done(`${r.revoked} browser${r.revoked === 1 ? '' : 's'} removed. Each will ask for a code at the next sign-in.`); } catch (e) { setError((e as Error).message); } }}>
            Remove all trusted browsers
          </Button>
        )}
      </Section>

      {!s.isPortal && <Typography variant="body2" color="text.secondary">See recent sign-ins and security changes on your <Link component={RouterLink} to="/account/sign-ins">sign-in history</Link>.</Typography>}

      {dialog === 'setup' && <SetupDialog appName={appName} email={me?.email ?? ''} replacing={s.mfaEnabled} onClose={() => setDialog(null)} onDone={() => done('Two-step verification is on. Keep your recovery codes somewhere safe.')} />}
      {dialog === 'codes' && <NewCodesDialog appName={appName} email={me?.email ?? ''} onClose={() => setDialog(null)} onDone={() => done('New recovery codes created. The earlier codes no longer work.')} />}
      {dialog === 'disable' && <DisableDialog onClose={() => setDialog(null)} onDone={() => done('Two-step verification is off. Trusted browsers and recovery codes were removed.')} />}
    </Box>
  );
}

function DevicesTable({ devices, onRemove }: { devices: Device[]; onRemove: (d: Device) => void }) {
  const narrow = useMediaQuery((t: Theme) => t.breakpoints.down('sm'));
  if (narrow) return (
    <Stack divider={<Divider />} gap={1.5}>
      {devices.map((d) => (
        <Box key={d.id}>
          <Stack direction="row" alignItems="center" gap={1}><Typography fontWeight={600}>{d.name}</Typography>{d.isThisBrowser && <Chip size="small" color="primary" label="This browser" />}</Stack>
          <Typography variant="body2" color="text.secondary">Last used {fmtDateTime(d.lastUsedAt ?? d.createdAt)}{d.ipAddress ? ` from ${d.ipAddress}` : ''} · until {fmtDateTime(d.expiresAt)}</Typography>
          <Button size="small" color="error" onClick={() => onRemove(d)} aria-label={`Remove ${d.name}`}>Remove</Button>
        </Box>
      ))}
    </Stack>
  );
  return (
    <Table size="small" aria-label="Trusted browsers">
      <TableHead><TableRow><TableCell>Browser</TableCell><TableCell>Last used</TableCell><TableCell>IP address</TableCell><TableCell>Trusted until</TableCell><TableCell /></TableRow></TableHead>
      <TableBody>
        {devices.map((d) => (
          <TableRow key={d.id}>
            <TableCell><Stack direction="row" alignItems="center" gap={1}>{d.name}{d.isThisBrowser && <Chip size="small" color="primary" label="This browser" />}</Stack></TableCell>
            <TableCell>{fmtDateTime(d.lastUsedAt ?? d.createdAt)}</TableCell>
            <TableCell>{d.ipAddress ?? '—'}</TableCell>
            <TableCell>{fmtDateTime(d.expiresAt)}</TableCell>
            <TableCell align="right"><Button size="small" color="error" onClick={() => onRemove(d)} aria-label={`Remove ${d.name}`}>Remove</Button></TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

function ChangePassword({ email, changedAt, onDone }: { email: string; changedAt: string | null; onDone: () => void }) {
  const [current, setCurrent] = useState('');
  const [pw, setPw] = useState('');
  const [confirm, setConfirm] = useState('');
  const [errors, setErrors] = useState<{ current?: string; next?: string; form?: string }>({});
  const [busy, setBusy] = useState(false);
  const min = usePasswordPolicy().data?.minLength ?? 12;
  const submit = async (e: FormEvent) => {
    e.preventDefault(); setErrors({});
    if (!current) { setErrors({ current: 'Enter your current password.' }); return; }
    if (!passwordReady(pw, confirm, min)) { setErrors({ form: pw !== confirm ? 'The two new passwords do not match.' : 'The new password does not meet the rules yet.' }); return; }
    setBusy(true);
    try { await post('/api/account/password', { currentPassword: current, newPassword: pw }); setCurrent(''); setPw(''); setConfirm(''); onDone(); }
    catch (x) {
      const ex = x as { message: string; errors?: Record<string, string[]> };
      setErrors(ex.errors?.currentPassword ? { current: ex.errors.currentPassword[0] } : ex.errors?.newPassword ? { next: ex.errors.newPassword.join(' ') } : { form: ex.message });
    } finally { setBusy(false); }
  };
  return (
    <Section icon={<KeyOutlined />} title="Password" status={changedAt ? <Typography variant="caption" color="text.secondary">Last changed {fmtDateTime(changedAt)}</Typography> : undefined}>
      <Box component="form" noValidate onSubmit={submit} sx={{ display: 'grid', gap: 2, maxWidth: 440 }}>
        {errors.form && <Alert severity="error">{errors.form}</Alert>}
        <input type="text" name="username" autoComplete="username" value={email} readOnly hidden />
        <PasswordField label="Current password" value={current} onChange={setCurrent} autoComplete="current-password" error={errors.current} />
        <NewPasswordFields value={pw} confirm={confirm} onChange={setPw} onConfirm={setConfirm} email={email} serverError={errors.next} />
        <Box><Button type="submit" variant="contained" disabled={busy}>Change password</Button></Box>
      </Box>
    </Section>
  );
}

function SetupDialog({ appName, email, replacing, onClose, onDone }: { appName: string; email: string; replacing: boolean; onClose: () => void; onDone: () => void }) {
  const full = useMediaQuery((t: Theme) => t.breakpoints.down('sm'));
  const [password, setPassword] = useState('');
  const [setup, setSetup] = useState<MfaSetupInfo | null>(null);
  const [code, setCode] = useState('');
  const [codes, setCodes] = useState<string[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const run = async (f: () => Promise<void>) => { setError(null); setBusy(true); try { await f(); } catch (e) { setError((e as Error).message); } finally { setBusy(false); } };
  return (
    <Dialog open onClose={codes ? undefined : onClose} fullWidth maxWidth="xs" fullScreen={full} aria-labelledby="setup-title">
      <DialogTitle id="setup-title">{codes ? 'Save your recovery codes' : replacing ? 'Replace authenticator app' : 'Set up two-step verification'}</DialogTitle>
      <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
        {error && <Alert severity="error">{error}</Alert>}
        {codes ? <RecoveryCodes codes={codes} appName={appName} account={email} onDone={onDone} doneLabel="Done" />
          : !setup ? (
            <Box component="form" id="setup-pw" noValidate sx={{ display: 'grid', gap: 2 }} onSubmit={(e: FormEvent) => { e.preventDefault(); void run(async () => setSetup(await post<MfaSetupInfo>('/api/account/mfa/setup', { password }))); }}>
              <Typography variant="body2">{replacing ? 'The new app replaces the old one. Browsers you trusted will ask for a code again.' : 'Enter your password to continue.'}</Typography>
              <PasswordField label="Your password" value={password} onChange={setPassword} autoComplete="current-password" autoFocus />
            </Box>
          ) : (
            <Box component="form" id="setup-code" noValidate sx={{ display: 'grid', gap: 2 }} onSubmit={(e: FormEvent) => { e.preventDefault(); void run(async () => setCodes((await post<{ recoveryCodes: string[] }>('/api/account/mfa/enable', { code })).recoveryCodes)); }}>
              <AuthenticatorSetup setup={setup} />
              <CodeField value={code} onChange={setCode} autoFocus={false} />
            </Box>
          )}
      </DialogContent>
      {!codes && (
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button variant="contained" type="submit" form={setup ? 'setup-code' : 'setup-pw'} disabled={busy || (setup ? code.length !== 6 : !password)}>{setup ? 'Turn on' : 'Continue'}</Button>
        </DialogActions>
      )}
    </Dialog>
  );
}

function NewCodesDialog({ appName, email, onClose, onDone }: { appName: string; email: string; onClose: () => void; onDone: () => void }) {
  const full = useMediaQuery((t: Theme) => t.breakpoints.down('sm'));
  const [code, setCode] = useState('');
  const [codes, setCodes] = useState<string[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  return (
    <Dialog open onClose={codes ? undefined : onClose} fullWidth maxWidth="xs" fullScreen={full} aria-labelledby="codes-title">
      <DialogTitle id="codes-title">{codes ? 'Save your new recovery codes' : 'New recovery codes'}</DialogTitle>
      <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
        {error && <Alert severity="error">{error}</Alert>}
        {codes ? <RecoveryCodes codes={codes} appName={appName} account={email} onDone={onDone} doneLabel="Done" /> : (
          <Box component="form" id="codes-form" noValidate sx={{ display: 'grid', gap: 2 }} onSubmit={async (e: FormEvent) => {
            e.preventDefault(); setError(null);
            try { setCodes((await post<{ recoveryCodes: string[] }>('/api/account/mfa/recovery-codes', { code })).recoveryCodes); } catch (x) { setError((x as Error).message); setCode(''); }
          }}>
            <Typography variant="body2">Your current recovery codes stop working as soon as the new ones are created. Confirm with a code from your authenticator app.</Typography>
            <CodeField value={code} onChange={setCode} />
          </Box>
        )}
      </DialogContent>
      {!codes && <DialogActions><Button onClick={onClose}>Cancel</Button><Button variant="contained" type="submit" form="codes-form" disabled={code.length !== 6}>Create new codes</Button></DialogActions>}
    </Dialog>
  );
}

function DisableDialog({ onClose, onDone }: { onClose: () => void; onDone: () => void }) {
  const full = useMediaQuery((t: Theme) => t.breakpoints.down('sm'));
  const [password, setPassword] = useState('');
  const [code, setCode] = useState('');
  const [error, setError] = useState<string | null>(null);
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs" fullScreen={full} aria-labelledby="disable-title">
      <DialogTitle id="disable-title">Turn off two-step verification</DialogTitle>
      <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
        <Alert severity="warning">Without a second step, anyone who learns your password can sign in. Your recovery codes and trusted browsers are removed.</Alert>
        {error && <Alert severity="error">{error}</Alert>}
        <Box component="form" id="disable-form" noValidate sx={{ display: 'grid', gap: 2 }} onSubmit={async (e: FormEvent) => {
          e.preventDefault(); setError(null);
          try { await post('/api/account/mfa/disable', { password, code }); onDone(); } catch (x) { setError((x as Error).message); }
        }}>
          <PasswordField label="Your password" value={password} onChange={setPassword} autoComplete="current-password" autoFocus />
          <CodeField value={code} onChange={setCode} autoFocus={false} />
        </Box>
      </DialogContent>
      <DialogActions><Button onClick={onClose}>Cancel</Button><Button color="error" variant="contained" type="submit" form="disable-form" disabled={!password || code.length !== 6}>Turn off</Button></DialogActions>
    </Dialog>
  );
}
