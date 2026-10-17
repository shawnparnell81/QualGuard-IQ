import { useEffect, useState } from 'react';
import { Alert, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, IconButton, MenuItem, Paper, Stack, Switch, TextField, Tooltip, Typography } from '@mui/material';
import CheckCircleOutline from '@mui/icons-material/CheckCircleOutline';
import ErrorOutline from '@mui/icons-material/ErrorOutline';
import ContentCopy from '@mui/icons-material/ContentCopy';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { del, get, post, put } from '../api';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { fmtDateTime } from '../components/Status';
import { mono } from '../theme';
import { ProviderMark, type ProviderKind } from '../components/SignInParts';

const mod = modules.find((m) => m.key === 'admin')!;

interface TestStep { name: string; ok: boolean; detail: string }
interface TestReport { ok: boolean; steps: TestStep[]; claims: Record<string, string | null> }
interface Provider {
  id: string; kind: ProviderKind; displayName: string; authority: string; clientId: string; hasClientSecret: boolean; scopes: string; allowedDomains: string[]; ssoRequiredDomains: string[];
  jitEnabled: boolean; jitDefaultRoleId: string | null; trustProviderMfa: boolean; isEnabled: boolean; lastTestAt: string | null; lastTestSucceeded: boolean; lastTestBy: string | null;
  lastTest: TestReport | null; testCurrent: boolean; linkedAccounts: number; rowVersion: number;
}
interface AdminRow { id: string; email: string; displayName: string; breakGlass: boolean; mfaEnabled: boolean }
interface View { callbackUrl: string; providers: Provider[]; administrators: AdminRow[]; jitRoles: { id: string; name: string }[] }

const kinds: { kind: ProviderKind; label: string; authority: string; help: string }[] = [
  { kind: 'Entra', label: 'Microsoft Entra ID', authority: 'https://login.microsoftonline.com/<tenant id>/v2.0', help: 'Your tenant\'s own address (Directory (tenant) ID), not common or organizations.' },
  { kind: 'Google', label: 'Google Workspace', authority: 'https://accounts.google.com', help: 'Always https://accounts.google.com.' },
  { kind: 'Okta', label: 'Okta', authority: 'https://<your org>.okta.com/oauth2/default', help: 'The issuer of your Okta authorization server (Security › API).' },
  { kind: 'Oidc', label: 'Other OpenID Connect', authority: 'https://idp.example.com', help: 'The issuer; its discovery document must be at /.well-known/openid-configuration.' },
];
const kindLabel = (k: ProviderKind) => kinds.find((x) => x.kind === k)?.label ?? k;

interface Draft {
  id?: string; kind: ProviderKind; displayName: string; authority: string; clientId: string; clientSecret: string; clearClientSecret: boolean; hasClientSecret: boolean; scopes: string;
  allowedDomains: string; ssoRequiredDomains: string; jitEnabled: boolean; jitDefaultRoleId: string; trustProviderMfa: boolean; rowVersion: number;
}
const blank = (): Draft => ({ kind: 'Entra', displayName: 'Microsoft', authority: '', clientId: '', clientSecret: '', clearClientSecret: false, hasClientSecret: false, scopes: 'openid profile email',
  allowedDomains: '', ssoRequiredDomains: '', jitEnabled: false, jitDefaultRoleId: '', trustProviderMfa: false, rowVersion: 0 });
const fromProvider = (p: Provider): Draft => ({ id: p.id, kind: p.kind, displayName: p.displayName, authority: p.authority, clientId: p.clientId, clientSecret: '', clearClientSecret: false,
  hasClientSecret: p.hasClientSecret, scopes: p.scopes, allowedDomains: p.allowedDomains.join(', '), ssoRequiredDomains: p.ssoRequiredDomains.join(', '), jitEnabled: p.jitEnabled,
  jitDefaultRoleId: p.jitDefaultRoleId ?? '', trustProviderMfa: p.trustProviderMfa, rowVersion: p.rowVersion });
const list = (s: string) => s.split(/[\s,;]+/).map((x) => x.trim()).filter(Boolean);

/** Admin › People › Single sign-on: identity providers (add, check, test, turn on) and break-glass accounts. */
export function SingleSignOnPage() {
  const qc = useQueryClient();
  const q = useQuery({ queryKey: ['sso-admin'], queryFn: () => get<View>('/api/admin/sso') });
  const [draft, setDraft] = useState<Draft | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<{ severity: 'success' | 'warning' | 'info'; text: string } | null>(null);
  const [checks, setChecks] = useState<Record<string, TestReport>>({});
  const [tested, setTested] = useState<string | null>(null);
  const refresh = () => { qc.invalidateQueries({ queryKey: ['sso-admin'] }); qc.invalidateQueries({ queryKey: ['sso-providers'] }); };

  useEffect(() => {
    const m = window.location.hash.match(/^#tested=([0-9a-f-]{36})$/i);
    if (m) { setTested(m[1]); window.history.replaceState(null, '', window.location.pathname); }
  }, []);
  const v = q.data;
  if (!v) return null;
  const testedProvider = tested ? v.providers.find((p) => p.id === tested) : undefined;

  const act = async (f: () => Promise<unknown>, ok?: string) => {
    setError(null); setNotice(null);
    try { await f(); if (ok) setNotice({ severity: 'success', text: ok }); refresh(); } catch (e) { setError((e as Error).message); }
  };
  const save = async () => {
    if (!draft) return;
    setError(null);
    const body = { kind: draft.kind, displayName: draft.displayName, authority: draft.authority, clientId: draft.clientId, clientSecret: draft.clientSecret || null, clearClientSecret: draft.clearClientSecret,
      scopes: draft.scopes, allowedDomains: list(draft.allowedDomains), ssoRequiredDomains: list(draft.ssoRequiredDomains), jitEnabled: draft.jitEnabled,
      jitDefaultRoleId: draft.jitDefaultRoleId || null, trustProviderMfa: draft.trustProviderMfa, rowVersion: draft.rowVersion };
    try {
      if (draft.id) {
        const r = await put<{ disabled: boolean }>(`/api/admin/sso/providers/${draft.id}`, body);
        setNotice(r?.disabled ? { severity: 'warning', text: `${draft.displayName} was turned off because its connection settings changed. Run a test sign-in, then turn it on again.` } : { severity: 'success', text: `${draft.displayName} saved.` });
      } else {
        await post('/api/admin/sso/providers', body);
        setNotice({ severity: 'info', text: `${draft.displayName} added. Next: register the redirect URI at the provider, run a test sign-in, then turn it on.` });
      }
      setDraft(null); refresh();
    } catch (e) { setError((e as Error).message); }
  };
  const check = async (p: Provider) => {
    setError(null);
    try { const r = await post<TestReport>(`/api/admin/sso/providers/${p.id}/check`); setChecks({ ...checks, [p.id]: r }); } catch (e) { setError((e as Error).message); }
  };
  const breakGlassCount = v.administrators.filter((a) => a.breakGlass).length;
  const enforcing = v.providers.some((p) => p.isEnabled && p.ssoRequiredDomains.length > 0);
  const safeRoles = v.jitRoles;

  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Single sign-on' }]} title="Single sign-on"
      actions={<Button variant="contained" onClick={() => { setError(null); setDraft(blank()); }}>Add identity provider</Button>}>
      {error && !draft && <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}
      {notice && <Alert severity={notice.severity} sx={{ mb: 2 }} onClose={() => setNotice(null)}>{notice.text}</Alert>}
      {testedProvider && (
        <Alert severity={testedProvider.lastTestSucceeded ? 'success' : 'error'} sx={{ mb: 2 }} onClose={() => setTested(null)}>
          {testedProvider.lastTestSucceeded ? `Test sign-in with ${testedProvider.displayName} passed. You can turn it on now.` : `Test sign-in with ${testedProvider.displayName} failed. See the steps below.`}
        </Alert>
      )}
      <Paper sx={{ p: 2.5, mb: 2 }} component="section" aria-labelledby="sso-redirect">
        <Typography variant="h3" component="h2" id="sso-redirect" sx={{ mb: 0.5 }}>Redirect URI</Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>Register this address at every provider (Entra ID: Web platform; Google: Authorized redirect URI; Okta: Sign-in redirect URI).</Typography>
        <Stack direction="row" alignItems="center" gap={1} sx={{ flexWrap: 'wrap' }}>
          <Box component="code" sx={{ fontFamily: mono, fontSize: 14, px: 1, py: 0.5, bgcolor: 'action.hover', borderRadius: 1, wordBreak: 'break-all' }}>{v.callbackUrl}</Box>
          <Tooltip title="Copy"><IconButton size="small" aria-label="Copy redirect URI" onClick={() => navigator.clipboard?.writeText(v.callbackUrl)}><ContentCopy fontSize="small" /></IconButton></Tooltip>
        </Stack>
      </Paper>

      {v.providers.length === 0 && <Paper sx={{ p: 3, mb: 2, textAlign: 'center' }}><Typography color="text.secondary">No identity providers yet. People sign in with their email and password.</Typography></Paper>}
      <Box sx={{ display: 'grid', gap: 2, mb: 2 }}>
        {v.providers.map((p) => {
          const report = checks[p.id] ?? p.lastTest;
          return (
            <Paper key={p.id} sx={{ p: 2.5 }} component="section" aria-label={`Identity provider ${p.displayName}`}>
              <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" gap={1.5}>
                <Box sx={{ minWidth: 0 }}>
                  <Stack direction="row" alignItems="center" gap={1} sx={{ flexWrap: 'wrap' }}>
                    <ProviderMark kind={p.kind} size={20} />
                    <Typography variant="h3" component="h2">{p.displayName}</Typography>
                    <Chip size="small" label={kindLabel(p.kind)} variant="outlined" />
                    <Chip size="small" label={p.isEnabled ? 'On' : 'Off'} color={p.isEnabled ? 'success' : 'default'} />
                    <Chip size="small" label={p.testCurrent ? 'Test passed' : p.lastTestAt ? (p.lastTestSucceeded ? 'Settings changed since test' : 'Test failed') : 'Not tested'}
                      color={p.testCurrent ? 'success' : p.lastTestAt && !p.lastTestSucceeded ? 'error' : 'warning'} variant="outlined" />
                  </Stack>
                  <Typography variant="body2" color="text.secondary" sx={{ mt: 0.75, wordBreak: 'break-all' }}>{p.authority} · client {p.clientId} · {p.hasClientSecret ? 'secret stored (encrypted)' : 'no secret'}</Typography>
                  <Typography variant="body2" sx={{ mt: 0.5 }}>
                    Domains: {p.allowedDomains.join(', ')}{p.ssoRequiredDomains.length > 0 && <> · <b>must use single sign-on:</b> {p.ssoRequiredDomains.join(', ')}</>}
                  </Typography>
                  <Typography variant="body2" color="text.secondary">
                    {p.trustProviderMfa ? 'Provider MFA trusted (no app code)' : 'App two-step verification still applies'} · {p.jitEnabled ? `New people get an account (${v.jitRoles.find((r) => r.id === p.jitDefaultRoleId)?.name ?? 'role'})` : 'Only existing accounts'} · {p.linkedAccounts} linked
                  </Typography>
                </Box>
                <Stack direction="row" gap={1} sx={{ flexWrap: 'wrap', alignItems: 'flex-start', flexShrink: 0 }}>
                  <Button size="small" onClick={() => { setError(null); setDraft(fromProvider(p)); }}>Edit</Button>
                  <Button size="small" onClick={() => check(p)}>Check connection</Button>
                  <Button size="small" variant="outlined" onClick={() => window.location.assign(`/api/admin/sso/providers/${p.id}/test`)}>Test sign-in</Button>
                  {p.isEnabled
                    ? <Button size="small" color="warning" variant="outlined" onClick={() => act(() => post(`/api/admin/sso/providers/${p.id}/disable`), `${p.displayName} turned off.`)}>Turn off</Button>
                    : <Tooltip title={p.testCurrent ? '' : 'Run a test sign-in with the current settings first'}><span>
                        <Button size="small" variant="contained" disabled={!p.testCurrent} onClick={() => { setTested(null); act(() => post(`/api/admin/sso/providers/${p.id}/enable`), `${p.displayName} is on. People see "Sign in with ${p.displayName}" on the sign-in page.`); }}>Turn on</Button>
                      </span></Tooltip>}
                  {!p.isEnabled && <Button size="small" color="error" onClick={() => { if (window.confirm(`Remove ${p.displayName}? Links to ${p.linkedAccounts} account(s) are removed too.`)) act(() => del(`/api/admin/sso/providers/${p.id}`), `${p.displayName} removed.`); }}>Remove</Button>}
                </Stack>
              </Stack>
              {report && (
                <Box sx={{ mt: 1.5, pt: 1.5, borderTop: 1, borderColor: 'divider' }}>
                  <Typography variant="overline" component="h3">{checks[p.id] ? 'Connection check' : `Last test sign-in${p.lastTestAt ? ` · ${fmtDateTime(p.lastTestAt)}` : ''}${p.lastTestBy ? ` · ${p.lastTestBy}` : ''}`}</Typography>
                  <Box component="ul" sx={{ listStyle: 'none', p: 0, m: 0, display: 'grid', gap: 0.5 }}>
                    {report.steps.map((s, i) => (
                      <Stack key={i} component="li" direction="row" gap={1} alignItems="flex-start">
                        {s.ok ? <CheckCircleOutline fontSize="small" color="success" aria-label="Passed" /> : <ErrorOutline fontSize="small" color="error" aria-label="Failed" />}
                        <Typography variant="body2"><b>{s.name}:</b> {s.detail}</Typography>
                      </Stack>
                    ))}
                  </Box>
                  {Object.values(report.claims).some((x) => x) && (
                    <Typography variant="caption" color="text.secondary" component="p" sx={{ mt: 1, fontFamily: mono, wordBreak: 'break-all' }}>
                      {Object.entries(report.claims).filter(([, x]) => x).map(([k, x]) => `${k}=${x}`).join('  ·  ')}
                    </Typography>
                  )}
                </Box>
              )}
            </Paper>
          );
        })}
      </Box>

      <Paper sx={{ p: 2.5 }} component="section" aria-labelledby="sso-bg">
        <Typography variant="h3" component="h2" id="sso-bg" sx={{ mb: 0.5 }}>Break-glass accounts</Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
          These administrators can always sign in with their password, even when their domain must use single sign-on, so someone can get in if a provider is down or misconfigured.
          At least one is needed before a domain can require single sign-on. Give them two-step verification and keep the password in a safe place.
        </Typography>
        {enforcing && breakGlassCount === 1 && <Alert severity="info" sx={{ mb: 1 }}>Only one break-glass account. Consider a second one held by another person.</Alert>}
        {v.administrators.map((a) => (
          <Stack key={a.id} direction="row" alignItems="center" justifyContent="space-between" gap={1} sx={{ py: 0.75, borderBottom: 1, borderColor: 'divider', flexWrap: 'wrap' }}>
            <FormControlLabel control={<Switch checked={a.breakGlass} onChange={(e) => act(() => put(`/api/admin/sso/break-glass/${a.id}`, { enabled: e.target.checked }))} />}
              label={<span>{a.displayName} <Typography component="span" variant="caption" color="text.secondary">· {a.email}</Typography></span>} />
            {a.breakGlass && !a.mfaEnabled ? <Chip size="small" color="warning" label="No two-step verification" /> : a.mfaEnabled ? <Chip size="small" variant="outlined" label="Two-step on" /> : null}
          </Stack>
        ))}
      </Paper>

      <Dialog open={!!draft} onClose={() => setDraft(null)} fullWidth maxWidth="sm" aria-labelledby="sso-dlg">
        {draft && (() => {
          const k = kinds.find((x) => x.kind === draft.kind)!;
          const set = (patch: Partial<Draft>) => setDraft({ ...draft, ...patch });
          return (<>
            <DialogTitle id="sso-dlg">{draft.id ? `Edit ${draft.displayName}` : 'Add identity provider'}</DialogTitle>
            <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
              {error && <Alert severity="error">{error}</Alert>}
              <TextField select label="Provider" value={draft.kind} onChange={(e) => {
                const nk = e.target.value as ProviderKind;
                set({ kind: nk, authority: nk === 'Google' ? 'https://accounts.google.com' : draft.kind === 'Google' ? '' : draft.authority,
                  displayName: draft.id ? draft.displayName : ({ Entra: 'Microsoft', Google: 'Google', Okta: 'Okta', Oidc: '' } as const)[nk] });
              }}>
                {kinds.map((x) => <MenuItem key={x.kind} value={x.kind}>{x.label}</MenuItem>)}
              </TextField>
              <TextField label="Button name" value={draft.displayName} onChange={(e) => set({ displayName: e.target.value })} helperText={`Shown as "Sign in with ${draft.displayName || '…'}"`} />
              <TextField label="Authority (issuer URL)" value={draft.authority} onChange={(e) => set({ authority: e.target.value })} placeholder={k.authority} helperText={k.help} />
              <TextField label="Client ID" value={draft.clientId} onChange={(e) => set({ clientId: e.target.value })} helperText={draft.kind === 'Entra' ? 'Application (client) ID' : undefined} />
              <TextField label="Client secret" type="password" autoComplete="new-password" value={draft.clientSecret} onChange={(e) => set({ clientSecret: e.target.value, clearClientSecret: false })}
                helperText={draft.hasClientSecret ? 'A secret is stored (encrypted; it is never shown). Leave empty to keep it.' : 'Stored encrypted and never shown again.'} />
              <TextField label="Scopes" value={draft.scopes} onChange={(e) => set({ scopes: e.target.value })} helperText="openid, profile and email are always requested." />
              <TextField label="Allowed email domains" value={draft.allowedDomains} onChange={(e) => set({ allowedDomains: e.target.value })} placeholder="contoso.com, contoso.co.uk"
                helperText="Only people whose verified email is in one of these domains can sign in with this provider." />
              <TextField label="Domains that must use single sign-on" value={draft.ssoRequiredDomains} onChange={(e) => set({ ssoRequiredDomains: e.target.value })}
                helperText="Optional. People in these domains can no longer sign in with a password (break-glass accounts excepted)." />
              <FormControlLabel control={<Switch checked={draft.trustProviderMfa} onChange={(e) => set({ trustProviderMfa: e.target.checked })} />}
                label={<span>Trust the provider's multi-factor sign-in<Typography component="span" variant="caption" color="text.secondary" display="block">Off: the app still asks for its own two-step code when the account or policy needs one. Turn on only when the provider enforces MFA for everyone.</Typography></span>} />
              <FormControlLabel control={<Switch checked={draft.jitEnabled} onChange={(e) => set({ jitEnabled: e.target.checked })} />}
                label={<span>Create an account at first sign-in<Typography component="span" variant="caption" color="text.secondary" display="block">Off: an administrator adds people first. On: anyone in an allowed domain gets an account with the role below.</Typography></span>} />
              {draft.jitEnabled && (
                <TextField select label="Role for new accounts" value={draft.jitDefaultRoleId} onChange={(e) => set({ jitDefaultRoleId: e.target.value })}
                  helperText="Given organization-wide. Roles with administration permissions cannot be given automatically.">
                  {safeRoles.map((r) => <MenuItem key={r.id} value={r.id}>{r.name}</MenuItem>)}
                </TextField>
              )}
              {draft.id && draft.hasClientSecret && <FormControlLabel control={<Switch checked={draft.clearClientSecret} onChange={(e) => set({ clearClientSecret: e.target.checked, clientSecret: '' })} />} label="Remove the stored secret (public client with PKCE only)" />}
            </DialogContent>
            <DialogActions>
              <Button onClick={() => setDraft(null)}>Cancel</Button>
              <Button variant="contained" onClick={save}>{draft.id ? 'Save' : 'Add provider'}</Button>
            </DialogActions>
          </>);
        })()}
      </Dialog>
    </ModuleWorkspace>
  );
}
