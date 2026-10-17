import { test, expect, type Page } from '@playwright/test';
import { Api, choose, dialog, localDate, signIn, state, users, shot } from './support';

/** Gap 15: a packing list template is designed, previewed and submitted, approved by a second person, and the shipment's issued packing list records the revision and reproduces exactly. */
const button = (p: Page, name: string | RegExp) => p.getByRole('button', { name });
interface Detail { lines: { line: { id: string } }[]; picks: { task: { id: string; quantity: number }; locationCode: string }[] }

test.describe('E2E 21: shipping document templates', () => {
  test('an engineer designs a customer packing list, an approver approves it, and the issued packing list is tied to that revision; viewers only read', async ({ page, browser }) => {
    const api = await Api.as(users.author);
    const { siteId } = state();
    const t = Date.now().toString(36).toUpperCase();
    const { id: customerId } = await api.post<{ id: string }>('/api/master/customers', { code: `DC${t}`, name: `Docs Customer ${t}`, siteId, status: 0 });

    // a packed shipment for that customer
    const loc = (code: string, kind: number, purpose: number, parentId: string | null) => api.post<{ id: string }>('/api/warehouse/locations', { code, name: code, kind, purpose, parentId, capacity: 500, siteId, departmentId: null });
    const wh = await loc(`DWH-${t}`, 0, 0, null); const dock = await loc(`DDK-${t}`, 2, 1, wh.id); const bin = await loc(`DA1-${t}`, 6, 0, wh.id); const ship = await loc(`DSH-${t}`, 2, 6, wh.id);
    const { id: partId } = await api.post<{ id: string }>('/api/master/parts', { partNumber: `DP-${t}`, name: 'Valve body', partType: 0, revision: 'A', unitOfMeasure: 'EA', siteId, status: 0 });
    const rc = await api.post<{ id: string }>('/api/warehouse/receipts', { kind: 3, supplierId: null, purchaseOrderId: null, asnNumber: null, packingSlip: null, carrierId: null, trackingNumber: null, receivedOn: localDate(), receivingLocationId: dock.id, notes: null,
      supplierOverrideReason: null, siteId, departmentId: null, lines: [{ partId, revision: 'A', purchaseOrderLineId: null, quantityExpected: null, quantityReceived: 5, supplierLotNumber: null, lotNumber: `DL-${t}`,
        serialNumbers: null, inspectionRequired: false, inspectionPlanId: null, aqlPlanId: null, aqlInspectionLevel: null, aqlLevel: null, putAwayLocationId: bin.id, clientRequestId: null }] });
    const lotId = (await api.get<{ lines: { line: { lotId: string } }[] }>(`/api/warehouse/receipts/${rc.id}`)).lines[0].line.lotId;
    const s = await api.post<{ id: string; number: string }>('/api/shipping/shipments', { kind: 0, customerId, supplierId: null, toSiteId: null, shipTo: 'Dock 2', customerPo: `PO-${t}`, promisedOn: localDate(2), stagingLocationId: ship.id,
      carrierId: null, serviceLevel: null, notes: null, siteId, departmentId: null, lines: [{ partId, revision: null, quantity: 3, lotId }] });
    await api.post(`/api/shipping/shipments/${s.id}/pick-list`);
    const d = await api.get<Detail>(`/api/shipping/shipments/${s.id}`);
    for (const p of d.picks) await api.post(`/api/shipping/shipments/${s.id}/picks/${p.task.id}/confirm`, { locationCode: p.locationCode, itemCode: `QG:LOT:DL-${t}`, quantity: p.task.quantity });
    await api.post(`/api/shipping/shipments/${s.id}/containers`, { kind: 0, parentId: null, weight: 2.5, lengthCm: 30, widthCm: 20, heightCm: 15, items: [{ lineId: d.lines[0].line.id, lotId, serialId: null, quantity: 3 }] });

    // design the template
    await signIn(page, users.author);
    await page.goto('/shipping/document-templates');
    await button(page, 'New template').click();
    await dialog(page).getByLabel('Code').fill(`PL${t}`);
    await dialog(page).getByLabel('Name').fill(`Docs Customer packing list ${t}`);
    await dialog(page).getByRole('combobox', { name: 'Site' }).click();
    await page.getByRole('option').first().click();
    await dialog(page).getByRole('button', { name: 'Create' }).click();
    await expect(page.getByRole('heading', { name: new RegExp(`PL${t}`) })).toBeVisible();
    await expect(page.getByText('Revision 1 · Draft')).toBeVisible();
    await choose(page, 'Customer', new RegExp(`DC${t}`));
    await page.getByLabel('Change summary').fill('Customer layout with receiving note');
    await page.getByLabel('Header title').fill('Docs packing list {shipmentNumber}');
    await choose(page, 'Add section', 'Text');
    const last = page.getByTestId(/^section-/).last();
    await last.getByLabel('Text').fill('Receiving: quote PO {customerPo}');
    await button(page, 'Save draft').click();
    await expect(page.getByText('Draft saved.')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Preview (sample data)' })).toHaveAttribute('href', /\/preview$/);
    const preview = await page.request.get((await page.getByRole('link', { name: 'Preview (sample data)' }).getAttribute('href'))!);
    expect(preview.headers()['content-type']).toContain('application/pdf');
    expect((await preview.body()).toString('latin1')).toContain('Docs packing list SHP-SAMPLE');
    await button(page, 'Submit for approval').click();
    await expect(page.getByText('Submitted for approval.')).toBeVisible();
    await expect(page.getByText('Revision 1 · Pending approval')).toBeVisible();
    await expect(button(page, 'Approve')).toHaveCount(0); // the preparer has no approval right here

    // a second person approves
    const ap = await (await browser.newContext()).newPage();
    await signIn(ap, users.approver);
    await ap.goto(page.url());
    await button(ap, 'Approve').click();
    await dialog(ap).getByLabel('Approval notes').fill('Matches the customer routing guide.');
    await dialog(ap).getByRole('button', { name: 'Approve' }).click();
    await expect(ap.getByText('Revision approved.')).toBeVisible();
    await expect(ap.getByText('Revision 1 · Effective')).toBeVisible();
    await expect(ap.getByText(/Approved content SHA-256 [0-9a-f]{64}/)).toBeVisible();

    // the shipment uses it, and the issued packing list reproduces
    await page.goto(`/shipping/shipments/${s.id}`);
    await page.getByRole('tab', { name: 'Ship & documents' }).click();
    await expect(page.getByText(new RegExp(`Packing list: PL${t} rev 1`))).toBeVisible();
    await page.getByLabel('Freight terms').fill('Prepaid');
    await button(page, 'Save terms').click();
    await expect(page.getByText('Shipping terms saved.')).toBeVisible();
    const popup = page.waitForEvent('popup');
    await page.getByRole('link', { name: 'Packing list (PDF)' }).click();
    await (await popup).close();
    const issued = page.getByRole('table', { name: 'Issued documents' });
    await expect(issued).toContainText(`PL${t} rev 1`);
    await issued.getByRole('button', { name: 'Reproduce' }).first().click();
    await expect(issued.getByText('Reproduced: identical')).toBeVisible();
    const docs = await api.get<{ templateCode: string; templateRevision: number }[]>(`/api/shipping/shipments/${s.id}/documents`);
    expect(docs[0]).toMatchObject({ templateCode: `PL${t}`, templateRevision: 1 });

    // viewers read templates but cannot create or edit them
    const viewer = await (await browser.newContext()).newPage();
    await signIn(viewer, users.viewer);
    await viewer.goto('/shipping/document-templates');
    await expect(viewer.getByRole('table', { name: 'Document templates' })).toContainText(`PL${t}`);
    await expect(button(viewer, 'New template')).toHaveCount(0);
    await viewer.getByRole('link', { name: `PL${t}` }).click();
    await expect(button(viewer, 'New revision')).toHaveCount(0);
    await expect(button(viewer, 'Edit details')).toHaveCount(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(1440); // nothing wider than the desktop
    await shot(page, '15-shipping-document-template');
  });
});
