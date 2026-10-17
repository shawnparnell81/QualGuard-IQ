import { test, expect, type Page } from '@playwright/test';
import { Api, localDate, signIn, state, users, shot } from './support';

/** Gap 5: customer and supplier portal journeys in the browser, plus the same boundaries checked directly against the API. */
const PW = 'portal-password-long-1';

async function portalSignIn(page: Page, email: string, kind: 'Customer' | 'Supplier') {
  await page.goto('/');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password', { exact: true }).fill(PW);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.getByText(`${kind} portal`, { exact: true })).toBeVisible();
}

async function signOutAndCheck(page: Page, privateUrl: string, privateText: string) {
  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page.getByLabel('Email')).toBeVisible();
  await page.goto(privateUrl); // a bookmarked portal link needs a new sign-in
  await expect(page.getByLabel('Email')).toBeVisible();
  await expect(page.getByText(privateText)).toHaveCount(0);
  expect((await page.request.get('/api/portal/me')).status()).toBe(401);
}

test.describe('E2E 15: customer and supplier portals', () => {
  test('a customer sees only its own complaints, the response and corrective actions once sent, uploads evidence, and cannot open another customer by URL', async ({ page }) => {
    const admin = await Api.as(users.admin);
    const t = Date.now().toString(36);
    const siteId = state().siteId;
    const customer = async (p: string) => (await admin.post<{ id: string }>('/api/master/customers', { code: `${p}${t}`.toUpperCase(), name: `${p} Pumps ${t}`, contactName: null, email: null, phone: null, address: null, siteId, departmentId: null, status: 0 })).id;
    const a = await customer('ca'); const b = await customer('cb');
    const complaint = async (customerId: string, title: string, ref: string) => (await admin.post<{ id: string }>('/api/complaints', {
      title, description: 'Seepage at bore A after 200 h.', customerId, customerContact: null, customerReference: ref, complaintDate: localDate(), reportedOn: localDate(),
      productId: null, partId: null, partRevision: null, partNumber: 'VB-100', lotNumber: 'L-77', batchNumber: null, serialNumber: null, quantity: 4,
      failureMode: 'Leak', severity: 1, risk: 1, warranty: 0, supplierId: null, siteId, departmentId: null, ownerId: null, responseDueOn: localDate(10) })).id;
    const answered = await complaint(a, `Leaking valve ${t}`, `ACME-${t}`);
    const open = await complaint(a, `Cracked housing ${t}`, `ACME2-${t}`);
    const foreign = await complaint(b, `Other customer issue ${t}`, `OTH-${t}`);
    const capaAnswered = await admin.post<{ number: string }>(`/api/complaints/${answered}/capa`, { title: `Seal groove finish ${t}`, description: 'Leak on returns' });
    const capaOpen = await admin.post<{ number: string }>(`/api/complaints/${open}/capa`, { title: `Housing crack ${t}`, description: 'Under investigation' });
    await admin.post(`/api/complaints/${answered}/containment`, { text: 'Stock quarantined.' });
    await admin.post(`/api/complaints/${answered}/investigation`, { investigation: 'Bench test.', rootCause: 'Groove finish out of specification.', correctiveAction: 'Finish check added.' });
    await admin.post(`/api/complaints/${answered}/response`, { response: `8D report ${t} sent with containment and root cause.`, sentOn: localDate() });
    const emailA = `cust-a-${t}@customer.test`;
    await admin.post('/api/portal/admin/accounts', { email: emailA, displayName: 'Jo Ortiz', kind: 'Customer', partyId: a, password: PW, mustChangePassword: false });
    await admin.post('/api/portal/admin/accounts', { email: `cust-b-${t}@customer.test`, displayName: 'Bo Other', kind: 'Customer', partyId: b, password: PW, mustChangePassword: false });

    await portalSignIn(page, emailA, 'Customer');
    await expect(page.getByTestId('portal-party')).toHaveText(`ca Pumps ${t}`);
    await expect(page.getByRole('tab', { name: 'Complaints' })).toHaveAttribute('aria-selected', 'true');
    await expect(page.getByRole('tab', { name: 'Corrective actions' })).toHaveCount(0); // supplier areas are not offered
    const list = page.getByRole('table');
    await expect(list).toContainText(`Leaking valve ${t}`);
    await expect(list).toContainText(`Cracked housing ${t}`);
    await expect(list).not.toContainText(`Other customer issue ${t}`);

    // Answered complaint: our response, root cause and the linked corrective action are shown.
    await page.getByRole('row').filter({ hasText: `Leaking valve ${t}` }).click();
    await expect(page).toHaveURL(new RegExp(`/portal/complaints/${answered}$`));
    await expect(page.getByText(`8D report ${t} sent with containment and root cause.`)).toBeVisible();
    await shot(page, '05-customer-portal-complaint');
    await expect(page.getByText('Groove finish out of specification.')).toBeVisible();
    await expect(page.getByText(new RegExp(`${capaAnswered.number} — Seal groove finish ${t}`))).toBeVisible();
    await page.getByRole('button', { name: '← Back' }).click();
    await expect(page).toHaveURL(/\/portal\/complaints$/);

    // Open complaint: no response yet, so corrective actions stay hidden. Evidence upload and a message.
    await page.getByRole('row').filter({ hasText: `Cracked housing ${t}` }).click();
    await expect(page.getByText(`ACME2-${t}`)).toBeVisible();
    await expect(page.getByText('Our response:')).toHaveCount(0);
    await expect(page.getByText(capaOpen.number)).toHaveCount(0);
    await page.getByTestId('portal-evidence-file').setInputFiles({ name: `crack-photo-${t}.txt`, mimeType: 'text/plain', buffer: Buffer.from('photo of crack') });
    await expect(page.getByTestId('portal-evidence')).toContainText(`crack-photo-${t}.txt`);
    await page.getByPlaceholder('Write a message').fill('Two more housings cracked at our line.');
    await page.getByRole('button', { name: 'Send' }).click();
    await expect(page.getByText('Two more housings cracked at our line.')).toBeVisible();

    // Navigation: quality report tab has its own address.
    await page.getByRole('tab', { name: 'Quality report' }).click();
    await expect(page).toHaveURL(/\/portal\/report$/);
    await expect(page.getByText('Open Complaints')).toBeVisible();

    // Direct URLs: another customer's complaint and a supplier-only area.
    await page.goto(`/portal/complaints/${foreign}`);
    await expect(page.getByTestId('portal-record-missing')).toBeVisible();
    await shot(page, '05-customer-portal-not-shared');
    await expect(page.getByText(`Other customer issue ${t}`)).toHaveCount(0);
    await page.goto('/portal/scars');
    await expect(page.getByTestId('portal-unavailable')).toBeVisible();
    // The API refuses the same requests, not just the UI.
    expect((await page.request.get(`/api/portal/complaints/${foreign}`)).status()).toBe(404);
    expect((await page.request.get('/api/portal/scars')).status()).toBe(403);
    expect((await page.request.get(`/api/complaints/${answered}`)).status()).toBe(403);
    expect((await page.request.get('/api/portal/admin/accounts')).status()).toBe(403);

    await page.goto(`/portal/complaints/${answered}`);
    await expect(page.getByText(`8D report ${t} sent with containment and root cause.`)).toBeVisible();
    await signOutAndCheck(page, `/portal/complaints/${answered}`, `Leaking valve ${t}`);
  });

  test('a supplier sees only its own NCRs and SCARs, responds when the SCAR waits for it, uploads evidence, sees its scorecard and reported audits, and cannot open another supplier by URL', async ({ page }) => {
    const admin = await Api.as(users.admin);
    const t = Date.now().toString(36);
    const siteId = state().siteId;
    const me = await admin.get<{ id: string }>('/api/auth/me');
    const supplier = async (p: string) => (await admin.post<{ id: string }>('/api/suppliers', { code: `${p}${t}`.toUpperCase(), legalName: `${p} Castings ${t}`, category: 'Castings', criticality: 1, risk: 1, siteId })).id;
    const a = await supplier('sa'); const b = await supplier('sb');
    const ncr = async (sid: string, title: string) => (await admin.post<{ id: string }>('/api/ncrs', { title, description: 'Porosity found at machining.', source: 'Supplier', siteId, detectedOn: localDate(), severity: 1, details: { supplierId: sid } })).id;
    const ncrA = await ncr(a, `Porosity lot 12 ${t}`); const ncrB = await ncr(b, `Other supplier NCR ${t}`);
    const scar = async (sid: string, ncrId: string, title: string) => (await admin.post<{ id: string }>('/api/suppliers/scars', { supplierId: sid, ncrId, title, problem: 'Porosity in 3 of 50 castings.', responseDueOn: localDate(14) })).id;
    const scarA = await scar(a, ncrA, `Casting porosity ${t}`); const scarB = await scar(b, ncrB, `Other supplier SCAR ${t}`);
    const audit = async (title: string, report: boolean) => {
      const { id } = await admin.post<{ id: string }>('/api/audits', { title, auditType: 'Supplier', scope: 'Casting process', criteria: null, siteId, departmentId: null, leadAuditorId: me.id,
        plannedStart: localDate(), plannedEnd: localDate(1), checklist: [{ clause: '8.4', question: 'Are special processes controlled?' }] });
      await admin.post(`/api/records/Audit/${id}/links`, { toType: 'Supplier', toId: a, linkType: 'Audited supplier' });
      if (report) {
        await admin.post(`/api/audits/${id}/start`);
        const d = await admin.get<{ checklist: { id: string }[] }>(`/api/audits/${id}`);
        await admin.put(`/api/audits/${id}/checklist`, [{ itemId: d.checklist[0].id, result: 2, evidence: 'No degassing record for lot 12.' }]);
        await admin.post(`/api/audits/${id}/findings`, { checklistItemId: d.checklist[0].id, findingType: 1, clause: '8.4', statement: `Degassing not recorded ${t}`, evidence: 'Lot 12', responsibleId: me.id, responseDueOn: localDate(20) });
        await admin.post(`/api/audits/${id}/reporting`, { text: 'One minor finding.' });
      }
    };
    await audit(`Process audit ${t}`, true);
    await audit(`Planned follow-up ${t}`, false);
    const emailA = `sup-a-${t}@supplier.test`;
    await admin.post('/api/portal/admin/accounts', { email: emailA, displayName: 'Rae Hale', kind: 'Supplier', partyId: a, password: PW, mustChangePassword: false });

    await portalSignIn(page, emailA, 'Supplier');
    await expect(page.getByTestId('portal-party')).toHaveText(`sa Castings ${t}`);
    await expect(page.getByRole('tab', { name: 'Complaints' })).toHaveCount(0);
    await expect(page.getByRole('table')).toContainText(`Casting porosity ${t}`);
    await expect(page.getByRole('table')).not.toContainText(`Other supplier SCAR ${t}`);

    // Supplier NCRs open.
    await page.getByRole('tab', { name: 'Nonconformances' }).click();
    await expect(page).toHaveURL(/\/portal\/ncrs$/);
    await expect(page.getByRole('table')).not.toContainText(`Other supplier NCR ${t}`);
    await page.getByRole('row').filter({ hasText: `Porosity lot 12 ${t}` }).click();
    await expect(page).toHaveURL(new RegExp(`/portal/ncrs/${ncrA}$`));
    await expect(page.getByText('Porosity found at machining.')).toBeVisible();
    await page.getByTestId('portal-evidence-file').setInputFiles({ name: `sort-${t}.txt`, mimeType: 'text/plain', buffer: Buffer.from('sorted 200 pcs') });
    await expect(page.getByTestId('portal-evidence')).toContainText(`sort-${t}.txt`);

    // SCAR: the response form is offered while the SCAR waits for the supplier, and locked afterwards.
    await page.getByRole('tab', { name: 'Corrective actions' }).click();
    await page.getByRole('row').filter({ hasText: `Casting porosity ${t}` }).click();
    await expect(page).toHaveURL(new RegExp(`/portal/scars/${scarA}$`));
    await page.getByLabel('Containment').fill('Sorted 2,000 pcs at our site.');
    await page.getByLabel('Root cause').fill('Degassing time shortened.');
    await page.getByLabel('Corrective action plan').fill('Restore degassing time; add density check.');
    await page.getByRole('button', { name: 'Submit response' }).click();
    await expect(page.getByTestId('scar-response-locked')).toContainText('Your response has been received');
    await expect(page.getByRole('button', { name: 'Submit response' })).toHaveCount(0);
    await expect(page.getByText('Degassing time shortened.')).toBeVisible();
    const staff = await admin.get<{ status: number; supplierRespondent: string }>(`/api/suppliers/scars/${scarA}`);
    expect(staff.status).toBe(1);
    expect(staff.supplierRespondent).toContain('supplier portal');
    expect((await page.request.post(`/api/portal/scars/${scarA}/response`, { data: { containment: 'x', rootCause: 'y', correctiveActionPlan: 'z' }, headers: { 'X-CSRF-TOKEN': await csrf(page) } })).status()).toBe(409);

    // Scorecard and audits.
    await page.getByRole('tab', { name: 'Scorecard' }).click();
    await expect(page).toHaveURL(/\/portal\/scorecard$/);
    await expect(page.getByRole('table')).toBeVisible();
    await page.getByRole('tab', { name: 'Audits' }).click();
    await expect(page.getByTestId('portal-audit')).toHaveCount(1);
    await expect(page.getByTestId('portal-audit')).toContainText(`Process audit ${t}`);
    await expect(page.getByTestId('portal-audit')).toContainText(`Degassing not recorded ${t}`);
    await expect(page.getByText(`Planned follow-up ${t}`)).toHaveCount(0);
    await shot(page, '05-supplier-portal-audits');

    // Direct URLs to another supplier's records, and to the customer area.
    await page.goto(`/portal/scars/${scarB}`);
    await expect(page.getByTestId('portal-record-missing')).toBeVisible();
    await page.goto(`/portal/ncrs/${ncrB}`);
    await expect(page.getByTestId('portal-record-missing')).toBeVisible();
    await expect(page.getByText(`Other supplier NCR ${t}`)).toHaveCount(0);
    await page.goto('/portal/complaints');
    await expect(page.getByTestId('portal-unavailable')).toBeVisible();
    expect((await page.request.get(`/api/portal/scars/${scarB}`)).status()).toBe(404);
    expect((await page.request.get(`/api/portal/ncrs/${ncrB}`)).status()).toBe(404);
    expect((await page.request.get('/api/portal/complaints')).status()).toBe(403);
    expect((await page.request.get(`/api/ncrs/${ncrA}`)).status()).toBe(403);
    expect((await page.request.get('/api/supplier-development/scorecard')).status()).toBe(403);

    await page.goto(`/portal/scars/${scarA}`);
    await expect(page.getByText('Degassing time shortened.')).toBeVisible();
    await shot(page, '05-supplier-portal-scar');
    await signOutAndCheck(page, `/portal/scars/${scarA}`, `Casting porosity ${t}`);
  });
});

test('staff sign out returns to the sign-in page and leaves no session behind', async ({ page }) => {
  await signIn(page, users.viewer);
  await page.getByRole('button', { name: 'User menu' }).click();
  await page.getByRole('menuitem', { name: 'Sign out' }).click();
  await expect(page.getByLabel('Email')).toBeVisible();
  expect((await page.request.get('/api/auth/me')).status()).toBe(401);
});

async function csrf(page: Page) {
  return (await (await page.request.get('/api/auth/csrf')).json()).token as string;
}
