import { test, expect, type Page } from '@playwright/test';
import { Api, choose, dialog, localDate, signIn, state, unique, users } from './support';

const button = (p: Page, name: string | RegExp) => p.getByRole('button', { name });
const addEquipment = (api: Api, name: string) => api.post<{ id: string }>('/api/calibration/equipment', {
  name, manufacturer: 'Mitutoyo', model: '500-196', serialNumber: unique('SN'), location: 'Cell 3', measurementRange: '0-150 mm', tolerance: '±0.02 mm',
  siteId: state().siteId, departmentId: null, ownerId: null, calibrationIntervalDays: 365, nextDueOn: localDate(30),
});

test.describe('E2E 5: out-of-tolerance calibration and impact assessment', () => {
  test('out-of-tolerance quarantines; return to service appears only after completed assessment and passing calibration', async ({ browser }) => {
    const api = await Api.as(users.author);
    const { id } = await addEquipment(api, unique('Digital caliper'));
    // An earlier passing calibration gives the assessment a last-known-good date.
    await api.post(`/api/calibration/equipment/${id}/events`, { performedOn: localDate(-200), performedBy: 'Metrology lab', result: 0, asFound: null, asLeft: null, standardsUsed: 'Gauge blocks GB-1', certificateNumber: 'C-100' });

    const author = await browser.newPage();
    await signIn(author, users.author);
    await author.goto(`/calibration/equipment/${id}`);
    await expect(button(author, 'Return to service')).toHaveCount(0);
    await button(author, 'Record calibration').click();
    await dialog(author).getByLabel('Performed by (person or lab)').fill('Metrology lab');
    await choose(author, 'Result', 'Out of tolerance');
    await dialog(author).getByLabel('As found').fill('+0.05 mm at 100 mm');
    await dialog(author).getByRole('button', { name: 'Save record' }).click();
    await expect(author).toHaveURL(/\/calibration\/impact\/[0-9a-f-]{36}$/);
    const impactUrl = author.url();
    await expect(button(author, 'Complete assessment')).toHaveCount(0); // calibration approvers only

    await button(author, 'Raise NCR').click();
    await dialog(author).getByLabel('Description').fill('Parts measured since the last good calibration need re-inspection.');
    await dialog(author).getByRole('button', { name: 'Create NCR' }).click();
    await expect(author).toHaveURL(/\/ncr\/[0-9a-f-]{36}$/);

    await author.goto(`/calibration/equipment/${id}`);
    await expect(author.getByText('Quarantined after an out-of-tolerance result')).toBeVisible();
    await expect(button(author, 'Return to service')).toHaveCount(0);

    const approver = await browser.newPage();
    await signIn(approver, users.approver);
    await approver.goto(impactUrl);
    await expect(approver.getByText(/Product measured with out-of-tolerance/)).toBeVisible();
    await button(approver, 'Complete assessment').click();
    await dialog(approver).getByLabel('Affected product (parts, lots, dates)').fill('Lots 4471-4490 of H-220.');
    await dialog(approver).getByLabel('Assessment').fill('Re-measured retained samples; all within tolerance with corrected caliper.');
    await dialog(approver).getByLabel('Decision').fill('No product impact; NCR to close after re-inspection record.');
    await dialog(approver).getByRole('button', { name: 'Complete' }).click();
    await expect(approver.getByRole('heading').getByText('Completed')).toBeVisible();

    // Assessment done but the last calibration failed: still no return to service.
    await author.reload();
    await expect(button(author, 'Return to service')).toHaveCount(0);
    await button(author, 'Record calibration').click();
    await dialog(author).getByLabel('Performed by (person or lab)').fill('Metrology lab');
    await choose(author, 'Result', 'Pass after adjustment');
    await dialog(author).getByRole('button', { name: 'Save record' }).click();
    await expect(author.getByRole('cell', { name: /Pass ?after ?adjustment|PassAfterAdjustment/i })).toBeVisible();
    await expect(button(author, 'Return to service')).toHaveCount(0); // calibration approvers only

    await approver.goto(`/calibration/equipment/${id}`);
    await button(approver, 'Return to service').click();
    await expect(approver.getByRole('heading').getByText('Active')).toBeVisible();
    await expect(button(approver, 'Return to service')).toHaveCount(0);
    await api.dispose();
  });

  test('read-only and unassigned users cannot record calibrations or complete assessments', async ({ browser }) => {
    const api = await Api.as(users.author);
    const { id } = await addEquipment(api, unique('Height gauge'));
    const r = await api.post(`/api/calibration/equipment/${id}/events`, { performedOn: localDate(), performedBy: 'Metrology lab', result: 3, asFound: 'Fails at 200 mm', asLeft: null, standardsUsed: null, certificateNumber: null });

    const viewer = await browser.newPage();
    await signIn(viewer, users.viewer);
    await viewer.goto('/calibration/equipment');
    await expect(viewer.getByRole('link', { name: 'Add equipment' })).toHaveCount(0);
    await viewer.goto(`/calibration/equipment/${id}`);
    await expect(viewer.getByText('Quarantined after an out-of-tolerance result')).toBeVisible();
    for (const name of ['Record calibration', 'Return to service']) await expect(button(viewer, name)).toHaveCount(0);
    await viewer.goto(`/calibration/impact/${r.impactAssessmentId}`);
    for (const name of ['Complete assessment', 'Raise NCR']) await expect(button(viewer, name)).toHaveCount(0);

    const outsider = await browser.newPage();
    await signIn(outsider, users.outsider);
    await expect(outsider.locator('header nav').getByRole('link', { name: 'Calibration' })).toHaveCount(0);
    await outsider.goto(`/calibration/equipment/${id}`);
    await expect(outsider.getByRole('alert')).toBeVisible();
    await api.dispose();
  });
});
