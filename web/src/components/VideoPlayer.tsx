import { useState } from 'react';
import { Alert, Box, Button, Chip, Stack, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { get, post } from '../api';

const formats = ['Mp4', 'WebM'];
const status = ['Queued', 'Converting', 'Ready', 'Failed'];
const color: ('default' | 'info' | 'success' | 'error')[] = ['default', 'info', 'success', 'error'];
interface Rendition { id: string; format: number; status: number; attempts: number; nextAttemptAt?: string; error?: string; sizeBytes?: number; durationSeconds?: number; width?: number; height?: number; videoCodec?: string; audioCodec?: string; sourceVideoCodec?: string; reused: boolean }
interface VideoStatus { converterAvailable: boolean; converter: string; formats: number[]; renditions: Rendition[] }

/** Plays the validated playback copy when one is ready and falls back to the original file otherwise; shows conversion status and lets editors retry. */
export function VideoPlayer({ fileId, fileName, canEdit }: { fileId: string; fileName: string; canEdit?: boolean }) {
  const qc = useQueryClient();
  const q = useQuery({ queryKey: ['renditions', fileId], queryFn: () => get<VideoStatus>(`/api/files/${fileId}/renditions`), refetchInterval: (x) => (x.state.data?.renditions.some((r) => r.status < 2) && x.state.data.converterAvailable ? 10000 : false) });
  const [error, setError] = useState<string | null>(null);
  const ready = q.data?.renditions.filter((r) => r.status === 2) ?? [];
  const src = ready.length ? `/api/files/${fileId}/playback/${formats[ready[0].format]}` : `/api/files/${fileId}?inline=true`;
  return (
    <Box sx={{ mb: 1 }}>
      <Box component="video" key={src} controls preload="metadata" sx={{ width: '100%', maxHeight: 360, bgcolor: '#000', borderRadius: 1 }}>
        {ready.map((r) => <source key={r.id} src={`/api/files/${fileId}/playback/${formats[r.format]}`} type={r.format === 1 ? 'video/webm' : 'video/mp4'} />)}
        <source src={`/api/files/${fileId}?inline=true`} />
      </Box>
      <Stack direction="row" gap={1} alignItems="center" flexWrap="wrap" sx={{ mt: 1 }} data-testid="video-status">
        <Typography variant="caption" color="text.secondary">{ready.length ? 'Playing the playback copy.' : 'Playing the original file.'}</Typography>
        {(q.data?.renditions ?? []).map((r) => <Chip key={r.id} size="small" color={color[r.status]} label={`${formats[r.format].toUpperCase()}: ${status[r.status]}${r.status === 2 && r.height ? ` ${r.height}p` : ''}`} />)}
        <Button size="small" href={`/api/files/${fileId}`} download={fileName}>Download original</Button>
        {canEdit && q.data?.renditions.some((r) => r.status === 3) && <Button size="small" onClick={async () => { setError(null); try { await post(`/api/files/${fileId}/renditions/retry`); qc.invalidateQueries({ queryKey: ['renditions', fileId] }); } catch (e) { setError((e as Error).message); } }}>Retry conversion</Button>}
      </Stack>
      {q.data && !q.data.converterAvailable && q.data.renditions.some((r) => r.status !== 2) && <Alert severity="info" sx={{ mt: 1 }}>Playback copies are not being made: {q.data.converter}</Alert>}
      {(q.data?.renditions ?? []).filter((r) => r.status === 3 || (r.status === 0 && r.attempts > 0)).map((r) => <Alert key={r.id} severity={r.status === 3 ? 'error' : 'warning'} sx={{ mt: 1 }}>{formats[r.format].toUpperCase()} conversion {r.status === 3 ? `failed after ${r.attempts} attempts` : `will be retried`}: {r.error}</Alert>)}
      {ready[0] && <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: .5 }}>Copy: {ready[0].videoCodec}/{ready[0].audioCodec ?? 'no audio'} · {ready[0].width}×{ready[0].height} · {ready[0].durationSeconds?.toFixed(1)} s · from {ready[0].sourceVideoCodec}{ready[0].reused ? ' · reused from identical upload' : ''}</Typography>}
      {error && <Alert severity="error" sx={{ mt: 1 }}>{error}</Alert>}
    </Box>
  );
}
