import { useEffect, useState, type ReactNode } from 'react';
import { Link as RouterLink, useLocation } from 'react-router-dom';
import { Box, Breadcrumbs, Collapse, IconButton, Link, List, ListItemButton, ListItemText, Tab, Tabs, Tooltip, Typography, useMediaQuery } from '@mui/material';
import ChevronLeft from '@mui/icons-material/ChevronLeft';
import ChevronRight from '@mui/icons-material/ChevronRight';
import ExpandLess from '@mui/icons-material/ExpandLess';
import ExpandMore from '@mui/icons-material/ExpandMore';
import { useAuth } from '../auth';
import type { ModuleDef, NavItem } from '../modules/registry';

export interface Crumb { label: string; to?: string }
export interface WorkspaceTab { label: string; value: string }

/**
 * A module's own workspace: collapsible left menu with that module's submenus,
 * breadcrumbs, and a secondary tab bar for the current page's options.
 */
export function ModuleWorkspace({ module, crumbs, title, actions, tabs, tab, onTab, children }: {
  module: ModuleDef; crumbs: Crumb[]; title: ReactNode; actions?: ReactNode;
  tabs?: WorkspaceTab[]; tab?: string; onTab?: (v: string) => void; children: ReactNode;
}) {
  const [stored, setStored] = useState(() => localStorage.getItem('ws.collapsed') === '1');
  // On phones and small tablets the menu starts folded and opens over the page, so scan screens get the full width.
  const narrow = useMediaQuery('(max-width:899.95px)');
  const [peek, setPeek] = useState(false);
  const loc = useLocation();
  useEffect(() => setPeek(false), [loc.pathname, loc.search]);
  const collapsed = narrow ? !peek : stored;
  const toggle = () => { if (narrow) { setPeek(!peek); return; } localStorage.setItem('ws.collapsed', stored ? '0' : '1'); setStored(!stored); };
  return (
    <Box sx={{ display: 'flex', minHeight: 'calc(100vh - 52px)', position: 'relative' }}>
      {narrow && <Box sx={{ width: 48, flexShrink: 0 }} />}
      <Box component="aside" sx={{ width: collapsed ? 48 : 248, flexShrink: 0, borderRight: 1, borderColor: 'divider', bgcolor: 'background.sidebar', transition: 'width 150ms',
        ...(narrow ? { position: 'absolute', top: 0, bottom: 0, left: 0, zIndex: 20, boxShadow: peek ? 6 : 0 } : {}) }}>
        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: collapsed ? 'center' : 'space-between', px: collapsed ? 0 : 2, py: 1.5 }}>
          {!collapsed && <Typography variant="overline" color="text.secondary" sx={{ display: 'flex', gap: 1, alignItems: 'center' }}>{module.icon}{module.label}</Typography>}
          <Tooltip title={collapsed ? 'Expand menu' : 'Collapse menu'}><IconButton size="small" onClick={toggle}>{collapsed ? <ChevronRight /> : <ChevronLeft />}</IconButton></Tooltip>
        </Box>
        {!collapsed && <SideNav items={module.sidebar} />}
      </Box>
      <Box component="main" sx={{ flex: 1, minWidth: 0 }}>
        <Box sx={{ px: { xs: 2, md: 4 }, pt: 2.5, bgcolor: 'background.paper', borderBottom: 1, borderColor: 'divider' }}>
          <Breadcrumbs sx={{ fontSize: 12.5, mb: 1 }}>
            <Link component={RouterLink} to="/" underline="hover" color="inherit">Home</Link>
            <Link component={RouterLink} to={module.basePath} underline="hover" color="inherit">{module.label}</Link>
            {crumbs.filter((c, i) => !(i === 0 && c.label === module.label)).map((c, i) => c.to ? <Link key={i} component={RouterLink} to={c.to} underline="hover" color="inherit">{c.label}</Link> : <Typography key={i} fontSize={12.5} color="text.primary">{c.label}</Typography>)}
          </Breadcrumbs>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, pb: tabs ? 0 : 2, flexWrap: 'wrap' }}>
            <Typography variant="h1" sx={{ flex: 1 }}>{title}</Typography>
            {actions}
          </Box>
          {tabs && (
            <Tabs value={tab} onChange={(_, v) => onTab?.(v)} variant="scrollable" sx={{ mt: 1, minHeight: 40, '& .MuiTab-root': { minHeight: 40 } }}>
              {tabs.map((t) => <Tab key={t.value} value={t.value} label={t.label} />)}
            </Tabs>
          )}
        </Box>
        <Box sx={{ p: { xs: 1.5, sm: 2.5, md: 4 } }}>{children}</Box>
      </Box>
    </Box>
  );
}

function SideNav({ items, depth = 0 }: { items: NavItem[]; depth?: number }) {
  const { can } = useAuth();
  const loc = useLocation();
  const current = loc.pathname + loc.search;
  return (
    <List dense disablePadding>
      {items.filter((i) => !i.permission || can(i.permission)).map((i) => <SideItem key={i.label} item={i} depth={depth} current={current} />)}
    </List>
  );
}

function SideItem({ item, depth, current }: { item: NavItem; depth: number; current: string }) {
  const [open, setOpen] = useState(true);
  const selected = current === item.path;
  if (item.children) {
    return (
      <>
        <ListItemButton onClick={() => setOpen(!open)} sx={{ pl: 2 + depth * 2 }}>
          <ListItemText primary={item.label} slotProps={{ primary: { fontWeight: 600, fontSize: 13 } }} />
          {open ? <ExpandLess fontSize="small" /> : <ExpandMore fontSize="small" />}
        </ListItemButton>
        <Collapse in={open}><SideNav items={item.children} depth={depth + 1} /></Collapse>
      </>
    );
  }
  return (
    <ListItemButton component={RouterLink} to={item.path} selected={selected}
      sx={{ pl: 2 + depth * 2, borderLeft: '3px solid', borderColor: selected ? 'secondary.main' : 'transparent', '&.Mui-selected': { bgcolor: 'action.selected' } }}>
      <ListItemText primary={item.label} slotProps={{ primary: { fontSize: 13 } }} />
    </ListItemButton>
  );
}
