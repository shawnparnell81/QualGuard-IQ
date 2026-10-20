import { test, expect } from '@playwright/test';
import { signIn, users } from './support';
import { longQuestionnaireWorkbook } from './xlsx-fixture';

const XLSX = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';

/** Uploading a workbook never just does nothing: the page says why the upload button can't be used yet, and a workbook too big for one form says so and lets the worksheets be chosen on the same import. */
test.describe('Excel upload feedback', () => {
  test('the upload button explains itself, and a too-large workbook can be narrowed to one worksheet', async ({ page }) => {
    await signIn(page, users.author);
    await page.goto('/imports');
    await page.getByTestId('import-file').setInputFiles({ name: 'Supplier questionnaire.xlsx', mimeType: XLSX, buffer: longQuestionnaireWorkbook() });

    // rows into form records need a template: the reason is shown under the disabled button
    await page.getByRole('combobox', { name: 'Import mode' }).click();
    await page.getByRole('option', { name: 'Import rows as records' }).click();
    await expect(page.getByRole('button', { name: 'Upload and inspect' })).toBeDisabled();
    await expect(page.getByTestId('import-blocked')).toContainText('Convert to an electronic form');

    await page.getByRole('combobox', { name: 'Import mode' }).click();
    await page.getByRole('option', { name: 'Convert to an electronic form' }).click();
    await expect(page.getByTestId('import-blocked')).toHaveCount(0);
    await page.getByRole('button', { name: 'Upload and inspect' }).click();
    await expect(page).toHaveURL(/\/imports\/[0-9a-f-]{36}$/);

    // every worksheet together is more than one form may hold: a visible error, not a silent failure
    const failed = page.getByTestId('import-failed');
    await expect(failed).toContainText('could not be converted to one form');
    await expect(failed).toContainText('Choose fewer worksheets');
    await expect(page.getByRole('button', { name: 'Submit for approval' })).toHaveCount(0);

    // choose one worksheet on the same import and convert again
    await page.getByLabel('Worksheets to convert').click();
    await page.getByRole('option', { name: 'Section 2' }).click();
    await page.keyboard.press('Escape');
    await page.getByLabel('Form title').fill('Supplier questionnaire section 2');
    await page.getByRole('button', { name: 'Apply' }).click();
    await expect(page.getByText('Preview ready')).toBeVisible();
    await expect(page.getByTestId('import-failed')).toHaveCount(0);
    await page.getByRole('tab', { name: 'Check and approve' }).click();
    await expect(page.getByRole('button', { name: 'Submit for approval' })).toBeEnabled();
  });
});
