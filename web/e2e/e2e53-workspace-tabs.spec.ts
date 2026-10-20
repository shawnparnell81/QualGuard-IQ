import { test, expect, type Page } from '@playwright/test';
import { shot, signIn, users } from './support';

/** Screenshots for review go in tabs-shots when E2E_SCREENSHOT_DIR is set. */
const capture = async (p: Page, name: string, fullPage = false) => { await p.waitForTimeout(500); await shot(p, `tabs-shots/${name}`, { fullPage }); };
const view = (p: Page) => p.locator('[data-testid^="tab-view-"][data-active="true"]');
const strip = (p: Page) => p.getByTestId('tab-strip');
const tabs = (p: Page) => strip(p).locator('[data-tab-url]');
/** Moves within the application the way a link does (no page reload). */
const go = (p: Page, url: string) => p.evaluate((u) => { history.pushState({}, '', u); dispatchEvent(new PopStateEvent('popstate')); }, url);
const tabFor = (p: Page, path: string) => strip(p).locator(`[data-tab-url="${path}"]`);

test.describe('E2E 53: tabs inside the application', () => {
  test('several tabs keep their typing, Back follows each tab, unsaved tabs ask before closing, tabs come back after a reload', async ({ page }) => {
    await signIn(page, users.admin);
    await expect(tabs(page)).toHaveCount(1);
    await expect(page.getByTestId('tab-home')).toHaveAttribute('data-active', 'true');

    // from Home every page opens in its own tab
    await go(page, '/ncr/new');
    await expect(tabs(page)).toHaveCount(2);
    const box = view(page).getByRole('textbox').first();
    await box.fill('Burr on housing flange');
    await view(page).getByTestId('page-home').click();
    await expect(page).toHaveURL(/\/$/);
    await go(page, '/audits');
    await expect(tabFor(page, '/audits')).toHaveCount(1);
    await go(page, '/audits/findings');
    await expect(tabFor(page, '/audits/findings')).toHaveCount(1);
    await view(page).getByTestId('page-home').click();
    await go(page, '/calibration/equipment');
    await expect(tabs(page)).toHaveCount(4);
    await capture(page, '01-several-tabs');

    // switching back keeps the half-typed form
    await tabFor(page, '/ncr/new').click();
    await expect(page).toHaveURL(/\/ncr\/new$/);
    await expect(view(page).getByRole('textbox').first()).toHaveValue('Burr on housing flange');
    await expect(tabFor(page, '/ncr/new').getByTestId('tab-close')).toBeVisible();

    // Back belongs to the tab: the audits tab went on to findings, the equipment tab has nowhere to go back to
    await tabFor(page, '/audits/findings').click();
    await expect(view(page).getByTestId('page-back')).toBeEnabled();
    await tabFor(page, '/calibration/equipment').click();
    await expect(view(page).getByTestId('page-back')).toBeDisabled();
    await tabFor(page, '/audits/findings').click();
    await view(page).getByTestId('page-back').click();
    await expect(page).toHaveURL(/\/audits$/);
    await expect(tabFor(page, '/audits')).toHaveAttribute('data-active', 'true');
    await expect(view(page).getByTestId('page-back')).toBeDisabled();
    await capture(page, '02-back-and-home');
    // the browser's own back stays in step with the window address
    await page.goBack();
    await expect(page).toHaveURL(/\/audits\/findings$/);
    await expect(tabs(page)).toHaveCount(4);

    // closing the tab with typing asks first
    await tabFor(page, '/ncr/new').getByTestId('tab-close').click();
    await expect(page.getByRole('dialog')).toContainText("haven't been saved");
    await capture(page, '03-unsaved-warning');
    await page.getByRole('button', { name: 'Keep it open' }).click();
    await expect(tabFor(page, '/ncr/new')).toHaveCount(1);
    await tabFor(page, '/ncr/new').getByTestId('tab-close').click();
    await page.getByRole('button', { name: 'Close without saving' }).click();
    await expect(tabFor(page, '/ncr/new')).toHaveCount(0);
    await expect(tabs(page)).toHaveCount(3);

    // reload: the same tabs come back, the window address shows the active one
    const before = await tabs(page).evaluateAll((els) => els.map((e) => e.getAttribute('data-tab-url')));
    await page.reload();
    await expect(tabs(page)).toHaveCount(3);
    expect(await tabs(page).evaluateAll((els) => els.map((e) => e.getAttribute('data-tab-url')))).toEqual(before);
  });

  test('right-click and Ctrl-click open in a new tab, the limit is explained, narrow screens use a tab menu', async ({ page }) => {
    await signIn(page, users.admin);
    await go(page, '/ncr');
    const link = view(page).locator('aside a[href="/capa"]').first();
    await link.click({ button: 'right' });
    await expect(page.getByTestId('open-in-new-tab')).toBeVisible();
    await capture(page, '04-right-click-open-in-new-tab');
    await page.getByTestId('open-in-new-tab').click();
    await expect(tabs(page)).toHaveCount(3);
    await expect(page).toHaveURL(/\/ncr$/); // opened behind the current tab
    await view(page).locator('aside a[href]').filter({ hasNot: page.locator('[href="/capa"]') }).nth(2).click({ modifiers: ['Control'] });
    await expect(tabs(page)).toHaveCount(4);
    await expect(page).toHaveURL(/\/ncr$/);

    // fill up to the limit
    await page.evaluate(() => {
      const key = Object.keys(localStorage).find((k) => k.startsWith('qg.tabs.v1.'))!;
      const s = JSON.parse(localStorage.getItem(key)!);
      const extra = ['/audits', '/audits/findings', '/training', '/training/courses', '/calibration/equipment', '/suppliers', '/complaints', '/changes', '/risks', '/documents', '/products/parts']
        .map((url, i) => ({ id: `x${i}`, url }));
      s.tabs = [...s.tabs, ...extra].slice(0, 15);
      localStorage.setItem(key, JSON.stringify(s));
    });
    await page.reload();
    await expect(tabs(page)).toHaveCount(15);
    await view(page).locator('aside a[href]').last().click({ modifiers: ['Control'] });
    await expect(page.getByTestId('tabs-notice')).toContainText('up to 15 tabs');
    await expect(tabs(page)).toHaveCount(15);
    await page.getByTestId('tabs-overflow').click();
    await expect(page.getByTestId('tabs-menu')).toContainText('15 of 15 tabs');
    await capture(page, '05-overflow-menu');
    await page.keyboard.press('Escape');

    await page.setViewportSize({ width: 390, height: 844 });
    await expect(page.getByTestId('tabs-switcher')).toBeVisible();
    await page.getByTestId('tabs-switcher').click();
    await expect(page.getByTestId('tabs-menu')).toBeVisible();
    await capture(page, '06-narrow-tab-menu');
  });

  test('tabs can be switched off in My appearance', async ({ page }) => {
    await signIn(page, users.admin);
    await page.goto('/account/appearance');
    await page.getByRole('switch', { name: 'Use tabs' }).uncheck();
    await expect(page.getByTestId('tab-strip')).toHaveCount(0);
    await expect(page.getByTestId('page-back')).toBeVisible();
    await page.getByRole('switch', { name: 'Use tabs' }).check();
    await expect(page.getByTestId('tab-strip')).toBeVisible();
  });
});
