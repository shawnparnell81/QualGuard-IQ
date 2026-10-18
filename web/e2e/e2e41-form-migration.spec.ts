import { test, expect } from '@playwright/test';
import { Api, dialog, shot, signIn, state, unique, users } from './support';

const V1 = [
  { key: 'lot', label: 'Lot', type: 'text', required: true },
  { key: 'a', label: 'Reading A', type: 'number', min: 0, max: 50 },
  { key: 'b', label: 'Reading B', type: 'number', min: 0, max: 50 },
  { key: 'total', label: 'Total', type: 'calc', formula: 'a+b' },
  { key: 'result', label: 'Result', type: 'choice', options: ['Accept', 'Reject'] },
  { key: 'retest', label: 'Retest done', type: 'boolean' },
  { key: 'why', label: 'Reject reason', type: 'text', visibleWhen: { field: 'result', operator: 'equals', value: 'Reject' } },
];

/** Forms build B13: opt-in form schema migration wizard (snapshot, new draft revision, comparison, approval, rollback). */
test.describe('Forms B13: form schema migration', () => {
  test('a template on the earlier schema is migrated on request, compared side by side, approved and rolled back', async ({ page }) => {
    test.setTimeout(180_000);
    const author = await Api.as(users.author);
    const approver = await Api.as(users.approver);
    const title = unique('Bond strength check');
    const id = await author.post<string>('/api/documents', { kind: 2, title, documentType: 'Inspection Record', siteId: state().siteId, departmentId: null, reviewIntervalMonths: 24, initialRevision: 'A', changeSummary: 'Initial release' });
    const doc = await author.get(`/api/documents/${id}`);
    const rev = doc.revisions[0];
    await author.put(`/api/documents/revisions/${rev.id}`, { changeSummary: 'Initial release', effectiveDate: null, trainingRequired: false, rowVersion: rev.rowVersion, fieldSchema: JSON.stringify(V1) });
    await author.post(`/api/documents/revisions/${rev.id}/submit`);
    await approver.sign(doc.number, users.approver.password);

    // nothing is converted automatically: the template is listed as a candidate
    await signIn(page, users.author);
    await page.goto('/forms/library');
    await page.getByRole('link', { name: /Schema migration/ }).click();
    await expect(page).toHaveURL(/\/forms\/migration$/);
    const cands = page.getByRole('table', { name: 'Templates to migrate' });
    const row = cands.getByRole('row').filter({ hasText: doc.number });
    await expect(row).toContainText('Version 1');
    await row.getByRole('button', { name: 'Migrate' }).click();
    await expect(dialog(page)).toContainText('Revision A stays effective and unchanged');
    await dialog(page).getByRole('button', { name: 'Start migration' }).click();

    // comparison, snapshot and side-by-side preview of the original and the migrated revision
    await expect(page).toHaveURL(/\/forms\/migration\/[0-9a-f-]{36}$/);
    await expect(page.getByText('Conversion summary')).toBeVisible();
    await expect(page.getByText('Matches the original')).toBeVisible();
    await expect(page.getByLabel('Original · revision A')).toBeVisible();
    await expect(page.getByLabel('Migrated · revision B')).toBeVisible();
    const cmp = page.getByRole('table', { name: 'Field comparison' });
    await expect(cmp.locator('tbody tr').filter({ hasText: 'Total' })).toContainText('a+b');
    await expect(cmp.locator('tbody tr').filter({ hasText: 'Result' })).toContainText('Accept, Reject');
    await expect(cmp.locator('tbody tr').filter({ hasText: 'Reject reason' })).toContainText('Yes');
    await expect(cmp.getByText('Kept, placed in a section')).toHaveCount(V1.length);
    await shot(page, 'forms-import/b13-migration-comparison');

    // the migrated revision goes through the normal approval route
    await page.getByRole('button', { name: 'Submit for approval' }).click();
    await expect(page.getByText(/Revision B is in review/)).toBeVisible();
    await approver.sign(doc.number, users.approver.password);

    // the approver rolls the migration back; a new draft revision restores the original definition
    await page.context().clearCookies();
    await signIn(page, users.approver);
    await page.goto('/forms/migration');
    await page.getByRole('tab', { name: 'Migration history' }).click();
    await page.getByRole('table', { name: 'Migration history' }).getByRole('row').filter({ hasText: doc.number }).click();
    await expect(page.getByText('Effective', { exact: true }).first()).toBeVisible();
    await page.getByRole('button', { name: 'Roll back' }).click();
    await dialog(page).getByLabel('Reason').fill('Print layout to be reviewed with production first');
    await dialog(page).getByRole('button', { name: 'Roll back' }).click();
    await expect(page.getByText(/Revision C restores the original definition/)).toBeVisible();
    await shot(page, 'forms-import/b13-migration-rolled-back');
    const after = await author.get(`/api/documents/${id}`);
    const c = after.revisions.find((r: { revision: string }) => r.revision === 'C');
    expect(JSON.parse(c.fieldSchema)).toEqual(V1);
    await author.dispose(); await approver.dispose();
  });
});
