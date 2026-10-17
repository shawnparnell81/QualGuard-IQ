import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { Alert, Box, Button, Checkbox, FormControlLabel, IconButton, InputAdornment, LinearProgress, Stack, TextField, Tooltip, Typography } from '@mui/material';
import CheckCircleOutline from '@mui/icons-material/CheckCircleOutline';
import RadioButtonUnchecked from '@mui/icons-material/RadioButtonUnchecked';
import ContentCopy from '@mui/icons-material/ContentCopy';
import Download from '@mui/icons-material/Download';
import Visibility from '@mui/icons-material/Visibility';
import VisibilityOff from '@mui/icons-material/VisibilityOff';
import { useQuery } from '@tanstack/react-query';
import { get } from '../api';
import { mono } from '../theme';

export interface PasswordPolicy { minLength: number; maxLength: number; historyCount: number; blocksCommonPasswords: boolean; challengeMinutes: number; resetLinkMinutes: number }
export const usePasswordPolicy = () => useQuery({ queryKey: ['password-policy'], queryFn: () => get<PasswordPolicy>('/api/auth/password-policy'), staleTime: 600_000 });

/** Password text box with a show/hide button. */
export function PasswordField(props: { label: string; value: string; onChange: (v: string) => void; autoComplete: string; autoFocus?: boolean; error?: string | null; helperText?: ReactNode; name?: string }) {
  const [show, setShow] = useState(false);
  return (
    <TextField label={props.label} name={props.name} type={show ? 'text' : 'password'} value={props.value} autoComplete={props.autoComplete} autoFocus={props.autoFocus}
      onChange={(e) => props.onChange(e.target.value)} error={!!props.error} helperText={props.error ?? props.helperText} fullWidth
      slotProps={{ input: { endAdornment: (
        <InputAdornment position="end">
          <IconButton edge="end" onClick={() => setShow(!show)} aria-label={show ? `Hide ${props.label.toLowerCase()}` : `Show ${props.label.toLowerCase()}`} size="small">
            {show ? <VisibilityOff fontSize="small" /> : <Visibility fontSize="small" />}
          </IconButton>
        </InputAdornment>) } }} />
  );
}

/** New password + confirmation with the live rule list. Rules the browser cannot check (breached list, history) are listed and checked by the server. */
export function NewPasswordFields({ value, confirm, onChange, onConfirm, email, serverError, autoFocus, label = 'New password' }: {
  value: string; confirm: string; onChange: (v: string) => void; onConfirm: (v: string) => void; email?: string; serverError?: string | null; autoFocus?: boolean; label?: string;
}) {
  const policy = usePasswordPolicy().data;
  const min = policy?.minLength ?? 12;
  const local = (email ?? '').split('@')[0].toLowerCase();
  const rules = useMemo(() => [
    { ok: value.length >= min, text: `At least ${min} characters (a few unrelated words work well)` },
    { ok: new Set(value).size >= 4, text: 'At least four different characters' },
    { ok: !value || local.length < 4 || !value.toLowerCase().includes(local), text: 'Does not contain your email name' },
    { ok: !!value && value === confirm, text: 'Both entries match' },
  ], [value, confirm, min, local]);
  return (
    <Box sx={{ display: 'grid', gap: 2 }}>
      <PasswordField label={label} name="new-password" value={value} onChange={onChange} autoComplete="new-password" autoFocus={autoFocus} error={serverError} />
      <PasswordField label={`Confirm ${label.toLowerCase()}`} name="confirm-password" value={confirm} onChange={onConfirm} autoComplete="new-password" />
      <Box component="ul" aria-label="Password rules" aria-live="polite" sx={{ listStyle: 'none', m: 0, p: 0, display: 'grid', gap: 0.5 }}>
        {rules.map((r) => (
          <Box component="li" key={r.text} sx={{ display: 'flex', alignItems: 'center', gap: 1, color: r.ok ? 'success.main' : 'text.secondary', fontSize: 13 }}>
            {r.ok ? <CheckCircleOutline sx={{ fontSize: 16 }} aria-hidden /> : <RadioButtonUnchecked sx={{ fontSize: 16 }} aria-hidden />}
            <span>{r.text}<Box component="span" sx={visuallyHidden}>{r.ok ? ' (met)' : ' (not met yet)'}</Box></span>
          </Box>
        ))}
        {policy && (
          <Box component="li" sx={{ fontSize: 13, color: 'text.secondary', pl: 3 }}>
            {policy.blocksCommonPasswords ? 'Not a known breached or common password' : ''}{policy.blocksCommonPasswords && policy.historyCount > 0 ? ', and ' : ''}
            {policy.historyCount > 0 ? `not one of your last ${policy.historyCount}` : ''}{policy.blocksCommonPasswords || policy.historyCount > 0 ? ' (checked when you continue).' : ''}
          </Box>
        )}
      </Box>
    </Box>
  );
}

export const passwordReady = (value: string, confirm: string, min = 12) => value.length >= min && value === confirm && new Set(value).size >= 4;

/** Six-digit code (or a recovery code when allowed). Numeric keyboard on phones, one-time-code autofill. */
export function CodeField({ value, onChange, recovery, error, autoFocus = true, label }: { value: string; onChange: (v: string) => void; recovery?: boolean; error?: string | null; autoFocus?: boolean; label?: string }) {
  return (
    <TextField label={label ?? (recovery ? 'Recovery code' : 'Authentication code')} value={value} autoFocus={autoFocus} fullWidth
      onChange={(e) => onChange(recovery ? e.target.value.slice(0, 20) : e.target.value.replace(/[^0-9]/g, '').slice(0, 6))}
      error={!!error} helperText={error ?? (recovery ? 'Ten characters, for example abcde-23456. Each code works once.' : 'The 6-digit code your authenticator app shows now.')}
      slotProps={{ htmlInput: recovery
        ? { autoComplete: 'off', autoCapitalize: 'none', spellCheck: false, style: { fontFamily: mono, letterSpacing: 2 } }
        : { inputMode: 'numeric', pattern: '[0-9]*', autoComplete: 'one-time-code', maxLength: 6, style: { fontFamily: mono, fontSize: 22, letterSpacing: 8, textAlign: 'center' } } }} />
  );
}

/** Time left on a sign-in step, with a bar. Calls onExpired once when it reaches zero. */
export function StepTimer({ expiresAt, onExpired }: { expiresAt: number; onExpired: () => void }) {
  const total = useMemo(() => Math.max(1, expiresAt - Date.now()), [expiresAt]); // the step's full length, fixed when it starts
  const [left, setLeft] = useState(total);
  useEffect(() => {
    const t = setInterval(() => {
      const l = expiresAt - Date.now();
      setLeft(l);
      if (l <= 0) { clearInterval(t); onExpired(); }
    }, 1000);
    return () => clearInterval(t);
  }, [expiresAt]); // eslint-disable-line react-hooks/exhaustive-deps
  const s = Math.max(0, Math.round(left / 1000));
  const low = s <= 60;
  return (
    <Box aria-live={low ? 'polite' : 'off'}>
      <Stack direction="row" justifyContent="space-between" sx={{ mb: 0.5 }}>
        <Typography variant="caption" color={low ? 'warning.main' : 'text.secondary'}>{low ? 'Less than a minute left for this step' : `Finish this step within ${Math.max(1, Math.round(total / 60000))} minutes`}</Typography>
        <Typography variant="caption" sx={{ fontFamily: mono }} color={low ? 'warning.main' : 'text.secondary'} aria-label={`${Math.floor(s / 60)} minutes ${s % 60} seconds left`}>
          {Math.floor(s / 60)}:{String(s % 60).padStart(2, '0')}
        </Typography>
      </Stack>
      <LinearProgress variant="determinate" value={Math.max(0, Math.min(100, (left / total) * 100))} color={low ? 'warning' : 'primary'} sx={{ height: 3, borderRadius: 2 }} aria-hidden />
    </Box>
  );
}

export interface MfaSetupInfo { key: string; keyGrouped: string; otpAuthUri: string; qrSvg: string; issuer: string; account: string; expiresAt: string }

/** QR code and manual key for an authenticator app. */
export function AuthenticatorSetup({ setup }: { setup: MfaSetupInfo }) {
  const [copied, setCopied] = useState(false);
  return (
    <Box sx={{ display: 'grid', gap: 2 }}>
      <Box component="ol" sx={{ m: 0, pl: 2.5, display: 'grid', gap: 0.75, '& li': { fontSize: 14 } }}>
        <li>Open an authenticator app on your phone (for example Microsoft Authenticator, Google Authenticator, 1Password or Authy).</li>
        <li>Add an account and scan this QR code. Or, if you can't scan, type the key below.</li>
        <li>Enter the 6-digit code the app shows.</li>
      </Box>
      <Box sx={{ display: 'flex', justifyContent: 'center' }}>
        <Box role="img" aria-label={`QR code for ${setup.issuer} (${setup.account}). The same key is shown as text below.`}
          sx={{ width: 200, height: 200, p: 1, bgcolor: '#FFFFFF', borderRadius: 1, border: 1, borderColor: 'divider', '& svg': { width: '100%', height: '100%' } }}
          dangerouslySetInnerHTML={{ __html: setup.qrSvg }} />
      </Box>
      <Box>
        <Typography variant="caption" color="text.secondary" component="div" id="mfa-key-label">Key for manual entry ({setup.issuer}, time-based)</Typography>
        <Stack direction="row" alignItems="center" gap={1} sx={{ mt: 0.5, p: 1, borderRadius: 1, bgcolor: 'action.hover' }}>
          <Typography aria-labelledby="mfa-key-label" data-testid="mfa-key" sx={{ fontFamily: mono, fontSize: 15, letterSpacing: 1, wordBreak: 'break-all', flex: 1 }}>{setup.keyGrouped}</Typography>
          <Tooltip title={copied ? 'Copied' : 'Copy key'}>
            <IconButton size="small" aria-label="Copy key" onClick={async () => { try { await navigator.clipboard.writeText(setup.key); setCopied(true); } catch { /* clipboard blocked */ } }}><ContentCopy fontSize="small" /></IconButton>
          </Tooltip>
        </Stack>
      </Box>
    </Box>
  );
}

/** Shows new recovery codes once, with copy and download, and asks the person to confirm they saved them. */
export function RecoveryCodes({ codes, appName, account, onDone, doneLabel = 'Continue' }: { codes: string[]; appName: string; account: string; onDone: () => void; doneLabel?: string }) {
  const [saved, setSaved] = useState(false);
  const [copied, setCopied] = useState(false);
  const text = `${appName} recovery codes for ${account}\nCreated ${new Date().toLocaleString()}\nEach code works once. Keep them somewhere safe, away from your phone.\n\n${codes.join('\n')}\n`;
  return (
    <Box sx={{ display: 'grid', gap: 2 }}>
      <Alert severity="warning">Save these codes now. They are shown only once. If you lose your phone, a recovery code is the only way to sign in without an administrator.</Alert>
      <Box component="ol" aria-label="Recovery codes" data-testid="recovery-codes"
        sx={{ m: 0, p: 1.5, listStyle: 'none', display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', sm: '1fr 1fr' }, gap: 1, borderRadius: 1, bgcolor: 'action.hover', fontFamily: mono, fontSize: 15 }}>
        {codes.map((c) => <li key={c}>{c}</li>)}
      </Box>
      <Stack direction="row" gap={1} flexWrap="wrap">
        <Button variant="outlined" size="small" startIcon={<ContentCopy />} onClick={async () => { try { await navigator.clipboard.writeText(codes.join('\n')); setCopied(true); } catch { /* blocked */ } }}>{copied ? 'Copied' : 'Copy'}</Button>
        <Button variant="outlined" size="small" startIcon={<Download />} onClick={() => {
          const a = document.createElement('a');
          a.href = URL.createObjectURL(new Blob([text], { type: 'text/plain' }));
          a.download = `${appName.replace(/\s+/g, '-').toLowerCase()}-recovery-codes.txt`;
          a.click(); URL.revokeObjectURL(a.href);
        }}>Download</Button>
      </Stack>
      <FormControlLabel control={<Checkbox checked={saved} onChange={(e) => setSaved(e.target.checked)} />} label="I have saved my recovery codes" />
      <Button variant="contained" size="large" disabled={!saved} onClick={onDone}>{doneLabel}</Button>
    </Box>
  );
}

export const visuallyHidden = { position: 'absolute', width: 1, height: 1, p: 0, m: -1, overflow: 'hidden', clip: 'rect(0 0 0 0)', whiteSpace: 'nowrap', border: 0 } as const;
