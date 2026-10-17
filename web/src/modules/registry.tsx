import type { ReactNode } from 'react';
import DescriptionOutlined from '@mui/icons-material/DescriptionOutlined';
import ArchitectureOutlined from '@mui/icons-material/ArchitectureOutlined';
import AdminPanelSettingsOutlined from '@mui/icons-material/AdminPanelSettingsOutlined';
import ReportProblemOutlined from '@mui/icons-material/ReportProblemOutlined';
import FactCheckOutlined from '@mui/icons-material/FactCheckOutlined';
import SchoolOutlined from '@mui/icons-material/SchoolOutlined';
import SpeedOutlined from '@mui/icons-material/SpeedOutlined';
import LocalShippingOutlined from '@mui/icons-material/LocalShippingOutlined';
import SupportAgentOutlined from '@mui/icons-material/SupportAgentOutlined';
import RuleOutlined from '@mui/icons-material/RuleOutlined';
import ChangeCircleOutlined from '@mui/icons-material/ChangeCircleOutlined';

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
      { label: 'Create document', path: '/documents/new', permission: 'documents.edit' },
      { label: 'Controlled forms', path: '/forms/records', permission: 'forms.view', children: [
        { label: 'Form records', path: '/forms/records', permission: 'forms.view' },
        { label: 'Fill in a form', path: '/forms/records/new', permission: 'forms.edit' },
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
    ],
  },
  {
    key: 'calibration',
    label: 'Calibration',
    description: 'Measuring equipment, calibration records and out-of-tolerance impact',
    icon: <SpeedOutlined fontSize="small" />,
    basePath: '/calibration/equipment',
    permissions: ['calibration.view'],
    sidebar: [
      { label: 'Equipment', path: '/calibration/equipment', children: [
        { label: 'All equipment', path: '/calibration/equipment' },
        { label: 'Due in 30 days', path: '/calibration/equipment?view=due' },
        { label: 'Quarantined', path: '/calibration/equipment?view=quarantined' },
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
    key: 'admin',
    label: 'Admin',
    description: 'Users, roles, sites, approval routes and logs',
    icon: <AdminPanelSettingsOutlined fontSize="small" />,
    basePath: '/admin',
    permissions: ['admin.users', 'admin.roles', 'admin.organization', 'audittrail.view', 'securitylog.view'],
    sidebar: [
      { label: 'People', path: '/admin/users', children: [
        { label: 'Users', path: '/admin/users', permission: 'admin.users' },
        { label: 'Roles', path: '/admin/roles', permission: 'admin.roles' },
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
    ],
  },
];

/** Training is visible to everyone (each person has their own training); other modules need one of their permissions. */
export const visibleModules = (can: (p: string) => boolean) => modules.filter((m) => m.permissions.length === 0 || m.permissions.some(can));
