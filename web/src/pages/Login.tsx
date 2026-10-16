import { useState } from 'react';
import { Alert, Box, Button, Paper, TextField, Typography } from '@mui/material';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useAuth, useBranding } from '../auth';
import { mono } from '../theme';

const schema = z.object({ email: z.string().email('Enter a valid email'), password: z.string().min(1, 'Required') });

export function Login() {
  const { login } = useAuth();
  const { data: brand } = useBranding();
  const [error, setError] = useState<string | null>(null);
  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<z.infer<typeof schema>>({ resolver: zodResolver(schema) });
  return (
    <Box sx={{ minHeight: '100vh', display: 'grid', gridTemplateColumns: { md: '1.1fr 1fr' } }}>
      <Box sx={{ bgcolor: 'primary.main', color: 'white', p: 6, display: { xs: 'none', md: 'flex' }, flexDirection: 'column', justifyContent: 'space-between' }}>
        <Typography sx={{ fontFamily: mono, letterSpacing: '0.1em', opacity: 0.8 }}>{brand?.organizationName}</Typography>
        <Box>
          <Typography sx={{ fontSize: 40, fontWeight: 600, lineHeight: 1.1 }}>{brand?.applicationName}</Typography>
          <Typography sx={{ mt: 2, opacity: 0.85, maxWidth: 440 }}>Documents, drawings, approvals and actions in one controlled system.</Typography>
        </Box>
        <Box sx={{ height: 4, width: 80, bgcolor: 'secondary.main' }} />
      </Box>
      <Box sx={{ display: 'grid', placeItems: 'center', p: 3 }}>
        <Paper component="form" onSubmit={handleSubmit(async (v) => { setError(null); try { await login(v.email, v.password); } catch (e) { setError((e as Error).message); } })}
          sx={{ p: 4, width: 380, display: 'grid', gap: 2 }}>
          <Typography variant="h2">Sign in</Typography>
          {error && <Alert severity="error">{error}</Alert>}
          <TextField label="Email" autoComplete="username" {...register('email')} error={!!errors.email} helperText={errors.email?.message} />
          <TextField label="Password" type="password" autoComplete="current-password" {...register('password')} error={!!errors.password} helperText={errors.password?.message} />
          <Button type="submit" variant="contained" size="large" disabled={isSubmitting}>Sign in</Button>
        </Paper>
      </Box>
    </Box>
  );
}
