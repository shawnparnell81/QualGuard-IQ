import { test, expect } from '@playwright/test';
import { Api, shot, signIn, state, unique, users } from './support';

/** Forms build B4: Enterprise Template Library catalog (views, search, favorites, columns, saved views, classification, clone, compare). */
test.describe('Forms B4: template library', () => {
  test('an author finds a template by view and search, favorites it, classifies it, saves a view and clones it', async ({ page }) => {
    const author = await Api.as(users.author);
    const title = unique('Library torque check');
    const id = await author.post<string>('/api/documents', { kind: 2, title, documentType: 'Inspection Record', siteId: state().siteId, departmentId: null, reviewIntervalMonths: 24, initialRevision: 'A', changeSummary: 'Initial release' });

    await signIn(page, users.author);
    await page.goto('/forms/library');
    await page.getByRole('button', { name: 'Draft templates' }).click();
    await page.getByLabel('Search templates').fill(title);
    await page.getByRole('button', { name: 'Search', exact: true }).click();
    const table = page.getByRole('table', { name: 'Templates' });
    await expect(table.getByText(title, { exact: true })).toBeVisible();
    const row = table.getByRole('row').filter({ has: page.getByText(title, { exact: true }) });
    await row.getByRole('button', { name: /^Favorite / }).click();
    await expect(row.getByRole('button', { name: /^Unfavorite / })).toBeVisible();
    await page.getByRole('checkbox', { name: 'Favorites', exact: true }).click();
    await expect(page).toHaveURL(/favorites=true/);
    await expect(table.getByText(title, { exact: true })).toBeVisible();

    await page.getByRole('button', { name: 'Save view' }).click();
    await page.getByLabel('View name').fill('My favorite drafts');
    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(page.getByText('My favorite drafts')).toBeVisible();
    await shot(page, 'forms-import/b4-library');

    await table.getByText(title, { exact: true }).click();
    const dlg = page.getByRole('dialog');
    await dlg.getByRole('button', { name: 'Classification' }).click();
    await dlg.getByLabel('Category').click();
    await page.getByRole('option', { name: 'Controlled Forms' }).click();
    await dlg.getByLabel('Confidentiality').click();
    await page.getByRole('option', { name: 'Confidential' }).click();
    await dlg.getByRole('button', { name: 'Save classification' }).click();
    await expect(page.locator('table[aria-label="Templates"]').getByText('Controlled Forms')).toBeVisible();
    await dlg.getByRole('button', { name: 'Actions' }).click();
    await dlg.getByLabel('Clone as').fill(`${title} copy`);
    await dlg.getByRole('button', { name: 'Clone' }).click();
    await expect(page).toHaveURL(/\/forms\/templates\/[0-9a-f-]{36}$/);
    await expect(page.getByText(`${title} copy`).first()).toBeVisible();
    expect(id).toBeTruthy();
  });
});
