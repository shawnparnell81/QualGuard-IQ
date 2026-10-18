import { test, expect, type Page } from '@playwright/test';
import { Api, choose, signIn, state, users, shot, localDate } from './support';

/** Gap 28: generated drafts are off by default; an administrator enables a tested provider for one function; the draft is labelled, cited and reviewed; the request log records it. */
const button = (p: Page, name: string | RegExp) => p.getByRole('button', { name });
const settings = (mode: number) => ({ mode, timeoutSeconds: 20, storeContent: false, contentRetentionDays: 30, metadataRetentionDays: 365, redactionPatterns: [], redactEmails: true, redactPhoneNumbers: true, maxSourceRecords: 8 });

test.describe('E2E 24: generated content', () => {
  test('rule-based by default; a tested provider drafts a cited, labelled summary that the requester reviews', async ({ page }) => {
    const admin = await Api.as(users.admin);
    await admin.put('/api/generation/settings', settings(0));
    const t = Date.now().toString(36).toUpperCase();
    const ncr = await admin.post('/api/ncrs', { title: `Burr on bracket ${t}`, description: 'Edge burr', source: 'In-process', siteId: state().siteId, detectedOn: localDate(), severity: 1 });
    try {
      await signIn(page, users.admin);
      await page.goto(`/ncr/${ncr.id}`);
      await expect(page.getByText('Description').first()).toBeVisible();
      await expect(button(page, 'Generate draft')).toHaveCount(0); // off by default

      await page.goto('/platform/generation');
      await choose(page, /^Mode/, /Approved functions/);
      await button(page, 'Save settings').click();
      await expect(page.getByText('Settings saved.')).toBeVisible();

      await page.getByRole('tab', { name: 'Providers' }).click();
      await button(page, 'Add provider').click();
      await page.getByLabel('Name').fill(`Sandbox ${t}`);
      await choose(page, /^Kind/, /Test fixture/);
      await button(page, 'Save provider').click();
      const card = page.getByTestId(`gen-provider-Sandbox ${t}`);
      await expect(card.getByRole('button', { name: 'Enable' })).toBeDisabled();
      await card.getByRole('button', { name: 'Test connection' }).click();
      await expect(page.getByText(/test passed/)).toBeVisible();
      await card.getByRole('button', { name: 'Enable' }).click();
      await expect(card).toContainText('Enabled');

      await page.getByRole('tab', { name: 'Functions' }).click();
      const row = page.getByRole('row').filter({ hasText: 'Record summary' });
      await row.getByRole('combobox', { name: 'Provider' }).click();
      await page.getByRole('option', { name: `Sandbox ${t}` }).click();
      await row.getByRole('checkbox', { name: 'Enable Record summary' }).click();
      await expect(row.getByRole('checkbox', { name: 'Enable Record summary' })).toBeChecked();

      await page.goto(`/ncr/${ncr.id}`);
      await button(page, 'Generate draft').click();
      await expect(page.getByText(/Generated draft — not reviewed/)).toBeVisible();
      await expect(page.getByTestId('generated-text')).toContainText('[S1]');
      await expect(page.getByRole('link', { name: new RegExp(`\\[S1\\] .*Burr on bracket ${t}`) })).toBeVisible();
      await shot(page, '28-generated-draft-on-ncr');
      await button(page, 'Mark as reviewed and used').click();
      await expect(page.getByText(/nothing was changed automatically/)).toBeVisible();

      await page.goto('/platform/generation');
      await page.getByRole('tab', { name: 'Request log' }).click();
      const log = page.getByRole('table', { name: 'Request log' }).getByRole('row').filter({ hasText: 'record-summary' }).first();
      await expect(log).toContainText('Generated');
      await expect(log).toContainText('Accepted');
    } finally {
      await admin.put('/api/generation/settings', settings(0));
    }
    await shot(page, '28-generated-request-log');
  });
});
