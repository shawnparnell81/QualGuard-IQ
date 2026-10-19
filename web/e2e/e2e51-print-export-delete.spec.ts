import fs from 'node:fs';
import path from 'node:path';
import { test, expect, type Page } from '@playwright/test';
import { Api, dialog, localDate, shot, signIn, state, unique, users } from './support';
import { workbook } from './xlsx-fixture';
import { nonconformanceReportDocx } from './docx-fixture';

const DOCX = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';
const XLSX = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';
const button = (p: Page, name: string | RegExp) => p.getByRole('button', { name, exact: typeof name === 'string' ? true : undefined });
/** Screenshots for review go in features-shots when E2E_SCREENSHOT_DIR is set. */
const capture = (p: Page, name: string) => shot(p, `features-shots/${name}`);
/** When screenshots are being taken, the exported files are kept beside them so they can be opened and checked by eye. */
function keep(files: Record<string, Buffer>) {
  const root = process.env.E2E_SCREENSHOT_DIR;
  if (!root) return;
  const dir = path.join(root, 'features-shots', 'exported-files');
  fs.mkdirSync(dir, { recursive: true });
  for (const [name, bytes] of Object.entries(files)) fs.writeFileSync(path.join(dir, name), bytes);
}
// a 2 x 2 PNG
const PNG = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFklEQVR4nGNgYGD4z8DAwMDAwMDAAAANAAH/BqJ+qwAAAABJRU5ErkJggg==', 'base64');

/** The browser has no Save dialog in these tests, so files arrive as downloads; one test stands in a Save dialog to check that path. */
async function withoutSavePicker(p: Page) { await p.addInitScript(() => { delete (window as unknown as { showSaveFilePicker?: unknown }).showSaveFilePicker; }); }
async function download(p: Page, click: () => Promise<void>) {
  const [d] = await Promise.all([p.waitForEvent('download'), click()]);
  const file = await d.path();
  return { name: d.suggestedFilename(), bytes: fs.readFileSync(file!) };
}

test.describe('E2E 51: print, PDF, export, delete and restore, import, settings', () => {
  test('a record prints, downloads as PDF and saves as Excel, Word and CSV; the list exports what is on screen', async ({ page }) => {
    const author = await Api.as(users.author);
    const title = unique('Burr on bracket B-12');
    const ncr = await author.post('/api/ncrs', { title, description: 'Edge burr on 4 of 20 brackets.', source: 'In-process', siteId: state().siteId, detectedOn: localDate(), severity: 1 });
    await withoutSavePicker(page);
    await signIn(page, users.author);
    await page.addInitScript(() => { (window as unknown as { __printed: number }).__printed = 0; window.print = () => { (window as unknown as { __printed: number }).__printed++; }; });
    await page.goto(`/ncr/${ncr.id}`);
    await expect(button(page, 'Download PDF')).toBeVisible();
    await button(page, 'Print').click();
    expect(await page.evaluate(() => (window as unknown as { __printed: number }).__printed)).toBe(1);
    await capture(page, '01-record-toolbar');
    await page.emulateMedia({ media: 'print' });
    await expect(page.getByTestId('print-heading')).toBeVisible();
    await expect(button(page, 'Download PDF')).toBeHidden();
    await capture(page, '02-record-print-preview');
    await page.emulateMedia({ media: 'screen' });

    const pdf = await download(page, () => button(page, 'Download PDF').click());
    expect(pdf.name).toMatch(/\.pdf$/);
    expect(pdf.bytes.subarray(0, 5).toString()).toBe('%PDF-');
    await expect(page.getByTestId('tool-notice')).toContainText('Downloaded');

    await button(page, 'Export').click();
    await expect(page.getByRole('menuitem', { name: /Excel workbook/ })).toBeVisible();
    await capture(page, '03-record-save-as-menu');
    const xlsx = await download(page, () => page.getByRole('menuitem', { name: /Excel workbook/ }).click());
    expect(xlsx.name).toMatch(/\.xlsx$/);
    expect(xlsx.bytes.subarray(0, 2).toString()).toBe('PK');
    await button(page, 'Export').click();
    const docx = await download(page, () => page.getByRole('menuitem', { name: /Word document/ }).click());
    expect(docx.name).toMatch(/\.docx$/);
    expect(docx.bytes.subarray(0, 2).toString()).toBe('PK');
    await button(page, 'Export').click();
    const csv = await download(page, () => page.getByRole('menuitem', { name: /CSV file/ }).click());
    expect(csv.bytes.toString('utf8')).toContain(title);

    // the list: what is on screen, with the filters typed in
    await page.goto('/ncr');
    await expect(page.getByRole('main').getByText(title)).toBeVisible();
    await capture(page, '04-list-toolbar');
    await button(page, 'Export list').click();
    await capture(page, '04b-list-export-menu');
    const list = await download(page, () => page.getByRole('menuitem', { name: /CSV file/ }).click());
    expect(list.name).toMatch(/\.csv$/);
    const text = list.bytes.toString('utf8');
    expect(text).toContain(title);
    expect(text.split('\n')[0]).toContain('Title');
    await button(page, 'Export list').click();
    const listXlsx = await download(page, () => page.getByRole('menuitem', { name: /Excel workbook/ }).click());
    expect(listXlsx.bytes.subarray(0, 2).toString()).toBe('PK');
    await button(page, 'Export list').click();
    const listDocx = await download(page, () => page.getByRole('menuitem', { name: /Word document/ }).click());
    expect(listDocx.bytes.subarray(0, 2).toString()).toBe('PK');
    await button(page, 'Export list').click();
    const listPdf = await download(page, () => page.getByRole('menuitem', { name: /PDF document/ }).click());
    expect(listPdf.bytes.subarray(0, 5).toString()).toBe('%PDF-');
    keep({ 'record.pdf': pdf.bytes, 'record.xlsx': xlsx.bytes, 'record.docx': docx.bytes, 'record.csv': csv.bytes, 'list.csv': list.bytes, 'list.xlsx': listXlsx.bytes, 'list.docx': listDocx.bytes, 'list.pdf': listPdf.bytes });
    await author.dispose();
  });

  test('Chrome and Edge open a Save dialog so the file goes to the folder the person picks', async ({ page }) => {
    await page.addInitScript(() => {
      const w = window as unknown as { __saved: { name: string; size: number; type: string }[]; showSaveFilePicker: unknown };
      w.__saved = [];
      w.showSaveFilePicker = async (o: { suggestedName: string; types: { accept: Record<string, string[]> }[] }) => ({
        createWritable: async () => ({
          write: async (b: Blob) => { w.__saved.push({ name: o.suggestedName, size: b.size, type: Object.keys(o.types[0].accept)[0] }); },
          close: async () => undefined,
        }),
      });
    });
    await signIn(page, users.author);
    await page.goto('/ncr');
    await button(page, 'Export list').click();
    await page.getByRole('menuitem', { name: /Excel workbook/ }).click();
    await expect(page.getByTestId('tool-notice')).toContainText('Saved as Excel workbook');
    await capture(page, '03c-saved-to-chosen-folder');
    const saved = await page.evaluate(() => (window as unknown as { __saved: { name: string; size: number; type: string }[] }).__saved);
    expect(saved).toHaveLength(1);
    expect(saved[0].name).toMatch(/\.xlsx$/);
    expect(saved[0].size).toBeGreaterThan(1000);
    expect(saved[0].type).toContain('spreadsheetml');
  });

  test('deleting needs the permission and a reason, is audited, and the record can be restored', async ({ page, browser }) => {
    const author = await Api.as(users.author);
    const title = unique('Wrong drawing revision used');
    const ncr = await author.post('/api/ncrs', { title, description: 'Rev C used instead of rev D.', source: 'In-process', siteId: state().siteId, detectedOn: localDate(), severity: 1 });

    // without the NCR delete permission the button is there but unavailable
    const ctx = await browser.newContext();
    const ap = await ctx.newPage();
    await signIn(ap, users.author);
    await ap.goto(`/ncr/${ncr.id}`);
    await expect(button(ap, 'Delete')).toBeDisabled();
    expect(await author.status('POST', `/api/records/ncr/${ncr.id}/delete`, { reason: 'Created by mistake' })).toBe(403);
    await ctx.close();

    await signIn(page, users.admin);
    await page.goto(`/ncr/${ncr.id}`);
    await button(page, 'Delete').click();
    await expect(dialog(page).getByRole('button', { name: 'Delete' })).toBeDisabled();
    await page.getByTestId('delete-reason').fill('Duplicate of an NCR raised by the night shift');
    await capture(page, '05-delete-reason');
    await dialog(page).getByRole('button', { name: 'Delete' }).click();
    await expect(page).toHaveURL(/\/ncr$/);
    await expect(page.getByTestId('arrival-notice')).toContainText('Deleted');
    await expect(page.getByRole('main').getByText(title)).toHaveCount(0);
    expect(await author.status('GET', `/api/ncrs/${ncr.id}`)).toBe(404);

    const admin = await Api.as(users.admin);
    const trail = await admin.get(`/api/audit-trail?page=1&pageSize=20`);
    expect(trail.items.some((e: { action: string; description: string }) => e.action === 'Deleted' && e.description.includes(title) && e.description.includes('Duplicate of an NCR'))).toBe(true);

    await page.goto('/admin/deleted-records');
    const row = page.getByTestId('deleted-row').filter({ hasText: title });
    await expect(row).toContainText('Duplicate of an NCR raised by the night shift');
    await capture(page, '06-deleted-records');
    await row.getByRole('button', { name: 'Restore' }).click();
    await page.getByTestId('restore-reason').fill('Not a duplicate after all');
    await capture(page, '07-restore-reason');
    await page.getByTestId('restore-confirm').click();
    await expect(page.getByTestId('restore-done')).toContainText('Restored');
    const back = await author.get(`/api/ncrs/${ncr.id}`);
    expect(back.title).toBe(title);
    await page.goto('/ncr');
    await expect(page.getByRole('main').getByText(title)).toBeVisible();
    await admin.dispose(); await author.dispose();
  });

  test('NCRs import from CSV and CAPAs from Excel with mapping, a preview and errors per row', async ({ page, browser }) => {
    const tag = Date.now().toString(36).toUpperCase();
    await signIn(page, users.author);
    await page.goto('/ncr');
    await page.getByRole('link', { name: 'Import NCRs' }).click();
    await expect(page).toHaveURL(/\/imports\?target=log-ncrs/);
    const csv = 'Title,Description,Source,Severity,Part number,Status\n'
      + `Scratch on cover ${tag},Cosmetic scratch,Receiving,Minor,CV-1,Open\n`
      + `Missing label ${tag},No part label,Final inspection,Severe,CV-2,Open\n`;
    await page.getByTestId('import-file').setInputFiles({ name: 'ncr log.csv', mimeType: 'text/csv', buffer: Buffer.from(csv) });
    await page.getByRole('button', { name: 'Upload and inspect' }).click();
    await expect(page).toHaveURL(/\/imports\/[0-9a-f-]{36}$/);
    await expect(page.getByRole('combobox', { name: 'Map Title' })).toBeVisible();
    await capture(page, '08-import-mapping');
    await page.getByRole('button', { name: 'Validate (dry run)' }).click();
    await expect(page.getByText(/1 of 2 rows ready/).first()).toBeVisible();
    await expect(page.getByText(/Severe/).first()).toBeVisible();
    await capture(page, '09-import-preview-errors');

    // fix the file and import every row (all or nothing)
    await page.goto('/imports?target=log-ncrs');
    await page.getByTestId('import-file').setInputFiles({ name: 'ncr log.csv', mimeType: 'text/csv', buffer: Buffer.from(csv.replace('Severe', 'Major')) });
    await page.getByRole('button', { name: 'Upload and inspect' }).click();
    await page.getByRole('button', { name: 'Validate (dry run)' }).click();
    await expect(page.getByText(/2 of 2 rows ready/).first()).toBeVisible();
    await page.getByRole('button', { name: 'Submit for approval' }).click();
    const url = page.url();
    const ctx = await browser.newContext();
    const p2 = await ctx.newPage();
    await signIn(p2, users.approver);
    await p2.goto(url);
    await p2.getByRole('tab', { name: 'Preview and validation' }).click();
    await p2.getByRole('button', { name: 'Approve and import' }).click();
    await expect(p2.getByText(/2 records created/)).toBeVisible();
    await ctx.close();
    await page.goto('/ncr');
    await expect(page.getByRole('main').getByText(`Missing label ${tag}`)).toBeVisible();

    // CAPAs from an Excel workbook
    const book = workbook([{ name: 'CAPAs', cells: {
      A1: 'Title', B1: 'Problem statement', C1: 'Source', D1: 'Type',
      A2: `Torque drift ${tag}`, B2: 'Torque readings drift after 4 hours.', C2: 'NCR trend', D2: 'Corrective',
    } }]);
    await page.goto('/capa');
    await page.getByRole('link', { name: 'Import CAPAs' }).click();
    await page.getByTestId('import-file').setInputFiles({ name: 'capa log.xlsx', mimeType: XLSX, buffer: book });
    await page.getByRole('button', { name: 'Upload and inspect' }).click();
    await page.getByRole('button', { name: 'Validate (dry run)' }).click();
    await expect(page.getByText(/1 of 1 rows ready/).first()).toBeVisible();
    await capture(page, '10-import-excel-preview');
    await page.getByRole('button', { name: 'Submit for approval' }).click();
    const url2 = page.url();
    const ctx2 = await browser.newContext();
    const p3 = await ctx2.newPage();
    await signIn(p3, users.approver);
    await p3.goto(url2);
    await p3.getByRole('tab', { name: 'Preview and validation' }).click();
    await p3.getByRole('button', { name: 'Approve and import' }).click();
    await expect(p3.getByText(/1 records created/)).toBeVisible();
    await ctx2.close();
  });

  test('a Word form is imported from the form templates list', async ({ page }) => {
    await signIn(page, users.author);
    await page.goto('/forms/templates');
    await page.getByRole('link', { name: 'Import from Word or Excel' }).click();
    await expect(page).toHaveURL(/\/imports\?mode=template/);
    await page.getByTestId('import-file').setInputFiles({ name: 'Nonconformance report.docx', mimeType: DOCX, buffer: nonconformanceReportDocx() });
    await expect(page.getByRole('combobox', { name: 'Import mode' })).toHaveText('Convert to an electronic form');
    await page.getByRole('button', { name: 'Upload and inspect' }).click();
    await expect(page.getByText('Preview ready').first()).toBeVisible();
    await expect(page.getByLabel('Disposition')).toBeVisible();
    await capture(page, '11-word-import');
  });

  test('organization and personal settings', async ({ page }) => {
    await signIn(page, users.admin);
    await page.getByRole('button', { name: 'User menu' }).click();
    await page.getByRole('menuitem', { name: 'Organization settings' }).click();
    await expect(page.getByRole('heading', { name: 'Organization settings' })).toBeVisible();
    await page.getByRole('combobox', { name: 'Date format' }).click();
    await page.getByRole('option', { name: '2026-03-07' }).click();
    await page.getByTestId('settings-save').click();
    await expect(page.getByTestId('settings-msg')).toHaveText('Settings saved.');
    await capture(page, '12-org-settings-general');

    await page.getByRole('tab', { name: 'Logo' }).click();
    await page.getByTestId('logo-file').setInputFiles({ name: 'logo.png', mimeType: 'image/png', buffer: PNG });
    await expect(page.getByTestId('logo-preview')).toBeVisible();
    await expect(page.getByTestId('org-logo')).toBeVisible();
    await capture(page, '13-org-settings-logo');
    await page.getByRole('button', { name: 'Remove logo' }).click();
    await expect(page.getByTestId('logo-preview')).toHaveCount(0);

    await page.getByRole('tab', { name: 'Numbering' }).click();
    const ncrRow = page.getByRole('row').filter({ hasText: /^NCR|Nonconformance/ }).first();
    await expect(ncrRow).toBeVisible();
    await capture(page, '14-org-settings-numbering');
    await page.getByRole('tab', { name: 'Sign-in & sessions' }).click();
    await expect(page.getByText('Signed out after')).toBeVisible();
    await capture(page, '15-org-settings-security');
    await page.getByRole('tab', { name: 'Storage & scanning' }).click();
    await expect(page.getByText('Virus scanning of uploads')).toBeVisible();
    await capture(page, '16-org-settings-system');

    await page.getByRole('button', { name: 'User menu' }).click();
    await page.getByRole('menuitem', { name: 'My settings' }).click();
    await expect(page.getByRole('heading', { name: 'My settings' })).toBeVisible();
    // the closing user menu is hidden from the accessibility tree but still covers the page until it has faded out
    await expect(page.locator('.MuiMenu-root')).toHaveCount(0);
    await page.waitForLoadState('networkidle');
    await page.getByLabel('Also send them to me by e-mail').uncheck();
    await expect(page.getByTestId('prefs-msg')).toHaveText('Saved.');
    await capture(page, '17-my-settings');
    const admin = await Api.as(users.admin);
    expect((await admin.get('/api/account/preferences')).emailNotifications).toBe(false);
    await admin.put('/api/account/preferences', { emailNotifications: true, dateFormat: null, theme: null });
    const s = await admin.get('/api/organization/settings');
    await admin.put('/api/organization/settings', { ...s, dateFormat: 'auto' });
    await admin.dispose();
  });
});
