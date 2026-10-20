/**
 * Builds small, realistic Office Open XML workbooks for the import tests (no third-party library; stored ZIP entries).
 * Cells are given as A1 addresses with text, numbers or formulas; styles: 0 plain, 1 bold title, 2 bottom border, 3 bold boxed header, 4 boxed.
 */
type Cell = string | number | { f: string } ;
export interface SheetSpec { name: string; cells: Record<string, Cell>; styles?: Record<string, number>; merges?: string[]; widths?: Record<number, number>; lists?: { ref: string; values: string[] }[]; hidden?: boolean }

const enc = (s: string) => Buffer.from(s, 'utf8');
const esc = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

const table = (() => { const t = new Uint32Array(256); for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; t[n] = c >>> 0; } return t; })();
const crc32 = (b: Buffer) => { let c = 0xffffffff; for (const x of b) c = table[(c ^ x) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };

export function zip(files: [string, Buffer][]): Buffer {
  const parts: Buffer[] = []; const central: Buffer[] = []; let offset = 0;
  for (const [name, data] of files) {
    const n = enc(name); const crc = crc32(data);
    const h = Buffer.alloc(30); h.writeUInt32LE(0x04034b50, 0); h.writeUInt16LE(20, 4); h.writeUInt16LE(0, 6); h.writeUInt16LE(0, 8); h.writeUInt32LE(0, 10);
    h.writeUInt32LE(crc, 14); h.writeUInt32LE(data.length, 18); h.writeUInt32LE(data.length, 22); h.writeUInt16LE(n.length, 26); h.writeUInt16LE(0, 28);
    parts.push(h, n, data);
    const c = Buffer.alloc(46); c.writeUInt32LE(0x02014b50, 0); c.writeUInt16LE(20, 4); c.writeUInt16LE(20, 6); c.writeUInt16LE(0, 8); c.writeUInt16LE(0, 10); c.writeUInt32LE(0, 12);
    c.writeUInt32LE(crc, 16); c.writeUInt32LE(data.length, 20); c.writeUInt32LE(data.length, 24); c.writeUInt16LE(n.length, 28); c.writeUInt32LE(offset, 42);
    central.push(c, n);
    offset += 30 + n.length + data.length;
  }
  const cd = Buffer.concat(central);
  const end = Buffer.alloc(22); end.writeUInt32LE(0x06054b50, 0); end.writeUInt16LE(files.length, 8); end.writeUInt16LE(files.length, 10); end.writeUInt32LE(cd.length, 12); end.writeUInt32LE(offset, 16);
  return Buffer.concat([...parts, cd, end]);
}

const addr = (a: string) => { const m = /^([A-Z]+)(\d+)$/.exec(a)!; let c = 0; for (const ch of m[1]) c = c * 26 + ch.charCodeAt(0) - 64; return { r: Number(m[2]), c }; };

function sheetXml(s: SheetSpec): string {
  const rows = new Map<number, string[]>();
  const all = new Set([...Object.keys(s.cells), ...Object.keys(s.styles ?? {})]);
  for (const a of [...all].sort((x, y) => { const p = addr(x), q = addr(y); return p.r - q.r || p.c - q.c; })) {
    const v = s.cells[a]; const st = s.styles?.[a] ?? 0; const { r } = addr(a);
    const sAttr = st ? ` s="${st}"` : '';
    let x: string;
    if (v === undefined) x = `<c r="${a}"${sAttr}/>`;
    else if (typeof v === 'number') x = `<c r="${a}"${sAttr}><v>${v}</v></c>`;
    else if (typeof v === 'object') x = `<c r="${a}"${sAttr}><f>${esc(v.f)}</f></c>`;
    else x = `<c r="${a}"${sAttr} t="inlineStr"><is><t xml:space="preserve">${esc(v)}</t></is></c>`;
    if (!rows.has(r)) rows.set(r, []);
    rows.get(r)!.push(x);
  }
  const cols = s.widths ? `<cols>${Object.entries(s.widths).map(([c, w]) => `<col min="${c}" max="${c}" width="${w}" customWidth="1"/>`).join('')}</cols>` : '';
  const merges = s.merges?.length ? `<mergeCells count="${s.merges.length}">${s.merges.map((m) => `<mergeCell ref="${m}"/>`).join('')}</mergeCells>` : '';
  const dv = s.lists?.length ? `<dataValidations count="${s.lists.length}">${s.lists.map((l) => `<dataValidation type="list" allowBlank="1" showInputMessage="1" showErrorMessage="1" sqref="${l.ref}"><formula1>"${esc(l.values.join(','))}"</formula1></dataValidation>`).join('')}</dataValidations>` : '';
  return `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">${cols}<sheetData>${[...rows.entries()].sort((a, b) => a[0] - b[0]).map(([r, cs]) => `<row r="${r}">${cs.join('')}</row>`).join('')}</sheetData>${merges}${dv}</worksheet>`;
}

const styles = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
<fonts count="3"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="14"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts>
<fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFD9E1F2"/><bgColor indexed="64"/></patternFill></fill></fills>
<borders count="3"><border><left/><right/><top/><bottom/><diagonal/></border><border><left/><right/><top/><bottom style="thin"><color auto="1"/></bottom><diagonal/></border><border><left style="thin"><color auto="1"/></left><right style="thin"><color auto="1"/></right><top style="thin"><color auto="1"/></top><bottom style="thin"><color auto="1"/></bottom><diagonal/></border></borders>
<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
<cellXfs count="5"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1" applyAlignment="1"><alignment horizontal="center"/></xf><xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1"/><xf numFmtId="0" fontId="2" fillId="2" borderId="2" xfId="0" applyFont="1" applyFill="1" applyBorder="1"/><xf numFmtId="0" fontId="0" fillId="0" borderId="2" xfId="0" applyBorder="1"/></cellXfs>
<cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>`;

export function workbook(sheets: SheetSpec[]): Buffer {
  const files: [string, Buffer][] = [
    ['[Content_Types].xml', enc(`<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>${sheets.map((_, i) => `<Override PartName="/xl/worksheets/sheet${i + 1}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>`).join('')}</Types>`)],
    ['_rels/.rels', enc(`<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>`)],
    ['xl/workbook.xml', enc(`<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>${sheets.map((s, i) => `<sheet name="${esc(s.name)}" sheetId="${i + 1}"${s.hidden ? ' state="hidden"' : ''} r:id="rId${i + 1}"/>`).join('')}</sheets><calcPr calcId="191029" fullCalcOnLoad="1"/></workbook>`)],
    ['xl/_rels/workbook.xml.rels', enc(`<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">${sheets.map((_, i) => `<Relationship Id="rId${i + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet${i + 1}.xml"/>`).join('')}<Relationship Id="rId${sheets.length + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>`)],
    ['xl/styles.xml', enc(styles)],
    ...sheets.map((s, i) => [`xl/worksheets/sheet${i + 1}.xml`, enc(sheetXml(s))] as [string, Buffer]),
  ];
  return zip(files);
}

/** A receiving inspection sheet: header fields, a calculated sample size, a disposition list, a measurement table with deviation and total, and a sign-off. */
export function receivingInspectionWorkbook(): Buffer {
  const cells: Record<string, Cell> = {
    A1: 'RECEIVING INSPECTION',
    A3: 'Part number:', A4: 'Lot qty:', D4: 'Sample size:', E4: { f: 'IF(B4>500,32,IF(B4>90,13,5))' }, A5: 'Disposition:',
    A7: 'Characteristic', B7: 'Nominal', C7: 'Measured', D7: 'Deviation',
    C11: 'Total', D11: { f: 'SUM(D8:D10)' },
    A13: 'Inspected by:', C13: 'Date:',
  };
  const st: Record<string, number> = { A1: 1, B3: 2, B4: 2, B5: 2, B13: 2, D13: 2, A7: 3, B7: 3, C7: 3, D7: 3 };
  for (let r = 8; r <= 10; r++) { cells[`D${r}`] = { f: `IF(C${r}="","",C${r}-B${r})` }; for (const c of 'ABCD') st[`${c}${r}`] = 4; }
  return workbook([{ name: 'Receiving inspection', cells, styles: st, merges: ['A1:E1'], widths: { 1: 18, 2: 14, 3: 14, 4: 14, 5: 12 }, lists: [{ ref: 'B5', values: ['Accept', 'Reject', 'Hold'] }] }]);
}

/** A long questionnaire spread over several worksheets: together more fields than one form may hold, each worksheet on its own well within it. */
export function longQuestionnaireWorkbook(sheetCount = 7, rowsPerSheet = 100): Buffer {
  return workbook(Array.from({ length: sheetCount }, (_, s) => {
    const cells: Record<string, Cell> = { A1: `SECTION ${s + 1}` };
    const st: Record<string, number> = { A1: 1 };
    for (let r = 3; r < 3 + rowsPerSheet; r++) { cells[`A${r}`] = `Question ${s + 1}.${r - 2}:`; st[`B${r}`] = 2; }
    return { name: `Section ${s + 1}`, cells, styles: st, widths: { 1: 24, 2: 30 } };
  }));
}
