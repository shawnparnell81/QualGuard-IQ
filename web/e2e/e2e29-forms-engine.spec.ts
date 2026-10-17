import { test, expect } from '@playwright/test';
import { Api, shot, signIn, state, unique, users } from './support';

/** Forms build B1: sectioned template (schema version 2) with tables, totals, running totals, repeated and conditional sections. */
export const inspectionSchema = {
  schemaVersion: 2,
  page: { size: 'Letter', orientation: 'portrait', marginMm: 15, printStyle: 'standardized', fontSize: 9 },
  sections: [
    { key: 'ident', title: 'Record identification', columns: 3 },
    { key: 'results', title: 'Dimensional results', columns: 2, orientation: 'landscape', instructions: 'Measure every ballooned characteristic and record the value as read.' },
    { key: 'failure', title: 'Failure details', columns: 1, visibleWhen: { field: 'outcome', operator: 'equals', value: 'Fail' } },
    { key: 'ops', title: 'Operations performed', columns: 2, repeatable: true, minRepeat: 1, maxRepeat: 5, repeatLabel: 'Operation' },
  ],
  fields: [
    { key: 'lot', label: 'Lot number', type: 'text', required: true, section: 'ident' },
    { key: 'qty', label: 'Quantity inspected', type: 'integer', min: 1, section: 'ident' },
    { key: 'unit_cost', label: 'Unit cost', type: 'currency', decimals: 2, section: 'ident' },
    { key: 'outcome', label: 'Outcome', type: 'radio', options: ['Pass', 'Fail'], section: 'ident' },
    { key: 'lot_value', label: 'Lot value', type: 'calc', formula: 'qty * unit_cost', decimals: 2, section: 'ident' },
    { key: 'reason', label: 'Describe the failure', type: 'longtext', required: true, section: 'failure' },
    { key: 'op_no', label: 'Operation number', type: 'integer', required: true, section: 'ops' },
    { key: 'op_minutes', label: 'Minutes', type: 'number', section: 'ops' },
    { key: 'fails', label: 'Characteristics out of tolerance', type: 'calc', formula: 'COUNTIF(dims.result, "FAIL")', section: 'results' },
  ],
  tables: [
    {
      key: 'dims', title: 'Characteristics', section: 'results', minRows: 1, maxRows: 50,
      headerGroups: [{ label: 'Characteristic', span: 1 }, { label: 'Specification', span: 2 }, { label: 'Result', span: 3 }],
      columns: [
        { key: 'balloon', label: 'Balloon', type: 'integer', required: true },
        { key: 'lsl', label: 'Lower limit', type: 'number' },
        { key: 'usl', label: 'Upper limit', type: 'number' },
        { key: 'measured', label: 'Measured', type: 'measurement', nominal: 0 },
        { key: 'dev', label: 'Over limit', type: 'calc', formula: 'MAX(0, measured - usl, lsl - measured)', decimals: 3 },
        { key: 'result', label: 'Result', type: 'calc', formula: 'WITHIN(measured, lsl, usl)', resultLabels: ['PASS', 'FAIL'] },
      ],
      totals: [{ column: 'dev', function: 'max' }, { column: 'balloon', function: 'count' }],
      runningTotals: ['dev'],
    },
    { key: 'visual', title: 'Visual checklist', section: 'results', fixedRows: ['Burrs', 'Scratches', 'Part marking'], minRows: 0, maxRows: 500, columns: [{ key: 'ok', label: 'Result', type: 'passfail', required: true }, { key: 'note', label: 'Note', type: 'text' }] },
  ],
  signatures: [{ key: 'inspector', label: 'Inspected by', role: 'performed', meaning: 'I performed this inspection as recorded', section: 'results', order: 1, required: true }],
};

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
