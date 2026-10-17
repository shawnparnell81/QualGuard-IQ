import { test, expect } from '@playwright/test';
import { Api, choose, dialog, signIn, state, unique, users } from './support';

const topMenu = (page: import('@playwright/test').Page) => page.locator('header nav');

test.describe('E2E 1: controlled procedure lifecycle', () => {
  test('author creates and submits, approver e-signs, document becomes effective', async ({ browser }) => {
    const title = unique('Torque verification procedure');
    const author = await browser.newPage();
    await signIn(author, users.author);
    await topMenu(author).getByRole('link', { name: 'Documents' }).click();
    await author.getByRole('link', { name: 'New document' }).click();
    await author.getByLabel('Title').fill(title);
    await choose(author, 'Type', 'Procedure');
    await choose(author, 'Site', /^E2E/);
    await author.getByRole('button', { name: 'Create document' }).click();
    await expect(author).toHaveURL(/\/documents\/[0-9a-f-]{36}$/);
    const url = author.url();

    // Submit stays hidden until the controlled file is attached.
    await expect(author.getByText('Attach the controlled file on revision A')).toBeVisible();
    await expect(author.getByRole('button', { name: /Submit Rev A/ })).toHaveCount(0);
    await author.getByRole('tab', { name: 'Files' }).click();
    await author.locator('input[type=file]').setInputFiles({ name: 'torque-procedure.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4 torque verification steps') });
    await expect(author.getByRole('link', { name: 'torque-procedure.pdf' })).toBeVisible();
    await author.getByRole('button', { name: 'Submit Rev A for approval' }).click();
    await expect(author.getByRole('button', { name: /Submit Rev A/ })).toHaveCount(0);
    // The author holds no approval permission and is not offered a signature.
    await expect(author.getByRole('button', { name: 'Review and sign' })).toHaveCount(0);

    const approver = await browser.newPage();
    await signIn(approver, users.approver);
    await approver.goto(url);
    await approver.getByRole('button', { name: 'Review and sign' }).click();
    await dialog(approver).getByLabel('Your password').fill('not-my-password');
    await dialog(approver).getByRole('button', { name: 'Approve and sign' }).click();
    await expect(dialog(approver).getByRole('alert')).toBeVisible();
    await dialog(approver).getByLabel('Your password').fill(users.approver.password);
    await dialog(approver).getByRole('button', { name: 'Approve and sign' }).click();
    await expect(dialog(approver)).toHaveCount(0);
    await expect(approver.getByRole('heading').getByText('Active')).toBeVisible();
    await expect(approver.getByRole('cell', { name: 'Effective' })).toBeVisible();
    // The approved file is locked: no new version or removal.
    await approver.getByRole('tab', { name: 'Files' }).click();
    await expect(approver.getByRole('button', { name: 'Remove file' })).toHaveCount(0);

    await author.reload();
    await expect(author.getByRole('button', { name: 'Start new revision' })).toBeVisible();
  });

  test('read-only and unassigned users cannot change or see controlled documents', async ({ browser }) => {
    const author = await Api.as(users.author);
    const id = await author.post<string>('/api/documents', { kind: 0, title: unique('Read-only check'), documentType: 'Procedure', siteId: state().siteId, departmentId: null, reviewIntervalMonths: 24, initialRevision: 'A', changeSummary: 'Initial release' });

    const viewer = await browser.newPage();
    await signIn(viewer, users.viewer);
    await viewer.goto('/documents');
    await expect(viewer.getByRole('heading', { name: 'Documents' })).toBeVisible();
    await expect(viewer.getByRole('link', { name: 'New document' })).toHaveCount(0);
    await viewer.goto(`/documents/${id}`);
    await expect(viewer.getByText('Read-only check', { exact: false }).first()).toBeVisible();
    for (const name of [/Submit Rev/, /Start new revision/, /Make obsolete/, /Save draft/]) await expect(viewer.getByRole('button', { name })).toHaveCount(0);
    await viewer.getByRole('tab', { name: 'Files' }).click();
    await expect(viewer.getByRole('button', { name: 'Upload file' })).toHaveCount(0);

    const outsider = await browser.newPage();
    await signIn(outsider, users.outsider);
    await expect(topMenu(outsider).getByRole('link', { name: 'Documents' })).toHaveCount(0);
    await outsider.goto(`/documents/${id}`);
    await expect(outsider.getByRole('alert')).toBeVisible();
    await expect(outsider.getByText('Read-only check')).toHaveCount(0);
    await author.dispose();
  });
});

test.describe('E2E 2: drawing revision rejected and reworked', () => {
  test('approver rejects with a reason in the UI; revision returns to draft for the author', async ({ browser }) => {
    const api = await Api.as(users.author);
    const id = await api.post<string>('/api/documents', { kind: 1, title: unique('Bracket assembly'), documentType: 'Assembly Drawing', siteId: state().siteId, departmentId: null, reviewIntervalMonths: 24, initialRevision: 'A', changeSummary: 'Initial release', partNumber: 'BR-2001', drawingSize: 'B', sheetCount: 1 });
    const doc = await api.get(`/api/documents/${id}`);
    await api.upload('DrawingRevision', doc.revisions[0].id, 'BR-2001-A.pdf', '%PDF-1.4 drawing');
    await api.post(`/api/documents/revisions/${doc.revisions[0].id}/submit`);

    const approver = await browser.newPage();
    await signIn(approver, users.approver);
    await approver.goto(`/drawings/${id}`);
    await approver.getByRole('button', { name: 'Review and sign' }).click();
    await dialog(approver).getByLabel('Your password').fill(users.approver.password);
    await dialog(approver).getByRole('button', { name: 'Reject' }).click();
    await expect(dialog(approver).getByRole('alert')).toBeVisible(); // a reason is required
    await dialog(approver).getByLabel('Comment (required to reject)').fill('Hole callout missing tolerance on view B.');
    await dialog(approver).getByRole('button', { name: 'Reject' }).click();
    await expect(dialog(approver)).toHaveCount(0);

    const author = await browser.newPage();
    await signIn(author, users.author);
    await author.goto(`/drawings/${id}`);
    await expect(author.getByRole('button', { name: 'Submit Rev A for approval' })).toBeVisible();
    await author.getByRole('tab', { name: 'Approvals' }).click();
    await expect(author.getByText('Hole callout missing tolerance on view B.')).toBeVisible();
    await api.dispose();
  });

  test('only approvers are offered a signature; read-only users get no actions', async ({ browser }) => {
    const api = await Api.as(users.author);
    const id = await api.post<string>('/api/documents', { kind: 1, title: unique('Shaft detail'), documentType: 'Detail Drawing', siteId: state().siteId, departmentId: null, reviewIntervalMonths: 24, initialRevision: 'A', changeSummary: 'Initial release', partNumber: 'SH-10', drawingSize: 'A', sheetCount: 1 });
    const doc = await api.get(`/api/documents/${id}`);
    await api.upload('DrawingRevision', doc.revisions[0].id, 'SH-10-A.pdf', '%PDF-1.4 shaft');
    await api.post(`/api/documents/revisions/${doc.revisions[0].id}/submit`);
    const work = await api.get('/api/my/work');
    expect(work.approvals.find((a: any) => a.recordNumber.startsWith(doc.number))).toBeUndefined();

    const viewer = await browser.newPage();
    await signIn(viewer, users.viewer);
    await viewer.goto(`/drawings/${id}`);
    await expect(viewer.getByText('Shaft detail', { exact: false }).first()).toBeVisible();
    await expect(viewer.getByRole('button', { name: 'Review and sign' })).toHaveCount(0);
    await expect(viewer.getByRole('button', { name: /Submit Rev/ })).toHaveCount(0);
    await api.dispose();
  });
});
