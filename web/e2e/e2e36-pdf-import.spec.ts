import { test, expect } from '@playwright/test';
import { shot, signIn, unique, users } from './support';
import { finalInspectionPdf, scannedPdf } from './pdf-fixture';

/** Forms build B8: a printed PDF form analyzed into fields with confidence levels, reviewed and confirmed, then converted; a scanned PDF waits for OCR. */
test.describe('Forms B8: PDF and scanned form intake', () => {
  test('an author reviews and confirms the detections of a PDF form before it can be submitted, and an approver accepts the fidelity report', async ({ page, browser }) => {
    const title = unique('Final inspection report');
    await signIn(page, users.author);
    await page.goto('/imports');
    await page.getByTestId('import-file').setInputFiles({ name: 'Final inspection report.pdf', mimeType: 'application/pdf', buffer: finalInspectionPdf() });
    await page.getByRole('combobox', { name: 'Import mode' }).click();
    await page.getByRole('option', { name: 'Convert to an electronic form' }).click();
    await page.getByRole('button', { name: 'Upload and inspect' }).click();
    await expect(page).toHaveURL(/\/imports\/[0-9a-f-]{36}$/);
    await expect(page.getByText('Preview ready')).toBeVisible();

    // the original PDF beside the converted form, which keeps the fields, checkboxes, the totalled table and the sign-off blocks
    await expect(page.locator('iframe[title="Original document"]')).toBeVisible();
    await expect(page.getByLabel('Work order')).toBeVisible();
    await expect(page.getByLabel('Result – Accept')).toBeVisible();
    await expect(page.getByText(/need confirmation before the conversion can be submitted/)).toBeVisible();
    await expect(page.getByLabel('Differences')).toBeVisible();
    await shot(page, 'forms-import/b8-side-by-side');

    await page.getByRole('tab', { name: 'Check and approve' }).click();
    await expect(page.getByRole('button', { name: 'Submit for approval' })).toBeDisabled();

    await page.getByRole('tab', { name: /Detections \(\d+ to confirm\)/ }).click();
    const table = page.getByRole('table', { name: 'Detections' });
    await expect(table.getByText('Label with a colon and empty space after it').first()).toBeVisible();
    await expect(table.getByText(/Medium · \d+%/).first()).toBeVisible();
    await shot(page, 'forms-import/b8-detections');
    await page.getByLabel('Label for Inspector').fill('Inspector name');
    await page.getByRole('button', { name: 'Save review' }).click();
    await expect(page.getByRole('tab', { name: /Detections \(\d+ to confirm\)/ })).toBeVisible();
    await page.getByRole('button', { name: 'Confirm all' }).click();
    await expect(page.getByRole('tab', { name: 'Detections', exact: true })).toBeVisible();
    await expect(page.getByText('Every detection has been reviewed or has high confidence.')).toBeVisible();

    await page.getByRole('tab', { name: 'Pages' }).click();
    await expect(page.getByRole('table', { name: 'Pages' }).getByText('Text read')).toBeVisible();
    await shot(page, 'forms-import/b8-pages');

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
    await p2.getByLabel('I have reviewed the side-by-side preview and accept the fidelity report').check();
    await p2.getByRole('button', { name: 'Approve and import' }).click();
    await expect(p2.getByText(/Draft form template/)).toBeVisible();
    await shot(p2, 'forms-import/b8-template-created');
    await p2.getByRole('tab', { name: 'History' }).click();
    await expect(p2.getByRole('table', { name: 'History' }).getByText('DetectionsReviewed').first()).toBeVisible();
    await ctx.close();
  });

  test('a scanned PDF shows which pages need OCR when no provider is configured', async ({ page }) => {
    await signIn(page, users.author);
    await page.goto('/imports');
    await page.getByTestId('import-file').setInputFiles({ name: 'Scanned receiving form.pdf', mimeType: 'application/pdf', buffer: scannedPdf() });
    await page.getByRole('combobox', { name: 'Import mode' }).click();
    await page.getByRole('option', { name: 'Convert to an electronic form' }).click();
    await page.getByRole('button', { name: 'Upload and inspect' }).click();
    await expect(page.getByText('Preview ready').first()).toBeVisible();
    await page.getByRole('tab', { name: 'Pages' }).click();
    await expect(page.getByRole('table', { name: 'Pages' }).getByText('Needs OCR')).toBeVisible();
    await expect(page.getByText(/OCR is not configured on this server, so pages without a text layer were not recognized/)).toBeVisible();
    await expect(page.getByRole('link', { name: /Page 1 picture 1/ })).toBeVisible();
    await shot(page, 'forms-import/b8-scanned-needs-ocr');
    await page.getByRole('tab', { name: /Issues/ }).click();
    await expect(page.getByRole('table', { name: 'Issues' }).getByText('CONVERSION')).toBeVisible();
  });
});
