import { test, expect } from '@playwright/test';
import { Api, shot, signIn, state, unique, users } from './support';

/** SEQUFLOH S7: Quality and Engineering Personnel areas, their dashboards and review queues, and the development record (Prompt 2 sections 4 to 6). */
test.describe('SEQUFLOH Quality and Engineering', () => {
  test('Engineering keeps the development record and Quality picks the instruction up from its review queue', async ({ page, browser }) => {
    test.setTimeout(150_000);
    const author = await Api.as(users.author);
    const siteId = state().siteId;
    const tag = unique('Dev').replace(' ', '-');
    const title = `Bracket validation ${tag}`;
    const wi = await author.call('POST', '/api/sequfloh/instructions', { type: 'Validation', title, siteId }) as { id: string; number: string };
    await author.call('PUT', `/api/sequfloh/instructions/${wi.id}`, { title, siteId, content: { validationAcceptance: { overallAcceptanceCriteria: 'Bracket fits fixture' } },
      steps: [{ sequence: 10, title: 'Fit bracket' }], characteristics: [], partsRows: [] });

    await signIn(page, users.author);
    await page.goto('/sequfloh/engineering');
    await expect(page.getByRole('heading', { name: 'Engineering Dashboard' })).toBeVisible();
    await expect(page.getByLabel('Draft validation instructions')).toBeVisible();
    await shot(page, 'sequfloh/s7-engineering-dashboard');

    await page.goto(`/sequfloh/engineering/development?q=${encodeURIComponent(wi.number)}`);
    for (const h of ['Instruction', 'Related project', 'Assigned engineer', 'Target completion', 'Development status', 'Review status', 'Approval status']) await expect(page.getByRole('columnheader', { name: h, exact: true })).toBeVisible();
    await page.getByRole('cell', { name: title }).click();
    const dev = page.getByRole('region', { name: 'Development' });
    await expect(dev).toBeVisible();
    await dev.getByRole('combobox', { name: 'Assigned engineer' }).click();
    await page.getByRole('option', { name: users.author.name, exact: true }).click();
    await dev.getByLabel('Target completion date').fill('2026-12-15');
    await dev.getByLabel('Related project').fill(`Project ${tag}`);
    await dev.getByLabel('Development status').fill('Steps drafted');
    await dev.getByLabel('Engineering notes').fill('Fixture drawing pending.');
    await dev.getByRole('button', { name: 'Save development details' }).click();
    await expect(dev.getByText('Development details saved.')).toBeVisible();
    await shot(page, 'sequfloh/s7-development');
    await page.goto(`/sequfloh/engineering/development?q=${encodeURIComponent(wi.number)}`);
    await expect(page.getByRole('cell', { name: `Project ${tag}` })).toBeVisible();
    await expect(page.getByRole('cell', { name: 'Steps drafted' })).toBeVisible();
    await expect(page.getByRole('cell', { name: users.author.name })).toBeVisible();

    await author.call('POST', `/api/sequfloh/instructions/${wi.id}/actions/submit-review`, {});
    const ctx = await browser.newContext();
    const qp = await ctx.newPage();
    await signIn(qp, users.approver);
    await qp.goto('/sequfloh/quality');
    await expect(qp.getByRole('heading', { name: 'Quality Dashboard' })).toBeVisible();
    await expect(qp.getByLabel('Validation instructions pending review')).toBeVisible();
    await shot(qp, 'sequfloh/s7-quality-dashboard');
    await qp.goto(`/sequfloh/quality/review-queue?q=${encodeURIComponent(wi.number)}`);
    const row = qp.getByRole('row').filter({ hasText: wi.number });
    await expect(row).toContainText('Work instruction');
    await expect(row).toContainText('Pending Review');
    await shot(qp, 'sequfloh/s7-review-queue');
    await row.click();
    await expect(qp).toHaveURL(new RegExp(`/sequfloh/instruction/${wi.id}`));
    await expect(qp.getByRole('heading', { name: new RegExp(wi.number) })).toBeVisible();

    await qp.goto('/sequfloh');
    await expect(qp.getByLabel('Pending reviews')).toBeVisible();
    await ctx.close();

    // the read-only viewer sees the development record but cannot change it
    const vctx = await browser.newContext();
    const vp = await vctx.newPage();
    await signIn(vp, users.viewer);
    await vp.goto(`/sequfloh/instruction/${wi.id}?tab=development`);
    await expect(vp.getByRole('region', { name: 'Development' }).getByLabel('Related project')).toHaveValue(`Project ${tag}`);
    await expect(vp.getByRole('button', { name: 'Save development details' })).toHaveCount(0);
    await vctx.close();
  });
});
