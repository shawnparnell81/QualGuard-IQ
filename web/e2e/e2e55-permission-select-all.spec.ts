import { test, expect } from '@playwright/test';
import { shot, signIn, users } from './support';

test('each permission card has Select all that ticks, unticks and shows a part-ticked state', async ({ page }) => {
  await signIn(page, users.admin);
  await page.goto('/admin/roles');
  await page.getByRole('button', { name: 'New role' }).click();
  const dlg = page.getByRole('dialog');
  const all = dlg.locator('[data-testid^="select-all-"]').first();
  await expect(all).toBeVisible();
  const card = all.locator('xpath=ancestor::*[contains(@class,"MuiPaper-root")][1]');
  const boxes = card.locator('input[type="checkbox"]');
  const n = await boxes.count();
  expect(n).toBeGreaterThan(1);
  const head = all.locator('input');
  await head.check();
  for (let i = 1; i < n; i++) await expect(boxes.nth(i)).toBeChecked();
  await boxes.nth(1).uncheck();
  await expect(all).toHaveClass(/MuiCheckbox-indeterminate/);
  await shot(page, 'tabfix-shots/04-permission-select-all');
  await head.check();
  for (let i = 1; i < n; i++) await expect(boxes.nth(i)).toBeChecked();
  await head.uncheck();
  for (let i = 1; i < n; i++) await expect(boxes.nth(i)).not.toBeChecked();
  // other cards are untouched
  await expect(dlg.locator('input[type="checkbox"]:checked')).toHaveCount(0);
});
