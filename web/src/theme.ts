import { alpha, createTheme, useTheme, type PaletteMode } from '@mui/material/styles';
import '@fontsource/manrope/400.css';
import '@fontsource/manrope/500.css';
import '@fontsource/manrope/600.css';
import '@fontsource/manrope/700.css';
import '@fontsource/ibm-plex-mono/400.css';
import { brand } from './brand';

declare module '@mui/material/styles' {
  interface TypeBackground { sidebar: string; bar: string; band: string }
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
  const divider = dark ? '#28313D' : '#D9DEE5';
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
        ? { default: '#0F141B', paper: '#171D26', sidebar: '#131922', bar: '#161D27', band: '#1D2530' }
        : { default: '#ECEFF3', paper: '#FFFFFF', sidebar: '#F7F8FA', bar: '#FFFFFF', band: '#F3F5F8' },
      text: dark ? { primary: '#E6EAF0', secondary: '#A8B3C2' } : { primary: brand.charcoal, secondary: '#5B6676' },
      divider,
      success: { main: dark ? '#5DBB83' : '#2E7D4F' },
      warning: { main: dark ? '#E0A84A' : '#B7791F' },
      error: { main: dark ? '#F07B72' : '#B3261E' },
      info: { main: dark ? '#7FB0DA' : '#2F6690' },
      action: dark ? { hover: alpha('#FFFFFF', 0.06), selected: alpha(brand.blueOnDark, 0.16) } : { hover: alpha(brand.blue, 0.05), selected: alpha(brand.blue, 0.1) },
    },
    // Squared panels, hairline rules and a compact type scale: a dense instrument-panel look rather than soft cards.
    shape: { borderRadius: 3 },
    typography: {
      fontFamily: '"Manrope", system-ui, sans-serif',
      fontSize: 13,
      h1: { fontSize: '1.45rem', fontWeight: 700, letterSpacing: '-0.015em' },
      h2: { fontSize: '1.2rem', fontWeight: 700, letterSpacing: '-0.01em' },
      h3: { fontSize: '0.98rem', fontWeight: 700 },
      button: { textTransform: 'none', fontWeight: 600, letterSpacing: '0.01em' },
      overline: { fontSize: '0.68rem', letterSpacing: '0.14em', fontWeight: 700, lineHeight: 1.8 },
      caption: { letterSpacing: '0.01em' },
    },
    components: {
      MuiCssBaseline: {
        styleOverrides: {
          // one visible keyboard focus ring everywhere (mouse clicks do not show it)
          ':focus-visible': { outline: `2px solid ${focus}`, outlineOffset: 2 },
          a: { color: main },
        },
      },
      MuiPaper: { defaultProps: { elevation: 0 }, styleOverrides: { root: { border: `1px solid ${divider}`, backgroundImage: 'none' }, rounded: { borderRadius: 3 } } },
      MuiCard: { styleOverrides: { root: { borderRadius: 3 } } },
      MuiMenu: { styleOverrides: { paper: { boxShadow: dark ? 'none' : '0 6px 18px rgba(20, 32, 50, 0.12)' } } },
      MuiDialog: { styleOverrides: { paper: { borderRadius: 4, borderTop: `3px solid ${main}` } } },
      MuiTab: { styleOverrides: { root: { textTransform: 'none', fontWeight: 600, minWidth: 0, paddingLeft: 14, paddingRight: 14 } } },
      MuiTabs: { styleOverrides: { indicator: { height: 3 } } },
      MuiOutlinedInput: { styleOverrides: { root: { borderRadius: 3 } } },
      MuiTooltip: { styleOverrides: { tooltip: { borderRadius: 2, fontSize: 12 } } },
      MuiAppBar: { styleOverrides: { root: { border: 'none' } } },
      MuiButtonBase: { styleOverrides: { root: { '&.Mui-focusVisible': { outline: `2px solid ${focus}`, outlineOffset: 2 } } } },
      MuiButton: {
        defaultProps: { disableElevation: true },
        styleOverrides: {
          containedPrimary: custom ? {} : { '&:hover': { backgroundColor: dark ? '#7AAEF5' : brand.blueHover }, '&:active': { backgroundColor: dark ? '#2F74D6' : brand.blueDark } },
        },
      },
      MuiTableCell: {
        styleOverrides: {
          root: { borderColor: divider },
          head: { fontWeight: 700, color: dark ? '#A8B3C2' : '#4B5563', fontSize: 11, textTransform: 'uppercase', letterSpacing: '0.1em', backgroundColor: dark ? '#1D2530' : '#F3F5F8' },
        },
      },
      MuiTableRow: { styleOverrides: { root: { '&.MuiTableRow-hover:hover': { backgroundColor: dark ? alpha(brand.blueOnDark, 0.08) : alpha(brand.blue, 0.04) } } } },
      MuiChip: { styleOverrides: { root: { fontWeight: 600, borderRadius: 3 }, sizeSmall: { height: 22, fontSize: 11.5 } } },
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
