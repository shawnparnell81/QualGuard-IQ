import { test, expect, type Page } from '@playwright/test';
import { Api, shot, signIn, state, unique, users } from './support';

/** Forms build B3: sectioned form designer (structure, properties, live preview, check and print preview) and signing inside a record. */

async function pick(page: Page, scope: ReturnType<Page['getByLabel']>, label: string, option: string) {
  await scope.getByLabel(label, { exact: true }).click();
  await page.getByRole('option', { name: option, exact: true }).click();
}

test.describe('Forms B3: designer and filler', () => {
  test('a designer builds sections, fields, a table and a signature block, checks the formula and previews the print', async ({ page }) => {
    const author = await Api.as(users.author);
    const title = unique('Designed bore check');
    const id = await author.post<string>('/api/documents', { kind: 2, title, documentType: 'Inspection Record', siteId: state().siteId, departmentId: null, reviewIntervalMonths: 24, initialRevision: 'A', changeSummary: 'Initial release' });

    await signIn(page, users.author);
    await page.goto(`/forms/templates/${id}`);
    await page.getByRole('tab', { name: /revisions/i }).click();
    await page.getByRole('button', { name: 'Use the sectioned designer' }).click();
    const props = page.getByLabel('Properties');

    await page.getByRole('button', { name: 'Add section' }).click();
    await props.getByLabel('Title', { exact: true }).fill('Bore measurements');
    await page.getByRole('button', { name: 'Add field' }).click();
    await props.getByLabel('Label', { exact: true }).fill('Bore diameter');
    await props.getByLabel('Key', { exact: true }).fill('bore');
    await pick(page, props, 'Type', 'Measurement (nominal and limits)');
    await props.getByLabel('Lower limit').fill('9.95');
    await props.getByLabel('Upper limit').fill('10.05');
    await page.getByRole('button', { name: 'Add field' }).click();
    await props.getByLabel('Label', { exact: true }).fill('Deviation from nominal');
    await props.getByLabel('Key', { exact: true }).fill('dev');
    await pick(page, props, 'Type', 'Calculated');
    await props.getByLabel('Formula').fill('ROUND(ABS(bore - 10), 3)');
    await page.getByRole('button', { name: 'Add table' }).click();
    await props.getByLabel('Title', { exact: true }).fill('Visual checks');
    await props.getByLabel(/^Fixed rows/).fill('Burrs\nScratches');
    await page.getByRole('button', { name: 'Add signature block' }).click();
    await props.getByLabel('Label', { exact: true }).fill('Inspected by');
    await pick(page, props, 'Role', 'Performed');

    const preview = page.getByLabel('Live preview');
    await expect(preview.getByRole('heading', { name: 'Bore measurements' })).toBeVisible();
    await preview.getByLabel('Bore diameter').fill('10.031');
    await page.getByRole('button', { name: 'Check and calculate' }).click();
    await expect(page.getByText(/The form is valid: 1 sections, 2 fields, 1 tables, 1 signature blocks/)).toBeVisible();
    await expect(preview.getByLabel('Deviation from nominal')).toHaveValue('0.031');
    await shot(page, 'forms-import/b3-designer');

    await page.getByRole('button', { name: 'Print preview', exact: true }).click();
    await expect(page.getByTitle('Print preview')).toBeVisible();
    await page.getByRole('button', { name: 'Close' }).click();

    // a bad formula is reported by the server check
    await page.getByLabel('Form structure').getByRole('button', { name: /field Deviation from nominal/ }).click();
    await props.getByLabel('Formula').fill('bore * nothing');
    await page.getByRole('button', { name: 'Check and calculate' }).click();
    await expect(page.getByRole('alert').filter({ hasText: 'nothing' })).toBeVisible();
    await props.getByLabel('Formula').fill('ROUND(ABS(bore - 10), 3)');

    await page.getByRole('button', { name: 'Done' }).click();
    await expect(page.getByRole('button', { name: 'Open designer' })).toBeVisible();
    await page.getByRole('button', { name: 'Save draft' }).click();
    await expect(page.getByText('Saved.')).toBeVisible();
    const doc = await author.get(`/api/documents/${id}`);
    const schema = JSON.parse(doc.revisions[0].fieldSchema);
    expect(schema.schemaVersion).toBe(2);
    expect(schema.sections[0].title).toBe('Bore measurements');
    expect(schema.fields.map((f: { key: string }) => f.key)).toEqual(['bore', 'dev']);
    expect(schema.tables[0].fixedRows).toEqual(['Burrs', 'Scratches']);
    expect(schema.signatures[0].role).toBe('performed');
    expect(schema.signatures[0].meaning).toBe('I performed the work recorded here');
  });

  test('a performer signs inside the record; changing the content afterwards asks for a new signature', async ({ page }) => {
    const author = await Api.as(users.author);
    const approver = await Api.as(users.approver);
    const schema = {
      schemaVersion: 2, sections: [{ key: 'main', title: 'Torque inspection', columns: 2 }],
      fields: [{ key: 'torque', label: 'Torque', type: 'number', required: true, section: 'main' }],
      signatures: [{ key: 'performer', label: 'Performed by', role: 'performed', meaning: 'I performed this check', section: 'main', order: 1, required: true }],
    };
    const id = await author.post<string>('/api/documents', { kind: 2, title: unique('Torque sign-off'), documentType: 'Inspection Record', siteId: state().siteId, departmentId: null, reviewIntervalMonths: 24, initialRevision: 'A', changeSummary: 'Initial release' });
    const doc = await author.get(`/api/documents/${id}`);
    const rev = doc.revisions[0];
    await author.put(`/api/documents/revisions/${rev.id}`, { changeSummary: 'Initial release', effectiveDate: null, trainingRequired: false, rowVersion: rev.rowVersion, fieldSchema: JSON.stringify(schema) });
    await author.post(`/api/documents/revisions/${rev.id}/submit`);
    await approver.sign(doc.number, users.approver.password);
    const rec = await author.post<{ id: string }>('/api/forms/records', { templateId: id, siteId: state().siteId, departmentId: null, title: null });

    await signIn(page, users.author);
    await page.goto(`/forms/records/${rec.id}`);
    await page.getByLabel('Torque', { exact: true }).fill('42');
    const block = page.getByLabel('Signature Performed by');
    await block.getByRole('button', { name: 'Sign' }).click();
    await page.getByLabel('Your password').fill('wrong-password-123');
    await page.getByRole('dialog').getByRole('button', { name: 'Sign' }).click();
    await expect(page.getByRole('dialog').getByRole('alert')).toBeVisible();
    await page.getByLabel('Your password').fill(users.author.password);
    await page.getByRole('dialog').getByRole('button', { name: 'Sign' }).click();
    await expect(block.getByText('Signed', { exact: true })).toBeVisible();
    await expect(block.getByText(users.author.name)).toBeVisible();
    await shot(page, 'forms-import/b3-record-signed');

    await page.getByLabel('Torque', { exact: true }).fill('45');
    await page.getByRole('button', { name: 'Save draft' }).click();
    await expect(block.getByText(/must be signed again/)).toBeVisible();
    await expect(block.getByRole('button', { name: 'Sign' })).toBeVisible();
  });
});
