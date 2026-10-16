import type { ReactNode } from 'react';
import DescriptionOutlined from '@mui/icons-material/DescriptionOutlined';
import ArchitectureOutlined from '@mui/icons-material/ArchitectureOutlined';
import AdminPanelSettingsOutlined from '@mui/icons-material/AdminPanelSettingsOutlined';

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
    permissions: ['documents.view'],
    sidebar: [
      { label: 'Library', path: '/documents', children: [
        { label: 'All documents', path: '/documents' },
        { label: 'Effective', path: '/documents?status=1' },
        { label: 'Drafts', path: '/documents?status=0' },
        { label: 'Obsolete', path: '/documents?status=2' },
      ] },
      { label: 'Create document', path: '/documents/new', permission: 'documents.edit' },
    ],
  },
  {
    key: 'engineering',
    label: 'Products & Engineering',
    description: 'Engineering drawings and controlled revisions',
    icon: <ArchitectureOutlined fontSize="small" />,
    basePath: '/drawings',
    permissions: ['drawings.view'],
    sidebar: [
      { label: 'Drawings', path: '/drawings', children: [
        { label: 'All drawings', path: '/drawings' },
        { label: 'Released', path: '/drawings?status=1' },
        { label: 'In work', path: '/drawings?status=0' },
        { label: 'Obsolete', path: '/drawings?status=2' },
      ] },
      { label: 'New drawing', path: '/drawings/new', permission: 'drawings.edit' },
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

export const visibleModules = (can: (p: string) => boolean) => modules.filter((m) => m.permissions.some(can));
