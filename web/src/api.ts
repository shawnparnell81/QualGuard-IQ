let csrf: string | null = null;

export class ApiError extends Error {
  constructor(public status: number, message: string, public errors?: Record<string, string[]>, public code?: string, public body?: Record<string, unknown>) {
    super(message);
  }
}

async function ensureCsrf(force = false) {
  if (csrf && !force) return csrf;
  const r = await fetch('/api/auth/csrf', { credentials: 'same-origin' });
  csrf = (await r.json()).token;
  return csrf!;
}

const WORKSTATION_KEY = 'qg.workstation';
/** Name of this device for label print routing (set on the Label printing page; kept on this device only). */
export const getWorkstation = () => { try { return localStorage.getItem(WORKSTATION_KEY) ?? ''; } catch { return ''; } };
export const setWorkstation = (name: string) => { const v = name.trim().slice(0, 100); if (v) localStorage.setItem(WORKSTATION_KEY, v); else localStorage.removeItem(WORKSTATION_KEY); };

export async function api<T = unknown>(method: string, url: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = {};
  const ws = getWorkstation();
  if (ws && /^[\x20-\x7E]+$/.test(ws)) headers['X-Workstation'] = ws;
  const isForm = body instanceof FormData;
  if (method !== 'GET') headers['X-CSRF-TOKEN'] = await ensureCsrf();
  if (body !== undefined && !isForm) headers['Content-Type'] = 'application/json';
  const send = () =>
    fetch(url, { method, headers, credentials: 'same-origin', body: body === undefined ? undefined : isForm ? body : JSON.stringify(body) });
  let r = await send();
  if (r.status === 400 && method !== 'GET') {
    const peek = await r.clone().json().catch(() => null);
    if (peek?.title?.includes('CSRF')) {
      headers['X-CSRF-TOKEN'] = await ensureCsrf(true);
      r = await send();
    }
  }
  if (!r.ok) {
    const p = await r.json().catch(() => ({}));
    throw new ApiError(r.status, p.title ?? `Request failed (${r.status})`, p.errors, p.code, p);
  }
  if (r.status === 204) return undefined as T;
  const text = await r.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export const get = <T,>(url: string) => api<T>('GET', url);
export const post = <T = unknown,>(url: string, body?: unknown) => api<T>('POST', url, body ?? {});
export const put = <T = unknown,>(url: string, body: unknown) => api<T>('PUT', url, body);
export const del = <T = unknown,>(url: string) => api<T>('DELETE', url);
export const resetCsrf = () => { csrf = null; };

/** POST that returns a file (PDF previews and exports). Errors are read as problem details like other calls. */
export async function postBlob(url: string, body: unknown): Promise<Blob> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': await ensureCsrf() };
  const r = await fetch(url, { method: 'POST', headers, credentials: 'same-origin', body: JSON.stringify(body) });
  if (!r.ok) {
    const p = await r.json().catch(() => ({}));
    throw new ApiError(r.status, p.title ?? `Request failed (${r.status})`, p.errors, p.code, p);
  }
  return r.blob();
}
