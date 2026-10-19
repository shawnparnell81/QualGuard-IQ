import type { ReactNode } from 'react';
import { Box, Paper, Typography } from '@mui/material';
import { useBranding } from '../auth';
import { logos } from '../brand';

/**
 * Shared frame for every signed-out screen (sign in and any later step such as a verification code or password change):
 * a dark brand panel on the left with the light-on-dark logo, and the form on a plain surface to the right.
 * On phones the brand panel shrinks to a strip above the form.
 */
export function AuthShell({ title, subtitle, organization, children, footer }: { title: string; subtitle?: string; organization?: string; children: ReactNode; footer?: ReactNode }) {
  const { data: brand } = useBranding();
  return (
    <Box component="main" sx={{ minHeight: '100vh', display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'minmax(360px, 42%) 1fr' }, bgcolor: 'background.default' }}>
      <Box sx={{ bgcolor: '#15181D', color: '#F1F4F8', display: 'flex', flexDirection: 'column', justifyContent: 'space-between', px: { xs: 3, md: 6 }, py: { xs: 3, md: 6 }, borderRight: { md: '4px solid' }, borderBottom: { xs: '4px solid', md: 'none' }, borderImage: 'linear-gradient(180deg, #1257C4, #0E8550) 1',
        backgroundImage: 'radial-gradient(700px 480px at 0% 0%, rgba(74, 143, 240, 0.16), transparent 62%), radial-gradient(640px 460px at 100% 100%, rgba(76, 201, 138, 0.12), transparent 60%)' }}>
        <Box component="img" src={logos.full.dark} alt="QualGuard IQ" data-testid="brand-full" sx={{ width: { xs: 220, md: 300 }, display: 'block' }} />
        <Box sx={{ display: { xs: 'none', md: 'block' } }}>
          <Typography sx={{ fontSize: 26, fontWeight: 700, lineHeight: 1.25, letterSpacing: '-0.01em', maxWidth: 380 }}>Quality records, approvals and evidence in one controlled place.</Typography>
          <Box sx={{ mt: 2, width: 48, height: 3, background: 'linear-gradient(90deg, #4A8FF0, #4CC98A)' }} />
        </Box>
        <Typography sx={{ display: { xs: 'none', md: 'block' }, fontSize: 12, color: '#9FB0C4' }}>{organization ?? ' '}</Typography>
      </Box>
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'center', px: 2, py: { xs: 4, sm: 6 } }}>
        <Box sx={{ width: '100%', maxWidth: 400 }}>
          <Paper sx={{ p: { xs: 3, sm: 4 } }}>
            {brand?.hasLogo && <Box component="img" src="/api/settings/logo" alt={organization ?? ''} data-testid="sign-in-org-logo" sx={{ display: 'block', maxHeight: 56, maxWidth: '70%', mb: 2 }} />}
            <Typography variant="h2" component="h1" sx={{ mb: subtitle ? 0.5 : 2.5 }}>{title}</Typography>
            {subtitle && <Typography variant="body2" color="text.secondary" sx={{ mb: 2.5 }}>{subtitle}</Typography>}
            {children}
          </Paper>
          <Box sx={{ mt: 3, textAlign: 'center' }}>
            {organization && <Typography variant="body2" color="text.secondary" sx={{ display: { md: 'none' } }}>{organization}</Typography>}
            {footer}
          </Box>
        </Box>
      </Box>
    </Box>
  );
}
