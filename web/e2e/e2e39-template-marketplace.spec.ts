import { test, expect } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { shot, signIn, users } from './support';

/** Forms build B11: template marketplace with industry packs, library preview, install as draft form templates, package export and import. */
test.describe('Forms B11: template marketplace', () => {
  test('industry packs and library templates are previewed, installed once as drafts, exported and imported', async ({ page }) => {
    test.setTimeout(180_000);
    await signIn(page, users.author);
    await page.goto('/forms/library');
    await page.getByRole('link', { name: /Template marketplace/ }).click();
    await expect(page).toHaveURL(/\/forms\/marketplace/);
    await expect(page.getByRole('heading', { name: 'Template marketplace' })).toBeVisible();
    for (const name of ['Automotive', 'Aerospace', 'Medical device', 'Electronics', 'Manufacturing']) await expect(page.getByRole('heading', { name, exact: true })).toBeVisible();
    // every pack, electronics included, now has templates
    await expect(page.getByText('No templates are defined for this pack yet.')).toHaveCount(0);
    await shot(page, 'forms-import/b11-marketplace-packs');

    // browse the library by category and preview a template with its live calculations
    await page.getByRole('tab', { name: 'Template library' }).click();
    await page.getByRole('list', { name: 'Library categories' }).getByRole('button', { name: /Management review/ }).click();
    const table = page.getByRole('table', { name: 'Library templates' });
    await expect(table.getByText('KPI Review', { exact: true })).toBeVisible();
    await table.getByText('KPI Review', { exact: true }).click();
    const dlg = page.getByRole('dialog');
    await expect(dlg.getByText('Purpose', { exact: true })).toBeVisible();
    await expect(dlg.getByLabel('Form preview')).toBeVisible();
    await shot(page, 'forms-import/b11-template-preview');
    await dlg.getByText('Indicators below target').first().scrollIntoViewIfNeeded();
    await expect(dlg.getByText('Indicators below target').first()).toBeVisible();
    await dlg.getByRole('button', { name: 'Close' }).click();

    // install two library templates
    await page.getByLabel('Select Minutes').check();
    await page.getByLabel('Select Decisions').check();
    await page.getByRole('button', { name: 'Install selected (2)' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Install' }).click();
    await expect(page.getByText(/Installed 2 draft form templates/)).toBeVisible();
    await expect(page.getByLabel('Select Minutes')).toBeDisabled();
    await expect(table.getByRole('row', { name: /Minutes/ }).getByRole('link')).toHaveText(/\S+/);

    // install a whole industry pack; a second install is not offered once everything is installed
    await page.getByRole('tab', { name: 'Industry packs' }).click();
    const aero = page.locator('.MuiCard-root').filter({ has: page.getByRole('heading', { name: 'Aerospace', exact: true }) });
    await aero.getByRole('button', { name: 'Install pack' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Install' }).click();
    await expect(page.getByText(/Installed 31 draft form templates/)).toBeVisible({ timeout: 90_000 });
    await expect(aero.getByText('31 of 31 installed')).toBeVisible();
    await expect(aero.getByRole('button', { name: 'Install pack' })).toBeDisabled();
    await aero.getByRole('button', { name: 'View templates' }).click();
    await expect(page.getByRole('table', { name: 'Library templates' }).getByText('Configuration Change Record')).toBeVisible();
    await shot(page, 'forms-import/b11-pack-installed');

    // installed templates are drafts in the catalog, filed under the pack
    await page.goto('/forms/library?view=industry&q=Configuration%20Change%20Record');
    await expect(page.getByRole('table', { name: 'Templates' }).getByText('Configuration Change Record')).toBeVisible();
    await expect(page.getByRole('table', { name: 'Templates' }).getByText('Draft').first()).toBeVisible();

    // export two templates as a package file, then import it back as a package
    await page.goto('/forms/marketplace');
    await page.getByRole('button', { name: 'Export package' }).click();
    const ex = page.getByRole('dialog');
    const key = `e2e-reviews-${Date.now().toString(36)}`;
    await ex.getByLabel('Package key').fill(key);
    await ex.getByLabel('Name', { exact: true }).fill('Review records');
    await ex.getByLabel('Find templates').fill('Minutes');
    await ex.getByRole('button', { name: 'Search' }).click();
    await ex.getByRole('table', { name: 'Templates to export' }).getByRole('checkbox').first().check();
    const [download] = await Promise.all([page.waitForEvent('download'), ex.getByRole('button', { name: 'Export' }).click()]);
    expect(download.suggestedFilename()).toBe(`${key}-1.0.qgpkg`);
    await expect(page.getByText(`Exported 1 template as ${key} 1.0.`)).toBeVisible();
    const file = readFileSync((await download.path())!);
    await page.getByLabel('Package file').setInputFiles({ name: `${key}-1.0.qgpkg`, mimeType: 'application/zip', buffer: file });
    await expect(page.getByText(/Imported package Review records 1.0 with 1 template\. Every definition matched its recorded hash/)).toBeVisible();
    const imported = page.locator('.MuiCard-root').filter({ hasText: key }).filter({ hasText: 'Imported' });
    await expect(imported.getByText('0 of 1 installed')).toBeVisible();
    await imported.getByRole('button', { name: 'Install pack' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Install' }).click();
    await expect(imported.getByText('1 of 1 installed')).toBeVisible();
    await shot(page, 'forms-import/b11-package-imported');
  });
});
