import { useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { Alert, Box, Button, Checkbox, Chip, Collapse, Paper, Stack, Typography } from '@mui/material';
import { alpha, useTheme } from '@mui/material/styles';
import ExpandMore from '@mui/icons-material/ExpandMore';
import ExpandLess from '@mui/icons-material/ExpandLess';
import CheckCircle from '@mui/icons-material/CheckCircle';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { get, post } from '../api';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { modules } from '../modules/registry';
import { SitePicker } from '../components/SitePicker';

interface PackTemplate { key: string; title: string; category: string; categoryName: string; installed: boolean }
interface PackOption { key: string; name: string; description: string; templateCount: number; installedCount: number; chosen: boolean; templates: PackTemplate[] }
export interface PackStatus { setupDone: boolean; chosenAt: string | null; canInstall: boolean; packs: PackOption[] }
interface InstallResult { installed: number; alreadyInstalled: number }

export const useIndustryPacks = (enabled = true) =>
  useQuery({ queryKey: ['industry-packs'], queryFn: () => get<PackStatus>('/api/industry-packs'), enabled, retry: false });

/**
 * Industry pack picker: one or more packs (Automotive, Aerospace, Electronics, Medical device, General manufacturing), each with a preview
 * of the forms it installs. Installing needs templates.install at the chosen site; the server records every install in the audit trail.
 */
export function IndustryPackPicker({ onDone, setup }: { onDone?: () => void; setup?: boolean }) {
  const qc = useQueryClient();
  const qg = useTheme().qg;
  const status = useIndustryPacks();
  const [picked, setPicked] = useState<string[]>([]);
  const [open, setOpen] = useState<string | null>(null);
  const [site, setSite] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const s = status.data;
  if (status.error) return <Alert severity="error">{(status.error as Error).message}</Alert>;
  if (!s) return null;
  const toggle = (k: string) => setPicked((p) => (p.includes(k) ? p.filter((x) => x !== k) : [...p, k]));
  const forms = new Set(s.packs.filter((p) => picked.includes(p.key)).flatMap((p) => p.templates.filter((t) => !t.installed).map((t) => t.key))).size;
  const refresh = () => { qc.invalidateQueries({ queryKey: ['industry-packs'] }); qc.invalidateQueries({ queryKey: ['template-packages'] }); qc.invalidateQueries({ queryKey: ['template-library'] }); };
  const install = async () => {
    setBusy(true); setMsg(null);
    try {
      const r = await post<InstallResult>('/api/industry-packs/install', { siteId: site, packs: picked });
      setMsg({ ok: true, text: `Installed ${r.installed} form template${r.installed === 1 ? '' : 's'} as drafts${r.alreadyInstalled ? ` (${r.alreadyInstalled} were already installed)` : ''}. Review and approve them under Forms › Templates before use.` });
      setPicked([]); refresh(); onDone?.();
    } catch (e) { setMsg({ ok: false, text: (e as Error).message }); } finally { setBusy(false); }
  };
  const skip = async () => { setBusy(true); try { await post('/api/industry-packs/skip'); refresh(); onDone?.(); } catch (e) { setMsg({ ok: false, text: (e as Error).message }); } finally { setBusy(false); } };
  return (
    <Stack gap={2} data-testid="industry-pack-picker">
      {msg && <Alert severity={msg.ok ? 'success' : 'error'} onClose={() => setMsg(null)}>{msg.text}</Alert>}
      {!s.canInstall && <Alert severity="info">You can look at the packs, but installing them needs the "Install templates" permission. An administrator can add it to your role.</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(2, minmax(0, 1fr))', xl: 'repeat(3, minmax(0, 1fr))' }, gap: 2 }}>
        {s.packs.map((p) => {
          const on = picked.includes(p.key);
          const done = p.installedCount === p.templateCount;
          return (
            <Paper key={p.key} data-testid={`pack-${p.key}`} variant="outlined" sx={{ p: 2, borderRadius: '16px', borderWidth: on ? 2 : 1, borderColor: on ? 'primary.main' : 'divider', background: on ? alpha(qg.glow, 0.08) : undefined, transition: 'border-color 120ms, background 120ms' }}>
              <Stack direction="row" alignItems="flex-start" gap={1}>
                <Checkbox checked={on} disabled={!s.canInstall || done} onChange={() => toggle(p.key)} slotProps={{ input: { 'aria-label': `Choose ${p.name}` } }} sx={{ mt: -0.75, ml: -1 }} />
                <Box sx={{ flex: 1, minWidth: 0 }}>
                  <Stack direction="row" gap={1} alignItems="center" flexWrap="wrap">
                    <Typography variant="h3" sx={{ fontSize: 17 }}>{p.name}</Typography>
                    {done ? <Chip size="small" color="success" icon={<CheckCircle />} label="Installed" /> : p.installedCount > 0 ? <Chip size="small" label={`${p.installedCount} of ${p.templateCount} installed`} /> : null}
                  </Stack>
                  <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>{p.description}</Typography>
                  <Button size="small" sx={{ mt: 1, px: 0 }} endIcon={open === p.key ? <ExpandLess /> : <ExpandMore />} onClick={() => setOpen(open === p.key ? null : p.key)} data-testid={`preview-${p.key}`}>
                    {open === p.key ? 'Hide forms' : `Preview the ${p.templateCount} forms`}
                  </Button>
                </Box>
              </Stack>
              <Collapse in={open === p.key} unmountOnExit>
                <Box component="ul" sx={{ m: 0, mt: 1, pl: 3, maxHeight: 260, overflow: 'auto', columns: { xs: 1, sm: 2 }, columnGap: 3 }}>
                  {p.templates.map((t) => (
                    <Box component="li" key={t.key} sx={{ breakInside: 'avoid', py: 0.25 }}>
                      <Typography variant="body2" component="span">{t.title}</Typography>
                      <Typography variant="caption" color="text.secondary" component="span"> · {t.categoryName}{t.installed ? ' · installed' : ''}</Typography>
                    </Box>
                  ))}
                </Box>
              </Collapse>
            </Paper>
          );
        })}
      </Box>
      {s.canInstall && (
        <Paper variant="outlined" sx={{ p: 2, borderRadius: '16px', display: 'flex', gap: 2, alignItems: 'center', flexWrap: 'wrap' }}>
          <Box sx={{ minWidth: 280, flex: '0 1 340px' }}><SitePicker permission="templates.install" value={site} onChange={setSite} label="Install at site" testId="pack-site" /></Box>
          <Typography variant="body2" color="text.secondary" sx={{ flex: 1, minWidth: 200 }}>
            {picked.length ? `${picked.length} pack${picked.length === 1 ? '' : 's'} chosen: ${forms} new form template${forms === 1 ? '' : 's'} will be added as drafts. Forms already installed are left as they are.` : 'Choose one or more packs. You can add more later from Admin › Industry packs.'}
          </Typography>
          {setup && <Button onClick={skip} disabled={busy}>Not now</Button>}
          <Button variant="contained" disabled={busy || !site || picked.length === 0} onClick={install} data-testid="install-packs">Install {picked.length > 1 ? 'packs' : 'pack'}</Button>
        </Paper>
      )}
    </Stack>
  );
}

/** Admin › Industry packs: install more packs at any time. */
export function IndustryPacksPage() {
  return (
    <ModuleWorkspace module={modules.find((m) => m.key === 'admin')!} crumbs={[{ label: 'Industry packs' }]} title="Industry packs">
      <Typography color="text.secondary" sx={{ mb: 2, maxWidth: 900 }}>
        Each pack adds the form templates for that industry, including the CAPA set every pack shares. Templates are added as drafts so you can review and approve them before use. See them under <RouterLink to="/forms/templates">Forms › Templates</RouterLink>.
      </Typography>
      <IndustryPackPicker />
    </ModuleWorkspace>
  );
}

/** First run: shown on Home to someone who can install templates until the organization has chosen its packs (or skipped the step). */
export function SetupPacksCard() {
  const s = useIndustryPacks();
  if (!s.data || s.data.setupDone || !s.data.canInstall) return null;
  return (
    <Paper data-testid="setup-packs" sx={{ p: { xs: 2, md: 3 }, mb: 3, borderRadius: '20px' }}>
      <Typography variant="overline" color="primary">Organization setup</Typography>
      <Typography variant="h2" sx={{ fontSize: 22, mb: 0.5 }}>Choose your industry packs</Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>Pick the industries you work in to install their ready-made forms. Open a pack to see every form it includes.</Typography>
      <IndustryPackPicker setup />
    </Paper>
  );
}
