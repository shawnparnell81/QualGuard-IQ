import { test, expect, type Page } from '@playwright/test';
import { Api, dialog, signIn, state, unique, users } from './support';

const button = (p: Page, name: string | RegExp) => p.getByRole('button', { name });

test.describe('E2E 6: training assignment and e-signed completion', () => {
  test('trainer assigns in the UI; trainee signs off with their password', async ({ browser }) => {
    const api = await Api.as(users.author);
    const title = unique('Torque wrench use');
    const course = await api.post<{ id: string }>('/api/training/courses', { title, description: 'Setting and using click-type torque wrenches.', siteId: state().siteId, departmentId: null, documentId: null, retrainOnRevision: true, validityMonths: 12, dueDays: 14 });

    const author = await browser.newPage();
    await signIn(author, users.author);
    await author.goto(`/training/courses/${course.id}`);
    await author.getByRole('combobox', { name: 'People' }).click();
    await author.getByRole('option', { name: users.viewer.name }).click();
    await author.keyboard.press('Escape');
    await button(author, 'Assign').click();
    await expect(author.getByText('1 assignment(s) created')).toBeVisible();
    await expect(author.getByRole('row', { name: new RegExp(users.viewer.name) })).toBeVisible();
    // A second assignment while one is open is skipped.
    await author.getByRole('combobox', { name: 'People' }).click();
    await author.getByRole('option', { name: users.viewer.name }).click();
    await author.keyboard.press('Escape');
    await button(author, 'Assign').click();
    await expect(author.getByText('0 assignment(s) created')).toBeVisible();

    const trainee = await browser.newPage();
    await signIn(trainee, users.viewer);
    await trainee.goto('/training');
    const row = trainee.getByRole('row', { name: new RegExp(title) });
    await row.getByRole('button', { name: 'Sign off' }).click();
    await dialog(trainee).getByLabel('Password').fill('wrong-password');
    await dialog(trainee).getByRole('button', { name: 'Sign' }).click();
    await expect(dialog(trainee).getByRole('alert')).toBeVisible();
    await dialog(trainee).getByLabel('Password').fill(users.viewer.password);
    await dialog(trainee).getByRole('button', { name: 'Sign' }).click();
    await expect(dialog(trainee)).toHaveCount(0);
    await expect(row.getByText('Completed')).toBeVisible();
    await expect(row.getByRole('button', { name: 'Sign off' })).toHaveCount(0);
    await api.dispose();
  });

  test('only the trainee can sign; read-only and unassigned users cannot assign', async ({ browser }) => {
    const api = await Api.as(users.author);
    const title = unique('Calibration label check');
    const course = await api.post<{ id: string }>('/api/training/courses', { title, description: null, siteId: state().siteId, departmentId: null, documentId: null, retrainOnRevision: false, validityMonths: null, dueDays: 7 });
    await api.post(`/api/training/courses/${course.id}/assign`, { userIds: [state().ids.viewer], dueOn: null });

    // The trainer does not see someone else's assignment in My training, and the server refuses to sign it.
    const author = await browser.newPage();
    await signIn(author, users.author);
    await author.goto('/training');
    await expect(author.getByRole('row', { name: new RegExp(title) })).toHaveCount(0);
    const viewerApi = await Api.as(users.viewer);
    const mine = await viewerApi.get('/api/training/my');
    const assignment = mine.find((a: any) => a.courseTitle === title);
    expect(await api.status('POST', `/api/training/assignments/${assignment.id}/complete`, { password: users.author.password, notes: null })).toBeGreaterThanOrEqual(400);

    const viewer = await browser.newPage();
    await signIn(viewer, users.viewer);
    await viewer.goto('/training/courses');
    await expect(viewer.getByRole('link', { name: 'New course' })).toHaveCount(0);
    await viewer.goto(`/training/courses/${course.id}`);
    await expect(viewer.getByRole('heading').getByText(title)).toBeVisible();
    await expect(viewer.getByText('Assign people')).toHaveCount(0);

    const outsider = await browser.newPage();
    await signIn(outsider, users.outsider);
    await outsider.goto('/training');
    await expect(outsider.getByText('No training assigned to you.')).toBeVisible();
    await outsider.goto(`/training/courses/${course.id}`);
    await expect(outsider.getByRole('alert')).toBeVisible();
    await api.dispose(); await viewerApi.dispose();
  });
});
