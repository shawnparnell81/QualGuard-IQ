import { Api, saveState, users } from './support';

const view = ['documents.view', 'drawings.view', 'forms.view', 'imports.view', 'ncr.view', 'capa.view', 'audits.view', 'training.view', 'calibration.view', 'masterdata.view', 'suppliers.view', 'complaints.view', 'inspection.view', 'changes.view', 'production.view', 'trace.view', 'recalls.view', 'returns.view', 'integrations.view', 'analytics.view', 'inventory.view', 'shipping.view', 'enterprise.view', 'compliance.view', 'validation.view', 'apqp.view', 'governance.view', 'engineering.view', 'knowledge.view', 'process.view', 'risks.view', 'objectives.view', 'spc.view', 'reports.view', 'qualityops.view'];
const edit = ['documents.edit', 'drawings.edit', 'forms.edit', 'imports.edit', 'ncr.edit', 'capa.edit', 'audits.edit', 'training.edit', 'calibration.edit', 'masterdata.edit', 'suppliers.edit', 'complaints.edit', 'inspection.edit', 'changes.edit', 'production.edit', 'trace.edit', 'recalls.edit', 'returns.edit', 'integrations.edit', 'assistant.use', 'inventory.edit', 'inventory.receive_blind', 'shipping.edit', 'enterprise.edit', 'compliance.edit', 'validation.edit', 'apqp.edit', 'governance.edit', 'engineering.edit', 'knowledge.edit', 'process.edit', 'reports.share', 'templates.install', 'templates.export', 'qualityops.edit', 'qualityevents.report', 'imports.download_original', 'imports.update_records', 'content.confidential'];
const approve = ['documents.approve', 'drawings.approve', 'forms.approve', 'imports.approve', 'ncr.approve', 'capa.approve', 'audits.approve', 'calibration.approve', 'suppliers.approve', 'complaints.approve', 'inspection.approve', 'changes.approve', 'recalls.approve', 'returns.approve', 'integrations.approve', 'inventory.approve', 'shipping.approve', 'validation.approve', 'apqp.approve', 'engineering.approve', 'qualityops.approve', 'imports.update_records', 'content.confidential'];

/** Creates the site, roles and people the UI tests sign in as. Runs once against a fresh database. */
export default async function globalSetup() {
  const admin = await Api.as(users.admin);
  const siteId = await admin.post<string>('/api/organization/sites', { code: 'E2E', name: 'Test Plant', address: null, timeZone: 'America/New_York' });
  const role = (name: string, permissions: string[]) => admin.post<string>('/api/admin/roles', { name, description: `${name} (UI tests)`, permissions });
  const roles = {
    author: await role('Quality Engineer', [...view, ...edit]),
    approver: await role('Quality Approver', [...view, ...approve]),
    viewer: await role('Read Only', view),
  };
  const ids: Record<string, string> = {};
  for (const u of [users.author, users.approver, users.viewer, users.outsider]) {
    ids[u.key] = await admin.post<string>('/api/admin/users', { email: u.email, displayName: u.name, password: u.password, mustChangePassword: false });
    const r = roles[u.key as keyof typeof roles];
    if (r) await admin.post(`/api/admin/users/${ids[u.key]}/roles`, { roleId: r, siteId, departmentId: null });
  }
  saveState({ siteId, ids });
  await admin.dispose();
}
