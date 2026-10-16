import { createTheme } from '@mui/material/styles';
import '@fontsource/ibm-plex-sans/400.css';
import '@fontsource/ibm-plex-sans/500.css';
import '@fontsource/ibm-plex-sans/600.css';
import '@fontsource/ibm-plex-mono/400.css';

/** Deep spruce and copper on warm paper: calm, high-contrast, built for long working sessions. */
export const makeTheme = (primary?: string | null) =>
  createTheme({
    palette: {
      mode: 'light',
      primary: { main: primary ?? '#1F4E4A', contrastText: '#FFFFFF' },
      secondary: { main: '#B4632A' },
      background: { default: '#F6F4EF', paper: '#FFFFFF' },
      text: { primary: '#1D2321', secondary: '#5B6461' },
      divider: '#E2DED5',
      success: { main: '#2E7D4F' },
      warning: { main: '#B7791F' },
      error: { main: '#B3261E' },
      info: { main: '#2F6690' },
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
      MuiPaper: { defaultProps: { elevation: 0 }, styleOverrides: { root: { border: '1px solid #E2DED5' } } },
      MuiAppBar: { styleOverrides: { root: { border: 'none' } } },
      MuiButton: { defaultProps: { disableElevation: true } },
      MuiTableCell: { styleOverrides: { head: { fontWeight: 600, color: '#5B6461', fontSize: 12, textTransform: 'uppercase', letterSpacing: '0.04em' } } },
      MuiChip: { styleOverrides: { root: { fontWeight: 500 } } },
    },
  });

export const mono = '"IBM Plex Mono", ui-monospace, monospace';
