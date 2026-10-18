import { test, expect } from '@playwright/test';
import { Api, localDate, shot, signIn, state, unique, users } from './support';

/** SEQUFLOH S5: tracking records created from a released instruction revision, results, signoffs, review and tracking folders (Prompt 2 sections 7-11, 17). */
test.describe('SEQUFLOH tracking records', () => {
  test('a validation record runs from a released revision through results, signoff and Quality approval', async ({ page, browser }) => {
    test.setTimeout(180_000);
    const author = await Api.as(users.author);
    const approver = await Api.as(users.approver);
    const siteId = state().siteId;
    const tag = unique('Trk').replace(' ', '-');
    const title = `Seal validation ${tag}`;
    const wi = await author.call('POST', '/api/sequfloh/instructions', { type: 'Validation', title, siteId }) as { id: string; number: string };
    await author.call('PUT', `/api/sequfloh/instructions/${wi.id}`, { title, siteId, content: { validationAcceptance: { overallAcceptanceCriteria: 'No leak at 150 psi' } },
      steps: [{ sequence: 10, title: 'Pressurize to 150 psi', lowerLimit: 145, upperLimit: 155, unit: 'psi', actualResultRequired: true }, { sequence: 20, title: 'Inspect for leaks' }], characteristics: [], partsRows: [] });
    await author.call('POST', `/api/sequfloh/instructions/${wi.id}/actions/submit-review`, {});
    for (const a of ['submit-approval', 'approve']) await approver.call('POST', `/api/sequfloh/instructions/${wi.id}/actions/${a}`, {});
    await approver.call('POST', `/api/sequfloh/instructions/${wi.id}/actions/release`, { effectiveDate: localDate() });

    await signIn(page, users.author);
    await page.goto('/sequfloh/tracking/validation');
    await page.getByRole('link', { name: 'New Validation record' }).click();
    await expect(page).toHaveURL(/\/sequfloh\/tracking\/new\?type=Validation/);
    await page.getByRole('combobox', { name: 'Released work instruction' }).fill(tag);
    await page.getByRole('option', { name: new RegExp(`${wi.number} rev A`) }).click();
    await page.getByLabel('Lot number').fill(`LOT-${tag}`);
    await shot(page, 'sequfloh/s5-tracking-new');
    await page.getByRole('button', { name: 'Create record' }).click();
    await expect(page).toHaveURL(/\/sequfloh\/record\//);
    const recordUrl = page.url();
    await expect(page.getByText('Not Started').first()).toBeVisible();
    await expect(page.getByText(`${wi.number}`).first()).toBeVisible();

    await page.getByRole('button', { name: 'Start', exact: true }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Start', exact: true }).click();
    await expect(page.getByText('Start: done.')).toBeVisible();
    await expect(page.getByText('Step 10: record the result.')).toBeVisible();
    await page.getByRole('tab', { name: 'Steps' }).click();
    const rows = page.getByRole('region', { name: 'Steps' }).getByRole('row');
    await rows.nth(1).getByRole('combobox', { name: 'Result' }).click();
    await page.getByRole('option', { name: 'Pass', exact: true }).click();
    await rows.nth(1).getByLabel('Actual result').fill('150');
    await rows.nth(2).getByRole('combobox', { name: 'Result' }).click();
    await page.getByRole('option', { name: 'Pass', exact: true }).click();
    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(page.getByText('Saved.')).toBeVisible();
    await shot(page, 'sequfloh/s5-tracking-steps');
    await page.getByRole('tab', { name: 'Review and signoffs' }).click();
    await page.getByRole('button', { name: 'Sign as technician' }).click();
    await expect(page.getByText('Technician signoff captured.')).toBeVisible();
    await page.getByRole('button', { name: 'Submit for review' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Submit for review' }).click();
    await expect(page.getByText('Pending Review').first()).toBeVisible();
    await expect(page.getByRole('button', { name: 'Approve' })).toHaveCount(0); // the performer cannot approve

    const qctx = await browser.newContext();
    const qp = await qctx.newPage();
    await signIn(qp, users.approver);
    await qp.goto(recordUrl);
    await qp.getByRole('button', { name: 'Approve', exact: true }).click();
    await qp.getByRole('dialog').getByRole('button', { name: 'Approve', exact: true }).click();
    await expect(qp.getByText('Approve: done.')).toBeVisible();
    await expect(qp.getByText('Completed').first()).toBeVisible();
    await qp.getByRole('tab', { name: 'Record' }).click();
    await shot(qp, 'sequfloh/s5-tracking-record');
    await qp.getByRole('tab', { name: 'Task events' }).click();
    for (const e of ['Start', 'Record Update', 'Signoff', 'Review Submission', 'Approval']) await expect(qp.getByRole('cell', { name: e, exact: true }).first()).toBeVisible();
    await shot(qp, 'sequfloh/s5-tracking-events');
    await qctx.close();

    // tracking folders: completed shows it, open does not; filters and clear filters
    await page.goto(`/sequfloh/tracking/completed?q=LOT-${tag}`);
    await expect(page.getByRole('cell', { name: /VAL-T-/ })).toBeVisible();
    await page.goto(`/sequfloh/tracking/open?q=LOT-${tag}`);
    await expect(page.getByText('No tracking records match these filters.')).toBeVisible();
    const prod = await author.call('POST', '/api/sequfloh/tracking', { type: 'Production', workInstructionId: wi.id, siteId, workOrder: `WO-${tag}`, priority: 'High' }) as { number: string };
    await page.goto(`/sequfloh/tracking/production?q=WO-${tag}`);
    await expect(page.getByRole('cell', { name: prod.number, exact: true })).toBeVisible();
    for (const h of ['Tracking number', 'Work instruction', 'Work order', 'Operation', 'Assigned', 'Status', 'Result', 'Priority', 'Due', 'Elapsed']) await expect(page.getByRole('columnheader', { name: h, exact: true })).toBeVisible();
    await page.getByRole('combobox', { name: 'Priority' }).click();
    await page.getByRole('option', { name: 'Low', exact: true }).click();
    await expect(page.getByText('No tracking records match these filters.')).toBeVisible();
    await page.getByRole('button', { name: 'Clear filters' }).click();
    await page.goto(`/sequfloh/tracking/production?q=WO-${tag}`);
    await shot(page, 'sequfloh/s5-tracking-folder');
  });
});
