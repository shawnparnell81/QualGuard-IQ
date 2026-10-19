/** File types the app saves, with the extension and the name shown in the browser's Save dialog. */
export type SaveFormat = 'pdf' | 'csv' | 'xlsx' | 'docx';
const kinds: Record<SaveFormat, { mime: string; ext: string; label: string }> = {
  pdf: { mime: 'application/pdf', ext: '.pdf', label: 'PDF document' },
  csv: { mime: 'text/csv', ext: '.csv', label: 'CSV file' },
  xlsx: { mime: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', ext: '.xlsx', label: 'Excel workbook' },
  docx: { mime: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', ext: '.docx', label: 'Word document' },
};
export const formatLabel = (f: SaveFormat) => kinds[f].label;

/** A safe file name from a record number and title, e.g. "NCR-0004 Burr on bracket.pdf". */
export function fileName(base: string, format: SaveFormat) {
  const clean = base.replace(/[\\/:*?"<>|\u0000-\u001f]+/g, ' ').replace(/\s+/g, ' ').trim().slice(0, 120) || 'export';
  return clean + kinds[format].ext;
}

interface Writable { write: (b: Blob) => Promise<void>; close: () => Promise<void> }
interface Handle { createWritable: () => Promise<Writable> }
type Picker = (o: { suggestedName: string; types: { description: string; accept: Record<string, string[]> }[] }) => Promise<Handle>;

export type SaveResult = 'saved' | 'downloaded' | 'cancelled';

/**
 * Saves a file the server produces. Chrome and Edge open the Save dialog first (so the person picks the folder),
 * then the file is fetched and written there. Other browsers get a normal download into their downloads folder.
 */
export async function saveFile(name: string, format: SaveFormat, produce: () => Promise<Blob>): Promise<SaveResult> {
  const picker = (window as unknown as { showSaveFilePicker?: Picker }).showSaveFilePicker;
  if (picker && window.self === window.top) {
    let handle: Handle;
    try {
      handle = await picker({ suggestedName: name, types: [{ description: kinds[format].label, accept: { [kinds[format].mime]: [kinds[format].ext] } }] });
    } catch (e) {
      if ((e as DOMException)?.name === 'AbortError') return 'cancelled';
      return download(name, await produce());
    }
    const blob = await produce();
    const w = await handle.createWritable();
    await w.write(blob);
    await w.close();
    return 'saved';
  }
  return download(name, await produce());
}

function download(name: string, blob: Blob): SaveResult {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url; a.download = name; a.rel = 'noopener';
  document.body.appendChild(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 30_000);
  return 'downloaded';
}
