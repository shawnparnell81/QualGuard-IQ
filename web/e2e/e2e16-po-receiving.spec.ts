import { test, expect, type Page } from '@playwright/test';
import { Api, localDate, signIn, state, users, shot } from './support';

/** Gap 12: PO-based receiving at the scan station on a phone; blind receiving is a separate permission. */
const button = (p: Page, name: string | RegExp) => p.getByRole('button', { name });
async function scan(page: Page, label: string | RegExp, code: string) {
  await page.getByLabel(label).fill(code);
  await page.getByLabel(label).press('Enter');
}

test.describe('E2E 16: PO receiving at the scan station', () => {
  test('a receiver scans the PO, picks a line, is stopped on the wrong part, receives partially, then a serialized line goes to inspection with a hold and labels', async ({ page }) => {
    const author = await Api.as(users.author);
    const approver = await Api.as(users.approver);
    const siteId = state().siteId;
    const t = Date.now().toString(36).toUpperCase();
    const part = async (pn: string) => (await author.post<{ id: string }>('/api/master/parts', { partNumber: pn, name: `Part ${pn}`, partType: 0, revision: 'A', unitOfMeasure: 'EA', siteId, status: 0 })).id;
    const p1 = await part(`HSG-${t}`); const p2 = await part(`ACT-${t}`);
    const { id: wh } = await author.post<{ id: string }>('/api/warehouse/locations', { code: `RW-${t}`, name: 'Receiving WH', kind: 0, purpose: 0, parentId: null, capacity: 1000, siteId, departmentId: null });
    await author.post('/api/warehouse/locations', { code: `RD-${t}`, name: 'Dock', kind: 2, purpose: 1, parentId: wh, capacity: 1000, siteId, departmentId: null });
    const { id: sup } = await author.post<{ id: string }>('/api/suppliers', { code: `RS${t}`, legalName: `Receiving Supplier ${t}`, category: 'Castings', criticality: 1, risk: 1, siteId });
    await author.post('/api/production/purchase-orders', { number: `PO-${t}`, supplierId: sup, orderedOn: localDate(-3), siteId,
      lines: [{ lineNumber: 1, partId: p1, itemNumber: `HSG-${t}`, revision: 'A', quantity: 10, dueOn: localDate() }, { lineNumber: 2, partId: p2, itemNumber: `ACT-${t}`, revision: 'A', quantity: 2, dueOn: localDate() }] });
    await approver.post('/api/warehouse/receiving/requirements', { partId: p2, supplierId: null, inspectionRequired: true, inspectionPlanId: null, aqlPlanId: null, aqlInspectionLevel: null, aqlLevel: null,
      holdOnReceipt: true, holdReason: 'New actuator supplier', serialsRequired: true, labelCopies: 1, notes: null, siteId, departmentId: null });

    await signIn(page, users.author);
    await page.setViewportSize({ width: 412, height: 915 });
    await page.goto('/warehouse/scan?flow=receive');
    await scan(page, 'Scan dock location', `QG:LOC:RD-${t}`);
    await scan(page, /Scan PO label/, `QG:PO:PO-${t}`);
    const lines = page.getByTestId('po-lines');
    await expect(lines).toContainText(`Line 1 · HSG-${t} rev A`);
    await expect(lines).toContainText('10 of 10 EA open');
    await expect(lines.getByRole('button').nth(1)).toContainText('Receiving hold');

    // Line 1: wrong part is refused at the scan, the right part with a partial quantity warns and posts.
    await lines.getByRole('button').first().click();
    await scan(page, 'Scan the item (part label)', `QG:PART:ACT-${t}`);
    await expect(page.getByText(`Wrong part: scanned ACT-${t}, PO line 1 is HSG-${t}.`)).toBeVisible();
    await scan(page, 'Scan the item (part label)', `QG:PART:HSG-${t}`);
    await page.getByLabel('Quantity received').fill('4');
    await button(page, 'Check').click();
    await expect(page.getByTestId('receive-check')).toContainText('partial receipt; 6 stay open on PO line 1');
    await button(page, 'Receive with warnings').click();
    await expect(page.getByText(/Receipt RCV-.* posted\./)).toBeVisible();
    await expect(page.getByTestId('receive-result')).toContainText('Accepted to stock');
    await expect(page.getByTestId('po-lines')).toContainText('6 of 10 EA open');
    // Over the open quantity (no tolerance configured) is refused by the server check.
    await page.getByTestId('po-lines').getByRole('button').first().click();
    await scan(page, 'Scan the item (part label)', `HSG-${t}`);
    await page.getByLabel('Quantity received').fill('7');
    await button(page, 'Check').click();
    await expect(page.getByTestId('receive-check')).toContainText('would exceed PO line 1 (6 open)');
    await expect(button(page, /^Receive( with warnings)?$/)).toBeDisabled();
    await button(page, 'Change line').click();

    // Line 2: serial per unit, inspection and receiving hold from the part's requirement; duplicate serial scan refused.
    await page.getByTestId('po-lines').getByRole('button').nth(1).click();
    await scan(page, 'Scan the item (part label)', `QG:PART:ACT-${t}`);
    await scan(page, 'Scan serial', `SN-${t}-1`);
    await scan(page, 'Scan serial', `SN-${t}-1`);
    await expect(page.getByText(`Serial SN-${t}-1 is already scanned.`)).toBeVisible();
    await scan(page, 'Scan serial', `QG:SN:ACT-${t}:SN-${t}-2`);
    await expect(page.getByTestId('scanned-serials')).toContainText(`SN-${t}-2`);
    await expect(page.getByLabel('Quantity received')).toHaveValue('2');
    await button(page, /^Receive$/).click();
    const result = page.getByTestId('receive-result');
    await expect(result).toContainText('Awaiting inspection');
    await expect(result).toContainText(/Hold HOLD-/);
    await shot(page, '183-gap12-po-receiving-scan-station');
    await result.getByRole('link', { name: /Labels \(3\)/ }).click();
    await expect(page.getByTestId('receipt-label')).toHaveCount(3);
    await expect(page.getByTestId('receipt-label').first()).toContainText('Lot · line 1');

    // The scan station receipt is recorded with its channel; the hold blocks the lot until released.
    await page.getByRole('link', { name: /RCV-/ }).first().click();
    await expect(page.getByText('Scan station')).toBeVisible();
    await expect(page.getByText(/Hold HOLD-/)).toBeVisible();
    // Nothing on the phone layout is wider than the screen.
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(412);
    await shot(page, '184-gap12-po-receiving');
  });

  test('blind receiving is offered only with its own permission and the API refuses it otherwise', async ({ page }) => {
    const approver = await Api.as(users.approver);
    await signIn(page, users.approver);
    await page.goto('/warehouse/scan');
    await expect(button(page, /Receive PO/)).toBeVisible();
    await expect(button(page, /Blind receipt/)).toHaveCount(0);
    await page.goto('/warehouse/scan?flow=blind');
    await expect(page.getByText('Blind receipts need the blind receiving permission.')).toBeVisible();
    const siteId = state().siteId;
    const status = await approver.status('POST', '/api/warehouse/receipts', { kind: 3, supplierId: null, purchaseOrderId: null, asnNumber: null, packingSlip: null, carrierId: null, trackingNumber: null, receivedOn: localDate(),
      receivingLocationId: '00000000-0000-0000-0000-000000000001', notes: null, supplierOverrideReason: null, siteId, departmentId: null, lines: [{ partId: '00000000-0000-0000-0000-000000000001', quantityReceived: 1 }] });
    expect(status).toBe(403);
  });
});
