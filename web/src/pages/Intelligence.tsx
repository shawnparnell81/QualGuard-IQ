import { useState } from 'react';
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Chip, LinearProgress, MenuItem, Paper, Stack, Switch, Table, TableBody, TableCell, TableHead, TableRow, TextField, ToggleButton, ToggleButtonGroup, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { get, post, put } from '../api';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, fmtDateTime, localDate } from '../components/Status';
import { Section, StepDialog, useRunner, useSites } from '../components/Kit';
import { mono } from '../theme';
import { GeneratedDraft } from './Generation';

const insights = modules.find((m) => m.key === 'insights')!;
const admin = modules.find((m) => m.key === 'admin')!;
const periods = ['Daily', 'Weekly', 'Monthly', 'Quarterly', 'Annual'];

interface KpiDef { key: string; label: string; unit: string; formula: string }
interface KpiPoint { period: string; start: string; end: string; value?: number; numerator: number; denominator: number }
interface KpiSeries { key: string; label: string; unit: string; formula: string; period: number; points: KpiPoint[] }
interface Rec { type: string; id: string; number: string; title: string; status: string; url: string; detail?: string }
interface TrendRow { label: string; current: number; previous: number; change: number }
interface Answer { question: string; intent: string; answer: string; records: Rec[]; trend: TrendRow[]; method: string; confidence?: number }
interface Suggestion { text: string; support: number; confidence: number; sources: string[] }
interface Investigation { subject: Rec; similar: { ncr: Rec; similarity: number; sharedTerms: string[] }[]; rootCauses: Suggestion[]; actions: Suggestion[]; recurringDefects: TrendRow[]; method: string; disclaimer: string }
interface Risk { kind: string; id: string; label: string; score: number; confidence: number; observations: number; drivers: string[] }
interface Exec { period: string; text: string; highlights: string[]; risks: string[]; kpis: KpiSeries[] }

const fmtVal = (v: number | undefined | null, unit: string) => (v == null ? '—' : unit === '$' ? v.toLocaleString(undefined, { style: 'currency', currency: 'USD', maximumFractionDigits: 0 }) : `${v.toLocaleString(undefined, { maximumFractionDigits: 2 })}${unit && unit !== 'count' ? ` ${unit}` : ''}`);

function Spark({ s }: { s: KpiSeries }) {
  return <Box sx={{ height: 180 }}><ResponsiveContainer><LineChart data={s.points.map((p) => ({ ...p, value: p.value ?? null }))}><CartesianGrid stroke="#E2DED5" /><XAxis dataKey="period" fontSize={11} /><YAxis fontSize={11} width={48} /><Tooltip /><Line dataKey="value" stroke="#1F4E4A" strokeWidth={2} dot={{ r: 2 }} connectNulls /></LineChart></ResponsiveContainer></Box>;
}

export function KpiLibrary() {
  const defs = useQuery({ queryKey: ['kpi-defs'], queryFn: () => get<KpiDef[]>('/api/intelligence/kpis') });
  const sites = useSites();
  const [key, setKey] = useState('fpy');
  const [period, setPeriod] = useState(2);
  const [from, setFrom] = useState(localDate(-365));
  const [to, setTo] = useState(localDate());
  const [site, setSite] = useState('');
  const s = useQuery({ queryKey: ['kpi', key, period, from, to, site], queryFn: () => get<KpiSeries>(`/api/intelligence/kpis/${key}?from=${from}&to=${to}&period=${period}${site ? `&siteId=${site}` : ''}`) });
  return (
    <ModuleWorkspace module={insights} crumbs={[{ label: 'KPI library' }]} title="KPI library"
      actions={<Stack direction="row" gap={1}>
        <TextField select size="small" label="Site" value={site} onChange={(e) => setSite(e.target.value)} sx={{ width: 180 }}><MenuItem value="">All my sites</MenuItem>{(sites.data ?? []).map((x) => <MenuItem key={x.id} value={x.id}>{x.name}</MenuItem>)}</TextField>
        <TextField size="small" type="date" label="From" value={from} onChange={(e) => setFrom(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
        <TextField size="small" type="date" label="To" value={to} onChange={(e) => setTo(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
      </Stack>}>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '260px 1fr' }, gap: 2 }}>
        <Paper sx={{ p: 1 }}>{(defs.data ?? []).map((d) => <Box key={d.key} onClick={() => setKey(d.key)} sx={{ px: 1.5, py: 1, borderRadius: 1, cursor: 'pointer', bgcolor: d.key === key ? '#EAF1EF' : undefined, borderLeft: 3, borderLeftColor: d.key === key ? 'primary.main' : 'transparent' }}>
          <Typography variant="body2" sx={{ fontWeight: d.key === key ? 600 : 400 }}>{d.label}</Typography><Typography variant="caption" color="text.secondary">{d.unit}</Typography></Box>)}</Paper>
        <Stack gap={2}>
          <ToggleButtonGroup size="small" exclusive value={period} onChange={(_, v) => v != null && setPeriod(v)}>{periods.map((p, i) => <ToggleButton key={p} value={i}>{p}</ToggleButton>)}</ToggleButtonGroup>
          {s.error && <Alert severity="error">{(s.error as Error).message}</Alert>}
          {s.data && <>
            <Section title={s.data.label}><Typography variant="body2" color="text.secondary" sx={{ mb: 1, fontFamily: mono }}>{s.data.formula}</Typography><Spark s={s.data} /></Section>
            <DataTable data={s.data.points} columns={[{ header: 'Period', accessorKey: 'period' }, { header: 'Value', accessorKey: 'value', cell: (x) => <b style={{ fontFamily: mono }}>{fmtVal(x.getValue(), s.data!.unit)}</b> },
              { header: 'Numerator', accessorKey: 'numerator', cell: (x) => <span style={{ fontFamily: mono }}>{x.getValue()}</span> }, { header: 'Denominator', accessorKey: 'denominator', cell: (x) => <span style={{ fontFamily: mono }}>{x.getValue()}</span> }]} />
          </>}
        </Stack>
      </Box>
    </ModuleWorkspace>
  );
}

const examples = ['Which CAPAs are overdue?', 'Which supplier caused the most NCRs this quarter?', 'Show the complaint trend', 'Why did scrap go up last month?', 'What should I prepare for the next audit?'];

function AnswerCard({ a }: { a: Answer }) {
  const nav = useNavigate();
  return (
    <Paper sx={{ p: 2.5, borderLeft: 4, borderLeftColor: 'primary.main' }}>
      <Typography variant="caption" color="text.secondary">{a.question}</Typography>
      <Typography sx={{ whiteSpace: 'pre-wrap', my: 1 }}>{a.answer}</Typography>
      {a.trend.length > 0 && <Table size="small" sx={{ mb: 1 }}><TableHead><TableRow><TableCell>Item</TableCell><TableCell>Now</TableCell><TableCell>Before</TableCell><TableCell>Change</TableCell></TableRow></TableHead>
        <TableBody>{a.trend.map((t) => <TableRow key={t.label}><TableCell>{t.label}</TableCell><TableCell sx={{ fontFamily: mono }}>{t.current}</TableCell><TableCell sx={{ fontFamily: mono }}>{t.previous}</TableCell><TableCell sx={{ fontFamily: mono, color: t.change > 0 ? 'error.main' : 'success.main' }}>{t.change > 0 ? '+' : ''}{t.change}</TableCell></TableRow>)}</TableBody></Table>}
      {a.records.length > 0 && <Stack direction="row" gap={1} flexWrap="wrap" sx={{ mb: 1 }}>{a.records.map((r) => <Chip key={r.type + r.id} size="small" variant="outlined" label={`${r.number} · ${r.title}`.slice(0, 60)} onClick={() => nav(r.url)} />)}</Stack>}
      <Typography variant="caption" color="text.secondary">{a.method}{a.confidence != null ? ` · confidence ${(a.confidence * 100).toFixed(0)}%` : ''}</Typography>
    </Paper>
  );
}

export function Assistant() {
  const [p, setP] = useSearchParams();
  const view = p.get('view') ?? 'ask';
  const [q, setQ] = useState('');
  const [log, setLog] = useState<Answer[]>([]);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [month, setMonth] = useState(localDate().slice(0, 7));
  const exec = useQuery({ queryKey: ['exec', month], queryFn: () => get<Exec>(`/api/intelligence/executive?month=${month}-01`), enabled: view === 'executive' });
  const ask = async (text: string) => {
    if (!text.trim()) return;
    setBusy(true); setErr(null);
    try { const a = await post<Answer>('/api/intelligence/ask', { question: text }); setLog((l) => [a, ...l]); setQ(''); } catch (e) { setErr((e as Error).message); } finally { setBusy(false); }
  };
  return (
    <ModuleWorkspace module={insights} crumbs={[{ label: 'Quality assistant' }]} title="Quality assistant"
      tabs={[{ label: 'Ask', value: 'ask' }, { label: 'Monthly narrative', value: 'executive' }]} tab={view} onTab={(v) => setP(v === 'ask' ? {} : { view: v })}>
      <Alert severity="info" sx={{ mb: 2 }}>Answers come from records you can already view, using rule-based matching and statistics. The assistant suggests; it never closes or approves anything.</Alert>
      {view === 'ask' && <>
        <Paper component="form" onSubmit={(e) => { e.preventDefault(); ask(q); }} sx={{ p: 2, mb: 1.5, display: 'flex', gap: 1 }}>
          <TextField fullWidth autoFocus placeholder="Ask about NCRs, CAPAs, suppliers, complaints, scrap or audits" value={q} onChange={(e) => setQ(e.target.value)} />
          <Button type="submit" variant="contained" disabled={busy}>Ask</Button>
        </Paper>
        <Stack direction="row" gap={1} flexWrap="wrap" sx={{ mb: 2 }}>{examples.map((x) => <Chip key={x} label={x} onClick={() => ask(x)} />)}</Stack>
        {busy && <LinearProgress sx={{ mb: 2 }} />}
        {err && <Alert severity="error" sx={{ mb: 2 }}>{err}</Alert>}
        <Stack gap={2}>{log.map((a, i) => <AnswerCard key={i} a={a} />)}</Stack>
      </>}
      {view === 'executive' && <>
        <TextField size="small" type="month" label="Month" value={month} onChange={(e) => setMonth(e.target.value)} sx={{ mb: 2 }} slotProps={{ inputLabel: { shrink: true } }} />
        {exec.error && <Alert severity="error">{(exec.error as Error).message}</Alert>}
        {exec.data && <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '1.3fr 1fr' }, gap: 2 }}>
          <Section title={`Quality summary · ${exec.data.period}`}>
            <Typography sx={{ whiteSpace: 'pre-wrap', mb: 2 }}>{exec.data.text}</Typography>
            <Typography variant="overline" color="success.main">Highlights</Typography>{exec.data.highlights.map((h) => <Typography key={h} variant="body2">• {h}</Typography>)}
            <Typography variant="overline" color="error.main" sx={{ display: 'block', mt: 1 }}>Risks</Typography>{exec.data.risks.map((h) => <Typography key={h} variant="body2">• {h}</Typography>)}
          </Section>
          <Stack gap={2}>{exec.data.kpis.map((k) => <Section key={k.key} title={k.label}><Spark s={k} /></Section>)}</Stack>
        </Box>}
      </>}
    </ModuleWorkspace>
  );
}

export function InvestigatePage() {
  const nav = useNavigate();
  const [p, setP] = useSearchParams();
  const ncrId = p.get('ncr') ?? '';
  const ncrs = useQuery({ queryKey: ['ncr-pick'], queryFn: () => get<{ items: { id: string; number: string; title: string }[] }>('/api/ncrs?pageSize=200') });
  const inv = useQuery({ queryKey: ['investigate', ncrId], queryFn: () => get<Investigation>(`/api/intelligence/investigate/${ncrId}`), enabled: !!ncrId });
  const d = inv.data;
  const sugg = (title: string, rows: Suggestion[]) => <Section title={title}>{rows.length === 0 && <Typography color="text.secondary">Not enough linked history.</Typography>}
    {rows.map((s) => <Box key={s.text} sx={{ py: 1, borderTop: 1, borderColor: 'divider' }}><Stack direction="row" justifyContent="space-between" gap={2}><Typography variant="body2">{s.text}</Typography><Chip size="small" label={`${(s.confidence * 100).toFixed(0)}% · ${s.support}×`} /></Stack>
      <Typography variant="caption" color="text.secondary" sx={{ fontFamily: mono }}>{s.sources.join(', ')}</Typography></Box>)}</Section>;
  return (
    <ModuleWorkspace module={insights} crumbs={[{ label: 'Investigate' }]} title="Investigation assistant"
      actions={<TextField select size="small" label="NCR" value={ncrId} onChange={(e) => setP({ ncr: e.target.value })} sx={{ minWidth: 320 }}>{(ncrs.data?.items ?? []).map((n) => <MenuItem key={n.id} value={n.id}>{n.number} — {n.title}</MenuItem>)}</TextField>}>
      {!ncrId && <Typography color="text.secondary">Choose an NCR to find similar past nonconformances, the root causes and actions that worked, and recurring defects.</Typography>}
      {inv.isFetching && <LinearProgress />}
      {inv.error && <Alert severity="error">{(inv.error as Error).message}</Alert>}
      {d && <>
        <Alert severity="warning" sx={{ mb: 2 }}>{d.disclaimer}</Alert>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '1fr 1fr' }, gap: 2 }}>
          <Section title={`Similar NCRs to ${d.subject.number}`}>
            <Table size="small"><TableBody>{d.similar.map((s) => <TableRow key={s.ncr.id} hover sx={{ cursor: 'pointer' }} onClick={() => nav(s.ncr.url)}>
              <TableCell sx={{ fontFamily: mono, width: 120 }}>{s.ncr.number}</TableCell><TableCell>{s.ncr.title}<Typography variant="caption" display="block" color="text.secondary">{s.sharedTerms.join(' · ')}</Typography></TableCell>
              <TableCell sx={{ width: 120 }}><LinearProgress variant="determinate" value={s.similarity * 100} /><Typography variant="caption" sx={{ fontFamily: mono }}>{(s.similarity * 100).toFixed(0)}%</Typography></TableCell></TableRow>)}
              {d.similar.length === 0 && <TableRow><TableCell><Typography color="text.secondary">No similar NCRs.</Typography></TableCell></TableRow>}</TableBody></Table>
          </Section>
          <Stack gap={2}>{sugg('Suggested root causes', d.rootCauses)}{sugg('Corrective actions that were used', d.actions)}
            <Section title="Recurring defects"><Table size="small"><TableBody>{d.recurringDefects.map((t) => <TableRow key={t.label}><TableCell>{t.label}</TableCell><TableCell sx={{ fontFamily: mono }}>{t.current} now · {t.previous} before</TableCell></TableRow>)}</TableBody></Table></Section></Stack>
        </Box>
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 2 }}>{d.method}</Typography>
        <Box sx={{ mt: 2 }}><GeneratedDraft key={ncrId} fn="ncr-investigation" recordType="Ncr" recordId={ncrId} /></Box>
      </>}
    </ModuleWorkspace>
  );
}

const riskKinds = [['supplier', 'Supplier risk'], ['product', 'Product failure'], ['complaint', 'Complaint risk'], ['ncr', 'Recurring NCR'], ['process', 'Process instability']];
export function RiskPage() {
  const [kind, setKind] = useState('supplier');
  const r = useQuery({ queryKey: ['predict', kind], queryFn: () => get<Risk[]>(`/api/intelligence/predict/${kind}`) });
  const t = useQuery({ queryKey: ['trends'], queryFn: () => get<Record<string, TrendRow[]>>('/api/intelligence/trends') });
  return (
    <ModuleWorkspace module={insights} crumbs={[{ label: 'Trends & risk' }]} title="Trends & predictive risk" tabs={riskKinds.map(([v, l]) => ({ value: v, label: l }))} tab={kind} onTab={setKind}>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '1.4fr 1fr' }, gap: 2 }}>
        <Section title="Ranked by score">
          <Table size="small"><TableHead><TableRow><TableCell>Subject</TableCell><TableCell>Score</TableCell><TableCell>Confidence</TableCell><TableCell>Why</TableCell></TableRow></TableHead>
            <TableBody>{(r.data ?? []).map((x) => <TableRow key={x.id}><TableCell>{x.label}</TableCell>
              <TableCell sx={{ width: 140 }}><Box sx={{ height: 8, borderRadius: 4, bgcolor: '#E2DED5' }}><Box sx={{ height: 8, borderRadius: 4, width: `${Math.min(100, x.score)}%`, bgcolor: x.score > 66 ? 'error.main' : x.score > 33 ? 'warning.main' : 'success.main' }} /></Box><Typography variant="caption" sx={{ fontFamily: mono }}>{x.score.toFixed(0)}</Typography></TableCell>
              <TableCell sx={{ fontFamily: mono }}>{(x.confidence * 100).toFixed(0)}% · n={x.observations}</TableCell><TableCell><Typography variant="caption">{x.drivers.join('; ')}</Typography></TableCell></TableRow>)}
              {r.data && r.data.length === 0 && <TableRow><TableCell colSpan={4}><Typography color="text.secondary">Not enough history to score.</Typography></TableCell></TableRow>}</TableBody></Table>
          <Typography variant="caption" color="text.secondary">Scores compare recent event rates with each subject's own history (Poisson rate). Confidence grows with the number of observations.</Typography>
        </Section>
        <Stack gap={2}>{Object.entries(t.data ?? {}).map(([k, rows]) => <Section key={k} title={`Trend · ${k}`}>
          <Table size="small"><TableBody>{rows.slice(0, 6).map((x) => <TableRow key={x.label}><TableCell>{x.label}</TableCell><TableCell sx={{ fontFamily: mono }}>{x.current}</TableCell><TableCell sx={{ fontFamily: mono, color: x.change > 0 ? 'error.main' : 'success.main' }}>{x.change > 0 ? '▲' : x.change < 0 ? '▼' : '·'} {Math.abs(x.change)}</TableCell></TableRow>)}</TableBody></Table></Section>)}</Stack>
      </Box>
    </ModuleWorkspace>
  );
}

export function DatasetsPage() {
  const list = useQuery({ queryKey: ['datasets'], queryFn: () => get<string[]>('/api/intelligence/datasets') });
  const [name, setName] = useState('');
  const page = useQuery({ queryKey: ['dataset', name], queryFn: () => get<{ rows: Record<string, unknown>[]; nextCursor?: string; asOf: string }>(`/api/intelligence/datasets/${name}?pageSize=50`), enabled: !!name });
  const cols = Object.keys(page.data?.rows[0] ?? {}).slice(0, 10);
  return (
    <ModuleWorkspace module={insights} crumbs={[{ label: 'Datasets' }]} title="Datasets for BI and data lakes" tabs={(list.data ?? []).map((d) => ({ label: d, value: d }))} tab={name || undefined} onTab={setName}>
      <Alert severity="info" sx={{ mb: 2 }}>Flat, incremental feeds for Power BI, Power Automate and data lakes. Page with <span style={{ fontFamily: mono }}>GET /api/intelligence/datasets/&lt;name&gt;?cursor=…</span>. Rows are limited to the sites the caller can view. Scheduled NDJSON exports go to object storage under exports/&lt;dataset&gt;/.</Alert>
      {name && page.data && <Paper variant="outlined" sx={{ overflowX: 'auto' }}><Table size="small"><TableHead><TableRow>{cols.map((c) => <TableCell key={c}>{c}</TableCell>)}</TableRow></TableHead>
        <TableBody>{page.data.rows.map((r, i) => <TableRow key={i}>{cols.map((c) => <TableCell key={c} sx={{ fontFamily: mono, fontSize: 12, whiteSpace: 'nowrap', maxWidth: 240, overflow: 'hidden', textOverflow: 'ellipsis' }}>{r[c] == null ? '' : String(r[c])}</TableCell>)}</TableRow>)}</TableBody></Table>
        <Typography variant="caption" color="text.secondary" sx={{ p: 1, display: 'block' }}>As of {fmtDateTime(page.data.asOf)} · next cursor {page.data.nextCursor ?? 'none'}</Typography></Paper>}
      {!name && <Typography color="text.secondary">Choose a dataset.</Typography>}
    </ModuleWorkspace>
  );
}

// ---------------------------------------------------------------- Alerts admin
const alertKinds = ['NCR open too long (days)', 'CAPA overdue (days)', 'Failed audit findings', 'Rejected AQL lots', 'Failed first articles', 'SPC violations', 'Supplier score below', 'Complaints above'];
interface Rule { id: string; name: string; kind: number; threshold: number; windowDays: number; recipients: string; inApp: boolean; email: boolean; sms: boolean; teams: boolean; active: boolean; siteId: string; departmentId?: string }
interface AlertEvent { id: string; ruleId: string; title: string; recordType?: string; recordId?: string; raisedAt: string; channels: string; deliveryErrors?: string; clearedAt?: string }
interface Endpoint { id: string; name: string; kind: string; url: string; enabled: boolean; hasSecret: boolean }

export function AlertsAdmin() {
  const nav = useNavigate();
  const qc = useQueryClient();
  const [p, setP] = useSearchParams();
  const view = p.get('view') ?? 'rules';
  const sites = useSites();
  const rules = useQuery({ queryKey: ['alert-rules'], queryFn: () => get<Rule[]>('/api/alerts/rules') });
  const events = useQuery({ queryKey: ['alert-events'], queryFn: () => get<AlertEvent[]>('/api/alerts/events'), enabled: view === 'events' });
  const eps = useQuery({ queryKey: ['alert-eps'], queryFn: () => get<Endpoint[]>('/api/alerts/endpoints'), enabled: view === 'endpoints' });
  const refresh = () => ['alert-rules', 'alert-events', 'alert-eps'].forEach((k) => qc.invalidateQueries({ queryKey: [k] }));
  const { error, busy, run } = useRunner(refresh);
  const [dlg, setDlg] = useState<{ kind: 'rule'; r?: Rule } | { kind: 'ep'; e?: Endpoint } | null>(null);
  const [raised, setRaised] = useState<number | null>(null);
  const rname = (id: string) => rules.data?.find((r) => r.id === id)?.name ?? '';
  const ruleBody = (v: Record<string, string>) => ({ name: v.name, kind: Number(v.kind), threshold: Number(v.threshold), windowDays: Number(v.windowDays), recipients: v.recipients.split(',').filter(Boolean),
    inApp: v.inApp === 'true', email: v.email === 'true', sms: v.sms === 'true', teams: v.teams === 'true', active: v.active === 'true', siteId: v.siteId, departmentId: null });
  return (
    <ModuleWorkspace module={admin} crumbs={[{ label: 'Notifications' }]} title="Alerts & integration points"
      tabs={[{ label: 'Alert rules', value: 'rules' }, { label: 'Raised alerts', value: 'events' }, { label: 'Integration points', value: 'endpoints' }]} tab={view} onTab={(v) => setP(v === 'rules' ? {} : { view: v })}
      actions={<Stack direction="row" gap={1}>
        {view === 'rules' && <Button variant="contained" onClick={() => setDlg({ kind: 'rule' })}>New rule</Button>}
        {view === 'endpoints' && <Button variant="contained" onClick={() => setDlg({ kind: 'ep' })}>New integration point</Button>}
        <Button variant="outlined" disabled={busy} onClick={() => run(async () => setRaised((await post<{ raised: number }>('/api/alerts/evaluate')).raised))}>Evaluate now</Button>
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {raised != null && <Alert severity="info" sx={{ mb: 2 }} onClose={() => setRaised(null)}>{raised} new alert(s) raised.</Alert>}
      {view === 'rules' && <DataTable data={rules.data ?? []} onRowClick={(r) => setDlg({ kind: 'rule', r })} empty="No alert rules yet."
        columns={[{ header: 'Rule', accessorKey: 'name' }, { header: 'Condition', accessorFn: (r) => `${alertKinds[r.kind]} ${r.threshold}` }, { header: 'Window', accessorFn: (r) => `${r.windowDays} d` },
          { header: 'Channels', accessorFn: (r) => [r.inApp && 'In-app', r.email && 'Email', r.sms && 'SMS', r.teams && 'Teams'].filter(Boolean).join(', ') }, { header: 'Recipients', accessorFn: (r) => (JSON.parse(r.recipients) as string[]).length },
          { header: 'Active', accessorKey: 'active', cell: (x) => <StatusChip status={x.getValue() ? 'Active' : 'Inactive'} /> }]} />}
      {view === 'events' && <DataTable data={events.data ?? []} onRowClick={(e) => e.recordType && e.recordId && nav(`/records/${e.recordType}/${e.recordId}`)} empty="No alerts raised."
        columns={[{ header: 'Raised', accessorKey: 'raisedAt', cell: (x) => fmtDateTime(x.getValue()) }, { header: 'Rule', accessorFn: (e) => rname(e.ruleId) }, { header: 'Alert', accessorKey: 'title' }, { header: 'Channels', accessorKey: 'channels' },
          { header: 'Delivery problems', accessorKey: 'deliveryErrors', cell: (x) => x.getValue() ? <Typography variant="caption" color="error.main">{x.getValue()}</Typography> : '' }, { header: 'Cleared', accessorKey: 'clearedAt', cell: (x) => fmtDateTime(x.getValue()) }]} />}
      {view === 'endpoints' && <Paper variant="outlined"><Table size="small"><TableHead><TableRow><TableCell>Name</TableCell><TableCell>Kind</TableCell><TableCell>URL</TableCell><TableCell>Signing secret</TableCell><TableCell>Enabled</TableCell></TableRow></TableHead>
        <TableBody>{(eps.data ?? []).map((e) => <TableRow key={e.id} hover sx={{ cursor: 'pointer' }} onClick={() => setDlg({ kind: 'ep', e })}><TableCell>{e.name}</TableCell><TableCell>{e.kind}</TableCell><TableCell sx={{ fontFamily: mono, fontSize: 12 }}>{e.url}</TableCell><TableCell>{e.hasSecret ? 'Set (hidden)' : '—'}</TableCell>
          <TableCell><Switch size="small" slotProps={{ input: { 'aria-label': `${e.name} enabled` } }} checked={e.enabled} onClick={(ev) => ev.stopPropagation()} onChange={(ev) => run(() => put(`/api/alerts/endpoints/${e.id}`, { name: e.name, kind: e.kind, url: e.url, secret: null, enabled: ev.target.checked }))} /></TableCell></TableRow>)}
          {eps.data && eps.data.length === 0 && <TableRow><TableCell colSpan={5}><Typography color="text.secondary">No integration points. SMS and Teams alerts need one.</Typography></TableCell></TableRow>}</TableBody></Table></Paper>}
      {dlg?.kind === 'rule' && <StepDialog open title={dlg.r ? `Edit ${dlg.r.name}` : 'New alert rule'} confirm="Save rule" onClose={() => setDlg(null)}
        inputs={[{ name: 'name', label: 'Name', initial: dlg.r?.name }, { name: 'kind', label: 'Condition', type: 'select', initial: String(dlg.r?.kind ?? 0), options: alertKinds.map((k, i) => [i, k]) },
          { name: 'threshold', label: 'Threshold', initial: String(dlg.r?.threshold ?? 7) }, { name: 'windowDays', label: 'Window (days)', initial: String(dlg.r?.windowDays ?? 30) },
          { name: 'recipients', label: 'Recipients', type: 'users', initial: dlg.r ? (JSON.parse(dlg.r.recipients) as string[]).join(',') : '' },
          { name: 'inApp', label: 'In-app', type: 'checkbox', initial: String(dlg.r?.inApp ?? true) }, { name: 'email', label: 'Email', type: 'checkbox', initial: String(dlg.r?.email ?? false) },
          { name: 'sms', label: 'SMS', type: 'checkbox', initial: String(dlg.r?.sms ?? false) }, { name: 'teams', label: 'Teams', type: 'checkbox', initial: String(dlg.r?.teams ?? false) },
          { name: 'active', label: 'Active', type: 'checkbox', initial: String(dlg.r?.active ?? true) }, { name: 'siteId', label: 'Site', type: 'select', initial: dlg.r?.siteId ?? '', options: (sites.data ?? []).map((s) => [s.id, s.name]) }]}
        onConfirm={async (v) => { if (dlg.r) await put(`/api/alerts/rules/${dlg.r.id}`, ruleBody(v)); else await post('/api/alerts/rules', ruleBody(v)); refresh(); }} />}
      {dlg?.kind === 'ep' && <StepDialog open title={dlg.e ? `Edit ${dlg.e.name}` : 'New integration point'} confirm="Save" onClose={() => setDlg(null)} intro="https only. Each POST is signed with HMAC-SHA256 in X-QualGuard-Signature. The secret is write-only."
        inputs={[{ name: 'name', label: 'Name', initial: dlg.e?.name }, { name: 'kind', label: 'Kind', type: 'select', initial: dlg.e?.kind ?? 'Teams', options: [['Sms', 'SMS gateway'], ['Teams', 'Microsoft Teams webhook'], ['DataLake', 'Data lake / webhook']] },
          { name: 'url', label: 'URL', initial: dlg.e?.url }, { name: 'secret', label: dlg.e?.hasSecret ? 'Signing secret (blank keeps current)' : 'Signing secret', type: 'password' }, { name: 'enabled', label: 'Enabled', type: 'checkbox', initial: String(dlg.e?.enabled ?? false) }]}
        onConfirm={async (v) => { const b = { name: v.name, kind: v.kind, url: v.url, secret: v.secret || null, enabled: v.enabled === 'true' }; if (dlg.e) await put(`/api/alerts/endpoints/${dlg.e.id}`, b); else await post('/api/alerts/endpoints', b); refresh(); }} />}
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 2 }}>The worker evaluates rules every 15 minutes. A rule raises once per record until the condition clears. <RouterLink to="/insights/datasets">Datasets</RouterLink> feed scheduled data-lake exports.</Typography>
    </ModuleWorkspace>
  );
}
