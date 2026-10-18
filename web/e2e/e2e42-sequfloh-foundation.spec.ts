import { test, expect } from '@playwright/test';
import { shot, signIn, unique, users } from './support';

/** SEQUFLOH S1: module entry, workspace, settings, numbering, reference data, module access (Prompt 1 sections 4, 6-10, 14). */
test.describe('SEQUFLOH foundation', () => {
  test('module opens inside the QualGuard IQ shell with its navigation and context', async ({ page }) => {
    await signIn(page, users.author);
    await page.goto('/sequfloh');
    await expect(page.getByRole('heading', { name: 'SEQUFLOH', exact: true })).toBeVisible();
    await expect(page.getByText('SEQUFLOH · Enterprise Execution Engine')).toBeVisible();
    for (const group of ['Live Execution', 'Tracking', 'Work Instructions', 'Parts and Items', 'Quality Personnel', 'Engineering Personnel', 'Reports', 'Audit Trail', 'SEQUFLOH Administration', 'SEQUFLOH Settings'])
      await expect(page.getByRole('button', { name: group, exact: true })).toBeVisible();
    await shot(page, 'sequfloh/s1-home');
    await page.getByRole('button', { name: 'Pin page' }).click();
    await expect(page.getByRole('button', { name: 'Unpin page' })).toBeVisible();
    await page.reload();
    await expect(page.getByRole('button', { name: 'Unpin page' })).toBeVisible();
    await expect(page.getByText('Pinned', { exact: true })).toBeVisible();
  });

  test('settings, numbering and dropdowns are administrable and audited', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page, users.author);
    await page.goto('/sequfloh/settings/tracking');
    await expect(page.getByRole('heading', { name: 'Tracking Settings' })).toBeVisible();
    await page.getByLabel('Default maximum time (minutes)').fill('5');
    await page.getByRole('button', { name: 'Save settings' }).click();
    await expect(page.getByText('The default maximum time cannot be less than the default minimum time.')).toBeVisible();
    await page.getByLabel('Default maximum time (minutes)').fill('90');
    await page.getByRole('button', { name: 'Save settings' }).click();
    await expect(page.getByText('Settings saved.')).toBeVisible();
    await shot(page, 'sequfloh/s1-tracking-settings');

    await page.goto('/sequfloh/settings/numbering');
    await expect(page.getByRole('cell', { name: 'Parts lists' })).toBeVisible();
    await shot(page, 'sequfloh/s1-numbering');

    await page.goto('/sequfloh/admin/dropdowns?list=hold-reasons');
    await expect(page.getByRole('cell', { name: 'Awaiting parts' })).toBeVisible();
    await page.getByRole('button', { name: 'New value' }).click();
    const label = unique('Awaiting calibration');
    await page.getByRole('dialog').getByLabel('Label').fill(label);
    await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click();
    const row = page.getByRole('row').filter({ hasText: label });
    await expect(row).toBeVisible();
    await row.getByRole('button', { name: 'Deactivate' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Confirm' }).click();
    await expect(row.getByText('Inactive')).toBeVisible();
    await row.getByRole('button', { name: 'History' }).click();
    await expect(page.getByRole('dialog').getByRole('cell', { name: 'Created', exact: true })).toBeVisible();
    await page.getByRole('dialog').getByRole('button', { name: 'Close' }).click();
    await shot(page, 'sequfloh/s1-dropdowns');

    await page.goto('/sequfloh/admin/module-access');
    await page.getByRole('combobox', { name: 'Role' }).click();
    await page.getByRole('option', { name: /Read Only/ }).click();
    await expect(page.getByText('sequfloh.view_dashboard')).toBeVisible();
    await shot(page, 'sequfloh/s1-module-access');
    await page.goto('/sequfloh/admin/roles');
    await expect(page.getByRole('columnheader', { name: 'Quality Engineer' })).toBeVisible();
  });

  test('a user without SEQUFLOH access sees no module and is refused on direct routes', async ({ page }) => {
    await signIn(page, users.outsider);
    await page.goto('/sequfloh/settings/general');
    await expect(page.getByText('You do not have permission to open this SEQUFLOH page.')).toBeVisible();
  });
});
