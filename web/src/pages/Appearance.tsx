import { PageNav, useTabsSetting } from '../tabs/Workspace';
import { FormControlLabel, Switch } from '@mui/material';
import { useEffect, useState } from 'react';
import { Alert, Box, Button, MenuItem, Paper, Stack, TextField, ToggleButton, ToggleButtonGroup, Typography } from '@mui/material';
import { useQueryClient } from '@tanstack/react-query';
import { put } from '../api';
import { useAuth } from '../auth';
import { familyNames, formFamilies, paletteNames, useAppearance, type AppearanceValues, type FamilyPalette, type FormFamily, type PaletteKey } from '../appearance';
import { useColorMode, type ColorPreference } from '../colorMode';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { modules } from '../modules/registry';
import { mono } from '../theme';

type Scope = 'organization' | 'personal';
const empty: AppearanceValues = { mode: null, primaryColor: null, accentColor: null, formPalettes: null };
const hex = /^#[0-9A-Fa-f]{6}$/;

/** Colour picker with a hex box and a reset back to the inherited colour. */
function ColourField({ label, value, fallback, onChange, testId }: { label: string; value: string | null; fallback: string; onChange: (v: string | null) => void; testId: string }) {
  const [text, setText] = useState(value ?? '');
  useEffect(() => setText(value ?? ''), [value]);
  const shown = value ?? fallback;
  return (
    <Stack direction="row" alignItems="center" gap={1.25} flexWrap="wrap">
      <Box component="input" type="color" aria-label={`${label} picker`} data-testid={`${testId}-picker`} value={shown} onChange={(e: React.ChangeEvent<HTMLInputElement>) => onChange(e.target.value.toUpperCase())}
        sx={{ width: 44, height: 36, p: 0, border: 1, borderColor: 'divider', borderRadius: 1.5, bgcolor: 'transparent', cursor: 'pointer' }} />
      <TextField size="small" label={label} value={text} placeholder={fallback} data-testid={testId} sx={{ width: 170 }} slotProps={{ htmlInput: { style: { fontFamily: mono } } }}
        onChange={(e) => { setText(e.target.value); if (hex.test(e.target.value)) onChange(e.target.value.toUpperCase()); if (!e.target.value) onChange(null); }}
        error={!!text && !hex.test(text)} helperText={value ? undefined : 'Default'} />
      <Button size="small" onClick={() => onChange(null)} disabled={!value}>Reset to default</Button>
    </Stack>
  );
}

/** Small sample of a palette: an edge-colour bar, an accent number badge and a callout. */
function Swatch({ edge, accent }: { edge: string; accent: string }) {
  return (
    <Box aria-hidden sx={{ display: 'flex', alignItems: 'center', gap: 0.75, px: 1, py: 0.5, border: 1, borderColor: 'divider', borderRadius: 1.5, borderLeft: `5px solid ${edge}`, bgcolor: 'background.paper', minWidth: 120 }}>
      <Box sx={{ width: 18, height: 18, borderRadius: '50%', bgcolor: edge }} />
      <Box sx={{ width: 18, height: 18, borderRadius: '50%', bgcolor: accent }} />
      <Box sx={{ flex: 1, height: 8, borderRadius: 1, bgcolor: `${accent}22`, borderLeft: `3px solid ${accent}` }} />
    </Box>
  );
}

/**
 * Appearance settings: light, dark or system mode, primary and accent colours, and the palette each form family uses (palette 1 green and
 * blue, palette 2 slate and blue, or custom colours). The organization scope sets everyone's default; the personal scope overrides it for one person.
 */
export function AppearanceSettings({ scope }: { scope: Scope }) {
  const qc = useQueryClient();
  const { setPreference } = useColorMode();
  const q = useAppearance(true);
  const info = q.data;
  const stored = (scope === 'organization' ? info?.organization : info?.personal) ?? empty;
  const [v, setV] = useState<AppearanceValues | null>(null);
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);
  if (!info) return null;
  const cur = v ?? stored;
  const readOnly = scope === 'organization' && !info.canEditOrganization;
  // what a cleared value falls back to: the built-in look for the organization, the organization default for a person
  const inherited = (k: 'mode' | 'primaryColor' | 'accentColor') => (scope === 'personal' ? info.organization?.[k] : null) ?? info.defaults[k];
  const familyInherited = (f: FormFamily): FamilyPalette => (scope === 'personal' ? info.organization?.formPalettes?.[f] : null) ?? info.defaults.formPalettes[f];
  const colours = (p: FamilyPalette) => p.palette === 'custom' && p.edge && p.accent ? { edge: p.edge, accent: p.accent } : info.defaults.palettes[p.palette === 'palette1' ? 'palette1' : 'palette2'];
  const setFamily = (f: FormFamily, p: FamilyPalette | null) => {
    const next = { ...(cur.formPalettes ?? {}) };
    if (p) next[f] = p; else delete next[f];
    setV({ ...cur, formPalettes: next });
  };
  const save = async (values: AppearanceValues, text: string) => {
    try {
      await put(`/api/appearance/${scope === 'organization' ? 'organization' : 'me'}`, values);
      if (scope === 'personal') setPreference(values.mode ?? 'system');
      setV(null); setMsg({ ok: true, text });
      await Promise.all(['appearance', 'preferences', 'branding'].map((k) => qc.invalidateQueries({ queryKey: [k] })));
    } catch (e) { setMsg({ ok: false, text: (e as Error).message }); }
  };
  const inheritLabel = scope === 'personal' ? 'Organization default' : 'Built-in default';
  return (
    <Stack gap={2.5} sx={{ maxWidth: 980 }} data-testid={`appearance-${scope}`}>
      {msg && <Alert severity={msg.ok ? 'success' : 'error'} onClose={() => setMsg(null)}>{msg.text}</Alert>}
      {readOnly && <Alert severity="info">You can see the organization appearance; changing it needs the organization settings permission.</Alert>}
      <Paper sx={{ p: 3 }}>
        <Typography variant="h3" sx={{ mb: 0.5 }}>Mode</Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
          {scope === 'organization' ? 'The mode everyone starts with. People can choose their own in My settings or from the user menu.' : 'Applies on every device you sign in on. The light, dark and system choice in the user menu changes it for this device only.'}
        </Typography>
        <ToggleButtonGroup exclusive size="small" disabled={readOnly} value={cur.mode ?? 'inherit'} aria-label="Mode"
          onChange={(_, m: ColorPreference | 'inherit' | null) => m && setV({ ...cur, mode: m === 'inherit' ? null : m })}>
          <ToggleButton value="inherit">{inheritLabel} ({inherited('mode')})</ToggleButton>
          <ToggleButton value="light">Light</ToggleButton>
          <ToggleButton value="dark">Dark</ToggleButton>
          <ToggleButton value="system">Match system</ToggleButton>
        </ToggleButtonGroup>
      </Paper>
      <Paper sx={{ p: 3, display: 'grid', gap: 2 }}>
        <Box>
          <Typography variant="h3" sx={{ mb: 0.5 }}>Colours</Typography>
          <Typography variant="body2" color="text.secondary">The primary colour is used for buttons, links and the active menu; the accent sits beside it on badges, highlights and gradient edges.</Typography>
        </Box>
        <Box sx={{ pointerEvents: readOnly ? 'none' : undefined, opacity: readOnly ? 0.7 : 1, display: 'grid', gap: 2 }}>
          <ColourField label="Primary colour" testId="primary-colour" value={cur.primaryColor} fallback={inherited('primaryColor')} onChange={(c) => setV({ ...cur, primaryColor: c })} />
          <ColourField label="Accent colour" testId="accent-colour" value={cur.accentColor} fallback={inherited('accentColor')} onChange={(c) => setV({ ...cur, accentColor: c })} />
        </Box>
      </Paper>
      <Paper sx={{ p: 3 }}>
        <Typography variant="h3" sx={{ mb: 0.5 }}>Form palettes</Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Each kind of form keeps its own layout. Pick the colours it uses: palette 1 (green and blue), palette 2 (slate and blue) or your own two colours, an edge colour for section bars, badges and table heads and an accent for numbers and notes.
        </Typography>
        <Stack gap={1.5}>
          {formFamilies.map((f) => {
            const own = cur.formPalettes?.[f] ?? null;
            const effective = own ?? familyInherited(f);
            const c = colours(effective);
            return (
              <Box key={f} data-testid={`family-${f}`} sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: '220px 260px 1fr' }, gap: 1.5, alignItems: 'center', py: 1, borderTop: 1, borderColor: 'divider' }}>
                <Typography fontWeight={600}>{familyNames[f]}</Typography>
                <TextField select size="small" label="Palette" fullWidth disabled={readOnly} value={own?.palette ?? 'inherit'} slotProps={{ htmlInput: { 'aria-label': `${familyNames[f]} palette` } }}
                  onChange={(e) => { const k = e.target.value as PaletteKey | 'inherit'; setFamily(f, k === 'inherit' ? null : k === 'custom' ? { palette: 'custom', edge: c.edge, accent: c.accent } : { palette: k }); }}>
                  <MenuItem value="inherit">Default: {paletteNames[familyInherited(f).palette].replace(' and ', ' & ').toLowerCase()}</MenuItem>
                  <MenuItem value="palette1">Palette 1: green & blue</MenuItem>
                  <MenuItem value="palette2">Palette 2: slate & blue</MenuItem>
                  <MenuItem value="custom">{paletteNames.custom}</MenuItem>
                </TextField>
                <Stack direction="row" gap={1.5} alignItems="center" flexWrap="wrap">
                  <Swatch edge={c.edge} accent={c.accent} />
                  {own?.palette === 'custom' && !readOnly && <>
                    <Box component="input" type="color" aria-label={`${familyNames[f]} edge colour`} value={own.edge ?? c.edge} onChange={(e: React.ChangeEvent<HTMLInputElement>) => setFamily(f, { ...own, edge: e.target.value.toUpperCase() })} sx={{ width: 40, height: 32, p: 0, border: 1, borderColor: 'divider', borderRadius: 1, cursor: 'pointer' }} />
                    <Box component="input" type="color" aria-label={`${familyNames[f]} accent colour`} value={own.accent ?? c.accent} onChange={(e: React.ChangeEvent<HTMLInputElement>) => setFamily(f, { ...own, accent: e.target.value.toUpperCase() })} sx={{ width: 40, height: 32, p: 0, border: 1, borderColor: 'divider', borderRadius: 1, cursor: 'pointer' }} />
                    <Typography variant="caption" sx={{ fontFamily: mono }}>{own.edge} / {own.accent}</Typography>
                  </>}
                </Stack>
              </Box>
            );
          })}
        </Stack>
      </Paper>
      {!readOnly && <Stack direction="row" gap={1.5}>
        <Button variant="contained" disabled={!v} onClick={() => save(cur, 'Appearance saved.')}>Save appearance</Button>
        <Button variant="outlined" disabled={!v} onClick={() => setV(null)}>Discard changes</Button>
        <Button color="inherit" onClick={() => save(empty, scope === 'organization' ? 'The organization is back on the built-in look.' : 'Your appearance follows the organization default again.')}>Reset everything to default</Button>
      </Stack>}
    </Stack>
  );
}

const admin = modules.find((m) => m.key === 'admin')!;

/** Settings › Appearance for the organization default. */
export function OrganizationAppearancePage() {
  return <ModuleWorkspace module={admin} crumbs={[{ label: 'Appearance' }]} title="Appearance"><AppearanceSettings scope="organization" /></ModuleWorkspace>;
}

/** My settings › Appearance: the signed-in person's own look. */
export function MyAppearancePage() {
  const { me } = useAuth();
  return (
    <Box sx={{ maxWidth: 1240, mx: 'auto', p: { xs: 2, sm: 4 } }}>
      <Box sx={{ mb: 1 }}><PageNav /></Box>
      <Typography variant="h1" sx={{ mb: 0.5 }}>My appearance</Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>Only changes how {me?.displayName ?? 'the application'} looks for you. Anything left on the organization default follows it.</Typography>
      <TabsToggle />
      <AppearanceSettings scope="personal" />
    </Box>
  );
}

/** Use tabs: keep several pages open inside the application. Saved for this person on this device. */
function TabsToggle() {
  const [on, setOn] = useTabsSetting();
  return (
    <Paper variant="outlined" sx={{ p: 2, mb: 3, display: 'flex', alignItems: 'center', gap: 2, flexWrap: 'wrap' }}>
      <FormControlLabel control={<Switch checked={on} onChange={(e) => setOn(e.target.checked)} inputProps={{ 'aria-label': 'Use tabs' }} />} label={<Typography fontWeight={600}>Use tabs</Typography>} />
      <Typography variant="body2" color="text.secondary" sx={{ flex: 1, minWidth: 240 }}>
        Keep records, forms and pages open as tabs under the top bar. Ctrl-click, middle-click or right-click a link or row to open it in a new tab. Alt+W closes the tab, Alt+[ and Alt+] move between tabs.
      </Typography>
    </Paper>
  );
}
