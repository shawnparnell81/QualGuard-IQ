import { test, expect, type Page } from '@playwright/test';
import { Api, choose, dialog, signIn, state, users } from './support';

/** Gap 14: printers, routing, automatic labels after a transaction, failures that never block it, retry, reprint with a reason and PDF fallback. */
const button = (p: Page, name: string | RegExp) => p.getByRole('button', { name });
interface Outcome { jobId: string; status: number; printer?: string; error?: string }

test.describe('E2E 19: label printing', () => {
  test('a supervisor sets up printers and routes, a bin label prints, a failed hold label is retried and reprinted with a reason, and labels without a printer download as PDF', async ({ page, browser }) => {
    const api = await Api.as(users.author);
    const { siteId } = state();
    const t = Date.now().toString(36).toUpperCase();
    const site = (await api.get<{ id: string; code: string }[]>('/api/organization/sites')).find((s) => s.id === siteId)!;
    const loc = (code: string, kind: number, purpose: number, parentId: string | null) => api.post<{ id: string }>('/api/warehouse/locations', { code, name: code, kind, purpose, parentId, capacity: 500, siteId, departmentId: null });
    const wh = await loc(`LWH-${t}`, 0, 0, null); const bin = await loc(`LA1-${t}`, 6, 0, wh.id); const yard = await loc(`LYD-${t}`, 0, 0, null);
    const { id: partId } = await api.post<{ id: string }>('/api/master/parts', { partNumber: `LP-${t}`, name: 'Label bracket', partType: 0, revision: 'A', unitOfMeasure: 'EA', siteId, status: 0 });

    // An operator (inventory.edit) sees the printers but cannot configure them.
    await signIn(page, users.author);
    await page.goto('/warehouse/labels');
    await choose(page, 'Site', new RegExp(`^${site.code} `));
    await page.getByRole('tab', { name: 'Printers' }).click();
    await expect(page.getByText(/No printers at this site|Printers/).first()).toBeVisible();
    await expect(button(page, 'Add printer')).toHaveCount(0);

    // The supervisor (inventory.approve) adds a working printer and one that answers with an error.
    const sup = await (await browser.newContext()).newPage();
    const operator = page;
    page = sup;
    await signIn(page, users.approver);
    await page.goto('/warehouse/labels');
    await choose(page, 'Site', new RegExp(`^${site.code} `));
    await page.getByRole('tab', { name: 'Printers' }).click();
    for (const [code, address] of [[`ZD-${t}`, 'ok'], [`BAD-${t}`, 'error']]) {
      await button(page, 'Add printer').click();
      await dialog(page).getByLabel('Code').fill(code);
      await dialog(page).getByLabel('Name', { exact: true }).fill(`Printer ${code}`);
      await choose(page, 'Connection', 'Virtual (test)');
      await dialog(page).getByLabel('Address (host:port or https URL)').fill(address);
      await dialog(page).getByRole('button', { name: 'Save' }).click();
      await expect(dialog(page)).toBeHidden();
    }
    const zd = page.getByTestId('printer-row').filter({ hasText: `ZD-${t}` });
    await zd.getByRole('button', { name: 'Print test label' }).click();
    await expect(page.getByTestId('labels-message')).toContainText(`Test label - ZD-${t}: printed on ZD-${t}`);
    await expect(zd.getByTestId('printer-status')).toHaveText('Online');
    const bad = page.getByTestId('printer-row').filter({ hasText: `BAD-${t}` });
    await bad.getByRole('button', { name: 'Print test label' }).click();
    await expect(page.getByTestId('labels-message')).toContainText('not printed on');
    await expect(bad.getByTestId('printer-status')).toHaveText('Error');

    // Routing: bin labels to ZD at the warehouse; hold labels to the failing printer (site default).
    await page.getByRole('tab', { name: 'Routing' }).click();
    const addRoute = async (label: string, printer: string, location?: string) => {
      await button(page, 'Add route').click();
      await choose(page, 'Label', new RegExp(`^${label}$`));
      await choose(page, 'Printer', new RegExp(printer));
      if (location) await choose(page, 'Warehouse, zone or bin (blank = whole site)', new RegExp(location));
      await dialog(page).getByRole('button', { name: 'Save' }).click();
      await expect(dialog(page)).toBeHidden();
    };
    await addRoute('Bin', `ZD-${t}`, `LWH-${t}$`);
    await addRoute('Hold', `BAD-${t}`);
    await expect(page.getByTestId('route-row').filter({ hasText: `ZD-${t}` })).toContainText(`Location LWH-${t}`);
    await choose(page, 'Label', /^Bin$/);
    await choose(page, 'Location', new RegExp(`LA1-${t}`));
    await expect(page.getByTestId('route-explanation')).toContainText(`matched on warehouse/zone: ZD-${t}`);

    // Back to the operator: a bin label printed from the location page goes to the routed printer.
    await sup.context().close();
    page = operator;
    await page.goto(`/warehouse/locations/${bin.id}`);
    await page.getByRole('tab', { name: 'Label' }).click();
    await button(page, 'Print bin label').click();
    await dialog(page).getByRole('button', { name: 'Print' }).click();
    await expect(page.getByTestId('label-message')).toContainText(`printed on ZD-${t}`);
    await expect(page.getByTestId('label-job').first().getByTestId('label-job-status')).toHaveText('Printed');

    // A hold is created; its label fails on the printer but the hold is saved, and the failure is shown on the record.
    const hold = await api.post<{ id: string; number: string }>('/api/warehouse/holds', { kind: 0, reason: 'Label test', partId, lotId: null, serialId: null, locationId: null, ncrId: null, customerId: null, supplierId: null, siteId });
    await page.goto(`/warehouse/holds/${hold.id}`);
    await expect(page.getByText(hold.number).first()).toBeVisible();
    await page.getByRole('tab', { name: 'Labels' }).click();
    const job = page.getByTestId('label-job').first();
    await expect(job.getByTestId('label-job-status')).toHaveText('Failed');
    await expect(job).toContainText('Next try');

    // The supervisor fixes the printer; the operator's retry prints it. A reprint needs a reason and is kept with the hold.
    const printers = await api.get<{ id: string; code: string; siteId: string }[]>(`/api/labels/printers?siteId=${siteId}`);
    const badP = printers.find((p) => p.code === `BAD-${t}`)!;
    const supApi = await Api.as(users.approver);
    await supApi.put(`/api/labels/printers/${badP.id}`, { code: `BAD-${t}`, name: `Printer BAD-${t}`, driver: 0, address: 'ok', language: 0, dpi: 203, widthMm: 102, heightMm: 152, enabled: true, siteId, secret: null, clearSecret: false });
    await job.getByRole('button', { name: 'Retry' }).click();
    await expect(page.getByTestId('label-message')).toContainText(`printed on BAD-${t}`);
    await expect(page.getByTestId('label-job').first().getByTestId('label-job-status')).toHaveText('Printed');
    await page.getByTestId('label-job').first().getByRole('button', { name: 'Reprint' }).click();
    await dialog(page).getByRole('button', { name: 'Reprint' }).click();
    await expect(dialog(page)).toContainText('Give the reason for the reprint.');
    await dialog(page).getByLabel('Reason for the reprint').fill('Label smudged at the cage');
    await dialog(page).getByRole('button', { name: 'Reprint' }).click();
    await expect(dialog(page)).toBeHidden();
    await expect(page.getByTestId('label-job')).toHaveCount(2);
    await expect(page.getByTestId('label-job').first()).toContainText('Reprint: Label smudged at the cage');

    // A location outside the routed warehouse has no bin route: the label is prepared as a PDF to download.
    const r = await api.post<Outcome[]>('/api/labels/print', { labelType: 2, refType: 'WarehouseLocation', refId: yard.id, printerId: null, copies: null });
    expect(r[0].status).toBe(5);
    const pdf = await page.request.get(`/api/labels/jobs/${r[0].jobId}/pdf`);
    expect(pdf.status()).toBe(200);
    expect(pdf.headers()['content-type']).toContain('application/pdf');

    // The print queue shows every job with its status.
    await page.goto('/warehouse/labels');
    await choose(page, 'Site', new RegExp(`^${site.code} `));
    await expect(page.getByTestId('queue-job').filter({ hasText: hold.number }).first()).toBeVisible();
    await supApi.dispose();
    await api.dispose();
  });
});
