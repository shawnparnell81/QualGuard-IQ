import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import type { PaletteMode } from '@mui/material';

/** Appearance preference kept on this device: light, dark, or follow the operating system. */
export type ColorPreference = 'light' | 'dark' | 'system';
const KEY = 'qgiq.colorMode';

interface ColorModeState { preference: ColorPreference; mode: PaletteMode; setPreference: (p: ColorPreference) => void }
const Ctx = createContext<ColorModeState>({ preference: 'system', mode: 'light', setPreference: () => {} });

const read = (): ColorPreference => {
  try { const v = localStorage.getItem(KEY); return v === 'light' || v === 'dark' ? v : 'system'; } catch { return 'system'; }
};
const media = () => (typeof window !== 'undefined' && window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null);

export function ColorModeProvider({ children }: { children: ReactNode }) {
  const [preference, setPref] = useState<ColorPreference>(read);
  const [systemDark, setSystemDark] = useState(() => !!media()?.matches);
  useEffect(() => {
    const m = media(); if (!m) return;
    const h = (e: MediaQueryListEvent) => setSystemDark(e.matches);
    m.addEventListener('change', h);
    return () => m.removeEventListener('change', h);
  }, []);
  // another tab changed the preference
  useEffect(() => { const h = (e: StorageEvent) => { if (e.key === KEY) setPref(read()); }; window.addEventListener('storage', h); return () => window.removeEventListener('storage', h); }, []);
  const mode: PaletteMode = preference === 'system' ? (systemDark ? 'dark' : 'light') : preference;
  useEffect(() => { document.documentElement.style.colorScheme = mode; }, [mode]);
  const value = useMemo(() => ({
    preference, mode,
    setPreference: (p: ColorPreference) => { try { if (p === 'system') localStorage.removeItem(KEY); else localStorage.setItem(KEY, p); } catch { /* private mode */ } setPref(p); },
  }), [preference, mode]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export const useColorMode = () => useContext(Ctx);
