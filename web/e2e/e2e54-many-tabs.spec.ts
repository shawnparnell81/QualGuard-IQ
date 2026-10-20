import { test, expect, type Locator, type Page } from '@playwright/test';
import { shot, signIn, users } from './support';

const capture = async (p: Page, name: string) => { await p.waitForTimeout(500); await shot(p, `tabfix-shots/${name}`); };
const view = (p: Page) => p.locator('[data-testid^="tab-view-"][data-active="true"]');
const strip = (p: Page) => p.getByTestId('tab-strip');
const tabs = (p: Page) => strip(p).locator('[data-tab-url]');
const urls = (p: Page) => tabs(p).evaluateAll((els) => els.map((e) => e.getAttribute('data-tab-url')!));
const go = (p: Page, url: string) => p.evaluate((u) => { history.pushState({}, '', u); dispatchEvent(new PopStateEvent('popstate')); }, url);

/** Links inside the shown page that lead somewhere other than where it is. */
async function linksIn(p: Page, scope: string, n: number): Promise<string[]> {
  const here = new URL(p.url()).pathname;
  await expect(view(p).locator(`${scope} a[href^="/"]`).first()).toBeVisible();
  const hrefs = await view(p).locator(`${scope} a[href^="/"]`).evaluateAll((els) => els.map((e) => e.getAttribute('href')!));
  const out = [...new Set(hrefs)].filter((h) => h !== '/' && h !== '/my-work' && h !== here && !h.startsWith('/api/')).slice(0, n);
  expect(out.length).toBe(n);
  return out;
}

type Opener = (p: Page, link: Locator) => Promise<void>;
const methods: [string, Opener][] = [
  ['middle-click', (_p, l) => l.click({ button: 'middle' })],
  ['Ctrl-click', (_p, l) => l.click({ modifiers: ['Control'] })],
  ['right-click Open in new tab', async (p, l) => { await l.click({ button: 'right' }); await p.getByTestId('open-in-new-tab').click(); }],
];

test.describe('E2E 54: every open adds a tab', () => {
  for (const [name, open] of methods) {
    test(`${name} opens four tabs, two from Home and two from another tab`, async ({ page }) => {
      await signIn(page, users.admin);
      await expect(tabs(page)).toHaveCount(1);
      const fromHome = await linksIn(page, '', 2);
      for (const [i, h] of fromHome.entries()) {
        await open(page, view(page).locator(`a[href="${h}"]`).first());
        await expect(tabs(page)).toHaveCount(2 + i);
      }
      await expect(page).toHaveURL(/\/$/); // opened behind Home
      // now from a tab that is not Home
      await strip(page).locator(`[data-tab-url="${fromHome[0]}"]`).click();
      await expect(page).toHaveURL(new RegExp(`${fromHome[0]}$`));
      const fromTab = await linksIn(page, 'aside', 2);
      for (const [i, h] of fromTab.entries()) {
        await open(page, view(page).locator(`aside a[href="${h}"]`).first());
        await expect(tabs(page)).toHaveCount(4 + i);
      }
      await expect(tabs(page)).toHaveCount(5);
      expect(await urls(page)).toEqual(expect.arrayContaining(['/', ...fromHome, ...fromTab]));
      if (name === 'middle-click') await capture(page, '01-four-tabs-middle-click');
    });
  }

  test('going to other areas from a tab that is not Home adds tabs instead of replacing it, and each tab keeps its typing and Back', async ({ page }) => {
    await signIn(page, users.admin);
    await go(page, '/ncr');
    await expect(tabs(page)).toHaveCount(2);
    // moving within NCRs stays in the tab and builds its Back
    await go(page, '/ncr/new');
    await expect(tabs(page)).toHaveCount(2);
    await view(page).getByRole('textbox').first().fill('Burr on housing flange');
    // top bar areas from the NCR tab: each gets a tab of its own
    for (const [i, url] of ['/capa', '/audits', '/training', '/calibration/equipment'].entries()) {
      await go(page, url);
      await expect(tabs(page)).toHaveCount(3 + i);
      await expect(strip(page).locator(`[data-tab-url="${url}"]`)).toHaveAttribute('data-active', 'true');
    }
    await expect(strip(page).locator('[data-tab-url="/ncr/new"]')).toHaveCount(1);
    await capture(page, '02-five-tabs-from-other-tabs');
    await strip(page).locator('[data-tab-url="/ncr/new"]').click();
    await expect(view(page).getByRole('textbox').first()).toHaveValue('Burr on housing flange');
    await expect(view(page).getByTestId('page-back')).toBeEnabled();
    await strip(page).locator('[data-tab-url="/capa"]').click();
    await expect(view(page).getByTestId('page-back')).toBeDisabled();
    // browser back returns to the tab it came from instead of adding another
    await page.goBack();
    await expect(tabs(page)).toHaveCount(6);
  });

  test('the add link in a picker opens a new tab every time and the form keeps its typing', async ({ page }) => {
    await signIn(page, users.admin);
    await page.goto('/production/work-orders/new');
    await expect(tabs(page)).toHaveCount(2);
    const number = view(page).getByLabel('Number (blank = next)');
    await number.fill('WO-TABS-1');
    for (let i = 0; i < 4; i++) {
      await view(page).getByLabel('Part', { exact: true }).click();
      await page.getByTestId('setup-add-part').click();
      await expect(tabs(page)).toHaveCount(3 + i);
      await expect(page).toHaveURL(/\/products\/parts\/new$/);
      await view(page).getByRole('textbox').first().fill(`P-${i}`);
      if (i === 3) await capture(page, '03-four-add-part-tabs');
      await strip(page).locator('[data-tab-url="/production/work-orders/new"]').click();
      await expect(view(page).getByLabel('Number (blank = next)')).toHaveValue('WO-TABS-1');
    }
    await expect(strip(page).locator('[data-tab-url="/products/parts/new"]')).toHaveCount(4);
    // each new part form kept its own typing (a new tab opens next to the one it came from, so the newest is first)
    for (let i = 0; i < 4; i++) {
      await strip(page).locator('[data-tab-url="/products/parts/new"]').nth(i).click();
      await expect(view(page).getByRole('textbox').first()).toHaveValue(`P-${3 - i}`);
    }
  });
});
