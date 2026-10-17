import { execFileSync } from 'node:child_process';
import { test, expect } from '@playwright/test';
import { Api, dialog, signIn, state, unique, users, type User } from './support';

/** Gap-closure UI checks: shared site picker (LPA, cost of quality), friendly record type labels, document type library. */
test.describe('E2E 14: gap closure', () => {
  test('the shared site picker offers only my active sites in layered audit and cost of quality dialogs', async ({ page }) => {
    const admin = await Api.as(users.admin);
    const t = Date.now().toString(36);
    const other = await admin.post<string>('/api/organization/sites', { code: `OT${t}`.slice(0, 10).toUpperCase(), name: `Other Plant ${t}`, address: null, timeZone: 'America/New_York' });
    const role = await admin.post<string>('/api/admin/roles', { name: `LPA lead ${t}`, description: 'UI test', permissions: ['lpa.view', 'lpa.edit', 'coq.view', 'coq.edit'] });
    const u: User = { key: 'lpa', email: `lpa-${t}@qms.test`, password: 'lpa-lead-password-1', name: `Lee Layered ${t}` };
    const uid = await admin.post<string>('/api/admin/users', { email: u.email, displayName: u.name, password: u.password });
    await admin.post(`/api/admin/users/${uid}/roles`, { roleId: role, siteId: state().siteId, departmentId: null });

    await signIn(page, u);
    await page.goto('/lpa?view=templates');
    await page.getByRole('button', { name: 'New template' }).click();
    const d = dialog(page);
    await d.getByLabel('Name').fill(unique('Cell 7 layer 1'));
    await d.getByLabel('Questions, one per line').fill('Control plan posted?');
    const picker = d.getByTestId('site-picker');
    await picker.click();
    await expect(page.getByRole('option', { name: /Test Plant/ })).toBeVisible();
    await expect(page.getByRole('option', { name: new RegExp(`Other Plant ${t}`) })).toHaveCount(0);
    await picker.fill('zzz-no-site');
    await expect(page.getByText('No matching site')).toBeVisible();
    await picker.fill('test');
    await page.keyboard.press('ArrowDown');
    await page.keyboard.press('Enter');
    await expect(picker).toHaveValue(/Test Plant \(E2E\)/);
    await d.getByRole('button', { name: 'Create template' }).click();
    await expect(d).toHaveCount(0);
    const api = await Api.as(u);
    const templates = await api.get<{ name: string; siteId: string }[]>('/api/lpa/templates');
    expect(templates.some((x) => x.name.startsWith('Cell 7 layer 1') && x.siteId === state().siteId)).toBe(true);
    // The server refuses a site outside my access even if a request names it directly.
    expect(await api.status('POST', '/api/lpa/templates', { name: 'Direct', layer: 1, area: null, questions: ['Q'], onFailure: 0, escalateToId: null, siteId: other, departmentId: null })).toBe(403);

    await page.goto('/coq?view=entries');
    await page.getByRole('button', { name: 'Record cost' }).click();
    await dialog(page).getByLabel('Element').fill('Training');
    await dialog(page).getByLabel('Amount (USD)').fill('125');
    await dialog(page).getByRole('button', { name: 'Record' }).click();
    await expect(dialog(page).getByText('Select a site.')).toBeVisible();
    await dialog(page).getByTestId('site-picker').click();
    await expect(page.getByRole('option', { name: new RegExp(`Other Plant ${t}`) })).toHaveCount(0);
    await page.getByRole('option', { name: /Test Plant/ }).click();
    await dialog(page).getByRole('button', { name: 'Record' }).click();
    await expect(dialog(page)).toHaveCount(0);
    expect((await api.get<{ amount: number; siteId: string }[]>('/api/coq/entries')).some((e) => e.amount === 125 && e.siteId === state().siteId)).toBe(true);
    await api.dispose(); await admin.dispose();
  });

  test('the workflow designer shows friendly record type names', async ({ page }) => {
    await signIn(page, users.admin);
    await page.goto('/platform/workflows');
    await expect(page.getByRole('tab', { name: 'Change Request', exact: true })).toBeVisible();
    await expect(page.getByRole('tab', { name: 'First Article Inspection' })).toBeVisible();
    await expect(page.getByRole('tab', { name: 'ChangeRequest', exact: true })).toHaveCount(0);
    await page.goto('/admin/routes');
    await expect(page.getByRole('cell', { name: 'Change Request', exact: true })).toBeVisible();
  });

  test('document types come from the governed library and retired types are not offered', async ({ page }) => {
    const admin = await Api.as(users.admin);
    const name = unique('Golden Sample');
    const ty = await admin.post<{ id: string }>('/api/document-types', { kind: 0, name, description: null });
    await signIn(page, users.admin);
    await page.goto('/documents/types');
    await expect(page.getByRole('cell', { name: 'Standard Operating Procedure' })).toBeVisible();
    await page.getByRole('cell', { name }).click();
    await dialog(page).getByLabel('In use for new documents').uncheck();
    await dialog(page).getByRole('button', { name: 'Save' }).click();
    await expect(page.getByRole('row', { name: new RegExp(name) })).toContainText('Retired');
    await page.goto('/documents/new');
    await page.getByRole('combobox', { name: 'Type' }).click();
    await expect(page.getByRole('option', { name: 'Calibration Instruction' })).toBeVisible();
    await expect(page.getByRole('option', { name })).toHaveCount(0);
    await page.keyboard.press('Escape');
    expect(ty.id).toBeTruthy();
    await admin.dispose();
  });

  test('scrap rate tiles name their denominator and manual volumes need a second approver', async ({ page }) => {
    await signIn(page, users.admin);
    await page.goto('/insights');
    await expect(page.getByTestId('denominator-scrap_rate')).toContainText(/denominator|cannot be calculated/i);
    await page.getByRole('tab', { name: 'Production volumes' }).click();
    await expect(page.getByText('Denominator order')).toBeVisible();
    await expect(page.getByText(/1\. MES production quantity/).first()).toBeVisible();
    await page.getByRole('button', { name: 'Enter production volume' }).click();
    const d = dialog(page);
    await d.getByTestId('site-picker').click();
    await page.getByRole('option', { name: /Test Plant/ }).click();
    await d.getByLabel('Quantity produced').fill('480');
    await d.getByLabel('Source / notes').fill(unique('Shift log'));
    await d.getByRole('button', { name: 'Submit for approval' }).click();
    const row = page.getByRole('row', { name: /480/ }).first();
    await expect(row).toContainText('Pending');
    await row.getByRole('button', { name: 'Approve' }).click();
    await expect(page.getByRole('alert')).toContainText(/cannot approve it/);
  });

  test('retention policies take filters and priority, and the dry run explains which policy governs each record', async ({ page }) => {
    const admin = await Api.as(users.admin);
    const t = Date.now().toString(36);
    const ncr = await admin.post<{ id: string; number: string }>('/api/ncrs', { title: `Retention ${t}`, description: 'Retention dry run', source: 'In-process', siteId: state().siteId, detectedOn: new Date().toISOString().slice(0, 10), severity: 1 });
    const number = (await admin.get<{ number: string }>(`/api/ncrs/${ncr.id}`)).number;
    await signIn(page, users.admin);
    await page.goto('/governance/retention');
    await page.getByRole('button', { name: 'New policy' }).click();
    const d = dialog(page);
    await d.getByLabel('Name', { exact: true }).fill(`Plant NCRs ${t}`);
    await d.getByLabel('Record type').click();
    await page.getByRole('option', { name: 'Nonconformance Report' }).click();
    await d.getByTestId('site-picker').click();
    await page.getByRole('option', { name: /Test Plant/ }).click();
    await d.getByLabel('Priority (1 = highest)').fill('2');
    await d.getByLabel('Retain for (years)').fill('12');
    await d.getByRole('button', { name: 'Create' }).click();
    const row = page.getByRole('row', { name: new RegExp(`Plant NCRs ${t}`) });
    await expect(row).toContainText('site Test Plant');
    await expect(row).toContainText('12');
    await page.getByRole('tab', { name: 'Dry run' }).click();
    await page.getByLabel('Site').click();
    await page.getByRole('option', { name: /Test Plant/ }).click();
    await page.getByRole('button', { name: 'Run dry run' }).click();
    await expect(page.getByTestId('retention-summary')).toContainText('evaluated');
    const rec = page.getByRole('row', { name: new RegExp(number) });
    await expect(rec).toContainText('Retained');
    await expect(rec).toContainText(`Plant NCRs ${t} v1`);
  });

  test('the content library pages through more than 1,000 files on the server', async ({ page }) => {
    test.setTimeout(150_000);
    const admin = await Api.as(users.admin);
    const tag = `cl${Date.now().toString(36)}`;
    const ncr = await admin.post<{ id: string }>('/api/ncrs', { title: `Library ${tag}`, description: 'Many files', source: 'In-process', siteId: state().siteId, detectedOn: new Date().toISOString().slice(0, 10), severity: 1 });
    // Bulk fixture straight into the test database (the API's upload path is covered elsewhere); same database serve-api.sh created.
    const env = { ...process.env, PGHOST: process.env.E2E_PGHOST ?? 'localhost', PGPORT: process.env.E2E_PGPORT ?? '5433', PGUSER: process.env.E2E_PGUSER ?? 'qgiq', PGPASSWORD: process.env.E2E_PGPASSWORD ?? 'qgiq_local_dev' };
    execFileSync('psql', ['-d', process.env.E2E_DB ?? 'qgiq_e2e', '-v', 'ON_ERROR_STOP=1', '-q', '-c', `
      INSERT INTO qms.attachments ("Id","OwnerType","OwnerId","SiteId","ViewPermission","FileName","ContentType","SizeBytes","Sha256","StorageKey","Classification","LineageId","Version","IsLocked","ScanStatus","IsArchived","CreatedAt","UpdatedAt")
      SELECT u.id, 'Ncr', '${ncr.id}', '${state().siteId}', 'ncr.view', '${tag}-' || lpad(u.g::text, 4, '0') || '.pdf', 'application/pdf', 100 + u.g, repeat('c', 64), 'e2e/${tag}/' || u.g, 0, u.id, 1, false, 1, false,
             TIMESTAMPTZ '2025-03-01 00:00:00+00' + u.g * INTERVAL '1 minute', now()
      FROM (SELECT g, gen_random_uuid() AS id FROM generate_series(1, 1100) g) u`], { env });

    await signIn(page, users.viewer);
    await page.goto('/documents/content');
    await page.getByPlaceholder('Search file names and descriptions').fill(tag);
    await expect(page.getByTestId('library-total')).toHaveText('1,100 files');
    await expect(page.getByTestId('library-page')).toHaveText('Page 1 of 22');
    await expect(page.getByText(`${tag}-1100.pdf`)).toBeVisible();
    for (let i = 1; i < 22; i++) await page.getByRole('button', { name: 'Next' }).click();
    await expect(page.getByTestId('library-page')).toHaveText('Page 22 of 22');
    await expect(page.getByText(`${tag}-0001.pdf`)).toBeVisible(); // file #1,100 counting from the newest
    await page.getByLabel('Sort').click();
    await page.getByRole('option', { name: 'Oldest' }).click();
    await expect(page.getByTestId('library-page')).toHaveText('Page 1 of 22');
    await expect(page.getByText(`${tag}-0001.pdf`)).toBeVisible();
  });
});
