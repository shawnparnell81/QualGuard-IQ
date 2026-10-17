import { Box, type SxProps, type Theme, useTheme } from '@mui/material';
import { logos } from '../brand';

type Variant = keyof typeof logos;

/** The QualGuard IQ logo in the variant that suits the current light or dark surface. Decorative unless a label is given. */
export function BrandLogo({ variant = 'full', height, width, label, sx }: { variant?: Variant; height?: number | string | Record<string, number | string>; width?: number | string | Record<string, number | string>; label?: string; sx?: SxProps<Theme> }) {
  const dark = useTheme().palette.mode === 'dark';
  return (
    <Box component="img" src={logos[variant][dark ? 'dark' : 'light']} alt={label ?? ''} aria-hidden={label ? undefined : true}
      draggable={false} data-testid={`brand-${variant}`}
      sx={{ display: 'block', height: height ?? 'auto', width: width ?? 'auto', userSelect: 'none', ...sx } as SxProps<Theme>} />
  );
}
