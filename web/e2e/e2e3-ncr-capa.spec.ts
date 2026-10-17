import { test, expect, type Page } from '@playwright/test';
import { Api, choose, dialog, localDate, signIn, state, unique, users } from './support';

const button = (p: Page, name: string | RegExp) => p.getByRole('button', { name });

test.describe('E2E 3: NCR to CAPA with independent effectiveness verification', () => {
  test('quality engineer reports and contains, approver signs disposition, CAPA closes after independent verification', async ({ browser }) => {
    const title = unique('Bore oversize on housing H-220');
    const author = await browser.newPage();
    await signIn(author, users.author);
    await author.goto('/ncr');
    await author.getByRole('main').getByRole('link', { name: 'Report nonconformance' }).click();
    await author.getByLabel('Title').fill(title);
    await author.getByLabel('What is wrong (requirement versus actual)').fill('12 of 50 bores at 25.06 mm against 25.00 +0.03/-0.00.');
    await choose(author, 'Source', 'In-process');
    await choose(author, 'Site', /^E2E/);
    await author.getByLabel('Part number').fill('H-220');
    await button(author, 'Create NCR').click();
    await expect(author).toHaveURL(/\/ncr\/[0-9a-f-]{36}$/);
    const ncrUrl = author.url();

    // Only the next permitted steps are offered.
    await expect(button(author, 'Close NCR')).toHaveCount(0);
    await expect(button(author, 'Create CAPA')).toHaveCount(0);
    await button(author, 'Record containment').click();
    await dialog(author).getByLabel(/Containment actions/).fill('Lot moved to MRB cage; WIP 100% inspected.');
    await dialog(author).getByRole('button', { name: 'Save' }).click();
    await expect(author.getByText('Lot moved to MRB cage; WIP 100% inspected.')).toBeVisible();

    await button(author, 'Propose disposition').click();
    await choose(author, 'Disposition', 'Rework');
    await dialog(author).getByLabel('Justification').fill('Bores can be honed to size; re-inspect 100%.');
    await dialog(author).getByLabel(/^Instructions/).fill('Hone to 25.00 +0.03 per WI-112; re-inspect every bore.');
    await choose(author, 'CAPA required', 'Yes');
    await dialog(author).getByRole('button', { name: 'Submit for approval' }).click();
    await expect(button(author, 'Propose disposition')).toHaveCount(0);
    await expect(button(author, 'Sign disposition')).toHaveCount(0);

    const approver = await browser.newPage();
    await signIn(approver, users.approver);
    await approver.goto(ncrUrl);
    await button(approver, 'Sign disposition').click();
    await dialog(approver).getByLabel('Your password').fill(users.approver.password);
    await dialog(approver).getByRole('button', { name: 'Approve and sign' }).click();
    await expect(dialog(approver)).toHaveCount(0);
    // Approver may view but not edit the NCR.
    for (const name of ['Record containment', 'Create CAPA', 'Close NCR']) await expect(button(approver, name)).toHaveCount(0);

    await author.reload();
    await expect(button(author, 'Close NCR')).toHaveCount(0); // a CAPA is required first
    await button(author, 'Create CAPA').click();
    await dialog(author).getByRole('button', { name: 'Create CAPA' }).click();
    await expect(author).toHaveURL(/\/capa\/[0-9a-f-]{36}$/);
    const capaUrl = author.url();
    const capaId = capaUrl.split('/').pop()!;
    await expect(button(author, 'Ready for effectiveness check')).toHaveCount(0);
    await button(author, 'Record root cause').click();
    await dialog(author).getByLabel('Root cause').fill('Worn boring bar insert; no tool-life limit in the routing.');
    await dialog(author).getByLabel('How effectiveness will be judged').fill('Zero oversize bores over the next 20 lots.');
    await dialog(author).getByLabel('Effectiveness check due').fill(localDate(60));
    await dialog(author).getByRole('button', { name: 'Save' }).click();
    await expect(author.getByText('Worn boring bar insert; no tool-life limit in the routing.')).toBeVisible();

    // Action assigned to someone else, completed by them from My Work, verified by the CAPA owner.
    const api = await Api.as(users.author);
    await api.post(`/api/records/Capa/${capaId}/actions`, { title: 'Add tool-life limit to routing R-220', description: null, actionType: 'Corrective', assigneeId: state().ids.viewer, dueDate: localDate(14), requiresVerification: true });
    const assignee = await browser.newPage();
    await signIn(assignee, users.viewer);
    await assignee.goto('/my-work');
    await assignee.getByRole('tab', { name: /Assigned/ }).click();
    await assignee.getByRole('row', { name: /Add tool-life limit/ }).getByRole('button', { name: 'Complete' }).click();
    await assignee.getByLabel('What was done').fill('Routing R-220 rev C sets a 400-part insert life.');
    await button(assignee, 'Mark complete').click();
    await expect(assignee.getByRole('row', { name: /Add tool-life limit/ })).toHaveCount(0);

    await author.reload();
    await author.getByRole('tab', { name: 'Actions' }).click();
    await author.getByRole('row', { name: /Add tool-life limit/ }).getByRole('button', { name: 'Verify' }).click();
    await dialog(author).getByLabel('Verification notes').fill('Routing checked on the shop floor.');
    await dialog(author).getByRole('button', { name: 'Record verification' }).click();
    await expect(dialog(author)).toHaveCount(0);
    await button(author, 'Ready for effectiveness check').click();
    // The owner cannot verify their own CAPA.
    await expect(button(author, 'Verify effectiveness')).toHaveCount(0);

    await approver.goto(capaUrl);
    await button(approver, 'Verify effectiveness').click();
    await dialog(approver).getByLabel('Evidence and result').fill('20 lots since change, no oversize bores.');
    await dialog(approver).getByRole('button', { name: 'Record verification' }).click();
    await expect(approver.getByRole('heading').getByText('Closed')).toBeVisible();

    await author.goto(ncrUrl);
    await expect(button(author, 'Close NCR')).toHaveCount(0); // disposition must be verified first
    await button(author, 'Verify disposition').click();
    await dialog(author).getByLabel('Verification evidence').fill('All affected units honed and re-inspected; records attached.');
    await dialog(author).getByRole('button', { name: 'Record verification' }).click();
    await expect(dialog(author)).toHaveCount(0);
    await button(author, 'Close NCR').click();
    await dialog(author).getByLabel('Closure notes and evidence').fill('Reworked lot accepted; CAPA closed effective.');
    await dialog(author).getByRole('button', { name: 'Close NCR' }).click();
    await expect(author.getByRole('heading').getByText('Closed')).toBeVisible();
    await expect(button(author, 'Record containment')).toHaveCount(0);
    await api.dispose();
  });

  test('read-only and unassigned users get no NCR or CAPA actions', async ({ browser }) => {
    const api = await Api.as(users.author);
    const ncr = await api.post('/api/ncrs', { title: unique('Scratch on cover'), description: 'Cosmetic scratch.', source: 'Final inspection', siteId: state().siteId, departmentId: null, ownerId: null, partNumber: null, lotNumber: null, quantityAffected: 1, detectedOn: localDate(), severity: 0 });
    const capa = await api.post('/api/capas', { title: unique('Packaging damage trend'), problemStatement: 'Three complaints in a quarter.', capaType: 'Corrective', source: 'NCR trend', siteId: state().siteId, departmentId: null, ownerId: null });

    const viewer = await browser.newPage();
    await signIn(viewer, users.viewer);
    await viewer.goto('/ncr');
    await expect(viewer.getByRole('link', { name: 'Report nonconformance' })).toHaveCount(0);
    await viewer.goto(`/ncr/${ncr.id}`);
    await expect(viewer.getByText('Cosmetic scratch.')).toBeVisible();
    for (const name of ['Record containment', 'Propose disposition', 'Create CAPA', 'Close NCR', 'Sign disposition']) await expect(button(viewer, name)).toHaveCount(0);
    await viewer.goto(`/capa/${capa.id}`);
    await expect(viewer.getByText('Three complaints in a quarter.')).toBeVisible();
    for (const name of [/root cause/, 'Ready for effectiveness check', 'Verify effectiveness']) await expect(button(viewer, name)).toHaveCount(0);

    const outsider = await browser.newPage();
    await signIn(outsider, users.outsider);
    await expect(outsider.locator('header nav').getByRole('link', { name: 'Quality Events' })).toHaveCount(0);
    await outsider.goto(`/ncr/${ncr.id}`);
    await expect(outsider.getByRole('alert')).toBeVisible();
    await expect(outsider.getByText('Cosmetic scratch.')).toHaveCount(0);
    await api.dispose();
  });
});
