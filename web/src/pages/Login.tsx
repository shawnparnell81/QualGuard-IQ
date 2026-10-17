import { useEffect, useState, type FormEvent } from 'react';
import { Alert, Box, Button, Checkbox, CircularProgress, FormControlLabel, Link, Stack, TextField, Typography } from '@mui/material';
import { ApiError, post } from '../api';
import { useAuth, useBranding } from '../auth';
import { AuthShell } from '../components/AuthShell';
import { AuthenticatorSetup, CodeField, NewPasswordFields, PasswordField, RecoveryCodes, StepTimer, passwordReady, usePasswordPolicy, type MfaSetupInfo } from '../components/SignInParts';

interface StepResponse { next: 'mfa' | 'enrol' | 'change-password'; challenge: string; expiresInSeconds: number; email: string; trustAvailable: boolean; trustDays: number; reason: string | null; recoveryCodesLeft: number }
interface Step extends StepResponse { deadline: number }

type View =
  | { kind: 'credentials'; notice?: string }
  | { kind: 'step'; step: Step }
  | { kind: 'codes'; codes: string[]; email: string }
  | { kind: 'expired'; message: string }
  | { kind: 'forgot' }
  | { kind: 'forgot-sent'; message: string }
  | { kind: 'reset'; token: string };

const msg = (e: unknown) => (e as Error).message;
const isStepEnd = (e: unknown) => e instanceof ApiError && (e.code === 'challenge_expired' || e.code === 'challenge_invalid' || e.status === 423);

/**
 * Every signed-out screen, all on the shared AuthShell frame: email and password, then (only when needed) a new password,
 * an authenticator or recovery code, or authenticator set-up with recovery codes; plus forgot and reset password.
 * Each later step stays open for a few minutes; when it times out the screen says so and offers to start again.
 */
export function Login() {
  const { data: brand } = useBranding();
  const appName = brand?.applicationName ?? 'QualGuard IQ';
  const resetToken = window.location.pathname === '/reset-password' ? new URLSearchParams(window.location.search).get('token') : null;
  const [view, setView] = useState<View>(resetToken ? { kind: 'reset', token: resetToken } : { kind: 'credentials' });
  const [email, setEmail] = useState('');
  const { signedIn } = useAuth();
  const toStep = (s: StepResponse) => setView({ kind: 'step', step: { ...s, deadline: Date.now() + s.expiresInSeconds * 1000 } });
  const startAgain = (notice?: string) => setView({ kind: 'credentials', notice });
  const leaveResetUrl = () => { if (window.location.pathname === '/reset-password') window.history.replaceState(null, '', '/'); };
  const org = brand?.organizationName;

  switch (view.kind) {
    case 'credentials':
      return <Credentials email={email} setEmail={setEmail} notice={view.notice} org={org} onStep={toStep} onDone={signedIn} onForgot={() => setView({ kind: 'forgot' })} />;
    case 'expired':
      return (
        <AuthShell title="Start again" organization={org}>
          <Alert severity="warning" sx={{ mb: 2 }}>{view.message}</Alert>
          <Button variant="contained" size="large" fullWidth autoFocus onClick={() => startAgain()}>Start again</Button>
        </AuthShell>
      );
    case 'step': {
      const s = view.step;
      const expire = (m?: string) => setView({ kind: 'expired', message: m ?? 'This sign-in step timed out. For your security each step stays open for a few minutes. Start again: enter your email and password.' });
      const fail = (e: unknown) => { if (isStepEnd(e)) expire(msg(e)); else throw e; };
      if (s.next === 'change-password') return <ChangeRequired step={s} org={org} onExpire={expire} onStep={toStep} onDone={signedIn} fail={fail} />;
      if (s.next === 'mfa') return <EnterCode step={s} org={org} onExpire={expire} onDone={signedIn} fail={fail} onStartAgain={() => startAgain()} />;
      return <Enrol step={s} org={org} onExpire={expire} fail={fail} onCodes={(codes) => setView({ kind: 'codes', codes, email: s.email })} />;
    }
    case 'codes':
      return (
        <AuthShell title="Save your recovery codes" subtitle="Two-step verification is on." organization={org}>
          <RecoveryCodes codes={view.codes} appName={appName} account={view.email} onDone={signedIn} doneLabel={`Continue to ${appName}`} />
        </AuthShell>
      );
    case 'forgot':
      return <Forgot email={email} setEmail={setEmail} org={org} onSent={(m) => setView({ kind: 'forgot-sent', message: m })} onBack={() => startAgain()} />;
    case 'forgot-sent':
      return (
        <AuthShell title="Check your email" organization={org}>
          <Alert severity="info" sx={{ mb: 2 }}>{view.message}</Alert>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>The link works once and expires after a while. If nothing arrives in a few minutes, ask again or contact your administrator.</Typography>
          <Button fullWidth onClick={() => startAgain()}>Back to sign in</Button>
        </AuthShell>
      );
    case 'reset':
      return <ResetPassword token={view.token} org={org} onDone={() => { leaveResetUrl(); startAgain('Your password was changed and every signed-in session was ended. Sign in with your new password.'); }}
        onNewLink={() => { leaveResetUrl(); setView({ kind: 'forgot' }); }} />;
  }
}

function Credentials({ email, setEmail, notice, org, onStep, onDone, onForgot }: { email: string; setEmail: (v: string) => void; notice?: string; org?: string; onStep: (s: StepResponse) => void; onDone: () => Promise<void>; onForgot: () => void }) {
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [touched, setTouched] = useState(false);
  const emailOk = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email);
  const submit = async (e: FormEvent) => {
    e.preventDefault(); setTouched(true); setError(null);
    if (!emailOk || !password) return;
    setBusy(true);
    try {
      const r = await post<StepResponse | undefined>('/api/auth/login', { email, password });
      if (r?.next) onStep(r); else await onDone();
    } catch (x) { setError(msg(x)); setBusy(false); }
  };
  return (
    <AuthShell title="Sign in" subtitle="Use your work email and password." organization={org}>
      <Box component="form" noValidate onSubmit={submit} sx={{ display: 'grid', gap: 2 }}>
        {notice && <Alert severity="success">{notice}</Alert>}
        {error && <Alert severity="error">{error}</Alert>}
        <TextField label="Email" type="email" autoComplete="username" autoFocus value={email} onChange={(e) => setEmail(e.target.value)}
          error={touched && !emailOk} helperText={touched && !emailOk ? 'Enter a valid email' : undefined} />
        <PasswordField label="Password" value={password} onChange={setPassword} autoComplete="current-password" error={touched && !password ? 'Required' : null} />
        <Button type="submit" variant="contained" size="large" disabled={busy} sx={{ mt: 0.5, py: 1.1 }}>{busy ? <CircularProgress size={22} color="inherit" aria-label="Signing in" /> : 'Sign in'}</Button>
        <Box sx={{ textAlign: 'center' }}><Link component="button" type="button" variant="body2" onClick={onForgot}>Forgot your password?</Link></Box>
      </Box>
    </AuthShell>
  );
}

function EnterCode({ step, org, onExpire, onDone, fail, onStartAgain }: { step: Step; org?: string; onExpire: (m?: string) => void; onDone: () => Promise<void>; fail: (e: unknown) => void; onStartAgain: () => void }) {
  const [code, setCode] = useState('');
  const [recovery, setRecovery] = useState(false);
  const [trust, setTrust] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const submit = async (e?: FormEvent) => {
    e?.preventDefault(); setError(null);
    if (!recovery && code.length !== 6) { setError('Enter all 6 digits.'); return; }
    if (recovery && code.replace(/[^a-z0-9]/gi, '').length !== 10) { setError('A recovery code has 10 letters and numbers.'); return; }
    setBusy(true);
    try { await post('/api/auth/mfa/verify', { challenge: step.challenge, code, trustDevice: trust }); await onDone(); }
    catch (x) { setBusy(false); try { fail(x); } catch { setError(msg(x)); setCode(''); } }
  };
  // submit automatically when the sixth digit is typed or pasted
  useEffect(() => { if (!recovery && code.length === 6 && !busy) void submit(); }, [code]); // eslint-disable-line react-hooks/exhaustive-deps
  return (
    <AuthShell title="Two-step verification" subtitle={recovery ? 'Enter one of your recovery codes.' : `Enter the code from your authenticator app for ${step.email}.`} organization={org}>
      <Box component="form" noValidate onSubmit={submit} sx={{ display: 'grid', gap: 2 }}>
        <StepTimer expiresAt={step.deadline} onExpired={() => onExpire()} />
        {error && <Alert severity="error">{error}</Alert>}
        <CodeField key={recovery ? 'r' : 'c'} value={code} onChange={setCode} recovery={recovery} />
        {step.trustAvailable && (
          <FormControlLabel control={<Checkbox checked={trust} onChange={(e) => setTrust(e.target.checked)} />}
            label={<span>Trust this browser for {step.trustDays} days<Typography component="span" variant="caption" color="text.secondary" display="block">Don't tick this on a shared or public computer.</Typography></span>} />
        )}
        <Button type="submit" variant="contained" size="large" disabled={busy}>{busy ? <CircularProgress size={22} color="inherit" aria-label="Checking code" /> : 'Verify'}</Button>
        <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems="center" gap={1}>
          <Link component="button" type="button" variant="body2" onClick={() => { setRecovery(!recovery); setCode(''); setError(null); }}>
            {recovery ? 'Use my authenticator app' : "Can't use your app? Use a recovery code"}
          </Link>
          <Link component="button" type="button" variant="body2" onClick={onStartAgain}>Start again</Link>
        </Stack>
        {recovery && step.recoveryCodesLeft <= 3 && <Alert severity="warning">You have {step.recoveryCodesLeft} recovery code{step.recoveryCodesLeft === 1 ? '' : 's'} left. Create new ones on your Security page after signing in.</Alert>}
      </Box>
    </AuthShell>
  );
}

function Enrol({ step, org, onExpire, fail, onCodes }: { step: Step; org?: string; onExpire: (m?: string) => void; fail: (e: unknown) => void; onCodes: (codes: string[]) => void }) {
  const [setup, setSetup] = useState<MfaSetupInfo | null>(null);
  const [code, setCode] = useState('');
  const [trust, setTrust] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    post<MfaSetupInfo>('/api/auth/mfa/enrol/start', { challenge: step.challenge }).then(setSetup).catch((x) => { try { fail(x); } catch { setError(msg(x)); } });
  }, [step.challenge]); // eslint-disable-line react-hooks/exhaustive-deps
  const submit = async (e: FormEvent) => {
    e.preventDefault(); setError(null);
    if (code.length !== 6) { setError('Enter all 6 digits.'); return; }
    setBusy(true);
    try { const r = await post<{ recoveryCodes: string[] }>('/api/auth/mfa/enrol/confirm', { challenge: step.challenge, code, trustDevice: trust }); onCodes(r.recoveryCodes); }
    catch (x) { setBusy(false); try { fail(x); } catch { setError(msg(x)); setCode(''); } }
  };
  return (
    <AuthShell title="Set up two-step verification" subtitle={step.reason ?? 'Your account needs a second step at sign-in.'} organization={org}>
      <Box component="form" noValidate onSubmit={submit} sx={{ display: 'grid', gap: 2 }}>
        <StepTimer expiresAt={step.deadline} onExpired={() => onExpire()} />
        {error && <Alert severity="error">{error}</Alert>}
        {!setup ? <Box sx={{ display: 'grid', placeItems: 'center', py: 4 }}><CircularProgress aria-label="Preparing set-up" /></Box> : <AuthenticatorSetup setup={setup} />}
        <CodeField value={code} onChange={setCode} autoFocus={false} />
        {step.trustAvailable && <FormControlLabel control={<Checkbox checked={trust} onChange={(e) => setTrust(e.target.checked)} />} label={`Trust this browser for ${step.trustDays} days`} />}
        <Button type="submit" variant="contained" size="large" disabled={busy || !setup}>{busy ? <CircularProgress size={22} color="inherit" aria-label="Checking code" /> : 'Turn on and continue'}</Button>
      </Box>
    </AuthShell>
  );
}

function ChangeRequired({ step, org, onExpire, onStep, onDone, fail }: { step: Step; org?: string; onExpire: (m?: string) => void; onStep: (s: StepResponse) => void; onDone: () => Promise<void>; fail: (e: unknown) => void }) {
  const [pw, setPw] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const min = usePasswordPolicy().data?.minLength ?? 12;
  const submit = async (e: FormEvent) => {
    e.preventDefault(); setError(null);
    if (!passwordReady(pw, confirm, min)) { setError(pw !== confirm ? 'The two passwords do not match.' : 'The new password does not meet the rules below yet.'); return; }
    setBusy(true);
    try {
      const r = await post<StepResponse | undefined>('/api/auth/password/change-required', { challenge: step.challenge, newPassword: pw });
      if (r?.next) onStep(r); else await onDone();
    } catch (x) { setBusy(false); try { fail(x); } catch { setError(msg(x)); } }
  };
  return (
    <AuthShell title="Choose a new password" subtitle={step.reason ?? 'Choose your own password to continue.'} organization={org}>
      <Box component="form" noValidate onSubmit={submit} sx={{ display: 'grid', gap: 2 }}>
        <StepTimer expiresAt={step.deadline} onExpired={() => onExpire()} />
        {error && <Alert severity="error">{error}</Alert>}
        <input type="text" name="username" autoComplete="username" value={step.email} readOnly hidden />
        <NewPasswordFields value={pw} confirm={confirm} onChange={setPw} onConfirm={setConfirm} email={step.email} autoFocus />
        <Button type="submit" variant="contained" size="large" disabled={busy}>{busy ? <CircularProgress size={22} color="inherit" aria-label="Saving" /> : 'Save password and continue'}</Button>
      </Box>
    </AuthShell>
  );
}

function Forgot({ email, setEmail, org, onSent, onBack }: { email: string; setEmail: (v: string) => void; org?: string; onSent: (m: string) => void; onBack: () => void }) {
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const ok = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email);
  const submit = async (e: FormEvent) => {
    e.preventDefault(); setError(null);
    if (!ok) { setError('Enter the email you sign in with.'); return; }
    setBusy(true);
    try { const r = await post<{ title: string }>('/api/auth/password/forgot', { email }); onSent(r.title); }
    catch (x) { setError(msg(x)); setBusy(false); }
  };
  return (
    <AuthShell title="Reset your password" subtitle="We'll email you a link to choose a new password." organization={org}>
      <Box component="form" noValidate onSubmit={submit} sx={{ display: 'grid', gap: 2 }}>
        {error && <Alert severity="error">{error}</Alert>}
        <TextField label="Email" type="email" autoComplete="username" autoFocus value={email} onChange={(e) => setEmail(e.target.value)} />
        <Button type="submit" variant="contained" size="large" disabled={busy}>{busy ? <CircularProgress size={22} color="inherit" aria-label="Sending" /> : 'Email me a reset link'}</Button>
        <Button onClick={onBack}>Back to sign in</Button>
      </Box>
    </AuthShell>
  );
}

function ResetPassword({ token, org, onDone, onNewLink }: { token: string; org?: string; onDone: () => void; onNewLink: () => void }) {
  const [valid, setValid] = useState<boolean | null>(null);
  const [pw, setPw] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const min = usePasswordPolicy().data?.minLength ?? 12;
  useEffect(() => { post<{ valid: boolean }>('/api/auth/password/reset/check', { challenge: token }).then((r) => setValid(r.valid)).catch(() => setValid(false)); }, [token]);
  const submit = async (e: FormEvent) => {
    e.preventDefault(); setError(null);
    if (!passwordReady(pw, confirm, min)) { setError(pw !== confirm ? 'The two passwords do not match.' : 'The new password does not meet the rules below yet.'); return; }
    setBusy(true);
    try { await post('/api/auth/password/reset', { token, newPassword: pw }); onDone(); }
    catch (x) { setError(msg(x)); setBusy(false); }
  };
  if (valid === null) return <AuthShell title="Reset your password" organization={org}><Box sx={{ display: 'grid', placeItems: 'center', py: 4 }}><CircularProgress aria-label="Checking link" /></Box></AuthShell>;
  if (!valid) return (
    <AuthShell title="This link no longer works" organization={org}>
      <Alert severity="warning" sx={{ mb: 2 }}>Reset links work once and expire after a short time. A newer link also replaces older ones.</Alert>
      <Button variant="contained" size="large" fullWidth onClick={onNewLink}>Get a new link</Button>
    </AuthShell>
  );
  return (
    <AuthShell title="Choose a new password" subtitle="After saving, every signed-in session ends and you sign in again." organization={org}>
      <Box component="form" noValidate onSubmit={submit} sx={{ display: 'grid', gap: 2 }}>
        {error && <Alert severity="error">{error}</Alert>}
        <NewPasswordFields value={pw} confirm={confirm} onChange={setPw} onConfirm={setConfirm} autoFocus />
        <Button type="submit" variant="contained" size="large" disabled={busy}>{busy ? <CircularProgress size={22} color="inherit" aria-label="Saving" /> : 'Save new password'}</Button>
      </Box>
    </AuthShell>
  );
}
