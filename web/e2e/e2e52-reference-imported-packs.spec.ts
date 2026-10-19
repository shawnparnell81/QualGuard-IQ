import { test, expect, type Page } from '@playwright/test';
import { Api, shot, signIn, state, unique, users } from './support';

const button = (p: Page, name: string | RegExp) => p.getByRole('button', { name, exact: typeof name === 'string' ? true : undefined });
/** Screenshots for review go in next-shots when E2E_SCREENSHOT_DIR is set. */
const capture = (p: Page, name: string, fullPage?: boolean) => shot(p, `next-shots/${name}`, { fullPage });

test.describe('E2E 52: reference copies, read-only historical imports, industry pack picker', () => {
  test('a reference copy is released without approval and replaced by a newer edition', async ({ page }) => {
    const author = await Api.as(users.author);
    const title = unique('ISO 2859-1 sampling procedures');
    const id = await author.post<string>('/api/documents', { kind: 0, title, documentType: 'External Standard', siteId: state().siteId, departmentId: null, ownerId: null, reviewIntervalMonths: 24, initialRevision: '2021', partNumber: null, drawingSize: null, sheetCount: null, changeSummary: null });
    const doc = await author.get(`/api/documents/${id}`);
    await author.upload('DocumentRevision', doc.revisions[0].id, 'iso-2859-1.pdf', '%PDF-1.4 standard');
    await signIn(page, users.author);
    await page.goto(`/documents/${id}`);
    await expect(page.getByTestId('reference-chip')).toBeVisible();
    await expect(page.getByTestId('reference-notice')).toBeVisible();
    await expect(button(page, /Submit Rev/)).toHaveCount(0);
    await expect(page.getByRole('tab', { name: 'Approvals' })).toHaveCount(0);
    await capture(page, '01-reference-copy-draft');
    await page.getByTestId('release-reference').click();
    await expect(page.getByTestId('replace-reference')).toBeVisible();
    await expect(button(page, 'Start new revision')).toHaveCount(0);
    await capture(page, '02-reference-copy-released');
    await page.getByTestId('replace-reference').click();
    await page.getByLabel('Edition or issue (optional)').fill('2024');
    await capture(page, '03-reference-copy-replace');
    await button(page, 'Add newer copy').click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await author.upload('DocumentRevision', (await author.get(`/api/documents/${id}`)).revisions[0].id, 'iso-2859-1-2024.pdf', '%PDF-1.4 newer');
    await page.reload();
    await page.getByTestId('release-reference').click();
    await page.getByRole('tab', { name: 'Copies' }).click();
    await expect(page.getByText('Superseded').first()).toBeVisible();
    await capture(page, '04-reference-copy-superseded');
    await page.goto('/documents');
    await expect(page.getByTestId('reference-chip').first()).toBeVisible();
    await capture(page, '05-library-reference-chip');
    await author.dispose();
  });

  test('administrators see which document types are reference copies', async ({ page }) => {
    await signIn(page, users.admin);
    await page.goto('/documents/types');
    await expect(page.getByRole('columnheader', { name: 'Control' })).toBeVisible();
    await page.getByRole('cell', { name: 'External Standard', exact: true }).click();
    await expect(page.getByLabel(/Reference copy: stored and controlled/)).toBeChecked();
    await capture(page, '06-document-type-reference');
  });

  test('a historical import is labelled Imported (read-only) and can still be printed and exported', async ({ page }) => {
    const author = await Api.as(users.author);
    const approver = await Api.as(users.approver);
    const tag = Date.now().toString(36).toUpperCase();
    const csv = 'NCR number,Title,Description,Source,Severity,Status,Closure notes\n'
      + `HIS-${tag}-1,Burr on edge ${tag},Burr found at receiving,Receiving,Minor,Closed,Deburred and released\n`;
    const job = await author.importFile(state().siteId, 'legacy ncr log.csv', csv, 'log-ncrs');
    await author.put(`/api/imports/${job}/purpose`, { purpose: 'historical-records', targetModule: 'log-ncrs', targetTemplateId: null, templateRevision: null, existingRecords: null, recordNumber: null });
    await author.post(`/api/imports/${job}/validate`);
    await author.post(`/api/imports/${job}/submit`, { skipInvalidRows: false });
    await approver.post(`/api/imports/${job}/approve`);
    await signIn(page, users.author);
    await page.goto(`/imports/${job}`);
    await capture(page, '07-historical-import-done');
    const list = await author.get(`/api/ncrs?q=${tag}`);
    const ncr = (list.items ?? list)[0];
    await page.goto(`/ncr/${ncr.id}`);
    await expect(page.getByTestId('imported-read-only')).toHaveText('Imported (read-only)');
    await expect(page.getByTestId('imported-read-only-notice')).toBeVisible();
    await expect(button(page, 'Download PDF')).toBeEnabled();
    await expect(button(page, 'Print')).toBeEnabled();
    await capture(page, '08-imported-read-only-record');
    expect(await author.status('POST', `/api/records/Ncr/${ncr.id}/files`)).toBeGreaterThanOrEqual(400);
    await author.dispose(); await approver.dispose();
  });

  test('first run offers the industry packs with a preview of their forms, and Admin can install more', async ({ page }) => {
    await signIn(page, users.author);
    await page.goto('/');
    const card = page.getByTestId('setup-packs');
    await expect(card).toBeVisible();
    for (const k of ['automotive', 'aerospace', 'electronics', 'medical-device', 'manufacturing']) await expect(card.getByTestId(`pack-${k}`)).toBeVisible();
    await card.getByTestId('preview-aerospace').click();
    await expect(card.getByTestId('pack-aerospace').getByText('CAPA', { exact: false }).first()).toBeVisible();
    // other specs may already have installed some library forms; choose packs that still have forms to add
    const author = await Api.as(users.author);
    const open = ((await author.get('/api/industry-packs')).packs as { key: string; name: string; templateCount: number; installedCount: number }[])
      .filter(p => p.installedCount < p.templateCount).slice(0, 2);
    await author.dispose();
    expect(open.length).toBeGreaterThan(0);
    for (const p of open) await card.getByLabel(`Choose ${p.name}`).check();
    await capture(page, '09-first-run-industry-packs');
    await page.getByTestId('pack-site').click();
    await page.getByRole('option').first().click();
    await card.getByTestId('install-packs').click();
    await expect(page.getByTestId('setup-packs')).toHaveCount(0, { timeout: 60_000 });

    await page.goto('/admin/industry-packs');
    await expect(page.getByTestId(`pack-${open[0].key}`).getByText('Installed')).toBeVisible();
    await expect(page.getByLabel(`Choose ${open[0].name}`)).toBeDisabled();
    await capture(page, '10-admin-industry-packs');

    // someone without the install permission can look but not install
    const viewer = await Api.as(users.viewer);
    expect((await viewer.get('/api/industry-packs')).canInstall).toBe(false);
    expect(await viewer.status('POST', '/api/industry-packs/install', { siteId: state().siteId, packs: ['electronics'] })).toBe(403);
    await viewer.dispose();
  });
});
