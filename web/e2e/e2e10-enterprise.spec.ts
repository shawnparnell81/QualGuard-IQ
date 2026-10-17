import { test, expect, type Page } from '@playwright/test';
import { Api, choose, dialog, signIn, state, unique, users } from './support';

const button = (p: Page, name: string | RegExp) => p.getByRole('button', { name });
/** Saves a numbered screenshot when SHOTS_DIR is set (overnight build evidence). */
async function shot(page: Page, name: string) {
  if (process.env.SHOTS_DIR) await page.screenshot({ path: `${process.env.SHOTS_DIR}/${name}.png`, fullPage: true });
}

test.describe('E2E 10: enterprise governance (Prompt 6)', () => {
  test('plant scorecards open the site war room', async ({ page }) => {
    await signIn(page, users.author);
    await page.goto('/enterprise');
    await expect(page.getByRole('heading', { name: 'Enterprise quality overview' })).toBeVisible();
    await expect(page.getByTestId('site-card').first()).toBeVisible();
    await shot(page, '117-enterprise-scorecards');
    await page.getByTestId('site-card').filter({ hasText: 'E2E' }).first().click();
    await expect(page.getByRole('heading', { name: /War room — Test Plant/ })).toBeVisible();
    await shot(page, '118-war-room');
  });

  test('a skill is defined and the qualification matrix shows the gap', async ({ page }) => {
    await signIn(page, users.author);
    await page.goto('/training/skills');
    await button(page, 'New skill').click();
    const code = `CMM${Date.now().toString(36).slice(-4).toUpperCase()}`;
    await dialog(page).getByLabel('Code').fill(code);
    await dialog(page).getByLabel('Name').fill('CMM programmer');
    await choose(page, 'Requires certification', 'Yes');
    await choose(page, 'Required for everyone at this site', 'Yes');
    await button(page, 'Create').click();
    await expect(page.getByText(code, { exact: true }).first()).toBeVisible();
    await expect(page.getByTestId('matrix-cell').first()).toBeVisible();
    await shot(page, '119-skills-matrix');
    await page.getByRole('tab', { name: /Gaps/ }).click();
    await expect(page.getByRole('cell', { name: 'CMM programmer' }).first()).toBeVisible();
  });

  test('a launch project shows APQP phases and deliverables can be completed', async ({ page, browser }) => {
    const api = await Api.as(users.author);
    const { id } = await api.post<{ id: string }>('/api/launch/projects', { title: unique('Pump launch'), kind: 0, siteId: state().siteId });
    await signIn(page, users.author);
    await page.goto(`/launch/${id}`);
    await expect(page.getByTestId('phase')).toHaveCount(6);
    await page.getByRole('row', { name: /Design and quality goals/ }).getByRole('button', { name: 'Done' }).click();
    await expect(page.getByRole('row', { name: /Design and quality goals/ }).getByRole('button', { name: 'Reopen' })).toBeVisible();
    await shot(page, '120-launch-project');
    const ap = await (await browser.newContext()).newPage(); // gate decisions need apqp.approve
    await signIn(ap, users.approver);
    await ap.goto(`/launch/${id}`);
    await button(ap, /gate review/).click();
    await dialog(ap).getByLabel('Password').fill(users.approver.password);
    await button(ap, 'Sign decision').click();
    await expect(dialog(ap).getByText(/open deliverables/)).toBeVisible(); // Go refused while phase 1 is open
  });

  test('a validation is planned with protocol tests and submitted for approval', async ({ page }) => {
    await signIn(page, users.author);
    await page.goto('/validation/new');
    await page.getByLabel('Title').fill(unique('Cure oven IQ/OQ'));
    await choose(page, 'Site', /E2E/);
    await page.getByLabel('Scope').fill('Oven 2');
    await button(page, 'Create').click();
    await button(page, 'Add test').click();
    await page.getByPlaceholder('Test').fill('Utilities connected');
    await page.getByPlaceholder('Acceptance criteria').fill('Per drawing');
    await button(page, 'Save tests').click();
    await expect(page.getByRole('cell', { name: 'Utilities connected' })).toBeVisible();
    await button(page, 'Submit protocol for approval').click();
    await expect(button(page, 'Submit protocol for approval')).toHaveCount(0);
    await shot(page, '121-validation');
  });

  test('a compliance pack is enabled and obligations appear', async ({ page }) => {
    await signIn(page, users.author);
    await page.goto('/compliance');
    const pack = page.getByTestId('pack').filter({ hasText: 'ISO 9001:2015' }).first();
    await expect(pack.getByRole('button')).toBeVisible();
    if (await pack.getByRole('button', { name: 'Enable for site' }).count()) await pack.getByRole('button', { name: 'Enable for site' }).click();
    await expect(pack.getByRole('button', { name: 'Disable' })).toBeVisible();
    await shot(page, '122-compliance-packs');
    await page.getByRole('tab', { name: /Obligations/ }).click();
    await expect(page.getByRole('cell', { name: '4.1' }).first()).toBeVisible();
    await shot(page, '123-compliance-obligations');
  });

  test('the PPAP element wall and Part 11 readiness render', async ({ page, browser }) => {
    const api = await Api.as(users.author);
    const t = Date.now().toString(36).toUpperCase();
    const { id: partId } = await api.post<{ id: string }>('/api/master/parts', { partNumber: `PP-${t}`, name: 'Bracket', partType: 0, revision: 'A', unitOfMeasure: 'EA', siteId: state().siteId, status: 0 });
    const { id } = await api.post<{ id: string }>('/api/launch/ppap', { partId, level: 3, reason: 'Initial submission', siteId: state().siteId });
    await signIn(page, users.author);
    await page.goto(`/launch/ppap/${id}`);
    await expect(page.getByTestId('ppap-element')).toHaveCount(18);
    await expect(page.getByTestId('ppap-code')).toHaveCount(18);
    await expect(page.getByTestId('ppap-legend')).toContainText('Retain and submit to the customer on request');
    await page.getByRole('button', { name: 'Show level matrix' }).click();
    await expect(page.getByTestId('ppap-matrix').getByRole('row')).toHaveCount(21);
    await shot(page, '124-ppap-elements');
    await page.goto('/launch/capability');
    await button(page, 'Calculate').click();
    await expect(page.getByText('Cpk', { exact: true })).toBeVisible();
    await shot(page, '125-capability');
    const adm = await (await browser.newContext()).newPage();
    await signIn(adm, users.admin);
    await adm.goto('/governance');
    await expect(adm.getByTestId('p11').first()).toBeVisible();
    await shot(adm, '126-part11-readiness');
  });
});
