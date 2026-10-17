import { useState } from 'react';
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, TextField, Typography } from '@mui/material';
import type { SignatureDef } from './definition';
import { roleLabel } from './definition';

/**
 * Re-authentication for a signature block inside a form record. The signer confirms with their password (and authenticator code when
 * their account uses one); the signature is bound to the content as saved.
 */
export function FormSignDialog({ block, recordNumber, onSign, onClose }: { block: SignatureDef; recordNumber: string; onSign: (password: string, mfaCode: string | null, comment: string | null) => Promise<void>; onClose: () => void }) {
  const [password, setPassword] = useState('');
  const [mfa, setMfa] = useState('');
  const [comment, setComment] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const go = async () => {
    setBusy(true); setError(null);
    try { await onSign(password, mfa.trim() || null, comment.trim() || null); onClose(); }
    catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  };
  return (
    <Dialog open onClose={onClose} maxWidth="xs" fullWidth>
      <DialogTitle>{block.label}: {recordNumber}</DialogTitle>
      <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
        <Typography variant="body2"><strong>{roleLabel[block.role] ?? block.role}.</strong> {block.meaning}</Typography>
        <Typography variant="body2" color="text.secondary">Unsaved changes are saved first. Your name, the time, this meaning and a fingerprint of the content are recorded; changing the content later invalidates the signature.</Typography>
        {error && <Alert severity="error">{error}</Alert>}
        <TextField label="Comment (optional)" multiline minRows={2} value={comment} onChange={(e) => setComment(e.target.value)} slotProps={{ htmlInput: { maxLength: 1000 } }} />
        <TextField label="Your password" type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} required />
        <TextField label="Authenticator code (if your account uses one)" value={mfa} onChange={(e) => setMfa(e.target.value)} slotProps={{ htmlInput: { inputMode: 'numeric', autoComplete: 'one-time-code' } }} />
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" disabled={busy || !password} onClick={go}>Sign</Button>
      </DialogActions>
    </Dialog>
  );
}
