/** Small PDF files for the end-to-end tests, written here (not copied): Helvetica text, lines and image-only (scanned) pages. */
type Page = { ops: string[]; image: boolean };
const W = 612, H = 792;
const esc = (s: string) => s.replace(/\\/g, '\\\\').replace(/\(/g, '\\(').replace(/\)/g, '\\)');

class PdfWriter {
  pages: Page[] = [];
  constructor(public title: string) {}
  page() { this.pages.push({ ops: [], image: false }); return this; }
  text(x: number, top: number, s: string, size = 10, bold = false) { this.pages.at(-1)!.ops.push(`BT /${bold ? 'F2' : 'F1'} ${size} Tf ${x} ${H - top - size} Td (${esc(s)}) Tj ET`); return this; }
  line(x1: number, top: number, x2: number) { this.pages.at(-1)!.ops.push(`0.5 w ${x1} ${H - top} m ${x2} ${H - top} l S`); return this; }
  scanned() { this.pages.push({ ops: [`q ${W} 0 0 ${H} 0 0 cm /Im1 Do Q`], image: true }); return this; }
  build(): Buffer {
    const objs: string[] = [];
    const add = (b: string) => { objs.push(b); return objs.length; };
    const catalog = add(''), pagesId = add('');
    const f1 = add('<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>');
    const f2 = add('<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>');
    const pix = Array.from({ length: 64 }, (_, i) => (i % 3 === 0 ? '40' : 'E0')).join('');
    const img = add(`<< /Type /XObject /Subtype /Image /Width 8 /Height 8 /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /ASCIIHexDecode /Length ${pix.length + 1} >>\nstream\n${pix}>\nendstream`);
    const ids = this.pages.map((p) => {
      const content = p.ops.join('\n') + '\n';
      const c = add(`<< /Length ${Buffer.byteLength(content, 'latin1')} >>\nstream\n${content}endstream`);
      return add(`<< /Type /Page /Parent ${pagesId} 0 R /MediaBox [0 0 ${W} ${H}] /Resources << /Font << /F1 ${f1} 0 R /F2 ${f2} 0 R >>${p.image ? ` /XObject << /Im1 ${img} 0 R >>` : ''} >> /Contents ${c} 0 R >>`);
    });
    objs[pagesId - 1] = `<< /Type /Pages /Kids [${ids.map((i) => `${i} 0 R`).join(' ')}] /Count ${ids.length} >>`;
    objs[catalog - 1] = `<< /Type /Catalog /Pages ${pagesId} 0 R >>`;
    const info = add(`<< /Title (${esc(this.title)}) >>`);
    let out = '%PDF-1.7\n';
    const offs: number[] = [];
    objs.forEach((o, i) => { offs.push(Buffer.byteLength(out, 'latin1')); out += `${i + 1} 0 obj\n${o}\nendobj\n`; });
    const xref = Buffer.byteLength(out, 'latin1');
    out += `xref\n0 ${objs.length + 1}\n0000000000 65535 f \n` + offs.map((o) => `${String(o).padStart(10, '0')} 00000 n \n`).join('');
    out += `trailer\n<< /Size ${objs.length + 1} /Root ${catalog} 0 R /Info ${info} 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
    return Buffer.from(out, 'latin1');
  }
}

/** A printed final inspection report: label blanks, an instruction, a measurement table with numbered rows and a total, checkbox symbols, sign-off lines. */
export function finalInspectionPdf(): Buffer {
  const b = new PdfWriter('Final Inspection Report').page();
  b.text(72, 50, 'Final Inspection Report', 18, true);
  b.text(72, 90, 'Work order: ____________').text(330, 90, 'Inspection date: __________');
  b.text(72, 115, 'Inspector: ____________');
  b.text(72, 140, 'Record every measurement in the table below. Use the calibrated gauge listed on the traveler.');
  b.text(72, 175, 'Measurements', 13, true);
  b.text(72, 200, 'No.', 10, true).text(130, 200, 'Characteristic', 10, true).text(330, 200, 'Nominal', 10, true).text(430, 200, 'Measured', 10, true);
  for (let r = 1; r <= 3; r++) b.text(72, 200 + r * 20, String(r)).line(72, 214 + r * 20, 520);
  b.text(72, 290, 'Total').text(430, 290, '0.00');
  b.text(72, 330, 'Result:').text(140, 330, '[ ] Accept').text(240, 330, '[ ] Reject');
  b.text(72, 380, 'Approval', 13, true);
  b.text(72, 410, 'Inspected by: ________________').text(330, 410, 'Date: __________');
  b.text(72, 440, 'Approved by: ________________').text(330, 440, 'Date: __________');
  return b.build();
}

/** A scanned page: one picture and no text layer. */
export function scannedPdf(): Buffer { return new PdfWriter('Scanned receiving form').scanned().build(); }
