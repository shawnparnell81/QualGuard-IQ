import { test, expect, type Page } from '@playwright/test';
import { choose, dialog, signIn, users, shot } from './support';

/** Gap 27: an administrator adds a sandbox SMS provider, which stays off until tested; a test message shows in the delivery log; a user opts in and out themselves. */
const button = (p: Page, name: string | RegExp) => p.getByRole('button', { name });

test.describe('E2E 23: SMS and push', () => {
  test('provider is enabled only after a passing test, test messages are logged with masked numbers, users manage their own opt-in', async ({ page, browser }) => {
    const t = Date.now().toString(36).toUpperCase();
    await signIn(page, users.admin);
    await page.goto('/platform/messaging');
    await button(page, 'Add provider').click();
    const d = dialog(page);
    await d.getByLabel('Name').fill(`Sandbox SMS ${t}`);
    await choose(page, /^Adapter/, /Test fixture/);
    await d.getByRole('button', { name: 'Save' }).click();
    const card = page.getByTestId(`provider-Sandbox SMS ${t}`);
    await expect(card).toContainText('Disabled');
    await expect(card).toContainText('Run a passing connection test');
    await expect(card.getByRole('button', { name: 'Enable' })).toBeDisabled();

    await card.getByRole('button', { name: 'Test connection' }).click();
    await expect(page.getByText(/test passed/)).toBeVisible();
    await card.getByRole('button', { name: 'Enable' }).click();
    await expect(card).toContainText('Enabled');

    await card.getByLabel(/Send test to/).fill('+15551230042');
    await card.getByRole('button', { name: 'Send test' }).click();
    await expect(page.getByText(/Test message sent/)).toBeVisible();
    await page.getByRole('tab', { name: 'Delivery log' }).click();
    const log = page.getByRole('table', { name: 'Delivery log' });
    await expect(log.getByRole('row').filter({ hasText: `Sandbox SMS ${t}` }).first()).toContainText('*******0042');

    // the user opts in under the account menu; a number without a country code is refused
    const ctx = await browser.newContext(); const up = await ctx.newPage();
    await signIn(up, users.viewer);
    await up.goto('/account/messaging');
    await up.getByLabel(/Mobile number/).fill('5551230077');
    await up.getByLabel(/I agree/).check();
    await button(up, 'Opt in').click();
    await expect(up.getByText(/international format/)).toBeVisible();
    await up.getByLabel(/Mobile number/).fill(`+1555${Date.now().toString().slice(-7)}`);
    await up.getByLabel(/I agree/).check();
    await button(up, 'Opt in').click();
    const mine = up.getByRole('table', { name: 'My subscriptions' }).getByRole('row').filter({ hasText: 'Opted in' }).last();
    await expect(mine).toBeVisible();
    await mine.getByRole('button', { name: 'Opt out' }).click();
    await expect(up.getByRole('table', { name: 'My subscriptions' })).toContainText('Opted out');
    // and cannot reach the provider administration
    await up.goto('/platform/messaging');
    await expect(up.getByText(/needs api.admin/)).toBeVisible();
    await ctx.close();
    await shot(page, '27-sms-push-providers');
  });
});
