import type { FieldDef } from '../pages/FormBuilder';

/** Form schema version 2 as stored on a template revision. A version 1 schema (array of fields) is read as one implicit section. */
export interface Condition { field: string; operator: string; value?: string | null }
export interface BandText { left?: string | null; center?: string | null; right?: string | null }
export interface PageSetup { size: string; orientation: 'portrait' | 'landscape'; marginMm: number; header?: BandText | null; footer?: BandText | null; watermark?: string | null; printStyle: 'standardized' | 'original'; fontSize: number; confidentiality?: string }
export interface SectionDef { key: string; title: string; parent?: string | null; instructions?: string | null; columns: number; repeatable?: boolean; minRepeat?: number | null; maxRepeat?: number | null; repeatLabel?: string | null; visibleWhen?: Condition | null; orientation?: string | null; pageBreakBefore?: boolean; keepTogether?: boolean }
export interface ColumnDef extends FieldDef { columns?: ColumnDef[] | null }
export interface TableTotal { column: string; function: string; label?: string | null }
export interface TableDef { key: string; title: string; section?: string | null; instructions?: string | null; columns: ColumnDef[]; headerGroups?: { label: string; span: number }[] | null; fixedRows?: string[] | null; minRows: number; maxRows: number; totals?: TableTotal[] | null; runningTotals?: string[] | null; subtotalBy?: string | null; repeatHeaderOnEachPage?: boolean; visibleWhen?: Condition | null }
export interface SignatureDef { key: string; label: string; role: string; meaning: string; section?: string | null; order: number; required: boolean; requiredPermission?: string | null; distinctFrom?: string[] | null }
export interface CellStyle { font?: 'sans' | 'serif' | 'mono' | null; fontName?: string | null; size?: number | null; bold?: boolean; italic?: boolean; underline?: boolean; colour?: string | null; fill?: string | null; align?: 'left' | 'center' | 'right' | null; vAlign?: 'top' | 'middle' | 'bottom' | null; wrap?: boolean; borderTop?: string | null; borderRight?: string | null; borderBottom?: string | null; borderLeft?: string | null; borderColour?: string | null; numberFormat?: string | null }
export interface LayoutCell { r: number; c: number; rowSpan: number; colSpan: number; text?: string | null; bind?: string | null; style?: CellStyle | null }
export interface LayoutGrid { columnWidths: number[]; rowHeights: number[]; cells: LayoutCell[]; repeatRows: number; repeat?: { table: string; startRow: number; rowCount: number; sourceRows: number } | null }
export interface LayoutBlock { type: 'grid' | 'paragraph' | 'pagebreak'; origin?: string | null; orientation?: string | null; grid?: LayoutGrid | null; text?: string | null; style?: CellStyle | null; indent?: number; spaceBefore?: number; spaceAfter?: number }
export interface SourceLayout { source: string; fileName?: string | null; blocks: LayoutBlock[] }
export interface FormDef { schemaVersion: number; page: PageSetup; sections: SectionDef[]; fields: ColumnDef[]; tables: TableDef[]; signatures: SignatureDef[]; layout?: SourceLayout | null }

export const defaultPage: PageSetup = { size: 'Letter', orientation: 'portrait', marginMm: 15, printStyle: 'standardized', fontSize: 9 };

export function parseDefinition(json?: string | null | object): FormDef {
  let raw: unknown = json;
  if (typeof json === 'string') { try { raw = JSON.parse(json); } catch { raw = []; } }
  if (Array.isArray(raw)) return { schemaVersion: 1, page: { ...defaultPage }, sections: [], fields: raw as ColumnDef[], tables: [], signatures: [] };
  const d = (raw ?? {}) as Partial<FormDef>;
  return { schemaVersion: d.schemaVersion ?? 2, page: { ...defaultPage, ...(d.page ?? {}) }, sections: d.sections ?? [], fields: d.fields ?? [], tables: d.tables ?? [], signatures: d.signatures ?? [], layout: d.layout ?? null };
}

export const isV2 = (json?: string | null) => !!json && json.trimStart().startsWith('{');

/** Mirrors the server's condition operators (Application/Rules/Conditions). */
export function matches(c: Condition, values: Record<string, string | null | undefined>): boolean {
  const a = (values[c.field] ?? '').trim(); const b = (c.value ?? '').trim(); const na = Number(a), nb = Number(b);
  const num = a !== '' && b !== '' && !isNaN(na) && !isNaN(nb); const cmp = num ? na - nb : a.localeCompare(b);
  switch (c.operator) {
    case 'equals': return a !== '' && a.toLowerCase() === b.toLowerCase(); case 'notEquals': return a.toLowerCase() !== b.toLowerCase();
    case 'greaterThan': return a !== '' && cmp > 0; case 'greaterOrEqual': return a !== '' && cmp >= 0; case 'lessThan': return a !== '' && cmp < 0; case 'lessOrEqual': return a !== '' && cmp <= 0;
    case 'contains': return a.toLowerCase().includes(b.toLowerCase()); case 'in': return b.split(',').map((x) => x.trim().toLowerCase()).includes(a.toLowerCase());
    case 'isEmpty': return a === ''; case 'isNotEmpty': return a !== ''; default: return true;
  }
}

/** Sections hidden by their own rule or by a hidden parent. */
export function hiddenSections(d: FormDef, values: Record<string, string | null>): Set<string> {
  const hidden = new Set(d.sections.filter((s) => s.visibleWhen && !matches(s.visibleWhen, values)).map((s) => s.key));
  let changed = true;
  while (changed) { changed = false; for (const s of d.sections) if (!hidden.has(s.key) && s.parent && hidden.has(s.parent)) { hidden.add(s.key); changed = true; } }
  return hidden;
}

export type Row = Record<string, string | null>;
export interface TableValue { rows: Row[]; totals?: Record<string, string | null>; subtotals?: Record<string, string | null>[] }
export function readTable(v: string | null | undefined): TableValue {
  if (!v) return { rows: [] };
  try { const j = JSON.parse(v); return Array.isArray(j) ? { rows: j } : { rows: j.rows ?? [], totals: j.totals, subtotals: j.subtotals }; } catch { return { rows: [] }; }
}
export function readRows(v: string | null | undefined): Row[] { return readTable(v).rows; }
export const writeRows = (rows: Row[]) => JSON.stringify(rows);

export const totalLabel = (t: TableTotal) => t.label ?? ({ sum: 'Total', average: 'Average', min: 'Minimum', max: 'Maximum', count: 'Count', stdev: 'Std. dev.', range: 'Range' } as Record<string, string>)[t.function] ?? t.function;
export const roleLabel: Record<string, string> = { prepared: 'Prepared', performed: 'Performed', reviewed: 'Reviewed', verified: 'Verified', approved: 'Approved', witness: 'Witnessed', customer: 'Customer', supplier: 'Supplier' };
