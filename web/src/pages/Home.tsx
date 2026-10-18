import { Link as RouterLink } from 'react-router-dom';
import { Box, Button, Chip, Paper, Stack, Typography } from '@mui/material';
import ArrowForward from '@mui/icons-material/ArrowForward';
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { useAuth } from '../auth';
import { visibleModules } from '../modules/registry';
import { fmtDate } from '../components/Status';
import { PanelHead } from '../components/Kit';
import { mono, useChartColors } from '../theme';

import { useWork, type WorkAction } from '../work';

function dueBuckets(items: WorkAction[]) {
  const today = new Date(); today.setHours(0, 0, 0, 0);
  const buckets = [
    { name: 'Overdue', count: 0 }, { name: 'This week', count: 0 }, { name: 'Next week', count: 0 }, { name: 'Later', count: 0 },
  ];
  for (const a of items) {
    const days = (new Date(a.dueDate + 'T00:00:00').getTime() - today.getTime()) / 86_400_000;
    buckets[days < 0 ? 0 : days < 7 ? 1 : days < 14 ? 2 : 3].count++;
  }
  return buckets;
}

const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

/** One plain sentence that says what needs attention, used as the page headline instead of a greeting. */
function headline(approvals: number, overdue: number) {
  if (approvals === 0 && overdue === 0) return 'Nothing needs you right now.';
  const parts = [approvals ? plural(approvals, 'approval', 'approvals') : null, overdue ? plural(overdue, 'overdue action', 'overdue actions') : null].filter(Boolean);
  return `${parts.join(' and ')} ${approvals + overdue === 1 ? 'needs' : 'need'} you.`;
}

export function Home() {
  const cc = useChartColors();
  const { me, can } = useAuth();
  const work = useWork();
  const w = work.data;
  const ledger = [
    { label: 'Assigned to me', value: w?.assigned.length, to: '/my-work' },
    { label: 'Approvals waiting on me', value: w?.approvals.length, to: '/my-work' },
    { label: 'Overdue', value: w?.overdue.length, to: '/my-work', alert: (w?.overdue.length ?? 0) > 0 },
    { label: 'Waiting on others', value: w?.waitingOnOthers.length, to: '/my-work' },
  ];
  const mods = visibleModules(can);
  return (
    <Box>
      {/* Brief: a dark full-width band with the date, a one-line status sentence and the four counts as a ledger. */}
      <Box sx={{ bgcolor: '#17202B', color: '#F1F4F8', borderBottom: '3px solid', borderColor: 'primary.main' }} data-testid="home-brief">
        <Box sx={{ maxWidth: 1440, mx: 'auto', px: { xs: 2, md: 4 }, py: { xs: 2.5, md: 3 }, display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '1fr auto' }, gap: 3, alignItems: 'end' }}>
          <Box>
            <Stack direction="row" alignItems="center" gap={1.25} sx={{ mb: 1.25 }}>
              <Box component="img" src="/brand/mark-dark.svg" alt="" aria-hidden sx={{ width: 22, height: 22 }} />
              <Typography sx={{ fontFamily: mono, fontSize: 12, color: '#9FB0C4', letterSpacing: '0.06em' }}>
                {new Date().toLocaleDateString(undefined, { weekday: 'short', year: 'numeric', month: 'short', day: 'numeric' }).toUpperCase()}
              </Typography>
            </Stack>
            <Typography variant="h1" sx={{ fontSize: { xs: '1.5rem', md: '1.9rem' }, color: 'inherit' }}>{w ? headline(w.approvals.length, w.overdue.length) : 'Loading your work…'}</Typography>
            <Typography sx={{ mt: 0.5, fontSize: 13, color: '#9FB0C4' }}>Signed in as {me?.displayName}</Typography>
          </Box>
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'repeat(2, 1fr)', sm: 'repeat(4, auto)' }, columnGap: 0, rowGap: 1.5 }}>
            {ledger.map((t, i) => (
              <Box key={t.label} component={RouterLink} to={t.to} sx={{ color: 'inherit', textDecoration: 'none', px: 2.5, borderLeft: { xs: i % 2 ? '1px solid #33404F' : 'none', sm: i ? '1px solid #33404F' : 'none' }, '&:hover .n': { color: '#7FB2FF' } }}>
                <Typography className="n" sx={{ fontFamily: mono, fontSize: 32, lineHeight: 1.1, color: t.alert ? '#FF9B8F' : 'inherit' }}>{t.value ?? '–'}</Typography>
                <Typography sx={{ fontSize: 11, letterSpacing: '0.1em', textTransform: 'uppercase', color: '#9FB0C4', whiteSpace: 'nowrap' }}>{t.label}</Typography>
              </Box>
            ))}
          </Box>
        </Box>
      </Box>

      <Box sx={{ maxWidth: 1440, mx: 'auto', px: { xs: 2, md: 4 }, py: 3, display: 'grid', gridTemplateColumns: { xs: '1fr', lg: 'minmax(0, 1fr) 340px' }, gap: 3 }}>
        <Stack gap={3} sx={{ minWidth: 0 }}>
          <Paper sx={{ overflow: 'hidden' }}>
            <PanelHead title="Approvals waiting on me" action={<Button component={RouterLink} to="/my-work" size="small" endIcon={<ArrowForward fontSize="small" />}>Open My Work</Button>} />
            {(w?.approvals ?? []).slice(0, 8).map((a, i) => (
              <Stack key={a.id} direction="row" gap={2} alignItems="center" sx={{ px: 2, py: 1.1, borderTop: i ? 1 : 0, borderColor: 'divider' }}>
                <Typography sx={{ fontFamily: mono, fontSize: 12.5, fontWeight: 500, minWidth: 120 }}>{a.recordNumber}</Typography>
                <Chip size="small" label={a.stepName} sx={{ bgcolor: 'action.selected', color: 'primary.main' }} />
                <Box sx={{ flex: 1 }} />
                <Typography variant="caption" color="text.secondary" sx={{ fontFamily: mono }}>since {fmtDate(a.submittedAt)}</Typography>
              </Stack>
            ))}
            {w && w.approvals.length === 0 && <Typography color="text.secondary" sx={{ px: 2, py: 2.5 }}>Nothing is waiting for your signature.</Typography>}
          </Paper>

          <Paper sx={{ overflow: 'hidden' }}>
            <PanelHead title="Overdue" />
            {(w?.overdue ?? []).slice(0, 8).map((a, i) => (
              <Stack key={a.id} direction="row" gap={2} alignItems="center" sx={{ px: 2, py: 1.1, borderTop: i ? 1 : 0, borderColor: 'divider', borderLeft: '3px solid', borderLeftColor: 'error.main' }}>
                <Typography sx={{ fontFamily: mono, fontSize: 12.5, minWidth: 120 }}>{a.number}</Typography>
                <Typography variant="body2" sx={{ flex: 1, minWidth: 0 }} noWrap>{a.title}</Typography>
                <Typography variant="caption" color="error.main" sx={{ fontFamily: mono }}>due {fmtDate(a.dueDate)}</Typography>
              </Stack>
            ))}
            {w && w.overdue.length === 0 && <Typography color="text.secondary" sx={{ px: 2, py: 2.5 }}>No overdue actions.</Typography>}
          </Paper>
        </Stack>

        <Stack gap={3}>
          <Paper sx={{ overflow: 'hidden' }}>
            <PanelHead title="My open actions by due date" />
            <Box sx={{ height: 190, p: 1.5 }}>
              <ResponsiveContainer>
                <BarChart data={dueBuckets(w?.assigned ?? [])} layout="vertical" margin={{ left: 8, right: 12 }}>
                  <CartesianGrid strokeDasharray="2 4" horizontal={false} stroke={cc.grid} />
                  <XAxis type="number" allowDecimals={false} tick={{ fontSize: 11 }} />
                  <YAxis type="category" dataKey="name" tick={{ fontSize: 11 }} width={72} />
                  <Tooltip />
                  <Bar dataKey="count" fill={cc.primary} barSize={14} />
                </BarChart>
              </ResponsiveContainer>
            </Box>
          </Paper>
        </Stack>
      </Box>

      {/* Module index: a full-width ruled directory in columns instead of a grid of cards. */}
      <Box sx={{ maxWidth: 1440, mx: 'auto', px: { xs: 2, md: 4 }, pb: 4 }}>
        <Paper sx={{ overflow: 'hidden' }} component="nav" aria-label="Jump to a module">
          <PanelHead title={`Modules · ${mods.length}`} />
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)', md: 'repeat(3, 1fr)', xl: 'repeat(4, 1fr)' }, mt: '-1px', ml: '-1px' }}>
            {mods.map((m) => (
              <Box key={m.key} component={RouterLink} to={m.basePath} title={m.description}
                sx={{ display: 'flex', alignItems: 'center', gap: 1.25, px: 2, py: 1, borderTop: 1, borderLeft: 1, borderColor: 'divider', color: 'text.primary', textDecoration: 'none', '& svg': { fontSize: 18, color: 'text.secondary' },
                  '&:hover': { bgcolor: 'action.hover', '& svg': { color: 'primary.main' }, '& .go': { opacity: 1 } } }}>
                {m.icon}
                <Typography variant="body2" fontWeight={600} sx={{ flex: 1 }} noWrap>{m.label}</Typography>
                <ArrowForward className="go" sx={{ opacity: 0, transition: 'opacity 120ms' }} />
              </Box>
            ))}
          </Box>
        </Paper>
      </Box>
    </Box>
  );
}
