import { test, expect } from '@playwright/test';
import { Api, shot, signIn, state, unique, users } from './support';
import { receivingInspectionWorkbook } from './xlsx-fixture';

async function torqueTemplate(author: Api, approver: Api, title: string) {
  const id = await author.post<string>('/api/documents', { kind: 2, title, documentType: 'Inspection Record', siteId: state().siteId, departmentId: null, reviewIntervalMonths: 24, initialRevision: 'A', changeSummary: 'Initial release' });
  const doc = await author.get(`/api/documents/${id}`);
  const rev = doc.revisions[0];
  const schema = { schemaVersion: 2, sections: [{ key: 'main', title: 'Main', columns: 2 }], fields: [
    { key: 'part', label: 'Part number', type: 'text', section: 'main', required: true },
    { key: 'torque', label: 'Torque', type: 'number', section: 'main', required: true },
    { key: 'plant', label: 'Plant', type: 'site', section: 'main' }] };
  await author.put(`/api/documents/revisions/${rev.id}`, { changeSummary: 'Initial release', effectiveDate: null, trainingRequired: false, rowVersion: rev.rowVersion, fieldSchema: JSON.stringify(schema) });
  await author.post(`/api/documents/revisions/${rev.id}/submit`);
  await approver.sign(doc.number, users.approver.password);
  return id;
}

async function draftRecord(author: Api, templateId: string, values: Record<string, string>) {
  const { id } = await author.post<{ id: string }>('/api/forms/records', { templateId, siteId: state().siteId, departmentId: null, title: null });
  const rec = await author.get(`/api/forms/records/${id}`);
  await author.put(`/api/forms/records/${id}`, { values, rowVersion: rec.rowVersion });
  return (await author.get(`/api/forms/records/${id}`)).number as string;
}

/** Forms build B9: one review for every converted format with suggestions and confirmation, form families, the conversion pipeline, and the eleven-step mapping workbench. */
test.describe('Forms B9: conversion engine and mapping workbench', () => {
  test('an Excel conversion is reviewed item by item, filed as an NCR form, and its pipeline and workbench explain what will be created', async ({ page }) => {
    await signIn(page, users.author);
    await page.goto('/imports');
    await page.getByTestId('import-file').setInputFiles({ name: 'Receiving inspection.xlsx', mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: receivingInspectionWorkbook() });
    await page.getByRole('combobox', { name: 'Import mode' }).click();
    await page.getByRole('option', { name: 'Convert to an electronic form' }).click();
    await page.getByRole('button', { name: 'Upload and inspect' }).click();
    await expect(page.getByText('Preview ready').first()).toBeVisible();

    await page.getByRole('tab', { name: /^Detections/ }).click();
    const review = page.getByRole('table', { name: 'Conversion review' });
    await expect(review.getByText('Table column').first()).toBeVisible();
    await expect(review.getByText('Formula').first()).toBeVisible();
    await page.getByRole('combobox', { name: 'Form family' }).click();
    await page.getByRole('option', { name: 'NCR form' }).click();
    await expect(page.getByText(/usual elements found; the template is filed with ncr/)).toBeVisible();
    await expect(page.getByLabel('Family elements').getByText(/Root cause: missing/)).toBeVisible();
    await shot(page, 'forms-import/b9-conversion-review');

    await page.getByRole('tab', { name: 'Pipeline' }).click();
    const pipe = page.getByRole('table', { name: 'Conversion pipeline' });
    await expect(pipe.locator('tbody tr')).toHaveCount(23);
    await expect(pipe.getByText('Detect formulas')).toBeVisible();
    await shot(page, 'forms-import/b9-pipeline');

    await page.getByRole('tab', { name: 'Workbench' }).click();
    await expect(page.getByLabel('Step 1 File review').getByText('Passed')).toBeVisible();
    await expect(page.getByLabel('Step 3 Target').getByText('New template, revision A (draft)')).toBeVisible();
    await expect(page.getByLabel('Step 5 Fields').getByRole('table', { name: 'Field mapping' })).toBeVisible();
    await expect(page.getByLabel('Step 8 Preview').getByText('a new draft form template')).toBeVisible();
    await shot(page, 'forms-import/b9-workbench-conversion');
  });

  test('a CSV updates matching records after the author chooses so in the workbench; references resolve; the approver sees before and after; results come as a package', async ({ page, browser }) => {
    const author = await Api.as(users.author);
    const approver = await Api.as(users.approver);
    const title = unique('Torque check');
    const tpl = await torqueTemplate(author, approver, title);
    await draftRecord(author, tpl, { part: 'P-1', torque: '5' });

    await signIn(page, users.author);
    await page.goto('/imports');
    await page.getByTestId('import-file').setInputFiles({ name: 'torque update.csv', mimeType: 'text/csv', buffer: Buffer.from('Part number,Torque,Plant\nP-1,9,E2E\nP-3,7,E2E\nP-4,8,NOWHERE\n') });
    await page.getByLabel('Form template').click();
    await page.getByRole('option', { name: new RegExp(title) }).click();
    await page.getByRole('button', { name: 'Upload and inspect' }).click();
    await expect(page.getByText('Mapping required').first()).toBeVisible();

    await page.getByRole('tab', { name: 'Workbench' }).click();
    await page.getByRole('combobox', { name: 'Target template' }).click();
    await page.getByRole('option', { name: new RegExp(title) }).click();
    await page.getByRole('combobox', { name: 'Existing records' }).click();
    await page.getByRole('option', { name: 'Update records that already exist' }).click();
    await page.getByRole('button', { name: 'Apply' }).click();
    await expect(page.getByLabel('Step 3 Target').getByText('Updated when they match')).toBeVisible();

    await page.getByRole('tab', { name: 'Settings and mapping' }).click();
    await page.getByLabel('Duplicate check on').click();
    await page.getByRole('option', { name: 'Part number' }).click();
    await page.keyboard.press('Escape');
    await page.getByRole('button', { name: 'Validate (dry run)' }).click();
    await expect(page.getByText(/1 existing records will be updated/)).toBeVisible();
    const preview = page.getByRole('table', { name: 'Preview' });
    await expect(preview.getByText(/Updates the existing record \(now: .*Torque 5\)/)).toBeVisible();
    await expect(preview.getByText(/no site 'NOWHERE' was found \(missing reference\)/)).toBeVisible();
    await shot(page, 'forms-import/b9-update-preview');

    await page.getByRole('tab', { name: 'Workbench' }).click();
    await expect(page.getByLabel('Step 6 Relationships').getByRole('table', { name: 'Relationships' }).getByText('1: NOWHERE')).toBeVisible();
    await expect(page.getByLabel('Step 8 Preview').getByRole('table', { name: 'Changes to existing records' }).getByText('torque')).toBeVisible();
    await shot(page, 'forms-import/b9-workbench-records');
    await page.getByRole('tab', { name: 'Preview and validation' }).click();
    await page.getByLabel('Import only the rows without problems').check();
    await page.getByRole('button', { name: 'Submit for approval' }).click();
    await expect(page.getByText('Waiting for approval by someone other than the uploader.')).toBeVisible();
    const url = page.url();

    const ctx = await browser.newContext();
    const p2 = await ctx.newPage();
    await signIn(p2, users.approver);
    await p2.goto(url);
    await p2.getByRole('tab', { name: 'Preview and validation' }).click();
    await p2.getByRole('button', { name: 'Approve and import' }).click();
    await expect(p2.getByText(/1 records created, 1 updated/)).toBeVisible();
    await p2.getByRole('tab', { name: 'Workbench' }).click();
    const results = p2.getByLabel('Step 11 Results');
    await expect(results.getByRole('link', { name: 'Download result package' })).toBeVisible();
    await expect(results.getByText('available')).toBeVisible();
    await shot(p2, 'forms-import/b9-results');
    const pkg = await p2.request.get(url.replace(/\/imports\//, '/api/imports/') + '/result-package');
    expect(pkg.ok()).toBeTruthy();
    expect(pkg.headers()['content-type']).toContain('application/zip');
    await ctx.close();
  });

  test('an attachment package puts the file on an existing record by its number', async ({ page, browser }) => {
    const author = await Api.as(users.author);
    const approver = await Api.as(users.approver);
    const tpl = await torqueTemplate(author, approver, unique('Torque check'));
    const number = await draftRecord(author, tpl, { part: 'P-9', torque: '4' });

    await signIn(page, users.author);
    await page.goto('/imports');
    await page.getByTestId('import-file').setInputFiles({ name: 'calibration certificate.csv', mimeType: 'text/csv', buffer: Buffer.from('Certificate,Result\nC-1,pass\n') });
    await page.getByLabel('Form template').click();
    await page.getByRole('option').first().click();
    await page.getByRole('button', { name: 'Upload and inspect' }).click();
    await expect(page.getByText('Mapping required').first()).toBeVisible();
    await page.getByRole('tab', { name: 'Workbench' }).click();
    await page.getByRole('combobox', { name: 'Import purpose' }).click();
    await page.getByRole('option', { name: 'Attachment package' }).click();
    await page.getByLabel('Record number').fill(number);
    await page.getByRole('button', { name: 'Apply' }).click();
    await expect(page.getByLabel('Step 3 Target').getByText(number)).toBeVisible();
    await shot(page, 'forms-import/b9-attachment-package');
    await page.getByRole('tab', { name: 'Check and approve' }).click();
    await page.getByRole('button', { name: 'Check' }).click();
    await page.getByRole('button', { name: 'Submit for approval' }).click();
    await expect(page.getByText('Waiting for approval by someone other than the uploader.')).toBeVisible();
    const url = page.url();

    const ctx = await browser.newContext();
    const p2 = await ctx.newPage();
    await signIn(p2, users.approver);
    await p2.goto(url);
    await p2.getByRole('tab', { name: 'Check and approve' }).click();
    await p2.getByRole('button', { name: 'Approve and import' }).click();
    await expect(p2.getByText(`File attached to ${number}.`)).toBeVisible();
    await ctx.close();
  });
});
