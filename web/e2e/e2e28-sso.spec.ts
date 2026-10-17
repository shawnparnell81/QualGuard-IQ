import { test, expect, type Page } from '@playwright/test';
import crypto from 'node:crypto';
import { Api, users, shot, signIn, choose, dialog } from './support';
import { startFakeOidc, FAKE_CLIENT_ID, FAKE_CLIENT_SECRET } from './fake-oidc';

/**
 * Single sign-on end to end against a local fake OpenID Connect provider: an administrator adds the provider, runs the test sign-in
 * that gates turning it on, and turns it on; then a person signs in with "Sign in with ..." and, because the provider's MFA is not
 * trusted, still enters the app's own two-step code. The provider is turned off at the end so later tests see the plain sign-in page.
 */

function base32(s: string) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  let bits = 0, value = 0; const out: number[] = [];
  for (const ch of s.replace(/[\s=-]/g, '').toUpperCase()) { value = (value << 5) | alphabet.indexOf(ch); bits += 5; if (bits >= 8) { out.push((value >>> (bits - 8)) & 0xff); bits -= 8; } }
  return Buffer.from(out);
}
const usedSteps = new Map<string, number>();
async function totp(key: string) {
  for (;;) {
    const now = Date.now() / 1000; const step = Math.floor(now / 30);
    if (step > (usedSteps.get(key) ?? -1) && now % 30 < 25) {
      usedSteps.set(key, step);
      const msg = Buffer.alloc(8); msg.writeBigUInt64BE(BigInt(step));
      const h = crypto.createHmac('sha1', base32(key)).update(msg).digest(); const o = h[h.length - 1] & 0xf;
      return String((h.readUInt32BE(o) & 0x7fffffff) % 1_000_000).padStart(6, '0');
    }
    await new Promise((r) => setTimeout(r, 1000));
  }
}
const inApp = (page: Page) => expect(page.getByRole('button', { name: 'User menu' })).toBeVisible();

test.describe.configure({ mode: 'serial' });

const domain = 'sso.qms.test';
let idp: Awaited<ReturnType<typeof startFakeOidc>>;
let admin: Api;
let providerId = '';
test.beforeAll(async () => { idp = await startFakeOidc(); admin = await Api.as(users.admin); });
test.afterAll(async () => {
  if (providerId) await admin.post(`/api/admin/sso/providers/${providerId}/disable`).catch(() => undefined);
  await admin.dispose(); await idp.close();
});

test('an administrator adds a provider, must pass a test sign-in, then turns it on; a person signs in with it and still enters the app code', async ({ page }) => {
  test.setTimeout(150_000);
  await signIn(page, users.admin);
  await page.goto('/admin/single-sign-on');
  await expect(page.getByRole('heading', { name: 'Redirect URI' })).toBeVisible();
  await expect(page.getByText('/api/auth/sso/callback')).toBeVisible();
  await page.getByRole('button', { name: 'Add identity provider' }).click();
  await choose(page, 'Provider', 'Okta');
  await dialog(page).getByLabel('Button name').fill('Okta');
  await dialog(page).getByLabel('Authority (issuer URL)').fill(idp.issuer);
  await dialog(page).getByLabel('Client ID').fill(FAKE_CLIENT_ID);
  await dialog(page).getByLabel('Client secret').fill(FAKE_CLIENT_SECRET);
  await dialog(page).getByLabel('Allowed email domains').fill(domain);
  await dialog(page).getByRole('button', { name: 'Add provider' }).click();
  await expect(dialog(page)).toHaveCount(0);
  const card = page.getByRole('region', { name: 'Identity provider Okta' });
  await expect(card.getByText('Not tested')).toBeVisible();
  await expect(card.getByRole('button', { name: 'Turn on' })).toBeDisabled();
  await expect(page.getByText(FAKE_CLIENT_SECRET)).toHaveCount(0); // the secret is never shown again
  providerId = (await admin.get('/api/admin/sso')).providers.find((p: any) => p.displayName === 'Okta').id;

  // connection check, then the test sign-in at the provider
  await card.getByRole('button', { name: 'Check connection' }).click();
  await expect(card.getByText(/Signing keys:/)).toBeVisible();
  await card.getByRole('button', { name: 'Test sign-in' }).click();
  await expect(page.getByRole('heading', { name: 'Test identity provider' })).toBeVisible();
  await page.getByLabel('Email').fill(`it.admin@${domain}`);
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page.getByText('Test sign-in with Okta passed. You can turn it on now.')).toBeVisible();
  await expect(card.getByText('Test passed')).toBeVisible();
  await expect(card.getByText(/ID token:.*Signature, issuer, audience, expiry and nonce are valid/)).toBeVisible();
  await card.getByRole('button', { name: 'Turn on' }).click();
  await expect(card.getByText('On', { exact: true })).toBeVisible();
  await shot(page, 'sign-in-42-admin-single-sign-on');

  // a person with two-step verification on signs in through the provider
  const email = `robin.sso@${domain}`; const pw = 'meadow lantern copper 8';
  await admin.post('/api/admin/users', { email, displayName: 'Robin Sso', password: pw, mustChangePassword: false });
  const me = await Api.as({ key: 'robin', email, password: pw, name: 'Robin Sso' });
  const setup = await me.post<{ key: string }>('/api/account/mfa/setup', { password: pw });
  await me.post('/api/account/mfa/enable', { code: await totp(setup.key) });
  await me.dispose();

  await page.getByRole('button', { name: 'User menu' }).click();
  await page.getByRole('menuitem', { name: 'Sign out' }).click();
  await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible();
  await page.getByLabel('Email').fill(email);
  await expect(page.getByRole('button', { name: 'Sign in with Okta' })).toBeVisible();
  await shot(page, 'sign-in-40-sign-in-with-sso');
  await page.getByRole('button', { name: 'Sign in with Okta' }).click();
  await expect(page.getByRole('heading', { name: 'Test identity provider' })).toBeVisible();
  await expect(page.getByLabel('Email')).toHaveValue(email); // login_hint passed through
  await page.getByRole('button', { name: 'Continue' }).click();
  // the provider's MFA is not trusted for this provider, so the app's own code is still asked
  await expect(page.getByRole('heading', { name: 'Two-step verification' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'User menu' })).toHaveCount(0);
  expect(page.url()).not.toContain('sso='); // the one-time ticket is removed from the address
  await page.getByLabel('Authentication code').fill(await totp(setup.key));
  await shot(page, 'sign-in-41-sso-then-two-step-code');
  await page.getByRole('button', { name: 'Verify' }).click();
  await inApp(page);
  const events = JSON.stringify(await admin.get('/api/security-events?pageSize=200'));
  expect(events).toContain('SsoAccountLinked');
  expect(events).toContain('SsoProviderTested');
});

test('unknown people are told to ask an administrator', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Sign in with Okta' }).click();
  await page.getByLabel('Email').fill(`nobody@${domain}`);
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page.getByRole('alert')).toContainText('There is no account for you yet');
  await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible();
  await shot(page, 'sign-in-43-sso-no-account');
});
