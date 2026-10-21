import { test, expect, type Page } from '@playwright/test';
import { Api, choose, dialog, localDate, shot, signIn, state, unique, users, type User } from './support';

const button = (p: Page, name: string) => p.getByRole('button', { name, exact: true });
/** Screenshots for review go in delete-shots when E2E_SCREENSHOT_DIR is set. */
const capture = (p: Page, name: string, fullPage?: boolean) => shot(p, `delete-shots/${name}`, { fullPage });

const manager: User = { key: 'manager', email: 'quality.manager@qms.test', password: 'manager-long-pass-1', name: 'Quinn Quality' };

/** A Quality Manager role holding the delete live quality records permission (granted by an administrator, not tied to the name). */
async function ensureManager() {
  const admin = await Api.as(users.admin);
  const roles = await admin.get<{ id: string; name: string }[]>('/api/admin/roles');
  if (!roles.some((r) => r.name === 'Quality Manager (delete rules)')) {
    const role = await admin.post<string>('/api/admin/roles', { name: 'Quality Manager (delete rules)', description: 'Quality Manager (UI tests)', permissions: ['ncr.view', 'ncr.edit', 'documents.view', 'records.delete_live'] });
    const id = await admin.post<string>('/api/admin/users', { email: manager.email, displayName: manager.name, password: manager.password, mustChangePassword: false });
    await admin.post(`/api/admin/users/${id}/roles`, { roleId: role, siteId: state().siteId, departmentId: null });
  }
  await admin.dispose();
}

const ncrBody = (title: string) => ({ title, description: 'Scratch on the housing face, lot 4471.', source: 'In-process', siteId: state().siteId, detectedOn: localDate(), severity: 0 });

test.describe('E2E 57: delete rules for drafts, live quality records and other records', () => {
  test.beforeAll(ensureManager);

  test('a draft shows Delete to the person working on it', async ({ page }) => {
    const author = await Api.as(users.author);
    const title = unique('Draft work instruction - deburr station');
    const id = await author.post<string>('/api/documents', { kind: 0, title, documentType: 'Work Instruction', siteId: state().siteId, departmentId: null, reviewIntervalMonths: 24, initialRevision: 'A', changeSummary: 'First draft' });
    await signIn(page, users.author);
    await page.goto(`/documents/${id}`);
    await expect(button(page, 'Delete')).toBeVisible();
    await capture(page, '01-draft-with-delete', false);
    await button(page, 'Delete').click();
    await page.getByTestId('delete-reason').fill('Started on the wrong template');
    await page.getByTestId('delete-confirm').click();
    await expect(page.getByTestId('arrival-notice')).toContainText('Deleted');
    expect(await author.status('GET', `/api/documents/${id}`)).toBe(404);
    await author.dispose();
  });

  test('a live NCR has no Delete for a normal user and is closed out with a note', async ({ page }) => {
    const author = await Api.as(users.author);
    const title = unique('Scratch on housing');
    const ncr = await author.post('/api/ncrs', ncrBody(title));
    await signIn(page, users.author);
    await page.goto(`/ncr/${ncr.id}`);
    await expect(button(page, 'Close out')).toBeVisible();
    await expect(button(page, 'Delete')).toHaveCount(0);
    await capture(page, '02-live-ncr-normal-user', false);
    await button(page, 'Close out').click();
    await choose(page, 'Outcome', /No fault found/);
    await page.getByTestId('close-out-note').fill('Parts re-measured on the CMM and all in tolerance.');
    await expect(page.getByTestId('close-out-confirm')).toBeEnabled();
    await capture(page, '02b-close-out-dialog', false);
    await page.getByTestId('close-out-confirm').click();
    await expect(page.getByTestId('closed-out')).toContainText('No fault found');
    await expect(button(page, 'Close out')).toHaveCount(0);
    await capture(page, '02c-closed-out', false);
    const admin = await Api.as(users.admin);
    const trail = await admin.get('/api/audit-trail?page=1&pageSize=20');
    expect(trail.items.some((e: { action: string; description: string }) => e.action === 'Closed out' && e.description.includes(title) && e.description.includes('No fault found'))).toBe(true);
    await admin.dispose(); await author.dispose();
  });

  test('a quality manager sees Delete on a live NCR and must give a reason', async ({ page, browser }) => {
    const author = await Api.as(users.author);
    const title = unique('Dent on cover');
    const ncr = await author.post('/api/ncrs', ncrBody(title));
    await signIn(page, manager);
    await page.goto(`/ncr/${ncr.id}`);
    await expect(button(page, 'Delete')).toBeVisible();
    await capture(page, '03-live-ncr-quality-manager', false);
    await button(page, 'Delete').click();
    await expect(page.getByTestId('delete-live-warning')).toBeVisible();
    await expect(page.getByTestId('delete-confirm')).toBeDisabled();
    await page.getByTestId('delete-reason').fill('Raised against a part from the other plant');
    await expect(page.getByTestId('delete-confirm')).toBeEnabled();
    await capture(page, '03b-reason-dialog', false);
    await page.getByTestId('delete-confirm').click();
    await expect(page.getByTestId('arrival-notice')).toContainText('Deleted');
    expect(await author.status('GET', `/api/ncrs/${ncr.id}`)).toBe(404);

    const ctx = await browser.newContext();
    const ap = await ctx.newPage();
    await signIn(ap, users.admin);
    await ap.goto('/admin/audit');
    await expect(ap.getByRole('main').getByText(/Raised against a part from the other plant/).first()).toBeVisible();
    await capture(ap, '05-audit-entry', false);
    await ctx.close(); await author.dispose();
  });

  test('an effective work instruction is deleted with a note', async ({ page }) => {
    const author = await Api.as(users.author);
    const approver = await Api.as(users.approver);
    const title = unique('Work instruction - torque the cover bolts');
    const id = await author.post<string>('/api/documents', { kind: 0, title, documentType: 'Work Instruction', siteId: state().siteId, departmentId: null, reviewIntervalMonths: 24, initialRevision: 'A', changeSummary: 'Initial release' });
    const doc = await author.get(`/api/documents/${id}`);
    await author.upload('DocumentRevision', doc.revisions[0].id, 'wi.pdf', '%PDF-1.4 work instruction');
    await author.post(`/api/documents/revisions/${doc.revisions[0].id}/submit`);
    await approver.sign(doc.number, users.approver.password);
    await signIn(page, users.admin);
    await page.goto(`/documents/${id}`);
    await button(page, 'Delete').click();
    await expect(dialog(page).getByLabel(/Note: why is it being deleted/)).toBeVisible();
    await page.getByTestId('delete-reason').fill('Replaced by the new assembly cell instruction WI-220');
    await expect(page.getByTestId('delete-confirm')).toBeEnabled();
    await capture(page, '04-work-instruction-delete-note', false);
    await page.getByTestId('delete-confirm').click();
    await expect(page.getByTestId('arrival-notice')).toContainText('Deleted');
    await author.dispose(); await approver.dispose();
  });

  test('the permission is in the Records card in Roles and Select all includes it', async ({ page }) => {
    await signIn(page, users.admin);
    await page.goto('/admin/roles');
    await page.getByRole('button', { name: 'New role' }).click();
    const dlg = dialog(page);
    const box = dlg.getByRole('checkbox', { name: 'records.delete_live' });
    await expect(box).toBeVisible();
    await box.scrollIntoViewIfNeeded();
    await dlg.getByTestId('select-all-records').locator('input').check();
    await expect(box).toBeChecked();
    await expect(dlg.getByRole('checkbox', { name: 'records.delete_locked' })).toBeChecked();
    await capture(page, '06-permission-in-roles', false);
    await dlg.getByTestId('select-all-records').locator('input').uncheck();
    await expect(box).not.toBeChecked();
  });
});
