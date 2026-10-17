import { Alert, Box, Button, Paper, Stack, Typography } from '@mui/material';
import { fmtDateTime } from './Status';
import type { useOfflineQueue } from '../offline';

type Q = ReturnType<typeof useOfflineQueue>;

/**
 * Sync status for the device's offline queue: saved locally / pending sync, synchronizing, synchronized, sync failed and
 * conflict requires review. The local-only banner clears only after the server has accepted every pending item.
 */
export function SyncBanner({ q }: { q: Q }) {
  const { pending, syncing, failed, conflicts, synced, online } = q;
  return <Stack gap={1} sx={{ mt: 2 }} data-testid="sync-banner">
    {pending.length > 0 && <Paper sx={{ p: 2, borderLeft: 5, borderColor: syncing.length ? 'info.main' : 'warning.main' }} data-testid="sync-pending">
      <Stack direction="row" justifyContent="space-between" alignItems="center">
        <Typography sx={{ fontWeight: 700 }}>
          {syncing.length ? `Synchronizing ${pending.length} item${pending.length > 1 ? 's' : ''}…` : online ? `Saved locally — ${pending.length} pending sync` : `Saved locally — ${pending.length} pending sync (offline)`}
        </Typography>
        <Button size="small" disabled={!online || syncing.length > 0} onClick={() => q.flush()}>Sync now</Button>
      </Stack>
      <Typography variant="caption" color="text.secondary">These are on this device only until the server confirms them.</Typography>
      {pending.map((i) => <Box key={i.id} sx={{ py: 0.75, borderTop: 1, borderColor: 'divider' }}>
        <Typography variant="body2">{i.label}</Typography>
        <Typography variant="caption" color="text.secondary">{i.state === 'syncing' ? 'Sending…' : `Saved ${fmtDateTime(i.at)}`}</Typography></Box>)}
    </Paper>}
    {failed.length > 0 && <Paper sx={{ p: 2, borderLeft: 5, borderColor: 'error.main' }} data-testid="sync-failed">
      <Typography sx={{ fontWeight: 700 }}>Sync failed ({failed.length})</Typography>
      {failed.map((i) => <Stack key={i.id} direction="row" justifyContent="space-between" alignItems="center" sx={{ py: 0.75, borderTop: 1, borderColor: 'divider' }}>
        <Box><Typography variant="body2">{i.label}</Typography><Typography variant="caption" color="error">Refused: {i.error}</Typography></Box>
        <Stack direction="row" gap={1}><Button size="small" disabled={!online} onClick={() => q.retry(i.id)}>Retry</Button><Button size="small" color="inherit" onClick={() => q.discard(i.id)}>Discard</Button></Stack></Stack>)}
    </Paper>}
    {conflicts.length > 0 && <Paper sx={{ p: 2, borderLeft: 5, borderColor: 'secondary.main' }} data-testid="sync-conflict">
      <Typography sx={{ fontWeight: 700 }}>Conflict requires review ({conflicts.length})</Typography>
      {conflicts.map((i) => <Stack key={i.id} direction="row" justifyContent="space-between" alignItems="center" sx={{ py: 0.75, borderTop: 1, borderColor: 'divider' }}>
        <Box><Typography variant="body2">{i.label}</Typography><Typography variant="caption">{i.error}</Typography></Box>
        <Button size="small" color="inherit" onClick={() => q.discard(i.id)}>Reviewed — remove</Button></Stack>)}
    </Paper>}
    {pending.length === 0 && synced && <Alert severity="success" onClose={() => q.clearSynced()} data-testid="sync-done">Synchronized — the server accepted {synced.count} item{synced.count > 1 ? 's' : ''} ({fmtDateTime(synced.at)}).</Alert>}
  </Stack>;
}
