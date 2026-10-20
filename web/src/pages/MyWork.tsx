import { PageNav } from '../tabs/Workspace';
import { useState } from 'react';
import { Box, Button, Paper, Stack, Tab, Tabs, TextField, Typography, Alert } from '@mui/material';
import type { ColumnDef } from '@tanstack/react-table';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link as RouterLink } from 'react-router-dom';
import { useWork, type WorkAction } from '../work';
import { DataTable } from '../components/DataTable';
import { SignDialog } from '../components/SignDialog';
import { fmtDate } from '../components/Status';
import { get, post } from '../api';
import { mono } from '../theme';

export function MyWork() {
  const work = useWork();
  const qc = useQueryClient();
  const [tab, setTab] = useState('approvals');
  const [signing, setSigning] = useState<{ id: string; number: string; step: string } | null>(null);
  const [completing, setCompleting] = useState<WorkAction | null>(null);
  const [notes, setNotes] = useState('');
  const [error, setError] = useState<string | null>(null);
  const w = work.data;
  const training = useQuery({ queryKey: ['mytraining'], queryFn: () => get<{ id: string; courseNumber: string; courseTitle: string; documentRevision: string | null; dueOn: string; status: number; reason: string; overdue: boolean }[]>('/api/training/my') });
  const openTraining = (training.data ?? []).filter((t) => t.status === 0);
  const actionCols: ColumnDef<WorkAction, any>[] = [
    { header: 'Number', accessorKey: 'number', cell: (c) => <span style={{ fontFamily: mono }}>{c.getValue()}</span> },
    { header: 'Action', accessorKey: 'title' },
    { header: 'Due', accessorKey: 'dueDate', cell: (c) => <Typography fontSize="inherit" color={c.row.original.isOverdue ? 'error' : undefined}>{fmtDate(c.getValue())}</Typography> },
    { header: '', id: 'done', cell: (c) => <Button size="small" onClick={() => { setCompleting(c.row.original); setNotes(''); setError(null); }}>Complete</Button> },
  ];
  return (
    <Box sx={{ maxWidth: 1240, mx: 'auto', p: 4 }}>
      <Box sx={{ mb: 1 }}><PageNav /></Box>
      <Typography variant="h1" sx={{ mb: 2 }}>My Work</Typography>
      <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ mb: 2 }}>
        <Tab value="approvals" label={`Approvals (${w?.approvals.length ?? 0})`} />
        <Tab value="assigned" label={`Assigned (${w?.assigned.length ?? 0})`} />
        <Tab value="overdue" label={`Overdue (${w?.overdue.length ?? 0})`} />
        <Tab value="training" label={`Training (${openTraining.length})`} />
        <Tab value="waiting" label={`Waiting on others (${w?.waitingOnOthers.length ?? 0})`} />
      </Tabs>
      {tab === 'approvals' && (
        <DataTable data={w?.approvals ?? []} empty="Nothing is waiting for your signature." columns={[
          { header: 'Record', accessorKey: 'recordNumber', cell: (c) => <span style={{ fontFamily: mono }}>{c.getValue()}</span> },
          { header: 'Type', accessorKey: 'recordType' },
          { header: 'Step', accessorKey: 'stepName' },
          { header: 'Submitted', accessorKey: 'submittedAt', cell: (c) => fmtDate(c.getValue()) },
          { header: '', id: 'sign', cell: (c) => <Button size="small" variant="contained" onClick={() => setSigning({ id: c.row.original.id, number: c.row.original.recordNumber, step: c.row.original.stepName })}>Review and sign</Button> },
        ]} />
      )}
      {tab === 'assigned' && <DataTable data={w?.assigned ?? []} columns={actionCols} empty="No open actions." />}
      {tab === 'overdue' && <DataTable data={w?.overdue ?? []} columns={actionCols} empty="Nothing overdue." />}
      {tab === 'training' && (
        <DataTable data={openTraining} empty="No training waiting for you." columns={[
          { header: 'Course', accessorKey: 'courseNumber', cell: (c) => <span style={{ fontFamily: mono }}>{c.getValue()}</span> },
          { header: 'Title', accessorKey: 'courseTitle' }, { header: 'Reason', accessorKey: 'reason' },
          { header: 'Due', accessorKey: 'dueOn', cell: (c) => <Typography fontSize="inherit" color={c.row.original.overdue ? 'error' : undefined}>{fmtDate(c.getValue())}</Typography> },
          { header: '', id: 'go', cell: () => <Button size="small" variant="contained" component={RouterLink} to="/training">Sign off</Button> },
        ]} />
      )}
      {tab === 'waiting' && (
        <DataTable data={w?.waitingOnOthers ?? []} empty="Nothing waiting." columns={[
          { header: 'Record', accessorKey: 'recordNumber' }, { header: 'Type', accessorKey: 'recordType' },
          { header: 'Submitted', accessorKey: 'submittedAt', cell: (c) => fmtDate(c.getValue()) },
        ]} />
      )}
      {signing && <SignDialog approvalId={signing.id} recordNumber={signing.number} stepName={signing.step} onClose={() => setSigning(null)} />}
      {completing && (
        <Paper sx={{ p: 2, mt: 2, maxWidth: 640 }}>
          <Typography variant="h3">Complete {completing.number}</Typography>
          {error && <Alert severity="error" sx={{ mt: 1 }}>{error}</Alert>}
          <TextField fullWidth multiline minRows={3} label="What was done" sx={{ my: 1.5 }} value={notes} onChange={(e) => setNotes(e.target.value)} />
          <Stack direction="row" gap={1}>
            <Button variant="contained" disabled={!notes.trim()} onClick={async () => {
              try { await post(`/api/actions/${completing.id}/complete`, { notes }); setCompleting(null); qc.invalidateQueries({ queryKey: ['work'] }); }
              catch (e) { setError((e as Error).message); }
            }}>Mark complete</Button>
            <Button onClick={() => setCompleting(null)}>Cancel</Button>
          </Stack>
        </Paper>
      )}
    </Box>
  );
}
