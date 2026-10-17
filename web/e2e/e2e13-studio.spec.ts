import { test, expect, type Page } from '@playwright/test';
import { Api, signIn, state, unique, users } from './support';

const button = (p: Page, name: string | RegExp) => p.getByRole('button', { name });
async function shot(page: Page, name: string) {
  if (process.env.SHOTS_DIR) await page.screenshot({ path: `${process.env.SHOTS_DIR}/${name}.png`, fullPage: true });
}

test.describe('E2E 13: platform studio, recovery, marketplace and content (Prompt 8)', () => {
  test('designers for workflows, forms, rules and master data open from the platform side menu', async ({ page }) => {
    await signIn(page, users.admin);
    await page.goto('/platform/workflows');
    await expect(page.getByRole('heading', { name: 'Workflow designer' })).toBeVisible();
    await expect(page.getByText(/Stage 1/).first()).toBeVisible();
    await shot(page, '152-workflow-designer');
    await page.goto('/platform/forms');
    await expect(page.getByRole('heading', { name: 'Form designer' })).toBeVisible();
    await shot(page, '153-form-designer');
    await page.goto('/platform/rules');
    await expect(page.getByRole('heading', { name: 'Business rule engine' })).toBeVisible();
    await shot(page, '154-business-rules');
    await page.goto('/platform/governance');
    await expect(page.getByRole('heading', { name: 'Data governance & master data' })).toBeVisible();
    await button(page, 'Measure data quality').click();
    await expect(page.getByText('Data quality measured for every domain.')).toBeVisible();
    await shot(page, '155-data-governance');
  });

  test('recovery center shows backups, gaps and scenarios; only administrators see it', async ({ page }) => {
    const admin = await Api.as(users.admin);
    const now = new Date();
    const fp = await admin.get('/api/platform/dr/fingerprint');
    const b = await admin.post('/api/platform/dr/backups', { kind: 0, label: unique('nightly'), location: '/backups/qgiq', method: 'pg_dump custom format (scripts/backup.sh)', startedAt: new Date(now.getTime() - 120000).toISOString(), completedAt: new Date(now.getTime() - 60000).toISOString(), sizeBytes: 5242880, sha256: 'ab'.repeat(32), state: 0, fingerprint: fp });
    await admin.post(`/api/platform/dr/backups/${b.id}/verify`, { passed: true, notes: 'verify-backup.sh PASS' });
    await admin.call('POST', '/api/platform/dr/objectives', { scenario: unique('Database server loss'), rtoMinutes: 240, rpoMinutes: 1440, procedureDocumentId: null });
    await signIn(page, users.admin);
    await page.goto('/platform/recovery');
    await expect(page.getByRole('heading', { name: 'Backup & disaster recovery' })).toBeVisible();
    await expect(page.getByText('Backup ledger')).toBeVisible();
    await expect(page.getByText('Restore passed').first()).toBeVisible();
    await expect(page.getByText(/recoverability gap/)).toBeVisible();
    await shot(page, '156-recovery-center');
    expect(await (await Api.as(users.viewer)).status('GET', '/api/platform/dr/status')).toBe(403);
  });

  test('a free extension is installed from the marketplace and creates its data rules', async ({ page }) => {
    const admin = await Api.as(users.admin);
    await admin.call('DELETE', '/api/platform/marketplace/part-master-hygiene', undefined, false);
    await signIn(page, users.admin);
    await page.goto('/platform/marketplace');
    await expect(page.getByRole('heading', { name: 'Extensions marketplace' })).toBeVisible();
    const card = page.locator('.MuiPaper-root').filter({ has: page.getByRole('heading', { name: 'Part master hygiene' }) }).last();
    await card.getByRole('button', { name: 'Install' }).click();
    await expect(page.getByText('Part master hygiene installed.')).toBeVisible();
    await expect(card.getByRole('button', { name: 'Disable' })).toBeVisible();
    await expect(page.locator('.MuiPaper-root').filter({ has: page.getByRole('heading', { name: /Aerospace pack/ }) }).last().getByText('Unlicensed')).toBeVisible();
    await shot(page, '157-marketplace');
    const rules = await admin.get('/api/governance/data/rules');
    expect(rules.some((r: any) => r.domainKey === 'Part' && r.field === 'Material')).toBeTruthy();
  });

  test('a video attached to an NCR plays in the content library with its version history', async ({ page }) => {
    const author = await Api.as(users.author);
    const ncr = await author.post('/api/ncrs', { title: unique('Video NCR'), description: 'Setup', source: 'In-process', siteId: state().siteId, detectedOn: new Date().toISOString().slice(0, 10), severity: 1 });
    const name = `${unique('line-setup').replace(/\s+/g, '-')}.mp4`;
    const v1 = await author.upload('Ncr', ncr.id, name, 'not really a video, but bytes '.repeat(50), 'video/mp4');
    expect(v1.contentType).toBe('video/mp4');
    await signIn(page, users.author);
    await page.goto('/documents/content');
    await expect(page.getByRole('heading', { name: 'Content library' })).toBeVisible();
    await page.getByRole('button', { name: /Videos/ }).click();
    await page.getByText(name).click();
    await expect(page.locator('video')).toBeVisible();
    await expect(page.getByText('Version history')).toBeVisible();
    await expect(page.getByText('current')).toBeVisible();
    await shot(page, '158-content-library-video');
  });
});
