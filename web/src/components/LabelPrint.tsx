import { useState } from 'react';
import { Alert, Box, Button, Chip, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { get, post } from '../api';
import { useAuth } from '../auth';
import { fmtDateTime } from './Status';
import { StepDialog } from './Kit';
import { mono } from '../theme';

export const labelTypes = ['Receiving', 'Inventory', 'Bin', 'Lot', 'Serial', 'Carton', 'Pallet', 'Shipping', 'Hold', 'Quarantine', 'Scrap'];
export const printerDrivers = ['Virtual (test)', 'Network printer (raw TCP, port 9100)', 'Print relay (HTTPS)', 'PDF download only'];
export const printerLanguages = ['ZPL', 'PDF'];
export const printerStatus = ['Not checked', 'Online', 'Offline', 'Error'];
export const printModes = ['Print immediately', 'Hold in queue'];
export const jobStatus = ['Pending', 'Printing', 'Printed', 'Failed', 'Cancelled', 'PDF download', 'Held in queue'];
export const jobTriggers = ['Automatic', 'Manual', 'Reprint'];

export interface PrinterRow { id: string; code: string; name: string; driver: number; address?: string; language: number; dpi: number; widthMm: number; heightMm: number; enabled: boolean;
  status: number; lastCheckedAt?: string; lastError?: string; hasSecret: boolean; siteId: string; siteName: string; pending: number; failed: number }
export interface PrintJob { id: string; labelType: number; trigger: number; refType: string; refId: string; refNumber: string; title: string; barcode: string; printerId?: string; copies: number; status: number;
  attempts: number; nextAttemptAt?: string; lastError?: string; requestedAt: string; printedAt?: string; workstation?: string; reprintOfId?: string; reprintReason?: string; siteId: string }
export interface JobRow { job: PrintJob; printer?: string; requestedBy: string }
export interface PrintOutcome { jobId: string; labelType: number; title: string; status: number; printer?: string; error?: string }

const jobColor = (s: number): 'default' | 'success' | 'warning' | 'error' | 'info' => (s === 2 ? 'success' : s === 3 ? 'error' : s === 6 || s === 0 || s === 1 ? 'warning' : s === 5 ? 'info' : 'default');
export const JobChip = ({ s }: { s: number }) => <Chip size="small" variant="outlined" color={jobColor(s)} label={jobStatus[s] ?? s} data-testid="label-job-status" />;
export const printerColor = (s: number): 'default' | 'success' | 'error' | 'warning' => (s === 1 ? 'success' : s === 2 ? 'error' : s === 3 ? 'warning' : 'default');

/** One line per outcome: printed, held, failed (with the reason) or a PDF to download. */
export function outcomeText(o: PrintOutcome[]) {
  return o.map((x) => x.status === 2 ? `${x.title}: printed on ${x.printer}`
    : x.status === 6 ? `${x.title}: held in the ${x.printer} queue`
    : x.status === 5 ? `${x.title}: no printer applies — download the PDF`
    : x.status === 3 ? `${x.title}: not printed${x.printer ? ` on ${x.printer}` : ''} — ${x.error ?? 'failed'}. It will be retried; you can also retry or download the PDF.`
    : `${x.title}: ${jobStatus[x.status]}`).join(' · ');
}

export const usePrinters = (siteId?: string) => useQuery({ queryKey: ['label-printers', siteId ?? ''], queryFn: () => get<PrinterRow[]>(`/api/labels/printers${siteId ? `?siteId=${siteId}` : ''}`) });

/** Actions on one print job: send/retry, reprint with a reason, PDF, cancel. */
export function JobActions({ j, onDone, onMessage }: { j: JobRow; onDone: () => void; onMessage: (m: { ok: boolean; text: string }) => void }) {
  const { can } = useAuth();
  const [reprint, setReprint] = useState(false);
  const printers = usePrinters(j.job.siteId);
  const operate = can('inventory.edit') || can('shipping.edit');
  const s = j.job.status;
  const run = async (f: () => Promise<unknown>) => { try { const r = await f(); if (r && typeof r === 'object' && 'status' in (r as object)) onMessage({ ok: (r as PrintOutcome).status === 2 || (r as PrintOutcome).status === 6, text: outcomeText([r as PrintOutcome]) }); onDone(); } catch (e) { onMessage({ ok: false, text: (e as Error).message }); } };
  return <Stack direction="row" gap={0.5} justifyContent="flex-end" flexWrap="wrap">
    {operate && (s === 3 || s === 6 || s === 0) && j.job.printerId && <Button size="small" onClick={() => run(() => post(`/api/labels/jobs/${j.job.id}/retry`))}>{s === 6 ? 'Release' : s === 3 ? 'Retry' : 'Send now'}</Button>}
    {operate && s !== 4 && <Button size="small" onClick={() => setReprint(true)}>Reprint</Button>}
    <Button size="small" href={`/api/labels/jobs/${j.job.id}/pdf`} target="_blank">PDF</Button>
    {operate && (s === 0 || s === 3 || s === 5 || s === 6) && <Button size="small" color="inherit" onClick={() => run(() => post(`/api/labels/jobs/${j.job.id}/cancel`))}>Cancel</Button>}
    <StepDialog open={reprint} title={`Reprint ${j.job.title}`} confirm="Reprint" onClose={() => setReprint(false)}
      intro="The reprint carries exactly the content of the original label. The reason is recorded in the audit trail."
      inputs={[{ name: 'reason', label: 'Reason for the reprint' },
        { name: 'printer', label: 'Printer', type: 'select', initial: j.job.printerId ?? '', options: [['', 'PDF download'], ...(printers.data ?? []).filter((p) => p.enabled).map((p) => [p.id, `${p.code} — ${p.name}`] as [string, string])] },
        { name: 'copies', label: 'Copies', initial: String(j.job.copies) }]}
      onConfirm={async (v) => {
        if ((v.reason ?? '').trim().length < 3) throw new Error('Give the reason for the reprint.');
        const r = await post<PrintOutcome>(`/api/labels/jobs/${j.job.id}/reprint`, { reason: v.reason, printerId: v.printer || null, copies: v.copies ? Number(v.copies) : null });
        onMessage({ ok: r.status === 2 || r.status === 6 || r.status === 5, text: outcomeText([r]) }); onDone();
      }} />
  </Stack>;
}

/**
 * Labels of one record: print buttons for the label types that apply, and every print job of the record
 * (including the labels its transaction produced) with status, printer, attempts and the last error.
 */
export function LabelPanel({ refType, refId, siteId, types, title = 'Labels' }: { refType: string; refId: string; siteId?: string; types: number[]; title?: string }) {
  const qc = useQueryClient();
  const { can } = useAuth();
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const [dlg, setDlg] = useState<number | null>(null);
  const jobs = useQuery({ queryKey: ['label-jobs', refType, refId], queryFn: () => get<{ items: JobRow[]; total: number }>(`/api/labels/jobs?refType=${encodeURIComponent(refType)}&refId=${refId}&pageSize=100`) });
  const printers = usePrinters(siteId);
  const operate = can('inventory.edit') || can('shipping.edit');
  const refresh = () => qc.invalidateQueries({ queryKey: ['label-jobs'] });
  const rows = jobs.data?.items ?? [];
  return <Paper variant="outlined" sx={{ p: 2 }} data-testid="label-panel">
    <Stack direction="row" justifyContent="space-between" alignItems="center" gap={1} flexWrap="wrap" sx={{ mb: 1 }}>
      <Typography variant="subtitle2">{title}</Typography>
      <Stack direction="row" gap={1} flexWrap="wrap">
        {types.map((t) => <Button key={t} size="small" variant="outlined" href={`/api/labels/preview?labelType=${labelTypes[t]}&refType=${encodeURIComponent(refType)}&refId=${refId}`} target="_blank">Preview {labelTypes[t].toLowerCase()} label</Button>)}
        {operate && types.map((t) => <Button key={`p${t}`} size="small" variant="contained" onClick={() => setDlg(t)}>Print {labelTypes[t].toLowerCase()} label</Button>)}
      </Stack>
    </Stack>
    {msg && <Alert severity={msg.ok ? 'success' : 'warning'} sx={{ mb: 1 }} onClose={() => setMsg(null)} data-testid="label-message">{msg.text}</Alert>}
    {jobs.error && <Alert severity="error">{(jobs.error as Error).message}</Alert>}
    {rows.length === 0 ? <Typography variant="body2" color="text.secondary">No labels printed for this record yet.</Typography>
      : <Table size="small"><TableHead><TableRow><TableCell>Label</TableCell><TableCell>Status</TableCell><TableCell>Printer</TableCell><TableCell>Requested</TableCell><TableCell /></TableRow></TableHead>
        <TableBody>{rows.map((j) => <TableRow key={j.job.id} data-testid="label-job">
          <TableCell><Typography variant="body2">{j.job.title}</Typography><Typography variant="caption" color="text.secondary" sx={{ fontFamily: mono }}>{j.job.barcode}</Typography>
            {j.job.reprintReason && <Typography variant="caption" display="block" color="text.secondary">Reprint: {j.job.reprintReason}</Typography>}</TableCell>
          <TableCell><JobChip s={j.job.status} />{j.job.lastError && j.job.status !== 2 && <Typography variant="caption" display="block" color="error">{j.job.lastError}</Typography>}
            {j.job.status === 3 && j.job.nextAttemptAt && <Typography variant="caption" display="block" color="text.secondary">Next try {fmtDateTime(j.job.nextAttemptAt)}</Typography>}</TableCell>
          <TableCell>{j.printer ?? '—'}{j.job.copies > 1 ? ` · ${j.job.copies} copies` : ''}</TableCell>
          <TableCell><Typography variant="body2">{fmtDateTime(j.job.requestedAt)}</Typography><Typography variant="caption" color="text.secondary">{jobTriggers[j.job.trigger]} · {j.requestedBy}</Typography></TableCell>
          <TableCell align="right"><JobActions j={j} onDone={refresh} onMessage={setMsg} /></TableCell>
        </TableRow>)}</TableBody></Table>}
    <StepDialog open={dlg !== null} title={`Print ${dlg !== null ? labelTypes[dlg].toLowerCase() : ''} label`} confirm="Print" onClose={() => setDlg(null)}
      intro="Leave the printer on automatic to use the printer routed for this label, workstation and location. With no printer the label is prepared as a PDF to download."
      inputs={[{ name: 'printer', label: 'Printer', type: 'select', initial: '', options: [['', 'Automatic (print routing)'], ...(printers.data ?? []).filter((p) => p.enabled).map((p) => [p.id, `${p.code} — ${p.name}`] as [string, string])] },
        { name: 'copies', label: 'Copies (blank = as routed)', initial: '' }]}
      onConfirm={async (v) => {
        const r = await post<PrintOutcome[]>('/api/labels/print', { labelType: dlg, refType, refId, printerId: v.printer || null, copies: v.copies ? Number(v.copies) : null });
        setMsg({ ok: r.every((x) => x.status === 2 || x.status === 6 || x.status === 5), text: outcomeText(r) }); refresh();
      }} />
  </Paper>;
}

/** Compact one-line label status for a record (used next to scan-station results). */
export function LabelStatus({ refType, refId }: { refType: string; refId: string }) {
  const jobs = useQuery({ queryKey: ['label-jobs', refType, refId], queryFn: () => get<{ items: JobRow[]; total: number }>(`/api/labels/jobs?refType=${encodeURIComponent(refType)}&refId=${refId}&pageSize=20`), retry: false });
  const rows = jobs.data?.items ?? [];
  if (!rows.length) return null;
  return <Box data-testid="label-status" sx={{ display: 'flex', gap: 0.75, flexWrap: 'wrap', alignItems: 'center' }}>
    <Typography variant="caption" color="text.secondary">Labels:</Typography>
    {rows.map((j) => <Chip key={j.job.id} size="small" variant="outlined" color={jobColor(j.job.status)} label={`${labelTypes[j.job.labelType]} · ${jobStatus[j.job.status]}${j.printer ? ` · ${j.printer}` : ''}`}
      component="a" clickable href={`/api/labels/jobs/${j.job.id}/pdf`} target="_blank" />)}
  </Box>;
}

