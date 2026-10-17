import type { ReactNode } from 'react';
import { Box, Paper, Typography } from '@mui/material';
import { alpha } from '@mui/material/styles';
import { BrandLogo } from './Brand';

/**
 * Shared frame for every signed-out screen (sign in and any later step such as a verification code or password change):
 * the full logo centred above one card, on a quiet blue wash.
 */
export function AuthShell({ title, subtitle, organization, children, footer }: { title: string; subtitle?: string; organization?: string; children: ReactNode; footer?: ReactNode }) {
  return (
    <Box component="main" sx={(t) => ({
      minHeight: '100vh', display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', px: 2, py: { xs: 4, sm: 6 },
      bgcolor: 'background.default',
      backgroundImage: `radial-gradient(ellipse 80% 55% at 50% -10%, ${alpha(t.palette.primary.main, t.palette.mode === 'dark' ? 0.22 : 0.12)}, transparent 70%)`,
    })}>
      <Box sx={{ width: '100%', maxWidth: 420, display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
        <BrandLogo variant="full" label="QualGuard IQ" width={{ xs: 260, sm: 320 }} sx={{ mb: { xs: 3, sm: 4 } }} />
        <Paper sx={{ width: '100%', p: { xs: 3, sm: 4 }, borderRadius: 2, borderTop: 3, borderTopColor: 'primary.main', boxShadow: (t) => t.palette.mode === 'dark' ? 'none' : '0 12px 32px rgba(18, 38, 70, 0.08)' }}>
          <Typography variant="h2" component="h1" sx={{ mb: subtitle ? 0.5 : 2.5 }}>{title}</Typography>
          {subtitle && <Typography variant="body2" color="text.secondary" sx={{ mb: 2.5 }}>{subtitle}</Typography>}
          {children}
        </Paper>
        <Box sx={{ mt: 3, textAlign: 'center' }}>
          {organization && <Typography variant="body2" color="text.secondary">{organization}</Typography>}
          {footer}
        </Box>
      </Box>
    </Box>
  );
}
