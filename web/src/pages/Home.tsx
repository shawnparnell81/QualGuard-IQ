import { Link as RouterLink, useNavigate } from 'react-router-dom';
import { Box, Button, Card, CardActionArea, Chip, Paper, Stack, Typography } from '@mui/material';
import { alpha, useTheme } from '@mui/material/styles';
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { useAuth } from '../auth';
import { visibleModules } from '../modules/registry';
import { fmtDate } from '../components/Status';
import { mono, useChartColors } from '../theme';

import { useWork, type WorkAction } from '../work';
import { SetupPacksCard } from './IndustryPacks';

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

export function Home() {
  const cc = useChartColors();
  const qg = useTheme().qg;
  const { me, can } = useAuth();
  const nav = useNavigate();
  const work = useWork();
  const w = work.data;
  const tiles = [
    { label: 'Assigned to me', value: w?.assigned.length, tone: 'primary.main' },
    { label: 'Approvals waiting on me', value: w?.approvals.length, tone: 'secondary.main' },
    { label: 'Overdue', value: w?.overdue.length, tone: 'error.main' },
    { label: 'Waiting on others', value: w?.waitingOnOthers.length, tone: 'text.secondary' },
  ];
  return (
    <Box sx={{ maxWidth: 1240, mx: 'auto', p: { xs: 2, sm: 3, md: 4 } }}>
      <Typography variant="overline" color="text.secondary">{new Date().toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric' })}</Typography>
      <Typography variant="h1" sx={{ mb: 3, fontSize: '2.3rem', background: qg.greeting, WebkitBackgroundClip: 'text', backgroundClip: 'text', color: 'transparent' }}>Good to see you, {me?.displayName.split(' ')[0]}</Typography>
      {can('templates.install') && <SetupPacksCard />}

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', md: 'repeat(4, minmax(0, 1fr))' }, gap: 2, mb: 3 }}>
        {tiles.map((t) => (
          <Paper key={t.label} sx={(th) => {
            const c = t.tone.split('.').reduce<any>((o, k) => o?.[k], th.palette) as string;
            return { p: 2.5, pb: 3, position: 'relative', overflow: 'hidden', borderRadius: '20px', '&::after': { content: '""', position: 'absolute', left: 20, right: 20, bottom: 12, height: 5, borderRadius: 5, background: `linear-gradient(90deg, ${c}, ${alpha(c, 0.15)})` } };
          }}>
            <Typography sx={{ fontSize: 36, fontWeight: 700, color: t.tone, letterSpacing: '-0.02em' }}>{t.value ?? '–'}</Typography>
            <Typography variant="body2" color="text.secondary">{t.label}</Typography>
          </Paper>
        ))}
      </Box>

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: '1.4fr 1fr' }, gap: 2, mb: 4 }}>
        <Paper sx={{ p: 2 }}>
          <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 1 }}>
            <Typography variant="h3">Approvals waiting on me</Typography>
            <Button component={RouterLink} to="/my-work" size="small">Open My Work</Button>
          </Stack>
          {(w?.approvals ?? []).slice(0, 6).map((a) => (
            <Stack key={a.id} direction="row" gap={1.5} alignItems="center" sx={{ py: 1, borderTop: 1, borderColor: 'divider' }}>
              <Typography sx={{ fontFamily: mono, fontSize: 13 }}>{a.recordNumber}</Typography>
              <Chip size="small" label={a.stepName} variant="outlined" />
              <Box sx={{ flex: 1 }} />
              <Typography variant="caption" color="text.secondary">since {fmtDate(a.submittedAt)}</Typography>
            </Stack>
          ))}
          {w && w.approvals.length === 0 && <Typography color="text.secondary" sx={{ py: 2 }}>Nothing is waiting for your signature.</Typography>}
        </Paper>
        <Paper sx={{ p: 2 }}>
          <Typography variant="h3" sx={{ mb: 1 }}>My open actions by due date</Typography>
          <Box sx={{ height: 180 }}>
            <ResponsiveContainer>
              <BarChart data={dueBuckets(w?.assigned ?? [])}>
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={cc.grid} />
                <XAxis dataKey="name" tick={{ fontSize: 12 }} />
                <YAxis allowDecimals={false} tick={{ fontSize: 12 }} width={28} />
                <Tooltip />
                <defs><linearGradient id="qgbar" x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stopColor={cc.accent} /><stop offset="100%" stopColor={cc.primary} /></linearGradient></defs>
                <Bar dataKey="count" fill="url(#qgbar)" radius={[10, 10, 10, 10]} barSize={34} />
              </BarChart>
            </ResponsiveContainer>
          </Box>
        </Paper>
      </Box>

      <Typography variant="h3" sx={{ mb: 1.5 }}>Modules</Typography>
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(260px, 1fr))', gap: 2 }}>
        {visibleModules(can).map((m) => (
          <Card key={m.key} variant="outlined">
            <CardActionArea onClick={() => nav(m.basePath)} sx={{ p: 2.5, height: '100%', display: 'flex', alignItems: 'flex-start', gap: 2, '&:hover .module-tile': { background: qg.icon.hoverBg, color: '#FFFFFF' }, '&:hover .module-name': { color: 'primary.main' } }}>
              <Box className="module-tile" sx={{ flexShrink: 0, width: qg.icon.size, height: qg.icon.size, borderRadius: `${qg.icon.radius}px`, display: 'grid', placeItems: 'center', color: '#FFFFFF', background: qg.gradient, transition: 'background .18s, color .18s', '& svg': { fontSize: qg.icon.size * 0.52 },
                boxShadow: `0 8px 18px ${qg.glow}, inset 0 1px 0 rgba(255,255,255,0.35)` }}>{m.icon}</Box>
              <Box sx={{ minWidth: 0 }}>
                <Typography className="module-name" fontWeight={700} sx={{ mb: 0.25, transition: 'color .18s' }}>{m.label}</Typography>
                <Typography variant="body2" color="text.secondary">{m.description}</Typography>
              </Box>
            </CardActionArea>
          </Card>
        ))}
      </Box>
    </Box>
  );
}
