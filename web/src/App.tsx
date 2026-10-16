import { useEffect, useMemo, useState } from 'react';
import { Route, Routes } from 'react-router-dom';
import { Box, CircularProgress, CssBaseline, ThemeProvider } from '@mui/material';
import { useAuth, useBranding } from './auth';
import { makeTheme } from './theme';
import { TopBar } from './layout/TopBar';
import { CommandPalette } from './layout/CommandPalette';
import { Login } from './pages/Login';
import { Home } from './pages/Home';
import { MyWork } from './pages/MyWork';
import { Search } from './pages/Search';
import { DocumentDetail, DocumentList, DocumentNew } from './pages/Documents';
import { AdminHome, AuditPage, OrganizationPage, RolesPage, RoutesPage, SecurityPage, SitesPage, UsersPage } from './pages/Admin';

export function App() {
  const { me, loading } = useAuth();
  const { data: brand } = useBranding();
  const theme = useMemo(() => makeTheme(brand?.primaryColor), [brand?.primaryColor]);
  const [palette, setPalette] = useState(false);
  useEffect(() => { if (brand) document.title = brand.applicationName; }, [brand]);
  useEffect(() => {
    const h = (e: KeyboardEvent) => { if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') { e.preventDefault(); setPalette((p) => !p); } };
    window.addEventListener('keydown', h);
    return () => window.removeEventListener('keydown', h);
  }, []);
  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      {loading ? <Box sx={{ display: 'grid', placeItems: 'center', height: '100vh' }}><CircularProgress /></Box>
        : !me ? <Login /> : (
          <>
            <TopBar onOpenPalette={() => setPalette(true)} />
            <CommandPalette open={palette} onClose={() => setPalette(false)} />
            <Routes>
              <Route path="/" element={<Home />} />
              <Route path="/my-work" element={<MyWork />} />
              <Route path="/search" element={<Search />} />
              <Route path="/documents" element={<DocumentList kind={0} />} />
              <Route path="/documents/new" element={<DocumentNew kind={0} />} />
              <Route path="/documents/:id" element={<DocumentDetail kind={0} />} />
              <Route path="/drawings" element={<DocumentList kind={1} />} />
              <Route path="/drawings/new" element={<DocumentNew kind={1} />} />
              <Route path="/drawings/:id" element={<DocumentDetail kind={1} />} />
              <Route path="/admin" element={<AdminHome />} />
              <Route path="/admin/users" element={<UsersPage />} />
              <Route path="/admin/roles" element={<RolesPage />} />
              <Route path="/admin/sites" element={<SitesPage />} />
              <Route path="/admin/routes" element={<RoutesPage />} />
              <Route path="/admin/organization" element={<OrganizationPage />} />
              <Route path="/admin/audit" element={<AuditPage />} />
              <Route path="/admin/security" element={<SecurityPage />} />
              <Route path="*" element={<Box sx={{ p: 6 }}>Page not found.</Box>} />
            </Routes>
          </>
        )}
    </ThemeProvider>
  );
}
