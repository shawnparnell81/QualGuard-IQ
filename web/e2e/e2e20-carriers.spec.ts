import { test, expect, type Page } from '@playwright/test';
import { Api, choose, dialog, localDate, signIn, state, users, shot } from './support';

/** Gap 13: carrier connections (disabled and sandbox by default, enabled only after a passing test) and the shipment carrier flow with the fixture carrier. */
const button = (p: Page, name: string | RegExp) => p.getByRole('button', { name });
interface Detail { lines: { line: { id: string } }[]; picks: { task: { id: string; quantity: number }; locationCode: string; lotNumber?: string }[] }

test.describe('E2E 20: carrier connections', () => {
  test('an engineer adds a sandbox fixture connection that stays off until its test passes, then validates an address, rates, selects a service, creates labels and tracks a shipment to delivery; viewers only read', async ({ page, browser }) => {
    const api = await Api.as(users.author);
    const { siteId } = state();
    const t = Date.now().toString(36).toUpperCase();
    const site = (await api.get<{ id: string; code: string; name: string }[]>('/api/organization/sites')).find((s) => s.id === siteId)!;
    const carrier = await api.post<{ id: string }>('/api/shipping/carriers', { code: `FXC${t}`, name: `Fixture Parcel ${t}`, kind: 0, trackingUrl: null, scac: null, active: true });

    await signIn(page, users.author);
    await page.goto('/shipping/carrier-connections');
    await button(page, 'Add connection').click();
    await choose(page, 'Carrier', `Fixture Parcel ${t}`);
    await dialog(page).getByLabel('Connection name').fill(`Sandbox ${t}`);
    await dialog(page).getByRole('combobox', { name: 'Site' }).click();
    await page.getByRole('option', { name: new RegExp(site.code) }).click();
    await dialog(page).getByLabel('Street', { exact: true }).fill('100 Industrial Way');
    await dialog(page).getByLabel('City').fill('Dayton');
    await dialog(page).getByLabel('State / region').fill('OH');
    await dialog(page).getByLabel('Postal code').fill('45402');
    await dialog(page).getByRole('button', { name: 'Save' }).click();
    await expect(dialog(page)).toBeHidden();

    const card = page.getByTestId(`carrier-connection-Sandbox ${t}`);
    await expect(card.getByText('Disabled', { exact: true })).toBeVisible();
    await expect(card.getByText('Unverified', { exact: true })).toBeVisible();
    await expect(card.getByText('Before it can be enabled:')).toBeVisible();
    await expect(card.getByRole('button', { name: 'Enable' })).toBeDisabled();
    await card.getByRole('button', { name: 'Test connection' }).click();
    await expect(card.getByText('Connection test passed for this configuration.')).toBeVisible();
    await expect(card.getByText('Verified', { exact: true })).toBeVisible();
    await card.getByRole('button', { name: 'Enable' }).click();
    await expect(card.getByText('Enabled', { exact: true })).toBeVisible();
    await card.getByRole('button', { name: 'Request log' }).click();
    await expect(card.getByRole('table', { name: 'Carrier requests' })).toContainText('Connection test');

    // a viewer sees the card but cannot configure or test
    const viewer = await (await browser.newContext()).newPage();
    await signIn(viewer, users.viewer);
    await viewer.goto('/shipping/carrier-connections');
    await expect(viewer.getByTestId(`carrier-connection-Sandbox ${t}`)).toBeVisible();
    await expect(button(viewer, 'Add connection')).toHaveCount(0);
    await expect(viewer.getByTestId(`carrier-connection-Sandbox ${t}`).getByRole('button', { name: 'Test connection' })).toHaveCount(0);

    // a packed shipment of two cartons
    const loc = (code: string, kind: number, purpose: number, parentId: string | null) => api.post<{ id: string }>('/api/warehouse/locations', { code, name: code, kind, purpose, parentId, capacity: 500, siteId, departmentId: null });
    const wh = await loc(`CWH-${t}`, 0, 0, null); const dock = await loc(`CDK-${t}`, 2, 1, wh.id); const bin = await loc(`CA1-${t}`, 6, 0, wh.id); const ship = await loc(`CSH-${t}`, 2, 6, wh.id);
    const { id: partId } = await api.post<{ id: string }>('/api/master/parts', { partNumber: `CP-${t}`, name: 'Pump cover', partType: 0, revision: 'A', unitOfMeasure: 'EA', siteId, status: 0 });
    const rc = await api.post<{ id: string }>('/api/warehouse/receipts', { kind: 3, supplierId: null, purchaseOrderId: null, asnNumber: null, packingSlip: null, carrierId: null, trackingNumber: null, receivedOn: localDate(), receivingLocationId: dock.id, notes: null,
      supplierOverrideReason: null, siteId, departmentId: null, lines: [{ partId, revision: 'A', purchaseOrderLineId: null, quantityExpected: null, quantityReceived: 10, supplierLotNumber: null, lotNumber: `CL-${t}`,
        serialNumbers: null, inspectionRequired: false, inspectionPlanId: null, aqlPlanId: null, aqlInspectionLevel: null, aqlLevel: null, putAwayLocationId: bin.id, clientRequestId: null }] });
    const lotId = (await api.get<{ lines: { line: { lotId: string } }[] }>(`/api/warehouse/receipts/${rc.id}`)).lines[0].line.lotId;
    const { id: customerId } = await api.post<{ id: string }>('/api/master/customers', { code: `CC${t}`, name: `Carrier Customer ${t}`, siteId, status: 0 });
    const s = await api.post<{ id: string; number: string }>('/api/shipping/shipments', { kind: 0, customerId, supplierId: null, toSiteId: null, shipTo: 'Dock 4', customerPo: null, promisedOn: localDate(2), stagingLocationId: ship.id,
      carrierId: null, serviceLevel: null, notes: null, siteId, departmentId: null, lines: [{ partId, revision: null, quantity: 6, lotId }] });
    await api.post(`/api/shipping/shipments/${s.id}/pick-list`);
    const d = await api.get<Detail>(`/api/shipping/shipments/${s.id}`);
    for (const p of d.picks) await api.post(`/api/shipping/shipments/${s.id}/picks/${p.task.id}/confirm`, { locationCode: p.locationCode, itemCode: `QG:LOT:CL-${t}`, quantity: p.task.quantity });
    const cartons: string[] = [];
    for (const [q, kg] of [[4, 3.5], [2, 2]]) cartons.push((await api.post<{ number: string }>(`/api/shipping/shipments/${s.id}/containers`, { kind: 0, parentId: null, weight: kg, lengthCm: 40, widthCm: 30, heightCm: 20, items: [{ lineId: d.lines[0].line.id, lotId, serialId: null, quantity: q }] })).number);

    await page.goto(`/shipping/shipments/${s.id}`);
    await page.getByRole('tab', { name: 'Carrier' }).click();
    await page.getByLabel('Name / company').fill('Acme Receiving');
    await page.getByLabel('Street', { exact: true }).fill('12 Harbor Rd');
    await page.getByLabel('City').fill('Toledo');
    await page.getByLabel('State / region').fill('OH');
    await page.getByLabel('Postal code').fill('43604');
    await button(page, 'Save address').click();
    await expect(page.getByText('Address saved.')).toBeVisible();
    await choose(page, 'Carrier connection', new RegExp(`Sandbox ${t}`));
    await expect(page.getByText('Sandbox connection: rates and labels are test results')).toBeVisible();
    await button(page, 'Validate with carrier').click();
    await expect(page.getByText(/Address valid/)).toBeVisible();
    await expect(page.getByText(/^Validated /)).toBeVisible();
    await button(page, 'Get rates').click();
    const rates = page.getByRole('table', { name: 'Rate quotes' });
    await expect(rates.getByRole('row')).toHaveCount(4);
    await rates.getByRole('row').filter({ hasText: 'Fixture 2nd Day' }).getByRole('button', { name: 'Select' }).click();
    await expect(page.getByText('Fixture 2nd Day selected.')).toBeVisible();
    await button(page, 'Create labels').click();
    await expect(page.getByText('Carrier labels created.')).toBeVisible();
    const labels = page.getByRole('table', { name: 'Carrier labels' });
    await expect(labels.getByRole('row')).toHaveCount(3);
    for (const c of cartons) await expect(labels).toContainText(c);
    await expect(labels.getByText('Active')).toHaveCount(2);
    await expect(page.getByRole('table', { name: 'Carrier requests' })).toContainText('Create shipment');
    const tracking = (await api.get<{ shipment: { trackingNumber: string } }>(`/api/shipping/shipments/${s.id}`)).shipment.trackingNumber;
    expect(tracking).toMatch(/^FX/);

    // verify and ship (the label's tracking number is used), then tracking marks it delivered
    await api.post(`/api/shipping/shipments/${s.id}/verify`, { scans: cartons.map((c) => ({ code: `QG:CTN:${c}`, quantity: null })) });
    await api.post(`/api/shipping/shipments/${s.id}/ship`, { carrierId: carrier.id, trackingNumber: null, serviceLevel: null, grossWeight: 5.5 });
    await page.reload();
    await page.getByRole('tab', { name: 'Carrier' }).click();
    await button(page, 'Refresh tracking').click();
    await expect(page.getByText(/8 new tracking event\(s\); delivered\./)).toBeVisible();
    await expect(page.getByText('Delivered', { exact: true }).first()).toBeVisible();
    expect((await api.get<{ shipment: { status: number } }>(`/api/shipping/shipments/${s.id}`)).shipment.status).toBe(6);
    await shot(page, '13-carrier-connection');
  });
});
