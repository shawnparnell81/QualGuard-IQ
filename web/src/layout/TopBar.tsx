import { useLayoutEffect, useRef, useState } from 'react';
import { Link as RouterLink, useLocation, useNavigate } from 'react-router-dom';
import { AppBar, Badge, Box, Button, IconButton, InputBase, Menu, MenuItem, Toolbar, Tooltip, Typography, Divider, ListItemText } from '@mui/material';
import SearchIcon from '@mui/icons-material/Search';
import ExpandMore from '@mui/icons-material/ExpandMore';
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
  const { me, logout } = useAuth();
  const { data: brand } = useBranding();
  const nav = useNavigate();
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
        <ModuleNav />
        <Box component="form" onSubmit={(e) => { e.preventDefault(); if (q.trim()) nav(`/search?q=${encodeURIComponent(q.trim())}`); }}
          sx={{ display: 'flex', alignItems: 'center', bgcolor: 'rgba(255,255,255,0.12)', borderRadius: 1, px: 1, width: 220, flexShrink: 0, ml: 1 }}>
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
          <MenuItem onClick={() => { setUserEl(null); nav('/account/sign-ins'); }}>Sign-in history</MenuItem>
          <MenuItem onClick={async () => { setUserEl(null); await logout(); nav('/'); }}>Sign out</MenuItem>
        </Menu>
      </Toolbar>
    </AppBar>
  );
}

/** Top module menu. Modules that do not fit the bar move into a "More" menu instead of scrolling out of sight. */
function ModuleNav() {
  const { can } = useAuth();
  const loc = useLocation();
  const mods = visibleModules(can);
  const navRef = useRef<HTMLDivElement>(null);
  const measureRef = useRef<HTMLDivElement>(null);
  const [fit, setFit] = useState(mods.length);
  const [moreEl, setMoreEl] = useState<HTMLElement | null>(null);
  useLayoutEffect(() => {
    const nav = navRef.current, measure = measureRef.current;
    if (!nav || !measure) return;
    const calc = () => {
      const widths = Array.from(measure.children).map((c) => (c as HTMLElement).offsetWidth + 4);
      const avail = nav.clientWidth;
      const total = widths.reduce((a, b) => a + b, 0);
      if (total <= avail) { setFit(widths.length); return; }
      let used = 96, n = 0;
      while (n < widths.length && used + widths[n] <= avail) used += widths[n++];
      setFit(Math.max(1, n));
    };
    calc();
    const ro = new ResizeObserver(calc);
    ro.observe(nav);
    return () => ro.disconnect();
  }, [mods.length]);
  const isActive = (m: (typeof mods)[number]) => {
    const roots = [m.basePath, ...m.sidebar.flatMap((i) => [i.path, ...(i.children ?? []).map((c) => c.path)])].map((x) => '/' + x.split(/[/?]/)[1]);
    return roots.some((r) => loc.pathname === r || loc.pathname.startsWith(r + '/'));
  };
  const btn = (m: (typeof mods)[number], active: boolean) => (
    <Button key={m.key} component={RouterLink} to={m.basePath} startIcon={m.icon} color="inherit" size="small"
      sx={{ px: 1.25, whiteSpace: 'nowrap', flexShrink: 0, opacity: active ? 1 : 0.85, borderBottom: active ? '2px solid' : '2px solid transparent', borderColor: active ? 'secondary.light' : 'transparent', borderRadius: 0 }}>
      {m.label}
    </Button>
  );
  const shown = mods.slice(0, fit), rest = mods.slice(fit);
  const restActive = rest.find(isActive);
  return (
    <Box ref={navRef} component="nav" aria-label="Modules" sx={{ display: 'flex', gap: 0.5, flex: '1 1 auto', minWidth: 0, position: 'relative', overflow: 'hidden' }}>
      <Box ref={measureRef} aria-hidden sx={{ position: 'absolute', visibility: 'hidden', display: 'flex', gap: 0.5, pointerEvents: 'none' }}>{mods.map((m) => btn(m, false))}</Box>
      {shown.map((m) => btn(m, isActive(m)))}
      {rest.length > 0 && <>
        <Button color="inherit" size="small" endIcon={<ExpandMore />} onClick={(e) => setMoreEl(e.currentTarget)} aria-label="More modules"
          sx={{ px: 1.25, whiteSpace: 'nowrap', flexShrink: 0, borderRadius: 0, borderBottom: '2px solid', borderColor: restActive ? 'secondary.light' : 'transparent' }}>{restActive ? restActive.label : 'More'}</Button>
        <Menu anchorEl={moreEl} open={!!moreEl} onClose={() => setMoreEl(null)}>
          {rest.map((m) => <MenuItem key={m.key} component={RouterLink} to={m.basePath} onClick={() => setMoreEl(null)} selected={isActive(m)} sx={{ gap: 1.5 }}>{m.icon}<ListItemText primary={m.label} /></MenuItem>)}
        </Menu>
      </>}
    </Box>
  );
}
