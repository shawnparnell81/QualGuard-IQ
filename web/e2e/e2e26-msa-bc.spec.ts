import { test, expect } from '@playwright/test';
import { Api, localDate, shot, signIn, state, unique, users, type User } from './support';

/** Bias study statistics with result versions, and the Business Central manufacturing connection's validation state. */
test.describe('E2E 26: bias study results and BC manufacturing validation', () => {
  test('a bias study shows t, critical t and p-value, and recalculation adds a version while the original stays of record', async ({ page }) => {
    const admin = await Api.as(users.admin);
    const t = Date.now().toString(36);
    const site = state().siteId;
    const role = await admin.post<string>('/api/admin/roles', { name: `MSA engineer ${t}`, description: 'UI test', permissions: ['spc.view', 'spc.edit', 'calibration.view', 'calibration.edit'] });
    const u: User = { key: 'msa', email: `msa-${t}@qms.test`, password: 'msa-engineer-password-1', name: `Morgan Measure ${t}` };
    const uid = await admin.post<string>('/api/admin/users', { email: u.email, displayName: u.name, password: u.password });
    await admin.post(`/api/admin/users/${uid}/roles`, { roleId: role, siteId: site, departmentId: null });
    const api = await Api.as(u);
    const eq = await api.post<string | { id: string }>('/api/calibration/equipment', { name: unique('Height gauge'), siteId: site, calibrationIntervalDays: 365, nextDueOn: localDate(100) });
    const equipmentId = typeof eq === 'string' ? eq : eq.id;
    const study = await api.post<{ id: string }>('/api/spc/msa', { title: unique('Height bias'), studyType: 2, equipmentId, tolerance: 1, referenceValue: 6.0, reviewIntervalMonths: 12, siteId: site, confidenceLevel: 0.99 });
    const readings = [6.0, 6.1, 5.9, 6.2, 6.0, 6.3, 6.1, 5.8, 6.2, 6.1, 6.0, 6.4, 6.2, 6.1, 6.0];
    await api.put(`/api/spc/msa/${study.id}/measurements`, readings.map((v, i) => ({ part: 1, trial: i + 1, value: v })));

    await signIn(page, u);
    await page.goto(`/spc/msa/${study.id}`);
    await expect(page.getByText('critical t', { exact: true })).toBeVisible();
    await expect(page.getByText('p value', { exact: true })).toBeVisible();
    await expect(page.getByText('degrees of freedom', { exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'Submit for approval' }).click();
    await page.getByRole('button', { name: 'Recalculate (keeps the original)' }).click();
    const versions = page.getByRole('table').filter({ hasText: 'Calculation' });
    await expect(versions.getByText('Yes (approved basis)')).toHaveCount(1);
    await expect(versions.locator('tbody tr')).toHaveCount(2);
    await shot(page, '06-bias-study-result-versions');
    await api.dispose(); await admin.dispose();
  });

  test('a Business Central manufacturing connection starts disabled with its unverified assumptions listed', async ({ page }) => {
    const api = await Api.as(users.author);
    const c = await api.post<{ id: string }>('/api/integrations', { name: unique('BC manufacturing'), system: 9, edition: 'WebServices', baseUrl: null, account: 'tenant-1', company: 'CRONUS', clientId: 'app-1', username: null, options: {}, maxConcurrency: null, requestsPerMinute: null, inventorySystemOfRecord: false, adminNotes: null, siteId: state().siteId, departmentId: null });
    await signIn(page, users.author);
    await page.goto(`/integrations/${c.id}?tab=validation`);
    await expect(page.getByTestId('erp-gate-summary')).toContainText('Cannot be enabled yet');
    await expect(page.getByTestId('erp-assumption').first()).toContainText('Unverified');
    await shot(page, '09-bc-manufacturing-validation');
    await api.dispose();
  });
});
