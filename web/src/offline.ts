import { useEffect, useState } from 'react';
import { post } from './api';

/**
 * Offline queue for warehouse scans. A posting made without a connection is kept in this device's storage with a
 * client request id and replayed when the connection returns; the server treats a repeated id as the same posting,
 * so a replay never books stock twice.
 */
export interface Queued { id: string; url: string; body: Record<string, unknown>; label: string; at: string; error?: string }
const KEY = 'qg.offline.queue';
const listeners = new Set<() => void>();
const read = (): Queued[] => { try { return JSON.parse(localStorage.getItem(KEY) ?? '[]'); } catch { return []; } };
const write = (q: Queued[]) => { localStorage.setItem(KEY, JSON.stringify(q)); listeners.forEach((l) => l()); };
export const newRequestId = () => (crypto.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(36).slice(2)}`);

const isNetwork = (e: unknown) => e instanceof TypeError || (e as Error)?.message === 'Failed to fetch';

/** Posts now when online; otherwise (or when the network drops mid-request) queues it. Server errors are raised, not queued. */
export async function postOrQueue<T>(url: string, body: Record<string, unknown>, label: string): Promise<{ queued: boolean; result?: T }> {
  const withId = { ...body, clientRequestId: (body.clientRequestId as string) ?? newRequestId() };
  if (!navigator.onLine) { write([...read(), { id: withId.clientRequestId as string, url, body: withId, label, at: new Date().toISOString() }]); return { queued: true }; }
  try { return { queued: false, result: await post<T>(url, withId) }; }
  catch (e) {
    if (!isNetwork(e)) throw e;
    write([...read(), { id: withId.clientRequestId as string, url, body: withId, label, at: new Date().toISOString() }]);
    return { queued: true };
  }
}

let flushing = false;
/** Replays queued postings in order. A posting the server refuses stays in the queue with its reason. */
export async function flushQueue() {
  if (flushing || !navigator.onLine) return;
  flushing = true;
  try {
    for (const item of read()) {
      try { await post(item.url, item.body); write(read().filter((x) => x.id !== item.id)); }
      catch (e) { if (isNetwork(e)) break; write(read().map((x) => (x.id === item.id ? { ...x, error: (e as Error).message } : x))); }
    }
  } finally { flushing = false; }
}
export const discardQueued = (id: string) => write(read().filter((x) => x.id !== id));

export function useOfflineQueue() {
  const [items, setItems] = useState<Queued[]>(read);
  const [online, setOnline] = useState(navigator.onLine);
  useEffect(() => {
    const l = () => setItems(read());
    const on = () => { setOnline(true); flushQueue(); };
    const off = () => setOnline(false);
    listeners.add(l); window.addEventListener('online', on); window.addEventListener('offline', off);
    const t = setInterval(() => flushQueue(), 30000);
    flushQueue();
    return () => { listeners.delete(l); window.removeEventListener('online', on); window.removeEventListener('offline', off); clearInterval(t); };
  }, []);
  return { items, online, flush: flushQueue, discard: discardQueued };
}
