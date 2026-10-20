import { test, expect, type Page } from '@playwright/test';
import { Api, shot, signIn, state, unique, users } from './support';

const capture = (p: Page, name: string) => shot(p, `dropdowns-shots/${name}`);
const openSelect = (p: Page, label: string) => p.getByLabel(label, { exact: true }).click();
/** Makes a setup list look empty for this page only, so the empty note can be checked on any database. */
/** The shortcut row is shown above the first option. */
async function expectOnTop(p: Page, row: string, option: RegExp) {
  const r = await p.getByTestId(row).boundingBox();
  const o = await p.getByRole('option', { name: option }).first().boundingBox();
  expect(r && o && r.y < o.y).toBeTruthy();
}
const emptyList = (p: Page, url: string) => p.route(`**${url}?*`, (r) => r.fulfill({ json: { items: [], total: 0 } }));

test.describe('E2E 53: pickers link to the page where their items are added', () => {
  test('an empty list shows a note and an add link that opens in a new tab', async ({ page }) => {
    await signIn(page, users.author);
    await emptyList(page, '/api/master/parts');
    await page.goto('/production/work-orders/new');
    await openSelect(page, 'Part');
    const row = page.getByTestId('setup-row-part');
    await expect(row).toContainText('No parts yet');
    const add = page.getByTestId('setup-add-part');
    await expect(add).toHaveText(/Add part/);
    await expect(add).toHaveAttribute('href', '/products/parts/new');
    await expect(add).toHaveAttribute('target', '_blank');
    await capture(page, '01-empty-dropdown-work-order');
    const [tab] = await Promise.all([page.context().waitForEvent('page'), add.click()]);
    await tab.waitForLoadState();
    expect(new URL(tab.url()).pathname).toBe('/products/parts/new');
    await tab.close();
    // the form is still open behind the link
    await expect(page.getByLabel('Number (blank = next)')).toBeVisible();
  });

  test('a filled list keeps the add link under its options', async ({ page }) => {
    const author = await Api.as(users.author);
    const pn = unique('DD');
    await author.post('/api/master/parts', { partNumber: pn, name: 'Bracket', partType: 0, revision: 'A', unitOfMeasure: 'EA', siteId: state().siteId, status: 0 });
    await signIn(page, users.author);
    await page.goto('/production/work-orders/new');
    await openSelect(page, 'Part');
    await expect(page.getByRole('option', { name: new RegExp(pn) })).toBeVisible();
    await expect(page.getByTestId('setup-row-part')).not.toContainText('No parts yet');
    await expect(page.getByTestId('setup-add-part')).toBeVisible();
    await expectOnTop(page, 'setup-row-part', new RegExp(pn));
    await capture(page, '02-filled-dropdown-work-order');
    await page.getByRole('option', { name: new RegExp(pn) }).click();
    const part = page.getByRole('combobox', { name: 'Part' });
    await expect(part).toContainText(pn);
    // clicking the shortcut row itself is not a choice: the picked part stays
    await part.click();
    await page.getByTestId('setup-row-part').click({ position: { x: 4, y: 4 } });
    await expect(page.getByRole('listbox')).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(part).toContainText(pn);
    await author.dispose();
  });

  test('the shared form fields on a new NCR carry the same shortcut', async ({ page }) => {
    await signIn(page, users.author);
    await emptyList(page, '/api/master/customers');
    await page.goto('/ncr/new');
    await openSelect(page, 'Customer');
    await expect(page.getByTestId('setup-row-customer')).toContainText('No customers yet');
    await expect(page.getByTestId('setup-add-customer')).toHaveAttribute('href', '/products/customers/new');
    await capture(page, '03-empty-dropdown-ncr-customer');
    await page.keyboard.press('Escape');
    await openSelect(page, 'Site');
    // the author has no right to add sites, and the list has items, so no link is offered
    await expect(page.getByRole('option', { name: /Test Plant/ })).toBeVisible();
    await expect(page.getByTestId('setup-add-site')).toHaveCount(0);
    await page.keyboard.press('Escape');
  });

  test('without the add permission the note asks for an administrator', async ({ page }) => {
    await signIn(page, users.viewer);
    await emptyList(page, '/api/master/parts');
    await page.goto('/production/work-orders/new');
    await openSelect(page, 'Part');
    const row = page.getByTestId('setup-row-part');
    await expect(row).toContainText('No parts yet');
    await expect(row).toContainText('Ask an administrator to add parts.');
    await expect(page.getByTestId('setup-add-part')).toHaveCount(0);
    await capture(page, '04-no-permission-note');
  });

  test('an administrator sees the add link on a filled site list', async ({ page }) => {
    await signIn(page, users.admin);
    await page.goto('/ncr/new');
    await openSelect(page, 'Site');
    await expect(page.getByTestId('setup-add-site')).toHaveAttribute('href', '/admin/sites');
    await expectOnTop(page, 'setup-row-site', /Test Plant/);
    await capture(page, '05-filled-dropdown-ncr-site-admin');
    await page.keyboard.press('Escape');
    // searchable site picker: the shortcut heads the open list as well
    await page.goto('/admin/industry-packs');
    await page.getByTestId('pack-site').click();
    await expect(page.getByTestId('setup-add-site')).toBeVisible();
    await expectOnTop(page, 'setup-row-site', /Test Plant/);
  });

  test('the entry log offers Record entry to people who may record', async ({ page }) => {
    await signIn(page, users.author);
    await page.goto('/production/entries');
    const btn = page.locator('a.MuiButton-root', { hasText: 'Record entry' });
    await expect(btn).toHaveAttribute('href', '/production/entry');
    await capture(page, '06-entry-log-record-entry');
    await btn.click();
    await expect(page).toHaveURL(/\/production\/entry$/);
    await page.context().clearCookies();
    await signIn(page, users.viewer);
    await page.goto('/production/entries');
    await expect(page.getByRole('heading', { name: 'Shop-floor entry log' })).toBeVisible();
    await expect(page.locator('a.MuiButton-root', { hasText: 'Record entry' })).toHaveCount(0);
  });
});
