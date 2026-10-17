import { useRef, useState } from 'react';
import { Alert, Box, Button, Chip, Container, Paper, Stack, Tab, Table, TableBody, TableCell, TableHead, TableRow, Tabs, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useLocation, useNavigate } from 'react-router-dom';
import { get, post } from '../api';
import { useAuth } from '../auth';
import { fmtDate, fmtDateTime } from '../components/Status';
import { mono } from '../theme';

/**
 * External portal for customer and supplier contacts. It has its own slim shell and talks only to /api/portal;
 * the server refuses every other API for portal accounts.
 */
interface Me { displayName: string; kind: 'Customer' | 'Supplier'; partyName: string }
type Row = { id: string; number: string; title: string; status: string; [k: string]: unknown };
const pathOf: Record<string, string> = { complaints: 'complaints', ncrs: 'ncrs', scars: 'scars' };

export function PortalApp() {
  const { logout } = useAuth();
  const nav = useNavigate();
  const loc = useLocation();
  const me = useQuery({ queryKey: ['portal-me'], queryFn: () => get<Me>('/api/portal/me') });
  const supplier = me.data?.kind === 'Supplier';
  const tabs = supplier ? [['scars', 'Corrective actions'], ['ncrs', 'Nonconformances'], ['scorecard', 'Scorecard'], ['audits', 'Audits']] : [['complaints', 'Complaints'], ['report', 'Quality report']];
  // Every portal page has its own address (/portal/<area> and /portal/<area>/<record id>) so links can be bookmarked and shared;
  // the server decides what each account may see, and this shell only offers the areas of the account's portal kind.
  const [, root, area, recordId] = loc.pathname.split('/');
  const requested = root === 'portal' ? area : undefined;
  const t = requested ?? tabs[0][0];
  const known = tabs.some(([v]) => v === t);
  const signOut = async () => { await logout(); nav('/', { replace: true }); };
  return (
    <Box sx={{ minHeight: '100vh', bgcolor: '#F4F2ED' }}>
      <Box component="header" sx={{ bgcolor: '#26323C', color: '#fff', px: 3, py: 1.25, display: 'flex', alignItems: 'center', gap: 2 }}>
        <Typography sx={{ fontWeight: 700, letterSpacing: 0.3 }}>{me.data?.kind ?? ''} portal</Typography>
        <Typography sx={{ opacity: 0.75, flex: 1 }} data-testid="portal-party">{me.data?.partyName}</Typography>
        <Typography variant="body2" sx={{ opacity: 0.8 }}>{me.data?.displayName}</Typography>
        <Button size="small" sx={{ color: '#fff' }} onClick={signOut}>Sign out</Button>
      </Box>
      <Container maxWidth="lg" sx={{ py: 3 }}>
        {me.error && <Alert severity="error">{(me.error as Error).message}</Alert>}
        {me.data && <>
          <Tabs value={known ? t : false} onChange={(_, v) => nav(`/portal/${v}`)} sx={{ mb: 2 }}>{tabs.map(([v, l]) => <Tab key={v} value={v} label={l} />)}</Tabs>
          {!known && <Alert severity="warning" data-testid="portal-unavailable">This page is not part of your {me.data.kind.toLowerCase()} portal.</Alert>}
          {known && pathOf[t] && (recordId ? <RecordView kind={t} id={recordId} onBack={() => nav(`/portal/${t}`)} /> : <RecordList kind={t} onOpen={(id) => nav(`/portal/${t}/${id}`)} />)}
          {known && t === 'scorecard' && <Scorecard />}
          {known && t === 'audits' && <Audits />}
          {known && t === 'report' && <QualityReport />}
        </>}
      </Container>
    </Box>
  );
}

function RecordList({ kind, onOpen }: { kind: string; onOpen: (id: string) => void }) {
  const q = useQuery({ queryKey: ['portal', kind], queryFn: () => get<Row[]>(`/api/portal/${kind}`) });
  return (
    <Paper variant="outlined">
      <Table size="small">
        <TableHead><TableRow><TableCell>Number</TableCell><TableCell>Title</TableCell><TableCell>Date</TableCell><TableCell>Response due</TableCell><TableCell>Status</TableCell></TableRow></TableHead>
        <TableBody>
          {(q.data ?? []).map((r) => <TableRow key={r.id} hover sx={{ cursor: 'pointer' }} onClick={() => onOpen(r.id)}>
            <TableCell sx={{ fontFamily: mono }}>{r.number}</TableCell><TableCell>{r.title}</TableCell>
            <TableCell>{fmtDate((r.reportedOn ?? r.detectedOn ?? r.issuedOn) as string)}</TableCell><TableCell>{fmtDate(r.responseDueOn as string)}</TableCell><TableCell><Chip size="small" label={r.status} /></TableCell>
          </TableRow>)}
          {q.data?.length === 0 && <TableRow><TableCell colSpan={5}><Typography color="text.secondary" sx={{ py: 2, textAlign: 'center' }}>Nothing here.</Typography></TableCell></TableRow>}
        </TableBody>
      </Table>
    </Paper>
  );
}

function RecordView({ kind, id, onBack }: { kind: string; id: string; onBack: () => void }) {
  const qc = useQueryClient();
  const q = useQuery({ queryKey: ['portal', kind, id], queryFn: () => get<Record<string, any>>(`/api/portal/${kind}/${id}`) });
  const [msg, setMsg] = useState('');
  const [resp, setResp] = useState({ containment: '', rootCause: '', correctiveActionPlan: '' });
  const [err, setErr] = useState<string | null>(null);
  const file = useRef<HTMLInputElement>(null);
  const refresh = () => qc.invalidateQueries({ queryKey: ['portal', kind, id] });
  const act = async (fn: () => Promise<unknown>) => { setErr(null); try { await fn(); refresh(); } catch (e) { setErr((e as Error).message); } };
  const r = q.data;
  if (q.error) return <Stack gap={2}><Button onClick={onBack} sx={{ alignSelf: 'flex-start' }}>← Back</Button>
    <Alert severity="warning" data-testid="portal-record-missing">This record was not found, or it is not shared with your organization.</Alert></Stack>;
  if (!r) return null;
  // The supplier may answer only while the SCAR is waiting for them (issued, or returned after a rejected answer).
  const canRespond = kind === 'scars' && (r.status === 'Issued');
  const facts: [string, unknown][] = [['Status', r.status], ['Part', r.partNumber], ['Lot', r.lotNumber], ['Quantity', r.quantityAffected], ['Your reference', r.customerReference], ['Response due', fmtDate(r.responseDueOn)]];
  return (
    <Stack gap={2}>
      <Button onClick={onBack} sx={{ alignSelf: 'flex-start' }}>← Back</Button>
      {err && <Alert severity="error">{err}</Alert>}
      <Paper variant="outlined" sx={{ p: 2 }}>
        <Typography variant="h6"><span style={{ fontFamily: mono }}>{r.number}</span> · {r.title}</Typography>
        <Stack direction="row" gap={3} flexWrap="wrap" sx={{ mt: 1 }}>{facts.filter(([, v]) => v != null && v !== '—').map(([k, v]) => <Box key={k}><Typography variant="caption" color="text.secondary">{k}</Typography><Typography>{String(v)}</Typography></Box>)}</Stack>
        {(r.description || r.problem) && <Typography sx={{ mt: 2, whiteSpace: 'pre-wrap' }}>{r.description ?? r.problem}</Typography>}
        {r.response && <Alert severity="info" sx={{ mt: 2 }}><b>Our response:</b> {r.response}{r.rootCause ? <><br /><b>Root cause:</b> {r.rootCause}</> : null}</Alert>}
        {r.correctiveActions?.length > 0 && <Box sx={{ mt: 1 }}>{r.correctiveActions.map((c: any) => <Typography key={c.number} variant="body2">{c.number} — {c.title} ({c.status})</Typography>)}</Box>}
        {r.reviewNotes && <Alert severity="warning" sx={{ mt: 2 }}>Your previous response was returned: {r.reviewNotes}</Alert>}
      </Paper>
      {kind === 'scars' && <Paper variant="outlined" sx={{ p: 2 }}>
        <Typography variant="subtitle1" sx={{ mb: 1 }}>Your response</Typography>
        {canRespond ? <Stack gap={1.5}>
          <TextField label="Containment" multiline minRows={2} value={resp.containment} onChange={(e) => setResp({ ...resp, containment: e.target.value })} />
          <TextField label="Root cause" multiline minRows={2} value={resp.rootCause} onChange={(e) => setResp({ ...resp, rootCause: e.target.value })} />
          <TextField label="Corrective action plan" multiline minRows={2} value={resp.correctiveActionPlan} onChange={(e) => setResp({ ...resp, correctiveActionPlan: e.target.value })} />
          <Button variant="contained" sx={{ alignSelf: 'flex-start' }} onClick={() => act(() => post(`/api/portal/scars/${id}/response`, resp))}>Submit response</Button>
        </Stack> : <Stack gap={1}>
          <Alert severity="info" data-testid="scar-response-locked">{r.responseReceivedOn ? 'Your response has been received and is with our team for review.' : 'This corrective action is not waiting for your response.'}</Alert>
          <Typography variant="body2"><b>Containment:</b> {r.supplierContainment ?? '—'}</Typography>
          <Typography variant="body2"><b>Root cause:</b> {r.supplierRootCause ?? '—'}</Typography>
          <Typography variant="body2"><b>Plan:</b> {r.correctiveActionPlan ?? '—'}</Typography>
          <Typography variant="caption" color="text.secondary">Received {fmtDate(r.responseReceivedOn)}</Typography></Stack>}
      </Paper>}
      <Paper variant="outlined" sx={{ p: 2 }}>
        <Typography variant="subtitle1" sx={{ mb: 1 }}>Messages</Typography>
        {(r.messages ?? []).map((m: any, i: number) => <Box key={i} sx={{ py: 1, borderBottom: 1, borderColor: 'divider' }}><Typography variant="caption" color="text.secondary">{m.author} · {fmtDateTime(m.createdAt)}</Typography><Typography sx={{ whiteSpace: 'pre-wrap' }}>{m.body}</Typography></Box>)}
        <Stack direction="row" gap={1} sx={{ mt: 1.5 }}><TextField size="small" fullWidth placeholder="Write a message" value={msg} onChange={(e) => setMsg(e.target.value)} />
          <Button variant="outlined" disabled={!msg.trim()} onClick={() => act(async () => { await post(`/api/portal/${kind}/${id}/messages`, { body: msg }); setMsg(''); })}>Send</Button></Stack>
      </Paper>
      <Paper variant="outlined" sx={{ p: 2 }}>
        <Stack direction="row" justifyContent="space-between" alignItems="center"><Typography variant="subtitle1">Evidence</Typography>
          <Button size="small" onClick={() => file.current?.click()}>Upload file</Button></Stack>
        <input ref={file} type="file" hidden data-testid="portal-evidence-file" onChange={(e) => { const f = e.target.files?.[0]; if (f) { const fd = new FormData(); fd.append('file', f); act(() => post(`/api/portal/${kind}/${id}/evidence`, fd)); } e.target.value = ''; }} />
        {(r.evidence ?? []).map((f: any) => <Typography key={f.id} variant="body2" data-testid="portal-evidence"><a href={`/api/portal/evidence/${f.id}`}>{f.fileName}</a> · {(f.sizeBytes / 1024).toFixed(0)} KB · {fmtDateTime(f.uploadedAt)}</Typography>)}
        {(r.evidence ?? []).length === 0 && <Typography variant="body2" color="text.secondary">No files yet.</Typography>}
      </Paper>
    </Stack>
  );
}

function Scorecard() {
  const q = useQuery({ queryKey: ['portal-scorecard'], queryFn: () => get<{ scorecard?: Record<string, number | string>; performance: any[]; recentLots: any[] }>('/api/portal/scorecard') });
  const s = q.data?.scorecard;
  const dims = ['quality', 'delivery', 'responsiveness', 'cost', 'correctiveAction', 'auditResults', 'certifications', 'risk'];
  return <Stack gap={2}>
    {s && <Paper variant="outlined" sx={{ p: 2 }}><Typography variant="h4" sx={{ fontFamily: mono }}>{Number(s.overall).toFixed(0)}<Typography component="span" color="text.secondary"> / 100 overall</Typography></Typography>
      <Stack direction="row" gap={3} flexWrap="wrap" sx={{ mt: 1 }}>{dims.map((d) => <Box key={d}><Typography variant="caption" color="text.secondary">{d.replace(/([A-Z])/g, ' $1')}</Typography><Typography sx={{ fontFamily: mono }}>{Number(s[d]).toFixed(0)}</Typography></Box>)}</Stack></Paper>}
    <Paper variant="outlined"><Table size="small"><TableHead><TableRow><TableCell>Lot</TableCell><TableCell>Date</TableCell><TableCell>Lot size</TableCell><TableCell>Sample</TableCell><TableCell>Result</TableCell></TableRow></TableHead>
      <TableBody>{(q.data?.recentLots ?? []).map((l) => <TableRow key={l.number}><TableCell sx={{ fontFamily: mono }}>{l.number}</TableCell><TableCell>{fmtDate(l.inspectionDate)}</TableCell><TableCell>{l.lotSize}</TableCell><TableCell>{l.sampleSize}</TableCell><TableCell>{l.result}</TableCell></TableRow>)}</TableBody></Table></Paper>
  </Stack>;
}

function Audits() {
  const q = useQuery({ queryKey: ['portal-audits'], queryFn: () => get<any[]>('/api/portal/audits') });
  return <Stack gap={2}>{(q.data ?? []).map((a) => <Paper key={a.id} variant="outlined" sx={{ p: 2 }} data-testid="portal-audit">
    <Typography variant="subtitle1"><span style={{ fontFamily: mono }}>{a.number}</span> · {a.title}</Typography>
    <Typography variant="body2" color="text.secondary">{fmtDate(a.plannedStart)} – {fmtDate(a.plannedEnd)}</Typography>
    {a.findings.map((f: any) => <Typography key={f.number} variant="body2" sx={{ mt: 0.5 }}>{f.number} · {f.type} · {f.statement} ({f.status}{f.responseDueOn ? `, response due ${fmtDate(f.responseDueOn)}` : ''})</Typography>)}
  </Paper>)}{q.data?.length === 0 && <Typography color="text.secondary">No audits shared with you.</Typography>}</Stack>;
}

function QualityReport() {
  const q = useQuery({ queryKey: ['portal-report'], queryFn: () => get<Record<string, any>>('/api/portal/quality-report') });
  const d = q.data;
  if (!d) return null;
  return <Paper variant="outlined" sx={{ p: 2 }}>
    <Stack direction="row" gap={4} flexWrap="wrap">{Object.entries(d).filter(([, v]) => typeof v === 'number' || typeof v === 'string').map(([k, v]) => <Box key={k}><Typography variant="caption" color="text.secondary">{k.replace(/([A-Z])/g, ' $1')}</Typography><Typography sx={{ fontFamily: mono }}>{typeof v === 'number' ? +v.toFixed(2) : v}</Typography></Box>)}</Stack>
    {Array.isArray(d.complaintsByMonth) && <Table size="small" sx={{ mt: 2, maxWidth: 360 }}><TableHead><TableRow><TableCell>Month</TableCell><TableCell>Complaints</TableCell></TableRow></TableHead>
      <TableBody>{d.complaintsByMonth.map((m: any) => <TableRow key={m.month}><TableCell>{m.month}</TableCell><TableCell>{m.count}</TableCell></TableRow>)}</TableBody></Table>}
  </Paper>;
}
