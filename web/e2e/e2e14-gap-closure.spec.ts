import { execFileSync } from 'node:child_process';
import { test, expect, request } from '@playwright/test';
import { Api, WEB, choose, dialog, localDate, signIn, state, unique, users, type User, shot } from './support';

/** Gap-closure UI checks: shared site picker (LPA, cost of quality), friendly record type labels, document type library. */
test.describe('E2E 14: gap closure', () => {
  test('the shared site picker offers only my active sites in layered audit and cost of quality dialogs', async ({ page }) => {
    const admin = await Api.as(users.admin);
    const t = Date.now().toString(36);
    const other = await admin.post<string>('/api/organization/sites', { code: `OT${t}`.slice(0, 10).toUpperCase(), name: `Other Plant ${t}`, address: null, timeZone: 'America/New_York' });
    const role = await admin.post<string>('/api/admin/roles', { name: `LPA lead ${t}`, description: 'UI test', permissions: ['lpa.view', 'lpa.edit', 'coq.view', 'coq.edit'] });
    const u: User = { key: 'lpa', email: `lpa-${t}@qms.test`, password: 'lpa-lead-password-1', name: `Lee Layered ${t}` };
    const uid = await admin.post<string>('/api/admin/users', { email: u.email, displayName: u.name, password: u.password, mustChangePassword: false });
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
    await shot(page, '01-lpa-site-picker');
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
    await shot(page, '02-coq-site-picker');
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
    await shot(page, '20-friendly-record-type-labels');
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
    await shot(page, '18-document-type-library');
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
    await shot(page, '07-denominator-production-volumes');
  });

  test('retention policies take filters and priority, and the dry run explains which policy governs each record', async ({ page }) => {
    const admin = await Api.as(users.admin);
    const t = Date.now().toString(36);
    const ncr = await admin.post<{ id: string; number: string }>('/api/ncrs', { title: `Retention ${t}`, description: 'Retention dry run', source: 'In-process', siteId: state().siteId, detectedOn: localDate(), severity: 1 });
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
    await shot(page, '17-retention-dry-run');
  });

  test('the content library pages through more than 1,000 files on the server', async ({ page }) => {
    test.setTimeout(150_000);
    const admin = await Api.as(users.admin);
    const tag = `cl${Date.now().toString(36)}`;
    const ncr = await admin.post<{ id: string }>('/api/ncrs', { title: `Library ${tag}`, description: 'Many files', source: 'In-process', siteId: state().siteId, detectedOn: localDate(), severity: 1 });
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
    await shot(page, '21-content-library-paging');
  });

  test('compliance mapping content shows its verification state and unverified packs never read as compliance', async ({ page }) => {
    await signIn(page, users.admin);
    await page.goto('/compliance');
    const iatf = page.getByTestId('pack').filter({ hasText: 'IATF 16949' });
    await expect(iatf.getByTestId('verification-chip')).toHaveText(/not verified/);
    await page.getByRole('tab', { name: 'Coverage' }).click();
    await expect(page.getByTestId('coverage-disclaimer')).toContainText('not a statement or guarantee');
    await page.getByRole('tab', { name: 'Content verification' }).click();
    const ppap = page.getByRole('row').filter({ hasText: 'PPAP submission level matrix' });
    await expect(ppap.getByTestId('mapping-state')).toHaveText('Verified');
    await expect(ppap).toContainText('Quality-One');
    for (const name of ['IATF 16949', 'AS9100']) await expect(page.getByRole('row').filter({ hasText: name }).first().getByTestId('mapping-state')).toHaveText('Unverified');
    await ppap.getByRole('button', { name: 'History' }).click();
    await expect(page.getByTestId('mapping-history')).toContainText('Verified');
    await shot(page, '24-compliance-mapping-verification');
  });

  test('a connection shows its live-validation gates, checklist and unverified key assumptions, and the gate decides enabling', async ({ page }) => {
    const api = await Api.as(users.author);
    const base = { company: null, clientId: null, username: null, options: {}, maxConcurrency: null, requestsPerMinute: null, inventorySystemOfRecord: false, adminNotes: null, siteId: state().siteId, departmentId: null };
    const plex = await api.post<{ id: string }>('/api/integrations', { ...base, name: unique('Plex gates'), system: 7, edition: 'Connect', baseUrl: 'https://connect.plex.example', account: 'tenant-1' });
    await signIn(page, users.author);
    await page.goto(`/integrations/${plex.id}?tab=validation`);
    await expect(page.getByTestId('erp-gate-summary')).toContainText('Cannot be enabled yet');
    const paths = page.getByTestId('erp-assumption').filter({ hasText: 'Paths' }).first();
    await expect(paths).toContainText('Key — needed for production');
    await expect(paths).toContainText('Unverified');
    await expect(page.getByText(/Plex developer portal\./)).toBeVisible();
    await expect(page.getByTestId('erp-gate').filter({ hasText: 'Write' })).toContainText('Not run');

    const csv = await api.post<{ id: string }>('/api/integrations', { ...base, name: unique('CSV gates'), system: 21, edition: 'Csv', baseUrl: null, account: null });
    await page.goto(`/integrations/${csv.id}?tab=validation`);
    await expect(page.getByTestId('erp-gate-summary')).toContainText('Run Test connection once');
    await page.getByRole('button', { name: 'Test connection' }).click();
    await expect(page.getByTestId('erp-gate-summary')).toContainText('passes for the current configuration');
    await expect(page.getByTestId('erp-gate').filter({ hasText: 'Connection' })).toContainText('Not applicable');
    await page.getByLabel('Enabled', { exact: true }).click();
    await expect(page.getByText('Enabled', { exact: true })).toBeVisible();
    await shot(page, '25-erp-live-validation');
  });

  test('the marketplace registers publisher public keys only and shows that signing is refused without a production key', async ({ page }) => {
    const { generateKeyPairSync } = await import('node:crypto');
    const pair = generateKeyPairSync('ec', { namedCurve: 'P-256' });
    const pub = pair.publicKey.export({ type: 'spki', format: 'pem' }).toString();
    const priv = pair.privateKey.export({ type: 'pkcs8', format: 'pem' }).toString();
    const kid = `pub-e2e-${Date.now().toString(36)}`;
    await signIn(page, users.admin);
    await page.goto('/platform/marketplace');
    const panel = page.getByTestId('licensing-keys');
    await expect(panel).toContainText('no production signing key is configured');
    await expect(panel).toContainText('held offline by the publisher');
    await panel.getByRole('button', { name: 'Register public key' }).click();
    const d = dialog(page);
    await d.getByLabel('Key id').fill(kid);
    await d.getByLabel('Public key (PEM)').fill(priv);
    await d.getByRole('button', { name: 'Register' }).click();
    await expect(d.getByRole('alert')).toContainText('That is a private key');
    await d.getByLabel('Public key (PEM)').fill(pub);
    await d.getByRole('button', { name: 'Register' }).click();
    await expect(d).toHaveCount(0);
    const row = panel.getByTestId('licensing-key').filter({ hasText: kid });
    await expect(row).toContainText('Production');
    await expect(row).toContainText('Active');
    await row.getByRole('button', { name: 'Retire' }).click();
    await expect(row).toContainText('Retired');
    await expect(page.getByText(priv.split('\n')[1])).toHaveCount(0);
    await shot(page, '26-licence-verification-keys');
  });

  test('an MES device is registered with scopes, sites and expiry, its secret shows once, and rotation and revocation take effect', async ({ page }) => {
    const admin = await Api.as(users.admin);
    const t = Date.now().toString(36);
    const role = await admin.post<string>('/api/admin/roles', { name: `MES service ${t}`, description: 'UI test', permissions: ['production.edit', 'production.view'] });
    const svc = await admin.post<string>('/api/admin/users', { email: `mes-${t}@qms.test`, displayName: `MES Line ${t}`, password: 'mes-service-password-1', mustChangePassword: false });
    await admin.post(`/api/admin/users/${svc}/roles`, { roleId: role, siteId: null, departmentId: null });

    await signIn(page, users.admin);
    await page.goto('/governance/api');
    await page.getByRole('tab', { name: 'MES devices' }).click();
    await expect(page.getByTestId('mes-auth-status')).toContainText('until Mes:RequireDeviceClients is set to true');
    await page.getByRole('button', { name: 'Register MES device' }).click();
    const d = dialog(page);
    await d.getByLabel('Name').fill(`Press line PLC ${t}`);
    await choose(page, 'Service user', `MES Line ${t}`);
    await d.getByRole('button', { name: 'Create' }).click();
    await expect(d.getByRole('alert')).toContainText('restricted to one or more sites');
    await d.getByRole('combobox', { name: 'Sites (required)' }).click();
    await page.getByRole('option', { name: /Test Plant/ }).click();
    await page.keyboard.press('Escape');
    await d.getByLabel(/Expires on/).fill(localDate(180));
    await d.getByRole('button', { name: 'Create' }).click();
    await expect(d).toHaveCount(0);
    const shown = page.getByTestId('client-secret');
    await expect(shown).toContainText('shown once');
    const apiKey = (await shown.locator('b').nth(1).textContent())!.trim();
    expect(apiKey).toMatch(/^qgm_[0-9a-f]{16}\./);

    const device = await request.newContext({ baseURL: WEB, extraHTTPHeaders: { 'X-Api-Key': apiKey } });
    const ev = (id: string, siteId = state().siteId) => ({ data: { source: `PLC-${t}`, eventId: id, kind: 'WorkOrder', siteId, payload: { number: `WO-${t}-${id}`, item: `P-${t}`, quantity: 5 } } });
    expect((await device.post('/api/mes/events', ev('1'))).status()).toBe(200);
    expect((await device.get('/api/ncrs')).status()).toBe(403);
    await page.reload();
    await page.getByRole('tab', { name: 'MES devices' }).click();
    const row = page.getByRole('row').filter({ hasText: `Press line PLC ${t}` });
    await expect(row).toContainText('production.edit');
    await expect(row).toContainText('POST /api/mes/events');
    await expect(row).toContainText('Active');
    await expect(row).not.toContainText('Never');
    await shot(page, '08-mes-devices-active');
    await expect(page.getByText(apiKey.split('.')[1])).toHaveCount(0);

    await row.getByRole('button', { name: 'Rotate' }).click();
    await dialog(page).getByLabel(/Grace period/).fill('30');
    await dialog(page).getByRole('button', { name: 'Rotate' }).click();
    await expect(row).toContainText('old secret until');
    expect((await device.post('/api/mes/events', ev('2'))).status()).toBe(200);

    await row.getByRole('button', { name: 'Revoke' }).click();
    await dialog(page).getByLabel('Reason').fill('Controller replaced');
    await dialog(page).getByRole('button', { name: 'Revoke permanently' }).click();
    await expect(row).toContainText('Revoked');
    await expect(row).toContainText('Controller replaced');
    await expect(row.getByRole('button', { name: 'Enable' })).toHaveCount(0);
    expect((await device.post('/api/mes/events', ev('3'))).status()).toBe(401);
    await device.dispose();
    await shot(page, '08-mes-devices-revoked');
  });

  test('PFMEA lines import from a spreadsheet with a mapping preview, row errors, recalculated RPN and the rejected rows left out', async ({ page }) => {
    const admin = await Api.as(users.admin);
    const t = Date.now().toString(36);
    const pf = await admin.post<{ id: string; number: string }>('/api/apqp/pfmea', { title: `Import PFMEA ${t}`, siteId: state().siteId });
    const csv = ['Proc No,Process function,Failure mode,Failure effect,S,Failure cause,O,D,RPN',
      '10,Press bearing,Bearing too shallow,Noise at end of line,7,Press stop worn,4,3,999',
      '20,Torque bolts,Bolt under-torqued,Cover leak,12,Tool not calibrated,5,6,',
      '30,Leak test,Leak not detected,Field leak,6,Test pressure low,3,4,'].join('\n');
    await signIn(page, users.admin);
    await page.goto(`/apqp/pfmea/${pf.id}`);
    await page.getByRole('button', { name: 'Import lines' }).click();
    const d = dialog(page);
    await d.getByTestId('import-file').setInputFiles({ name: 'line-3-pfmea.csv', mimeType: 'text/csv', buffer: Buffer.from(csv) });
    await d.getByRole('button', { name: 'Preview' }).click();
    // "Proc No" is not a recognised step column: the required Step field is unmapped until it is chosen.
    await expect(d.getByText('Map the required columns: Step.')).toBeVisible();
    await d.getByRole('combobox', { name: 'Step *' }).click();
    await page.getByRole('option', { name: 'Proc No', exact: true }).click();
    const summary = d.getByTestId('import-summary');
    await expect(summary).toContainText('3 rows');
    await expect(summary).toContainText('2 ready');
    await expect(summary).toContainText('1 rejected');
    const rows = d.getByTestId('import-rows');
    await expect(rows).toContainText('Severity must be between 1 and 10');
    await expect(rows).toContainText('RPN in the file (999) replaced by the calculated 84');
    await expect(d.getByRole('button', { name: 'Import 2 lines' })).toBeDisabled();
    await d.getByLabel(/Import the 2 valid rows/).check();
    await d.getByRole('button', { name: 'Import 2 lines' }).click();
    await expect(d).toHaveCount(0);
    await expect(page.getByTestId('import-done')).toContainText(`2 line(s) imported into ${pf.number} rev 1. 1 rejected row(s) left out.`);
    await expect(page.locator('input[value="Bearing too shallow"]')).toHaveCount(1); // the draft's line editor shows the imported line
    const lines = await admin.get<{ failureMode: string; rpn: number }[]>(`/api/apqp/pfmea/${pf.id}/lines`);
    expect(lines.map((l) => l.rpn).sort()).toEqual([72, 84]);
    await expect(page.getByRole('row').filter({ hasText: 'line-3-pfmea.csv' }).first()).toContainText('2 ok · 1 rejected');
    await shot(page, '03-pfmea-import');
  });
  test('control plan lines import with PFMEA links, row errors and the new characteristic columns in the editor', async ({ page }) => {
    const admin = await Api.as(users.admin);
    const t = Date.now().toString(36);
    const pf = await admin.post<{ id: string }>('/api/apqp/pfmea', { title: `CP link PFMEA ${t}`, siteId: state().siteId });
    await admin.put(`/api/apqp/pfmea/${pf.id}/lines`, [{ stepSequence: 20, processFunction: 'Bore', failureMode: 'Bore oversize', failureEffect: 'Leak', failureCause: 'Tool wear', preventionControls: null, detectionControls: null,
      severity: 6, occurrence: 3, detection: 3, recommendedAction: null, responsibleId: null, dueOn: null, specialCharacteristic: null, completedOn: null, actionTaken: null, revisedSeverity: null, revisedOccurrence: null, revisedDetection: null }]);
    const cp = await admin.post<{ id: string; number: string }>('/api/apqp/plans', { title: `Import plan ${t}`, siteId: state().siteId, phase: 2, pfmeaId: pf.id });
    const csv = ['Step,Process step,Operation,Product characteristic,Process characteristic,Specification,Tolerance,Measurement method,Inspection equipment,Sample size,Frequency,Reaction plan,Related PFMEA item',
      '20,Bore,Op 20A,Bore diameter,Spindle speed,25.00,±0.03,Air gauge,AG-114,5,Hourly,Stop line and quarantine,Bore oversize',
      '30,Wash,,,Nozzle pressure,3 bar,,Pressure gauge,PG-2,1,Each shift,Clean nozzle,',
      '40,Deburr,,Edge break,,,,Visual,,5,Hourly,Rework,Not in the PFMEA'].join('\n');
    await signIn(page, users.admin);
    await page.goto(`/apqp/plans/${cp.id}`);
    await page.getByRole('button', { name: 'Import lines' }).click();
    const d = dialog(page);
    await d.getByTestId('import-file').setInputFiles({ name: 'cp-lines.csv', mimeType: 'text/csv', buffer: Buffer.from(csv) });
    await d.getByRole('button', { name: 'Preview' }).click();
    const summary = d.getByTestId('import-summary');
    await expect(summary).toContainText('3 rows');
    await expect(summary).toContainText('2 ready');
    await expect(d.getByTestId('import-rows')).toContainText('PFMEA item "Not in the PFMEA" is not a failure mode of the linked PFMEA');
    await d.getByLabel(/Import the 2 valid rows/).check();
    await d.getByRole('button', { name: 'Import 2 lines' }).click();
    await expect(d).toHaveCount(0);
    await expect(page.getByTestId('import-done')).toContainText(`2 line(s) imported into ${cp.number} rev 1. 1 rejected row(s) left out.`);
    await expect(page.locator('input[value="AG-114"]')).toHaveCount(1);
    await expect(page.locator('input[value="Spindle speed"]')).toHaveCount(1);
    const lines = await admin.get<{ stepSequence: number; characteristicType: string; pfmeaLineId: string | null }[]>(`/api/apqp/plans/${cp.id}/lines`);
    expect(lines.map((l) => `${l.stepSequence}:${l.characteristicType}:${l.pfmeaLineId ? 'linked' : '-'}`)).toEqual(['20:Product:linked', '30:Process:-']);
    await shot(page, '04-control-plan-import');
  });
});
