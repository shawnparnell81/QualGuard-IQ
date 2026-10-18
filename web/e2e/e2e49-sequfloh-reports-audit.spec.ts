import { test, expect } from '@playwright/test';
import { Api, localDate, shot, signIn, state, unique, users } from './support';

/** SEQUFLOH S8: reports, Excel export from the Export Center, audit trail views, tracking record print and module search (Prompt 2 sections 18 to 23). */
test.describe('SEQUFLOH reports, export, audit and search', () => {
  test('a filtered report exports to Excel, the export shows in Export History, and search finds the task', async ({ page, browser }) => {
    test.setTimeout(150_000);
    const author = await Api.as(users.author);
    const approver = await Api.as(users.approver);
    const siteId = state().siteId;
    const tag = unique('Rpt').replace(' ', '-');
    const title = `Gearbox check ${tag}`;
    const wi = await author.call('POST', '/api/sequfloh/instructions', { type: 'Validation', title, siteId }) as { id: string; number: string };
    await author.call('PUT', `/api/sequfloh/instructions/${wi.id}`, { title, siteId, content: { validationAcceptance: { overallAcceptanceCriteria: 'Runs quietly' } },
      steps: [{ sequence: 10, title: 'Run gearbox' }], characteristics: [], partsRows: [] });
    await author.call('POST', `/api/sequfloh/instructions/${wi.id}/actions/submit-review`, {});
    for (const a of ['submit-approval', 'approve']) await approver.call('POST', `/api/sequfloh/instructions/${wi.id}/actions/${a}`, {});
    await approver.call('POST', `/api/sequfloh/instructions/${wi.id}/actions/release`, { effectiveDate: localDate() });
    const task = await author.call('POST', '/api/sequfloh/tracking', { type: 'Production', workInstructionId: wi.id, siteId, workOrder: `WO-${tag}`, operation: 'Gearbox run-in' }) as { id: string; number: string };

    await signIn(page, users.author);
    await page.goto(`/sequfloh/reports/task-performance?q=WO-${tag}`);
    await expect(page.getByRole('heading', { name: 'Task Performance' })).toBeVisible();
    for (const h of ['Task', 'Status', 'Assigned', 'Part', 'Operation', 'Elapsed (min)', 'Variance (min)', 'Result']) await expect(page.getByRole('columnheader', { name: h, exact: true })).toBeVisible();
    await expect(page.getByRole('cell', { name: task.number })).toBeVisible();
    await expect(page.getByLabel('Not Started')).toBeVisible(); // status count card
    await shot(page, 'sequfloh/s8-task-performance');

    await page.goto(`/sequfloh/reports/export-center?q=WO-${tag}`);
    const card = page.getByRole('region', { name: 'Production Tracking' });
    await card.getByLabel('File name (optional)').fill(`prod-${tag}`);
    await shot(page, 'sequfloh/s8-export-center');
    const [download] = await Promise.all([page.waitForEvent('download'), card.getByRole('link', { name: 'Export Production Tracking' }).click()]);
    expect(download.suggestedFilename()).toBe(`prod-${tag}.xlsx`);

    await page.goto(`/sequfloh/audit/exports?q=prod-${tag}`);
    await expect(page.getByRole('heading', { name: 'Export History' })).toBeVisible();
    const row = page.getByRole('row').filter({ hasText: `prod-${tag}.xlsx` });
    await expect(row).toContainText('Exported');
    await expect(row).toContainText(users.author.name);
    await shot(page, 'sequfloh/s8-audit-exports');
    await row.click();
    const dlg = page.getByRole('dialog', { name: 'Audit event' });
    await expect(dlg).toContainText('Production Tracking');
    await dlg.getByRole('button', { name: 'Close' }).click();
    await page.getByRole('tab', { name: 'Work Instruction History' }).click();
    await expect(page.getByRole('heading', { name: 'Work Instruction History' })).toBeVisible();
    await page.getByRole('textbox', { name: 'Search', exact: true }).fill(wi.number);
    await page.getByRole('button', { name: 'Search', exact: true }).click();
    await expect(page.getByRole('row').filter({ hasText: 'Work Instruction Release' }).first()).toBeVisible();
    await expect(page.getByRole('link', { name: 'Print summary' })).toHaveAttribute('href', /\/api\/sequfloh\/audit\/instructions\/print/);

    await page.goto(`/sequfloh/record/${task.id}`);
    await expect(page.getByRole('link', { name: 'Print' })).toHaveAttribute('href', `/api/sequfloh/tracking/${task.id}/print`);
    const pdf = await page.request.get(`/api/sequfloh/tracking/${task.id}/print`);
    expect(pdf.headers()['content-type']).toContain('application/pdf');

    await page.goto(`/sequfloh/search?q=${encodeURIComponent(task.number)}`);
    const tasks = page.getByRole('region', { name: 'Tasks' });
    await expect(tasks.getByText(task.number)).toBeVisible();
    await shot(page, 'sequfloh/s8-search');
    await tasks.getByText(task.number).click();
    await expect(page).toHaveURL(new RegExp(`/sequfloh/record/${task.id}`));

    // the read-only viewer can run reports but has no export permission
    const ctx = await browser.newContext();
    const vp = await ctx.newPage();
    await signIn(vp, users.viewer);
    await vp.goto(`/sequfloh/reports/task-performance?q=WO-${tag}`);
    await expect(vp.getByRole('cell', { name: task.number })).toBeVisible();
    await expect(vp.getByRole('link', { name: 'Export to Excel' })).toHaveCount(0);
    await ctx.close();
  });
});
