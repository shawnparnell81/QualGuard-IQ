import { test, expect, type Page } from '@playwright/test';
import { Api, choose, dialog, shot, signIn, state, unique, users } from './support';

/** Forms build B12: quality operations (TECH 13, FI phase 6) used through the pages. */
const site = async (page: Page) => {
  await page.getByRole('combobox', { name: 'Site' }).click();
  await page.getByRole('option', { name: /^E2E — / }).click();
};

test.describe('Forms B12: quality operations', () => {
  test('event intake, triage with the severity matrix, escalation to a deviation approved by another person', async ({ page }) => {
    test.setTimeout(180_000);
    const { siteId } = state();
    const api = await Api.as(users.author);
    const t = Date.now().toString(36).toUpperCase();
    const { id: partId } = await api.post<{ id: string }>('/api/master/parts', { partNumber: `QO-${t}`, name: 'Gasket housing', partType: 0, revision: 'A', unitOfMeasure: 'EA', siteId, status: 0 });

    await signIn(page, users.author);
    await page.goto('/quality-ops');
    await expect(page.getByRole('heading', { name: 'Quality operations' })).toBeVisible();
    await expect(page.getByText('Open quality events')).toBeVisible();
    await shot(page, 'forms-import/b12-quality-ops-overview');

    await page.getByRole('link', { name: 'Report event' }).first().click();
    const title = unique('Gasket leak at end-of-line test');
    await page.getByLabel('What happened').fill(title);
    await page.getByLabel('Description').fill('Two housings leaked at the helium test station.');
    await choose(page, 'Category', 'Product');
    await choose(page, 'Part (optional)', new RegExp(`QO-${t}`));
    await page.getByLabel('Lot or batch').fill(`LOT-${t}`);
    await site(page);
    await page.getByRole('button', { name: 'Report event' }).click();
    await expect(page).toHaveURL(/\/quality-ops\/events\/[0-9a-f-]{36}$/);
    await expect(page.getByRole('heading', { name: new RegExp(title) })).toBeVisible();

    await page.getByRole('button', { name: 'Start triage' }).click();
    await page.getByRole('button', { name: 'Classify and rate' }).click();
    const cls = dialog(page);
    await cls.getByRole('combobox', { name: 'Classification' }).click();
    await page.getByRole('option', { name: 'Nonconformance' }).click();
    await cls.getByRole('combobox', { name: 'Likelihood' }).click();
    await page.getByRole('option', { name: '4 · Likely' }).click();
    await cls.getByRole('combobox', { name: 'Impact' }).click();
    await page.getByRole('option', { name: '4 · Major' }).click();
    await cls.getByLabel('Triage notes').fill('Seal compound batch suspected.');
    await cls.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByText('Critical', { exact: true })).toBeVisible(); // 4 x 4 = 16 lands in Critical
    await shot(page, 'forms-import/b12-event-classified');

    await page.getByRole('button', { name: 'Escalate' }).click();
    await dialog(page).getByRole('button', { name: 'Escalate' }).click();
    await expect(page).toHaveURL(/\/quality-ops\/deviations\/[0-9a-f-]{36}$/);
    const devHeading = page.getByRole('heading', { name: /DEV-/ });
    await expect(devHeading).toBeVisible();
    const devNumber = ((await devHeading.textContent()) ?? '').match(/DEV-[A-Z0-9-]*\d/)![0];

    // unplanned deviations need a risk assessment and containment before submission
    await page.getByRole('button', { name: 'Submit for approval' }).click();
    await expect(page.getByText('Complete the risk assessment before submitting.')).toBeVisible();
    await page.getByRole('button', { name: 'Edit' }).click();
    await page.getByLabel('Risk assessment').fill('Leak path confined to the seal; no safety function affected.');
    await page.getByLabel('Containment actions (unplanned)').fill('Lot held, 100% leak test on the remainder.');
    await page.getByRole('button', { name: 'Save' }).click();
    await page.getByRole('button', { name: 'Submit for approval' }).click();
    await expect(page.getByText('Waiting for approval (see Approvals in My work).')).toBeVisible();

    const approver = await Api.as(users.approver);
    await approver.sign(devNumber, users.approver.password);
    await page.reload();
    await expect(page.getByText('This deviation is in force.')).toBeVisible();
    await page.getByRole('tab', { name: 'Approval' }).click();
    await expect(page.getByText(users.approver.name).first()).toBeVisible();
    await shot(page, 'forms-import/b12-deviation-approved');

    // the event shows where it went
    await page.goto('/quality-ops/events?view=all');
    await page.getByRole('row', { name: new RegExp(title) }).click();
    await expect(page.getByText(`Escalated to ${devNumber}.`)).toBeVisible();
    await api.dispose(); await approver.dispose();
  });

  test('laboratory investigation, batch release blocked then signed, register entry, skip-lot, review and analytics', async ({ page }) => {
    test.setTimeout(240_000);
    const { siteId } = state();
    const api = await Api.as(users.author);
    const t = Date.now().toString(36).toUpperCase();
    const { id: partId } = await api.post<{ id: string }>('/api/master/parts', { partNumber: `QR-${t}`, name: 'Bracket', partType: 0, revision: 'B', unitOfMeasure: 'EA', siteId, status: 0 });
    const lot = `RL-${t}`;
    const { id: eventId } = await api.post<{ id: string }>('/api/quality-ops/events', { siteId, title: `Burr on bracket ${t}`, description: 'Found at packing', category: 'Product', partId, lotNumber: lot });

    await signIn(page, users.author);
    // laboratory investigation with an out-of-specification result
    await page.goto('/quality-ops/investigations/new');
    await choose(page, 'Type', 'Laboratory');
    await page.getByLabel('Title').fill(`Hardness high ${t}`);
    await page.getByLabel('Scope').fill('Batch hardness test above the drawing limit.');
    await site(page);
    await page.getByRole('button', { name: 'Open investigation' }).click();
    await expect(page.getByRole('heading', { name: /INV-/ })).toBeVisible();
    await page.getByRole('button', { name: 'Record work' }).click();
    const w = dialog(page);
    await w.getByLabel('Findings').fill('Retest on a second specimen confirmed the result.');
    await w.getByLabel('Sample reference').fill('S-44');
    await w.getByLabel('Test method').fill('Rockwell C');
    await w.getByLabel('Lower limit').fill('28');
    await w.getByLabel('Upper limit').fill('32');
    await w.getByLabel('Result', { exact: true }).fill('34.5');
    await w.getByLabel('Unit').fill('HRC');
    await w.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByText(/Out of specification: 34.5 HRC against 28 to 32/)).toBeVisible();
    await page.getByRole('button', { name: 'Link record' }).click();
    await dialog(page).getByLabel('Find a record by number or title').fill(`Burr on bracket ${t}`);
    await page.getByRole('option', { name: new RegExp(`Burr on bracket ${t}`) }).click();
    await dialog(page).getByRole('button', { name: 'Link' }).click();
    await expect(page.getByText('Linked records (1)')).toBeVisible();
    await shot(page, 'forms-import/b12-lab-investigation');

    // batch release is blocked while a quality event on the lot is open
    await page.goto('/quality-ops/releases/new');
    await choose(page, /^Part$/, new RegExp(`QR-${t}`));
    await page.getByLabel('Lot or batch').fill(lot);
    await page.getByLabel('Quantity').fill('1200');
    await site(page);
    await page.getByRole('button', { name: 'Request release' }).click();
    await expect(page.getByRole('heading', { name: /REL-/ })).toBeVisible();
    const releaseUrl = page.url();
    await expect(page.getByText('Release is blocked until every check passes.')).toBeVisible();
    await page.goto('/quality-ops/releases');
    await expect(page.getByText('Blocked').first()).toBeVisible();
    await shot(page, 'forms-import/b12-release-board');
    await api.post(`/api/quality-ops/events/${eventId}/triage`);
    await api.post(`/api/quality-ops/events/${eventId}/close`, { notes: 'Burr removed; cosmetic only' });

    // the approver signs the release
    await page.context().clearCookies();
    await signIn(page, users.approver);
    await page.goto(releaseUrl);
    await expect(page.getByText('Release is blocked until every check passes.')).toHaveCount(0);
    await page.getByRole('button', { name: 'Release', exact: true }).click();
    const sig = dialog(page);
    await sig.getByLabel('Password', { exact: true }).fill(users.approver.password);
    await sig.getByLabel('Comment').fill('All checks clear');
    await sig.getByRole('button', { name: 'Sign and release' }).click();
    await expect(page.getByText(/Batch released by Pat Approver .* Signature verified\./)).toBeVisible();
    await shot(page, 'forms-import/b12-release-signed');

    // register entry follows its governed status transitions
    await page.context().clearCookies();
    await signIn(page, users.author);
    await page.goto('/quality-ops/registers');
    await page.getByRole('link', { name: /Regulatory intelligence/ }).click();
    await page.getByRole('link', { name: 'Add entry' }).click();
    await page.getByLabel('Title *').fill(`Revised flammability standard ${t}`);
    await page.getByLabel('Source or authority *').fill('Standards body');
    await page.getByLabel('Reference or citation *').fill('Edition 2026');
    await choose(page, 'Impact *', 'Medium');
    await page.getByLabel('Site').click();
    await page.getByRole('option', { name: /Test Plant \(E2E\)/ }).click();
    await page.getByRole('button', { name: 'Add entry' }).click();
    await expect(page.getByRole('heading', { name: /REG-/ })).toBeVisible();
    await expect(page.getByText('Identified').first()).toBeVisible();
    await page.getByRole('button', { name: 'Under assessment' }).click();
    await dialog(page).getByLabel('Comment').fill('Assessing the affected products');
    await dialog(page).getByRole('button', { name: 'Under assessment' }).click();
    await expect(page.getByRole('button', { name: 'Action required' })).toBeVisible();
    await shot(page, 'forms-import/b12-register-entry');

    // skip-lot: qualify after two accepted lots, then one lot in three is inspected
    await page.goto('/quality-ops/skip-lot');
    await page.getByRole('button', { name: 'Add rule' }).click();
    const r = dialog(page);
    await r.getByLabel('Site').click();
    await page.getByRole('option', { name: /Test Plant \(E2E\)/ }).click();
    await r.getByRole('combobox', { name: 'Part' }).click();
    await page.getByRole('option', { name: new RegExp(`QR-${t}`) }).click();
    await r.getByLabel('Qualify after (consecutive accepted lots)').fill('2');
    await r.getByLabel('Then inspect one lot in').fill('3');
    await r.getByRole('button', { name: 'Add rule' }).click();
    const row = page.getByRole('row', { name: new RegExp(`QR-${t}`) });
    for (const n of [1, 2]) {
      await row.getByRole('button', { name: 'Record lot' }).click();
      await expect(dialog(page).getByText(/Inspect this lot/)).toBeVisible();
      await dialog(page).getByLabel('Lot').fill(`SL-${n}`);
      await dialog(page).getByRole('button', { name: 'Record' }).click();
      await expect(dialog(page)).toHaveCount(0);
    }
    await expect(row.getByText('Skipping')).toBeVisible();
    await row.getByRole('button', { name: 'Record lot' }).click();
    await expect(dialog(page).getByText(/This lot may be skipped/)).toBeVisible();
    await dialog(page).getByRole('button', { name: 'Cancel' }).click();
    await row.getByRole('button', { name: 'History' }).click();
    await expect(page.getByText('Qualifying → Skipping')).toBeVisible();
    await shot(page, 'forms-import/b12-skip-lot');

    // product quality review gathers its metrics
    await page.goto('/quality-ops/reviews/new');
    await page.getByLabel('Title').fill(`Bracket product review ${t}`);
    await choose(page, /^Part$/, new RegExp(`QR-${t}`));
    await site(page);
    await page.getByRole('button', { name: 'Create review' }).click();
    await expect(page.getByRole('heading', { name: /PQR-/ })).toBeVisible();
    await expect(page.getByText('Metrics as of')).toBeVisible();
    await shot(page, 'forms-import/b12-quality-review');

    // analytics: readiness scores, cycle time, aging and bottlenecks
    await page.goto('/quality-ops/analytics');
    await expect(page.getByText('Quality readiness')).toBeVisible();
    await expect(page.getByText('Audit readiness')).toBeVisible();
    await expect(page.getByText('Cycle time (closed in the period)')).toBeVisible();
    await shot(page, 'forms-import/b12-analytics');
    await api.dispose();
  });

  test('approval delegation, service levels, mock recall exercise and severity settings', async ({ page }) => {
    test.setTimeout(150_000);
    const t = Date.now().toString(36).toUpperCase();
    await signIn(page, users.approver);
    await page.goto('/quality-ops/delegations');
    await page.getByRole('button', { name: 'Delegate approvals' }).click();
    const d = dialog(page);
    await d.getByLabel('Delegate').click();
    await page.getByRole('option', { name: users.author.name }).click();
    await page.keyboard.press('Escape');
    await d.getByLabel('Reason').fill('Leave');
    await d.getByRole('button', { name: 'Delegate' }).click();
    const drow = page.getByRole('row', { name: new RegExp(`${users.approver.name}.*${users.author.name}`) });
    await expect(drow.getByText('Active')).toBeVisible();
    await shot(page, 'forms-import/b12-delegation');
    await drow.getByRole('button', { name: 'Revoke' }).click();
    await expect(drow.getByText('Revoked')).toBeVisible();

    await page.context().clearCookies();
    await signIn(page, users.admin);
    await page.goto('/quality-ops/service-levels');
    await page.getByRole('button', { name: 'Add service level' }).click();
    const p = dialog(page);
    await p.getByLabel('Name', { exact: true }).fill(`Event triage ${t}`);
    await p.getByRole('combobox', { name: 'Module' }).click();
    await page.getByRole('option').first().click();
    await p.getByLabel('Target (hours)').fill('8');
    await p.getByRole('button', { name: 'Add' }).click();
    await expect(page.getByRole('cell', { name: `Event triage ${t}` })).toBeVisible();
    await page.getByRole('button', { name: 'Add calendar' }).click();
    const c = dialog(page);
    await c.getByLabel('Name', { exact: true }).fill(`Plant calendar ${t}`);
    await c.getByLabel('Holidays (one per line: YYYY-MM-DD name)').fill('2026-12-25 Christmas Day');
    await c.getByRole('button', { name: 'Add' }).click();
    await expect(page.getByRole('row', { name: new RegExp(`Plant calendar ${t}`) }).getByText(/Christmas Day/)).toBeVisible();
    await shot(page, 'forms-import/b12-service-levels');

    await page.goto('/quality-ops/settings');
    await expect(page.getByText('Severity matrix (likelihood × impact)')).toBeVisible();
    await shot(page, 'forms-import/b12-severity-settings');

    // mock recall exercise
    const author = await Api.as(users.author);
    await author.post('/api/master/parts', { partNumber: `QM-${t}`, name: 'Valve', partType: 0, revision: 'A', unitOfMeasure: 'EA', siteId: state().siteId, status: 0 });
    await author.dispose();
    await page.context().clearCookies();
    await signIn(page, users.author);
    await page.goto('/field/recalls/new');
    await page.getByLabel('Title').fill(`Mock recall ${t}`);
    await page.getByLabel('Reason').fill('Quarterly traceability exercise');
    await choose(page, 'Site', /^E2E — /);
    await page.getByLabel('Mock recall exercise').check();
    await choose(page, 'Item', new RegExp(`Part QM-${t}`));
    await page.getByRole('button', { name: 'Open recall' }).click();
    await expect(page.getByLabel('Mock recall result')).toBeVisible();
    await expect(page.getByLabel('Mock recall result').getByText('Not yet traced')).toBeVisible();
    await page.getByRole('button', { name: 'Trace lots' }).click();
    await expect(page.getByLabel('Mock recall result').getByText(/\d+(\.\d+)? min/)).toBeVisible();
    await shot(page, 'forms-import/b12-mock-recall');
  });
});
