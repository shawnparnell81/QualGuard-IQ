import { test, expect } from '@playwright/test';
import { Api, shot, signIn, state, unique, users } from './support';
import { inspectionSchema } from './forms-fixtures';

/** Forms build B1: sectioned template (schema version 2) with tables, totals, running totals, repeated and conditional sections. */

test.describe('Forms B1: sectioned form engine', () => {
  test('a sectioned template with tables is filled in; totals, results and conditional sections work', async ({ page }) => {
    const author = await Api.as(users.author);
    const approver = await Api.as(users.approver);
    const title = unique('Final dimensional inspection');
    const id = await author.post<string>('/api/documents', { kind: 2, title, documentType: 'Inspection Record', siteId: state().siteId, departmentId: null, reviewIntervalMonths: 24, initialRevision: 'A', changeSummary: 'Initial release' });
    const doc = await author.get(`/api/documents/${id}`);
    const rev = doc.revisions[0];
    await author.put(`/api/documents/revisions/${rev.id}`, { changeSummary: 'Initial release', effectiveDate: null, trainingRequired: false, rowVersion: rev.rowVersion, fieldSchema: JSON.stringify(inspectionSchema) });
    await author.post(`/api/documents/revisions/${rev.id}/submit`);
    await approver.sign(doc.number, users.approver.password);
    const rec = await author.post<{ id: string }>('/api/forms/records', { templateId: id, siteId: state().siteId, departmentId: null, title: null });

    await signIn(page, users.author);
    await page.goto(`/forms/records/${rec.id}`);
    await expect(page.getByRole('heading', { name: 'Record identification' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Failure details' })).toHaveCount(0);
    await page.getByLabel('Lot number').fill('LOT-4471');
    await page.getByLabel('Quantity inspected').fill('40');
    await page.getByLabel('Unit cost').fill('12.5');
    await page.getByRole('radio', { name: 'Fail' }).check();
    await expect(page.getByRole('heading', { name: 'Failure details' })).toBeVisible();
    await page.getByLabel('Describe the failure').fill('Balloon 2 bore oversize.');

    const dims = page.getByRole('table', { name: 'Characteristics' });
    for (const [b, lo, hi, m] of [['1', '9.95', '10.05', '10.01'], ['2', '4.9', '5.1', '5.16']]) {
      await page.getByRole('button', { name: 'Add row' }).first().click();
      const row = dims.locator('tbody tr').nth(Number(b) - 1);
      await row.getByLabel('Balloon').fill(b);
      await row.getByLabel('Lower limit').fill(lo);
      await row.getByLabel('Upper limit').fill(hi);
      await row.getByLabel('Measured').fill(m);
    }
    const visual = page.getByRole('table', { name: 'Visual checklist' });
    for (let i = 0; i < 3; i++) await visual.locator('tbody tr').nth(i).getByRole('button', { name: 'Pass' }).click();
    await page.getByRole('button', { name: 'Add operation' }).click();
    await page.getByLabel('Operation number').fill('10');

    await page.getByRole('button', { name: 'Save draft' }).click();
    await expect(page.getByText('Draft saved. Calculated fields were updated.')).toBeVisible();
    await expect(page.getByLabel('Lot value')).toHaveValue('500.00');
    await expect(dims.getByText('FAIL')).toBeVisible();
    await expect(dims.getByText('PASS')).toBeVisible();
    await expect(page.getByLabel('Characteristics out of tolerance')).toHaveValue('1');
    await expect(dims.getByText('0.060').first()).toBeVisible(); // max over limit total + running total
    await expect(page.getByText('I performed this inspection as recorded')).toBeVisible();
    await shot(page, 'forms-import/b1-sectioned-form-filled');

    // the server re-checks everything: a fixed checklist row without a result blocks submission
    const saved = await author.get(`/api/forms/records/${rec.id}`);
    expect(JSON.parse(saved.values.dims).totals.count_balloon).toBe('2');
    expect(saved.definition.schemaVersion).toBe(2);
    const bad = { ...saved.values, visual: JSON.stringify([{ ok: 'pass' }, {}, { ok: 'pass' }]) };
    expect(await author.status('PUT', `/api/forms/records/${rec.id}`, { values: bad, rowVersion: saved.rowVersion })).toBe(200);
    expect(await author.status('POST', `/api/forms/records/${rec.id}/submit`)).toBe(400);
    await author.dispose(); await approver.dispose();
  });
});
