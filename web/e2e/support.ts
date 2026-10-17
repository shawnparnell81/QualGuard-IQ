import { request, expect, type APIRequestContext, type Page } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

export const WEB = process.env.E2E_WEB_URL ?? 'http://127.0.0.1:5174';
const STATE = path.join(import.meta.dirname, '.state.json');

export interface User { key: string; email: string; password: string; name: string; id?: string }
export const users = {
  admin: { key: 'admin', email: 'e2e-admin@qms.test', password: 'e2e-admin-password', name: 'Test Administrator' },
  author: { key: 'author', email: 'author@qms.test', password: 'author-password-1', name: 'Alex Author' },
  approver: { key: 'approver', email: 'approver@qms.test', password: 'approver-password-1', name: 'Pat Approver' },
  viewer: { key: 'viewer', email: 'viewer@qms.test', password: 'viewer-password-1', name: 'Val Viewer' },
  outsider: { key: 'outsider', email: 'outsider@qms.test', password: 'outsider-password-1', name: 'Oscar Outsider' },
} satisfies Record<string, User>;

export interface State { siteId: string; ids: Record<string, string> }
export const saveState = (s: State) => fs.writeFileSync(STATE, JSON.stringify(s));
export const state = (): State => JSON.parse(fs.readFileSync(STATE, 'utf8'));

/** Business date in the site's zone (America/New_York), matching the server. */
export const localDate = (addDays = 0) => {
  const d = new Date(Date.now() + addDays * 86_400_000);
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'America/New_York', year: 'numeric', month: '2-digit', day: '2-digit' }).format(d);
};
export const unique = (label: string) => `${label} ${Date.now().toString(36)}`;

export class Api {
  private constructor(private ctx: APIRequestContext, private csrf: string) {}
  static async as(u: User) {
    const ctx = await request.newContext({ baseURL: WEB });
    const token = async () => (await (await ctx.get('/api/auth/csrf')).json()).token as string;
    const r = await ctx.post('/api/auth/login', { data: { email: u.email, password: u.password }, headers: { 'X-CSRF-TOKEN': await token() } });
    if (!r.ok()) throw new Error(`login ${u.email}: ${r.status()} ${await r.text()}`);
    return new Api(ctx, await token());
  }
  async call<T = any>(method: string, url: string, data?: unknown, expectOk = true): Promise<T> {
    const r = await this.ctx.fetch(url, { method, data: data as any, headers: { 'X-CSRF-TOKEN': this.csrf } });
    if (expectOk && !r.ok()) throw new Error(`${method} ${url}: ${r.status()} ${await r.text()}`);
    const t = await r.text();
    return (t ? JSON.parse(t) : null) as T;
  }
  status = async (method: string, url: string, data?: unknown) => (await this.ctx.fetch(url, { method, data: data as any, headers: { 'X-CSRF-TOKEN': this.csrf } })).status();
  get = <T = any>(url: string) => this.call<T>('GET', url);
  post = <T = any>(url: string, data?: unknown) => this.call<T>('POST', url, data ?? {});
  put = <T = any>(url: string, data?: unknown) => this.call<T>('PUT', url, data);
  async upload(type: string, id: string, name: string, body: string, mimeType = 'application/pdf') {
    const r = await this.ctx.post(`/api/records/${type}/${id}/files`, {
      headers: { 'X-CSRF-TOKEN': this.csrf },
      multipart: { file: { name, mimeType, buffer: Buffer.from(body) }, classification: '0' },
    });
    if (!r.ok()) throw new Error(`upload: ${r.status()} ${await r.text()}`);
    return r.json();
  }
  /** Signs the pending approval for a record number (as this user). */
  async sign(recordNumber: string, password: string, approve = true, comment: string | null = null) {
    const work = await this.get('/api/my/work');
    const a = work.approvals.find((x: any) => x.recordNumber.startsWith(recordNumber));
    if (!a) throw new Error(`No approval waiting for ${recordNumber}`);
    await this.post(`/api/my/approvals/${a.id}/sign`, { approve, password, comment });
  }
  dispose = () => this.ctx.dispose();
}

export async function signIn(page: Page, u: User) {
  await page.goto('/');
  await page.getByLabel('Email').fill(u.email);
  await page.getByLabel('Password').fill(u.password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('button', { name: 'User menu' })).toBeVisible();
}

/** Chooses an option in an MUI select identified by its label. */
export async function choose(page: Page, label: string | RegExp, option: string | RegExp) {
  await page.getByRole('combobox', { name: label }).click();
  await page.getByRole('option', { name: option }).click();
}

export const dialog = (page: Page) => page.getByRole('dialog');

/** Saves a screenshot when E2E_SCREENSHOT_DIR is set (documentation captures); does nothing otherwise. */
export async function shot(page: Page, name: string) {
  const dir = process.env.E2E_SCREENSHOT_DIR;
  if (!dir) return;
  fs.mkdirSync(dir, { recursive: true });
  await page.waitForLoadState('networkidle').catch(() => undefined);
  await page.evaluate(() => window.scrollTo(0, 0)); // the sticky top bar would otherwise sit mid-image
  const wide = (page.viewportSize()?.width ?? 1440) >= 900; // phone captures keep the screen size
  await page.screenshot({ path: path.join(dir, `${name}.png`), fullPage: wide });
}
