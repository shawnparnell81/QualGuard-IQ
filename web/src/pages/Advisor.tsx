import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Alert, Box, Button, Chip, MenuItem, Paper, Stack, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { get, post } from '../api';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { Section, useRunner } from '../components/Kit';
import { mono } from '../theme';
import { SiteSelect, mod, useSite } from './Enterprise';

/* eslint-disable @typescript-eslint/no-explicit-any */
const topics = [['trend', 'Quality trends'], ['risk', 'Risk'], ['supplier', 'Supplier'], ['complaint', 'Complaint'], ['ncr', 'NCR'], ['capa', 'CAPA']];
const needsId = ['supplier', 'complaint', 'ncr', 'capa'];
const A = (label: string) => [{ label: 'Advisor', to: '/advisor' }, { label }];
const Method = ({ m }: { m?: string }) => (m ? <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: 1 }}>{m}</Typography> : null);

function Records({ rs }: { rs: any[] }) {
  const nav = useNavigate();
  return <Stack direction="row" gap={0.5} flexWrap="wrap" sx={{ mt: 1 }}>{rs.map((r, i) => <Chip key={i} size="small" variant="outlined" label={`${r.number} ${r.title ?? ''}`.slice(0, 48)} onClick={() => r.url && nav(r.url)} />)}</Stack>;
}

/** Conversational advisor: chat with follow-up context, and explain-a-record. */
export function AdvisorChat() {
  const [msgs, setMsgs] = useState<{ role: string; text: string; ans?: any }[]>([]);
  const [text, setText] = useState(''); const [topic, setTopic] = useState('trend'); const [rid, setRid] = useState(''); const [exp, setExp] = useState<any>(null);
  const [tab, setTab] = useState('chat');
  const r = useRunner(() => undefined);
  const send = () => r.run(async () => {
    const next = [...msgs, { role: 'user', text }]; setMsgs(next); setText('');
    const ans = await post<any>('/api/advisor/chat', { messages: next.map((m) => ({ role: m.role, text: m.text })) });
    setMsgs([...next, { role: 'assistant', text: ans.answer, ans }]);
  });
  return (
    <ModuleWorkspace module={mod('advisor')} crumbs={[{ label: 'Advisor' }]} title="Quality advisor" tabs={[{ label: 'Ask', value: 'chat' }, { label: 'Explain', value: 'explain' }]} tab={tab} onTab={setTab}>
      <Alert severity="info" sx={{ mb: 2 }}>The advisor is a built-in, rule-based assistant over records you can view. It drafts and explains; it never approves, signs or changes records.</Alert>
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      {tab === 'chat' && <Paper variant="outlined" sx={{ p: 2, maxWidth: 900 }}>
        <Stack gap={1.5} sx={{ minHeight: 240, mb: 2 }} data-testid="chat-log">
          {msgs.length === 0 && <Typography color="text.secondary">Try: "open NCRs this month", then a follow-up like "which supplier?"</Typography>}
          {msgs.map((m, i) => <Box key={i} sx={{ alignSelf: m.role === 'user' ? 'flex-end' : 'flex-start', maxWidth: '80%', bgcolor: m.role === 'user' ? 'primary.main' : 'grey.100', color: m.role === 'user' ? '#fff' : undefined, borderRadius: 2, px: 1.5, py: 1 }}>
            <Typography variant="body2">{m.text}</Typography>{m.ans && <><Records rs={m.ans.records ?? []} /><Method m={m.ans.method} /></>}</Box>)}
        </Stack>
        <Stack direction="row" gap={1}><TextField fullWidth size="small" placeholder="Ask about NCRs, CAPAs, suppliers, complaints, trends" value={text} onChange={(e) => setText(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && text.trim() && send()} />
          <Button variant="contained" disabled={!text.trim()} onClick={send}>Send</Button></Stack>
      </Paper>}
      {tab === 'explain' && <Stack direction={{ xs: 'column', md: 'row' }} gap={2}>
        <Paper variant="outlined" sx={{ p: 2, width: { md: 320 } }}><Stack gap={2}>
          <TextField select size="small" label="Explain" value={topic} onChange={(e) => setTopic(e.target.value)}>{topics.map(([k, l]) => <MenuItem key={k} value={k}>{l}</MenuItem>)}</TextField>
          {needsId.includes(topic) && <TextField size="small" label="Record ID" value={rid} onChange={(e) => setRid(e.target.value)} />}
          <Button variant="contained" onClick={() => r.run(async () => setExp(await get(`/api/advisor/explain/${topic}${needsId.includes(topic) ? `?id=${rid.trim()}` : ''}`)))}>Explain</Button></Stack></Paper>
        <Box sx={{ flex: 1 }} data-testid="explain-result">{exp && <Section title={exp.answer}><Box component="ul" sx={{ pl: 2.5 }}>{exp.points.map((p: string, i: number) => <li key={i}><Typography variant="body2">{p}</Typography></li>)}</Box><Records rs={exp.records} /><Method m={exp.method} /></Section>}</Box>
      </Stack>}
    </ModuleWorkspace>
  );
}

export function DocumentAssistant() {
  const [kind, setKind] = useState('Procedure'); const [title, setTitle] = useState(''); const [text, setText] = useState(''); const [res, setRes] = useState<any>(null);
  const r = useRunner(() => undefined);
  return (
    <ModuleWorkspace module={mod('advisor')} crumbs={A('Document assistant')} title="Document assistant">
      {r.error && <Alert severity="error" sx={{ mb: 2 }}>{r.error}</Alert>}
      <Stack direction={{ xs: 'column', md: 'row' }} gap={2}>
        <Paper variant="outlined" sx={{ p: 2, flex: 1 }}><Stack gap={1.5}>
          <TextField select size="small" label="Document type" value={kind} onChange={(e) => setKind(e.target.value)}>{['Procedure', 'Work instruction', 'Policy', 'Form'].map((k) => <MenuItem key={k} value={k}>{k}</MenuItem>)}</TextField>
          <TextField size="small" label="Title" value={title} onChange={(e) => setTitle(e.target.value)} />
          <TextField multiline minRows={10} label="Paste text to check (leave empty to draft an outline)" value={text} onChange={(e) => setText(e.target.value)} />
          <Button variant="contained" disabled={!title.trim()} onClick={() => r.run(async () => setRes(await post('/api/advisor/documents', { kind, title, text: text || null, documentId: null })))}>{text.trim() ? 'Check consistency' : 'Draft outline'}</Button>
        </Stack></Paper>
        <Box sx={{ flex: 1 }} data-testid="doc-result">{res && <Stack gap={1.5}>
          {res.draft && <Paper variant="outlined" sx={{ p: 2, whiteSpace: 'pre-wrap', fontFamily: mono, fontSize: 13 }}>{res.draft}</Paper>}
          {res.issues.map((i: string, n: number) => <Alert key={n} severity="warning">{i}</Alert>)}
          {res.recommendations.map((i: string, n: number) => <Alert key={n} severity="info">{i}</Alert>)}
          {res.issues.length === 0 && !res.draft && <Alert severity="success">No consistency issues found.</Alert>}<Method m={res.method} /></Stack>}</Box>
      </Stack>
    </ModuleWorkspace>
  );
}

export function AuditPrep() {
  const [site, setSite] = useSite(); const [proc, setProc] = useState('');
  const q = useQuery({ queryKey: ['audit-prep', site, proc], enabled: !!site, queryFn: () => get<any>(`/api/advisor/audit-prep/${site}${proc ? `?process=${encodeURIComponent(proc)}` : ''}`) });
  const d = q.data;
  return (
    <ModuleWorkspace module={mod('advisor')} crumbs={A('Audit preparation')} title="Audit preparation" actions={<Stack direction="row" gap={1}><TextField size="small" label="Process (optional)" value={proc} onChange={(e) => setProc(e.target.value)} /><SiteSelect value={site} onChange={setSite} /></Stack>}>
      {q.error && <Alert severity="error">{(q.error as Error).message}</Alert>}
      {d && <Stack direction={{ xs: 'column', md: 'row' }} gap={2}>
        <Box sx={{ flex: 1 }}><Section title="Risk areas">
          <Stack gap={1}>{d.riskAreas.length === 0 && <Typography variant="body2" color="text.secondary">No audit findings in the last two years.</Typography>}{d.riskAreas.map((a: any, i: number) => <Paper key={i} variant="outlined" sx={{ p: 1, borderLeft: 4, borderColor: 'warning.main' }}><Typography variant="body2" sx={{ fontWeight: 600 }}>{a.area}</Typography><Typography variant="caption">{a.findings} finding(s) · {a.major} major · {a.open} open</Typography></Paper>)}</Stack>
        </Section>
          {d.comparison?.repeatClauses?.length > 0 && <Box sx={{ mt: 2 }}><Section title="Repeat findings by clause"><Stack direction="row" gap={0.5} flexWrap="wrap">{d.comparison.repeatClauses.map((c: any, i: number) => <Chip key={i} color="error" variant="outlined" label={String(c)} />)}</Stack></Section></Box>}</Box>
        <Box sx={{ flex: 1 }}><Section title="Suggested checklist"><Box component="ol" sx={{ pl: 2.5 }} data-testid="prep-checklist">{d.checklist.map((c: any, i: number) => <li key={i}><Typography variant="body2">{c}</Typography></li>)}</Box></Section></Box>
      </Stack>}
    </ModuleWorkspace>
  );
}
