/** The table on the page as plain text, exactly as the person sees it (current filters, sorting and columns). */
export interface TableText { columns: string[]; rows: string[][] }

const text = (el: Element) => ((el as HTMLElement).innerText ?? el.textContent ?? '').replace(/\s+/g, ' ').trim();
const visible = (el: Element) => (el as HTMLElement).offsetParent !== null || (el as HTMLElement).getClientRects().length > 0;

/** The list tables in a page area, largest first. Tables marked data-export="off" (small helper grids) are left out. */
export function findTables(root: Element | null): HTMLTableElement[] {
  if (!root) return [];
  return Array.from(root.querySelectorAll('table'))
    .filter((t) => t.dataset.export !== 'off' && !t.closest('[data-export="off"]') && !t.closest('[role="dialog"]') && visible(t) && t.querySelector('thead th'))
    .sort((a, b) => b.querySelectorAll('tbody tr').length - a.querySelectorAll('tbody tr').length);
}

export function readTable(t: HTMLTableElement): TableText {
  const headRow = t.querySelectorAll('thead tr');
  const head = headRow[headRow.length - 1];
  const headers = head ? Array.from(head.querySelectorAll('th,td')).map(text) : [];
  const body = Array.from(t.querySelectorAll('tbody tr'))
    .map((r) => Array.from(r.querySelectorAll('th,td')))
    // the "nothing to show" row spans every column and carries no data
    .filter((cells) => cells.length === headers.length || (cells.length > 1 && headers.length === 0))
    .map((cells) => cells.map(text));
  // columns with no heading and no text (checkboxes, action buttons) are dropped
  const keep = headers.map((h, i) => h !== '' || body.some((r) => (r[i] ?? '') !== ''));
  return {
    columns: headers.map((h, i) => h || `Column ${i + 1}`).filter((_, i) => keep[i]),
    rows: body.map((r) => r.filter((_, i) => keep[i])),
  };
}

/** Describes the filters filled in on the page ("Status: Open; Search: bracket") so the export says what it contains. */
export function readFilters(root: Element | null): string | undefined {
  if (!root) return undefined;
  const parts: string[] = [];
  root.querySelectorAll('.MuiTextField-root, .MuiFormControl-root').forEach((f) => {
    if (f.closest('table') || f.closest('[role="dialog"]') || f.parentElement?.closest('.MuiFormControl-root')) return;
    const label = f.querySelector('label');
    const input = f.querySelector('input:not([type=hidden]), textarea') as HTMLInputElement | null;
    const select = f.querySelector('[role="combobox"]');
    const value = select ? text(select) : input?.value?.trim();
    if (label && value) parts.push(`${text(label).replace(/\s*\*$/, '')}: ${value}`);
  });
  return parts.length ? parts.join('; ').slice(0, 500) : undefined;
}
