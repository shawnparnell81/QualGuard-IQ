import { useEffect, useRef, useState } from 'react';
import { Box, Button, LinearProgress, MenuItem, Paper, Stack, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { get } from '../api';
import { mono } from '../theme';
import { tones } from '../brand';

export const stockStatus = ['Available', 'AwaitingInspection', 'Quarantined', 'Rejected', 'Mrb', 'Damaged', 'InTransit', 'AwaitingDisposition'];
export const stockStatusLabel = ['Available', 'Awaiting inspection', 'Quarantined', 'Rejected', 'MRB', 'Damaged', 'In transit', 'Awaiting disposition'];
export const purposes = ['Storage', 'Receiving', 'Inspection', 'Quarantine', 'Production', 'WIP', 'Shipping', 'Returns', 'Scrap', 'MRB', 'Consigned', 'In transit'];
export const locKinds = ['Warehouse', 'Building', 'Zone', 'Aisle', 'Row', 'Rack', 'Bin', 'Floor'];
export const txKinds = ['Receipt', 'Issue', 'Consumption', 'Adjustment', 'Transfer', 'Return', 'Scrap', 'Rework return', 'Shipment', 'Count adjustment', 'Status change', 'Production receipt', 'Supplier return', 'Pick', 'ERP mirror', 'Lot split', 'Lot merge'];
export const categories = ['Raw material', 'Component', 'WIP', 'Finished goods', 'Spare parts', 'Consigned'];
export const holdKinds = ['Quality', 'Supplier', 'Engineering', 'Customer'];
export const holdStatus = ['Active', 'ReleaseRequested', 'Released', 'Cancelled'];
export const receiptKinds = ['Purchase order', 'Supplier delivery', 'ASN', 'Blind'];
export const receiptStatus = ['Open', 'Received', 'Inspecting', 'Completed', 'Cancelled'];
export const lineStatus = ['Pending', 'AwaitingInspection', 'Accepted', 'Rejected', 'Quarantined', 'PartiallyAccepted'];
export const mrbDisp = ['None', 'Accept', 'Rework', 'Repair', 'Scrap', 'Return to supplier', 'Use as is'];
export const mrbStatus = ['Open', 'PendingApproval', 'Approved', 'Rejected', 'Executed', 'Cancelled'];
export const rtvStatus = ['Open', 'Shipped', 'Credited', 'Closed', 'Cancelled'];
export const countFreq = ['Daily', 'Weekly', 'Monthly', 'Annual', 'Physical inventory'];
export const countStatus = ['Counting', 'PendingApproval', 'Posted', 'Rejected', 'Cancelled'];
export const transferKinds = ['Warehouse', 'Site', 'Department'];
export const transferStatus = ['Open', 'InTransit', 'Received', 'Cancelled'];
export const carrierKinds = ['Parcel', 'LTL', 'FTL', 'Customer carrier', 'Courier'];
export const shipKinds = ['Customer', 'Supplier return', 'Intercompany', 'Sample'];
export const shipStatus = ['Draft', 'Picking', 'Picked', 'Packed', 'Verified', 'Shipped', 'Delivered', 'Cancelled'];
export const pickStatus = ['Open', 'Picked', 'Short', 'Cancelled'];
export const containerKinds = ['Carton', 'Pallet', 'Container', 'Tote'];

export interface LocationRow { id: string; code: string; name: string; kind: number; purpose: number; parentId?: string; path: string; capacity?: number; active: boolean; frozen: boolean; onHand: number; items: number; siteId: string }
export interface StockRow { id: string; partId: string; partNumber: string; partName?: string; revision?: string; lotId?: string; lotNumber?: string; serialId?: string; serialNumber?: string; locationId: string; locationCode: string;
  locationPurpose: number; status: number; category: number; onHand: number; reserved: number; allocated: number; available: number; unitOfMeasure: string; workOrderId?: string; workOrderNumber?: string; held: boolean; lastMovedAt?: string; siteId: string }
export interface TxRow { id: string; number: string; kind: number; occurredAt: string; partNumber: string; revision?: string; lotNumber?: string; serialNumber?: string; fromLocation?: string; fromStatus?: number; toLocation?: string; toStatus?: number;
  quantity: number; unitOfMeasure: string; workOrder?: string; refType?: string; refId?: string; refNumber?: string; reason?: string; source: string | number; performedBy: string }
export interface Carrier { id: string; code: string; name: string; kind: number; trackingUrl?: string; scac?: string; active: boolean }

export const useLocations = (siteId?: string) => useQuery({ queryKey: ['wh-locations', siteId ?? ''], queryFn: () => get<LocationRow[]>(`/api/warehouse/locations${siteId ? `?siteId=${siteId}` : ''}`) });
export const useCarriers = () => useQuery({ queryKey: ['carriers'], queryFn: () => get<Carrier[]>('/api/shipping/carriers') });

/** Location select; optional purposes narrow it (e.g. only quarantine locations). Shows the path so bins are unambiguous. */
export function LocationSelect({ label, value, onChange, siteId, purposes: only, allowNone, size }: { label: string; value: string; onChange: (v: string) => void; siteId?: string; purposes?: number[]; allowNone?: boolean; size?: 'small' | 'medium' }) {
  const locs = useLocations(siteId);
  const list = (locs.data ?? []).filter((l) => l.active && (!only || only.includes(l.purpose)));
  return (
    <TextField select size={size} label={label} value={value} onChange={(e) => onChange(e.target.value)}>
      {allowNone && <MenuItem value="">None</MenuItem>}
      {list.map((l) => <MenuItem key={l.id} value={l.id}><span style={{ fontFamily: mono }}>{l.path}</span>&nbsp;· {purposes[l.purpose]}{l.frozen ? ' · frozen' : ''}</MenuItem>)}
    </TextField>
  );
}

/** Tone per stock status: available stock is green, anything blocked is rust, transit is slate. */
export const statusTone = (s: number) => (s === 0 ? tones.ok : s === 6 ? tones.slate : s === 1 ? tones.warn : tones.bad);
export function StockBadge({ status, held }: { status: number; held?: boolean }) {
  const tone = held && status === 0 ? tones.bad : statusTone(status);
  return <Box component="span" sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.75, fontSize: 12.5, color: tone, fontWeight: 600 }}>
    <Box component="span" sx={{ width: 8, height: 8, borderRadius: '2px', bgcolor: tone }} />{held && status === 0 ? 'On hold' : stockStatusLabel[status]}</Box>;
}

/** Metric tile with numerator / denominator, used by the warehouse and logistics dashboards. */
export function MetricTile({ label, value, unit, num, den, note, onClick }: { label: string; value?: number | null; unit: string; num?: number | null; den?: number | null; note?: string; onClick?: () => void }) {
  return (
    <Paper variant="outlined" onClick={onClick} sx={{ p: 1.75, cursor: onClick ? 'pointer' : undefined, borderTop: 3, borderTopColor: 'primary.main', '&:hover': onClick ? { boxShadow: 2 } : undefined }}>
      <Typography variant="caption" color="text.secondary">{label}</Typography>
      <Typography sx={{ fontSize: 26, fontWeight: 600, fontFamily: mono, lineHeight: 1.2 }}>{value == null ? '—' : value.toLocaleString()}<Typography component="span" sx={{ fontSize: 13, ml: 0.5 }} color="text.secondary">{unit}</Typography></Typography>
      {num != null && den != null && <Typography variant="caption" color="text.secondary" sx={{ fontFamily: mono }}>{num.toLocaleString()} / {den.toLocaleString()}</Typography>}
      {note && <Typography variant="caption" display="block" color="text.secondary">{note}</Typography>}
    </Paper>
  );
}

export function FillBar({ pct }: { pct?: number | null }) {
  if (pct == null) return <Typography variant="caption" color="text.secondary">No capacity set</Typography>;
  return <Stack direction="row" alignItems="center" gap={1}><LinearProgress variant="determinate" value={Math.min(100, pct)} sx={{ flex: 1, height: 10, borderRadius: 1, bgcolor: 'divider', '& .MuiLinearProgress-bar': { bgcolor: pct > 90 ? tones.bad : pct > 70 ? tones.warn : tones.ok } }} /><Typography sx={{ fontFamily: mono, fontSize: 12.5, width: 52, textAlign: 'right' }}>{pct}%</Typography></Stack>;
}

/**
 * Scanner field: a keyboard-wedge scanner types the code and presses Enter; the camera button decodes with ZXing
 * (Code 128, Code 39, Data Matrix and QR) on phones and tablets.
 */
export function ScanField({ label, onScan, autoFocus, value, onChange, helper }: { label: string; onScan: (code: string) => void; autoFocus?: boolean; value?: string; onChange?: (v: string) => void; helper?: string }) {
  const [own, setOwn] = useState('');
  const v = value ?? own; const set = onChange ?? setOwn;
  const [camera, setCamera] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const video = useRef<HTMLVideoElement>(null);
  useEffect(() => {
    if (!camera) return;
    let stop: (() => void) | undefined;
    (async () => {
      try {
        const { BrowserMultiFormatReader } = await import('@zxing/browser');
        const reader = new BrowserMultiFormatReader();
        const controls = await reader.decodeFromVideoDevice(undefined, video.current!, (res) => { const t = res?.getText(); if (t) { set(t); onScan(t); setCamera(false); } });
        stop = () => controls.stop();
      } catch (e) { setErr((e as Error).message || 'Camera not available'); setCamera(false); }
    })();
    return () => stop?.();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [camera]);
  return (
    <Box>
      <Stack direction="row" gap={1} alignItems="flex-start">
        <TextField fullWidth autoFocus={autoFocus} label={label} value={v} helperText={err ?? helper} error={!!err}
          onChange={(e) => set(e.target.value)} onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); if (v.trim()) onScan(v.trim()); } }}
          sx={{ '& input': { fontFamily: mono, fontSize: 18, py: 1.6 } }} />
        <Button variant="outlined" onClick={() => { setErr(null); setCamera(!camera); }} sx={{ minWidth: 92, height: 56 }}>{camera ? 'Stop' : 'Camera'}</Button>
      </Stack>
      {camera && <Box sx={{ mt: 1 }}><video ref={video} style={{ width: '100%', maxHeight: 260, objectFit: 'cover', borderRadius: 6, background: '#000' }} muted playsInline /></Box>}
    </Box>
  );
}
