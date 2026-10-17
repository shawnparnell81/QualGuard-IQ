import { lazy, Suspense, useEffect, useMemo, useState } from 'react';
import { Route, Routes } from 'react-router-dom';
import { Box, CircularProgress, CssBaseline, ThemeProvider } from '@mui/material';
import { useAuth, useBranding } from './auth';
import { makeTheme } from './theme';
import { TopBar } from './layout/TopBar';
import { CommandPalette } from './layout/CommandPalette';
import { Login } from './pages/Login';

const Home = lazy(() => import('./pages/Home').then((m) => ({ default: m.Home })));
const MyWork = lazy(() => import('./pages/MyWork').then((m) => ({ default: m.MyWork })));
const SignInHistory = lazy(() => import('./pages/Account').then((m) => ({ default: m.SignInHistory })));
const Search = lazy(() => import('./pages/Search').then((m) => ({ default: m.Search })));
const DocumentDetail = lazy(() => import('./pages/Documents').then((m) => ({ default: m.DocumentDetail })));
const DocumentList = lazy(() => import('./pages/Documents').then((m) => ({ default: m.DocumentList })));
const DocumentNew = lazy(() => import('./pages/Documents').then((m) => ({ default: m.DocumentNew })));
const FormRecordDetail = lazy(() => import('./pages/Forms').then((m) => ({ default: m.FormRecordDetail })));
const FormRecordList = lazy(() => import('./pages/Forms').then((m) => ({ default: m.FormRecordList })));
const FormRecordNew = lazy(() => import('./pages/Forms').then((m) => ({ default: m.FormRecordNew })));
const CapaDetail = lazy(() => import('./pages/Ncr').then((m) => ({ default: m.CapaDetail })));
const CapaList = lazy(() => import('./pages/Ncr').then((m) => ({ default: m.CapaList })));
const CapaNew = lazy(() => import('./pages/Ncr').then((m) => ({ default: m.CapaNew })));
const NcrDetail = lazy(() => import('./pages/Ncr').then((m) => ({ default: m.NcrDetail })));
const NcrList = lazy(() => import('./pages/Ncr').then((m) => ({ default: m.NcrList })));
const NcrNew = lazy(() => import('./pages/Ncr').then((m) => ({ default: m.NcrNew })));
const AuditDetail = lazy(() => import('./pages/Audits').then((m) => ({ default: m.AuditDetail })));
const AuditList = lazy(() => import('./pages/Audits').then((m) => ({ default: m.AuditList })));
const AuditNew = lazy(() => import('./pages/Audits').then((m) => ({ default: m.AuditNew })));
const FindingDetail = lazy(() => import('./pages/Audits').then((m) => ({ default: m.FindingDetail })));
const FindingList = lazy(() => import('./pages/Audits').then((m) => ({ default: m.FindingList })));
const CourseDetail = lazy(() => import('./pages/Training').then((m) => ({ default: m.CourseDetail })));
const CourseList = lazy(() => import('./pages/Training').then((m) => ({ default: m.CourseList })));
const CourseNew = lazy(() => import('./pages/Training').then((m) => ({ default: m.CourseNew })));
const MyTraining = lazy(() => import('./pages/Training').then((m) => ({ default: m.MyTraining })));
const TrainingMatrix = lazy(() => import('./pages/Training').then((m) => ({ default: m.TrainingMatrix })));
const EquipmentDetail = lazy(() => import('./pages/Calibration').then((m) => ({ default: m.EquipmentDetail })));
const EquipmentList = lazy(() => import('./pages/Calibration').then((m) => ({ default: m.EquipmentList })));
const EquipmentNew = lazy(() => import('./pages/Calibration').then((m) => ({ default: m.EquipmentNew })));
const ImpactDetail = lazy(() => import('./pages/Calibration').then((m) => ({ default: m.ImpactDetail })));
const EngineeringHome = lazy(() => import('./pages/Products').then((m) => ({ default: m.EngineeringHome })));
const ProductList = lazy(() => import('./pages/Products').then((m) => ({ default: m.ProductList })));
const ProductNew = lazy(() => import('./pages/Products').then((m) => ({ default: m.ProductNew })));
const ProductDetail = lazy(() => import('./pages/Products').then((m) => ({ default: m.ProductDetail })));
const PartList = lazy(() => import('./pages/Products').then((m) => ({ default: m.PartList })));
const PartNew = lazy(() => import('./pages/Products').then((m) => ({ default: m.PartNew })));
const PartDetail = lazy(() => import('./pages/Products').then((m) => ({ default: m.PartDetail })));
const CustomerList = lazy(() => import('./pages/Products').then((m) => ({ default: m.CustomerList })));
const CustomerNew = lazy(() => import('./pages/Products').then((m) => ({ default: m.CustomerNew })));
const CustomerDetail = lazy(() => import('./pages/Products').then((m) => ({ default: m.CustomerDetail })));
const SupplierList = lazy(() => import('./pages/Suppliers').then((m) => ({ default: m.SupplierList })));
const SupplierNew = lazy(() => import('./pages/Suppliers').then((m) => ({ default: m.SupplierNew })));
const SupplierDetail = lazy(() => import('./pages/Suppliers').then((m) => ({ default: m.SupplierDetail })));
const ComplaintList = lazy(() => import('./pages/Complaints').then((m) => ({ default: m.ComplaintList })));
const ComplaintNew = lazy(() => import('./pages/Complaints').then((m) => ({ default: m.ComplaintNew })));
const ComplaintDetail = lazy(() => import('./pages/Complaints').then((m) => ({ default: m.ComplaintDetail })));
const ComplaintTrends = lazy(() => import('./pages/Complaints').then((m) => ({ default: m.ComplaintTrends })));
const ScarList = lazy(() => import('./pages/Suppliers').then((m) => ({ default: m.ScarList })));
const ScarDetail = lazy(() => import('./pages/Suppliers').then((m) => ({ default: m.ScarDetail })));
const ReviewsDue = lazy(() => import('./pages/Suppliers').then((m) => ({ default: m.ReviewsDue })));
const AdminHome = lazy(() => import('./pages/Admin').then((m) => ({ default: m.AdminHome })));
const AuditPage = lazy(() => import('./pages/Admin').then((m) => ({ default: m.AuditPage })));
const OrganizationPage = lazy(() => import('./pages/Admin').then((m) => ({ default: m.OrganizationPage })));
const RolesPage = lazy(() => import('./pages/Admin').then((m) => ({ default: m.RolesPage })));
const RoutesPage = lazy(() => import('./pages/Admin').then((m) => ({ default: m.RoutesPage })));
const SecurityPage = lazy(() => import('./pages/Admin').then((m) => ({ default: m.SecurityPage })));
const SitesPage = lazy(() => import('./pages/Admin').then((m) => ({ default: m.SitesPage })));
const UsersPage = lazy(() => import('./pages/Admin').then((m) => ({ default: m.UsersPage })));

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
            <Suspense fallback={<Box sx={{ display: 'grid', placeItems: 'center', height: '60vh' }}><CircularProgress /></Box>}>
            <Routes>
              <Route path="/" element={<Home />} />
              <Route path="/my-work" element={<MyWork />} />
              <Route path="/search" element={<Search />} />
              <Route path="/account/sign-ins" element={<SignInHistory />} />
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
              <Route path="/products" element={<EngineeringHome />} />
              <Route path="/products/items" element={<ProductList />} />
              <Route path="/products/items/new" element={<ProductNew />} />
              <Route path="/products/items/:id" element={<ProductDetail />} />
              <Route path="/products/parts" element={<PartList />} />
              <Route path="/products/parts/new" element={<PartNew />} />
              <Route path="/products/parts/:id" element={<PartDetail />} />
              <Route path="/products/customers" element={<CustomerList />} />
              <Route path="/products/customers/new" element={<CustomerNew />} />
              <Route path="/products/customers/:id" element={<CustomerDetail />} />
              <Route path="/suppliers" element={<SupplierList />} />
              <Route path="/suppliers/new" element={<SupplierNew />} />
              <Route path="/suppliers/scar" element={<ScarList />} />
              <Route path="/complaints" element={<ComplaintList />} />
              <Route path="/complaints/new" element={<ComplaintNew />} />
              <Route path="/complaints/trends" element={<ComplaintTrends />} />
              <Route path="/complaints/:id" element={<ComplaintDetail />} />
              <Route path="/suppliers/scar/:id" element={<ScarDetail />} />
              <Route path="/suppliers/reviews-due" element={<ReviewsDue />} />
              <Route path="/suppliers/:id" element={<SupplierDetail />} />
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
            </Suspense>
          </>
        )}
    </ThemeProvider>
  );
}
