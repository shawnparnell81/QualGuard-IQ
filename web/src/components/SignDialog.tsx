import { useState } from 'react';
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, TextField, Typography } from '@mui/material';
import { useQueryClient } from '@tanstack/react-query';
import { post } from '../api';

/** Electronic signature: the signer re-enters their password; the meaning is shown before signing. */
export function SignDialog({ approvalId, recordNumber, stepName, onClose }: { approvalId: string; recordNumber: string; stepName: string; onClose: () => void }) {
  const qc = useQueryClient();
  const [password, setPassword] = useState('');
  const [comment, setComment] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const sign = async (approve: boolean) => {
    setBusy(true); setError(null);
    try {
      await post(`/api/my/approvals/${approvalId}/sign`, { approve, password, comment: comment || null });
      await qc.invalidateQueries();
      onClose();
    } catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  };
  return (
    <Dialog open onClose={onClose} maxWidth="xs" fullWidth>
      <DialogTitle>Sign {recordNumber}</DialogTitle>
      <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
        <Typography variant="body2" color="text.secondary">Step: {stepName}. Your name, the time and the meaning of this signature are recorded permanently.</Typography>
        {error && <Alert severity="error">{error}</Alert>}
        <TextField label="Comment (required to reject)" multiline minRows={2} value={comment} onChange={(e) => setComment(e.target.value)} />
        <TextField label="Your password" type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} required />
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button color="error" disabled={busy || !password} onClick={() => sign(false)}>Reject</Button>
        <Button variant="contained" disabled={busy || !password} onClick={() => sign(true)}>Approve and sign</Button>
      </DialogActions>
    </Dialog>
  );
}
