import { get } from './api';

/**
 * Pick work downloaded to this device for offline picking. Only the signed-in picker's own assigned, open picks are downloaded
 * (the server filters them). The copy is kept per person, holds only what a picker needs to find and confirm the material
 * (no prices, customer addresses or other record data), expires after a shift, and is wiped on sign-out.
 */
export interface MobilePickTask { taskId: string; lineNumber: number; partNumber: string; revision: string | null; lotNumber: string | null; supplierLotNumber: string | null; serialNumber: string | null; locationCode: string; quantity: number; unitOfMeasure: string }
export interface MobilePickList { shipmentId: string; number: string; customer: string | null; stagingLocation: string; assignedAt: string | null; tasks: MobilePickTask[] }
export interface PickWork { userId: string; downloadedAt: string; lists: MobilePickList[]; done: Record<string, { requestId: string; at: string }> }

const PREFIX = 'qg.picks.';
export const PICK_WORK_HOURS = 12;
const key = (userId: string) => PREFIX + userId;

export function readPickWork(userId: string): PickWork | null {
  try {
    const w = JSON.parse(localStorage.getItem(key(userId)) ?? 'null') as PickWork | null;
    return w && w.userId === userId ? w : null;
  } catch { return null; }
}
const save = (w: PickWork) => localStorage.setItem(key(w.userId), JSON.stringify(w));

export const pickWorkExpired = (w: PickWork) => Date.now() - new Date(w.downloadedAt).getTime() > PICK_WORK_HOURS * 3600_000;

/** Downloads the caller's assigned open picks; confirmations still waiting to sync stay marked done so they are not picked twice. */
export async function downloadPickWork(userId: string, pendingTaskIds: string[]): Promise<PickWork> {
  const r = await get<{ userId: string; downloadedAt: string; lists: MobilePickList[] }>('/api/shipping/my-picks');
  if (r.userId !== userId) throw new Error('Signed-in user changed; sign in again.');
  const prev = readPickWork(userId);
  const done: PickWork['done'] = {};
  for (const id of pendingTaskIds) if (prev?.done[id]) done[id] = prev.done[id];
  const w: PickWork = { userId, downloadedAt: r.downloadedAt, lists: r.lists, done };
  save(w);
  return w;
}

export function markPicked(userId: string, taskId: string, requestId: string): PickWork | null {
  const w = readPickWork(userId);
  if (!w) return null;
  w.done[taskId] = { requestId, at: new Date().toISOString() };
  save(w);
  return w;
}

/** Wipes every downloaded pick list on this device (sign-out). */
export function clearPickWork() {
  for (let i = localStorage.length - 1; i >= 0; i--) { const k = localStorage.key(i); if (k?.startsWith(PREFIX)) localStorage.removeItem(k); }
}

export const normalizeLocation = (code: string) => code.trim().replace(/^QG:LOC:/i, '').toUpperCase();

/**
 * Checks a scanned item against the downloaded pick on the device. QualGuard labels are checked exactly (wrong part, lot or
 * serial is refused at once); a code the device cannot interpret (for example a supplier GS1 label) is accepted with a warning
 * and the server checks it when the pick syncs.
 */
export function checkPickItem(t: MobilePickTask, code: string): { error?: string; warning?: string } {
  const v = code.trim(); const p = v.split(':');
  const needs = t.serialNumber ? `Scan the serial label (SN ${t.serialNumber}).` : t.lotNumber ? `Scan the lot label (lot ${t.lotNumber}).` : null;
  if (p[0] === 'QG' && p[1] === 'SN' && p.length >= 3) {
    const sn = p.length >= 4 ? p.slice(3).join(':') : p[2]; const pn = p.length >= 4 ? p[2] : null;
    if (pn && pn !== t.partNumber) return { error: `Wrong part scanned: pick ${t.partNumber}.` };
    if (!t.serialNumber) return t.lotNumber ? { warning: 'Serial label scanned; the server checks it belongs to the lot.' } : {};
    return sn === t.serialNumber ? {} : { error: `Wrong serial scanned: pick SN ${t.serialNumber}.` };
  }
  if (p[0] === 'QG' && p[1] === 'LOT' && p.length >= 3) {
    const lot = p.slice(2).join(':');
    if (t.lotNumber && lot !== t.lotNumber && lot !== t.supplierLotNumber) return { error: `Wrong lot scanned: pick lot ${t.lotNumber}.` };
    return t.serialNumber ? { error: needs! } : {};
  }
  if (/^QG:PART:/i.test(v)) return v.slice(8) !== t.partNumber ? { error: `Wrong part scanned: pick ${t.partNumber}.` } : needs ? { error: needs } : {};
  if (t.serialNumber && v === t.serialNumber) return {};
  if (t.lotNumber && (v === t.lotNumber || v === t.supplierLotNumber)) return t.serialNumber ? { error: needs! } : {};
  if (v === t.partNumber) return needs ? { error: needs } : {};
  return { warning: 'This label is not recognised on the device; the server checks it when the pick syncs.' };
}
