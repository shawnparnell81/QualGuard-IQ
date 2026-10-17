import { test, expect } from '@playwright/test';
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { Api, shot, signIn, state, unique, users } from './support';
import { inspectionSchema } from './forms-fixtures';

/** Forms build B2: print view. Standardized output for records and template revisions, with markings and page numbering. */

/** Saves page 1 of a PDF as a PNG next to the other screenshots (only when screenshots are being captured). */
function pdfShot(pdf: Buffer, name: string) {
  const dir = process.env.E2E_SCREENSHOT_DIR;
  if (!dir) return;
  const tmp = path.join(os.tmpdir(), `${name.replace(/\W/g, '_')}.pdf`);
  fs.writeFileSync(tmp, pdf);
  fs.mkdirSync(path.join(dir, path.dirname(name)), { recursive: true });
  execFileSync('pdftoppm', ['-png', '-r', '80', '-f', '1', '-l', '1', '-singlefile', tmp, path.join(dir, name)]);
}

test.describe('Forms B2: print view', () => {
  test('a record and its template print as PDF with status markings, page numbers and the record number', async ({ page }) => {
    const author = await Api.as(users.author);
    const approver = await Api.as(users.approver);
    const title = unique('Printed dimensional inspection');
    const id = await author.post<string>('/api/documents', { kind: 2, title, documentType: 'Inspection Record', siteId: state().siteId, departmentId: null, reviewIntervalMonths: 24, initialRevision: 'A', changeSummary: 'Initial release' });
    const doc = await author.get(`/api/documents/${id}`);
    const rev = doc.revisions[0];
    const schema = { ...inspectionSchema, page: { ...inspectionSchema.page, footer: { left: '{template} rev {revision}', center: '{confidentiality}', right: 'Page {page} of {pages}' }, confidentiality: 'Company confidential' } };
    await author.put(`/api/documents/revisions/${rev.id}`, { changeSummary: 'Initial release', effectiveDate: null, trainingRequired: false, rowVersion: rev.rowVersion, fieldSchema: JSON.stringify(schema) });
    await author.post(`/api/documents/revisions/${rev.id}/submit`);
    await approver.sign(doc.number, users.approver.password);
    const rec = await author.post<{ id: string }>('/api/forms/records', { templateId: id, siteId: state().siteId, departmentId: null, title: null });
    const r0 = await author.get(`/api/forms/records/${rec.id}`);
    const rows = Array.from({ length: 30 }, (_, i) => ({ balloon: String(i + 1), lsl: '9.95', usl: '10.05', measured: i === 4 ? '10.09' : '10.01' }));
    await author.put(`/api/forms/records/${rec.id}`, { rowVersion: r0.rowVersion, values: { lot: 'LOT-5520', qty: '30', unit_cost: '4.25', outcome: 'Pass', dims: JSON.stringify({ rows }), visual: JSON.stringify({ rows: [{ ok: 'pass' }, { ok: 'pass' }, { ok: 'pass' }] }), ops: JSON.stringify([{ op_no: '10', op_minutes: '12' }]) } });

    await signIn(page, users.author);
    await page.goto(`/forms/records/${rec.id}`);
    const print = page.getByRole('link', { name: 'Print', exact: true });
    await expect(print).toBeVisible();
    await expect(page.getByRole('link', { name: 'Print original look' })).toHaveCount(0); // no converted source layout on this template
    const href = await print.getAttribute('href');
    const res = await page.request.get(href!);
    expect(res.status()).toBe(200);
    expect(res.headers()['content-type']).toContain('application/pdf');
    const body = await res.body();
    const text = body.toString('latin1');
    expect(text.startsWith('%PDF-')).toBeTruthy();
    const pages = (text.match(/\/Type \/Page \/Parent/g) ?? []).length;
    expect(pages).toBeGreaterThanOrEqual(2);
    expect(text).toContain(`(Page ${pages} of ${pages}) Tj`);
    expect(text).toContain('(DRAFT) Tj');
    expect(text).toContain('(Company confidential) Tj');
    expect(text).toContain('(LOT-5520) Tj');
    expect(text).toContain(`(Record ${r0.number}) Tj`);
    expect(text).toContain('/MediaBox [0 0 792 612]'); // landscape results section
    pdfShot(body, 'forms-import/b2-record-print-standardized');

    await page.goto(`/forms/templates/${id}`);
    await page.getByRole('tab', { name: /revisions/i }).click();
    const tplPrint = page.getByRole('link', { name: 'Print rev A' });
    await expect(tplPrint).toBeVisible();
    await shot(page, 'forms-import/b2-template-print-action');
    const tpl = await page.request.get((await tplPrint.getAttribute('href'))!);
    const tplText = (await tpl.body()).toString('latin1');
    expect(tplText).toContain('CONTROLLED COPY');
    expect(tplText).not.toContain('(DRAFT) Tj');

    const outsider = await Api.as(users.outsider);
    expect([403, 404]).toContain(await outsider.status('GET', `/api/forms/records/${rec.id}/pdf`));
  });
});
