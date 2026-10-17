import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Alert, Box, Button, Chip, LinearProgress, MenuItem, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { z } from 'zod';
import { get, post, put } from '../api';
import { useAuth } from '../auth';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, fmtDate, fmtDateTime } from '../components/Status';
import { Facts, SchemaForm, Section, StepDialog, useRunner } from '../components/Kit';
import { Loading, SiteSelect, mod, num, useSite } from './Enterprise';

/* eslint-disable @typescript-eslint/no-explicit-any */
const sources = ['Customer', 'Regulatory', 'Industry', 'Contractual'];
const validationKinds = ['Process', 'Equipment', 'Software', 'Test method'];
const validationStatus = ['Planning', 'ProtocolApproval', 'Execution', 'ResultsApproval', 'Released', 'Cancelled'];
const evidenceTypes = ['Document', 'Audit', 'Capa', 'TrainingCourse', 'Validation', 'ControlPlan', 'Risk'];

export const mappingStates = ['Draft', 'Unverified', 'Under review', 'Verified', 'Superseded', 'Rejected'];
const stateColor: Record<number, 'default' | 'warning' | 'info' | 'success' | 'error'> = { 0: 'default', 1: 'warning', 2: 'info', 3: 'success', 4: 'default', 5: 'error' };
/** Verification state of shipped mapping content; unverified content is always labelled as such. */
export function VerificationChip({ v }: { v?: { state: number; verified: boolean; label: string } }) {
  if (!v) return <Chip size="small" color="warning" variant="outlined" label="Unverified" />;
  return <Chip size="small" data-testid="verification-chip" color={v.verified ? 'success' : 'warning'} variant="outlined" title={v.label} label={v.verified ? 'Verified content' : `${mappingStates[v.state] ?? 'Unverified'} content — not verified`} />;
}

export function ComplianceHome() {
  const [site, setSite] = useSite();
  const qc = useQueryClient();
  const { can } = useAuth();
  const [tab, setTab] = useState('packs');
  const packs = useQuery({ queryKey: ['packs', site], enabled: !!site, queryFn: () => get<any[]>(`/api/compliance/packs?siteId=${site}`) });
  const cov = useQuery({ queryKey: ['coverage', site], enabled: !!site, queryFn: () => get<any>(`/api/compliance/coverage?siteId=${site}`) });
  const obl = useQuery({ queryKey: ['obligations', site], enabled: !!site, queryFn: () => get<any[]>(`/api/compliance/obligations?siteId=${site}`) });
  const maps = useQuery({ queryKey: ['compliance-mappings'], enabled: tab === 'verify', queryFn: () => get<any>('/api/compliance/mappings') });
  const [dlg, setDlg] = useState<null | 'new' | { map: string } | { submit: any } | { decide: any } | { history: any }>(null);
  const hist = useQuery({ queryKey: ['compliance-mapping-history', (dlg as any)?.history?.id], enabled: !!(dlg as any)?.history, queryFn: () => get<any[]>(`/api/compliance/mappings/${(dlg as any).history.id}/history`) });
  const refresh = () => ['packs', 'coverage', 'obligations', 'compliance-mappings'].forEach((k) => qc.invalidateQueries({ queryKey: [k] }));
  const r = useRunner(refresh);
  return (
    <ModuleWorkspace module={mod('compliance')} crumbs={[{ label: 'Compliance' }]} title="Regulatory and customer compliance"
      tabs={[{ label: 'Packs', value: 'packs' }, { label: `Obligations (${obl.data?.length ?? 0})`, value: 'obl' }, { label: 'Coverage', value: 'cov' }, { label: 'Content verification', value: 'verify' }]} tab={tab} onTab={setTab}
      actions={<Stack direction="row" gap={1}><SiteSelect value={site} onChange={setSite} />{can('compliance.edit') && <Button variant="contained" onClick={() => setDlg('new')}>Add obligation</Button>}</Stack>}>
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      {tab === 'packs' && <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(260px, 1fr))', gap: 1.5 }}>
        {(packs.data ?? []).map((p) => (
          <Paper key={p.code} data-testid="pack" sx={{ p: 2, borderTop: 4, borderColor: p.enabled ? 'success.main' : 'divider' }}>
            <Typography sx={{ fontWeight: 600 }}>{p.name}</Typography>
            <Typography variant="caption" color="text.secondary">{p.source} · {p.clauseCount} clauses (numbers and titles only)</Typography>
            <Box sx={{ mt: 1 }}><VerificationChip v={p.verification} /></Box>
            <Box sx={{ mt: 1.5 }}>{can('compliance.edit') && <Button size="small" variant={p.enabled ? 'outlined' : 'contained'} onClick={() => r.run(() => post(`/api/compliance/packs/${p.code}/enable?siteId=${site}`, { enabled: !p.enabled }))}>{p.enabled ? 'Disable' : 'Enable for site'}</Button>}</Box>
          </Paper>))}
      </Box>}
      {tab === 'obl' && <DataTable data={obl.data ?? []} empty="No obligations. Enable a pack or add one."
        columns={[{ header: 'Number', id: 'n', accessorFn: (o: any) => o.obligation.number, cell: num }, { header: 'Reference', id: 'r', accessorFn: (o: any) => o.obligation.reference, cell: num }, { header: 'Title', id: 't', accessorFn: (o: any) => o.obligation.title },
          { header: 'Source', id: 's', accessorFn: (o: any) => sources[o.obligation.source] }, { header: 'Pack', id: 'p', accessorFn: (o: any) => o.obligation.packCode ?? 'Own' },
          { header: 'Evidence', id: 'm', accessorFn: (o: any) => o.mappings.length, cell: (x) => (x.getValue() ? <Chip size="small" color="success" label={`${x.getValue()} linked`} /> : <Chip size="small" label="Not mapped" />) },
          { header: '', id: 'a', cell: (x) => can('compliance.edit') && <Button size="small" onClick={() => setDlg({ map: x.row.original.obligation.id })}>Map evidence</Button> }]} />}
      {tab === 'cov' && cov.data?.disclaimer && <Alert severity="info" sx={{ mb: 2 }} data-testid="coverage-disclaimer">{cov.data.disclaimer}</Alert>}
      {tab === 'cov' && cov.data && <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2 }}>
        <Section title="By pack"><Table size="small"><TableHead><TableRow>{['Pack', 'Content', 'Obligations', 'Mapped', 'Audited', 'Coverage'].map((h) => <TableCell key={h}>{h}</TableCell>)}</TableRow></TableHead>
          <TableBody>{cov.data.byPack.map((p: any) => <TableRow key={p.pack}><TableCell>{p.pack}</TableCell><TableCell>{p.pack === '(own)' ? <Typography variant="caption">Own</Typography> : <VerificationChip v={{ state: p.verificationState, verified: p.contentVerified, label: p.verificationLabel }} />}</TableCell><TableCell>{p.total}</TableCell><TableCell>{p.mapped}</TableCell><TableCell>{p.audited}</TableCell>
            <TableCell sx={{ minWidth: 120 }}><LinearProgress variant="determinate" value={p.total ? (100 * p.mapped) / p.total : 0} /></TableCell></TableRow>)}</TableBody></Table></Section>
        <Section title="By source"><Table size="small"><TableBody>{cov.data.bySource.map((s: any) => <TableRow key={s.source}><TableCell>{s.source}</TableCell><TableCell>{s.mapped} / {s.total} mapped</TableCell></TableRow>)}</TableBody></Table></Section>
      </Box>}
      {tab === 'verify' && maps.data && <>
        <Alert severity="info" sx={{ mb: 2 }}>{maps.data.disclaimer}</Alert>
        <DataTable data={maps.data.mappings.map((g: any) => ({ ...g.versions[0], status: g.status, count: g.versions.length }))} empty="No mapping content."
          columns={[{ header: 'Content', id: 'n', accessorFn: (x: any) => x.name }, { header: 'Version', id: 'v', accessorFn: (x: any) => x.mappingVersion, cell: (x) => <span>v{x.getValue()} <Typography component="span" variant="caption" color="text.secondary">({x.row.original.packVersion})</Typography></span> },
            { header: 'State', id: 's', accessorFn: (x: any) => x.state, cell: (x) => <Chip size="small" data-testid="mapping-state" color={stateColor[x.getValue()]} label={mappingStates[x.getValue()]} /> },
            { header: 'Counts as', id: 'c', accessorFn: (x: any) => x.status.label, cell: (x) => <VerificationChip v={x.row.original.status} /> },
            { header: 'Source', id: 'src', accessorFn: (x: any) => x.sourceTitle ?? '', cell: (x) => <Box><Typography variant="body2">{x.row.original.sourceTitle ?? '—'}</Typography><Typography variant="caption" color="text.secondary">{[x.row.original.edition, x.row.original.publisher].filter(Boolean).join(' · ')}</Typography></Box> },
            { header: 'Verified', id: 'vd', accessorFn: (x: any) => x.verifiedOn ?? '', cell: (x) => x.getValue() ? `${x.getValue()} · ${x.row.original.verifiedByName ?? ''}` : '—' },
            { header: '', id: 'a', cell: (x) => { const v = x.row.original; return <Stack direction="row" gap={0.5}>
              {can('compliance.edit') && [0, 1, 5].includes(v.state) && <Button size="small" onClick={() => setDlg({ submit: v })}>Submit for review</Button>}
              {can('compliance.verify') && v.state === 2 && <Button size="small" variant="contained" onClick={() => setDlg({ decide: v })}>Review</Button>}
              {can('compliance.edit') && [3, 4].includes(v.state) && <Button size="small" onClick={() => r.run(() => post(`/api/compliance/mappings/${v.id}/revise`))}>New version</Button>}
              <Button size="small" onClick={() => setDlg({ history: v })}>History</Button></Stack>; } }]} />
      </>}
      <StepDialog open={!!dlg && typeof dlg === 'object' && 'submit' in dlg} onClose={() => setDlg(null)} title="Submit for verification" confirm="Submit"
        intro="Name the authoritative source the content was checked against. Record clause identifiers and references only; do not paste standard text."
        inputs={[{ name: 'sourceTitle', label: 'Source title', initial: (dlg as any)?.submit?.sourceTitle ?? '' }, { name: 'edition', label: 'Edition', initial: (dlg as any)?.submit?.edition ?? '' },
          { name: 'publisher', label: 'Owner / publisher', initial: (dlg as any)?.submit?.publisher ?? '' }, { name: 'reference', label: 'Reference (licensed copy, document number or URL)', initial: (dlg as any)?.submit?.reference ?? '' }, { name: 'comment', label: 'Comments', type: 'multiline' }]}
        onConfirm={async (v) => { await post(`/api/compliance/mappings/${(dlg as any).submit.id}/submit`, v); refresh(); }} />
      <StepDialog open={!!dlg && typeof dlg === 'object' && 'decide' in dlg} onClose={() => setDlg(null)} title="Review mapping content" confirm="Record decision"
        intro={`${(dlg as any)?.decide?.name ?? ''} — checked against ${(dlg as any)?.decide?.sourceTitle ?? ''} (${(dlg as any)?.decide?.edition ?? ''}). You cannot review your own submission.`}
        inputs={[{ name: 'verify', label: 'Decision', type: 'select', options: [['true', 'Verified — matches the source'], ['false', 'Rejected — does not match']], initial: 'true' }, { name: 'effectiveOn', label: 'Effective date', type: 'date' }, { name: 'comment', label: 'Review comments', type: 'multiline' }]}
        onConfirm={async (v) => { await post(`/api/compliance/mappings/${(dlg as any).decide.id}/decision`, { verify: v.verify === 'true', comment: v.comment, effectiveOn: v.effectiveOn || null }); refresh(); }} />
      {!!dlg && typeof dlg === 'object' && 'history' in dlg && <Paper variant="outlined" sx={{ p: 2, mt: 2 }} data-testid="mapping-history">
        <Stack direction="row" alignItems="center"><Typography sx={{ fontWeight: 600, flex: 1 }}>History — {(dlg as any).history.name} v{(dlg as any).history.mappingVersion}</Typography><Button size="small" onClick={() => setDlg(null)}>Close</Button></Stack>
        <Table size="small"><TableBody>{(hist.data ?? []).map((e: any) => <TableRow key={e.id}><TableCell>{fmtDateTime(e.at)}</TableCell><TableCell>{e.fromState == null ? '—' : mappingStates[e.fromState]} → {mappingStates[e.toState]}</TableCell><TableCell>{e.byName}</TableCell><TableCell>{e.comment}</TableCell></TableRow>)}</TableBody></Table>
      </Paper>}
      <StepDialog open={dlg === 'new'} onClose={() => setDlg(null)} title="Add obligation" confirm="Add"
        inputs={[{ name: 'source', label: 'Source', type: 'select', options: sources.map((s, i) => [i, s]), initial: '0' }, { name: 'reference', label: 'Reference (e.g. customer-specific requirement section)' }, { name: 'title', label: 'Title' }, { name: 'party', label: 'Customer / authority' }]}
        onConfirm={async (v) => { await post('/api/compliance/obligations', { source: Number(v.source || 0), reference: v.reference, title: v.title, party: v.party, siteId: site }); refresh(); }} />
      <StepDialog open={typeof dlg === 'object' && dlg !== null} onClose={() => setDlg(null)} title="Map evidence" confirm="Map" intro="Link the record that satisfies this obligation."
        inputs={[{ name: 'type', label: 'Record type', type: 'select', options: evidenceTypes.map((t) => [t, t]), initial: 'Document' }, { name: 'number', label: 'Record number' }]}
        onConfirm={async (v) => {
          const hits = await get<any>(`/api/search?q=${encodeURIComponent(v.number)}`);
          const all: any[] = Array.isArray(hits) ? hits : hits.items ?? hits.results ?? [];
          const hit = all.find((h) => (h.recordType ?? h.type) === v.type);
          if (!hit) throw new Error(`No ${v.type} matching ${v.number} found.`);
          await post(`/api/compliance/obligations/${(dlg as any).map}/mappings`, { targetType: v.type, targetId: hit.recordId ?? hit.id, linkType: 'Evidence' }); refresh();
        }} />
    </ModuleWorkspace>
  );
}

export function ValidationList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const q = useQuery({ queryKey: ['validations'], queryFn: () => get<any[]>('/api/validation') });
  return (
    <ModuleWorkspace module={mod('compliance')} crumbs={[{ label: 'Validation' }]} title="Validation"
      actions={can('validation.edit') && <Button variant="contained" onClick={() => nav('/validation/new')}>New validation</Button>}>
      <DataTable data={q.data ?? []} onRowClick={(r) => nav(`/validation/${r.id}`)} empty="No validation projects."
        columns={[{ header: 'Number', accessorKey: 'number', cell: num }, { header: 'Title', accessorKey: 'title' }, { header: 'Kind', accessorKey: 'kind', cell: (x) => validationKinds[x.getValue()] },
          { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={validationStatus[x.getValue()]} /> }, { header: 'Released', accessorKey: 'releasedAt', cell: (x) => fmtDate(x.getValue()) }]} />
    </ModuleWorkspace>
  );
}

export function ValidationNew() {
  const nav = useNavigate();
  const schema = z.object({ title: z.string().min(1, 'Required'), kind: z.coerce.number(), siteId: z.string().min(1, 'Required'), scope: z.string().min(1, 'Required') });
  return (
    <ModuleWorkspace module={mod('compliance')} crumbs={[{ label: 'Validation', to: '/validation' }, { label: 'New' }]} title="New validation">
      <SchemaForm schema={schema} defaults={{ title: '', kind: 0, siteId: '', scope: '' }} submitLabel="Create"
        fields={[{ name: 'title', label: 'Title', full: true }, { name: 'kind', label: 'Kind', type: 'select', options: validationKinds.map((k, i) => [i, k]) }, { name: 'siteId', label: 'Site', type: 'site' }, { name: 'scope', label: 'Scope', type: 'multiline' }]}
        onSubmit={async (v) => { const r = await post<{ id: string }>('/api/validation', v); nav(`/validation/${r.id}`); }} onCancel={() => nav(-1)} />
    </ModuleWorkspace>
  );
}

export function ValidationDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const q = useQuery({ queryKey: ['validation', id], queryFn: () => get<any>(`/api/validation/${id}`) });
  const [rows, setRows] = useState<any[] | null>(null);
  const [dlg, setDlg] = useState<null | { exec: string } | { resolve: string } | 'results'>(null);
  const r = useRunner(() => { qc.invalidateQueries({ queryKey: ['validation', id] }); setRows(null); });
  if (!q.data) return <Loading m="compliance" crumbs={[{ label: 'Validation', to: '/validation' }]} q={q} />;
  const v = q.data.project; const st = v.status;
  const tests: any[] = rows ?? q.data.tests;
  const edit = (i: number, k: string, val: string) => setRows(tests.map((x, j) => (j === i ? { ...x, [k]: val } : x)));
  return (
    <ModuleWorkspace module={mod('compliance')} crumbs={[{ label: 'Validation', to: '/validation' }, { label: v.number }]} title={`${v.number} — ${v.title}`}
      actions={<Stack direction="row" gap={1} alignItems="center">
        {st === 0 && <Button variant="contained" onClick={() => r.run(() => post(`/api/validation/${id}/submit-protocol`))}>Submit protocol for approval</Button>}
        {st === 2 && <Button variant="contained" onClick={() => setDlg('results')}>Submit results</Button>}</Stack>}>
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      <Stack direction="row" sx={{ mb: 2 }}>{['Planning', 'Protocol approval', 'Execution', 'Results approval', 'Released'].map((s, i) => (
        <Box key={s} sx={{ flex: 1, py: 0.75, textAlign: 'center', fontSize: 12.5, bgcolor: i < st ? 'primary.main' : i === st ? 'secondary.main' : 'action.hover', color: i <= st ? 'common.white' : 'text.secondary', clipPath: 'polygon(0 0, 94% 0, 100% 50%, 94% 100%, 0 100%, 6% 50%)' }}>{s}</Box>))}</Stack>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'minmax(0, 1fr) 280px' }, gap: 2 }}>
        <Stack gap={2}>
          <Section title="Protocol tests" action={st === 0 && <Stack direction="row" gap={1}><Button size="small" onClick={() => setRows([...tests, { stage: 'IQ', sequence: tests.length + 1, description: '', acceptanceCriteria: '' }])}>Add test</Button>
            {rows && <Button size="small" variant="contained" onClick={() => r.run(() => put(`/api/validation/${id}/tests`, rows.map((t) => ({ stage: t.stage, sequence: Number(t.sequence), description: t.description, acceptanceCriteria: t.acceptanceCriteria }))))}>Save tests</Button>}</Stack>}>
            <Table size="small"><TableHead><TableRow>{['#', 'Stage', 'Test', 'Acceptance criteria', 'Result', ''].map((h) => <TableCell key={h}>{h}</TableCell>)}</TableRow></TableHead>
              <TableBody>{tests.map((t, i) => (
                <TableRow key={t.id ?? i}>
                  <TableCell>{t.sequence}</TableCell>
                  {rows ? <>
                    <TableCell><TextField select size="small" value={t.stage} onChange={(e) => edit(i, 'stage', e.target.value)} slotProps={{ htmlInput: { 'aria-label': `Stage for test ${t.sequence}` } }}>{['IQ', 'OQ', 'PQ'].map((s) => <MenuItem key={s} value={s}>{s}</MenuItem>)}</TextField></TableCell>
                    <TableCell><TextField size="small" fullWidth placeholder="Test" slotProps={{ htmlInput: { 'aria-label': `Test ${t.sequence}` } }} value={t.description} onChange={(e) => edit(i, 'description', e.target.value)} /></TableCell>
                    <TableCell><TextField size="small" fullWidth placeholder="Acceptance criteria" slotProps={{ htmlInput: { 'aria-label': `Acceptance criteria for test ${t.sequence}` } }} value={t.acceptanceCriteria} onChange={(e) => edit(i, 'acceptanceCriteria', e.target.value)} /></TableCell><TableCell /><TableCell />
                  </> : <>
                    <TableCell>{t.stage}</TableCell><TableCell>{t.description}</TableCell><TableCell>{t.acceptanceCriteria}</TableCell>
                    <TableCell>{t.passed == null ? '—' : <Chip size="small" color={t.passed ? 'success' : 'error'} label={t.passed ? 'Pass' : 'Fail'} />}<Typography variant="caption" display="block">{t.actualResult}</Typography></TableCell>
                    <TableCell>{st === 2 && <Button size="small" onClick={() => setDlg({ exec: t.id })}>Record</Button>}</TableCell></>}
                </TableRow>))}</TableBody></Table>
          </Section>
          <Section title={`Deviations (${q.data.deviations.length})`}>
            {q.data.deviations.length === 0 ? <Typography variant="body2" color="text.secondary">None. A failed test raises a deviation automatically.</Typography> :
              <Table size="small"><TableBody>{q.data.deviations.map((d: any) => (
                <TableRow key={d.id}><TableCell>{d.description}</TableCell><TableCell>{d.resolution ?? <Chip size="small" color="warning" label="Open" />}</TableCell>
                  <TableCell>{!d.resolution && <Button size="small" onClick={() => setDlg({ resolve: d.id })}>Resolve</Button>}</TableCell></TableRow>))}</TableBody></Table>}
          </Section>
        </Stack>
        <Facts items={[['Status', <StatusChip key="s" status={validationStatus[st]} />], ['Kind', validationKinds[v.kind]], ['Scope', v.scope], ['Conclusion', v.conclusion], ['Released', fmtDateTime(v.releasedAt)]]} />
      </Box>
      <StepDialog open={!!dlg && typeof dlg === 'object' && 'exec' in dlg} onClose={() => setDlg(null)} title="Record test result" confirm="Record"
        inputs={[{ name: 'actual', label: 'Actual result', type: 'multiline' }, { name: 'passed', label: 'Meets acceptance criteria', type: 'checkbox', initial: 'true' }]}
        onConfirm={async (x) => { await post(`/api/validation/${id}/tests/${(dlg as any).exec}/execute`, { actualResult: x.actual, passed: x.passed === 'true' }); qc.invalidateQueries({ queryKey: ['validation', id] }); }} />
      <StepDialog open={!!dlg && typeof dlg === 'object' && 'resolve' in dlg} onClose={() => setDlg(null)} title="Resolve deviation" confirm="Resolve"
        inputs={[{ name: 'res', label: 'Resolution', type: 'multiline' }, { name: 'retest', label: 'Retest required', type: 'checkbox', initial: 'true' }]}
        onConfirm={async (x) => { await post(`/api/validation/${id}/deviations/${(dlg as any).resolve}/resolve`, { resolution: x.res, retest: x.retest === 'true' }); qc.invalidateQueries({ queryKey: ['validation', id] }); }} />
      <StepDialog open={dlg === 'results'} onClose={() => setDlg(null)} title="Submit results for approval" confirm="Submit" intro="Approval is e-signed through the configured route; release follows approval."
        inputs={[{ name: 'c', label: 'Conclusion', type: 'multiline' }]} onConfirm={async (x) => { await post(`/api/validation/${id}/submit-results`, { conclusion: x.c }); qc.invalidateQueries({ queryKey: ['validation', id] }); }} />
    </ModuleWorkspace>
  );
}
