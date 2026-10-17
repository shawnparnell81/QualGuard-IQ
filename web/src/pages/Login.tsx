import { useState } from 'react';
import { Alert, Box, Button, TextField } from '@mui/material';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useAuth, useBranding } from '../auth';
import { AuthShell } from '../components/AuthShell';

const schema = z.object({ email: z.string().email('Enter a valid email'), password: z.string().min(1, 'Required') });

export function Login() {
  const { login } = useAuth();
  const { data: brand } = useBranding();
  const [error, setError] = useState<string | null>(null);
  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<z.infer<typeof schema>>({ resolver: zodResolver(schema) });
  return (
    <AuthShell title="Sign in" subtitle="Use your work email and password." organization={brand?.organizationName}>
      <Box component="form" noValidate onSubmit={handleSubmit(async (v) => { setError(null); try { await login(v.email, v.password); } catch (e) { setError((e as Error).message); } })}
        sx={{ display: 'grid', gap: 2 }}>
        {error && <Alert severity="error">{error}</Alert>}
        <TextField label="Email" type="email" autoComplete="username" autoFocus {...register('email')} error={!!errors.email} helperText={errors.email?.message} />
        <TextField label="Password" type="password" autoComplete="current-password" {...register('password')} error={!!errors.password} helperText={errors.password?.message} />
        <Button type="submit" variant="contained" size="large" disabled={isSubmitting} sx={{ mt: 0.5, py: 1.1 }}>Sign in</Button>
      </Box>
    </AuthShell>
  );
}
