import { alpha, createTheme, useTheme, type PaletteMode } from '@mui/material/styles';
import '@fontsource/ibm-plex-sans/400.css';
import '@fontsource/ibm-plex-sans/500.css';
import '@fontsource/ibm-plex-sans/600.css';
import '@fontsource/ibm-plex-mono/400.css';
import { brand } from './brand';

declare module '@mui/material/styles' {
  interface TypeBackground { sidebar: string; bar: string }
}

const valid = (c?: string | null) => (c && /^#[0-9a-fA-F]{6}$/.test(c) ? c : null);

/**
 * QualGuard IQ theme: logo blue on cool neutrals, charcoal text, copper as the secondary accent. Light and dark modes share
 * one structure. An organization colour from Settings › Branding replaces the light-mode primary; dark mode keeps the lifted
 * logo blue so text and links stay readable.
 */
export const makeTheme = (primary?: string | null, mode: PaletteMode = 'light') => {
  const dark = mode === 'dark';
  const custom = dark ? null : valid(primary);
  const main = custom ?? (dark ? brand.blueOnDark : brand.blue);
  const focus = dark ? '#8DB8F7' : brand.blue;
  const divider = dark ? '#2A3340' : '#E1E5EA';
  return createTheme({
    palette: {
      mode,
      primary: custom
        ? { main: custom, contrastText: '#FFFFFF' }
        : dark
          ? { main: brand.blueOnDark, light: '#7AAEF5', dark: '#2F74D6', contrastText: '#0B1220' }
          : { main: brand.blue, light: brand.blueLight, dark: brand.blueDark, contrastText: '#FFFFFF' },
      secondary: dark ? { main: '#D08A55', contrastText: '#1A1208' } : { main: brand.copper, contrastText: '#FFFFFF' },
      background: dark
        ? { default: '#121821', paper: '#1A212C', sidebar: '#161D27', bar: '#161D27' }
        : { default: '#F4F6F8', paper: '#FFFFFF', sidebar: '#EDF1F6', bar: '#FFFFFF' },
      text: dark ? { primary: '#E6EAF0', secondary: '#A8B3C2' } : { primary: brand.charcoal, secondary: '#5B6676' },
      divider,
      success: { main: dark ? '#5DBB83' : '#2E7D4F' },
      warning: { main: dark ? '#E0A84A' : '#B7791F' },
      error: { main: dark ? '#F07B72' : '#B3261E' },
      info: { main: dark ? '#7FB0DA' : '#2F6690' },
      action: dark ? { hover: alpha('#FFFFFF', 0.06), selected: alpha(brand.blueOnDark, 0.16) } : { hover: alpha(brand.blue, 0.05), selected: alpha(brand.blue, 0.1) },
    },
    shape: { borderRadius: 6 },
    typography: {
      fontFamily: '"IBM Plex Sans", system-ui, sans-serif',
      fontSize: 13.5,
      h1: { fontSize: '1.6rem', fontWeight: 600 },
      h2: { fontSize: '1.3rem', fontWeight: 600 },
      h3: { fontSize: '1.1rem', fontWeight: 600 },
      button: { textTransform: 'none', fontWeight: 500 },
      overline: { letterSpacing: '0.08em', fontWeight: 600 },
    },
    components: {
      MuiCssBaseline: {
        styleOverrides: {
          // one visible keyboard focus ring everywhere (mouse clicks do not show it)
          ':focus-visible': { outline: `2px solid ${focus}`, outlineOffset: 2 },
          a: { color: main },
        },
      },
      MuiPaper: { defaultProps: { elevation: 0 }, styleOverrides: { root: { border: `1px solid ${divider}`, backgroundImage: 'none' } } },
      MuiAppBar: { styleOverrides: { root: { border: 'none' } } },
      MuiButtonBase: { styleOverrides: { root: { '&.Mui-focusVisible': { outline: `2px solid ${focus}`, outlineOffset: 2 } } } },
      MuiButton: {
        defaultProps: { disableElevation: true },
        styleOverrides: {
          containedPrimary: custom ? {} : { '&:hover': { backgroundColor: dark ? '#7AAEF5' : brand.blueHover }, '&:active': { backgroundColor: dark ? '#2F74D6' : brand.blueDark } },
        },
      },
      MuiTableCell: { styleOverrides: { head: { fontWeight: 600, color: dark ? '#A8B3C2' : '#5B6676', fontSize: 12, textTransform: 'uppercase', letterSpacing: '0.04em' } } },
      MuiChip: { styleOverrides: { root: { fontWeight: 500 } } },
      MuiLink: { defaultProps: { underline: 'hover' } },
    },
  });
};

export const mono = '"IBM Plex Mono", ui-monospace, monospace';

/** Theme colours for charts drawn with Recharts or plain SVG (props take plain strings, not sx tokens). */
export function useChartColors() {
  const t = useTheme();
  return { grid: t.palette.divider, primary: t.palette.primary.main, accent: t.palette.secondary.main, ink: t.palette.text.primary, muted: t.palette.text.secondary };
}
