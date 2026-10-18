import { test, expect } from '@playwright/test';
import { shot, signIn, unique, users } from './support';

/** SEQUFLOH S2: Part Master (shared), parts lists with lifecycle and revisions, reusable operation steps, revision history (Prompt 1 sections 12-13). */
test.describe('SEQUFLOH parts and items', () => {
  test('a part is created in the shared Part Master and carried into a controlled parts list', async ({ page }) => {
    test.setTimeout(150_000);
    const pn = unique('SQP').replace(' ', '-');
    await signIn(page, users.author);
    await page.goto('/sequfloh/parts');
    await expect(page.getByRole('heading', { name: 'Part Master' })).toBeVisible();
    await page.getByRole('button', { name: 'New part' }).click();
    const d = page.getByRole('dialog');
    await d.getByLabel('Part number').fill(pn);
    await d.getByRole('textbox', { name: 'Name', exact: true }).fill('Pump housing');
    await d.getByLabel('Product family').fill('Pumps');
    await d.getByLabel('Drawing number').fill('DWG-' + pn);
    await d.getByLabel('Repairable').check();
    await d.getByLabel('Serialized').check();
    await d.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByRole('heading', { name: new RegExp(pn) })).toBeVisible();
    await expect(page.getByText('DWG-' + pn)).toBeVisible();
    await shot(page, 'sequfloh/s2-part-detail');

    await page.goto(`/sequfloh/parts?view=repairable&q=${pn}`);
    await expect(page.getByRole('heading', { name: 'Repair Parts' })).toBeVisible();
    await expect(page.getByRole('cell', { name: pn })).toBeVisible();
    await shot(page, 'sequfloh/s2-part-master');

    await page.goto('/sequfloh/parts-lists');
    await page.getByRole('button', { name: 'New parts list' }).click();
    await page.getByRole('dialog').getByLabel('Title').fill('Repair kit ' + pn);
    await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click();
    await expect(page.getByRole('heading', { name: new RegExp('PL-.*Repair kit ' + pn) })).toBeVisible();
    for (const [num, qty] of [['OR-' + pn, '4'], ['GS-' + pn, '1']]) {
      await page.getByRole('button', { name: 'Add item' }).click();
      const i = page.getByRole('dialog');
      await i.getByLabel('Part number', { exact: true }).fill(num);
      await i.getByLabel('Description').fill('Seal ' + num);
      await i.getByLabel('Quantity').fill(qty);
      await i.getByRole('button', { name: 'Save item' }).click();
      await expect(page.getByRole('cell', { name: num, exact: true })).toBeVisible();
    }
    await page.getByRole('button', { name: 'Move item 2 up' }).click();
    await expect(page.getByRole('row').nth(1)).toContainText('GS-' + pn);
    await page.getByRole('button', { name: 'Approve' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Approve' }).click();
    await expect(page.getByText('This list is approved and locked.')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Add item' })).toHaveCount(0);
    await shot(page, 'sequfloh/s2-parts-list');
    await page.getByRole('button', { name: 'New revision' }).click();
    await page.getByRole('dialog').getByLabel('Revision reason').fill('Add backup ring');
    await page.getByRole('dialog').getByRole('button', { name: 'Create revision' }).click();
    await expect(page.getByRole('button', { name: 'Add item' })).toBeVisible();
    await page.getByRole('tab', { name: 'Revisions' }).click();
    await expect(page.getByRole('cell', { name: 'Add backup ring' })).toBeVisible();

    await page.goto(`/sequfloh/revision-history`);
    await page.getByRole('textbox', { name: 'Search', exact: true }).fill(pn);
    await page.getByRole('textbox', { name: 'Search', exact: true }).press('Enter');
    await expect(page.getByRole('cell', { name: 'Add backup ring' })).toBeVisible();
    await shot(page, 'sequfloh/s2-revision-history');
  });

  test('reusable operation steps are maintained as a library', async ({ page }) => {
    const title = unique('Torque cover');
    await signIn(page, users.author);
    await page.goto('/sequfloh/operation-steps');
    await page.getByRole('button', { name: 'New operation step' }).click();
    const d = page.getByRole('dialog');
    await d.getByLabel('Title').fill(title);
    await d.getByLabel('Detailed action').fill('Torque the four cover bolts in a cross pattern');
    await d.getByLabel('Target').fill('12');
    await d.getByLabel('Minimum').fill('20');
    await d.getByLabel('Maximum').fill('14');
    await d.getByRole('button', { name: 'Save' }).click();
    await expect(d.getByRole('alert')).toBeVisible();
    await d.getByLabel('Minimum').fill('10');
    await d.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByRole('cell', { name: title, exact: true })).toBeVisible();
    await shot(page, 'sequfloh/s2-operation-steps');
  });
});
