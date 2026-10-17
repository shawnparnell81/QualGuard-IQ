import { Alert, Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { get } from '../api';
import { DataTable } from '../components/DataTable';
import { StatusChip, fmtDateTime } from '../components/Status';

interface SignIn { occurredAt: string; eventType: string; succeeded: boolean; ipAddress: string | null; userAgent: string | null; detail: string | null }
const labels: Record<string, string> = {
  SignIn: 'Signed in', SignInFailed: 'Failed sign-in', SignInLockedOut: 'Locked out', SignOut: 'Signed out',
  SignInStepRequired: 'Password accepted, next step asked', SignInStepFailed: 'Sign-in step expired or invalid', MfaFailed: 'Wrong authentication code',
  MfaEnrolled: 'Two-step verification turned on', MfaReplaced: 'Authenticator app replaced', MfaDisabled: 'Two-step verification turned off', MfaReset: 'Two-step verification reset by an administrator',
  MfaSetupFailed: 'Authenticator setup code refused', RecoveryCodeUsed: 'Recovery code used', RecoveryCodesRegenerated: 'New recovery codes created',
  TrustedDeviceAdded: 'Browser trusted', TrustedDeviceRevoked: 'Trusted browser removed', TrustedDevicesRevoked: 'All trusted browsers removed',
  PasswordChanged: 'Password changed', PasswordResetRequested: 'Password reset link requested', PasswordResetCompleted: 'Password reset', PasswordResetFailed: 'Password reset refused',
  TemporaryPasswordSet: 'Temporary password set by an administrator', TokenFailed: 'API token refused', ReauthenticationFailed: 'Password confirmation failed',
};

export function SignInHistory() {
  const q = useQuery({ queryKey: ['sign-ins'], queryFn: () => get<{ enabled: boolean; items: SignIn[] }>('/api/account/sign-ins') });
  return (
    <Box sx={{ maxWidth: 1240, mx: 'auto', p: { xs: 2, sm: 4 } }}>
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
