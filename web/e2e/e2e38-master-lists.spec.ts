import { test, expect } from '@playwright/test';
import { localDate, shot, signIn, users } from './support';

/** Forms build B10: governed master lists read from the module records, and master list migration (Excel mode D) through the Import Center. */
test.describe('Forms B10: master lists', () => {
  test('a calibration master list is migrated into equipment, approved by someone else, and shown with its columns, views and export', async ({ page, browser }) => {
    const code = `CAL${Date.now().toString(36).toUpperCase()}`;
    const due = localDate(120);
    await signIn(page, users.author);
    await page.goto('/master-lists/calibration-equipment');
    await expect(page.getByRole('heading', { name: 'Calibration master list' })).toBeVisible();
    await page.getByRole('link', { name: 'Bulk import or update' }).click();
    await expect(page).toHaveURL(/\/imports\?target=master-equipment/);
    await expect(page.getByLabel('Master list', { exact: true })).toBeVisible();
    const csv = 'Equipment ID,Asset ID,Equipment name,Type,Manufacturer,Serial number,Range,Resolution,Accuracy,Acceptance criteria,Frequency (days),Last calibration,Next due date,Provider,Status,Limited use,Limited-use restriction\n'
      + `${code}-1,AS-501,Outside micrometer,Micrometer,Mitutoyo,MM-501,0-25 mm,0.001 mm,±0.002 mm,Within ±0.002 mm at 5 points,365,${localDate(-245)},${due},Internal lab,Active,No,\n`
      + `${code}-2,AS-502,Pressure gauge,Gauge,Wika,PG-502,0-10 bar,0.05 bar,±0.5% FS,Within ±0.5% FS at 3 points,180,${localDate(-60)},${due},Acme Calibration,Active,Yes,Only 0-6 bar\n`;
    await page.getByTestId('import-file').setInputFiles({ name: 'calibration master list.csv', mimeType: 'text/csv', buffer: Buffer.from(csv) });
    await page.getByRole('button', { name: 'Upload and inspect' }).click();
    await expect(page.getByText(/Mapping required|Preview ready/).first()).toBeVisible();
    await page.getByRole('button', { name: 'Validate (dry run)' }).click();
    await expect(page.getByText(/2 of 2 rows ready/).first()).toBeVisible();
    await page.getByRole('tab', { name: 'Workbench' }).click();
    await expect(page.getByLabel('Step 2 Import purpose').getByRole('strong')).toHaveText('Master list');
    await shot(page, 'forms-import/b10-master-list-workbench');
    await page.getByRole('tab', { name: 'Preview and validation' }).click();
    await page.getByRole('button', { name: 'Submit for approval' }).click();
    await expect(page.getByText('Waiting for approval by someone other than the uploader.')).toBeVisible();
    const url = page.url();

    const ctx = await browser.newContext();
    const p2 = await ctx.newPage();
    await signIn(p2, users.approver);
    await p2.goto(url);
    await p2.getByRole('tab', { name: 'Preview and validation' }).click();
    await p2.getByRole('button', { name: 'Approve and import' }).click();
    await expect(p2.getByText(/2 records created/)).toBeVisible();
    await ctx.close();

    await page.goto(`/master-lists/calibration-equipment?q=${code}`);
    const table = page.getByRole('table', { name: 'Calibration master list' });
    await expect(table.locator('tbody tr')).toHaveCount(2);
    await expect(table.getByRole('cell', { name: '±0.5% FS', exact: true })).toBeVisible();
    await expect(table.getByText('Yes: Only 0-6 bar')).toBeVisible();
    await expect(table.getByText('Acme Calibration')).toBeVisible();
    await expect(page.getByLabel('Totals by status').getByText('Active: 2')).toBeVisible();
    await shot(page, 'forms-import/b10-calibration-master-list');

    await page.getByRole('button', { name: 'Columns' }).click();
    await page.getByRole('dialog').getByLabel('Manufacturer').uncheck();
    await page.getByRole('button', { name: 'Done' }).click();
    await expect(table.getByRole('columnheader', { name: 'Manufacturer' })).toHaveCount(0);
    await page.getByRole('button', { name: 'Save view' }).click();
    await page.getByLabel('View name').fill(`Migrated ${code}`);
    await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click();
    await expect(page.getByLabel(`Delete view Migrated ${code}`)).toBeVisible();

    const xlsx = await page.request.get(`/api/master-lists/calibration-equipment/export?q=${code}`);
    expect(xlsx.ok()).toBeTruthy();
    expect(xlsx.headers()['content-type']).toContain('spreadsheetml');
    const csvOut = await (await page.request.get(`/api/master-lists/calibration-equipment/export?format=csv&q=${code}`)).text();
    expect(csvOut).toContain('Acceptance criteria');
    expect(csvOut).toContain(`${code}-2`);

    await page.getByRole('button', { name: 'History' }).click();
    await expect(page.getByRole('table', { name: 'List history' }).locator('tbody tr').first()).toBeVisible();
    await page.getByRole('button', { name: 'Close' }).click();
    await page.getByRole('button', { name: /Master document list/ }).click();
    await expect(page.getByRole('heading', { name: 'Master document list' })).toBeVisible();
    await expect(page.getByRole('table', { name: 'Master document list' }).getByRole('columnheader', { name: 'Related process' })).toBeVisible();
    await shot(page, 'forms-import/b10-master-document-list');
  });
});
