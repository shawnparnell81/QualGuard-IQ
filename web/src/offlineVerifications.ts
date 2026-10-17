import { get } from './api';

/**
 * Shipments assigned to the signed-in person for scan verification, downloaded to the device so the contents can be verified
 * without a connection. The copy is kept per person, holds only what is needed to recognise the packed cartons, lots and serials
 * (no prices or addresses), expires after a shift and is wiped on sign-out. Offline scans only verify contents: the shipment
 * cannot be released until the release conditions are confirmed online.
 */
export interface VerifyItem { lineId: string; partNumber: string; lotNumber: string | null; lotRevision: string | null; serialNumber: string | null; quantity: number }
export interface VerifyContainer { id: string; number: string; kind: number; parentId: string | null; items: VerifyItem[] }
export interface VerifyLine { lineId: string; lineNumber: number; partNumber: string; revision: string | null; quantity: number; requiredLot: string | null; verified: number }
export interface VerifyList { shipmentId: string; number: string; customer: string | null; state: number; lines: VerifyLine[]; containers: VerifyContainer[]; scannedCodes: string[] }
/** scanned: codes accepted on this device (or already counted on the server) per shipment, with what they counted per line. */
export interface VerifyWork { userId: string; downloadedAt: string; lists: VerifyList[]; scanned: Record<string, { code: string; lines: Record<string, number>; containers: string[]; requestId?: string }[]> }

const PREFIX = 'qg.verify.';
export const VERIFY_WORK_HOURS = 12;
const key = (userId: string) => PREFIX + userId;

export function readVerifyWork(userId: string): VerifyWork | null {
  try {
    const w = JSON.parse(localStorage.getItem(key(userId)) ?? 'null') as VerifyWork | null;
    return w && w.userId === userId ? w : null;
  } catch { return null; }
}
const save = (w: VerifyWork) => localStorage.setItem(key(w.userId), JSON.stringify(w));
export const verifyWorkExpired = (w: VerifyWork) => Date.now() - new Date(w.downloadedAt).getTime() > VERIFY_WORK_HOURS * 3600_000;

/** Subtree of a container (the container and everything nested in it). */
export function subtree(list: VerifyList, id: string): VerifyContainer[] {
  const out: VerifyContainer[] = []; const stack = [id];
  while (stack.length) { const c = stack.pop()!; const x = list.containers.find((k) => k.id === c); if (!x || out.includes(x)) continue; out.push(x); list.containers.filter((k) => k.parentId === c).forEach((k) => stack.push(k.id)); }
  return out;
}

/** Downloads the caller's assigned shipments awaiting verification; scans still waiting to sync stay counted. */
export async function downloadVerifyWork(userId: string, pendingRequestIds: string[]): Promise<VerifyWork> {
  const r = await get<{ userId: string; downloadedAt: string; lists: VerifyList[] }>('/api/shipping/my-verifications');
  if (r.userId !== userId) throw new Error('Signed-in user changed; sign in again.');
  const prev = readVerifyWork(userId);
  const scanned: VerifyWork['scanned'] = {};
  for (const l of r.lists) {
    // server-counted codes: recount them on the device so progress matches
    const server = l.scannedCodes.map((code) => ({ code, ...countOf(l, code) }));
    const pending = (prev?.scanned[l.shipmentId] ?? []).filter((x) => x.requestId && pendingRequestIds.includes(x.requestId) && !l.scannedCodes.some((c) => c.toUpperCase() === x.code.toUpperCase()));
    scanned[l.shipmentId] = [...server, ...pending];
  }
  const w: VerifyWork = { userId, downloadedAt: r.downloadedAt, lists: r.lists, scanned };
  save(w);
  return w;
}

function countOf(l: VerifyList, code: string): { lines: Record<string, number>; containers: string[] } {
  const r = checkVerifyScan(l, [], code, null);
  return r.error ? { lines: {}, containers: [] } : { lines: r.lines!, containers: r.containers! };
}

export function markVerified(userId: string, shipmentId: string, entry: { code: string; lines: Record<string, number>; containers: string[]; requestId: string }): VerifyWork | null {
  const w = readVerifyWork(userId);
  if (!w) return null;
  (w.scanned[shipmentId] ??= []).push(entry);
  save(w);
  return w;
}

/** Forgets the device-side scans of one shipment (after a restart online). */
export function resetVerified(userId: string, shipmentId: string): VerifyWork | null {
  const w = readVerifyWork(userId);
  if (!w) return null;
  w.scanned[shipmentId] = [];
  save(w);
  return w;
}

export function clearVerifyWork() {
  for (let i = localStorage.length - 1; i >= 0; i--) { const k = localStorage.key(i); if (k?.startsWith(PREFIX)) localStorage.removeItem(k); }
}

/** Verified quantity per line from the scans made so far (device view). */
export function progress(l: VerifyList, scans: VerifyWork['scanned'][string]): Record<string, number> {
  const p: Record<string, number> = Object.fromEntries(l.lines.map((x) => [x.lineId, 0]));
  for (const s of scans) for (const [k, v] of Object.entries(s.lines)) p[k] = (p[k] ?? 0) + v;
  return p;
}

/**
 * Checks a verification scan on the device against the downloaded packing tree: carton/pallet numbers (or QG:CTN: labels), QualGuard
 * lot and serial labels. Duplicates, wrong product, wrong lot, wrong serial, wrong revision and over-count are refused at once. A code
 * the device cannot interpret is refused too (verification must be exact); the server repeats every check when the scan syncs.
 */
export function checkVerifyScan(l: VerifyList, scans: VerifyWork['scanned'][string], raw: string, quantity: number | null):
  { error?: string; lines?: Record<string, number>; containers?: string[]; needsQuantity?: boolean } {
  const code = raw.trim(); const up = code.toUpperCase();
  if (!code) return { error: 'Scan a carton, pallet or item.' };
  if (scans.some((s) => s.code.toUpperCase() === up)) return { error: `${code} was already scanned on this verification.` };
  const done = progress(l, scans);
  const counted = new Set(scans.flatMap((s) => s.containers));
  const fits = (add: Record<string, number>) => {
    for (const [k, v] of Object.entries(add)) { const line = l.lines.find((x) => x.lineId === k)!; if ((done[k] ?? 0) + v > line.quantity) return `Wrong quantity on line ${line.lineNumber}: ${(done[k] ?? 0) + v} would be verified, ${line.quantity} ordered.`; }
    return null;
  };
  const cont = l.containers.find((c) => c.number.toUpperCase() === up || `QG:CTN:${c.number}`.toUpperCase() === up);
  if (cont) {
    if (scans.some((s) => s.containers[0] === cont.id)) return { error: `${cont.number} was already scanned on this verification.` };
    if (counted.has(cont.id)) return { error: `${cont.number} is already counted in a scanned container.` };
    const sub = subtree(l, cont.id);
    if (sub.some((c) => counted.has(c.id))) return { error: `${cont.number} is already counted in a scanned container.` };
    const items = sub.flatMap((c) => c.items);
    if (scans.some((s) => s.containers.length === 0 && items.some((i) => (i.serialNumber && s.code.toUpperCase().endsWith(i.serialNumber.toUpperCase())) || (!i.serialNumber && i.lotNumber && s.code.toUpperCase().endsWith(i.lotNumber.toUpperCase())))))
      return { error: `Items in ${cont.number} were already scanned one by one; restart the verification to count by container.` };
    const add: Record<string, number> = {};
    for (const i of items) add[i.lineId] = (add[i.lineId] ?? 0) + i.quantity;
    const over = fits(add); if (over) return { error: over };
    return { lines: add, containers: sub.map((c) => c.id) };
  }
  const p = code.split(':');
  const all = l.containers.flatMap((c) => c.items.map((i) => ({ ...i, containerId: c.id })));
  let part: string | null = null; let sn: string | null = null; let lot: string | null = null;
  if (p[0] === 'QG' && p[1] === 'SN' && p.length >= 3) { sn = p.length >= 4 ? p.slice(3).join(':') : p[2]; part = p.length >= 4 ? p[2] : null; }
  else if (p[0] === 'QG' && p[1] === 'LOT' && p.length >= 3) lot = p.slice(2).join(':');
  else if (all.some((i) => i.serialNumber === code)) sn = code;
  else if (all.some((i) => i.lotNumber === code)) lot = code;
  else return { error: `${code} is not a carton, pallet, lot or serial on ${l.number}.` };
  if (part && !l.lines.some((x) => x.partNumber === part)) return { error: `Wrong product: ${part} is not on shipment ${l.number}.` };
  if (sn) {
    const it = all.find((i) => i.serialNumber === sn && (!part || i.partNumber === part));
    if (!it) return { error: `Wrong serial: SN ${sn} was not packed on this shipment.` };
    const line = l.lines.find((x) => x.lineId === it.lineId)!;
    if (line.revision && it.lotRevision && line.revision.toUpperCase() !== it.lotRevision.toUpperCase()) return { error: `Wrong revision: SN ${sn} is revision ${it.lotRevision}, line ${line.lineNumber} ships revision ${line.revision}.` };
    if (counted.has(it.containerId)) return { error: `SN ${sn} is already counted in a scanned container.` };
    const over = fits({ [it.lineId]: 1 }); if (over) return { error: over };
    return { lines: { [it.lineId]: 1 }, containers: [] };
  }
  const packed = all.filter((i) => i.lotNumber === lot);
  if (packed.length === 0) return { error: `Wrong lot: lot ${lot} was not packed on this shipment.` };
  if (packed.some((i) => i.serialNumber)) return { error: `Lot ${lot} is serialized; scan each serial label.` };
  if (packed.some((i) => counted.has(i.containerId))) return { error: `Lot ${lot} is already counted in a scanned container.` };
  const lineId = packed[0].lineId; const line = l.lines.find((x) => x.lineId === lineId)!;
  if (line.requiredLot && line.requiredLot !== lot) return { error: `Wrong lot: line ${line.lineNumber} must ship lot ${line.requiredLot}.` };
  const total = packed.reduce((a, i) => a + i.quantity, 0);
  const q = quantity ?? total;
  if (!(q > 0)) return { error: `Enter the quantity for lot ${lot}.`, needsQuantity: true };
  if (q > total) return { error: `Only ${total} of lot ${lot} was packed.` };
  const over = fits({ [lineId]: q }); if (over) return { error: over };
  return { lines: { [lineId]: q }, containers: [] };
}
