import { test, expect, type Page } from '@playwright/test';
import { Api, localDate, signIn, state, unique, users } from './support';

/** Saves a phone capture into PHONE_SHOTS_DIR when it is set (before/after review captures); does nothing otherwise. */
async function capture(page: Page, name: string) {
  const dir = process.env.PHONE_SHOTS_DIR;
  if (!dir) return;
  await page.waitForLoadState('networkidle', { timeout: 8_000 }).catch(() => undefined);
  await page.screenshot({ path: `${dir}/${name}.png`, fullPage: true });
}

/** Wider than the screen means the whole page scrolls sideways; wide tables must scroll inside their own box instead. */
async function expectFits(page: Page, path: string, shotName?: string) {
  await page.goto(path);
  await page.getByRole('main').waitFor({ timeout: 20_000 });
  await page.waitForLoadState('networkidle', { timeout: 8_000 }).catch(() => undefined);
  if (shotName) await capture(page, shotName);
  await expect.soft.poll(async () => page.evaluate(() => document.documentElement.scrollWidth), { message: `${path} scrolls sideways` }).toBeLessThanOrEqual(391);
}

test.describe('E2E 56: phone width', () => {
  test.use({ viewport: { width: 390, height: 844 } });

  test('a signed-out visit logs no failed request', async ({ page }) => {
    const errors: string[] = [];
    // failed loads show up once as a response; other console errors are kept as they are
    page.on('console', (m) => { if (m.type() === 'error' && !m.text().startsWith('Failed to load resource')) errors.push(m.text()); });
    page.on('response', (r) => { const u = new URL(r.url()); if (r.status() >= 400 && u.pathname.startsWith('/api/')) errors.push(`${r.status()} ${u.pathname}`); });
    await page.goto('/');
    await expect(page.getByLabel(/Email/i).first()).toBeVisible();
    await page.waitForLoadState('networkidle', { timeout: 8_000 }).catch(() => undefined);
    expect(errors).toEqual([]);
  });

  test('record and list pages fit a 390px phone with no sideways page scroll', async ({ page }) => {
    test.setTimeout(240_000);
    const api = await Api.as(users.admin);
    const siteId = state().siteId;
    const ncr = await api.post<{ id: string }>('/api/ncrs', { title: unique('Phone width NCR with a fairly long title to wrap'), description: 'Burr on the outer edge of the housing flange.', source: 'In-process', siteId, detectedOn: localDate(), severity: 1 });
    const t = Date.now().toString(36).toUpperCase();
    const { id: productId } = await api.post<{ id: string }>('/api/master/products', { number: `PR-${t}`, name: unique('Pump'), category: 'Pumps', siteId, status: 0 });
    const { id: partId } = await api.post<{ id: string }>('/api/master/parts', { partNumber: `PX-${t}`, name: 'Impeller', partType: 0, revision: 'C', unitOfMeasure: 'EA', productId, siteId, status: 0 });
    const wo = await api.post<{ id: string }>('/api/production/work-orders', { number: `WO-${t}`, kind: 0, partId, revision: 'C', quantity: 10, startOn: localDate(), dueOn: localDate(7), routingRef: null,
      operations: [{ sequence: 10, name: 'Machine the outer bore', workCenter: 'CNC-1' }, { sequence: 20, name: 'Final inspection', workCenter: 'QC-1' }], siteId, departmentId: null });

    await signIn(page, users.admin);
    await expectFits(page, `/ncr/${ncr.id}`, 'phone-ncr-detail');
    await expectFits(page, '/ncr', 'phone-ncr-list');
    await expectFits(page, `/production/work-orders/${wo.id}`, 'phone-work-order-detail');
    for (const p of ['/capa', '/production/work-orders', '/coq', '/platform/marketplace', '/calibration/equipment', '/documents', '/suppliers', '/audits', '/complaints', '/training']) await expectFits(page, p, `phone-list${p.replace(/\//g, '-')}`);
  });
});
