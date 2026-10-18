import type { ReactNode } from 'react';
import InsightsOutlined from '@mui/icons-material/InsightsOutlined';
import DescriptionOutlined from '@mui/icons-material/DescriptionOutlined';
import ArchitectureOutlined from '@mui/icons-material/ArchitectureOutlined';
import AdminPanelSettingsOutlined from '@mui/icons-material/AdminPanelSettingsOutlined';
import ReportProblemOutlined from '@mui/icons-material/ReportProblemOutlined';
import FactCheckOutlined from '@mui/icons-material/FactCheckOutlined';
import SchoolOutlined from '@mui/icons-material/SchoolOutlined';
import SpeedOutlined from '@mui/icons-material/SpeedOutlined';
import LocalShippingOutlined from '@mui/icons-material/LocalShippingOutlined';
import SupportAgentOutlined from '@mui/icons-material/SupportAgentOutlined';
import GridViewOutlined from '@mui/icons-material/GridViewOutlined';
import FlagOutlined from '@mui/icons-material/FlagOutlined';
import RuleOutlined from '@mui/icons-material/RuleOutlined';
import ChangeCircleOutlined from '@mui/icons-material/ChangeCircleOutlined';
import StraightenOutlined from '@mui/icons-material/StraightenOutlined';
import AccountTreeOutlined from '@mui/icons-material/AccountTreeOutlined';
import ShowChartOutlined from '@mui/icons-material/ShowChartOutlined';
import LayersOutlined from '@mui/icons-material/LayersOutlined';
import PaidOutlined from '@mui/icons-material/PaidOutlined';
import HubOutlined from '@mui/icons-material/HubOutlined';
import PrecisionManufacturingOutlined from '@mui/icons-material/PrecisionManufacturingOutlined';
import QrCode2Outlined from '@mui/icons-material/QrCode2Outlined';
import AssignmentReturnOutlined from '@mui/icons-material/AssignmentReturnOutlined';
import SyncAltOutlined from '@mui/icons-material/SyncAltOutlined';
import WarehouseOutlined from '@mui/icons-material/WarehouseOutlined';
import Inventory2Outlined from '@mui/icons-material/Inventory2Outlined';
import DesignServicesOutlined from '@mui/icons-material/DesignServicesOutlined';
import PublicOutlined from '@mui/icons-material/PublicOutlined';
import MenuBookOutlined from '@mui/icons-material/MenuBookOutlined';
import PsychologyOutlined from '@mui/icons-material/PsychologyOutlined';

export interface NavItem { label: string; path: string; permission?: string; children?: NavItem[] }
export interface ModuleDef {
  key: string;
  label: string;
  description: string;
  icon: ReactNode;
  basePath: string;
  /** Any of these permissions makes the module visible. */
  permissions: string[];
  sidebar: NavItem[];
}

/**
 * Modules shown in the top menu. A module appears only once it is built and the user holds one of its permissions.
 */
export const modules: ModuleDef[] = [
  {
    key: 'documents',
    label: 'Documents',
    description: 'Procedures, work instructions, policies and controlled forms',
    icon: <DescriptionOutlined fontSize="small" />,
    basePath: '/documents',
    permissions: ['documents.view', 'forms.view'],
    sidebar: [
      { label: 'Library', path: '/documents', children: [
        { label: 'All documents', path: '/documents' },
        { label: 'Effective', path: '/documents?status=1' },
        { label: 'Drafts', path: '/documents?status=0' },
        { label: 'Obsolete', path: '/documents?status=2' },
      ] },
      { label: 'Content library', path: '/documents/content' },
      { label: 'Document types', path: '/documents/types', permission: 'admin.organization' },
      { label: 'Create document', path: '/documents/new', permission: 'documents.edit' },
      { label: 'Controlled forms', path: '/forms/records', permission: 'forms.view', children: [
        { label: 'Form records', path: '/forms/records', permission: 'forms.view' },
        { label: 'Fill in a form', path: '/forms/records/new', permission: 'forms.edit' },
        { label: 'Template library', path: '/forms/library', permission: 'forms.view' },
        { label: 'Import Center', path: '/imports', permission: 'imports.view' },
        { label: 'Form templates', path: '/forms/templates', permission: 'forms.view' },
        { label: 'New template', path: '/forms/templates/new', permission: 'forms.edit' },
      ] },
    ],
  },
  {
    key: 'quality',
    label: 'Quality Events',
    description: 'Nonconformances, dispositions and corrective and preventive actions',
    icon: <ReportProblemOutlined fontSize="small" />,
    basePath: '/ncr',
    permissions: ['ncr.view', 'capa.view'],
    sidebar: [
      { label: 'Nonconformances', path: '/ncr', permission: 'ncr.view', children: [
        { label: 'Open NCRs', path: '/ncr?view=open', permission: 'ncr.view' },
        { label: 'Awaiting disposition', path: '/ncr?view=2', permission: 'ncr.view' },
        { label: 'All NCRs', path: '/ncr?view=all', permission: 'ncr.view' },
        { label: 'Report nonconformance', path: '/ncr/new', permission: 'ncr.edit' },
      ] },
      { label: 'CAPA', path: '/capa', permission: 'capa.view', children: [
        { label: 'All CAPAs', path: '/capa', permission: 'capa.view' },
        { label: 'Effectiveness checks', path: '/capa?status=3', permission: 'capa.view' },
        { label: 'New CAPA', path: '/capa/new', permission: 'capa.edit' },
      ] },
    ],
  },
  {
    key: 'audits',
    label: 'Audits',
    description: 'Audit program, checklists, findings and corrective actions',
    icon: <FactCheckOutlined fontSize="small" />,
    basePath: '/audits',
    permissions: ['audits.view'],
    sidebar: [
      { label: 'Program', path: '/audits', children: [
        { label: 'All audits', path: '/audits' },
        { label: 'In progress', path: '/audits?status=1' },
        { label: 'Plan audit', path: '/audits/new', permission: 'audits.edit' },
      ] },
      { label: 'Findings', path: '/audits/findings', children: [
        { label: 'Open findings', path: '/audits/findings' },
        { label: 'All findings', path: '/audits/findings?all=1' },
      ] },
      { label: 'Annual programs', path: '/audits/programs' },
    ],
  },
  {
    key: 'training',
    label: 'Training',
    description: 'Courses, assignments, sign-off and retraining on document changes',
    icon: <SchoolOutlined fontSize="small" />,
    basePath: '/training',
    permissions: [],
    sidebar: [
      { label: 'My training', path: '/training' },
      { label: 'Administration', path: '/training/courses', permission: 'training.view', children: [
        { label: 'Courses', path: '/training/courses', permission: 'training.view' },
        { label: 'Training status', path: '/training/status', permission: 'training.view' },
        { label: 'New course', path: '/training/courses/new', permission: 'training.edit' },
      ] },
      { label: 'Workforce', path: '/training/skills', permission: 'training.view', children: [
        { label: 'Skills matrix', path: '/training/skills', permission: 'training.view' },
        { label: 'Curricula & programs', path: '/training/curricula', permission: 'training.view' },
      ] },
    ],
  },
  {
    key: 'calibration',
    label: 'Equipment',
    description: 'Equipment, tooling and gages: calibration, out-of-tolerance impact, preventive maintenance and repair history',
    icon: <SpeedOutlined fontSize="small" />,
    basePath: '/calibration/equipment',
    permissions: ['calibration.view'],
    sidebar: [
      { label: 'Equipment', path: '/calibration/equipment', children: [
        { label: 'All equipment', path: '/calibration/equipment' },
        { label: 'Calibration due', path: '/calibration/equipment?view=due' },
        { label: 'Maintenance due', path: '/calibration/equipment?view=pm' },
        { label: 'Quarantined', path: '/calibration/equipment?view=quarantined' },
        { label: 'Down', path: '/calibration/equipment?view=down' },
        { label: 'Add equipment', path: '/calibration/equipment/new', permission: 'calibration.edit' },
      ] },
    ],
  },
  {
    key: 'engineering',
    label: 'Products & Engineering',
    description: 'Products, parts, customers, engineering drawings and revisions',
    icon: <ArchitectureOutlined fontSize="small" />,
    basePath: '/products',
    permissions: ['drawings.view', 'masterdata.view'],
    sidebar: [
      { label: 'Product records', path: '/products/items', permission: 'masterdata.view', children: [
        { label: 'Products', path: '/products/items', permission: 'masterdata.view' },
        { label: 'Parts', path: '/products/parts', permission: 'masterdata.view' },
        { label: 'Customers', path: '/products/customers', permission: 'masterdata.view' },
        { label: 'New part', path: '/products/parts/new', permission: 'masterdata.edit' },
      ] },
      { label: 'Drawings', path: '/drawings', permission: 'drawings.view', children: [
        { label: 'All drawings', path: '/drawings' },
        { label: 'Released', path: '/drawings?status=1' },
        { label: 'In work', path: '/drawings?status=0' },
        { label: 'Obsolete', path: '/drawings?status=2' },
      ] },
      { label: 'New drawing', path: '/drawings/new', permission: 'drawings.edit' },
    ],
  },
  {
    key: 'suppliers',
    label: 'Suppliers',
    description: 'Supplier register, qualification, approved scope, certifications and restrictions',
    icon: <LocalShippingOutlined fontSize="small" />,
    basePath: '/suppliers',
    permissions: ['suppliers.view'],
    sidebar: [
      { label: 'Supplier register', path: '/suppliers', children: [
        { label: 'All suppliers', path: '/suppliers' },
        { label: 'Approved', path: '/suppliers?status=2' },
        { label: 'Pending approval', path: '/suppliers?status=1' },
        { label: 'Suspended', path: '/suppliers?status=4' },
        { label: 'Add supplier', path: '/suppliers/new', permission: 'suppliers.edit' },
      ] },
      { label: 'Corrective action requests', path: '/suppliers/scar', children: [
        { label: 'Open SCARs', path: '/suppliers/scar' },
        { label: 'All SCARs', path: '/suppliers/scar?all=1' },
      ] },
      { label: 'Periodic reviews due', path: '/suppliers/reviews-due' },
      { label: 'Performance', path: '/suppliers/performance', children: [
        { label: 'Scorecard', path: '/suppliers/performance' },
        { label: 'Development programs', path: '/suppliers/performance?view=programs' },
        { label: 'Weighting', path: '/suppliers/performance?view=weights' },
      ] },
    ],
  },
  {
    key: 'changes',
    label: 'Changes',
    description: 'Change requests, risk and impact, affected items, approval, implementation and post-implementation review',
    icon: <ChangeCircleOutlined fontSize="small" />,
    basePath: '/changes',
    permissions: ['changes.view'],
    sidebar: [
      { label: 'Change requests', path: '/changes', children: [
        { label: 'Open', path: '/changes' },
        { label: 'In review', path: '/changes?view=1' },
        { label: 'Implementation', path: '/changes?view=3' },
        { label: 'All changes', path: '/changes?view=all' },
        { label: 'Request change', path: '/changes/new', permission: 'changes.edit' },
      ] },
    ],
  },
  {
    key: 'inspection',
    label: 'Inspection',
    description: 'Revision-controlled inspection and test plans, receiving / in-process / final inspection, release and rejection',
    icon: <RuleOutlined fontSize="small" />,
    basePath: '/inspection',
    permissions: ['inspection.view'],
    sidebar: [
      { label: 'Inspections', path: '/inspection', children: [
        { label: 'Awaiting decision', path: '/inspection' },
        { label: 'Failed', path: '/inspection?view=2' },
        { label: 'All inspections', path: '/inspection?view=all' },
      ] },
      { label: 'Plans', path: '/inspection/plans', children: [
        { label: 'All plans', path: '/inspection/plans' },
        { label: 'New plan', path: '/inspection/plans/new', permission: 'inspection.edit' },
      ] },
    ],
  },
  {
    key: 'complaints',
    label: 'Complaints',
    description: 'Customer complaints, returns (RMA), returned-material evaluation and customer responses',
    icon: <SupportAgentOutlined fontSize="small" />,
    basePath: '/complaints',
    permissions: ['complaints.view'],
    sidebar: [
      { label: 'Complaints', path: '/complaints', children: [
        { label: 'Open', path: '/complaints' },
        { label: 'Awaiting customer', path: '/complaints?view=2' },
        { label: 'All complaints', path: '/complaints?view=all' },
        { label: 'Record complaint', path: '/complaints/new', permission: 'complaints.edit' },
      ] },
      { label: 'Trends', path: '/complaints/trends' },
    ],
  },
  {
    key: 'objectives',
    label: 'Performance',
    description: 'Quality objectives, KPIs with controlled calculations, and management reviews',
    icon: <FlagOutlined fontSize="small" />,
    basePath: '/objectives',
    permissions: ['objectives.view', 'reviews.view'],
    sidebar: [
      { label: 'Objectives & KPIs', path: '/objectives', permission: 'objectives.view', children: [
        { label: 'Active', path: '/objectives' },
        { label: 'Drafts', path: '/objectives?view=0' },
        { label: 'All', path: '/objectives?view=all' },
        { label: 'New objective', path: '/objectives/new', permission: 'objectives.edit' },
      ] },
      { label: 'Management review', path: '/reviews', permission: 'reviews.view', children: [
        { label: 'All reviews', path: '/reviews' },
        { label: 'Schedule review', path: '/reviews/new', permission: 'reviews.edit' },
      ] },
    ],
  },
  {
    key: 'risks',
    label: 'Risk',
    description: 'Risk and opportunity register, heat map, residual risk acceptance and periodic reviews',
    icon: <GridViewOutlined fontSize="small" />,
    basePath: '/risks',
    permissions: ['risks.view'],
    sidebar: [
      { label: 'Register', path: '/risks', children: [
        { label: 'Open', path: '/risks' },
        { label: 'Pending acceptance', path: '/risks?view=2' },
        { label: 'Reviews due', path: '/risks?view=review' },
        { label: 'Opportunities', path: '/risks?view=opportunities' },
        { label: 'All', path: '/risks?view=all' },
        { label: 'Add', path: '/risks/new', permission: 'risks.edit' },
      ] },
      { label: 'Heat map', path: '/risks/matrix' },
      { label: 'Matrix settings', path: '/risks/settings', permission: 'admin.organization' },
    ],
  },
  {
    key: 'faiaql',
    label: 'First Article & AQL',
    description: 'First article inspections with ballooned characteristics and signatures; AQL lot sampling, dispositions and supplier trends',
    icon: <StraightenOutlined fontSize="small" />,
    basePath: '/fai',
    permissions: ['fai.view', 'inspection.view'],
    sidebar: [
      { label: 'First articles', path: '/fai', permission: 'fai.view', children: [
        { label: 'Active', path: '/fai', permission: 'fai.view' },
        { label: 'Awaiting signatures', path: '/fai?view=pending', permission: 'fai.view' },
        { label: 'Overdue', path: '/fai?view=overdue', permission: 'fai.view' },
        { label: 'Start a first article', path: '/fai/new', permission: 'fai.edit' },
      ] },
      { label: 'AQL sampling', path: '/aql', permission: 'inspection.view', children: [
        { label: 'Open lots', path: '/aql', permission: 'inspection.view' },
        { label: 'Rejected lots', path: '/aql?view=rejected', permission: 'inspection.view' },
        { label: 'Sample a lot', path: '/aql/new', permission: 'inspection.edit' },
        { label: 'Sampling plans', path: '/aql/plans', permission: 'inspection.view' },
        { label: 'Supplier trends', path: '/aql/suppliers', permission: 'inspection.view' },
      ] },
      { label: 'Dashboard', path: '/fai/dashboard', permission: 'reports.view' },
    ],
  },
  {
    key: 'apqp',
    label: 'Process Planning',
    description: 'Process flows, PFMEA with action priority, and control plans under revision control',
    icon: <AccountTreeOutlined fontSize="small" />,
    basePath: '/apqp',
    permissions: ['apqp.view'],
    sidebar: [
      { label: 'Documents', path: '/apqp', children: [
        { label: 'Control plans', path: '/apqp?kind=plans' },
        { label: 'PFMEA', path: '/apqp?kind=pfmea' },
        { label: 'Process flows', path: '/apqp?kind=flows' },
      ] },
      { label: 'Start a document', path: '/apqp/new?kind=plans', permission: 'apqp.edit' },
    ],
  },
  {
    key: 'spc',
    label: 'SPC & MSA',
    description: 'Control charts with rule checks and capability, gage R&R, bias, linearity and stability studies',
    icon: <ShowChartOutlined fontSize="small" />,
    basePath: '/spc',
    permissions: ['spc.view'],
    sidebar: [
      { label: 'Control charts', path: '/spc', children: [
        { label: 'All charts', path: '/spc' },
        { label: 'New chart', path: '/spc/new', permission: 'spc.edit' },
      ] },
      { label: 'Measurement systems', path: '/spc/msa', children: [
        { label: 'All studies', path: '/spc/msa' },
        { label: 'Review due', path: '/spc/msa?due=1' },
        { label: 'New study', path: '/spc/msa/new', permission: 'spc.edit' },
      ] },
    ],
  },
  {
    key: 'lpa',
    label: 'Layered Audits',
    description: 'Layered process audit templates, schedules, completion and automatic NCR / CAPA on failure',
    icon: <LayersOutlined fontSize="small" />,
    basePath: '/lpa',
    permissions: ['lpa.view'],
    sidebar: [
      { label: 'Audits', path: '/lpa', children: [
        { label: 'Due', path: '/lpa' },
        { label: 'Missed', path: '/lpa?view=missed' },
        { label: 'All', path: '/lpa?view=all' },
      ] },
      { label: 'Templates', path: '/lpa?view=templates' },
    ],
  },
  {
    key: 'coq',
    label: 'Cost of Quality',
    description: 'Prevention, appraisal and failure costs, scrap and rework with approval and validation',
    icon: <PaidOutlined fontSize="small" />,
    basePath: '/coq',
    permissions: ['coq.view'],
    sidebar: [
      { label: 'Summary', path: '/coq', children: [
        { label: 'By month', path: '/coq' },
        { label: 'By category', path: '/coq?view=category' },
        { label: 'By product', path: '/coq?view=product' },
        { label: 'By supplier', path: '/coq?view=supplier' },
        { label: 'By customer', path: '/coq?view=customer' },
      ] },
      { label: 'Records', path: '/coq?view=entries', children: [
        { label: 'Cost entries', path: '/coq?view=entries' },
        { label: 'Scrap', path: '/coq?view=scrap' },
        { label: 'Rework orders', path: '/coq?view=rework' },
      ] },
    ],
  },
  {
    key: 'shopfloor',
    label: 'Shop Floor',
    description: 'Work orders, purchase orders, shop-floor data collection and the live quality monitor',
    icon: <PrecisionManufacturingOutlined fontSize="small" />,
    basePath: '/production',
    permissions: ['production.view'],
    sidebar: [
      { label: 'Live monitor', path: '/production' },
      { label: 'Record entry', path: '/production/entry', permission: 'production.edit' },
      { label: 'Orders', path: '/production/work-orders', children: [
        { label: 'Work orders', path: '/production/work-orders' },
        { label: 'Purchase orders', path: '/production/purchase-orders' },
        { label: 'New work order', path: '/production/work-orders/new', permission: 'production.edit' },
        { label: 'New purchase order', path: '/production/purchase-orders/new', permission: 'production.edit' },
      ] },
      { label: 'Entry log', path: '/production/entries' },
    ],
  },
  {
    key: 'trace',
    label: 'Traceability',
    description: 'Lots, serial numbers, genealogy, traceability search, barcode labels and scanning',
    icon: <QrCode2Outlined fontSize="small" />,
    basePath: '/trace',
    permissions: ['trace.view'],
    sidebar: [
      { label: 'Trace search', path: '/trace' },
      { label: 'Scan', path: '/trace/scan' },
      { label: 'Inventory identity', path: '/trace/lots', children: [
        { label: 'Lots & batches', path: '/trace/lots' },
        { label: 'Serial numbers', path: '/trace/serials' },
        { label: 'New lot', path: '/trace/lots/new', permission: 'trace.edit' },
        { label: 'New serial', path: '/trace/serials/new', permission: 'trace.edit' },
      ] },
      { label: 'Labels', path: '/trace/labels' },
    ],
  },
  {
    key: 'warehouse',
    label: 'Warehouse',
    description: 'Inventory ledger, locations, receiving and inspection, holds, MRB, returns to vendor, counts, transfers and scan stations',
    icon: <WarehouseOutlined fontSize="small" />,
    basePath: '/warehouse',
    permissions: ['inventory.view'],
    sidebar: [
      { label: 'Overview', path: '/warehouse' },
      { label: 'Scan station', path: '/warehouse/scan' },
      { label: 'Inventory', path: '/warehouse/stock', children: [
        { label: 'Stock', path: '/warehouse/stock' },
        { label: 'Work in process', path: '/warehouse/wip' },
        { label: 'Movement history', path: '/warehouse/ledger' },
        { label: 'Lot merge', path: '/warehouse/lot-merge', permission: 'inventory.edit' },
      ] },
      { label: 'Locations', path: '/warehouse/locations' },
      { label: 'Receiving', path: '/warehouse/receipts', children: [
        { label: 'Receipts', path: '/warehouse/receipts' },
        { label: 'Receiving rules', path: '/warehouse/receiving-rules' },
      ] },
      { label: 'Quality stock', path: '/warehouse/holds', children: [
        { label: 'Holds', path: '/warehouse/holds' },
        { label: 'MRB', path: '/warehouse/mrb' },
        { label: 'Returns to vendor', path: '/warehouse/rtv' },
      ] },
      { label: 'Counts', path: '/warehouse/counts' },
      { label: 'Transfers', path: '/warehouse/transfers' },
      { label: 'Label printing', path: '/warehouse/labels' },
    ],
  },
  {
    key: 'shipping',
    label: 'Shipping & Logistics',
    description: 'Shipments with pick, pack, verify and ship, packing lists and bills of lading, carriers and logistics reporting',
    icon: <Inventory2Outlined fontSize="small" />,
    basePath: '/shipping',
    permissions: ['shipping.view'],
    sidebar: [
      { label: 'Logistics overview', path: '/shipping' },
      { label: 'Shipments', path: '/shipping/shipments' },
      { label: 'New shipment', path: '/shipping/shipments/new', permission: 'shipping.edit' },
      { label: 'Carriers', path: '/shipping/carriers' },
      { label: 'Carrier connections', path: '/shipping/carrier-connections' },
      { label: 'Document templates', path: '/shipping/document-templates' },
    ],
  },
  {
    key: 'field',
    label: 'Field Quality',
    description: 'Recalls with lot trace and containment, customer returns (RMA) and warranty claims',
    icon: <AssignmentReturnOutlined fontSize="small" />,
    basePath: '/field/recalls',
    permissions: ['recalls.view', 'returns.view'],
    sidebar: [
      { label: 'Recalls', path: '/field/recalls', permission: 'recalls.view', children: [
        { label: 'All recalls', path: '/field/recalls', permission: 'recalls.view' },
        { label: 'Open a recall', path: '/field/recalls/new', permission: 'recalls.edit' },
      ] },
      { label: 'Returns (RMA)', path: '/field/returns', permission: 'returns.view', children: [
        { label: 'All returns', path: '/field/returns', permission: 'returns.view' },
        { label: 'Authorize a return', path: '/field/returns/new', permission: 'returns.edit' },
      ] },
      { label: 'Warranty', path: '/field/warranty', permission: 'returns.view', children: [
        { label: 'Claims', path: '/field/warranty', permission: 'returns.view' },
        { label: 'Warranty metrics', path: '/field/warranty/metrics', permission: 'returns.view' },
        { label: 'New claim', path: '/field/warranty/new', permission: 'returns.edit' },
      ] },
    ],
  },
  {
    key: 'integrations',
    label: 'Integrations',
    description: 'ERP and MES connections (off until enabled), field mappings, sync runs and the inbound change queue',
    icon: <SyncAltOutlined fontSize="small" />,
    basePath: '/integrations',
    permissions: ['integrations.view'],
    sidebar: [
      { label: 'Connections', path: '/integrations', children: [
        { label: 'All connections', path: '/integrations' },
        { label: 'New connection', path: '/integrations/new', permission: 'integrations.edit' },
        { label: 'Supported systems', path: '/integrations/adapters' },
      ] },
      { label: 'Change queue', path: '/integrations/changes', children: [
        { label: 'Pending', path: '/integrations/changes' },
        { label: 'In approval', path: '/integrations/changes?status=1' },
        { label: 'Applied', path: '/integrations/changes?status=2' },
        { label: 'Failed', path: '/integrations/changes?status=4' },
      ] },
    ],
  },
  {
    key: 'insights',
    label: 'Insights',
    description: 'Plant and executive quality metrics with drill-down, record relationship maps and trend history',
    icon: <HubOutlined fontSize="small" />,
    basePath: '/insights',
    permissions: ['analytics.view', 'assistant.use'],
    sidebar: [
      { label: 'Production metrics', path: '/insights', permission: 'analytics.view' },
      { label: 'Executive overview', path: '/insights?view=executive', permission: 'analytics.view' },
      { label: 'Relationships', path: '/insights/graph', permission: 'analytics.view' },
      { label: 'KPI library', path: '/insights/kpis', permission: 'analytics.view' },
      { label: 'Quality assistant', path: '/insights/assistant', permission: 'assistant.use', children: [
        { label: 'Ask', path: '/insights/assistant', permission: 'assistant.use' },
        { label: 'Investigate an NCR', path: '/insights/investigate', permission: 'assistant.use' },
        { label: 'Monthly narrative', path: '/insights/assistant?view=executive', permission: 'analytics.view' },
      ] },
      { label: 'Trends & risk', path: '/insights/risk', permission: 'analytics.view' },
      { label: 'Datasets', path: '/insights/datasets', permission: 'analytics.view' },
    ],
  },
  {
    key: 'reports',
    label: 'Reports',
    description: 'Quality dashboard, report library and CSV export, scoped to your sites',
    icon: <InsightsOutlined fontSize="small" />,
    basePath: '/reports',
    permissions: ['reports.view'],
    sidebar: [
      { label: 'Dashboard', path: '/reports' },
      { label: 'Report library', path: '/reports/catalog' },
    ],
  },
  {
    key: 'enterprise',
    label: 'Enterprise',
    description: 'Multi-site scorecards, war rooms, organization structure and corporate standards',
    icon: <HubOutlined fontSize="small" />,
    basePath: '/enterprise',
    permissions: ['enterprise.view'],
    sidebar: [
      { label: 'Plant scorecards', path: '/enterprise' },
      { label: 'Organization', path: '/enterprise/org' },
      { label: 'Corporate standards', path: '/enterprise/standards' },
    ],
  },
  {
    key: 'compliance',
    label: 'Compliance',
    description: 'Compliance packs, obligations, coverage and validation',
    icon: <RuleOutlined fontSize="small" />,
    basePath: '/compliance',
    permissions: ['compliance.view', 'validation.view'],
    sidebar: [
      { label: 'Compliance', path: '/compliance', permission: 'compliance.view' },
      { label: 'Validation', path: '/validation', permission: 'validation.view', children: [
        { label: 'All validations', path: '/validation', permission: 'validation.view' },
        { label: 'New validation', path: '/validation/new', permission: 'validation.edit' },
      ] },
    ],
  },
  {
    key: 'launch',
    label: 'Launch',
    description: 'APQP launch and transfer projects, PPAP and capability',
    icon: <FlagOutlined fontSize="small" />,
    basePath: '/launch',
    permissions: ['apqp.view'],
    sidebar: [
      { label: 'Projects', path: '/launch' },
      { label: 'PPAP', path: '/launch/ppap' },
      { label: 'Capability', path: '/launch/capability' },
    ],
  },
  {
    key: 'product-design',
    label: 'Engineering',
    description: 'Product requirements and traceability, design reviews, BOM quality and variants',
    icon: <DesignServicesOutlined fontSize="small" />,
    basePath: '/engineering',
    permissions: ['engineering.view'],
    sidebar: [
      { label: 'Requirements', path: '/engineering' },
      { label: 'Design reviews', path: '/engineering/reviews' },
      { label: 'BOM quality', path: '/engineering/bom', permission: 'masterdata.view' },
      { label: 'Variants', path: '/engineering/variants', permission: 'masterdata.view' },
    ],
  },
  {
    key: 'qos',
    label: 'Quality OS',
    description: 'Global command center, factory center, risk matrix, KPI framework, digital twin and board reports',
    icon: <PublicOutlined fontSize="small" />,
    basePath: '/qos',
    permissions: ['enterprise.view', 'risks.view', 'objectives.view', 'spc.view'],
    sidebar: [
      { label: 'Command center', path: '/qos', permission: 'enterprise.view' },
      { label: 'Factory center', path: '/qos/factory' },
      { label: 'Risk matrix', path: '/qos/risk', permission: 'risks.view' },
      { label: 'KPI framework', path: '/qos/kpis', permission: 'objectives.view' },
      { label: 'Digital twin', path: '/qos/twin' },
      { label: 'Board reports', path: '/qos/boards', permission: 'enterprise.view' },
    ],
  },
  {
    key: 'knowledge',
    label: 'Knowledge',
    description: 'Lessons learned, root-cause library, playbooks, business processes and maturity',
    icon: <MenuBookOutlined fontSize="small" />,
    basePath: '/knowledge',
    permissions: ['knowledge.view', 'process.view'],
    sidebar: [
      { label: 'Knowledge base', path: '/knowledge', permission: 'knowledge.view' },
      { label: 'Playbooks', path: '/knowledge/playbooks', permission: 'knowledge.view' },
      { label: 'Processes', path: '/processes', permission: 'process.view' },
      { label: 'Maturity', path: '/maturity', permission: 'process.view' },
    ],
  },
  {
    key: 'advisor',
    label: 'Advisor',
    description: 'Rule-based quality advisor: ask, explain, document assistant and audit preparation',
    icon: <PsychologyOutlined fontSize="small" />,
    basePath: '/advisor',
    permissions: ['assistant.use'],
    sidebar: [
      { label: 'Ask & explain', path: '/advisor' },
      { label: 'Document assistant', path: '/advisor/documents' },
      { label: 'Audit preparation', path: '/advisor/audit-prep' },
    ],
  },
  {
    key: 'governance',
    label: 'Governance',
    description: 'Part 11 readiness, retention, business continuity, API clients and channels',
    icon: <AdminPanelSettingsOutlined fontSize="small" />,
    basePath: '/governance',
    permissions: ['governance.view', 'audittrail.view', 'api.admin', 'security.review'],
    sidebar: [
      { label: 'Part 11 readiness', path: '/governance', permission: 'audittrail.view' },
      { label: 'Retention', path: '/governance/retention', permission: 'governance.view' },
      { label: 'Business continuity', path: '/governance/continuity', permission: 'governance.view' },
      { label: 'API & channels', path: '/governance/api', permission: 'api.admin' },
      { label: 'Access reviews', path: '/governance/access', permission: 'security.review' },
      { label: 'Deployments', path: '/governance/deployments', permission: 'security.review' },
    ],
  },
  {
    key: 'platform',
    label: 'Platform',
    description: 'Personal workspace, activity, calendar, resources, genealogy, designers, webhooks and operations',
    icon: <HubOutlined fontSize="small" />,
    basePath: '/platform',
    permissions: [],
    sidebar: [
      { label: 'My workspace', path: '/platform' },
      { label: 'Activity stream', path: '/platform/activity' },
      { label: 'Calendar', path: '/platform/calendar' },
      { label: 'Resources', path: '/platform/resources', permission: 'audits.view' },
      { label: 'Genealogy', path: '/platform/genealogy' },
      { label: 'Report designer', path: '/platform/reports', permission: 'reports.view' },
      { label: 'Report schedules', path: '/platform/report-schedules', permission: 'reports.view' },
      { label: 'Dashboards', path: '/platform/dashboards', permission: 'reports.view' },
      { label: 'Workflow designer', path: '/platform/workflows', permission: 'admin.organization' },
      { label: 'Form designer', path: '/platform/forms', permission: 'forms.edit' },
      { label: 'Business rules', path: '/platform/rules', permission: 'platform.admin' },
      { label: 'Data governance', path: '/platform/governance', permission: 'masterdata.view' },
      { label: 'Recovery center', path: '/platform/recovery', permission: 'platform.admin' },
      { label: 'Webhooks', path: '/platform/webhooks', permission: 'platform.admin' },
      { label: 'SMS and push', path: '/platform/messaging', permission: 'api.admin' },
      { label: 'Generated content', path: '/platform/generation', permission: 'platform.admin' },
      { label: 'System health', path: '/platform/health', permission: 'platform.admin' },
      { label: 'Security operations', path: '/platform/security', permission: 'security.review' },
      { label: 'Modules', path: '/platform/modules', permission: 'platform.admin' },
      { label: 'Marketplace', path: '/platform/marketplace', permission: 'platform.admin' },
    ],
  },
  {
    key: 'admin',
    label: 'Admin',
    description: 'Users, roles, sites, approval routes and logs',
    icon: <AdminPanelSettingsOutlined fontSize="small" />,
    basePath: '/admin',
    permissions: ['admin.users', 'admin.roles', 'portal.admin', 'admin.organization', 'audittrail.view', 'securitylog.view', 'alerts.admin'],
    sidebar: [
      { label: 'People', path: '/admin/users', children: [
        { label: 'Users', path: '/admin/users', permission: 'admin.users' },
        { label: 'Roles', path: '/admin/roles', permission: 'admin.roles' },
        { label: 'Portal accounts', path: '/admin/portal', permission: 'portal.admin' },
        { label: 'Sign-in security', path: '/admin/sign-in-security', permission: 'admin.users' },
        { label: 'Single sign-on', path: '/admin/single-sign-on', permission: 'admin.users' },
      ] },
      { label: 'Organization', path: '/admin/organization', children: [
        { label: 'Settings & branding', path: '/admin/organization', permission: 'admin.organization' },
        { label: 'Sites & departments', path: '/admin/sites', permission: 'admin.organization' },
        { label: 'Approval routes', path: '/admin/routes', permission: 'admin.organization' },
      ] },
      { label: 'Logs', path: '/admin/audit', children: [
        { label: 'Audit trail', path: '/admin/audit', permission: 'audittrail.view' },
        { label: 'Security events', path: '/admin/security', permission: 'securitylog.view' },
      ] },
      { label: 'Notifications', path: '/admin/alerts', permission: 'alerts.admin', children: [
        { label: 'Alert rules', path: '/admin/alerts', permission: 'alerts.admin' },
        { label: 'Raised alerts', path: '/admin/alerts?view=events', permission: 'alerts.admin' },
        { label: 'Integration points', path: '/admin/alerts?view=endpoints', permission: 'alerts.admin' },
      ] },
    ],
  },
];

/** Training is visible to everyone (each person has their own training); other modules need one of their permissions. */
export const visibleModules = (can: (p: string) => boolean) => modules.filter((m) => m.permissions.length === 0 || m.permissions.some(can));
