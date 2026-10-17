import { test, expect, type Page } from '@playwright/test';
import { Api, dialog, localDate, signIn, state, unique, users } from './support';

const button = (p: Page, name: string | RegExp) => p.getByRole('button', { name });

async function complaint(api: Api) {
  const siteId = state().siteId;
  const { id: customerId } = await api.post<{ id: string }>('/api/master/customers', { code: unique('C').replace(' ', '-'), name: unique('Acme Pumps'), contactName: null, email: null, phone: null, address: null, siteId, departmentId: null, status: 0 });
  const { id } = await api.post<{ id: string }>('/api/complaints', {
    title: unique('Leaking valve body'), description: 'Customer reports seepage at bore A after 200 h.', customerId, customerContact: null, customerReference: 'PO-1', complaintDate: localDate(), reportedOn: localDate(),
    productId: null, partId: null, partRevision: null, partNumber: 'VB-100', lotNumber: 'L-77', batchNumber: null, serialNumber: null, quantity: 4,
    failureMode: 'Leak', severity: 1, risk: 1, warranty: 0, supplierId: null, siteId, departmentId: null, ownerId: null, responseDueOn: localDate(10),
  });
  return id;
}

test.describe('E2E 7: complaints and change control', () => {
  test('complaint goes through containment, investigation, response and acceptance, and an independent approver verifies it', async ({ browser }) => {
    const api = await Api.as(users.author);
    const id = await complaint(api);
    const author = await browser.newPage();
    await signIn(author, users.author);
    await author.goto(`/complaints/${id}`);
    await expect(button(author, 'Send customer response')).toHaveCount(0); // needs investigation first
    await button(author, 'Record containment').click();
    await dialog(author).getByLabel(/^Containment/).fill('Stock at customer and in-house quarantined.');
    await dialog(author).getByRole('button', { name: /Save|Record/ }).click();
    await expect(button(author, 'Investigation')).toBeVisible();

    await api.post(`/api/complaints/${id}/investigation`, { investigation: 'Bore measured oversize.', rootCause: 'Worn boring bar.', correctiveAction: 'Tool life limit added.' });
    await api.post(`/api/complaints/${id}/response`, { response: '8D attached.', sentOn: localDate() });
    await api.post(`/api/complaints/${id}/acceptance`, { accepted: true, feedback: 'Accepted.', on: localDate() });
    await author.reload();
    await expect(button(author, 'Verify corrective action')).toHaveCount(0); // edit role cannot verify

    const approver = await browser.newPage();
    await signIn(approver, users.approver);
    await approver.goto(`/complaints/${id}`);
    await expect(button(approver, 'Verify corrective action')).toBeVisible();
    await expect(button(approver, 'Record containment')).toHaveCount(0);
    await api.dispose();
  });

  test('a change cannot be submitted without items, and viewers and outsiders get no actions', async ({ browser }) => {
    const api = await Api.as(users.author);
    const { id } = await api.post<{ id: string }>('/api/changes', {
      title: unique('Raise bore tolerance'), changeType: 'Design change', reason: 'Recurring rejects', justification: null, currentCondition: '+0.025', proposedCondition: '+0.030',
      siteId: state().siteId, departmentId: null, ownerId: null, plannedEffectiveOn: localDate(30), costImpact: null, scheduleImpact: null, implementationPlan: null, validationPlan: null, communicationPlan: null,
    });
    expect(await api.status('POST', `/api/changes/${id}/submit`)).toBeGreaterThanOrEqual(400);

    const viewer = await browser.newPage();
    await signIn(viewer, users.viewer);
    await viewer.goto(`/changes/${id}`);
    await expect(viewer.getByText(/Raise bore tolerance/).first()).toBeVisible();
    for (const name of ['Submit for approval', 'Edit', 'Cancel']) await expect(button(viewer, name)).toHaveCount(0);

    const outsider = await browser.newPage();
    await signIn(outsider, users.outsider);
    await outsider.goto(`/changes/${id}`);
    await expect(outsider.getByRole('alert')).toBeVisible();
    await api.dispose();
  });
});
