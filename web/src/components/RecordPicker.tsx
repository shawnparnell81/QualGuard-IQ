import { useState } from 'react';
import { Autocomplete, MenuItem, Stack, TextField } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { get } from '../api';

export interface PickedRecord { type: string; id: string; label: string }
interface Hit { recordType: string; recordId: string; number: string; title: string }

/**
 * Finds any record the user can see (shared search). For documents, drawings and inspection plans it can also
 * narrow to a specific revision, which is what change evidence needs.
 */
export function RecordPicker({ label, onChange, revisions = false }: { label: string; onChange: (r: PickedRecord | null) => void; revisions?: boolean }) {
  const [q, setQ] = useState('');
  const [hit, setHit] = useState<Hit | null>(null);
  const [rev, setRev] = useState('');
  const hits = useQuery({ queryKey: ['picker', q], queryFn: () => get<Hit[]>(`/api/search?q=${encodeURIComponent(q)}&limit=15`), enabled: q.length >= 2 });
  const isDoc = hit?.recordType === 'Document' || hit?.recordType === 'Drawing';
  const isPlan = hit?.recordType === 'InspectionPlan';
  const revs = useQuery({
    queryKey: ['picker-revs', hit?.recordType, hit?.recordId], enabled: revisions && !!hit && (isDoc || isPlan),
    queryFn: async () => isDoc
      ? (await get<{ revisions: { id: string; revision: string; status: number }[] }>(`/api/documents/${hit!.recordId}`)).revisions.map((r) => ({ id: r.id, label: `Rev ${r.revision} (${['Draft', 'In review', 'Approved', 'Effective', 'Superseded', 'Obsolete'][r.status]})` }))
      : (await get<{ revisions: { id: string; revision: string; status: number }[] }>(`/api/inspection/plans/${hit!.recordId}`)).revisions.map((r) => ({ id: r.id, label: `Rev ${r.revision} (${['Draft', 'In review', 'Effective', 'Superseded'][r.status]})` })),
  });
  const emit = (h: Hit | null, r: string) => {
    if (!h) return onChange(null);
    if (revisions && r && (h.recordType === 'Document' || h.recordType === 'Drawing' || h.recordType === 'InspectionPlan'))
      return onChange({ type: h.recordType === 'Document' ? 'DocumentRevision' : h.recordType === 'Drawing' ? 'DrawingRevision' : 'InspectionPlanRevision', id: r, label: `${h.number} ${revs.data?.find((x) => x.id === r)?.label ?? ''}` });
    onChange({ type: h.recordType, id: h.recordId, label: `${h.number} ${h.title}` });
  };
  return (
    <Stack gap={1.5}>
      <Autocomplete size="small" options={hits.data ?? []} value={hit} filterOptions={(x) => x} onInputChange={(_, v) => setQ(v)}
        getOptionLabel={(o) => `${o.number}  ${o.title}  · ${o.recordType}`} isOptionEqualToValue={(a, b) => a.recordId === b.recordId}
        onChange={(_, v) => { setHit(v); setRev(''); emit(v, ''); }} renderInput={(p) => <TextField {...p} label={label} placeholder="Type a number or title" />} />
      {revisions && (isDoc || isPlan) && <TextField size="small" select label="Revision" value={rev} onChange={(e) => { setRev(e.target.value); emit(hit, e.target.value); }}>
        {(revs.data ?? []).map((r) => <MenuItem key={r.id} value={r.id}>{r.label}</MenuItem>)}</TextField>}
    </Stack>
  );
}
