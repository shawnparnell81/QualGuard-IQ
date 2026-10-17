import { Link as RouterLink, useNavigate } from 'react-router-dom';
import { Box, Button, Card, CardActionArea, Chip, Paper, Stack, Typography } from '@mui/material';
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { useAuth } from '../auth';
import { visibleModules } from '../modules/registry';
import { fmtDate } from '../components/Status';
import { mono } from '../theme';

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

export function Home() {
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
    <Box sx={{ maxWidth: 1240, mx: 'auto', p: 4 }}>
      <Typography variant="overline" color="text.secondary">{new Date().toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric' })}</Typography>
      <Typography variant="h1" sx={{ mb: 3 }}>Good to see you, {me?.displayName.split(' ')[0]}</Typography>

      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 2, mb: 3 }}>
        {tiles.map((t) => (
          <Paper key={t.label} sx={{ p: 2, borderTop: 3, borderTopColor: t.tone }}>
            <Typography sx={{ fontFamily: mono, fontSize: 30, fontWeight: 500 }}>{t.value ?? '–'}</Typography>
            <Typography variant="body2" color="text.secondary">{t.label}</Typography>
          </Paper>
        ))}
      </Box>

      <Box sx={{ display: 'grid', gridTemplateColumns: '1.4fr 1fr', gap: 2, mb: 4 }}>
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
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#E2DED5" />
                <XAxis dataKey="name" tick={{ fontSize: 12 }} />
                <YAxis allowDecimals={false} tick={{ fontSize: 12 }} width={28} />
                <Tooltip />
                <Bar dataKey="count" fill="#1F4E4A" radius={[4, 4, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </Box>
        </Paper>
      </Box>

      <Typography variant="h3" sx={{ mb: 1.5 }}>Modules</Typography>
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(260px, 1fr))', gap: 2 }}>
        {visibleModules(can).map((m) => (
          <Card key={m.key} variant="outlined">
            <CardActionArea onClick={() => nav(m.basePath)} sx={{ p: 2, height: '100%' }}>
              <Stack direction="row" gap={1} alignItems="center" sx={{ color: 'primary.main', mb: 0.5 }}>{m.icon}<Typography fontWeight={600}>{m.label}</Typography></Stack>
              <Typography variant="body2" color="text.secondary">{m.description}</Typography>
            </CardActionArea>
          </Card>
        ))}
      </Box>
    </Box>
  );
}
