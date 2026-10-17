import { test, expect, type Page } from '@playwright/test';
import { Api, choose, dialog, signIn, state, unique, users } from './support';

const button = (p: Page, name: string | RegExp) => p.getByRole('button', { name });
async function shot(page: Page, name: string) {
  if (process.env.SHOTS_DIR) await page.screenshot({ path: `${process.env.SHOTS_DIR}/${name}.png`, fullPage: true });
}

test.describe('E2E 12: platform services (Prompt 8)', () => {
  test('my workspace, activity stream and calendar show my work', async ({ page }) => {
    const api = await Api.as(users.author);
    const title = unique('Platform NCR');
    const created = await api.post('/api/ncrs', { title, description: 'Burr', source: 'In-process', siteId: state().siteId, detectedOn: new Date().toISOString().slice(0, 10), severity: 1 });
    const ncr = await api.get(`/api/ncrs/${created.id}`);
    await (await Api.as(users.admin)).post('/api/platform/events/pump', {});
    await signIn(page, users.author);
    await page.goto('/platform');
    await expect(page.getByRole('heading', { name: 'My workspace' })).toBeVisible();
    await expect(page.getByText('Open tasks')).toBeVisible();
    await shot(page, '143-my-workspace');
    await page.goto('/platform/activity');
    await expect(page.getByText(`Created NCR ${ncr.number}`).first()).toBeVisible();
    await shot(page, '144-activity-stream');
    await page.goto('/platform/calendar');
    await expect(page.getByRole('heading', { name: 'Quality calendar' })).toBeVisible();
    await shot(page, '145-calendar');
  });

  test('a saved pivot report and a dashboard are designed', async ({ page }) => {
    await signIn(page, users.author);
    await page.goto('/platform/reports');
    await button(page, 'New report').click();
    const name = unique('NCR pivot');
    await dialog(page).getByLabel('Name').fill(name);
    await choose(page, 'Source report', /Open NCR aging/);
    await dialog(page).getByLabel('Pivot rows').fill('status');
    await dialog(page).getByLabel('Pivot columns').fill('source');
    await button(page, 'Save').click();
    await expect(page.getByText(`${name} —`)).toBeVisible();
    await expect(page.getByText('Total').first()).toBeVisible();
    await shot(page, '146-report-designer');
    await page.goto('/platform/dashboards');
    await button(page, 'New dashboard').click();
    await dialog(page).getByLabel('Name').fill(unique('Quality board'));
    await button(page, 'Create').click();
    await expect(page.getByText('Open Ncr')).toBeVisible();
    await shot(page, '147-dashboard');
  });

  test('admin manages webhooks, health, security ops and modules; others cannot', async ({ page }) => {
    await signIn(page, users.admin);
    await page.goto('/platform/webhooks');
    await button(page, 'Add webhook').click();
    await dialog(page).getByLabel('Name').fill(unique('ERP hook'));
    await dialog(page).getByLabel('HTTPS URL').fill('https://erp.invalid/hooks/qualguard');
    await choose(page, 'Event', 'record.created');
    await button(page, 'Create').click();
    await expect(page.getByText('Signing secret (shown once)')).toBeVisible();
    await shot(page, '148-webhooks');
    await page.goto('/platform/health');
    await expect(page.getByText('Database latency')).toBeVisible();
    await shot(page, '149-system-health');
    await page.goto('/platform/security');
    await expect(page.getByText('Privileged access')).toBeVisible();
    await shot(page, '150-security-ops');
    await page.goto('/platform/modules');
    await expect(page.getByText('Depends on').first()).toBeVisible();
    await shot(page, '151-module-registry');
    const viewer = await Api.as(users.viewer);
    expect(await viewer.status('GET', '/api/platform/health')).toBe(403);
  });
});
