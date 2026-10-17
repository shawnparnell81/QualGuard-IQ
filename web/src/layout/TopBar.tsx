import { useState } from 'react';
import { Link as RouterLink, useLocation, useNavigate } from 'react-router-dom';
import { AppBar, Badge, Box, Button, IconButton, InputBase, Menu, MenuItem, Toolbar, Tooltip, Typography, Divider, ListItemText } from '@mui/material';
import SearchIcon from '@mui/icons-material/Search';
import NotificationsNoneOutlined from '@mui/icons-material/NotificationsNoneOutlined';
import AssignmentIndOutlined from '@mui/icons-material/AssignmentIndOutlined';
import AccountCircleOutlined from '@mui/icons-material/AccountCircleOutlined';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useAuth, useBranding } from '../auth';
import { visibleModules } from '../modules/registry';
import { get, post } from '../api';
import { mono } from '../theme';

interface Notif { id: number; title: string; body: string | null; link: string | null; createdAt: string; readAt: string | null }

export function TopBar({ onOpenPalette }: { onOpenPalette: () => void }) {
  const { me, can, logout } = useAuth();
  const { data: brand } = useBranding();
  const nav = useNavigate();
  const loc = useLocation();
  const qc = useQueryClient();
  const [q, setQ] = useState('');
  const [userEl, setUserEl] = useState<HTMLElement | null>(null);
  const [notifEl, setNotifEl] = useState<HTMLElement | null>(null);
  const notifs = useQuery({ queryKey: ['notifications'], queryFn: () => get<{ unread: number; items: Notif[] }>('/api/my/notifications'), refetchInterval: 60_000 });

  const openNotif = async (n: Notif) => {
    setNotifEl(null);
    if (!n.readAt) { await post(`/api/my/notifications/${n.id}/read`); qc.invalidateQueries({ queryKey: ['notifications'] }); }
    if (n.link) nav(n.link);
  };

  return (
    <AppBar position="sticky" color="primary" elevation={0}>
      <Toolbar variant="dense" sx={{ gap: 1, minHeight: 52 }}>
        <Box component={RouterLink} to="/" sx={{ display: 'flex', alignItems: 'center', gap: 1, color: 'inherit', textDecoration: 'none', mr: 2 }}>
          <Box sx={{ width: 26, height: 26, borderRadius: '6px', bgcolor: 'secondary.main', display: 'grid', placeItems: 'center', fontFamily: mono, fontWeight: 600, fontSize: 13 }}>
            {(brand?.applicationName ?? 'Q').slice(0, 1)}
          </Box>
          <Typography sx={{ fontWeight: 600, letterSpacing: '0.01em', whiteSpace: 'nowrap' }}>{brand?.applicationName}</Typography>
        </Box>
        <Box component="nav" sx={{ display: 'flex', gap: 0.5, flexWrap: 'nowrap', overflowX: 'auto' }}>
          {visibleModules(can).map((m) => {
            const roots = [m.basePath, ...m.sidebar.flatMap((i) => [i.path, ...(i.children ?? []).map((c) => c.path)])].map((x) => '/' + x.split(/[/?]/)[1]);
            const active = roots.some((r) => loc.pathname === r || loc.pathname.startsWith(r + '/'));
            return (
              <Button key={m.key} component={RouterLink} to={m.basePath} startIcon={m.icon} color="inherit" size="small"
                sx={{ px: 1.25, whiteSpace: 'nowrap', flexShrink: 0, opacity: active ? 1 : 0.85, borderBottom: active ? '2px solid' : '2px solid transparent', borderColor: active ? 'secondary.light' : 'transparent', borderRadius: 0 }}>
                {m.label}
              </Button>
            );
          })}
        </Box>
        <Box sx={{ flex: 1 }} />
        <Box component="form" onSubmit={(e) => { e.preventDefault(); if (q.trim()) nav(`/search?q=${encodeURIComponent(q.trim())}`); }}
          sx={{ display: 'flex', alignItems: 'center', bgcolor: 'rgba(255,255,255,0.12)', borderRadius: 1, px: 1, width: 240, flexShrink: 0 }}>
          <SearchIcon fontSize="small" sx={{ opacity: 0.8 }} />
          <InputBase value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search records" sx={{ color: 'inherit', ml: 1, flex: 1, fontSize: 13 }} inputProps={{ 'aria-label': 'Search records' }} />
          <Tooltip title="Command palette"><Box component="button" type="button" onClick={onOpenPalette}
            sx={{ fontFamily: mono, fontSize: 11, border: '1px solid rgba(255,255,255,0.4)', borderRadius: 0.5, px: 0.6, color: 'inherit', bgcolor: 'transparent', cursor: 'pointer' }}>Ctrl K</Box></Tooltip>
        </Box>
        <Tooltip title="My Work"><IconButton color="inherit" component={RouterLink} to="/my-work"><AssignmentIndOutlined /></IconButton></Tooltip>
        <Tooltip title="Notifications">
          <IconButton color="inherit" onClick={(e) => setNotifEl(e.currentTarget)}>
            <Badge badgeContent={notifs.data?.unread ?? 0} color="secondary"><NotificationsNoneOutlined /></Badge>
          </IconButton>
        </Tooltip>
        <Menu anchorEl={notifEl} open={!!notifEl} onClose={() => setNotifEl(null)} slotProps={{ paper: { sx: { width: 380, maxHeight: 480 } } }}>
          {(notifs.data?.items ?? []).length === 0 && <MenuItem disabled>No notifications</MenuItem>}
          {(notifs.data?.items ?? []).slice(0, 15).map((n) => (
            <MenuItem key={n.id} onClick={() => openNotif(n)} sx={{ whiteSpace: 'normal', fontWeight: n.readAt ? 400 : 600 }}>
              <ListItemText primary={n.title} secondary={`${n.body ?? ''} · ${new Date(n.createdAt).toLocaleString()}`} />
            </MenuItem>
          ))}
          <Divider />
          <MenuItem onClick={async () => { await post('/api/my/notifications/read-all'); qc.invalidateQueries({ queryKey: ['notifications'] }); }}>Mark all as read</MenuItem>
        </Menu>
        <IconButton color="inherit" onClick={(e) => setUserEl(e.currentTarget)} aria-label="User menu"><AccountCircleOutlined /></IconButton>
        <Menu anchorEl={userEl} open={!!userEl} onClose={() => setUserEl(null)}>
          <MenuItem disabled><ListItemText primary={me?.displayName} secondary={me?.email} /></MenuItem>
          <Divider />
          <MenuItem onClick={async () => { setUserEl(null); await logout(); nav('/'); }}>Sign out</MenuItem>
        </Menu>
      </Toolbar>
    </AppBar>
  );
}
