import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Dialog, InputBase, List, ListItemButton, ListItemText, Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useAuth } from '../auth';
import { visibleModules, type NavItem } from '../modules/registry';
import { get } from '../api';

interface Hit { recordType: string; recordId: string; number: string; title: string; status: string | null; url: string }
interface Cmd { label: string; hint: string; to: string }

/** Ctrl+K: jump to any page the user can open, or to a record by number or title. */
export function CommandPalette({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { can } = useAuth();
  const nav = useNavigate();
  const [q, setQ] = useState('');
  const [idx, setIdx] = useState(0);
  useEffect(() => { if (open) { setQ(''); setIdx(0); } }, [open]);

  const pages = useMemo(() => {
    const out: Cmd[] = [{ label: 'Home', hint: 'Page', to: '/' }, { label: 'My Work', hint: 'Page', to: '/my-work' }];
    const walk = (items: NavItem[], mod: string) => items.forEach((i) => {
      if (i.permission && !can(i.permission)) return;
      if (i.children) walk(i.children, mod); else out.push({ label: i.label, hint: mod, to: i.path });
    });
    visibleModules(can).forEach((m) => walk(m.sidebar, m.label));
    return out;
  }, [can]);

  const records = useQuery({ queryKey: ['palette', q], queryFn: () => get<Hit[]>(`/api/search?q=${encodeURIComponent(q)}&limit=8`), enabled: q.trim().length >= 2 });
  const results: Cmd[] = [
    ...pages.filter((p) => p.label.toLowerCase().includes(q.toLowerCase())).slice(0, 8),
    ...(records.data ?? []).map((h) => ({ label: `${h.number}  ${h.title}`, hint: h.recordType, to: h.url })),
  ];
  const go = (c?: Cmd) => { if (c) { onClose(); nav(c.to); } };

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm" slotProps={{ paper: { sx: { position: 'absolute', top: 80 } } }}>
      <InputBase autoFocus fullWidth value={q} placeholder="Go to a page or record..." onChange={(e) => { setQ(e.target.value); setIdx(0); }}
        onKeyDown={(e) => {
          if (e.key === 'ArrowDown') { e.preventDefault(); setIdx((i) => Math.min(i + 1, results.length - 1)); }
          if (e.key === 'ArrowUp') { e.preventDefault(); setIdx((i) => Math.max(i - 1, 0)); }
          if (e.key === 'Enter') go(results[idx]);
        }}
        sx={{ px: 2.5, py: 1.75, fontSize: 16, borderBottom: 1, borderColor: 'divider' }} />
      <List dense sx={{ maxHeight: 380, overflow: 'auto' }}>
        {results.map((r, i) => (
          <ListItemButton key={r.to + r.label} selected={i === idx} onClick={() => go(r)}>
            <ListItemText primary={r.label} />
            <Typography variant="caption" color="text.secondary">{r.hint}</Typography>
          </ListItemButton>
        ))}
        {results.length === 0 && <Box sx={{ p: 2, color: 'text.secondary' }}>No matches</Box>}
      </List>
    </Dialog>
  );
}
