import { test, expect, type Page } from '@playwright/test';
import { Api, choose, dialog, signIn, users, shot } from './support';

/** Gaps 19 and 22: report schedules with time zone and history, pause/resume, board report editions approved by a second person, and the attachment policy kept by administrators. */
const button = (p: Page, name: string | RegExp) => p.getByRole('button', { name });

test.describe('E2E 22: report schedules', () => {
  test('an engineer schedules a shared report, runs it, pauses and resumes; board editions need a second person; the delivery policy is admin-only', async ({ page, browser }) => {
    const api = await Api.as(users.author);
    const t = Date.now().toString(36).toUpperCase();
    await api.post('/api/designer/reports', { name: `Aging ${t}`, reportKey: 'ncr.aging', kind: 0, columns: ['number', 'status'], siteId: null, rangeDays: 90, filterColumn: null, filterValue: null, rowField: null, columnField: null, valueField: null, shared: true, confidential: false });

    await signIn(page, users.author);
    await page.goto('/platform/report-schedules');
    await button(page, 'New schedule').click();
    const d = dialog(page);
    await d.getByLabel('Name').fill(`Weekly aging ${t}`);
    await choose(page, /^Saved report$/, new RegExp(`Aging ${t}`));
    await choose(page, /^Time zone/, 'America/Chicago');
    await page.getByRole('combobox', { name: 'People' }).click();
    await page.getByRole('option', { name: users.approver.name }).click();
    await page.keyboard.press('Escape');
    await page.getByRole('combobox', { name: /^Delivery/ }).click();
    await expect(page.getByRole('option', { name: 'PDF attachment' })).toHaveAttribute('aria-disabled', 'true'); // attachments are off by default
    await page.getByRole('option', { name: 'Secure link' }).click();
    await d.getByRole('button', { name: 'Create schedule' }).click();
    const card = page.getByTestId('schedule-card').filter({ hasText: `Weekly aging ${t}` });
    await expect(card).toContainText('(America/Chicago)');
    await expect(card).toContainText('Next run');

    await card.getByRole('button', { name: 'Run now' }).click();
    await expect(page.getByText(/Run finished: Delivered/)).toBeVisible();
    const history = card.getByRole('table', { name: 'Distribution history' });
    await expect(history).toContainText('Manual');
    await history.getByRole('row').nth(1).click();
    await expect(card).toContainText(`${users.approver.name}: delivered`);
    await shot(page, '19-report-schedule-history');

    await card.getByRole('button', { name: 'Pause' }).click();
    await dialog(page).getByLabel('Reason (optional)').fill('Plant shutdown week');
    await dialog(page).getByRole('button', { name: 'Pause' }).click();
    await expect(card).toContainText('Paused');
    await expect(card).toContainText('Plant shutdown week');
    await card.getByRole('button', { name: 'Resume' }).click();
    await expect(card).toContainText('Next run');

    // board editions: the author generates, cannot approve; the approver approves
    await page.getByRole('tab', { name: 'Board editions' }).click();
    await button(page, 'Generate Board quality report').click();
    const row = page.getByRole('table', { name: 'Board report editions' }).getByRole('row').filter({ hasText: 'Draft' }).first();
    await expect(row).toBeVisible();
    await expect(row.getByRole('button', { name: 'Approve' })).toHaveCount(0);

    // the delivery policy is read-only for non-administrators
    await page.getByRole('tab', { name: 'Delivery policy' }).click();
    await expect(page.getByText('Only administrators with platform.admin can change the policy.')).toBeVisible();
    await expect(page.getByRole('checkbox', { name: 'Allow scheduled reports to be attached' })).toBeDisabled();

    const admin = await (await browser.newContext()).newPage();
    await signIn(admin, users.admin);
    await admin.goto('/platform/report-schedules');
    await admin.getByRole('tab', { name: 'Board editions' }).click();
    const arow = admin.getByRole('table', { name: 'Board report editions' }).getByRole('row').filter({ hasText: 'Draft' }).first();
    await arow.getByRole('button', { name: 'Approve' }).click();
    await dialog(admin).getByLabel('Notes (required)').fill('Figures checked against the monthly pack');
    await dialog(admin).getByRole('button', { name: 'Approve' }).click();
    await expect(admin.getByRole('table', { name: 'Board report editions' })).toContainText('Figures checked against the monthly pack');

    await admin.getByRole('tab', { name: 'Delivery policy' }).click();
    await admin.getByRole('checkbox', { name: 'Allow scheduled reports to be attached' }).check();
    await admin.getByRole('button', { name: 'Save policy' }).click();
    await expect(admin.getByText('Policy saved.')).toBeVisible();
    // restore the default so other tests start from link-only delivery
    await admin.getByRole('checkbox', { name: 'Allow scheduled reports to be attached' }).uncheck();
    await admin.getByRole('button', { name: 'Save policy' }).click();
    await expect(admin.getByText('Policy saved.')).toBeVisible();
    await shot(page, '22-report-delivery-policy');
  });
});
