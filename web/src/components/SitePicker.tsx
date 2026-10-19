import { SetupHint } from './SetupLink';
import { Autocomplete, CircularProgress, TextField } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { get } from '../api';

export interface AccessibleSite { id: string; code: string; name: string; timeZone: string }

/** Active sites where the signed-in user holds `permission`; the server filters them, so the list never offers a site the save would refuse. */
export const useAccessibleSites = (permission: string) =>
  useQuery({ queryKey: ['accessible-sites', permission], queryFn: () => get<AccessibleSite[]>(`/api/organization/sites/accessible?permission=${encodeURIComponent(permission)}`) });

/**
 * Shared searchable site selector. Shows the site name, stores the site id. Type to filter by name or code; arrow keys and Enter pick.
 * Used wherever a dialog needs a site (layered audits, cost of quality, ...). Do not build module-specific copies.
 */
export function SitePicker({ permission, value, onChange, label = 'Site', helperText, required, testId }: {
  permission: string; value: string | null | undefined; onChange: (id: string | null) => void; label?: string; helperText?: string; required?: boolean; testId?: string;
}) {
  const q = useAccessibleSites(permission);
  const options = q.data ?? [];
  const selected = options.find((s) => s.id === value) ?? null;
  const empty = !q.isLoading && options.length === 0;
  return (
    <Autocomplete
      options={options} value={selected} loading={q.isLoading} autoHighlight openOnFocus
      getOptionLabel={(s) => `${s.name} (${s.code})`} isOptionEqualToValue={(a, b) => a.id === b.id}
      filterOptions={(opts, st) => { const t = st.inputValue.trim().toLowerCase(); return t ? opts.filter((s) => s.name.toLowerCase().includes(t) || s.code.toLowerCase().includes(t)) : opts; }}
      noOptionsText={empty ? 'You have no active sites for this work' : 'No matching site'} loadingText="Loading sites…"
      onChange={(_, s) => onChange(s?.id ?? null)}
      renderInput={(p) => <TextField {...p} label={label} required={required} error={empty}
        helperText={empty ? <>No sites available for this work. <SetupHint kind="site" count={1} fallback="Ask an administrator for access to a site." /></> : <>{helperText}{helperText ? ' ' : ''}<SetupHint kind="site" count={options.length} /></>}
        slotProps={{ htmlInput: { ...p.inputProps, 'data-testid': testId ?? 'site-picker' }, input: { ...p.InputProps, endAdornment: <>{q.isLoading ? <CircularProgress size={16} /> : null}{p.InputProps.endAdornment}</> } }} />}
    />
  );
}
