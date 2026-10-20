import { PageNav } from '../tabs/Workspace';
import { useSearchParams, useNavigate } from 'react-router-dom';
import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { get } from '../api';
import { DataTable } from '../components/DataTable';
import { StatusChip } from '../components/Status';
import { mono } from '../theme';

interface Hit { recordType: string; recordId: string; number: string; title: string; status: string | null; url: string }

export function Search() {
  const [p] = useSearchParams();
  const nav = useNavigate();
  const q = p.get('q') ?? '';
  const hits = useQuery({ queryKey: ['search', q], queryFn: () => get<Hit[]>(`/api/search?q=${encodeURIComponent(q)}&limit=100`), enabled: q.length >= 2 });
  return (
    <Box sx={{ maxWidth: 1240, mx: 'auto', p: 4 }}>
      <Box sx={{ mb: 1 }}><PageNav /></Box>
      <Typography variant="h1" sx={{ mb: 0.5 }}>Search</Typography>
      <Typography color="text.secondary" sx={{ mb: 2 }}>Results for “{q}”. Only records you are allowed to open are shown.</Typography>
      <DataTable data={hits.data ?? []} onRowClick={(h) => nav(h.url)} empty="No matching records." columns={[
        { header: 'Number', accessorKey: 'number', cell: (c) => <span style={{ fontFamily: mono }}>{c.getValue()}</span> },
        { header: 'Title', accessorKey: 'title' },
        { header: 'Type', accessorKey: 'recordType' },
        { header: 'Status', accessorKey: 'status', cell: (c) => c.getValue() ? <StatusChip status={c.getValue()} /> : null },
      ]} />
    </Box>
  );
}
