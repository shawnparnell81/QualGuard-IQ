import { Alert, Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { get } from '../api';
import { DataTable } from '../components/DataTable';
import { StatusChip, fmtDateTime } from '../components/Status';

interface SignIn { occurredAt: string; eventType: string; succeeded: boolean; ipAddress: string | null; userAgent: string | null; detail: string | null }
const labels: Record<string, string> = { SignIn: 'Signed in', SignInFailed: 'Failed sign-in', SignInLockedOut: 'Locked out', SignOut: 'Signed out' };

export function SignInHistory() {
  const q = useQuery({ queryKey: ['sign-ins'], queryFn: () => get<{ enabled: boolean; items: SignIn[] }>('/api/account/sign-ins') });
  return (
    <Box sx={{ maxWidth: 1240, mx: 'auto', p: 4 }}>
      <Typography variant="h1" sx={{ mb: 2 }}>Sign-in history</Typography>
      {q.data && !q.data.enabled
        ? <Alert severity="info">Login history is turned off for your organization. An administrator can turn it on in Organization settings.</Alert>
        : <DataTable data={q.data?.items ?? []} empty="No sign-in activity recorded." columns={[
            { header: 'When', accessorKey: 'occurredAt', cell: (c) => fmtDateTime(c.getValue() as string) },
            { header: 'Event', accessorKey: 'eventType', cell: (c) => labels[c.getValue() as string] ?? c.getValue() },
            { header: 'Result', accessorKey: 'succeeded', cell: (c) => <StatusChip status={c.getValue() ? 'Succeeded' : 'Failed'} /> },
            { header: 'IP address', accessorKey: 'ipAddress' },
            { header: 'Browser', accessorKey: 'userAgent', cell: (c) => <Typography fontSize={12} noWrap sx={{ maxWidth: 420 }}>{(c.getValue() as string) ?? '—'}</Typography> },
            { header: 'Detail', accessorKey: 'detail' },
          ]} />}
    </Box>
  );
}
