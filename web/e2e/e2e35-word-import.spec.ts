import { test, expect } from '@playwright/test';
import { shot, signIn, unique, users } from './support';
import { nonconformanceReportDocx } from './docx-fixture';

const DOCX = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';

/** Forms build B7: a Word nonconformance report converted to an electronic form, compared side by side with the original, and approved only with the fidelity report accepted. */
test.describe('Forms B7: Word conversion', () => {
  test('an author converts a Word form, checks it beside the original, and an approver accepts the fidelity report', async ({ page, browser }) => {
    const title = unique('Nonconformance report');
    await signIn(page, users.author);
    await page.goto('/imports');
    await page.getByTestId('import-file').setInputFiles({ name: 'Nonconformance report.docx', mimeType: DOCX, buffer: nonconformanceReportDocx() });
    await page.getByRole('combobox', { name: 'Import mode' }).click();
    await page.getByRole('option', { name: 'Convert to an electronic form' }).click();
    await page.getByRole('button', { name: 'Upload and inspect' }).click();
    await expect(page).toHaveURL(/\/imports\/[0-9a-f-]{36}$/);
    await expect(page.getByText('Preview ready')).toBeVisible();

    // the original is rendered by the server; the converted form keeps dropdown, date, checkbox and the calculated reject rate
    await expect(page.locator('iframe[title="Original document"]')).toBeVisible({ timeout: 60_000 });
    await expect(page.getByLabel('Disposition')).toBeVisible();
    await page.getByLabel('Lot size').fill('200');
    await page.getByLabel('Rejected').fill('10');
    await page.getByRole('button', { name: 'Calculate' }).click();
    await expect(page.getByLabel('Reject rate %')).toHaveValue('5');
    await expect(page.getByLabel('Customer notified')).toBeVisible();
    await expect(page.getByLabel('Differences')).toContainText('Missing fonts');
    await shot(page, 'forms-import/b7-side-by-side');

    await page.getByRole('tab', { name: 'Fidelity report' }).click();
    await page.getByLabel('Show').click();
    await page.getByRole('option', { name: 'Converted', exact: true }).click();
    await expect(page.getByRole('table', { name: 'Fidelity report' }).getByText(/became a dropdown field 'disposition'/)).toBeVisible();
    await shot(page, 'forms-import/b7-fidelity-report');
    await page.getByRole('tab', { name: 'Document' }).click();
    await expect(page.getByRole('table', { name: 'Content controls' }).getByText('date_found')).toBeVisible();

    await page.getByRole('tab', { name: 'Check and approve' }).click();
    await page.getByLabel('Form title').fill(title);
    await page.getByRole('button', { name: 'Apply' }).click();
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
    await shot(p2, 'forms-import/b7-template-created');
    await p2.getByRole('tab', { name: 'History' }).click();
    await expect(p2.getByRole('table', { name: 'History' }).getByText('FidelityAccepted')).toBeVisible();
    await ctx.close();
  });

  test('a Word document can be kept as a controlled document', async ({ page }) => {
    await signIn(page, users.author);
    await page.goto('/imports');
    await page.getByTestId('import-file').setInputFiles({ name: 'NCR procedure form.docx', mimeType: DOCX, buffer: nonconformanceReportDocx() });
    await page.getByRole('combobox', { name: 'Import mode' }).click();
    await page.getByRole('option', { name: 'Keep the document as a controlled document' }).click();
    await page.getByRole('button', { name: 'Upload and inspect' }).click();
    await expect(page.getByText('Preview ready')).toBeVisible();
    await expect(page.getByText(/paragraphs, 3 tables/)).toBeVisible();
    await page.getByRole('tab', { name: 'Document' }).click();
    await expect(page.getByRole('table', { name: 'Document tables' })).toBeVisible();
    await shot(page, 'forms-import/b7-document-inventory');
  });
});
