import { test, expect, type Page } from '@playwright/test';
import { Api, choose, dialog, signIn, state, unique, users } from './support';

const button = (p: Page, name: string | RegExp) => p.getByRole('button', { name });
async function shot(page: Page, name: string) {
  if (process.env.SHOTS_DIR) await page.screenshot({ path: `${process.env.SHOTS_DIR}/${name}.png`, fullPage: true });
}

test.describe('E2E 11: engineering, Quality OS and knowledge (Prompt 7)', () => {
  test('a requirement is captured, approved and shows in the traceability matrix', async ({ page, browser }) => {
    await signIn(page, users.author);
    await page.goto('/engineering');
    await expect(page.getByRole('heading', { name: 'Product requirements' })).toBeVisible();
    await button(page, 'New requirement').click();
    const title = unique('Burst pressure 300 bar');
    await dialog(page).getByLabel('Title').fill(title);
    await dialog(page).getByLabel('Requirement text').fill('Housing withstands 300 bar for 60 s without leakage.');
    await choose(page, 'Site', /E2E/);
    await dialog(page).getByLabel('Customer / standard reference').fill('CS-PUMP-12 §4.2');
    await button(page, 'Create').click();
    await expect(page.getByRole('heading', { name: new RegExp(title) })).toBeVisible();
    await expect(page.getByText('Coverage')).toBeVisible();
    await expect(button(page, 'Approve')).toHaveCount(0); // needs engineering.approve
    const url = page.url();
    const ap = await (await browser.newContext()).newPage();
    await signIn(ap, users.approver);
    await ap.goto(url);
    await button(ap, 'Approve').click();
    await expect(button(ap, 'Approve')).toHaveCount(0);
    await shot(ap, '127-requirement-detail');
    await page.goto('/engineering');
    await page.getByRole('tab', { name: 'Traceability matrix' }).click();
    await expect(page.getByTestId('trace-matrix')).toContainText(title);
    await shot(page, '128-traceability-matrix');
  });

  test('a design review gate collects findings and blocks on open risks', async ({ page }) => {
    const api = await Api.as(users.author);
    const title = unique('Housing design review');
    const productId = await api.post<any>('/api/master/products', { number: unique('PR').replace(' ', '-'), name: 'Pump housing', category: 'Pumps', siteId: state().siteId, status: 0 }).then((r) => r.id ?? r);
    const { id } = await api.post<{ id: string }>('/api/engineering/reviews', { title, kind: 2, siteId: state().siteId, productId });
    await signIn(page, users.author);
    await page.goto('/engineering/reviews');
    await expect(page.getByTestId('gate-Design')).toContainText(title);
    await shot(page, '129-design-review-gates');
    await page.goto(`/engineering/reviews/${id}`);
    await button(page, 'Add finding').click();
    await dialog(page).getByLabel('Finding').fill('Port wall thickness below minimum at core shift');
    await choose(page, /Design risk/, 'Yes');
    await button(page, 'Add').click();
    await expect(page.getByText('Port wall thickness below minimum')).toBeVisible();
    await button(page, 'Record meeting').click();
    await dialog(page).getByLabel('Held on').fill(new Date().toISOString().slice(0, 10));
    await dialog(page).getByLabel('Decision').fill('Proceed after wall thickness fix');
    await button(page, 'Save').click();
    await button(page, 'Submit for approval').click();
    await expect(page.getByRole('alert').filter({ hasText: /risk/i })).toBeVisible();
    await shot(page, '130-design-review-detail');
    await api.dispose();
  });

  test('command center, risk matrix and KPI framework render global data', async ({ page }) => {
    await signIn(page, users.author);
    await page.goto('/qos');
    await expect(page.getByRole('heading', { name: 'Global quality command center' })).toBeVisible();
    await expect(page.getByTestId('gauge').first()).toBeVisible();
    await shot(page, '131-command-center');
    await page.getByRole('link', { name: 'Risk matrix' }).first().click();
    await expect(page.getByTestId('heatmap')).toBeVisible();
    await shot(page, '132-enterprise-risk-matrix');
    await page.goto('/qos/kpis');
    await expect(page.getByTestId('kpi-level-Corporate')).toBeVisible();
    await shot(page, '133-kpi-framework');
    await page.goto('/qos/factory');
    await expect(page.getByTestId('lane-NCRs')).toBeVisible();
    await shot(page, '134-factory-center');
  });

  test('a 5 Why lesson is published and found by similar-issue search', async ({ page }) => {
    const api = await Api.as(users.author);
    const tag = Date.now().toString(36);
    const { id } = await api.post<{ id: string }>('/api/knowledge', { kind: 4, title: `Seal weep ${tag}`, body: `Port seal weeping ${tag} after endurance test`, siteId: state().siteId, whys: ['Seal weeps', 'Groove finish rough', 'Tool worn', 'No tool life limit'], rootCause: 'No tool life limit on groove insert', correctiveAction: 'Tool life counter' });
    await signIn(page, users.author);
    await page.goto(`/knowledge/${id}`);
    await expect(page.getByTestId('why-chain')).toContainText('Tool worn');
    await button(page, 'Publish').click();
    await expect(button(page, 'Unpublish')).toBeVisible();
    await shot(page, '135-knowledge-five-why');
    await page.goto('/knowledge');
    await page.getByRole('tab', { name: 'Find similar issues' }).click();
    await page.getByLabel('Describe the issue').fill(`seal weeping ${tag} at port`);
    await button(page, 'Find similar').click();
    await expect(page.getByTestId('similar-hit').first()).toContainText(`Seal weep ${tag}`);
    await shot(page, '136-similar-issues');
    await api.dispose();
  });

  test('playbooks, process chain and maturity', async ({ page }) => {
    const api = await Api.as(users.author);
    await api.post('/api/knowledge/playbooks', { title: unique('Customer escape'), trigger: 1, steps: [{ title: 'Contain', guidance: 'Quarantine stock', dueDays: 1 }, { title: '8D', guidance: 'Open 8D', dueDays: 10 }] });
    const code = `P${Date.now().toString(36).slice(-4).toUpperCase()}`;
    const proc = await api.post<{ id: string }>('/api/processes', { code, name: 'Purchasing', purpose: 'Buy conforming product', siteId: state().siteId, reviewIntervalDays: 365 });
    await api.post('/api/maturity', { subject: 1, subjectId: proc.id, assessedOn: new Date().toISOString().slice(0, 10), scores: [{ area: 'Leadership', level: 3 }, { area: 'Process control', level: 4 }] });
    await signIn(page, users.author);
    await page.goto('/knowledge/playbooks');
    await expect(page.getByTestId('playbook').first()).toBeVisible();
    await shot(page, '137-playbooks');
    await page.goto('/processes');
    await expect(page.getByTestId('process-chain')).toContainText(code);
    await page.getByTestId('process-chain').getByText('Purchasing').first().click();
    await expect(button(page, 'Record review')).toBeVisible();
    await shot(page, '138-process-chain');
    await page.goto('/maturity');
    await page.getByRole('tab', { name: 'Process' }).click();
    await expect(page.getByTestId('maturity-row').first()).toBeVisible();
    await shot(page, '139-maturity');
    await api.dispose();
  });

  test('the advisor answers with records and the document assistant checks text', async ({ page }) => {
    await signIn(page, users.author);
    await page.goto('/advisor');
    await page.getByPlaceholder(/Ask about/).fill('open NCRs');
    await button(page, 'Send').click();
    await expect(page.getByTestId('chat-log').locator('p').nth(1)).toBeVisible();
    await shot(page, '140-advisor-chat');
    await page.goto('/advisor/documents');
    await page.getByLabel('Title').fill('Receiving inspection');
    await button(page, 'Draft outline').click();
    await expect(page.getByTestId('doc-result')).toContainText(/Purpose|Scope/i);
    await shot(page, '141-document-assistant');
  });

  test('an access certification snapshots role assignments for decisions', async ({ page }) => {
    await signIn(page, users.admin);
    await page.goto('/governance/access');
    await button(page, 'Start review').click();
    await dialog(page).getByLabel('Title').fill(unique('Q4 access certification'));
    await button(page, 'Start').click();
    await expect(page.getByRole('button', { name: 'Keep' }).first()).toBeVisible();
    await page.getByRole('button', { name: 'Keep' }).first().click();
    await expect(page.getByText(/pending\)/)).toBeVisible();
    await shot(page, '142-access-certification');
  });
});
