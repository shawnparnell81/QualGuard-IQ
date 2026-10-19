import { test, expect } from '@playwright/test';
import { signIn, unique, users } from './support';

/** Work instruction layout: text, step, caution, table and sign-off blocks, reordering, saving as a template and starting a new instruction from it. */
test.describe('SEQUFLOH layout blocks', () => {
  test('an author lays out an instruction with blocks, saves it as a template and reuses it', async ({ page }) => {
    test.setTimeout(150_000);
    const title = unique('Pump seal repair');
    const tpl = unique('Pump seal layout');
    await signIn(page, users.author);
    await page.goto('/sequfloh/instructions/new');
    await page.getByRole('combobox', { name: 'Work instruction type' }).click();
    await page.getByRole('option', { name: 'Validation', exact: true }).click();
    await page.getByLabel('Title').fill(title);
    await page.getByRole('button', { name: 'Create and open designer' }).click();
    await expect(page.getByRole('heading', { name: new RegExp(title) })).toBeVisible();

    await page.getByRole('tab', { name: 'Layout' }).click();
    const add = async (kind: string) => { await page.getByRole('button', { name: 'Add block' }).click(); await page.getByRole('menuitem', { name: kind }).click(); };
    await add('Text');
    await page.getByRole('region', { name: 'Block 1: Text' }).getByLabel('Text').fill('Replace the shaft seal on the pump.');
    await add('Step');
    const step = page.getByRole('region', { name: 'Block 2: Step' });
    await expect(step.getByLabel('Operation number')).toHaveValue('10');
    await step.getByLabel('Step title').fill('Drain the pump');
    await add('Caution or warning');
    const caution = page.getByRole('region', { name: 'Block 3: Caution or warning' });
    await caution.getByLabel('Text').fill('Isolate power before opening the casing.');
    await add('Table');
    await page.getByRole('region', { name: 'Block 4: Table' }).getByLabel('Row 1 Item').fill('Casing bolts');
    await add('Check or sign-off');
    await page.getByRole('region', { name: 'Block 5: Check or sign-off' }).getByLabel('What is checked').fill('No leak after 10 minutes');

    // the caution belongs before the step
    await page.getByRole('button', { name: 'Move block 3 up' }).click();
    await expect(page.getByRole('region', { name: 'Block 2: Caution or warning' })).toBeVisible();
    await page.getByRole('button', { name: 'Save draft' }).click();
    await expect(page.getByText('Draft saved.')).toBeVisible();
    await expect(page.getByRole('region', { name: 'Block 3: Step' }).getByLabel('Step title')).toHaveValue('Drain the pump');

    await page.getByRole('button', { name: 'Save as template' }).click();
    await page.getByRole('dialog').getByLabel('Template name').fill(tpl);
    await page.getByRole('dialog').getByRole('button', { name: 'Save template' }).click();
    await expect(page.getByText(`Saved as template "${tpl}".`)).toBeVisible();

    // a new instruction starts from the template
    await page.goto('/sequfloh/instructions/new');
    await page.getByRole('combobox', { name: 'Work instruction type' }).click();
    await page.getByRole('option', { name: 'Validation', exact: true }).click();
    await page.getByLabel('Title').fill(`${title} B`);
    await page.getByRole('combobox', { name: 'Start from template' }).click();
    await page.getByRole('option', { name: new RegExp(tpl) }).click();
    await page.getByRole('button', { name: 'Create and open designer' }).click();
    await expect(page.getByRole('heading', { name: new RegExp(`${title} B`) })).toBeVisible();
    await page.getByRole('tab', { name: 'Layout' }).click();
    await expect(page.getByText(`Started from template ${tpl}.`)).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Layout blocks (5)' })).toBeVisible();
    await page.getByRole('button', { name: 'Remove block 5' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Remove' }).click();
    await expect(page.getByRole('heading', { name: 'Layout blocks (4)' })).toBeVisible();

    // the template is listed with the others
    await page.goto('/sequfloh/instructions/templates');
    await expect(page.getByRole('region', { name: 'Layout templates' }).getByText(tpl, { exact: true })).toBeVisible();
  });
});
