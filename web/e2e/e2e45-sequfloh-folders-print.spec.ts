import { test, expect } from '@playwright/test';
import { Api, localDate, shot, signIn, state, unique, users } from './support';

/** SEQUFLOH S4: separate validation and repair folders (columns, filters, actions) and controlled printing (Prompt 1 sections 22-23). */
test.describe('SEQUFLOH folders and printing', () => {
  test('validation and repair folders filter, act on rows and print controlled copies', async ({ page }) => {
    test.setTimeout(180_000);
    const author = await Api.as(users.author);
    const approver = await Api.as(users.approver);
    const siteId = state().siteId;
    const tag = unique('Fld').replace(' ', '-');
    const make = async (type: string, title: string, content: object, steps: object[], extra: object = {}) => {
      const c = await author.call('POST', '/api/sequfloh/instructions', { type, title, siteId });
      await author.call('PUT', `/api/sequfloh/instructions/${c.id}`, { title, siteId, content, steps, characteristics: [], partsRows: [], ...extra });
      return c as { id: string; number: string };
    };
    const release = async (id: string) => {
      await author.call('POST', `/api/sequfloh/instructions/${id}/actions/submit-review`, {});
      for (const a of ['submit-approval', 'approve']) await approver.call('POST', `/api/sequfloh/instructions/${id}/actions/${a}`, {});
      await approver.call('POST', `/api/sequfloh/instructions/${id}/actions/release`, { effectiveDate: localDate() });
    };
    const pass = await make('Validation', `Pump validation ${tag}`, { validationAcceptance: { overallAcceptanceCriteria: 'Flow within 10 %' }, results: { resultStatus: 'Pass' } },
      [{ sequence: 10, title: 'Run the pump for one hour' }], { resultStatus: 'Pass' });
    await release(pass.id);
    const fail = await make('Validation', `Valve validation ${tag}`, { validationAcceptance: { overallAcceptanceCriteria: 'No leak' } }, [{ sequence: 10, title: 'Leak test' }], { resultStatus: 'Fail' });
    const test1 = await make('RepairTest', `Motor test ${tag}`, { repairTest: { passCriteria: 'Runs at rated speed', repairCategory: 'Electrical' } }, [{ sequence: 10, title: 'Spin test' }],
      { repairCategory: 'Electrical' });
    await make('RepairTest', `Frame test ${tag}`, { repairTest: { passCriteria: 'No cracks', repairCategory: 'Structural' } }, [{ sequence: 10, title: 'Dye check' }], { repairCategory: 'Structural' });

    await signIn(page, users.author);
    await page.goto(`/sequfloh/instructions/validation?q=${tag}`);
    for (const h of ['Instruction number', 'Validation type', 'Result status', 'Reviewer', 'Approver', 'Effective', 'Last updated']) await expect(page.getByRole('columnheader', { name: h })).toBeVisible();
    await expect(page.getByRole('cell', { name: pass.number, exact: true })).toBeVisible();
    await expect(page.getByRole('cell', { name: fail.number, exact: true })).toBeVisible();
    await page.getByRole('combobox', { name: 'Result' }).click();
    await page.getByRole('option', { name: 'Pass' }).click();
    await expect(page.getByRole('cell', { name: fail.number, exact: true })).toHaveCount(0);
    await expect(page.getByRole('cell', { name: pass.number, exact: true })).toBeVisible();
    await shot(page, 'sequfloh/s4-validation-folder');
    await page.getByRole('button', { name: 'Clear filters' }).click();
    await expect(page.getByText('No work instructions in this folder yet.').or(page.getByRole('cell', { name: fail.number, exact: true }))).toBeVisible();

    await page.goto(`/sequfloh/instructions/repair?q=${tag}`);
    for (const h of ['Part being repaired', 'Repair description', 'Repair category']) await expect(page.getByRole('columnheader', { name: h })).toBeVisible();
    await page.getByRole('combobox', { name: 'Repair category' }).click();
    await page.getByRole('option', { name: 'Electrical' }).click();
    await expect(page.getByRole('cell', { name: `Motor test ${tag}` })).toBeVisible();
    await expect(page.getByRole('cell', { name: `Frame test ${tag}` })).toHaveCount(0);
    await shot(page, 'sequfloh/s4-repair-folder');
    await page.getByRole('button', { name: `Actions for ${test1.number}` }).click();
    await expect(page.getByRole('menuitem', { name: 'Duplicate' })).toBeVisible();
    await expect(page.getByRole('menuitem', { name: 'Archive' })).toHaveCount(0);
    await page.keyboard.press('Escape');

    // a controlled copy prints through the application's PDF renderer and is audited
    const pdf = await page.request.get(`/api/sequfloh/instructions/${pass.id}/print`);
    expect(pdf.ok()).toBeTruthy();
    expect(pdf.headers()['content-type']).toContain('application/pdf');
    expect((await pdf.body()).subarray(0, 5).toString()).toBe('%PDF-');
    await page.goto(`/sequfloh/instruction/${pass.id}`);
    await expect(page.getByRole('link', { name: 'Print' })).toHaveAttribute('href', `/api/sequfloh/instructions/${pass.id}/print`);
    await page.getByRole('tab', { name: 'History' }).click();
    await expect(page.getByRole('cell', { name: 'Work Instruction Printed' })).toBeVisible();

    const ap = await (await page.context().browser()!.newContext()).newPage();
    await signIn(ap, users.approver);
    await ap.goto(`/sequfloh/instructions/repair?q=${tag}`);
    await ap.getByRole('button', { name: `Actions for ${test1.number}` }).click();
    await ap.getByRole('menuitem', { name: 'Archive' }).click();
    await ap.getByRole('dialog').getByRole('button', { name: 'Archive' }).click();
    await expect(ap.getByRole('cell', { name: `Motor test ${tag}` })).toHaveCount(0);
    await ap.goto(`/sequfloh/instructions/archived?q=${tag}`);
    await ap.getByRole('cell', { name: test1.number, exact: true }).click();
    await ap.getByRole('button', { name: 'Restore', exact: true }).click();
    await ap.getByRole('dialog').getByRole('button', { name: 'Restore', exact: true }).click();
    await expect(ap.getByText('Draft', { exact: true }).first()).toBeVisible();
  });
});
