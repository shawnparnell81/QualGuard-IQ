import { test, expect } from '@playwright/test';
import { Api, shot, signIn, state, unique, users } from './support';

/** Forms build B5: Import Center with CSV — upload, inspection, settings, mapping, dry run, approval by someone else, import and rollback. */
test.describe('Forms B5: Import Center', () => {
  test('an author imports a semicolon CSV into form records; an approver approves and the records are created', async ({ page, browser }) => {
    const author = await Api.as(users.author);
    const approver = await Api.as(users.approver);
    const title = unique('Torque log');
    const id = await author.post<string>('/api/documents', { kind: 2, title, documentType: 'Inspection Record', siteId: state().siteId, departmentId: null, reviewIntervalMonths: 24, initialRevision: 'A', changeSummary: 'Initial release' });
    const doc = await author.get(`/api/documents/${id}`);
    const rev = doc.revisions[0];
    const schema = { schemaVersion: 2, sections: [{ key: 'main', title: 'Main', columns: 2 }], fields: [
      { key: 'part', label: 'Part number', type: 'text', section: 'main', required: true },
      { key: 'torque', label: 'Torque', type: 'number', section: 'main', required: true },
      { key: 'checked_on', label: 'Checked on', type: 'date', section: 'main' }] };
    await author.put(`/api/documents/revisions/${rev.id}`, { changeSummary: 'Initial release', effectiveDate: null, trainingRequired: false, rowVersion: rev.rowVersion, fieldSchema: JSON.stringify(schema) });
    await author.post(`/api/documents/revisions/${rev.id}/submit`);
    await approver.sign(doc.number, users.approver.password);

    await signIn(page, users.author);
    await page.goto('/imports');
    await page.getByTestId('import-file').setInputFiles({ name: 'torque.csv', mimeType: 'text/csv', buffer: Buffer.from('Part number;Torque;Checked on\nP-1;42,5;01/10/2026\nP-2;abc;02/10/2026\nP-3;12;03/10/2026\n') });
    await page.getByLabel('Form template').click();
    await page.getByRole('option', { name: new RegExp(title) }).click();
    await page.getByRole('button', { name: 'Upload and inspect' }).click();
    await expect(page).toHaveURL(/\/imports\/[0-9a-f-]{36}$/);
    await expect(page.getByText('Mapping required')).toBeVisible();
    await expect(page.getByLabel('Delimiter')).toHaveText('Semicolon');
    await expect(page.getByRole('combobox', { name: 'Map Part number' })).toHaveText('Part number *');
    await page.getByLabel('Date format').click();
    await page.getByRole('option', { name: 'dd/MM/yyyy' }).click();
    await page.getByLabel('Transformations for Part number').fill('trim|upper');
    await page.getByRole('button', { name: 'Validate (dry run)' }).click();
    await expect(page.getByText(/Dry run: 2 of 3 rows ready/)).toBeVisible();
    await expect(page.getByRole('table', { name: 'Preview' }).getByText('2026-10-01')).toBeVisible();
    await shot(page, 'forms-import/b5-dry-run');
    await page.getByLabel('Import only the rows without problems').check();
    await page.getByRole('button', { name: 'Submit for approval' }).click();
    await expect(page.getByText('Waiting for approval by someone other than the uploader.')).toBeVisible();
    const url = page.url();

    const ctx = await browser.newContext();
    const p2 = await ctx.newPage();
    await signIn(p2, users.approver);
    await p2.goto(url);
    await p2.getByRole('tab', { name: 'Preview and validation' }).click();
    await p2.getByRole('button', { name: 'Approve and import' }).click();
    await expect(p2.getByText('Partially imported')).toBeVisible();
    await expect(p2.getByText(/2 records created, 1 rows skipped/)).toBeVisible();
    await p2.getByRole('tab', { name: 'History' }).click();
    await expect(p2.getByText('Awaiting approval → Importing')).toBeVisible();
    await shot(p2, 'forms-import/b5-imported');
    await p2.getByRole('tab', { name: 'Preview and validation' }).click();
    await p2.getByLabel('Reason to roll back').fill('Wrong week');
    await p2.getByRole('button', { name: 'Roll back' }).click();
    await expect(p2.getByText('Rolled back', { exact: true })).toBeVisible();
    await ctx.close();
  });

  test('viewers see imports but cannot upload; outsiders cannot open the Import Center', async ({ page }) => {
    await signIn(page, users.viewer);
    await page.goto('/imports');
    await expect(page.getByRole('table', { name: 'Import jobs' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Upload and inspect' })).toHaveCount(0);
    const outsider = await Api.as(users.outsider);
    expect(await outsider.status('GET', '/api/imports')).toBe(403);
  });
});
