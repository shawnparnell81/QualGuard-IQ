import { useEffect, useState } from 'react';
import { ApiError, post } from './api';

/**
 * Offline queue for mobile postings (warehouse scans, picks, verifications). A posting made without a connection is kept in
 * this device's storage with a client request id and replayed when the connection returns; the server treats a repeated id
 * as the same posting, so a replay never books anything twice.
 *
 * States: "local" (saved on the device, pending sync), "syncing" (being sent), "failed" (the server refused it; kept apart
 * with its reason, can be retried or discarded) and "conflict" (the server reported a conflict that needs a person to review).
 * An item leaves the queue only after the server has confirmed it (2xx). The queue survives refreshes and restarts
 * (localStorage) and is shared across tabs; only one tab sends at a time.
 */
export type QueueState = 'local' | 'syncing' | 'failed' | 'conflict';
export interface Queued { id: string; url: string; body: Record<string, unknown>; label: string; at: string; state?: QueueState; error?: string; attempts?: number }
const KEY = 'qg.offline.queue';
const SYNCED = 'qg.offline.synced';
const listeners = new Set<() => void>();
const read = (): Queued[] => { try { return (JSON.parse(localStorage.getItem(KEY) ?? '[]') as Queued[]).map((x) => ({ ...x, state: x.state ?? (x.error ? 'failed' : 'local') })); } catch { return []; } };
const notify = () => listeners.forEach((l) => l());
const write = (q: Queued[]) => { localStorage.setItem(KEY, JSON.stringify(q)); notify(); };
const update = (id: string, patch: Partial<Queued>) => write(read().map((x) => (x.id === id ? { ...x, ...patch } : x)));
export const newRequestId = () => (crypto.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(36).slice(2)}`);

const isNetwork = (e: unknown) => e instanceof TypeError || (e as Error)?.message === 'Failed to fetch' || (e instanceof ApiError && (e.status === 0 || e.status === 502 || e.status === 503 || e.status === 504));
const enqueue = (url: string, body: Record<string, unknown>, label: string) => {
  localStorage.removeItem(SYNCED);
  write([...read().filter((x) => x.id !== body.clientRequestId), { id: body.clientRequestId as string, url, body, label, at: new Date().toISOString(), state: 'local', attempts: 0 }]);
};

/** Posts now when online; otherwise (or when the network drops mid-request) queues it. Server errors are raised, not queued. */
export async function postOrQueue<T>(url: string, body: Record<string, unknown>, label: string): Promise<{ queued: boolean; result?: T }> {
  const withId = { ...body, clientRequestId: (body.clientRequestId as string) ?? newRequestId() };
  if (!navigator.onLine) { enqueue(url, withId, label); return { queued: true }; }
  try { return { queued: false, result: await post<T>(url, withId) }; }
  catch (e) {
    if (!isNetwork(e)) throw e;
    enqueue(url, withId, label);
    return { queued: true };
  }
}

let flushing = false;
const withLock = async (fn: () => Promise<void>) => {
  const locks = (navigator as Navigator & { locks?: { request: (n: string, o: { ifAvailable: boolean }, cb: (l: unknown) => Promise<void>) => Promise<void> } }).locks;
  if (locks) await locks.request('qg-offline-sync', { ifAvailable: true }, async (l) => { if (l) await fn(); });
  else await fn();
};

/** Replays queued postings in order. Refused postings stay in the queue, apart, with the reason; network failures stop the run. */
export async function flushQueue() {
  if (flushing || !navigator.onLine) return;
  flushing = true;
  try {
    await withLock(async () => {
      let accepted = 0;
      for (const item of read().filter((x) => x.state === 'local' || x.state === 'syncing')) {
        update(item.id, { state: 'syncing', attempts: (item.attempts ?? 0) + 1 });
        try {
          await post(item.url, item.body);
          write(read().filter((x) => x.id !== item.id)); // only after the server confirmed it
          accepted++;
        } catch (e) {
          if (isNetwork(e)) { update(item.id, { state: 'local' }); break; }
          update(item.id, { state: e instanceof ApiError && e.status === 409 ? 'conflict' : 'failed', error: (e as Error).message });
        }
      }
      if (accepted) { localStorage.setItem(SYNCED, JSON.stringify({ count: accepted, at: new Date().toISOString() })); notify(); }
    });
  } finally { flushing = false; }
}
export const discardQueued = (id: string) => write(read().filter((x) => x.id !== id));
/** Puts a failed item back in line and sends it again (same client request id, so the server cannot book it twice). */
export const retryQueued = async (id: string) => { update(id, { state: 'local', error: undefined }); await flushQueue(); };
export const clearSynced = () => { localStorage.removeItem(SYNCED); notify(); };
const readSynced = (): { count: number; at: string } | null => { try { return JSON.parse(localStorage.getItem(SYNCED) ?? 'null'); } catch { return null; } };

export function useOfflineQueue() {
  const [items, setItems] = useState<Queued[]>(read);
  const [synced, setSynced] = useState(readSynced);
  const [online, setOnline] = useState(navigator.onLine);
  useEffect(() => {
    const l = () => { setItems(read()); setSynced(readSynced()); };
    const on = () => { setOnline(true); flushQueue(); };
    const off = () => setOnline(false);
    const storage = (e: StorageEvent) => { if (e.key === KEY || e.key === SYNCED) l(); };
    listeners.add(l); window.addEventListener('online', on); window.addEventListener('offline', off); window.addEventListener('storage', storage);
    const t = setInterval(() => flushQueue(), 30000);
    flushQueue();
    return () => { listeners.delete(l); window.removeEventListener('online', on); window.removeEventListener('offline', off); window.removeEventListener('storage', storage); clearInterval(t); };
  }, []);
  const pending = items.filter((x) => x.state === 'local' || x.state === 'syncing');
  return { items, pending, syncing: items.filter((x) => x.state === 'syncing'), failed: items.filter((x) => x.state === 'failed'), conflicts: items.filter((x) => x.state === 'conflict'),
    synced, online, flush: flushQueue, discard: discardQueued, retry: retryQueued, clearSynced };
}
