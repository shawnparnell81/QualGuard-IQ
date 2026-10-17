import { test, expect, type Page } from '@playwright/test';
import { Api, localDate, signIn, state, users } from './support';

/** Gap 10: assigned picks are downloaded to a phone, picked offline by scan, and synced once; work cancelled meanwhile comes back as a conflict. */
const button = (p: Page, name: string | RegExp) => p.getByRole('button', { name });
async function scan(page: Page, label: string | RegExp, code: string) {
  await page.getByLabel(label).fill(code);
  await page.getByLabel(label).press('Enter');
}

test.describe('E2E 17: offline picking at the scan station', () => {
  test('a picker downloads assigned picks, is stopped on wrong location and lot, picks offline, syncs once, and a shipment cancelled meanwhile is a conflict', async ({ page, context }) => {
    const api = await Api.as(users.author);
    const { siteId, ids } = state();
    const t = Date.now().toString(36).toUpperCase();
    const loc = (code: string, kind: number, purpose: number, parentId: string | null) => api.post<{ id: string }>('/api/warehouse/locations', { code, name: code, kind, purpose, parentId, capacity: 500, siteId, departmentId: null });
    const wh = await loc(`PWH-${t}`, 0, 0, null);
    const dock = await loc(`PDK-${t}`, 2, 1, wh.id);
    const bin = await loc(`PA1-${t}`, 6, 0, wh.id);
    const ship = await loc(`PSH-${t}`, 2, 6, wh.id);
    const { id: partId } = await api.post<{ id: string }>('/api/master/parts', { partNumber: `PK-${t}`, name: 'Pump housing', partType: 0, revision: 'A', unitOfMeasure: 'EA', siteId, status: 0 });
    await api.post('/api/warehouse/receipts', { kind: 3, supplierId: null, purchaseOrderId: null, asnNumber: null, packingSlip: null, carrierId: null, trackingNumber: null, receivedOn: localDate(), receivingLocationId: dock.id, notes: null,
      supplierOverrideReason: null, siteId, departmentId: null, lines: [{ partId, revision: 'A', purchaseOrderLineId: null, quantityExpected: null, quantityReceived: 20, supplierLotNumber: null, lotNumber: `PL-${t}`,
        serialNumbers: null, inspectionRequired: false, inspectionPlanId: null, aqlPlanId: null, aqlInspectionLevel: null, aqlLevel: null, putAwayLocationId: bin.id, clientRequestId: null }] });
    const { id: customerId } = await api.post<{ id: string }>('/api/master/customers', { code: `PC${t}`, name: `Pick Customer ${t}`, siteId, status: 0 });
    const shipment = async (quantity: number) => {
      const s = await api.post<{ id: string; number: string }>('/api/shipping/shipments', { kind: 0, customerId, supplierId: null, toSiteId: null, shipTo: 'Dock 4', customerPo: null, promisedOn: localDate(1), stagingLocationId: ship.id,
        carrierId: null, serviceLevel: null, notes: null, siteId, departmentId: null, lines: [{ partId, revision: null, quantity, lotId: null }] });
      await api.post(`/api/shipping/shipments/${s.id}/pick-list`);
      await api.put(`/api/shipping/shipments/${s.id}/picker`, { pickerId: ids.author });
      return s;
    };
    const s1 = await shipment(5);
    const s2 = await shipment(3);

    await signIn(page, users.author);
    await page.setViewportSize({ width: 412, height: 915 });
    await page.goto('/warehouse/scan?flow=pick');
    await button(page, 'Download my picks').click();
    await expect(page.getByTestId('pick-result')).toContainText(/Downloaded \d+ picks? on \d+ shipments? assigned to you/);
    await expect(page.getByTestId('pick-lists')).toContainText(s1.number);
    await expect(page.getByTestId('pick-lists')).toContainText(s2.number);

    // The connection drops; the downloaded work is still there and the scans are checked on the device.
    await context.setOffline(true);
    await expect(page.getByText('Offline — scans are queued')).toBeVisible();
    await page.getByTestId('pick-lists').getByRole('button', { name: new RegExp(s1.number) }).click();
    await expect(page.getByTestId('pick-next')).toContainText(`PA1-${t}`);
    await scan(page, 'Scan pick location', `QG:LOC:PSH-${t}`);
    await expect(page.getByTestId('pick-result')).toContainText(`Wrong location: pick from PA1-${t}.`);
    await scan(page, 'Scan pick location', `QG:LOC:PA1-${t}`);
    await scan(page, 'Scan the lot / serial picked', `QG:LOT:OTHER-${t}`);
    await expect(page.getByTestId('pick-result')).toContainText(`Wrong lot scanned: pick lot PL-${t}.`);
    await scan(page, 'Scan the lot / serial picked', `QG:LOT:PL-${t}`);
    await expect(page.getByLabel('Quantity picked')).toHaveValue('5');
    await button(page, 'Confirm pick').click();
    await expect(page.getByTestId('pick-result')).toContainText('saved on this device');
    await expect(page.getByText(`All picks on ${s1.number} are done`)).toBeVisible();
    await expect(page.getByTestId('sync-pending')).toContainText('Saved locally — 1 pending sync (offline)');

    // Second shipment picked offline too, but the office cancels it before the device reconnects.
    await button(page, 'All my picks').click();
    await page.getByTestId('pick-lists').getByRole('button', { name: new RegExp(s2.number) }).click();
    await scan(page, 'Scan pick location', `PA1-${t}`);
    await scan(page, 'Scan the lot / serial picked', `QG:LOT:PL-${t}`);
    await button(page, 'Confirm pick').click();
    await expect(page.getByTestId('sync-pending')).toContainText('2 pending sync');
    await api.post(`/api/shipping/shipments/${s2.id}/cancel`);

    // Reconnect: the first pick is accepted once, the cancelled one is a conflict for review.
    await context.setOffline(false);
    await expect(page.getByTestId('sync-conflict')).toContainText('Conflict requires review (1)', { timeout: 20_000 });
    await expect(page.getByTestId('sync-conflict')).toContainText('cancelled while you were offline');
    await expect(page.getByTestId('sync-done')).toContainText('the server accepted 1 item');
    await expect(page.getByTestId('sync-pending')).toHaveCount(0);
    const d1 = await api.get<{ shipment: { status: number }; picks: { task: { status: number; quantityPicked: number } }[] }>(`/api/shipping/shipments/${s1.id}`);
    expect(d1.shipment.status).toBe(2);
    expect(d1.picks.reduce((a, p) => a + p.task.quantityPicked, 0)).toBe(5);
    // A refreshed download no longer offers finished or cancelled work.
    await button(page, 'Refresh my picks').click();
    await expect(page.getByTestId('pick-lists')).toHaveCount(0);
    await expect(page.getByText('No picks are assigned to you.')).toBeVisible();

    // The desk sees the device confirmation (offline, device time) and the refusal with its reason.
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(`/shipping/shipments/${s2.id}`);
    await page.getByRole('tab', { name: 'Pick' }).click();
    await expect(page.getByTestId('pick-confirmations')).toContainText('shipment_cancelled');
    await page.goto(`/shipping/shipments/${s1.id}`);
    await page.getByRole('tab', { name: 'Pick' }).click();
    const log = page.getByTestId('pick-confirmations');
    await expect(log).toContainText('Accepted');
    await expect(log).toContainText('offline');
    await page.getByRole('tab', { name: 'Custody' }).click();
    await expect(page.getByText(/Picked 5 PK-.* \(offline\)/)).toBeVisible();
  });

  test('a lead assigns the pick list at the desk; signing out wipes the downloaded work from the device', async ({ page }) => {
    const api = await Api.as(users.author);
    const { siteId } = state();
    const t = Date.now().toString(36).toUpperCase();
    const loc = (code: string, kind: number, purpose: number, parentId: string | null) => api.post<{ id: string }>('/api/warehouse/locations', { code, name: code, kind, purpose, parentId, capacity: 500, siteId, departmentId: null });
    const wh = await loc(`QWH-${t}`, 0, 0, null); const dock = await loc(`QDK-${t}`, 2, 1, wh.id); const bin = await loc(`QA1-${t}`, 6, 0, wh.id); const ship = await loc(`QSH-${t}`, 2, 6, wh.id);
    const { id: partId } = await api.post<{ id: string }>('/api/master/parts', { partNumber: `QK-${t}`, name: 'Seal kit', partType: 0, revision: 'A', unitOfMeasure: 'EA', siteId, status: 0 });
    await api.post('/api/warehouse/receipts', { kind: 3, supplierId: null, purchaseOrderId: null, asnNumber: null, packingSlip: null, carrierId: null, trackingNumber: null, receivedOn: localDate(), receivingLocationId: dock.id, notes: null,
      supplierOverrideReason: null, siteId, departmentId: null, lines: [{ partId, revision: 'A', purchaseOrderLineId: null, quantityExpected: null, quantityReceived: 4, supplierLotNumber: null, lotNumber: `QL-${t}`,
        serialNumbers: null, inspectionRequired: false, inspectionPlanId: null, aqlPlanId: null, aqlInspectionLevel: null, aqlLevel: null, putAwayLocationId: bin.id, clientRequestId: null }] });
    const { id: customerId } = await api.post<{ id: string }>('/api/master/customers', { code: `QC${t}`, name: `Seal Customer ${t}`, siteId, status: 0 });
    const s = await api.post<{ id: string; number: string }>('/api/shipping/shipments', { kind: 0, customerId, supplierId: null, toSiteId: null, shipTo: 'Dock 1', customerPo: null, promisedOn: localDate(1), stagingLocationId: ship.id,
      carrierId: null, serviceLevel: null, notes: null, siteId, departmentId: null, lines: [{ partId, revision: null, quantity: 2, lotId: null }] });
    await api.post(`/api/shipping/shipments/${s.id}/pick-list`);

    await signIn(page, users.author);
    await page.goto(`/shipping/shipments/${s.id}`);
    await page.getByRole('tab', { name: 'Pick' }).click();
    const assign = page.getByTestId('picker-assign');
    await assign.getByLabel('Assigned picker').click();
    await page.getByRole('option', { name: users.author.name }).click();
    await assign.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByText('Picker assigned; the picks appear in their scan-station download.')).toBeVisible();
    await expect(assign).toContainText(`Assigned to ${users.author.name}`);

    await page.goto('/warehouse/scan?flow=pick');
    await button(page, 'Download my picks').click();
    await expect(page.getByTestId('pick-lists')).toContainText(s.number);
    expect(await page.evaluate(() => Object.keys(localStorage).filter((k) => k.startsWith('qg.picks.')).length)).toBe(1);
    await page.getByRole('button', { name: 'User menu' }).click();
    await page.getByRole('menuitem', { name: 'Sign out' }).click();
    await expect(page.getByLabel('Email')).toBeVisible();
    expect(await page.evaluate(() => Object.keys(localStorage).filter((k) => k.startsWith('qg.picks.')).length)).toBe(0);
  });
});
