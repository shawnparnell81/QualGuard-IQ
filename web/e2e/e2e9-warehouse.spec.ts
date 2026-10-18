import { test, expect, type Page } from '@playwright/test';
import { Api, dialog, localDate, signIn, state, unique, users, shot } from './support';

const button = (p: Page, name: string | RegExp) => p.getByRole('button', { name });
const tag = () => Date.now().toString(36).toUpperCase();

/** A part plus a small warehouse: WH → DOCK (receiving), B1 and B2 (storage), SHIP (shipping). */
async function warehouse(api: Api) {
  const siteId = state().siteId;
  const t = tag();
  const { id: productId } = await api.post<{ id: string }>('/api/master/products', { number: `PR-${t}`, name: unique('Valve'), category: 'Valves', siteId, status: 0 });
  const { id: partId } = await api.post<{ id: string }>('/api/master/parts', { partNumber: `PW-${t}`, name: 'Valve body', partType: 0, revision: 'A', unitOfMeasure: 'EA', productId, siteId, status: 0 });
  const loc = (code: string, kind: number, purpose: number, parentId: string | null) => api.post<{ id: string }>('/api/warehouse/locations', { code, name: code, kind, purpose, parentId, capacity: 500, siteId, departmentId: null });
  const wh = await loc(`WH-${t}`, 0, 0, null);
  const dock = await loc(`DK-${t}`, 2, 1, wh.id);
  const b1 = await loc(`B1-${t}`, 6, 0, wh.id);
  const b2 = await loc(`B2-${t}`, 6, 0, wh.id);
  const ship = await loc(`SH-${t}`, 2, 6, wh.id);
  return { t, siteId, partId, part: `PW-${t}`, wh: wh.id, dock: { id: dock.id, code: `DK-${t}` }, b1: { id: b1.id, code: `B1-${t}` }, b2: { id: b2.id, code: `B2-${t}` }, ship: { id: ship.id, code: `SH-${t}` } };
}
type W = Awaited<ReturnType<typeof warehouse>>;
async function receive(api: Api, w: W, quantity: number, to: string) {
  const r = await api.post<{ id: string }>('/api/warehouse/receipts', { kind: 3, supplierId: null, purchaseOrderId: null, asnNumber: null, packingSlip: null, carrierId: null, trackingNumber: null, receivedOn: localDate(), receivingLocationId: w.dock.id, notes: null,
    supplierOverrideReason: null, siteId: w.siteId, departmentId: null, lines: [{ partId: w.partId, revision: 'A', purchaseOrderLineId: null, quantityExpected: null, quantityReceived: quantity, supplierLotNumber: null, lotNumber: `L-${w.t}`,
      serialNumbers: null, inspectionRequired: false, inspectionPlanId: null, aqlPlanId: null, aqlInspectionLevel: null, aqlLevel: null, putAwayLocationId: to, clientRequestId: null }] });
  return { receiptId: r.id, lot: `L-${w.t}` };
}

test.describe('E2E 9: warehouse, scan station and shipping', () => {
  test('a receiver with blind receiving rights receives at the scan station and the stock shows on the warehouse pages', async ({ page }) => {
    const api = await Api.as(users.author);
    const w = await warehouse(api);
    await signIn(page, users.author);
    await page.setViewportSize({ width: 412, height: 915 }); // phone
    await page.goto('/warehouse/scan?flow=blind');
    await page.getByLabel('Scan dock location').fill(`QG:LOC:${w.dock.code}`);
    await page.keyboard.press('Enter');
    await page.getByLabel(/Scan part label/).fill(`QG:PART:${w.part}`);
    await page.keyboard.press('Enter');
    await page.getByRole('spinbutton').fill('12');
    await button(page, /^Receive$/).last().click();
    await expect(page.getByText(/Receipt .* posted\./)).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(412); // nothing wider than the phone

    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto('/warehouse/stock');
    await expect(page.getByRole('cell', { name: w.part }).first()).toBeVisible();
    await page.goto('/warehouse/receipts');
    await page.getByRole('cell', { name: 'Blind' }).first().click();
    await expect(page.getByText(new RegExp(`1\\. ${w.part}`))).toBeVisible();
  });

  test('a move scanned without a connection is queued, then posts once the connection is back', async ({ page, context }) => {
    const api = await Api.as(users.author);
    const w = await warehouse(api);
    const { lot } = await receive(api, w, 20, w.b1.id);
    await signIn(page, users.author);
    await page.goto('/warehouse/scan?flow=move');
    await expect(page.getByText('Online')).toBeVisible();
    await context.setOffline(true);
    await expect(page.getByText('Offline — scans are queued')).toBeVisible();
    await page.getByLabel('Scan lot, serial or part').fill(`QG:LOT:${lot}`);
    await page.keyboard.press('Enter');
    await page.getByLabel('Scan source location').fill(`QG:LOC:${w.b1.code}`);
    await page.keyboard.press('Enter');
    await page.getByLabel('Scan destination location').fill(`QG:LOC:${w.b2.code}`);
    await page.keyboard.press('Enter');
    await page.getByRole('spinbutton').fill('8');
    await button(page, /^Move$/).click();
    await expect(page.getByText(/saved on this device/)).toBeVisible();
    await expect(page.getByTestId('sync-pending')).toContainText('Saved locally — 1 pending sync (offline)');
    // Back online but the API is unreachable: the item stays local, survives a refresh, and is not cleared early.
    await page.route('**/api/warehouse/moves/scan', (r) => r.abort('internetdisconnected'));
    await context.setOffline(false);
    await page.reload();
    await expect(page.getByTestId('sync-pending')).toContainText('Saved locally — 1 pending sync');
    await page.getByRole('button', { name: 'Sync now' }).click();
    await expect(page.getByTestId('sync-pending')).toContainText('1 pending sync');
    await expect(page.getByTestId('sync-done')).toHaveCount(0);
    // Connection restored: the server accepts it once and the local-only banner clears.
    await page.unroute('**/api/warehouse/moves/scan');
    await page.getByRole('button', { name: 'Sync now' }).click();
    await expect(page.getByTestId('sync-done')).toContainText('Synchronized — the server accepted 1 item', { timeout: 20_000 });
    await expect(page.getByTestId('sync-pending')).toHaveCount(0);
    // Stock that is no longer there when the queued move reaches the server is a conflict for a person to review, kept apart from the pending items.
    await context.setOffline(true);
    await page.getByLabel('Scan lot, serial or part').fill(`QG:LOT:${lot}`);
    await page.keyboard.press('Enter');
    await page.getByLabel('Scan source location').fill(`QG:LOC:${w.b1.code}`);
    await page.keyboard.press('Enter');
    await page.getByLabel('Scan destination location').fill(`QG:LOC:${w.b2.code}`);
    await page.keyboard.press('Enter');
    await page.getByRole('spinbutton').fill('999');
    await button(page, /^Move$/).click();
    await context.setOffline(false); // the online event starts the sync by itself
    await expect(page.getByTestId('sync-conflict')).toContainText('Conflict requires review (1)', { timeout: 20_000 });
    await expect(page.getByTestId('sync-conflict')).toContainText('is available at');
    await expect(page.getByTestId('sync-pending')).toHaveCount(0);
    await expect(page.getByTestId('sync-done')).toHaveCount(0); // no stale "synchronized" message for the refused item
    await page.getByRole('button', { name: 'Reviewed — remove' }).click();
    await expect(page.getByTestId('sync-conflict')).toHaveCount(0);

    const stock = await api.get<{ locationCode: string; onHand: number }[]>(`/api/warehouse/stock?partId=${w.partId}`);
    expect(stock.find((s) => s.locationCode === w.b2.code)?.onHand).toBe(8);
    expect(stock.find((s) => s.locationCode === w.b1.code)?.onHand).toBe(12);
    await shot(page, '188-gap16-offline-queue-banner');
  });

  test('a shipment is picked, packed, scan-verified and shipped with a bill of lading', async ({ page }) => {
    const api = await Api.as(users.author);
    const w = await warehouse(api);
    await receive(api, w, 10, w.b1.id);
    const carrier = await api.post<{ id: string }>('/api/shipping/carriers', { code: `CR-${w.t}`, name: `Freight ${w.t}`, kind: 1, trackingUrl: 'https://track.example/{tracking}', scac: 'FRTX', active: true });
    const { id: customerId } = await api.post<{ id: string }>('/api/master/customers', { code: `C-${w.t}`, name: unique('Acme Pumps'), contactName: null, email: null, phone: null, address: null, siteId: w.siteId, departmentId: null, status: 0 });
    const sh = await api.post<{ id: string; number: string }>('/api/shipping/shipments', { kind: 0, customerId, supplierId: null, toSiteId: null, shipTo: 'Dock 4, Acme Pumps', customerPo: `PO-${w.t}`, promisedOn: localDate(3), stagingLocationId: w.ship.id,
      carrierId: carrier.id, serviceLevel: 'LTL', notes: null, siteId: w.siteId, departmentId: null, lines: [{ partId: w.partId, revision: 'A', quantity: 6, lotId: null }] });
    await signIn(page, users.author);
    await page.goto(`/shipping/shipments/${sh.id}`);
    await button(page, 'Build pick list').click();
    await expect(page.getByText(w.b1.code)).toBeVisible();
    await page.getByLabel('Scan location').fill(`QG:LOC:${w.b1.code}`);
    await page.getByLabel('Scan item (lot / serial)').fill(`QG:LOT:L-${w.t}`);
    await button(page, 'Confirm pick').click();
    await expect(page.getByText(/Picked .* from/)).toBeVisible();

    await page.getByRole('tab', { name: 'Pack' }).click();
    await page.getByLabel('Qty').fill('6');
    await button(page, /^Pack$/).click();
    await expect(page.getByText(/QG:CTN:/)).toBeVisible();
    const carton = (await page.getByText(/QG:CTN:/).first().textContent())!.trim();

    await page.getByRole('tab', { name: 'Verify' }).click();
    await page.getByLabel(/Scan QG:CTN/).fill(carton);
    await page.keyboard.press('Enter');
    await button(page, /Verify 1 scan/).click();
    await expect(page.getByText('All lines verified. The shipment is ready to ship.')).toBeVisible();

    await button(page, /^Ship$/).click();
    await dialog(page).getByLabel('Tracking / PRO number').fill(`PRO${w.t}`);
    await dialog(page).getByRole('button', { name: 'Ship' }).click();
    await expect(button(page, 'Mark delivered')).toBeVisible();
    await page.getByRole('tab', { name: 'Ship & documents' }).click();
    await expect(page.getByRole('link', { name: 'Bill of lading (PDF)' })).toBeVisible();
    await expect(page.getByRole('link', { name: `Track with Freight ${w.t}` })).toHaveAttribute('href', `https://track.example/PRO${w.t}`);
    const left = await api.get<{ locationCode: string; onHand: number }[]>(`/api/warehouse/stock?partId=${w.partId}`);
    expect(left.find((s) => s.locationCode === w.b1.code)?.onHand).toBe(4);
  });

  test('a frozen count blocks movement, records a variance and goes to approval; viewers cannot edit', async ({ page }) => {
    const api = await Api.as(users.author);
    const w = await warehouse(api);
    const { lot } = await receive(api, w, 10, w.b1.id);
    const c = await api.post<{ id: string }>('/api/warehouse/counts', { frequency: 4, locationId: w.b1.id, partId: null, freeze: true, scheduledOn: localDate(), notes: null });
    expect(await api.status('POST', '/api/warehouse/moves/scan', { item: `QG:LOT:${lot}`, from: `QG:LOC:${w.b1.code}`, to: `QG:LOC:${w.b2.code}`, quantity: 1, clientRequestId: null })).toBe(409);
    await signIn(page, users.author);
    await page.goto(`/warehouse/counts/${c.id}`);
    await expect(page.getByText(`${w.b1.code} frozen`)).toBeVisible();
    await page.getByLabel(new RegExp(`Counted ${w.part}`)).fill('9');
    await button(page, 'Save counts').click();
    await expect(page.getByText('Counts saved.')).toBeVisible();
    await expect(page.getByRole('cell', { name: /-1 \(-10\.0%\)/ })).toBeVisible();
    await button(page, 'Submit for approval').click();
    await expect(page.getByText(/Submitted\./)).toBeVisible();

    const viewer = await page.context().browser()!.newPage();
    await signIn(viewer, users.viewer);
    await viewer.goto('/warehouse/locations');
    await expect(viewer.getByRole('cell', { name: new RegExp(w.b1.code) }).first()).toBeVisible();
    await expect(viewer.getByRole('button', { name: 'New warehouse' })).toHaveCount(0);
    await viewer.close();
  });
});
