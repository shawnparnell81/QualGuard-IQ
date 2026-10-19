import { setupOptions } from '../components/SetupLink';
import { useState } from 'react';
import { Alert, Button, Chip, MenuItem, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { get, getWorkstation, post, put, setWorkstation } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { fmtDateTime } from '../components/Status';
import { Section, StepDialog, useSites, useUsers } from '../components/Kit';
import { JobActions, JobChip, jobStatus, jobTriggers, labelTypes, outcomeText, printerColor, printerDrivers, printerLanguages, printerStatus, printModes, usePrinters, type JobRow, type PrintOutcome, type PrinterRow } from '../components/LabelPrint';
import { useLocations } from '../warehouse/common';
import { SitePicker } from './Warehouse';
import { mono } from '../theme';

const mod = modules.find((m) => m.key === 'warehouse')!;
interface Route { id: string; labelType: number; siteId?: string; locationId?: string; workstation?: string; userId?: string; printerId: string; copies: number; autoPrint: boolean; mode: number; active: boolean }
interface RouteRow { route: Route; printer: string; site?: string; location?: string; user?: string }

/** Label printers, print routing and the print queue of a site. */
export function LabelPrintingPage() {
  const { can } = useAuth();
  const qc = useQueryClient();
  const sites = useSites();
  const [tab, setTab] = useState('queue');
  const [site, setSite] = useState('');
  const siteId = site || sites.data?.find((s) => !s.isArchived)?.id || '';
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const manage = can('inventory.approve');
  const refresh = () => { qc.invalidateQueries({ queryKey: ['label-printers'] }); qc.invalidateQueries({ queryKey: ['label-routes'] }); qc.invalidateQueries({ queryKey: ['label-jobs'] }); qc.invalidateQueries({ queryKey: ['label-queue'] }); };
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Label printing' }]} title="Label printing"
      tabs={[{ label: 'Print queue', value: 'queue' }, { label: 'Printers', value: 'printers' }, { label: 'Routing', value: 'routes' }, { label: 'This workstation', value: 'ws' }]} tab={tab} onTab={setTab}
      actions={<SitePicker value={siteId} onChange={setSite} />}>
      {msg && <Alert severity={msg.ok ? 'success' : 'warning'} sx={{ mb: 2 }} onClose={() => setMsg(null)} data-testid="labels-message">{msg.text}</Alert>}
      {tab === 'queue' && siteId && <QueueTab siteId={siteId} onMessage={setMsg} refresh={refresh} />}
      {tab === 'printers' && siteId && <PrintersTab siteId={siteId} manage={manage} onMessage={setMsg} refresh={refresh} />}
      {tab === 'routes' && siteId && <RoutesTab siteId={siteId} manage={manage} refresh={refresh} />}
      {tab === 'ws' && <WorkstationTab />}
    </ModuleWorkspace>
  );
}

function QueueTab({ siteId, onMessage, refresh }: { siteId: string; onMessage: (m: { ok: boolean; text: string }) => void; refresh: () => void }) {
  const [status, setStatus] = useState('');
  const [printer, setPrinter] = useState('');
  const [page, setPage] = useState(1);
  const printers = usePrinters(siteId);
  const size = 50;
  const jobs = useQuery({ queryKey: ['label-queue', siteId, status, printer, page], refetchInterval: 15000,
    queryFn: () => get<{ items: JobRow[]; total: number }>(`/api/labels/jobs?siteId=${siteId}${status ? `&status=${status}` : ''}${printer ? `&printerId=${printer}` : ''}&page=${page}&pageSize=${size}`) });
  const rows = jobs.data?.items ?? [];
  const total = jobs.data?.total ?? 0;
  return <Stack gap={2}>
    <Stack direction="row" gap={2} flexWrap="wrap">
      <TextField select size="small" label="Status" value={status} onChange={(e) => { setStatus(e.target.value); setPage(1); }} sx={{ minWidth: 180 }} data-testid="queue-status">
        <MenuItem value="">All</MenuItem>{jobStatus.map((s, i) => <MenuItem key={s} value={String(i)}>{s}</MenuItem>)}</TextField>
      <TextField select size="small" label="Printer" value={printer} onChange={(e) => { setPrinter(e.target.value); setPage(1); }} sx={{ minWidth: 220 }}>
        <MenuItem value="">All printers</MenuItem>{(printers.data ?? []).map((p) => <MenuItem key={p.id} value={p.id}>{p.code} — {p.name}</MenuItem>)}</TextField>
    </Stack>
    {jobs.error && <Alert severity="error">{(jobs.error as Error).message}</Alert>}
    <Paper variant="outlined">
      <Table size="small">
        <TableHead><TableRow><TableCell>Label</TableCell><TableCell>Record</TableCell><TableCell>Status</TableCell><TableCell>Printer</TableCell><TableCell>Attempts</TableCell><TableCell>Requested</TableCell><TableCell /></TableRow></TableHead>
        <TableBody>
          {rows.map((j) => <TableRow key={j.job.id} data-testid="queue-job">
            <TableCell><Typography variant="body2">{labelTypes[j.job.labelType]}</Typography><Typography variant="caption" color="text.secondary">{jobTriggers[j.job.trigger]}{j.job.reprintReason ? ` — ${j.job.reprintReason}` : ''}</Typography></TableCell>
            <TableCell><Typography variant="body2" sx={{ fontFamily: mono }}>{j.job.refNumber}</Typography><Typography variant="caption" color="text.secondary">{j.job.title}</Typography></TableCell>
            <TableCell><JobChip s={j.job.status} />{j.job.lastError && j.job.status !== 2 && <Typography variant="caption" display="block" color="error">{j.job.lastError}</Typography>}
              {j.job.status === 3 && j.job.nextAttemptAt && <Typography variant="caption" display="block" color="text.secondary">Next try {fmtDateTime(j.job.nextAttemptAt)}</Typography>}</TableCell>
            <TableCell>{j.printer ?? 'PDF'}{j.job.copies > 1 ? ` · ${j.job.copies}×` : ''}</TableCell>
            <TableCell>{j.job.attempts}</TableCell>
            <TableCell><Typography variant="body2">{fmtDateTime(j.job.requestedAt)}</Typography><Typography variant="caption" color="text.secondary">{j.requestedBy}{j.job.workstation ? ` · ${j.job.workstation}` : ''}</Typography></TableCell>
            <TableCell align="right"><JobActions j={j} onDone={refresh} onMessage={onMessage} /></TableCell>
          </TableRow>)}
          {rows.length === 0 && <TableRow><TableCell colSpan={7}><Typography variant="body2" color="text.secondary">No print jobs.</Typography></TableCell></TableRow>}
        </TableBody>
      </Table>
    </Paper>
    {total > size && <Stack direction="row" gap={1} alignItems="center"><Button size="small" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</Button>
      <Typography variant="body2">Page {page} of {Math.ceil(total / size)} · {total} jobs</Typography><Button size="small" disabled={page * size >= total} onClick={() => setPage(page + 1)}>Next</Button></Stack>}
    <Typography variant="caption" color="text.secondary">Failed jobs are retried in the background after 1, 2, 5 and 15 minutes (five attempts in total). Held jobs wait until someone releases them. A disabled printer is not retried.</Typography>
  </Stack>;
}

function PrintersTab({ siteId, manage, onMessage, refresh }: { siteId: string; manage: boolean; onMessage: (m: { ok: boolean; text: string }) => void; refresh: () => void }) {
  const printers = usePrinters(siteId);
  const [edit, setEdit] = useState<PrinterRow | 'new' | null>(null);
  const e = edit === 'new' ? null : edit;
  const test = async (p: PrinterRow) => { try { const r = await post<PrintOutcome>(`/api/labels/printers/${p.id}/test`); onMessage({ ok: r.status === 2, text: outcomeText([r]) }); } catch (x) { onMessage({ ok: false, text: (x as Error).message }); } refresh(); };
  return <Section title="Printers" action={manage && <Button size="small" variant="contained" onClick={() => setEdit('new')}>Add printer</Button>}>
    <Paper variant="outlined">
      <Table size="small">
        <TableHead><TableRow><TableCell>Printer</TableCell><TableCell>Connection</TableCell><TableCell>Stock</TableCell><TableCell>Status</TableCell><TableCell>Queue</TableCell><TableCell /></TableRow></TableHead>
        <TableBody>
          {(printers.data ?? []).map((p) => <TableRow key={p.id} data-testid="printer-row">
            <TableCell><Typography variant="body2" sx={{ fontFamily: mono, fontWeight: 600 }}>{p.code}</Typography><Typography variant="caption" color="text.secondary">{p.name}</Typography></TableCell>
            <TableCell><Typography variant="body2">{printerDrivers[p.driver]}</Typography><Typography variant="caption" color="text.secondary" sx={{ fontFamily: mono }}>{p.address ?? ''}{p.hasSecret ? ' · token set' : ''}</Typography></TableCell>
            <TableCell>{printerLanguages[p.language]} · {p.widthMm} × {p.heightMm} mm · {p.dpi} dpi</TableCell>
            <TableCell>{p.enabled ? <Chip size="small" variant="outlined" color={printerColor(p.status)} label={printerStatus[p.status]} data-testid="printer-status" /> : <Chip size="small" label="Disabled" />}
              {p.lastCheckedAt && <Typography variant="caption" display="block" color="text.secondary">{fmtDateTime(p.lastCheckedAt)}</Typography>}
              {p.lastError && <Typography variant="caption" display="block" color="error">{p.lastError}</Typography>}</TableCell>
            <TableCell>{p.pending} waiting{p.failed ? ` · ${p.failed} failed` : ''}</TableCell>
            <TableCell align="right">{manage && <><Button size="small" onClick={() => test(p)}>Print test label</Button><Button size="small" onClick={() => setEdit(p)}>Edit</Button></>}</TableCell>
          </TableRow>)}
          {(printers.data ?? []).length === 0 && <TableRow><TableCell colSpan={6}><Typography variant="body2" color="text.secondary">No printers at this site. Without a printer, labels are prepared as PDFs to download and print.</Typography></TableCell></TableRow>}
        </TableBody>
      </Table>
    </Paper>
    <StepDialog open={edit !== null} title={e ? `Edit ${e.code}` : 'Add printer'} confirm="Save" onClose={() => setEdit(null)}
      intro="Network printers take ZPL on raw TCP (host or host:port, port 9100 by default). A print relay is an HTTPS endpoint that accepts the label and forwards it to a printer on the local network; its token is stored encrypted. PDF download only prepares labels to print by hand."
      inputs={[
        { name: 'code', label: 'Code', initial: e?.code ?? '' }, { name: 'name', label: 'Name', initial: e?.name ?? '' },
        { name: 'driver', label: 'Connection', type: 'select', initial: String(e?.driver ?? 1), options: printerDrivers.map((d, i) => [i, d]) },
        { name: 'address', label: 'Address (host:port or https URL)', initial: e?.address ?? '' },
        { name: 'language', label: 'Label language', type: 'select', initial: String(e?.language ?? 0), options: printerLanguages.map((d, i) => [i, d]) },
        { name: 'dpi', label: 'Resolution', type: 'select', initial: String(e?.dpi ?? 203), options: [[152, '152 dpi'], [203, '203 dpi'], [300, '300 dpi'], [600, '600 dpi']] },
        { name: 'w', label: 'Label width (mm)', initial: String(e?.widthMm ?? 102) }, { name: 'h', label: 'Label height (mm)', initial: String(e?.heightMm ?? 152) },
        { name: 'enabled', label: 'Enabled', type: 'checkbox', initial: String(e?.enabled ?? true) },
        { name: 'secret', label: e?.hasSecret ? 'Relay token (blank = keep the current token)' : 'Relay token (print relay only)', type: 'password' },
        ...(e?.hasSecret ? [{ name: 'clear', label: 'Remove the stored token', type: 'checkbox' as const, initial: 'false' }] : []),
      ]}
      onConfirm={async (v) => {
        const body = { code: v.code, name: v.name, driver: Number(v.driver), address: v.address || null, language: Number(v.language), dpi: Number(v.dpi), widthMm: Number(v.w), heightMm: Number(v.h),
          enabled: v.enabled === 'true', siteId: e?.siteId ?? siteId, secret: v.secret || null, clearSecret: v.clear === 'true' };
        if (e) await put(`/api/labels/printers/${e.id}`, body); else await post('/api/labels/printers', body);
        refresh();
      }} />
  </Section>;
}

function RoutesTab({ siteId, manage, refresh }: { siteId: string; manage: boolean; refresh: () => void }) {
  const routes = useQuery({ queryKey: ['label-routes'], queryFn: () => get<RouteRow[]>('/api/labels/routes') });
  const printers = usePrinters(siteId);
  const locs = useLocations(siteId);
  const users = useUsers();
  const [edit, setEdit] = useState<Route | 'new' | null>(null);
  const [check, setCheck] = useState({ type: '3', location: '', user: '' });
  const resolved = useQuery({ queryKey: ['label-resolve', siteId, check], enabled: !!siteId,
    queryFn: () => get<{ printer?: string; explanation: string }>(`/api/labels/routes/resolve?labelType=${labelTypes[Number(check.type)]}&siteId=${siteId}${check.location ? `&locationId=${check.location}` : ''}${check.user ? `&userId=${check.user}` : ''}`) });
  const e = edit === 'new' ? null : edit;
  const rows = (routes.data ?? []).filter((r) => r.route.siteId === siteId);
  const zones = (locs.data ?? []).filter((l) => l.active);
  return <Stack gap={2}>
    <Section title="Print routing" action={manage && <Button size="small" variant="contained" onClick={() => setEdit('new')}>Add route</Button>}>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>For each label, the most specific active route wins: a user's own route, then a workstation, then the deepest warehouse or zone, then the site default. Labels with no route are prepared as PDFs.</Typography>
      <Paper variant="outlined">
        <Table size="small">
          <TableHead><TableRow><TableCell>Label</TableCell><TableCell>Applies to</TableCell><TableCell>Printer</TableCell><TableCell>Copies</TableCell><TableCell>Automatic</TableCell><TableCell>Mode</TableCell><TableCell /></TableRow></TableHead>
          <TableBody>
            {rows.map((r) => <TableRow key={r.route.id} data-testid="route-row" sx={{ opacity: r.route.active ? 1 : 0.5 }}>
              <TableCell>{labelTypes[r.route.labelType]}</TableCell>
              <TableCell>{[r.user && `User ${r.user}`, r.route.workstation && `Workstation ${r.route.workstation}`, r.location && `Location ${r.location}`].filter(Boolean).join(' · ') || 'Whole site'}{!r.route.active && ' (inactive)'}</TableCell>
              <TableCell>{r.printer}</TableCell><TableCell>{r.route.copies}</TableCell><TableCell>{r.route.autoPrint ? 'Yes' : 'Manual only'}</TableCell><TableCell>{printModes[r.route.mode]}</TableCell>
              <TableCell align="right">{manage && <Button size="small" onClick={() => setEdit(r.route)}>Edit</Button>}</TableCell>
            </TableRow>)}
            {rows.length === 0 && <TableRow><TableCell colSpan={7}><Typography variant="body2" color="text.secondary">No routes at this site.</Typography></TableCell></TableRow>}
          </TableBody>
        </Table>
      </Paper>
    </Section>
    <Section title="Which printer would be used?">
      <Stack direction={{ xs: 'column', md: 'row' }} gap={2}>
        <TextField select size="small" label="Label" value={check.type} onChange={(x) => setCheck({ ...check, type: x.target.value })} sx={{ minWidth: 160 }}>{labelTypes.map((t, i) => <MenuItem key={t} value={String(i)}>{t}</MenuItem>)}</TextField>
        <TextField select size="small" label="Location" value={check.location} onChange={(x) => setCheck({ ...check, location: x.target.value })} sx={{ minWidth: 220 }}>
          <MenuItem value="">Any</MenuItem>{zones.map((l) => <MenuItem key={l.id} value={l.id}>{l.path ?? l.code}</MenuItem>)}</TextField>
        <TextField select size="small" label="User" value={check.user} onChange={(x) => setCheck({ ...check, user: x.target.value })} sx={{ minWidth: 200 }}>
          <MenuItem value="">Anyone</MenuItem>{(users.data ?? []).map((u) => <MenuItem key={u.id} value={u.id}>{u.displayName}</MenuItem>)}{setupOptions('user', users.data)}</TextField>
      </Stack>
      <Typography variant="body2" sx={{ mt: 1 }} data-testid="route-explanation">{resolved.data?.explanation}{getWorkstation() ? ` (this workstation: ${getWorkstation()})` : ''}</Typography>
    </Section>
    <StepDialog open={edit !== null} title={e ? `Edit ${labelTypes[e.labelType].toLowerCase()} route` : 'Add route'} confirm="Save" onClose={() => setEdit(null)}
      intro="Automatic routes print when the transaction completes (receipt, putaway, pack, ship, hold, quarantine, scrap). Hold in queue keeps automatic labels until someone releases them from the print queue."
      inputs={[
        { name: 'type', label: 'Label', type: 'select', initial: String(e?.labelType ?? 3), options: labelTypes.map((t, i) => [i, t]) },
        { name: 'printer', label: 'Printer', type: 'select', initial: e?.printerId ?? '', options: (printers.data ?? []).map((p) => [p.id, `${p.code} — ${p.name}`]) },
        { name: 'location', label: 'Warehouse, zone or bin (blank = whole site)', type: 'select', initial: e?.locationId ?? '', options: [['', 'Whole site'], ...zones.map((l) => [l.id, l.path ?? l.code] as [string, string])] },
        { name: 'ws', label: 'Workstation (blank = any)', initial: e?.workstation ?? '' },
        { name: 'user', label: 'User (blank = anyone)', type: 'select', setup: 'user', initial: e?.userId ?? '', options: [['', 'Anyone'], ...(users.data ?? []).map((u) => [u.id, u.displayName] as [string, string])] },
        { name: 'copies', label: 'Copies', initial: String(e?.copies ?? 1) },
        { name: 'auto', label: 'Print automatically', type: 'checkbox', initial: String(e?.autoPrint ?? true) },
        { name: 'mode', label: 'Mode', type: 'select', initial: String(e?.mode ?? 0), options: printModes.map((m, i) => [i, m]) },
        { name: 'active', label: 'Active', type: 'checkbox', initial: String(e?.active ?? true) },
      ]}
      onConfirm={async (v) => {
        if (!v.printer) throw new Error('Select a printer.');
        const body = { labelType: Number(v.type), siteId, locationId: v.location || null, workstation: v.ws || null, userId: v.user || null, printerId: v.printer, copies: Number(v.copies || 1), autoPrint: v.auto === 'true', mode: Number(v.mode), active: v.active === 'true' };
        if (e) await put(`/api/labels/routes/${e.id}`, body); else await post('/api/labels/routes', body);
        refresh();
      }} />
  </Stack>;
}

function WorkstationTab() {
  const [name, setName] = useState(getWorkstation());
  const [saved, setSaved] = useState(false);
  return <Section title="This workstation">
    <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>Give this device the workstation name used in print routing (for example DOCK-3). The name is kept on this device and sent with every request so labels go to the printer next to it.</Typography>
    <Stack direction="row" gap={1} alignItems="center">
      <TextField size="small" label="Workstation name" value={name} onChange={(x) => { setName(x.target.value); setSaved(false); }} inputProps={{ maxLength: 100 }} />
      <Button variant="contained" onClick={() => { setWorkstation(name); setName(getWorkstation()); setSaved(true); }}>Save</Button>
      {saved && <Typography variant="body2" color="success.main">Saved{getWorkstation() ? '' : ' — no workstation name'}</Typography>}
    </Stack>
  </Section>;
}
