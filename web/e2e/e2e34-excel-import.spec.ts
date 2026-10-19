import { test, expect } from '@playwright/test';
import { Api, shot, signIn, state, unique, users } from './support';
import { receivingInspectionWorkbook } from './xlsx-fixture';

/** Forms build B6: an Excel inspection sheet converted to an electronic form with side-by-side preview, fidelity report and approval; offline workbook round trip. */
test.describe('Forms B6: Excel conversion', () => {
  test('an author converts a workbook to a form, checks it side by side, and an approver creates the draft template', async ({ page, browser }) => {
    const title = unique('Receiving inspection');
    await signIn(page, users.author);
    await page.goto('/imports');
    await page.getByTestId('import-file').setInputFiles({ name: 'Receiving inspection.xlsx', mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: receivingInspectionWorkbook() });
    await page.getByRole('combobox', { name: 'Import mode' }).click();
    await page.getByRole('option', { name: 'Convert to an electronic form' }).click();
    await page.getByRole('button', { name: 'Upload and inspect' }).click();
    await expect(page).toHaveURL(/\/imports\/[0-9a-f-]{36}$/);
    await expect(page.getByText('Preview ready')).toBeVisible();

    // side by side: the original sheet and the converted form
    const original = page.getByRole('table', { name: 'Worksheet Receiving inspection' });
    await expect(original.getByText('RECEIVING INSPECTION')).toBeVisible();
    await expect(original.locator('td[data-cell="E4"] [aria-label="formula"]')).toHaveCount(1);
    await expect(original.locator('td[data-cell="B5"] [aria-label="validation"]')).toHaveCount(1);
    await page.getByLabel('Lot qty').fill('120');
    await page.getByRole('button', { name: 'Calculate' }).click();
    await expect(page.getByLabel('Sample size')).toHaveValue('13');
    await shot(page, 'forms-import/b6-side-by-side');
    await page.getByRole('tab', { name: 'Print layout' }).click();
    await expect(page.locator('iframe[title="Converted form print layout"]')).toBeVisible();

    await page.getByRole('tab', { name: 'Fidelity report' }).click();
    await page.getByLabel('Show').click();
    await page.getByRole('option', { name: 'Converted', exact: true }).click();
    await expect(page.getByRole('table', { name: 'Fidelity report' }).getByText(/Validation list became a dropdown with 3 options/)).toBeVisible();
    await expect(page.getByRole('table', { name: 'Field mapping report' }).getByText('Receiving inspection!B3')).toBeVisible();
    await shot(page, 'forms-import/b6-fidelity-report');
    await page.getByRole('tab', { name: 'Workbook' }).click();
    await expect(page.getByRole('table', { name: 'Formula inventory' }).getByText('=SUM(D8:D10)')).toBeVisible();

    await page.getByRole('tab', { name: 'Check and approve' }).click();
    await page.getByLabel('Form title').fill(title);
    // the page still shows the old "Preview ready" until the settings are saved and the workbook converted again
    const applied = page.waitForResponse((r) => r.url().includes('/excel-settings') && r.request().method() === 'PUT');
    await page.getByRole('button', { name: 'Apply' }).click();
    expect((await applied).ok()).toBe(true);
    await expect(page.getByText('Preview ready')).toBeVisible();
    await page.getByRole('tab', { name: 'Check and approve' }).click();
    await page.getByRole('button', { name: 'Submit for approval' }).click();
    await expect(page.getByText('Waiting for approval by someone other than the uploader.')).toBeVisible();
    const url = page.url();

    const ctx = await browser.newContext();
    const p2 = await ctx.newPage();
    await signIn(p2, users.approver);
    await p2.goto(url);
    await p2.getByRole('tab', { name: 'Check and approve' }).click();
    await expect(p2.getByRole('button', { name: 'Approve and import' })).toBeDisabled();
    await p2.getByLabel('I have reviewed the side-by-side preview and accept the fidelity report').check();
    await p2.getByRole('button', { name: 'Approve and import' }).click();
    await expect(p2.getByText(/Draft form template/)).toBeVisible();
    await shot(p2, 'forms-import/b6-template-created');
    await p2.getByRole('link', { name: /^FT-/ }).click();
    await expect(p2.getByRole('heading', { name: new RegExp(title) })).toBeVisible();
    await ctx.close();
  });

  test('a completed offline workbook comes back as a draft record and cannot be submitted twice', async ({ page }) => {
    const author = await Api.as(users.author);
    const approver = await Api.as(users.approver);
    const title = unique('Offline lot check');
    const id = await author.post<string>('/api/documents', { kind: 2, title, documentType: 'Inspection Record', siteId: state().siteId, departmentId: null, reviewIntervalMonths: 24, initialRevision: 'A', changeSummary: 'Initial release' });
    const doc = await author.get(`/api/documents/${id}`);
    const rev = doc.revisions[0];
    const schema = { schemaVersion: 2, sections: [{ key: 'main', title: 'Lot', columns: 2 }], fields: [
      { key: 'lot', label: 'Lot number', type: 'text', section: 'main', required: true },
      { key: 'qty', label: 'Quantity', type: 'integer', section: 'main' }] };
    await author.put(`/api/documents/revisions/${rev.id}`, { changeSummary: 'Initial release', effectiveDate: null, trainingRequired: false, rowVersion: rev.rowVersion, fieldSchema: JSON.stringify(schema) });
    await author.post(`/api/documents/revisions/${rev.id}/submit`);
    await approver.sign(doc.number, users.approver.password);

    await signIn(page, users.author);
    await page.goto(`/forms/templates/${id}`);
    await page.getByRole('tab', { name: /Revisions/ }).click().catch(() => undefined);
    const download = page.waitForEvent('download');
    await page.getByRole('link', { name: 'Download offline workbook' }).click();
    const file = await (await download).path();
    await expect(page.getByRole('button', { name: 'Submit completed workbook' })).toBeVisible();
    await shot(page, 'forms-import/b6-offline-workbook');
    // the blank copy is a valid submission (only the input cells are read); a second submission of the same copy is refused
    await page.getByTestId('offline-workbook').setInputFiles(file!);
    await expect(page).toHaveURL(/\/forms\/records\/[0-9a-f-]{36}$/);
    await page.goto(`/forms/templates/${id}`);
    await page.getByRole('tab', { name: /Revisions/ }).click().catch(() => undefined);
    await page.getByTestId('offline-workbook').setInputFiles(file!);
    await expect(page.getByText('This offline copy has already been submitted.')).toBeVisible();
  });
});
