import { useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { get } from '../api';
import { useColorMode, type ColorPreference } from '../colorMode';
import { setDateFormat } from '../components/Status';

/** Applies the signed-in person's saved settings (theme, date format) on this device. Renders nothing. */
export function PreferencesSync() {
  const { preference, setPreference } = useColorMode();
  const q = useQuery({ queryKey: ['preferences'], queryFn: () => get<{ theme: string | null; effectiveDateFormat: string }>('/api/account/preferences'), staleTime: 300_000 });
  useEffect(() => {
    if (!q.data) return;
    setDateFormat(q.data.effectiveDateFormat);
    const t = q.data.theme as ColorPreference | null;
    if (t && t !== preference) setPreference(t);
  }, [q.data]);
  return null;
}
