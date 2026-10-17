import { useEffect } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Alert, Box } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { get } from '../api';

/** Opens any record by type and id, using the URL the owning module registered. */
export function RecordRedirect() {
  const { type, id } = useParams();
  const nav = useNavigate();
  const q = useQuery({ queryKey: ['record-summary', type, id], queryFn: () => get<{ url: string }>(`/api/records/${type}/${id}`), retry: false });
  useEffect(() => { if (q.data) nav(q.data.url, { replace: true }); }, [q.data, nav]);
  return <Box sx={{ p: 3 }}>{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : 'Opening…'}</Box>;
}
