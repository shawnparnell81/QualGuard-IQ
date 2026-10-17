import { test, expect, type Page } from '@playwright/test';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { Api, users, shot, unique } from './support';

/**
 * Sign-in security end to end: first sign-in with a temporary password, two-step verification set up from the Security page,
 * sign-in with an authenticator code, a recovery code and a trusted browser, enrolment forced by a role, forgotten password by email,
 * an expired step, and the phone layout. Codes come from an independent RFC 6238 generator below.
 */

const MAIL = process.env.E2E_MAIL_DIR ?? '/tmp/qgiq-e2e-mail';

function base32(s: string) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  let bits = 0, value = 0; const out: number[] = [];
  for (const ch of s.replace(/[\s=-]/g, '').toUpperCase()) {
    value = (value << 5) | alphabet.indexOf(ch); bits += 5;
    if (bits >= 8) { out.push((value >>> (bits - 8)) & 0xff); bits -= 8; }
  }
  return Buffer.from(out);
}
function totpAt(key: string, step: number) {
  const msg = Buffer.alloc(8); msg.writeBigUInt64BE(BigInt(step));
  const h = crypto.createHmac('sha1', base32(key)).update(msg).digest();
  const o = h[h.length - 1] & 0xf;
  return String((h.readUInt32BE(o) & 0x7fffffff) % 1_000_000).padStart(6, '0');
}
const used = new Map<string, number>();
/** A code for a time step not used before for this key (the server refuses a reused step), with time left to submit it. */
async function code(key: string) {
  for (;;) {
    const now = Date.now() / 1000; const step = Math.floor(now / 30);
    if (step > (used.get(key) ?? -1) && now % 30 < 25) { used.set(key, step); return totpAt(key, step); }
    await new Promise((r) => setTimeout(r, 1000));
  }
}

/** Latest reset email for an address, decoded, returning the link it carries. */
async function resetLink(email: string) {
  for (let i = 0; i < 40; i++) {
    const files = fs.existsSync(MAIL) ? fs.readdirSync(MAIL).map((f) => path.join(MAIL, f)).sort((a, b) => fs.statSync(b).mtimeMs - fs.statSync(a).mtimeMs) : [];
    for (const f of files) {
      const raw = fs.readFileSync(f, 'utf8');
      if (!raw.toLowerCase().includes(email.toLowerCase()) || !/Subject:.*Reset/i.test(raw)) continue;
      const qp = raw.replace(/=\r?\n/g, '').replace(/=([0-9A-F]{2})/g, (_, h) => String.fromCharCode(parseInt(h, 16)));
      const b64 = [...raw.matchAll(/Content-Transfer-Encoding: base64\r?\n(?:[^\r\n]+\r?\n)*\r?\n([A-Za-z0-9+/=\r\n]+)/gi)]
        .map((m) => Buffer.from(m[1].replace(/\s/g, ''), 'base64').toString('utf8')).join('\n');
      const m = (qp + '\n' + b64).match(/https?:\/\/[^\s"'<>]+\/reset-password\?token=[A-Za-z0-9_-]+/);
      if (m) return m[0];
    }
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error(`No reset email for ${email}`);
}

async function signInForm(page: Page, email: string, password: string) {
  await page.goto('/');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
}
async function signOut(page: Page) {
  await page.getByRole('button', { name: 'User menu' }).click();
  await page.getByRole('menuitem', { name: 'Sign out' }).click();
  await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
}
const inApp = (page: Page) => expect(page.getByRole('button', { name: 'User menu' })).toBeVisible();

test.describe.configure({ mode: 'serial' });

let admin: Api;
test.beforeAll(async () => { admin = await Api.as(users.admin); });
test.afterAll(async () => { await admin.dispose(); });

const stamp = Date.now().toString(36);
const person = { email: `quinn.${stamp}@qms.test`, name: 'Quinn Inspector', temp: 'harbour-lantern-temp-71', mine: 'copper kettle under maple 9' };
let mfaKey = '';
let recovery: string[] = [];

test('first sign-in with a temporary password asks for a new password before anything else', async ({ page }) => {
  await admin.post('/api/admin/users', { email: person.email, displayName: person.name, password: person.temp });
  await signInForm(page, person.email, person.temp);
  await expect(page.getByRole('heading', { name: 'Choose a new password' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'User menu' })).toHaveCount(0);
  // a common password is refused by the server's offline list
  await page.getByLabel('New password', { exact: true }).fill('password123456');
  await page.getByLabel('Confirm new password', { exact: true }).fill('password123456');
  await page.getByRole('button', { name: 'Save password and continue' }).click();
  await expect(page.getByText(/common|breach/i).first()).toBeVisible();
  await page.getByLabel('New password', { exact: true }).fill(person.mine);
  await page.getByLabel('Confirm new password', { exact: true }).fill(person.mine);
  await expect(page.getByRole('list', { name: 'Password rules' })).toBeVisible();
  await shot(page, 'sign-in-03-first-sign-in-new-password');
  await page.getByRole('button', { name: 'Save password and continue' }).click();
  await inApp(page);
});

test('two-step verification set up from the Security page, with recovery codes', async ({ page }) => {
  test.setTimeout(150_000);
  await signInForm(page, person.email, person.mine);
  await inApp(page);
  await page.getByRole('button', { name: 'User menu' }).click();
  await page.getByRole('menuitem', { name: 'Security' }).click();
  await expect(page.getByRole('heading', { name: 'Security', exact: true })).toBeVisible();
  await shot(page, 'sign-in-10-security-page-off');
  await page.getByRole('button', { name: 'Set up two-step verification' }).click();
  const d = page.getByRole('dialog');
  await d.getByLabel('Your password', { exact: true }).fill(person.mine);
  await d.getByRole('button', { name: 'Continue' }).click();
  await expect(d.getByRole('img', { name: /QR code/ })).toBeVisible();
  mfaKey = (await d.getByTestId('mfa-key').innerText()).replace(/\s/g, '');
  expect(mfaKey).toMatch(/^[A-Z2-7]{32}$/);
  await shot(page, 'sign-in-11-security-set-up-qr');
  await d.getByLabel('Authentication code').fill('000000');
  await d.getByRole('button', { name: 'Turn on' }).click();
  await expect(d.getByRole('alert')).toBeVisible();
  await d.getByLabel('Authentication code').fill(await code(mfaKey));
  await d.getByRole('button', { name: 'Turn on' }).click();
  const list = d.getByTestId('recovery-codes');
  await expect(list.locator('li')).toHaveCount(10);
  recovery = (await list.locator('li').allInnerTexts()).map((t) => t.trim());
  expect(recovery[0]).toMatch(/^[a-z0-9]{5}-[a-z0-9]{5}$/);
  await shot(page, 'sign-in-12-recovery-codes');
  await expect(d.getByRole('button', { name: 'Done' })).toBeDisabled();
  await d.getByLabel('I have saved my recovery codes').check();
  await d.getByRole('button', { name: 'Done' }).click();
  await expect(page.getByText('Two-step verification is on.')).toBeVisible();
  await expect(page.getByText('10 of 10 recovery codes left.')).toBeVisible();
  await shot(page, 'sign-in-13-security-page-on');
});

test('sign-in asks for the code; trusting the browser skips it next time until it is removed', async ({ page }) => {
  test.setTimeout(150_000);
  await signInForm(page, person.email, person.mine);
  await expect(page.getByRole('heading', { name: 'Two-step verification' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'User menu' })).toHaveCount(0);
  await page.getByLabel('Authentication code').fill('123456');
  await page.getByRole('button', { name: 'Verify' }).click();
  await expect(page.getByRole('alert')).toContainText(/code/i);
  await page.getByLabel(/Trust this browser for 30 days/).check();
  await page.getByLabel('Authentication code').fill(await code(mfaKey));
  await shot(page, 'sign-in-02-authenticator-code');
  await page.getByRole('button', { name: 'Verify' }).click();
  await inApp(page);
  await page.goto('/account/security');
  await expect(page.getByText('This browser', { exact: true })).toBeVisible();
  await shot(page, 'sign-in-14-trusted-browsers');
  await signOut(page);
  await signInForm(page, person.email, person.mine);
  await inApp(page); // trusted: no code asked
  await page.goto('/account/security');
  await page.getByRole('button', { name: 'Remove all trusted browsers' }).click();
  await expect(page.getByText(/1 browser removed/)).toBeVisible();
  await expect(page.getByText('No trusted browsers.')).toBeVisible();
  await signOut(page);
  await signInForm(page, person.email, person.mine);
  await expect(page.getByRole('heading', { name: 'Two-step verification' })).toBeVisible();
});

test('a recovery code signs in once and cannot be used again', async ({ page }) => {
  await signInForm(page, person.email, person.mine);
  await page.getByRole('button', { name: "Can't use your app? Use a recovery code" }).click();
  await page.getByLabel('Recovery code').fill(recovery[0]);
  await shot(page, 'sign-in-04-recovery-code');
  await page.getByRole('button', { name: 'Verify' }).click();
  await inApp(page);
  await page.goto('/account/security');
  await expect(page.getByText('9 of 10 recovery codes left.')).toBeVisible();
  await signOut(page);
  await signInForm(page, person.email, person.mine);
  await page.getByRole('button', { name: "Can't use your app? Use a recovery code" }).click();
  await page.getByLabel('Recovery code').fill(recovery[0]);
  await page.getByRole('button', { name: 'Verify' }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(page.getByRole('button', { name: 'User menu' })).toHaveCount(0);
});

test('forgotten password: neutral answer, emailed single-use link, then the code is still asked', async ({ page }) => {
  test.setTimeout(150_000);
  await page.goto('/');
  await page.getByRole('button', { name: 'Forgot your password?' }).click();
  await page.getByLabel('Email').fill(person.email);
  await shot(page, 'sign-in-05-forgot-password');
  await page.getByRole('button', { name: 'Email me a reset link' }).click();
  await expect(page.getByRole('heading', { name: 'Check your email' })).toBeVisible();
  await shot(page, 'sign-in-06-reset-email-sent');
  // the same answer for an address that has no account
  await page.getByRole('button', { name: 'Back to sign in' }).click();
  await page.getByRole('button', { name: 'Forgot your password?' }).click();
  await page.getByLabel('Email').fill(`nobody.${stamp}@qms.test`);
  await page.getByRole('button', { name: 'Email me a reset link' }).click();
  await expect(page.getByRole('heading', { name: 'Check your email' })).toBeVisible();

  const link = await resetLink(person.email);
  await page.goto(link.replace(/^https?:\/\/[^/]+/, ''));
  await expect(page.getByRole('heading', { name: 'Choose a new password' })).toBeVisible();
  const next = 'violet harbour seventeen kites';
  await page.getByLabel('New password', { exact: true }).fill(person.mine); // in the recent history
  await page.getByLabel('Confirm new password', { exact: true }).fill(person.mine);
  await page.getByRole('button', { name: 'Save new password' }).click();
  await expect(page.getByText(/recent|last \d|reuse|used before/i).first()).toBeVisible();
  await page.getByLabel('New password', { exact: true }).fill(next);
  await page.getByLabel('Confirm new password', { exact: true }).fill(next);
  await shot(page, 'sign-in-07-reset-new-password');
  await page.getByRole('button', { name: 'Save new password' }).click();
  await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
  await expect(page).not.toHaveURL(/token=/);
  await page.getByLabel('Email').fill(person.email);
  await page.getByLabel('Password', { exact: true }).fill(next);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { name: 'Two-step verification' })).toBeVisible();
  await page.getByLabel('Authentication code').fill(await code(mfaKey));
  await page.getByRole('button', { name: 'Verify' }).click();
  await inApp(page);
  person.mine = next;
  // the link is single use
  await page.context().clearCookies();
  await page.goto(link.replace(/^https?:\/\/[^/]+/, ''));
  await expect(page.getByRole('heading', { name: 'This link no longer works' })).toBeVisible();
  await shot(page, 'sign-in-08-reset-link-used');
});

test('a role that requires two-step verification forces set-up at sign-in', async ({ page }) => {
  test.setTimeout(150_000);
  const perms = await admin.get<string[]>('/api/admin/roles/permissions');
  const roleName = unique('Release signers');
  const roleId = await admin.post<string>('/api/admin/roles', { name: roleName, description: 'Signs product release', permissions: [perms[0]], requiresMfa: true });
  const email = `rory.${stamp}@qms.test`; const pw = 'granite orchard lamp 42';
  const id = await admin.post<string>('/api/admin/users', { email, displayName: 'Rory Release', password: pw, mustChangePassword: false });
  await admin.post(`/api/admin/users/${id}/roles`, { roleId, siteId: null, departmentId: null });

  await signInForm(page, email, pw);
  await expect(page.getByRole('heading', { name: 'Set up two-step verification' })).toBeVisible();
  await expect(page.getByText(new RegExp(roleName))).toBeVisible();
  await expect(page.getByTestId('mfa-key')).toBeVisible();
  await shot(page, 'sign-in-09-forced-set-up');
  const key = (await page.getByTestId('mfa-key').innerText()).replace(/\s/g, '');
  await page.getByLabel('Authentication code').fill(await code(key));
  await page.getByRole('button', { name: 'Turn on and continue' }).click();
  await expect(page.getByTestId('recovery-codes').locator('li')).toHaveCount(10);
  await page.getByLabel('I have saved my recovery codes').check();
  await page.getByRole('button', { name: /Continue to/ }).click();
  await inApp(page);
  await page.goto('/account/security');
  await expect(page.getByRole('button', { name: 'Turn off' })).toBeDisabled();
  await expect(page.getByText(new RegExp(`${roleName}`)).first()).toBeVisible();
  await admin.put(`/api/admin/roles/${roleId}/mfa`, { requiresMfa: false });
});

test('a sign-in step that runs out says so and offers to start again', async ({ page }) => {
  await page.clock.install();
  await signInForm(page, person.email, person.mine);
  await expect(page.getByRole('heading', { name: 'Two-step verification' })).toBeVisible();
  await page.clock.fastForward('06:00');
  await expect(page.getByRole('heading', { name: 'Start again' })).toBeVisible();
  await expect(page.getByText(/5 minutes/)).toBeVisible();
  await shot(page, 'sign-in-15-step-expired');
  await page.getByRole('button', { name: 'Start again' }).click();
  await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
});

test('administrators: sign-in security policy, temporary password and 2-step reset', async ({ page }) => {
  const { signIn } = await import('./support');
  await signIn(page, users.admin);
  await page.goto('/admin/sign-in-security');
  await expect(page.getByRole('heading', { name: 'Two-step verification' })).toBeVisible();
  await expect(page.getByText('Common and breached passwords refused')).toBeVisible();
  await shot(page, 'sign-in-20-admin-sign-in-security');
  await page.goto('/admin/users');
  const row = page.getByRole('row').filter({ hasText: person.email });
  await expect(row.getByText('2-step on')).toBeVisible();
  await row.getByRole('button', { name: 'Reset 2-step' }).click();
  await page.getByRole('dialog').getByLabel(/Reason/).fill('Lost phone, identity checked by video call');
  await shot(page, 'sign-in-21-admin-reset-2-step');
  await page.getByRole('dialog').getByRole('button', { name: 'Reset' }).click();
  await expect(page.getByText(/Two-step verification reset for/)).toBeVisible();
  await row.getByRole('button', { name: 'Set temporary password' }).click();
  const temp = 'cobalt meadow ferry 58';
  await page.getByRole('dialog').getByLabel('Temporary password', { exact: true }).fill(temp);
  await page.getByRole('dialog').getByLabel('Confirm temporary password', { exact: true }).fill(temp);
  await shot(page, 'sign-in-22-admin-temporary-password');
  await page.getByRole('dialog').getByRole('button', { name: 'Set password' }).click();
  await expect(row.getByText('Must change password')).toBeVisible();
  await expect(row.getByText('2-step on')).toHaveCount(0);
  await signOut(page);
  await signInForm(page, person.email, temp);
  await expect(page.getByRole('heading', { name: 'Choose a new password' })).toBeVisible();
});

test.describe('phone', () => {
  test.use({ viewport: { width: 390, height: 844 } });
  test('sign-in and the code step fit a phone screen', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
    await shot(page, 'sign-in-30-phone-sign-in');
    const email = `pia.${stamp}@qms.test`; const pw = 'sapphire tunnel violin 3';
    await admin.post('/api/admin/users', { email, displayName: 'Pia Phone', password: pw, mustChangePassword: false });
    const me = await Api.as({ key: 'pia', email, password: pw, name: 'Pia Phone' });
    const setup = await me.post<{ key: string }>('/api/account/mfa/setup', { password: pw });
    await me.post('/api/account/mfa/enable', { code: await code(setup.key) });
    await me.dispose();
    await signInForm(page, email, pw);
    await expect(page.getByRole('heading', { name: 'Two-step verification' })).toBeVisible();
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    expect(overflow).toBe(false);
    await expect(page.getByLabel('Authentication code')).toHaveAttribute('inputmode', 'numeric');
    await shot(page, 'sign-in-31-phone-code');
    await page.getByLabel('Authentication code').fill(await code(setup.key));
    await page.getByRole('button', { name: 'Verify' }).click();
    await inApp(page);
    await page.goto('/account/security');
    await shot(page, 'sign-in-32-phone-security');
  });
});

test('sign-in screen', async ({ page }) => {
  await page.goto('/');
  await page.getByLabel('Email').fill(person.email);
  await shot(page, 'sign-in-01-sign-in');
});
