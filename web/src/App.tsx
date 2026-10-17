import { ReportSchedules } from './pages/ReportSchedules';
import { MessagingAdmin, MyMessaging } from './pages/Messaging';
import { GenerationAdmin } from './pages/Generation';
import { lazy, Suspense, useEffect, useMemo, useState } from 'react';
import { Route, Routes } from 'react-router-dom';
import { Box, CircularProgress, CssBaseline, ThemeProvider } from '@mui/material';
import { useAuth, useBranding } from './auth';
import { makeTheme } from './theme';
import { useColorMode } from './colorMode';
import { TopBar } from './layout/TopBar';
import { CommandPalette } from './layout/CommandPalette';
import { EnterpriseOverview, WarRoom, OrgStructure, StandardList, StandardNew, StandardDetail, SkillsMatrix, CurriculumList, AuditProgramList, AuditProgramDetail } from './pages/Enterprise';
import { ComplianceHome, ValidationList, ValidationNew, ValidationDetail } from './pages/Compliance';
import { ProjectList, ProjectNew, ProjectDetail, PpapList, PpapNew, PpapDetail, CapabilityStudies } from './pages/Launch';
import { Part11Readiness, RetentionPage, ContinuityPage, ApiAccessPage, AccessReviews, Deployments } from './pages/Governance';
import { RequirementList, RequirementDetail, DesignReviewList, DesignReviewDetail, BomQuality, Variants } from './pages/Engineering';
import { CommandCenter, FactoryCenter, EnterpriseRiskMatrix, KpiFramework, DigitalTwin, BoardReports } from './pages/QualityOs';
import { KnowledgeLibrary, KnowledgeDetail, Playbooks, ProcessMap, Maturity } from './pages/Knowledge';
import { AdvisorChat, DocumentAssistant, AuditPrep } from './pages/Advisor';
import { MyWorkspace, ActivityStream, QualityCalendar, Resources, Genealogy, ReportDesigner, DashboardDesigner, Webhooks, PlatformHealth, SecurityOps, ModuleRegistry } from './pages/Platform';
import { WorkflowDesigner, FormDesigner, RulesStudio, DataGovernance, RecoveryCenter, Marketplace, ContentLibrary } from './pages/PlatformStudio';
import { Login } from './pages/Login';

const LiveMonitor = lazy(() => import('./pages/ShopFloor').then((m) => ({ default: m.LiveMonitor })));
const WorkOrderList = lazy(() => import('./pages/ShopFloor').then((m) => ({ default: m.WorkOrderList })));
const WorkOrderNew = lazy(() => import('./pages/ShopFloor').then((m) => ({ default: m.WorkOrderNew })));
const WorkOrderDetail = lazy(() => import('./pages/ShopFloor').then((m) => ({ default: m.WorkOrderDetail })));
const PurchaseOrderList = lazy(() => import('./pages/ShopFloor').then((m) => ({ default: m.PurchaseOrderList })));
const PurchaseOrderNew = lazy(() => import('./pages/ShopFloor').then((m) => ({ default: m.PurchaseOrderNew })));
const PurchaseOrderDetail = lazy(() => import('./pages/ShopFloor').then((m) => ({ default: m.PurchaseOrderDetail })));
const EntryLog = lazy(() => import('./pages/ShopFloor').then((m) => ({ default: m.EntryLog })));
const ShopFloorEntry = lazy(() => import('./pages/ShopFloor').then((m) => ({ default: m.ShopFloorEntry })));
const TraceSearch = lazy(() => import('./pages/Traceability').then((m) => ({ default: m.TraceSearch })));
const LotList = lazy(() => import('./pages/Traceability').then((m) => ({ default: m.LotList })));
const LotNew = lazy(() => import('./pages/Traceability').then((m) => ({ default: m.LotNew })));
const LotDetail = lazy(() => import('./pages/Traceability').then((m) => ({ default: m.LotDetail })));
const SerialList = lazy(() => import('./pages/Traceability').then((m) => ({ default: m.SerialList })));
const SerialNew = lazy(() => import('./pages/Traceability').then((m) => ({ default: m.SerialNew })));
const SerialDetail = lazy(() => import('./pages/Traceability').then((m) => ({ default: m.SerialDetail })));
const ScanPage = lazy(() => import('./pages/Traceability').then((m) => ({ default: m.ScanPage })));
const LabelsPage = lazy(() => import('./pages/Traceability').then((m) => ({ default: m.LabelsPage })));
const RecallList = lazy(() => import('./pages/FieldQuality').then((m) => ({ default: m.RecallList })));
const RecallNew = lazy(() => import('./pages/FieldQuality').then((m) => ({ default: m.RecallNew })));
const RecallDetail = lazy(() => import('./pages/FieldQuality').then((m) => ({ default: m.RecallDetail })));
const ReturnList = lazy(() => import('./pages/FieldQuality').then((m) => ({ default: m.ReturnList })));
const ReturnNew = lazy(() => import('./pages/FieldQuality').then((m) => ({ default: m.ReturnNew })));
const ReturnDetail = lazy(() => import('./pages/FieldQuality').then((m) => ({ default: m.ReturnDetail })));
const WarrantyList = lazy(() => import('./pages/FieldQuality').then((m) => ({ default: m.WarrantyList })));
const WarrantyNew = lazy(() => import('./pages/FieldQuality').then((m) => ({ default: m.WarrantyNew })));
const WarrantyDetail = lazy(() => import('./pages/FieldQuality').then((m) => ({ default: m.WarrantyDetail })));
const WarrantyMetrics = lazy(() => import('./pages/FieldQuality').then((m) => ({ default: m.WarrantyMetrics })));
const ConnectionList = lazy(() => import('./pages/Integrations').then((m) => ({ default: m.ConnectionList })));
const ConnectionNew = lazy(() => import('./pages/Integrations').then((m) => ({ default: m.ConnectionNew })));
const ConnectionDetail = lazy(() => import('./pages/Integrations').then((m) => ({ default: m.ConnectionDetail })));
const AdapterCatalog = lazy(() => import('./pages/Integrations').then((m) => ({ default: m.AdapterCatalog })));
const ChangeQueue = lazy(() => import('./pages/Integrations').then((m) => ({ default: m.ChangeQueue })));
const ErpChangeDetail = lazy(() => import('./pages/Integrations').then((m) => ({ default: m.ErpChangeDetail })));
const KpiLibrary = lazy(() => import('./pages/Intelligence').then((m) => ({ default: m.KpiLibrary })));
const Assistant = lazy(() => import('./pages/Intelligence').then((m) => ({ default: m.Assistant })));
const InvestigatePage = lazy(() => import('./pages/Intelligence').then((m) => ({ default: m.InvestigatePage })));
const RiskPage = lazy(() => import('./pages/Intelligence').then((m) => ({ default: m.RiskPage })));
const DatasetsPage = lazy(() => import('./pages/Intelligence').then((m) => ({ default: m.DatasetsPage })));
const AlertsAdmin = lazy(() => import('./pages/Intelligence').then((m) => ({ default: m.AlertsAdmin })));
const Home = lazy(() => import('./pages/Home').then((m) => ({ default: m.Home })));
const MyWork = lazy(() => import('./pages/MyWork').then((m) => ({ default: m.MyWork })));
const SignInHistory = lazy(() => import('./pages/Account').then((m) => ({ default: m.SignInHistory })));
const AccountSecurity = lazy(() => import('./pages/AccountSecurity').then((m) => ({ default: m.AccountSecurity })));
const Search = lazy(() => import('./pages/Search').then((m) => ({ default: m.Search })));
const DocumentDetail = lazy(() => import('./pages/Documents').then((m) => ({ default: m.DocumentDetail })));
const DocumentList = lazy(() => import('./pages/Documents').then((m) => ({ default: m.DocumentList })));
const DocumentNew = lazy(() => import('./pages/Documents').then((m) => ({ default: m.DocumentNew })));
const DocumentTypesPage = lazy(() => import('./pages/Documents').then((m) => ({ default: m.DocumentTypesPage })));
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
const WarehouseOverview = lazy(() => import('./pages/Warehouse').then((m) => ({ default: m.WarehouseOverview })));
const StockPage = lazy(() => import('./pages/Warehouse').then((m) => ({ default: m.StockPage })));
const LedgerPage = lazy(() => import('./pages/Warehouse').then((m) => ({ default: m.LedgerPage })));
const WipPage = lazy(() => import('./pages/Warehouse').then((m) => ({ default: m.WipPage })));
const LocationsPage = lazy(() => import('./pages/Warehouse').then((m) => ({ default: m.LocationsPage })));
const LocationDetail = lazy(() => import('./pages/Warehouse').then((m) => ({ default: m.LocationDetail })));
const ReceiptList = lazy(() => import('./pages/Warehouse').then((m) => ({ default: m.ReceiptList })));
const ReceiptNew = lazy(() => import('./pages/Warehouse').then((m) => ({ default: m.ReceiptNew })));
const ReceiptDetailPage = lazy(() => import('./pages/Warehouse').then((m) => ({ default: m.ReceiptDetailPage })));
const ReceiptLabelsPage = lazy(() => import('./pages/Warehouse').then((m) => ({ default: m.ReceiptLabelsPage })));
const LabelPrintingPage = lazy(() => import('./pages/Labels').then((m) => ({ default: m.LabelPrintingPage })));
const ReceivingRulesPage = lazy(() => import('./pages/Warehouse').then((m) => ({ default: m.ReceivingRulesPage })));
const HoldList = lazy(() => import('./pages/Warehouse').then((m) => ({ default: m.HoldList })));
const HoldDetail = lazy(() => import('./pages/Warehouse').then((m) => ({ default: m.HoldDetail })));
const MrbList = lazy(() => import('./pages/Warehouse').then((m) => ({ default: m.MrbList })));
const MrbNew = lazy(() => import('./pages/Warehouse').then((m) => ({ default: m.MrbNew })));
const MrbDetail = lazy(() => import('./pages/Warehouse').then((m) => ({ default: m.MrbDetail })));
const RtvList = lazy(() => import('./pages/Warehouse').then((m) => ({ default: m.RtvList })));
const RtvDetail = lazy(() => import('./pages/Warehouse').then((m) => ({ default: m.RtvDetail })));
const CountList = lazy(() => import('./pages/Warehouse').then((m) => ({ default: m.CountList })));
const CountDetailPage = lazy(() => import('./pages/Warehouse').then((m) => ({ default: m.CountDetailPage })));
const TransferList = lazy(() => import('./pages/Warehouse').then((m) => ({ default: m.TransferList })));
const TransferNew = lazy(() => import('./pages/Warehouse').then((m) => ({ default: m.TransferNew })));
const TransferDetail = lazy(() => import('./pages/Warehouse').then((m) => ({ default: m.TransferDetail })));
const LotMergePage = lazy(() => import('./pages/Warehouse').then((m) => ({ default: m.LotMergePage })));
const ScanStation = lazy(() => import('./pages/ScanStation').then((m) => ({ default: m.ScanStation })));
const LogisticsOverview = lazy(() => import('./pages/Shipping').then((m) => ({ default: m.LogisticsOverview })));
const ShipmentList = lazy(() => import('./pages/Shipping').then((m) => ({ default: m.ShipmentList })));
const ShipmentNew = lazy(() => import('./pages/Shipping').then((m) => ({ default: m.ShipmentNew })));
const ShipmentDetail = lazy(() => import('./pages/Shipping').then((m) => ({ default: m.ShipmentDetail })));
const CarrierList = lazy(() => import('./pages/Shipping').then((m) => ({ default: m.CarrierList })));
const ShippingDocumentTemplates = lazy(() => import('./pages/ShippingDocuments').then((m) => ({ default: m.ShippingDocumentTemplates })));
const ShippingDocumentTemplate = lazy(() => import('./pages/ShippingDocuments').then((m) => ({ default: m.ShippingDocumentTemplate })));
const CarrierConnections = lazy(() => import('./pages/Carriers').then((m) => ({ default: m.CarrierConnections })));
const ApqpList = lazy(() => import('./pages/Advanced').then((m) => ({ default: m.ApqpList })));
const ApqpNew = lazy(() => import('./pages/Advanced').then((m) => ({ default: m.ApqpNew })));
const ApqpDetail = lazy(() => import('./pages/Advanced').then((m) => ({ default: m.ApqpDetail })));
const SpcList = lazy(() => import('./pages/Advanced').then((m) => ({ default: m.SpcList })));
const SpcNew = lazy(() => import('./pages/Advanced').then((m) => ({ default: m.SpcNew })));
const SpcDetail = lazy(() => import('./pages/Advanced').then((m) => ({ default: m.SpcDetail })));
const MsaList = lazy(() => import('./pages/Advanced').then((m) => ({ default: m.MsaList })));
const MsaNew = lazy(() => import('./pages/Advanced').then((m) => ({ default: m.MsaNew })));
const MsaDetail = lazy(() => import('./pages/Advanced').then((m) => ({ default: m.MsaDetail })));
const LpaHome = lazy(() => import('./pages/Advanced').then((m) => ({ default: m.LpaHome })));
const CoqHome = lazy(() => import('./pages/Advanced').then((m) => ({ default: m.CoqHome })));
const SupplierScorecard = lazy(() => import('./pages/Advanced').then((m) => ({ default: m.SupplierScorecard })));
const InsightsHome = lazy(() => import('./pages/Advanced').then((m) => ({ default: m.InsightsHome })));
const RelationshipGraph = lazy(() => import('./pages/Advanced').then((m) => ({ default: m.RelationshipGraph })));
const PortalAccounts = lazy(() => import('./pages/Advanced').then((m) => ({ default: m.PortalAccounts })));
const PortalApp = lazy(() => import('./pages/Portal').then((m) => ({ default: m.PortalApp })));
const RecordRedirect = lazy(() => import('./pages/RecordRedirect').then((m) => ({ default: m.RecordRedirect })));
const ChangeList = lazy(() => import('./pages/Changes').then((m) => ({ default: m.ChangeList })));
const ChangeNew = lazy(() => import('./pages/Changes').then((m) => ({ default: m.ChangeNew })));
const ChangeDetail = lazy(() => import('./pages/Changes').then((m) => ({ default: m.ChangeDetail })));
const InspectionList = lazy(() => import('./pages/Inspection').then((m) => ({ default: m.InspectionList })));
const InspectionDetail = lazy(() => import('./pages/Inspection').then((m) => ({ default: m.InspectionDetail })));
const FaiList = lazy(() => import('./pages/FaiAql').then((m) => ({ default: m.FaiList })));
const FaiNew = lazy(() => import('./pages/FaiAql').then((m) => ({ default: m.FaiNew })));
const FaiDetail = lazy(() => import('./pages/FaiAql').then((m) => ({ default: m.FaiDetail })));
const AqlList = lazy(() => import('./pages/FaiAql').then((m) => ({ default: m.AqlList })));
const AqlNew = lazy(() => import('./pages/FaiAql').then((m) => ({ default: m.AqlNew })));
const AqlDetail = lazy(() => import('./pages/FaiAql').then((m) => ({ default: m.AqlDetail })));
const AqlPlans = lazy(() => import('./pages/FaiAql').then((m) => ({ default: m.AqlPlans })));
const FaiAqlDashboard = lazy(() => import('./pages/FaiAql').then((m) => ({ default: m.FaiAqlDashboard })));
const SupplierAql = lazy(() => import('./pages/FaiAql').then((m) => ({ default: m.SupplierAql })));
const PlanList = lazy(() => import('./pages/Inspection').then((m) => ({ default: m.PlanList })));
const PlanNew = lazy(() => import('./pages/Inspection').then((m) => ({ default: m.PlanNew })));
const PlanDetail = lazy(() => import('./pages/Inspection').then((m) => ({ default: m.PlanDetail })));
const ComplaintList = lazy(() => import('./pages/Complaints').then((m) => ({ default: m.ComplaintList })));
const ComplaintNew = lazy(() => import('./pages/Complaints').then((m) => ({ default: m.ComplaintNew })));
const ComplaintDetail = lazy(() => import('./pages/Complaints').then((m) => ({ default: m.ComplaintDetail })));
const ReportDashboard = lazy(() => import('./pages/Reports').then((m) => ({ default: m.ReportDashboard })));
const ReportCatalog = lazy(() => import('./pages/Reports').then((m) => ({ default: m.ReportCatalog })));
const ReportRun = lazy(() => import('./pages/Reports').then((m) => ({ default: m.ReportRun })));
const RiskList = lazy(() => import('./pages/Risks').then((m) => ({ default: m.RiskList })));
const RiskNew = lazy(() => import('./pages/Risks').then((m) => ({ default: m.RiskNew })));
const RiskDetail = lazy(() => import('./pages/Risks').then((m) => ({ default: m.RiskDetail })));
const RiskMatrix = lazy(() => import('./pages/Risks').then((m) => ({ default: m.RiskMatrix })));
const RiskSettingsPage = lazy(() => import('./pages/Risks').then((m) => ({ default: m.RiskSettingsPage })));
const ObjectiveList = lazy(() => import('./pages/Objectives').then((m) => ({ default: m.ObjectiveList })));
const ObjectiveNew = lazy(() => import('./pages/Objectives').then((m) => ({ default: m.ObjectiveNew })));
const ObjectiveDetail = lazy(() => import('./pages/Objectives').then((m) => ({ default: m.ObjectiveDetail })));
const ReviewList = lazy(() => import('./pages/Reviews').then((m) => ({ default: m.ReviewList })));
const ReviewNew = lazy(() => import('./pages/Reviews').then((m) => ({ default: m.ReviewNew })));
const ReviewDetail = lazy(() => import('./pages/Reviews').then((m) => ({ default: m.ReviewDetail })));
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
const SignInSecurityPage = lazy(() => import('./pages/Admin').then((m) => ({ default: m.SignInSecurityPage })));
const SingleSignOnPage = lazy(() => import('./pages/SingleSignOn').then((m) => ({ default: m.SingleSignOnPage })));
const SitesPage = lazy(() => import('./pages/Admin').then((m) => ({ default: m.SitesPage })));
const UsersPage = lazy(() => import('./pages/Admin').then((m) => ({ default: m.UsersPage })));

export function App() {
  const { me, loading } = useAuth();
  const { data: brand } = useBranding();
  const { mode } = useColorMode();
  const theme = useMemo(() => makeTheme(brand?.primaryColor, mode), [brand?.primaryColor, mode]);
  useEffect(() => { // browser chrome follows the top bar colour
    document.querySelectorAll('meta[name="theme-color"]').forEach((m) => m.remove());
    const meta = document.createElement('meta'); meta.name = 'theme-color'; meta.content = theme.palette.background.bar; document.head.appendChild(meta);
  }, [theme]);
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
        : !me ? <Login /> : me.portalKind ? <Suspense fallback={null}><PortalApp /></Suspense> : (
          <>
            <TopBar onOpenPalette={() => setPalette(true)} />
            <CommandPalette open={palette} onClose={() => setPalette(false)} />
            <Suspense fallback={<Box sx={{ display: 'grid', placeItems: 'center', height: '60vh' }}><CircularProgress /></Box>}>
            <Routes>
              <Route path="/" element={<Home />} />
              <Route path="/my-work" element={<MyWork />} />
              <Route path="/search" element={<Search />} />
              <Route path="/account/sign-ins" element={<SignInHistory />} />
              <Route path="/account/security" element={<AccountSecurity />} />
              <Route path="/account/messaging" element={<MyMessaging />} />
              <Route path="/documents" element={<DocumentList kind={0} />} />
              <Route path="/documents/new" element={<DocumentNew kind={0} />} />
              <Route path="/documents/:id" element={<DocumentDetail kind={0} />} />
              <Route path="/fai" element={<FaiList />} />
              <Route path="/fai/new" element={<FaiNew />} />
              <Route path="/fai/dashboard" element={<FaiAqlDashboard />} />
              <Route path="/fai/:id" element={<FaiDetail />} />
              <Route path="/aql" element={<AqlList />} />
              <Route path="/aql/new" element={<AqlNew />} />
              <Route path="/aql/plans" element={<AqlPlans />} />
              <Route path="/aql/suppliers" element={<SupplierAql />} />
              <Route path="/aql/:id" element={<AqlDetail />} />
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
              <Route path="/records/:type/:id" element={<RecordRedirect />} />
              <Route path="/changes" element={<ChangeList />} />
              <Route path="/changes/new" element={<ChangeNew />} />
              <Route path="/changes/:id" element={<ChangeDetail />} />
              <Route path="/inspection" element={<InspectionList />} />
              <Route path="/inspection/plans" element={<PlanList />} />
              <Route path="/inspection/plans/new" element={<PlanNew />} />
              <Route path="/inspection/plans/:id" element={<PlanDetail />} />
              <Route path="/inspection/:id" element={<InspectionDetail />} />
              <Route path="/objectives" element={<ObjectiveList />} />
              <Route path="/objectives/new" element={<ObjectiveNew />} />
              <Route path="/objectives/:id" element={<ObjectiveDetail />} />
              <Route path="/reviews" element={<ReviewList />} />
              <Route path="/reviews/new" element={<ReviewNew />} />
              <Route path="/reviews/:id" element={<ReviewDetail />} />
              <Route path="/reports" element={<ReportDashboard />} />
              <Route path="/reports/catalog" element={<ReportCatalog />} />
              <Route path="/reports/r/:key" element={<ReportRun />} />
              <Route path="/risks" element={<RiskList />} />
              <Route path="/risks/new" element={<RiskNew />} />
              <Route path="/risks/matrix" element={<RiskMatrix />} />
              <Route path="/risks/settings" element={<RiskSettingsPage />} />
              <Route path="/risks/:id" element={<RiskDetail />} />
              <Route path="/complaints" element={<ComplaintList />} />
              <Route path="/complaints/new" element={<ComplaintNew />} />
              <Route path="/complaints/trends" element={<ComplaintTrends />} />
              <Route path="/complaints/:id" element={<ComplaintDetail />} />
              <Route path="/suppliers/scar/:id" element={<ScarDetail />} />
              <Route path="/suppliers/performance" element={<SupplierScorecard />} />
              <Route path="/apqp" element={<ApqpList />} />
              <Route path="/apqp/new" element={<ApqpNew />} />
              <Route path="/apqp/:kind/:id" element={<ApqpDetail />} />
              <Route path="/spc" element={<SpcList />} />
              <Route path="/spc/new" element={<SpcNew />} />
              <Route path="/spc/msa" element={<MsaList />} />
              <Route path="/spc/msa/new" element={<MsaNew />} />
              <Route path="/spc/msa/:id" element={<MsaDetail />} />
              <Route path="/spc/:id" element={<SpcDetail />} />
              <Route path="/lpa" element={<LpaHome />} />
              <Route path="/lpa/:id" element={<LpaHome />} />
              <Route path="/coq" element={<CoqHome />} />
              <Route path="/coq/scrap" element={<CoqHome />} />
              <Route path="/coq/rework/:id" element={<CoqHome />} />
              <Route path="/insights" element={<InsightsHome />} />
              <Route path="/insights/graph" element={<RelationshipGraph />} />
              <Route path="/admin/portal" element={<PortalAccounts />} />
              <Route path="/suppliers/reviews-due" element={<ReviewsDue />} />
              <Route path="/suppliers/:id" element={<SupplierDetail />} />
              <Route path="/enterprise" element={<EnterpriseOverview />} />
              <Route path="/enterprise/war-room/:siteId" element={<WarRoom />} />
              <Route path="/enterprise/org" element={<OrgStructure />} />
              <Route path="/enterprise/standards" element={<StandardList />} />
              <Route path="/enterprise/standards/new" element={<StandardNew />} />
              <Route path="/enterprise/standards/:id" element={<StandardDetail />} />
              <Route path="/training/skills" element={<SkillsMatrix />} />
              <Route path="/training/curricula" element={<CurriculumList />} />
              <Route path="/audits/programs" element={<AuditProgramList />} />
              <Route path="/audits/programs/:id" element={<AuditProgramDetail />} />
              <Route path="/compliance" element={<ComplianceHome />} />
              <Route path="/validation" element={<ValidationList />} />
              <Route path="/validation/new" element={<ValidationNew />} />
              <Route path="/validation/:id" element={<ValidationDetail />} />
              <Route path="/launch" element={<ProjectList />} />
              <Route path="/launch/new" element={<ProjectNew />} />
              <Route path="/launch/ppap" element={<PpapList />} />
              <Route path="/launch/ppap/new" element={<PpapNew />} />
              <Route path="/launch/ppap/:id" element={<PpapDetail />} />
              <Route path="/launch/capability" element={<CapabilityStudies />} />
              <Route path="/launch/:id" element={<ProjectDetail />} />
              <Route path="/platform" element={<MyWorkspace />} />
              <Route path="/platform/activity" element={<ActivityStream />} />
              <Route path="/platform/calendar" element={<QualityCalendar />} />
              <Route path="/platform/resources" element={<Resources />} />
              <Route path="/platform/genealogy" element={<Genealogy />} />
              <Route path="/platform/reports" element={<ReportDesigner />} />
              <Route path="/platform/reports/:id" element={<ReportDesigner />} />
              <Route path="/platform/report-schedules" element={<ReportSchedules />} />
              <Route path="/platform/dashboards" element={<DashboardDesigner />} />
              <Route path="/platform/webhooks" element={<Webhooks />} />
              <Route path="/platform/messaging" element={<MessagingAdmin />} />
              <Route path="/platform/generation" element={<GenerationAdmin />} />
              <Route path="/platform/health" element={<PlatformHealth />} />
              <Route path="/platform/security" element={<SecurityOps />} />
              <Route path="/platform/modules" element={<ModuleRegistry />} />
              <Route path="/platform/workflows" element={<WorkflowDesigner />} />
              <Route path="/platform/forms" element={<FormDesigner />} />
              <Route path="/platform/rules" element={<RulesStudio />} />
              <Route path="/platform/governance" element={<DataGovernance />} />
              <Route path="/platform/recovery" element={<RecoveryCenter />} />
              <Route path="/platform/marketplace" element={<Marketplace />} />
              <Route path="/documents/content" element={<ContentLibrary />} />
              <Route path="/documents/types" element={<DocumentTypesPage />} />
              <Route path="/governance" element={<Part11Readiness />} />
              <Route path="/governance/retention" element={<RetentionPage />} />
              <Route path="/governance/continuity" element={<ContinuityPage />} />
              <Route path="/governance/api" element={<ApiAccessPage />} />
              <Route path="/governance/access" element={<AccessReviews />} />
              <Route path="/governance/deployments" element={<Deployments />} />
              <Route path="/engineering" element={<RequirementList />} />
              <Route path="/engineering/requirements/:id" element={<RequirementDetail />} />
              <Route path="/engineering/reviews" element={<DesignReviewList />} />
              <Route path="/engineering/reviews/:id" element={<DesignReviewDetail />} />
              <Route path="/engineering/bom" element={<BomQuality />} />
              <Route path="/engineering/variants" element={<Variants />} />
              <Route path="/qos" element={<CommandCenter />} />
              <Route path="/qos/factory" element={<FactoryCenter />} />
              <Route path="/qos/risk" element={<EnterpriseRiskMatrix />} />
              <Route path="/qos/kpis" element={<KpiFramework />} />
              <Route path="/qos/twin" element={<DigitalTwin />} />
              <Route path="/qos/boards" element={<BoardReports />} />
              <Route path="/knowledge" element={<KnowledgeLibrary />} />
              <Route path="/knowledge/playbooks" element={<Playbooks />} />
              <Route path="/knowledge/:id" element={<KnowledgeDetail />} />
              <Route path="/processes" element={<ProcessMap />} />
              <Route path="/maturity" element={<Maturity />} />
              <Route path="/advisor" element={<AdvisorChat />} />
              <Route path="/advisor/documents" element={<DocumentAssistant />} />
              <Route path="/advisor/audit-prep" element={<AuditPrep />} />
              <Route path="/admin" element={<AdminHome />} />
              <Route path="/admin/users" element={<UsersPage />} />
              <Route path="/admin/roles" element={<RolesPage />} />
              <Route path="/admin/sites" element={<SitesPage />} />
              <Route path="/admin/routes" element={<RoutesPage />} />
              <Route path="/admin/organization" element={<OrganizationPage />} />
              <Route path="/admin/audit" element={<AuditPage />} />
              <Route path="/admin/security" element={<SecurityPage />} />
              <Route path="/admin/sign-in-security" element={<SignInSecurityPage />} />
              <Route path="/admin/single-sign-on" element={<SingleSignOnPage />} />
              <Route path="/production" element={<LiveMonitor />} />
              <Route path="/production/entry" element={<ShopFloorEntry />} />
              <Route path="/production/entries" element={<EntryLog />} />
              <Route path="/production/work-orders" element={<WorkOrderList />} />
              <Route path="/production/work-orders/new" element={<WorkOrderNew />} />
              <Route path="/production/work-orders/:id" element={<WorkOrderDetail />} />
              <Route path="/production/purchase-orders" element={<PurchaseOrderList />} />
              <Route path="/production/purchase-orders/new" element={<PurchaseOrderNew />} />
              <Route path="/production/purchase-orders/:id" element={<PurchaseOrderDetail />} />
              <Route path="/warehouse" element={<WarehouseOverview />} />
              <Route path="/warehouse/stock" element={<StockPage />} />
              <Route path="/warehouse/ledger" element={<LedgerPage />} />
              <Route path="/warehouse/wip" element={<WipPage />} />
              <Route path="/warehouse/locations" element={<LocationsPage />} />
              <Route path="/warehouse/locations/:id" element={<LocationDetail />} />
              <Route path="/warehouse/receipts" element={<ReceiptList />} />
              <Route path="/warehouse/receipts/new" element={<ReceiptNew />} />
              <Route path="/warehouse/receipts/:id" element={<ReceiptDetailPage />} />
              <Route path="/warehouse/receipts/:id/labels" element={<ReceiptLabelsPage />} />
              <Route path="/warehouse/receiving-rules" element={<ReceivingRulesPage />} />
              <Route path="/warehouse/labels" element={<LabelPrintingPage />} />
              <Route path="/warehouse/holds" element={<HoldList />} />
              <Route path="/warehouse/holds/:id" element={<HoldDetail />} />
              <Route path="/warehouse/mrb" element={<MrbList />} />
              <Route path="/warehouse/mrb/new" element={<MrbNew />} />
              <Route path="/warehouse/mrb/:id" element={<MrbDetail />} />
              <Route path="/warehouse/rtv" element={<RtvList />} />
              <Route path="/warehouse/rtv/:id" element={<RtvDetail />} />
              <Route path="/warehouse/counts" element={<CountList />} />
              <Route path="/warehouse/counts/:id" element={<CountDetailPage />} />
              <Route path="/warehouse/transfers" element={<TransferList />} />
              <Route path="/warehouse/transfers/new" element={<TransferNew />} />
              <Route path="/warehouse/transfers/:id" element={<TransferDetail />} />
              <Route path="/warehouse/lot-merge" element={<LotMergePage />} />
              <Route path="/warehouse/scan" element={<ScanStation />} />
              <Route path="/shipping" element={<LogisticsOverview />} />
              <Route path="/shipping/shipments" element={<ShipmentList />} />
              <Route path="/shipping/shipments/new" element={<ShipmentNew />} />
              <Route path="/shipping/shipments/:id" element={<ShipmentDetail />} />
              <Route path="/shipping/carriers" element={<CarrierList />} />
              <Route path="/shipping/carrier-connections" element={<CarrierConnections />} />
              <Route path="/shipping/document-templates" element={<ShippingDocumentTemplates />} />
              <Route path="/shipping/document-templates/:id" element={<ShippingDocumentTemplate />} />
              <Route path="/trace" element={<TraceSearch />} />
              <Route path="/trace/scan" element={<ScanPage />} />
              <Route path="/trace/labels" element={<LabelsPage />} />
              <Route path="/trace/lots" element={<LotList />} />
              <Route path="/trace/lots/new" element={<LotNew />} />
              <Route path="/trace/lots/:id" element={<LotDetail />} />
              <Route path="/trace/serials" element={<SerialList />} />
              <Route path="/trace/serials/new" element={<SerialNew />} />
              <Route path="/trace/serials/:id" element={<SerialDetail />} />
              <Route path="/field/recalls" element={<RecallList />} />
              <Route path="/field/recalls/new" element={<RecallNew />} />
              <Route path="/field/recalls/:id" element={<RecallDetail />} />
              <Route path="/field/returns" element={<ReturnList />} />
              <Route path="/field/returns/new" element={<ReturnNew />} />
              <Route path="/field/returns/:id" element={<ReturnDetail />} />
              <Route path="/field/warranty" element={<WarrantyList />} />
              <Route path="/field/warranty/new" element={<WarrantyNew />} />
              <Route path="/field/warranty/metrics" element={<WarrantyMetrics />} />
              <Route path="/field/warranty/:id" element={<WarrantyDetail />} />
              <Route path="/integrations" element={<ConnectionList />} />
              <Route path="/integrations/new" element={<ConnectionNew />} />
              <Route path="/integrations/adapters" element={<AdapterCatalog />} />
              <Route path="/integrations/changes" element={<ChangeQueue />} />
              <Route path="/integrations/changes/:id" element={<ErpChangeDetail />} />
              <Route path="/integrations/:id" element={<ConnectionDetail />} />
              <Route path="/insights/kpis" element={<KpiLibrary />} />
              <Route path="/insights/assistant" element={<Assistant />} />
              <Route path="/insights/investigate" element={<InvestigatePage />} />
              <Route path="/insights/risk" element={<RiskPage />} />
              <Route path="/insights/datasets" element={<DatasetsPage />} />
              <Route path="/admin/alerts" element={<AlertsAdmin />} />
              <Route path="*" element={<Box sx={{ p: 6 }}>Page not found.</Box>} />
            </Routes>
            </Suspense>
          </>
        )}
    </ThemeProvider>
  );
}
