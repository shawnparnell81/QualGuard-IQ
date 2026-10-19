import { useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { get } from '../api';
import { setDateFormat } from '../components/Status';

/**
 * Applies the signed-in person's saved date format on this device. Renders nothing.
 * Light, dark or system mode comes from Appearance, which the app shell already applies.
 */
export function PreferencesSync() {
  const q = useQuery({ queryKey: ['preferences'], queryFn: () => get<{ effectiveDateFormat: string }>('/api/account/preferences'), staleTime: 300_000 });
  useEffect(() => { if (q.data) setDateFormat(q.data.effectiveDateFormat); }, [q.data]);
  return null;
}
