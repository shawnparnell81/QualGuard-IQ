import { test, expect, type Page } from '@playwright/test';
import { Api, localDate, signIn, state, users, shot } from './support';

/** Gap 11: assigned shipments are verified offline by scan; offline scans never release a shipment until the release conditions are confirmed online. */
const button = (p: Page, name: string | RegExp) => p.getByRole('button', { name });
async function scan(page: Page, code: string) {
  const f = page.getByLabel('Scan carton, pallet, lot or serial');
  await f.fill(code);
  await f.press('Enter');
}
interface Detail { shipment: { status: number; verificationState: number }; lines: { line: { id: string } }[]; picks: { task: { id: string; quantity: number }; locationCode: string; lotNumber?: string }[] }

test.describe('E2E 18: offline shipping verification at the scan station', () => {
  test('a verifier checks cartons offline, duplicates and wrong lots are stopped, a hold and a cancellation made meanwhile are caught, and only an online confirmation releases', async ({ page, context }) => {
    const api = await Api.as(users.author);
    const { siteId, ids } = state();
    const t = Date.now().toString(36).toUpperCase();
    const loc = (code: string, kind: number, purpose: number, parentId: string | null) => api.post<{ id: string }>('/api/warehouse/locations', { code, name: code, kind, purpose, parentId, capacity: 500, siteId, departmentId: null });
    const wh = await loc(`VWH-${t}`, 0, 0, null); const dock = await loc(`VDK-${t}`, 2, 1, wh.id); const bin = await loc(`VA1-${t}`, 6, 0, wh.id); const ship = await loc(`VSH-${t}`, 2, 6, wh.id);
    const { id: partId } = await api.post<{ id: string }>('/api/master/parts', { partNumber: `VK-${t}`, name: 'Valve body', partType: 0, revision: 'A', unitOfMeasure: 'EA', siteId, status: 0 });
    const lots = ['VA', 'VB', 'VC'].map((x) => `${x}-${t}`);
    const rc = await api.post<{ id: string }>('/api/warehouse/receipts', { kind: 3, supplierId: null, purchaseOrderId: null, asnNumber: null, packingSlip: null, carrierId: null, trackingNumber: null, receivedOn: localDate(), receivingLocationId: dock.id, notes: null,
      supplierOverrideReason: null, siteId, departmentId: null, lines: lots.map((lotNumber) => ({ partId, revision: 'A', purchaseOrderLineId: null, quantityExpected: null, quantityReceived: 10, supplierLotNumber: null, lotNumber,
        serialNumbers: null, inspectionRequired: false, inspectionPlanId: null, aqlPlanId: null, aqlInspectionLevel: null, aqlLevel: null, putAwayLocationId: bin.id, clientRequestId: null })) });
    const lotIds = (await api.get<{ lines: { line: { lotId: string } }[] }>(`/api/warehouse/receipts/${rc.id}`)).lines.map((l) => l.line.lotId);
    const { id: customerId } = await api.post<{ id: string }>('/api/master/customers', { code: `VC${t}`, name: `Verify Customer ${t}`, siteId, status: 0 });
    const carrierId = (await api.get<{ id: string; code: string }[]>('/api/shipping/carriers')).find((c) => c.code === 'UPS')!.id;
    /** A picked and packed shipment of one lot, in cartons of the given sizes, assigned to the verifier. */
    const packed = async (lot: number, cartons: number[]) => {
      const quantity = cartons.reduce((a, b) => a + b, 0);
      const s = await api.post<{ id: string; number: string }>('/api/shipping/shipments', { kind: 0, customerId, supplierId: null, toSiteId: null, shipTo: 'Dock 2', customerPo: null, promisedOn: localDate(1), stagingLocationId: ship.id,
        carrierId, serviceLevel: null, notes: null, siteId, departmentId: null, lines: [{ partId, revision: null, quantity, lotId: lotIds[lot] }] });
      await api.post(`/api/shipping/shipments/${s.id}/pick-list`);
      const d = await api.get<Detail>(`/api/shipping/shipments/${s.id}`);
      for (const p of d.picks) await api.post(`/api/shipping/shipments/${s.id}/picks/${p.task.id}/confirm`, { locationCode: p.locationCode, itemCode: `QG:LOT:${lots[lot]}`, quantity: p.task.quantity });
      const nos: string[] = [];
      for (const q of cartons) nos.push((await api.post<{ number: string }>(`/api/shipping/shipments/${s.id}/containers`, { kind: 0, parentId: null, weight: 2, items: [{ lineId: d.lines[0].line.id, lotId: lotIds[lot], serialId: null, quantity: q }] })).number);
      await api.put(`/api/shipping/shipments/${s.id}/verifier`, { verifierId: ids.author });
      return { ...s, cartons: nos };
    };
    const s1 = await packed(0, [4, 2]);
    const s2 = await packed(1, [2]);
    const s3 = await packed(2, [3]);

    await signIn(page, users.author);
    await page.setViewportSize({ width: 412, height: 915 });
    await page.goto('/warehouse/scan?flow=verify');
    await button(page, 'Download my verifications').click();
    await expect(page.getByTestId('verify-result')).toContainText(/Downloaded \d+ shipments? assigned to you for verification/);
    for (const s of [s1, s2, s3]) await expect(page.getByTestId('verify-lists')).toContainText(s.number);

    // Offline: the device checks each scan against the downloaded packing tree.
    await context.setOffline(true);
    await expect(page.getByText('Offline — scans are queued')).toBeVisible();
    await page.getByTestId('verify-lists').getByRole('button', { name: new RegExp(s1.number) }).click();
    await scan(page, `QG:LOT:${lots[1]}`);
    await expect(page.getByTestId('verify-result')).toContainText(`Wrong lot: lot ${lots[1]} was not packed on this shipment.`);
    await scan(page, `QG:CTN:${s1.cartons[0]}`);
    await expect(page.getByTestId('verify-result')).toContainText('saved on this device');
    await scan(page, s1.cartons[0]);
    await expect(page.getByTestId('verify-result')).toContainText('already scanned');
    await scan(page, s1.cartons[1]);
    await expect(page.getByTestId('verify-pending')).toContainText('pending validation');
    await expect(page.getByTestId('verify-state')).toHaveText('Pending validation');
    await expect(button(page, 'Confirm release conditions online')).toBeDisabled();
    await expect(page.getByTestId('sync-pending')).toContainText('2 pending sync');

    // Two more shipments verified offline; meanwhile the office cancels one and holds the lot of the other.
    for (const s of [s2, s3]) {
      await button(page, 'All my shipments').click();
      await page.getByTestId('verify-lists').getByRole('button', { name: new RegExp(s.number) }).click();
      await scan(page, s.cartons[0]);
      await expect(page.getByTestId('verify-pending')).toBeVisible();
    }
    await api.post(`/api/shipping/shipments/${s2.id}/cancel`, { returnLocationId: bin.id, reason: 'Order withdrawn' });
    await api.post('/api/warehouse/holds', { kind: 0, reason: 'Suspect heat treat', partId, lotId: lotIds[2], serialId: null, locationId: null, ncrId: null, customerId: null, supplierId: null, siteId });

    // Reconnect: s1 and s3 scans sync once, the cancelled shipment is a conflict for review.
    await context.setOffline(false);
    await expect(page.getByTestId('sync-conflict')).toContainText('Conflict requires review (1)', { timeout: 20_000 });
    await expect(page.getByTestId('sync-conflict')).toContainText('cancelled while you were offline');
    await expect(page.getByTestId('sync-done')).toContainText('the server accepted 3 items');
    expect((await api.get<Detail>(`/api/shipping/shipments/${s1.id}`)).shipment.status).toBe(3); // still Packed: synced offline scans do not verify
    expect((await api.get<Detail>(`/api/shipping/shipments/${s1.id}`)).shipment.verificationState).toBe(2);
    const ship1 = { carrierId, trackingNumber: `1Z${t}`, serviceLevel: 'Ground', grossWeight: 6 };
    await expect(api.post(`/api/shipping/shipments/${s1.id}/ship`, ship1)).rejects.toThrow(/409.*shipment is Packed/);

    // The held shipment is refused at the online confirmation.
    await expect(button(page, 'Confirm release conditions online')).toBeEnabled();
    await button(page, 'Confirm release conditions online').click();
    await expect(page.getByTestId('verify-result')).toContainText('Release blocked');
    await expect(page.getByTestId('verify-checks')).toContainText('✗ Holds');

    // s1 is confirmed online and then released.
    await button(page, 'All my shipments').click();
    await page.getByTestId('verify-lists').getByRole('button', { name: new RegExp(s1.number) }).click();
    await button(page, 'Confirm release conditions online').click();
    await expect(page.getByTestId('verify-result')).toContainText('Release conditions confirmed');
    await expect(page.getByTestId('verify-validated')).toBeVisible();
    await expect(page.getByTestId('verify-checks')).toContainText('✓ Staged inventory');
    await api.post(`/api/shipping/shipments/${s1.id}/ship`, ship1);
    expect((await api.get<Detail>(`/api/shipping/shipments/${s1.id}`)).shipment.status).toBe(5);

    // The desk sees every scan with device time, the offline flag and the refusal reasons.
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(`/shipping/shipments/${s2.id}`);
    await page.getByRole('tab', { name: 'Verify' }).click();
    await expect(page.getByTestId('verification-scans')).toContainText('cancelled while you were offline');
    await page.goto(`/shipping/shipments/${s3.id}`);
    await page.getByRole('tab', { name: 'Verify' }).click();
    await expect(page.getByTestId('pending-validation')).toContainText('release conditions are not confirmed');
    await expect(page.getByTestId('verification-scans')).toContainText('offline');
    await page.goto(`/shipping/shipments/${s1.id}`);
    await page.getByRole('tab', { name: 'Custody' }).click();
    await expect(page.getByText('Verification confirmed online')).toBeVisible();
    await shot(page, '11-offline-verification');
  });

  test('a lead assigns the verifier at the desk; signing out wipes the downloaded verification work', async ({ page }) => {
    const api = await Api.as(users.author);
    const { siteId } = state();
    const t = Date.now().toString(36).toUpperCase();
    const loc = (code: string, kind: number, purpose: number, parentId: string | null) => api.post<{ id: string }>('/api/warehouse/locations', { code, name: code, kind, purpose, parentId, capacity: 500, siteId, departmentId: null });
    const wh = await loc(`WWH-${t}`, 0, 0, null); const dock = await loc(`WDK-${t}`, 2, 1, wh.id); const bin = await loc(`WA1-${t}`, 6, 0, wh.id); const ship = await loc(`WSH-${t}`, 2, 6, wh.id);
    const { id: partId } = await api.post<{ id: string }>('/api/master/parts', { partNumber: `WK-${t}`, name: 'Gasket', partType: 0, revision: 'A', unitOfMeasure: 'EA', siteId, status: 0 });
    const rc = await api.post<{ id: string }>('/api/warehouse/receipts', { kind: 3, supplierId: null, purchaseOrderId: null, asnNumber: null, packingSlip: null, carrierId: null, trackingNumber: null, receivedOn: localDate(), receivingLocationId: dock.id, notes: null,
      supplierOverrideReason: null, siteId, departmentId: null, lines: [{ partId, revision: 'A', purchaseOrderLineId: null, quantityExpected: null, quantityReceived: 4, supplierLotNumber: null, lotNumber: `WL-${t}`,
        serialNumbers: null, inspectionRequired: false, inspectionPlanId: null, aqlPlanId: null, aqlInspectionLevel: null, aqlLevel: null, putAwayLocationId: bin.id, clientRequestId: null }] });
    const { id: customerId } = await api.post<{ id: string }>('/api/master/customers', { code: `WC${t}`, name: `Gasket Customer ${t}`, siteId, status: 0 });
    const s = await api.post<{ id: string; number: string }>('/api/shipping/shipments', { kind: 0, customerId, supplierId: null, toSiteId: null, shipTo: 'Dock 1', customerPo: null, promisedOn: localDate(1), stagingLocationId: ship.id,
      carrierId: null, serviceLevel: null, notes: null, siteId, departmentId: null, lines: [{ partId, revision: null, quantity: 2, lotId: null }] });
    await api.post(`/api/shipping/shipments/${s.id}/pick-list`);
    const d = await api.get<Detail>(`/api/shipping/shipments/${s.id}`);
    for (const p of d.picks) await api.post(`/api/shipping/shipments/${s.id}/picks/${p.task.id}/confirm`, { locationCode: p.locationCode, itemCode: `QG:LOT:WL-${t}`, quantity: p.task.quantity });
    const lotId = (await api.get<{ lines: { line: { lotId: string } }[] }>(`/api/warehouse/receipts/${rc.id}`)).lines[0].line.lotId;
    await api.post(`/api/shipping/shipments/${s.id}/containers`, { kind: 0, parentId: null, weight: 1, items: [{ lineId: d.lines[0].line.id, lotId, serialId: null, quantity: 2 }] });

    await signIn(page, users.author);
    await page.goto(`/shipping/shipments/${s.id}`);
    await page.getByRole('tab', { name: 'Verify' }).click();
    const assign = page.getByTestId('verifier-assign');
    await assign.getByLabel('Assigned verifier').click();
    await page.getByRole('option', { name: users.author.name }).click();
    await assign.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByText('Verifier assigned; the shipment appears in their scan-station download.')).toBeVisible();
    await expect(assign).toContainText(`Assigned to ${users.author.name}`);

    await page.goto('/warehouse/scan?flow=verify');
    await button(page, 'Download my verifications').click();
    await expect(page.getByTestId('verify-result')).toContainText('assigned to you for verification');
    expect(await page.evaluate(() => Object.keys(localStorage).filter((k) => k.startsWith('qg.verify.')).length)).toBe(1);
    await page.getByRole('button', { name: 'User menu' }).click();
    await page.getByRole('menuitem', { name: 'Sign out' }).click();
    await expect(page.getByLabel('Email')).toBeVisible();
    expect(await page.evaluate(() => Object.keys(localStorage).filter((k) => k.startsWith('qg.verify.')).length)).toBe(0);
  });
});
