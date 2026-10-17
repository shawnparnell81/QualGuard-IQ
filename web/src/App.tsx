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
import { FormRecordDetail, FormRecordList, FormRecordNew } from './pages/Forms';
import { CapaDetail, CapaList, CapaNew, NcrDetail, NcrList, NcrNew } from './pages/Ncr';
import { AuditDetail, AuditList, AuditNew, FindingDetail, FindingList } from './pages/Audits';
import { CourseDetail, CourseList, CourseNew, MyTraining, TrainingMatrix } from './pages/Training';
import { EquipmentDetail, EquipmentList, EquipmentNew, ImpactDetail } from './pages/Calibration';
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
              <Route path="/forms/templates" element={<DocumentList kind={2} />} />
              <Route path="/forms/templates/new" element={<DocumentNew kind={2} />} />
              <Route path="/forms/templates/:id" element={<DocumentDetail kind={2} />} />
              <Route path="/forms/records" element={<FormRecordList />} />
              <Route path="/forms/records/new" element={<FormRecordNew />} />
              <Route path="/forms/records/:id" element={<FormRecordDetail />} />
              <Route path="/ncr" element={<NcrList />} />
              <Route path="/ncr/new" element={<NcrNew />} />
              <Route path="/ncr/:id" element={<NcrDetail />} />
              <Route path="/capa" element={<CapaList />} />
              <Route path="/capa/new" element={<CapaNew />} />
              <Route path="/capa/:id" element={<CapaDetail />} />
              <Route path="/audits" element={<AuditList />} />
              <Route path="/audits/new" element={<AuditNew />} />
              <Route path="/audits/findings" element={<FindingList />} />
              <Route path="/audits/findings/:id" element={<FindingDetail />} />
              <Route path="/audits/:id" element={<AuditDetail />} />
              <Route path="/training" element={<MyTraining />} />
              <Route path="/training/courses" element={<CourseList />} />
              <Route path="/training/courses/new" element={<CourseNew />} />
              <Route path="/training/courses/:id" element={<CourseDetail />} />
              <Route path="/training/status" element={<TrainingMatrix />} />
              <Route path="/calibration/equipment" element={<EquipmentList />} />
              <Route path="/calibration/equipment/new" element={<EquipmentNew />} />
              <Route path="/calibration/equipment/:id" element={<EquipmentDetail />} />
              <Route path="/calibration/impact/:id" element={<ImpactDetail />} />
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
