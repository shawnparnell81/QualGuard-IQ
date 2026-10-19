import { test, expect, type Page } from '@playwright/test';
import { Api, localDate, shot, signIn, state, unique, users } from './support';

/** SEQUFLOH S3: work instruction type-first creation, the five designers, steps and characteristics, lifecycle with review, approval, release and revision (Prompt 1 sections 15-21). */
const openNew = async (page: Page, type: string, title: string) => {
  await page.goto('/sequfloh/instructions/new');
  await expect(page.getByRole('heading', { name: 'WORK INSTRUCTION TYPE' })).toBeVisible();
  await page.getByRole('combobox', { name: 'Work instruction type' }).click();
  await page.getByRole('option', { name: type, exact: true }).click();
  await page.getByLabel('Title').fill(title);
  await page.getByRole('button', { name: 'Create and open designer' }).click();
  await expect(page.getByRole('heading', { name: new RegExp(title) })).toBeVisible();
};
const addStep = async (page: Page, title: string, fields: Record<string, string> = {}) => {
  await page.getByRole('button', { name: 'Add step' }).click();
  const d = page.getByRole('dialog');
  await d.getByLabel('Title', { exact: true }).fill(title);
  for (const [k, v] of Object.entries(fields)) await d.getByLabel(k, { exact: true }).fill(v);
  await d.getByRole('button', { name: 'Save step' }).click();
  await expect(page.getByRole('cell', { name: title, exact: true })).toBeVisible();
};
const act = async (page: Page, label: string, comment?: string, extra?: (p: Page) => Promise<void>) => {
  await page.getByRole('button', { name: label, exact: true }).click();
  const d = page.getByRole('dialog');
  if (comment) await d.getByLabel(/^Comment|^Revision reason/).fill(comment);
  if (extra) await extra(page);
  await d.getByRole('button', { name: label, exact: true }).click();
  await expect(d).toHaveCount(0);
};

test.describe('SEQUFLOH work instructions', () => {
  test('a validation instruction is designed, reviewed, approved, released read-only and revised', async ({ page, browser }) => {
    test.setTimeout(180_000);
    const title = unique('Seal leak validation');
    await signIn(page, users.author);
    await openNew(page, 'Validation', title);
    const number = (await page.getByRole('heading', { name: new RegExp(title) }).innerText()).split(' ')[0];
    await page.getByRole('tab', { name: 'Validation steps' }).click();
    await page.getByRole('button', { name: 'Submit for review' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Submit for review' }).click();
    await expect(page.getByRole('dialog').getByRole('alert')).toContainText('Add at least one validation step');
    await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click();
    await addStep(page, 'Pressurize the housing', { 'Lower limit': '5', 'Upper limit': '2' });
    await page.getByRole('button', { name: 'Save draft' }).click();
    await expect(page.getByRole('alert').filter({ hasText: 'lower limit cannot be greater than the upper limit' })).toBeVisible();
    await page.getByRole('button', { name: 'Edit step 1' }).click();
    await page.getByRole('dialog').getByLabel('Lower limit', { exact: true }).fill('1');
    await page.getByRole('dialog').getByRole('button', { name: 'Save step' }).click();
    await addStep(page, 'Hold pressure for ten minutes');
    await page.getByRole('button', { name: 'Duplicate step 2' }).click();
    await page.getByRole('button', { name: 'Delete step 3' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Delete' }).click();
    await page.getByRole('button', { name: 'Move step 2 up' }).click();
    await expect(page.getByRole('row').nth(1)).toContainText('Hold pressure for ten minutes');
    await page.getByRole('button', { name: 'Move step 2 up' }).click();
    await page.getByRole('tab', { name: 'Content' }).click();
    await page.getByRole('region', { name: 'Validation acceptance' }).getByLabel('Overall acceptance criteria').fill('No leak above 0.5 sccm at 3 bar.');
    await page.getByRole('region', { name: 'Objective' }).getByLabel('Validation objective').fill('Show the new seal holds pressure.');
    await page.getByRole('button', { name: 'Save draft' }).click();
    await expect(page.getByText('Draft saved.')).toBeVisible();
    await shot(page, 'sequfloh/s3-validation-designer');
    await page.getByRole('tab', { name: 'Validation steps' }).click();
    await shot(page, 'sequfloh/s3-validation-steps');
    await act(page, 'Submit for review');
    await expect(page.getByRole('button', { name: 'Save draft' })).toHaveCount(0);

    const ap = await (await browser.newContext()).newPage();
    await signIn(ap, users.approver);
    await ap.goto('/sequfloh/instructions/pending-review');
    await ap.getByRole('cell', { name: number, exact: true }).click();
    await act(ap, 'Return for changes', 'Add the test pressure unit');
    await expect(ap.getByText('Changes Requested').first()).toBeVisible();

    await page.reload();
    await act(page, 'Submit for review');
    await ap.reload();
    await act(ap, 'Submit for approval');
    await act(ap, 'Approve');
    await act(ap, 'Release', undefined, async (p) => { await p.getByRole('dialog').getByLabel('Effective date').fill(localDate()); });
    await expect(ap.getByText('Released', { exact: true }).first()).toBeVisible();
    await expect(ap.getByRole('button', { name: 'Save draft' })).toHaveCount(0);
    await ap.getByRole('tab', { name: 'History' }).click();
    await expect(ap.getByRole('cell', { name: 'Add the test pressure unit', exact: true })).toBeVisible();
    await shot(ap, 'sequfloh/s3-lifecycle-history');

    await page.reload();
    await expect(page.getByText('Released revisions are read-only')).toBeVisible();
    await act(page, 'Create revision', 'Tighten the leak limit');
    await expect(page.getByRole('heading', { name: new RegExp(`${number} rev B`) })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Save draft' })).toBeVisible();
    await page.goto('/sequfloh/instructions/validation?q=' + encodeURIComponent(number));
    await expect(page.getByRole('cell', { name: 'Released' })).toBeVisible();
    await expect(page.getByRole('cell', { name: 'Draft' })).toBeVisible();
    await shot(page, 'sequfloh/s3-validation-folder');
  });

  test('a repair work instruction uses the part master, controlled repair description, copied operation steps and parts', async ({ page }) => {
    test.setTimeout(150_000);
    const api = await Api.as(users.author);
    const pn = unique('RWP').replace(' ', '-');
    const part = await api.call('POST', '/api/sequfloh/parts', { part: { partNumber: pn, name: 'Gearbox ' + pn, description: null, partType: 1, revision: 'C', unitOfMeasure: 'EA', productId: null, material: null,
      hasSpecialCharacteristics: false, siteId: state().siteId, departmentId: null, status: 0, rowVersion: 0 },
      execution: { productFamily: 'Drives', assemblyType: 'Gearbox assembly', isRepairable: true, isSerialized: true, isLotControlled: false, drawingNumber: null, specification: null, rowVersion: 0 } });
    const seal = await api.call('POST', '/api/sequfloh/parts', { part: { partNumber: 'SEAL-' + pn, name: 'Shaft seal', description: null, partType: 0, revision: 'A', unitOfMeasure: 'EA', productId: null, material: null,
      hasSpecialCharacteristics: false, siteId: state().siteId, departmentId: null, status: 0, rowVersion: 0 },
      execution: { productFamily: 'Drives', assemblyType: null, isRepairable: false, isSerialized: false, isLotControlled: true, drawingNumber: null, specification: null, rowVersion: 0 } });
    expect(seal.id).toBeTruthy();
    const step = unique('Replace shaft seal');
    await api.call('POST', '/api/sequfloh/operation-steps', { title: step, category: 'Disassembly', detailedAction: 'Remove the cover and press out the old seal', defaultTools: 'Seal press', defaultParts: null,
      specification: null, target: 5, minimum: 4, maximum: 6, unit: 'N·m', safetyWarning: 'Wear gloves', qualityCheckpoint: 'Seal seated flush', expectedResult: 'No oil weep', evidenceRequirement: 'Photo',
      requiredRole: null, requiredQualification: null, isActive: true, rowVersion: 0 });

    await signIn(page, users.author);
    const title = unique('Gearbox seal repair');
    await page.goto('/sequfloh/instructions/new?type=RepairWork');
    await page.getByLabel('Title').fill(title);
    await page.getByLabel('Part being repaired').fill(pn);
    await page.getByRole('option', { name: new RegExp(pn) }).click();
    await page.getByRole('button', { name: 'Create and open designer' }).click();
    await expect(page.getByRole('heading', { name: new RegExp(title) })).toBeVisible();
    const controls = page.getByRole('region', { name: 'Repair controls' });
    await expect(controls.getByText('Gearbox assembly')).toBeVisible();
    await controls.getByRole('combobox', { name: 'Repair description (controlled)' }).click();
    await page.getByRole('option', { name: 'Component replacement' }).click();
    await controls.getByRole('button', { name: 'Copy operation steps' }).click();
    await page.getByRole('dialog').getByLabel('Search operation steps').fill(step);
    await page.getByRole('dialog').getByRole('checkbox').first().check();
    await page.getByRole('dialog').getByRole('button', { name: /Copy 1 step/ }).click();
    await expect(page.getByText('Operation steps copied')).toBeVisible();
    await controls.getByRole('button', { name: 'Copy parts' }).click();
    const d = page.getByRole('dialog');
    await d.getByRole('combobox', { name: 'Source' }).click();
    await page.getByRole('option', { name: 'Individually selected parts' }).click();
    await d.getByRole('combobox', { name: 'Parts', exact: true }).fill('SEAL-' + pn);
    await page.getByRole('option', { name: new RegExp('SEAL-' + pn) }).click();
    await d.getByRole('button', { name: 'Copy', exact: true }).click();
    await expect(page.getByText('Parts copied into this instruction')).toBeVisible();
    await shot(page, 'sequfloh/s3-repair-designer');
    await page.getByRole('tab', { name: 'Repair steps' }).click();
    await expect(page.getByRole('cell', { name: step, exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'Edit step 1' }).click();
    await page.getByRole('dialog').getByLabel('Torque value').fill('5 N·m');
    await page.getByRole('dialog').getByRole('button', { name: 'Save step' }).click();
    await page.getByRole('tab', { name: 'Document control' }).click();
    await page.getByLabel('Effective date').fill(localDate(7));
    await page.getByRole('button', { name: 'Save draft' }).click();
    await expect(page.getByText('Draft saved.')).toBeVisible();
    await page.getByRole('tab', { name: 'Repair parts' }).click();
    await expect(page.getByRole('cell', { name: 'SEAL-' + pn })).toBeVisible();
    await shot(page, 'sequfloh/s3-repair-parts');
    await expect(page.getByText('Before this instruction can be approved and released')).toHaveCount(0);
    // the library template is unchanged by the edited copy
    const lib = await api.call('GET', `/api/sequfloh/operation-steps?q=${encodeURIComponent(step)}`);
    expect(lib.items[0].detailedAction).toBe('Remove the cover and press out the old seal');
    expect(part.id).toBeTruthy();
  });

  test('FAI characteristics are added, duplicated, reordered and removed; a viewer only reads', async ({ page }) => {
    test.setTimeout(120_000);
    const title = unique('Bracket FAI');
    await signIn(page, users.author);
    await openNew(page, 'FAI', title);
    const url = page.url();
    await page.getByRole('tab', { name: 'Characteristics' }).click();
    for (const [n, req] of [['1', 'Hole diameter 6.0 ±0.1'], ['2', 'Flatness 0.05']]) {
      await page.getByRole('button', { name: 'Add characteristic' }).click();
      const d = page.getByRole('dialog');
      await d.getByLabel('Characteristic number').fill(n);
      await d.getByLabel('Requirement').fill(req);
      await d.getByLabel('Drawing zone').fill('B' + n);
      await d.getByRole('button', { name: 'Save characteristic' }).click();
    }
    await page.getByRole('button', { name: 'Duplicate characteristic 1' }).click();
    await expect(page.getByRole('cell', { name: '1-copy' })).toBeVisible();
    await page.getByRole('button', { name: 'Delete characteristic 3' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Delete' }).click();
    await page.getByRole('button', { name: 'Move characteristic 2 up' }).click();
    await expect(page.getByRole('row').nth(1)).toContainText('Flatness 0.05');
    await page.getByRole('button', { name: 'Save draft' }).click();
    await expect(page.getByText('Draft saved.')).toBeVisible();
    await shot(page, 'sequfloh/s3-fai-characteristics');

    const viewer = await page.context().browser()!.newContext();
    const vp = await viewer.newPage();
    await signIn(vp, users.viewer);
    await vp.goto('/sequfloh/instructions/fai');
    await expect(vp.getByRole('heading', { name: 'FAI Work Instructions' })).toBeVisible();
    await expect(vp.getByRole('button', { name: 'New instruction' })).toHaveCount(0);
    await vp.goto(url.replace(/^https?:\/\/[^/]+/, ''));
    await expect(vp.getByRole('heading', { name: new RegExp(title) })).toBeVisible();
    await expect(vp.getByRole('button', { name: 'Save draft' })).toHaveCount(0);
    await expect(vp.getByRole('button', { name: 'Submit for review' })).toHaveCount(0);
    await vp.goto('/sequfloh/instructions/new');
    await expect(vp.getByText('You do not have permission to open this SEQUFLOH page')).toBeVisible();
    await vp.goto('/sequfloh/instructions/templates');
    await expect(vp.getByText('Repair Work Instruction', { exact: true })).toBeVisible();
    await expect(vp.getByRole('button', { name: 'Use template' })).toHaveCount(0);
  });
});
