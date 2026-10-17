import { test, expect, type Page } from '@playwright/test';
import { Api, choose, dialog, localDate, signIn, state, unique, users } from './support';

const button = (p: Page, name: string | RegExp) => p.getByRole('button', { name });

async function planAudit(api: Api, title: string) {
  return api.post<{ id: string }>('/api/audits', {
    title, auditType: 'Process', scope: 'Machining cell 3: setup, first-off and in-process inspection.', criteria: 'ISO 9001:2015', siteId: state().siteId, departmentId: null,
    leadAuditorId: state().ids.author, plannedStart: localDate(), plannedEnd: localDate(1),
    checklist: [{ clause: '8.5.1', question: 'Are setup sheets at the current revision?' }, { clause: '7.1.5', question: 'Are gauges within calibration?' }],
  });
}

test.describe('E2E 4: audit with finding, corrective action and closure', () => {
  test('auditor runs the audit and raises a finding; only an audits approver closes it after verification', async ({ browser }) => {
    const api = await Api.as(users.author);
    const { id } = await planAudit(api, unique('Cell 3 process audit'));
    const author = await browser.newPage();
    await signIn(author, users.author);
    await author.goto(`/audits/${id}`);
    await expect(button(author, 'Raise finding')).toHaveCount(0); // not started yet
    await button(author, 'Start audit').click();
    await expect(button(author, 'Start audit')).toHaveCount(0);

    const results = author.getByRole('combobox', { name: 'Result' });
    await results.nth(0).click();
    await author.getByRole('option', { name: 'Nonconforming' }).click();
    await author.getByLabel('Evidence').nth(0).fill('Setup sheet for P/N 4410 at rev B; current is rev C.');
    await results.nth(1).click();
    await author.getByRole('option', { name: 'Conforming', exact: true }).click();
    await author.getByLabel('Evidence').nth(1).fill('All five gauges sampled were in date.');
    await button(author, 'Save checklist').click();

    await button(author, 'Raise finding').click();
    await choose(author, 'Checklist item (optional)', /^1\./);
    await author.getByLabel('Clause').fill('8.5.1');
    await author.getByLabel('Statement of nonconformity / observation').fill('Obsolete setup sheet in use at cell 3.');
    await choose(author, 'Responsible', users.author.name);
    await dialog(author).getByRole('button', { name: 'Raise finding' }).click();
    await author.getByRole('tab', { name: /Findings \(1\)/ }).click();

    await button(author, 'Move to reporting').click();
    await dialog(author).getByLabel('Audit summary').fill('One minor nonconformity on document control at point of use.');
    await dialog(author).getByRole('button', { name: 'Move to reporting' }).click();
    await expect(button(author, 'Move to reporting')).toHaveCount(0);
    await expect(button(author, 'Close audit')).toHaveCount(0); // author is not an audits approver

    // Corrective action: assigned to someone else, verified independently by the auditor.
    const audit = await api.get(`/api/audits/${id}`);
    const findingId = audit.findings[0].id;
    await api.post(`/api/records/AuditFinding/${findingId}/actions`, { title: 'Purge obsolete setup sheets at cell 3', description: null, actionType: 'Corrective', assigneeId: state().ids.viewer, dueDate: localDate(10), requiresVerification: true });
    const approver = await browser.newPage();
    await signIn(approver, users.approver);
    await approver.goto(`/audits/${id}`);
    await expect(button(approver, 'Close audit')).toHaveCount(0); // finding still open

    await author.goto(`/audits/findings/${findingId}`);
    await expect(button(author, 'Verify finding')).toHaveCount(0); // action not closed
    const viewer = await Api.as(users.viewer);
    const actions = await viewer.get(`/api/records/AuditFinding/${findingId}/actions`);
    await viewer.post(`/api/actions/${actions[0].id}/complete`, { notes: 'Cell 3 binder replaced; rev C only.' });
    await author.reload();
    await author.getByRole('row', { name: /Purge obsolete setup sheets/ }).getByRole('button', { name: 'Verify' }).click();
    await dialog(author).getByLabel('Verification notes').fill('Binder checked at cell 3.');
    await dialog(author).getByRole('button', { name: 'Record verification' }).click();
    await button(author, 'Verify finding').click();
    await dialog(author).getByLabel('Verification notes').fill('Action effective; no obsolete sheets found.');
    await dialog(author).getByRole('button', { name: 'Verify' }).click();
    await expect(author.getByRole('heading').getByText('Verified')).toBeVisible();

    await approver.reload();
    await button(approver, 'Close audit').click();
    await expect(approver.getByRole('heading').getByText('Closed')).toBeVisible();
    await api.dispose(); await viewer.dispose();
  });

  test('read-only and unassigned users cannot run or close audits', async ({ browser }) => {
    const api = await Api.as(users.author);
    const { id } = await planAudit(api, unique('Receiving audit'));
    const viewer = await browser.newPage();
    await signIn(viewer, users.viewer);
    await viewer.goto('/audits');
    await expect(viewer.getByRole('link', { name: 'Plan audit' })).toHaveCount(0);
    await viewer.goto(`/audits/${id}`);
    await expect(viewer.getByText('Are setup sheets at the current revision?')).toBeVisible();
    for (const name of ['Start audit', 'Save checklist', 'Raise finding', 'Move to reporting', 'Close audit']) await expect(button(viewer, name)).toHaveCount(0);
    await expect(viewer.getByRole('combobox', { name: 'Result' }).first()).toHaveAttribute('aria-disabled', 'true');

    const outsider = await browser.newPage();
    await signIn(outsider, users.outsider);
    await expect(outsider.locator('header nav').getByRole('link', { name: 'Audits' })).toHaveCount(0);
    await outsider.goto(`/audits/${id}`);
    await expect(outsider.getByRole('alert')).toBeVisible();
    await api.dispose();
  });
});
