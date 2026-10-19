import { test, expect, type Page } from '@playwright/test';
import { Api, dialog, signIn, state, unique, users } from './support';

const button = (p: Page, name: string | RegExp) => p.getByRole('button', { name });
const tag = () => Date.now().toString(36).toUpperCase();

async function part(api: Api) {
  const siteId = state().siteId;
  const t = tag();
  const { id: productId } = await api.post<{ id: string }>('/api/master/products', { number: `PR-${t}`, name: unique('Pump'), category: 'Pumps', siteId, status: 0 });
  const { id } = await api.post<{ id: string }>('/api/master/parts', { partNumber: `PX-${t}`, name: 'Impeller', partType: 0, revision: 'C', unitOfMeasure: 'EA', productId, siteId, status: 0 });
  return { id, partNumber: `PX-${t}`, t };
}

test.describe('E2E 8: shop floor, traceability, returns and integrations', () => {
  test('an operator records production on the touch form and the live monitor shows the yield', async ({ page }) => {
    const api = await Api.as(users.author);
    const p = await part(api);
    const wo = await api.post<{ id: string; number: string }>('/api/production/work-orders', { number: `WO-${p.t}`, kind: 0, partId: p.id, revision: 'C', quantity: 10, startOn: null, dueOn: null, routingRef: null,
      operations: [{ sequence: 10, name: 'Machine', workCenter: 'CNC-1' }], siteId: state().siteId, departmentId: null });
    await signIn(page, users.author);
    await page.goto(`/production/entry?wo=${wo.id}`);
    await page.getByLabel('Operation').click();
    await page.getByRole('option', { name: /10 · Machine/ }).click();
    await page.getByLabel('Good').fill('9');
    await page.getByLabel('Scrapped').fill('1');
    await button(page, 'Record').last().click();
    await expect(page.getByText('Recorded.')).toBeVisible();
    await page.goto(`/production/work-orders/${wo.id}`);
    await expect(page.getByRole('cell', { name: 'CNC-1' })).toBeVisible();
    await expect(page.getByRole('cell', { name: '90.0%' })).toBeVisible();
    await page.goto('/production');
    await expect(page.getByText('First-pass yield (24 h)')).toBeVisible();
  });

  test('a lot consumed into a serial shows in genealogy, trace search and the scanner', async ({ page }) => {
    const api = await Api.as(users.author);
    const p = await part(api);
    const siteId = state().siteId;
    const lot = await api.post<{ id: string; number: string }>('/api/trace/lots', { number: `LOT-${p.t}`, kind: 1, partId: p.id, revision: 'C', supplierId: null, supplierLotNumber: null, customerId: null, customerLotNumber: null,
      quantity: 50, unitOfMeasure: 'EA', manufacturedOn: null, expiresOn: null, workOrderId: null, purchaseOrder: null, siteId, departmentId: null });
    const sn = await api.post<{ id: string; serialNumber: string }>('/api/trace/serials', { serialNumber: `SN-${p.t}`, partId: p.id, revision: 'C', productId: null, lotId: null, manufacturedOn: null, workOrderId: null, warrantyMonths: 12, siteId, departmentId: null });
    await api.post('/api/trace/events', { kind: 9, fromLotId: lot.id, fromSerialId: null, toLotId: null, toSerialId: sn.id, quantity: 1, workOrderId: null, operationSequence: null, supplierId: null, customerId: null, refType: null, refId: null, refNumber: null, notes: null, occurredAt: null });

    await signIn(page, users.viewer);
    await page.goto(`/trace/lots/${lot.id}`);
    await expect(page.getByText('Outputs +1')).toBeVisible();
    await expect(page.getByText(`SN-${p.t}`)).toBeVisible();
    await expect(button(page, 'Record movement')).toHaveCount(0); // view only

    await page.goto(`/trace?q=LOT-${p.t}`);
    await expect(page.getByText(`Matches (`)).toBeVisible();
    await page.goto('/trace/scan');
    await page.getByLabel('Scanner input').fill(`QG:LOT:LOT-${p.t}`);
    await page.keyboard.press('Enter');
    await expect(page.getByText(new RegExp(`^Lot LOT-${p.t}`))).toBeVisible();
    await button(page, 'Open').click();
    await expect(page).toHaveURL(new RegExp(`/trace/lots/${lot.id}`));
  });

  test('a customer return is received and inspected on its workspace', async ({ page }) => {
    const api = await Api.as(users.author);
    const p = await part(api);
    const siteId = state().siteId;
    const { id: customerId } = await api.post<{ id: string }>('/api/master/customers', { code: `C-${p.t}`, name: unique('Acme'), contactName: null, email: null, phone: null, address: null, siteId, departmentId: null, status: 0 });
    const ret = await api.post<{ id: string; number: string }>('/api/returns', { customerId, productId: null, partId: p.id, serialNumber: null, lotNumber: 'L-1', quantity: 2, returnReason: 'Noise at startup', customerReference: null, complaintId: null, siteId, departmentId: null, ownerId: null });
    await signIn(page, users.author);
    await page.goto(`/field/returns/${ret.id}`);
    await button(page, 'Receive').click();
    await dialog(page).getByLabel('Condition on arrival').fill('Packaged');
    await dialog(page).getByRole('button', { name: 'Receive' }).click();
    await expect(button(page, 'Record inspection')).toBeVisible();
    await button(page, 'Record inspection').click();
    await dialog(page).getByLabel('Inspection notes').fill('Bearing noise confirmed on bench.');
    await dialog(page).getByRole('button', { name: 'Save inspection' }).click();
    await expect(button(page, 'Disposition')).toBeVisible();
  });

  test('a connection starts off, hides its secret and lists the unverified vendor details', async ({ page }) => {
    const api = await Api.as(users.author);
    const c = await api.post<{ id: string }>('/api/integrations', { name: unique('Plex test'), system: 7, edition: 'Connect', baseUrl: 'https://connect.plex.example', account: 'tenant-1', company: null, clientId: null, username: null, options: {},
      maxConcurrency: null, requestsPerMinute: null, inventorySystemOfRecord: false, adminNotes: null, siteId: state().siteId, departmentId: null });
    await signIn(page, users.author);
    await page.goto(`/integrations/${c.id}`);
    await expect(page.getByText('Disabled', { exact: true })).toBeVisible();
    await button(page, 'Set secret').click();
    await dialog(page).getByLabel(/^API key/).fill('plex-api-key-value');
    await dialog(page).getByRole('button', { name: 'Save' }).click();
    await expect(page.getByText(/^Set .*never shown/)).toBeVisible();
    await expect(page.getByText('plex-api-key-value')).toHaveCount(0);
    await page.getByRole('tab', { name: 'Unverified' }).click();
    await expect(page.getByText('Customer Id header', { exact: true })).toBeVisible();

    const viewer = await page.context().browser()!.newPage();
    await signIn(viewer, users.viewer);
    await viewer.goto(`/integrations/${c.id}`);
    await expect(viewer.getByRole('button', { name: /secret/ })).toHaveCount(0);
  });
});
