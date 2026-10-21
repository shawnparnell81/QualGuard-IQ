import { useState } from 'react';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { Alert, Box, Button, Chip, Link, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { get, post } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { Facts, Section, StepDialog, useRunner } from '../components/Kit';
import { StatusChip, fmtDateTime } from '../components/Status';
import { FormView } from '../forms/FormView';
import { parseDefinition } from '../forms/definition';
import { mono } from '../theme';

interface Candidate { documentId: string; number: string; title: string; siteCode: string; revision: string; revisionId: string; schemaVersion: number; fieldCount: number; recordCount: number; openMigrationId: string | null; openMigrationNumber: string | null }
interface Summary { id: string; number: string; documentId: string; documentNumber: string; title: string; sourceRevision: string; targetRevision: string; fromSchemaVersion: number; toSchemaVersion: number; status: number; fieldCount: number; changedCount: number; recordCount: number; createdAt: string; createdBy: string }
interface Facts2 { section: string | null; required: boolean; formula: string | null; options: string[] | null; unit: string | null; min: number | null; max: number | null; conditional: boolean }
interface FieldDiff { key: string; label: string; type: string; change: 'unchanged' | 'placed' | 'changed' | 'removed' | 'added'; before: Facts2 | null; after: Facts2 | null }
interface Comparison { fromSchemaVersion: number; toSchemaVersion: number; sections: { key: string; title: string; fields: number }[]; summary: Record<string, number>; fields: FieldDiff[] }
interface Detail { summary: Summary; sourceRevisionId: string; targetRevisionId: string; targetRevisionStatus: number; comparison: Comparison; snapshotSha256: string; snapshotIntact: boolean; rolledBackAt: string | null; rolledBackBy: string | null; rollbackReason: string | null; rollbackRevisionId: string | null; rollbackRevision: string | null; allowed: string[] }

const STATUS = ['Draft', 'In review', 'Approved', 'Effective', 'Rolled back'];
const CHANGE: Record<FieldDiff['change'], string> = { unchanged: 'Unchanged', placed: 'Kept, placed in a section', changed: 'Changed', removed: 'Removed', added: 'Added' };
const docs = () => modules.find((m) => m.key === 'documents')!;
const crumbs = [{ label: 'Template library', to: '/forms/library' }, { label: 'Schema migration', to: '/forms/migration' }];

/** Opt-in form schema migration: templates on an earlier schema, and the history of migrations. Nothing is converted until someone chooses to. */
export function FormMigrationList() {
  const { can } = useAuth();
  const nav = useNavigate();
  const [tab, setTab] = useState('candidates');
  const [pick, setPick] = useState<Candidate | null>(null);
  const cands = useQuery({ queryKey: ['form-migration-candidates'], queryFn: () => get<Candidate[]>('/api/v1/form-migrations/candidates') });
  const hist = useQuery({ queryKey: ['form-migrations'], queryFn: () => get<Summary[]>('/api/v1/form-migrations'), enabled: tab === 'history' });
  const head = (labels: string[]) => <TableHead><TableRow>{labels.map((l, i) => <TableCell key={l + i} sx={{ fontWeight: 600 }} align={/Fields|Records|Changed/.test(l) ? 'right' : undefined}>{l}</TableCell>)}</TableRow></TableHead>;
  return (
    <ModuleWorkspace module={docs()} crumbs={[crumbs[0], { label: 'Schema migration' }]} title="Form schema migration"
      tabs={[{ label: 'Templates to migrate', value: 'candidates' }, { label: 'Migration history', value: 'history' }]} tab={tab} onTab={setTab}>
      {tab === 'candidates' && <>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          These effective templates use the earlier form schema. They keep working as they are; nothing is converted automatically. Migrating one keeps the current revision unchanged,
          saves a snapshot of it, and puts the converted definition in a new draft revision with a field-by-field comparison. The new revision is approved through the normal route,
          records already filled stay on the revision they were filled against, and the migration can be rolled back.
        </Typography>
        <Paper variant="outlined" sx={{ overflowX: 'auto' }}>
          <Table size="small" aria-label="Templates to migrate">
            {head(['Template', 'Title', 'Site', 'Revision', 'Schema', 'Fields', 'Records', ''])}
            <TableBody>
              {(cands.data ?? []).map((c) => <TableRow key={c.documentId} hover>
                <TableCell><Link component={RouterLink} to={`/forms/templates/${c.documentId}`} sx={{ fontFamily: mono }}>{c.number}</Link></TableCell>
                <TableCell>{c.title}</TableCell><TableCell>{c.siteCode}</TableCell><TableCell>{c.revision}</TableCell><TableCell>Version {c.schemaVersion}</TableCell>
                <TableCell align="right">{c.fieldCount}</TableCell><TableCell align="right">{c.recordCount}</TableCell>
                <TableCell align="right">{c.openMigrationId
                  ? <Button size="small" component={RouterLink} to={`/forms/migration/${c.openMigrationId}`}>Open {c.openMigrationNumber}</Button>
                  : can('forms.edit') && <Button size="small" variant="outlined" onClick={() => setPick(c)}>Migrate</Button>}</TableCell>
              </TableRow>)}
              {cands.isSuccess && !cands.data.length && <TableRow><TableCell colSpan={8} sx={{ color: 'text.secondary' }}>Every effective template you can see uses the current form schema.</TableCell></TableRow>}
            </TableBody>
          </Table>
        </Paper>
      </>}
      {tab === 'history' && <Paper variant="outlined" sx={{ overflowX: 'auto' }}>
        <Table size="small" aria-label="Migration history">
          {head(['Migration', 'Template', 'From', 'To', 'Status', 'Fields', 'Changed', 'Records', 'Started'])}
          <TableBody>
            {(hist.data ?? []).map((m) => <TableRow key={m.id} hover sx={{ cursor: 'pointer' }} onClick={() => nav(`/forms/migration/${m.id}`)}>
              <TableCell sx={{ fontFamily: mono }}>{m.number}</TableCell><TableCell>{m.documentNumber} · {m.title}</TableCell>
              <TableCell>Rev {m.sourceRevision} (v{m.fromSchemaVersion})</TableCell><TableCell>Rev {m.targetRevision} (v{m.toSchemaVersion})</TableCell>
              <TableCell><StatusChip status={STATUS[m.status]} /></TableCell><TableCell align="right">{m.fieldCount}</TableCell><TableCell align="right">{m.changedCount}</TableCell>
              <TableCell align="right">{m.recordCount}</TableCell><TableCell>{fmtDateTime(m.createdAt)} · {m.createdBy}</TableCell>
            </TableRow>)}
            {hist.isSuccess && !hist.data.length && <TableRow><TableCell colSpan={9} sx={{ color: 'text.secondary' }}>No templates have been migrated.</TableCell></TableRow>}
          </TableBody>
        </Table>
      </Paper>}
      <StepDialog open={!!pick} title={`Migrate ${pick?.number ?? ''}`} confirm="Start migration" onClose={() => setPick(null)}
        intro={pick ? `Revision ${pick.revision} stays effective and unchanged until the migrated revision is approved. ${pick.recordCount} record${pick.recordCount === 1 ? '' : 's'} filled against it stay on revision ${pick.revision}.` : undefined}
        inputs={[{ name: 'changeSummary', label: 'Change summary (optional)', type: 'multiline' }]}
        onConfirm={async (v) => { const d = await post<Detail>('/api/v1/form-migrations', { documentId: pick!.documentId, changeSummary: v.changeSummary || null }); nav(`/forms/migration/${d.summary.id}`); }} />
    </ModuleWorkspace>
  );
}

function SchemaPreview({ documentId, revisionId, title }: { documentId: string; revisionId: string; title: string }) {
  const s = useQuery({ queryKey: ['form-template-schema', revisionId], queryFn: () => get<{ revision: string; schemaVersion: number; definition: unknown }>(`/api/v1/form-templates/${documentId}/revisions/${revisionId}/schema`) });
  return (
    <Paper variant="outlined" sx={{ p: 2, minWidth: 0 }} aria-label={title}>
      <Typography variant="overline" color="text.secondary">{title}{s.data ? ` · schema version ${s.data.schemaVersion}` : ''}</Typography>
      {s.data && <FormView def={parseDefinition(s.data.definition as object)} values={{}} onChange={() => undefined} readOnly />}
    </Paper>
  );
}

const show = (f: Facts2 | null, k: keyof Facts2) => {
  if (!f) return '—';
  const v = f[k];
  if (v === null || v === undefined || v === '') return '—';
  if (Array.isArray(v)) return v.join(', ');
  if (typeof v === 'boolean') return v ? 'Yes' : 'No';
  return String(v);
};

/** One migration: what was converted, the side-by-side preview, the field comparison, approval and rollback. */
export function FormMigrationDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const [dlg, setDlg] = useState(false);
  const q = useQuery({ queryKey: ['form-migration', id], queryFn: () => get<Detail>(`/api/v1/form-migrations/${id}`) });
  const { error, run } = useRunner(() => { qc.invalidateQueries({ queryKey: ['form-migration', id] }); qc.invalidateQueries({ queryKey: ['form-migration-candidates'] }); });
  const d = q.data;
  if (!d) return <ModuleWorkspace module={docs()} crumbs={crumbs} title="Migration">{q.error && <Alert severity="error">{(q.error as Error).message}</Alert>}</ModuleWorkspace>;
  const m = d.summary;
  const c = d.comparison;
  const kept = (a: string, b: string) => `${c.summary[a] ?? 0} of ${c.summary[b] ?? 0}`;
  return (
    <ModuleWorkspace module={docs()} crumbs={[...crumbs, { label: m.number }]}
      title={<span><span style={{ fontFamily: mono }}>{m.number}</span> · {m.documentNumber} {m.title}</span>}
      actions={<Stack direction="row" gap={1}>
        <Button component={RouterLink} to={`/forms/templates/${m.documentId}`}>Open template</Button>
        {d.allowed.includes('submit') && <Button variant="contained" onClick={() => run(() => post(`/api/v1/form-migrations/${id}/submit`))}>Submit for approval</Button>}
        {d.allowed.includes('rollback') && <Button color="error" variant="outlined" onClick={() => setDlg(true)}>Roll back</Button>}
      </Stack>}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {m.status === 1 && <Alert severity="info" sx={{ mb: 2 }}>Revision {m.targetRevision} is in review. It becomes effective when the approval route is complete; until then revision {m.sourceRevision} stays in use.</Alert>}
      {m.status === 4 && <Alert severity="warning" sx={{ mb: 2 }}>Rolled back {d.rolledBackAt ? fmtDateTime(d.rolledBackAt) : ''}{d.rolledBackBy ? ` by ${d.rolledBackBy}` : ''}: {d.rollbackReason}.
        {d.rollbackRevision ? ` Revision ${d.rollbackRevision} restores the original definition and goes through approval like any revision.` : ` Draft revision ${m.targetRevision} holds the original definition again.`}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', lg: '2fr 1fr' }, gap: 2, mb: 2 }}>
        <Section title="Conversion summary">
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(4, 1fr)' }, gap: 1.5 }}>
            {([['Fields', String(c.summary.fields ?? 0)], ['Changed', String(c.summary.changed ?? 0)], ['Formulas kept', kept('formulasKept', 'formulas')], ['Option lists kept', kept('optionListsKept', 'optionLists')],
              ['Required kept', kept('requiredKept', 'required')], ['Conditions kept', kept('conditionsKept', 'conditions')], ['Sections', String(c.sections.length)], ['Records on the original', String(m.recordCount)]] as [string, string][]).map(([k, v]) =>
              <Paper key={k} variant="outlined" sx={{ p: 1.5 }}><Typography variant="caption" color="text.secondary">{k}</Typography><Typography variant="h3" component="p">{v}</Typography></Paper>)}
          </Box>
        </Section>
        <Facts items={[
          ['Status', <StatusChip key="s" status={STATUS[m.status]} />],
          ['Original', `Revision ${m.sourceRevision} · schema version ${m.fromSchemaVersion} (unchanged)`],
          ['Migrated', `Revision ${m.targetRevision} · schema version ${m.toSchemaVersion}`],
          ['Snapshot', <span key="h"><span style={{ fontFamily: mono, wordBreak: 'break-all' }}>{d.snapshotSha256.slice(0, 16)}…</span> <Chip size="small" color={d.snapshotIntact ? 'success' : 'error'} label={d.snapshotIntact ? 'Matches the original' : 'Does not match'} /></span>],
          ['Started', `${fmtDateTime(m.createdAt)} · ${m.createdBy}`],
        ]} />
      </Box>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', lg: '1fr 1fr' }, gap: 2, mb: 2 }}>
        <SchemaPreview documentId={m.documentId} revisionId={d.sourceRevisionId} title={`Original · revision ${m.sourceRevision}`} />
        <SchemaPreview documentId={m.documentId} revisionId={d.targetRevisionId} title={`Migrated · revision ${m.targetRevision}`} />
      </Box>
      <Section title="Field comparison">
        <Paper variant="outlined" sx={{ overflowX: 'auto' }}>
          <Table size="small" aria-label="Field comparison">
            <TableHead><TableRow>{['Field', 'Type', 'Result', 'Section', 'Required', 'Formula', 'Options', 'Conditional'].map((h) => <TableCell key={h} sx={{ fontWeight: 600 }}>{h}</TableCell>)}</TableRow></TableHead>
            <TableBody>{c.fields.map((f) => <TableRow key={f.key}>
              <TableCell><Typography variant="body2" fontWeight={600}>{f.label}</Typography><Typography variant="caption" color="text.secondary" sx={{ fontFamily: mono }}>{f.key}</Typography></TableCell>
              <TableCell>{f.type}</TableCell>
              <TableCell><Chip size="small" variant="outlined" color={f.change === 'changed' || f.change === 'removed' ? 'warning' : 'success'} label={CHANGE[f.change]} /></TableCell>
              <TableCell>{show(f.before, 'section') === '—' ? 'None' : show(f.before, 'section')} → {show(f.after, 'section')}</TableCell>
              <TableCell>{show(f.after ?? f.before, 'required')}</TableCell>
              <TableCell sx={{ fontFamily: mono }}>{show(f.after ?? f.before, 'formula')}</TableCell>
              <TableCell>{show(f.after ?? f.before, 'options')}</TableCell>
              <TableCell>{show(f.after ?? f.before, 'conditional')}</TableCell>
            </TableRow>)}</TableBody>
          </Table>
        </Paper>
      </Section>
      <StepDialog open={dlg} danger title={`Roll back ${m.number}`} confirm="Roll back" onClose={() => setDlg(false)}
        intro={m.status === 0 ? `Draft revision ${m.targetRevision} gets the original definition of revision ${m.sourceRevision} back.` : `A new draft revision restoring the definition of revision ${m.sourceRevision} is started and goes through approval. Records keep the revision they were filled against.`}
        inputs={[{ name: 'reason', label: 'Reason', type: 'multiline' }]}
        onConfirm={async (v) => { await post(`/api/v1/form-migrations/${id}/rollback`, { reason: v.reason ?? '' }); qc.invalidateQueries({ queryKey: ['form-migration', id] }); qc.invalidateQueries({ queryKey: ['form-migration-candidates'] }); }} />
    </ModuleWorkspace>
  );
}
