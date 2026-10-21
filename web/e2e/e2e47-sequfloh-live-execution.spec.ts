import { test, expect } from '@playwright/test';
import { Api, localDate, shot, signIn, state, unique, users } from './support';

/** SEQUFLOH S6: Dispatch Queue, Active Station, Paused / Completed / Overdue views, Work History; timing survives a refresh (Prompt 2 sections 12-16). */
test.describe('SEQUFLOH live execution', () => {
  test('a task is dispatched, started, paused, refreshed, resumed and completed with persisted timing and task events', async ({ page, browser }) => {
    test.setTimeout(300_000);
    const author = await Api.as(users.author);
    const approver = await Api.as(users.approver);
    const siteId = state().siteId;
    const tag = unique('Live').replace(' ', '-');
    const title = `Final assembly ${tag}`;
    const wi = await author.call('POST', '/api/sequfloh/instructions', { type: 'Validation', title, siteId }) as { id: string; number: string };
    await author.call('PUT', `/api/sequfloh/instructions/${wi.id}`, { title, siteId, content: { validationAcceptance: { overallAcceptanceCriteria: 'Assembled to drawing' } },
      steps: [{ sequence: 10, title: 'Assemble housing' }], characteristics: [], partsRows: [] });
    await author.call('POST', `/api/sequfloh/instructions/${wi.id}/actions/submit-review`, {});
    for (const a of ['submit-approval', 'approve']) await approver.call('POST', `/api/sequfloh/instructions/${wi.id}/actions/${a}`, {});
    await approver.call('POST', `/api/sequfloh/instructions/${wi.id}/actions/release`, { effectiveDate: localDate() });
    const task = await author.call('POST', '/api/sequfloh/tracking', { type: 'Production', workInstructionId: wi.id, siteId, workOrder: `WO-${tag}`, operation: 'Final assembly',
      priority: 'High', dueAt: new Date(Date.now() - 3 * 3_600_000).toISOString() }) as { id: string; number: string };

    await signIn(page, users.author);
    await page.goto(`/sequfloh/execution/dispatch?q=WO-${tag}`);
    await expect(page.getByRole('cell', { name: task.number, exact: true })).toBeVisible();
    for (const h of ['Tracking number', 'Part', 'Operation', 'Instruction / revision', 'Assigned', 'Status', 'Priority', 'Due', 'Actions']) await expect(page.getByRole('columnheader', { name: h, exact: true })).toBeVisible();
    await page.getByRole('button', { name: `Dispatch ${task.number}` }).click();
    await page.getByRole('dialog').getByRole('combobox', { name: 'Assign to' }).click();
    await page.getByRole('option', { name: users.author.name, exact: true }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Dispatch', exact: true }).click();
    await expect(page.getByText(`${task.number}: dispatch done.`)).toBeVisible();
    await shot(page, 'sequfloh/s6-dispatch-queue');
    await page.getByRole('button', { name: `Start ${task.number}` }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Start', exact: true }).click();
    await expect(page.getByText(`${task.number}: start done.`)).toBeVisible();

    await page.goto(`/sequfloh/execution/active?task=${task.id}`);
    const station = page.getByRole('region', { name: 'Active task' });
    await expect(station.getByText('In Progress')).toBeVisible();
    await expect(page.getByLabel('Next required action')).toContainText('Record the result.');
    await expect(page.getByRole('region', { name: 'Quality checkpoints' })).toBeVisible();
    await page.getByRole('button', { name: 'Pause', exact: true }).click();
    await page.getByRole('dialog').getByLabel('Reason (required)').fill('Material shortage');
    await page.getByRole('dialog').getByRole('button', { name: 'Pause', exact: true }).click();
    await expect(page.getByText('Pause: done.')).toBeVisible();
    const before = await station.getByLabel('Elapsed time').textContent();
    await page.reload(); // refresh while paused: the stored timing is unchanged
    await expect(station.getByText('Paused', { exact: true })).toBeVisible();
    await expect(station.getByLabel('Elapsed time')).toHaveText(before!);

    await page.goto(`/sequfloh/execution/paused?q=WO-${tag}`);
    await expect(page.getByRole('cell', { name: 'Material shortage' })).toBeVisible();
    await shot(page, 'sequfloh/s6-paused-tasks');
    await page.getByRole('button', { name: `Resume ${task.number}` }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Resume', exact: true }).click();
    await expect(page.getByText(`${task.number}: resume done.`)).toBeVisible();
    await page.goto(`/sequfloh/execution/overdue?q=WO-${tag}`);
    await expect(page.getByRole('cell', { name: task.number, exact: true })).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'Overdue by' })).toBeVisible();

    await page.goto(`/sequfloh/execution/active?task=${task.id}`);
    await page.getByRole('combobox', { name: 'Result' }).click();
    await page.getByRole('option', { name: 'Pass', exact: true }).click();
    await page.getByLabel('Technician comments').fill('Assembled and torqued');
    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(page.getByText('Saved.')).toBeVisible();
    await page.getByRole('button', { name: 'Sign as technician' }).click();
    await expect(page.getByText('Technician signoff captured.')).toBeVisible();
    await expect(page.getByLabel('Next required action')).toContainText('Complete the task.');
    await shot(page, 'sequfloh/s6-active-station');
    await page.getByRole('button', { name: 'Complete', exact: true }).click();
    await page.getByRole('dialog').getByLabel(/Variance reason/).fill('Short run');
    await page.getByRole('dialog').getByRole('button', { name: 'Complete', exact: true }).click();
    await expect(page.getByText('Complete: done.')).toBeVisible();

    await page.goto(`/sequfloh/execution/completed?q=WO-${tag}`);
    await expect(page.getByRole('cell', { name: task.number, exact: true })).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'Variance' })).toBeVisible();
    await page.goto(`/sequfloh/execution/history?q=${task.number}`);
    for (const e of ['Dispatch', 'Start', 'Pause', 'Resume', 'Completion']) await expect(page.getByRole('cell', { name: e, exact: true }).first()).toBeVisible();
    await shot(page, 'sequfloh/s6-work-history');

    // a read-only user sees the queue but no task actions
    const vctx = await browser.newContext();
    const vp = await vctx.newPage();
    await signIn(vp, users.viewer);
    // on a busy machine the first load can land before the sign-in settles; reload until the row shows instead of waiting on one load
    await expect(async () => {
      await vp.goto(`/sequfloh/execution/completed?q=WO-${tag}`);
      await expect(vp.getByRole('cell', { name: task.number, exact: true })).toBeVisible({ timeout: 10_000 });
    }).toPass({ timeout: 90_000, intervals: [1_000, 2_000, 5_000] });
    await expect(vp.getByRole('button', { name: new RegExp(`(Restart|Reverse result) ${task.number}`) })).toHaveCount(0);
    await vctx.close();
  });
});
