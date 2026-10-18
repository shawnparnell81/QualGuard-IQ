/** Builds a small Word document for the end-to-end tests from WordprocessingML written here (stored ZIP entries, no third-party library). */
import { zip } from './xlsx-fixture';

const enc = (s: string) => Buffer.from(s, 'utf8');
const x = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
const NS = 'xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml"';
const run = (t: string, rpr = '') => `<w:r>${rpr ? `<w:rPr>${rpr}</w:rPr>` : ''}<w:t xml:space="preserve">${x(t)}</w:t></w:r>`;
const p = (runs: string, ppr = '') => `<w:p>${ppr ? `<w:pPr>${ppr}</w:pPr>` : ''}${runs}</w:p>`;
const heading = (t: string) => p(run(t), '<w:pStyle w:val="Heading1"/>');
const text = (tag: string, alias: string) => `<w:sdt><w:sdtPr><w:alias w:val="${x(alias)}"/><w:tag w:val="${tag}"/><w:showingPlcHdr/><w:text/></w:sdtPr><w:sdtContent>${run('Click or tap here to enter text.')}</w:sdtContent></w:sdt>`;
const date = (tag: string, alias: string) => `<w:sdt><w:sdtPr><w:alias w:val="${x(alias)}"/><w:tag w:val="${tag}"/><w:showingPlcHdr/><w:date><w:dateFormat w:val="yyyy-MM-dd"/></w:date></w:sdtPr><w:sdtContent>${run('Click or tap to enter a date.')}</w:sdtContent></w:sdt>`;
const drop = (tag: string, alias: string, items: string[]) => `<w:sdt><w:sdtPr><w:alias w:val="${x(alias)}"/><w:tag w:val="${tag}"/><w:showingPlcHdr/><w:dropDownList><w:listItem w:displayText="Choose an item." w:value=""/>${items.map((i) => `<w:listItem w:displayText="${x(i)}" w:value="${x(i)}"/>`).join('')}</w:dropDownList></w:sdtPr><w:sdtContent>${run('Choose an item.')}</w:sdtContent></w:sdt>`;
const check = (tag: string, alias: string) => `<w:sdt><w:sdtPr><w:alias w:val="${x(alias)}"/><w:tag w:val="${tag}"/><w14:checkbox><w14:checked w14:val="0"/></w14:checkbox></w:sdtPr><w:sdtContent>${run('☐')}</w:sdtContent></w:sdt>`;
const field = (instr: string, shown: string) => `<w:r><w:fldChar w:fldCharType="begin"/></w:r><w:r><w:instrText xml:space="preserve"> ${x(instr)} </w:instrText></w:r><w:r><w:fldChar w:fldCharType="separate"/></w:r>${run(shown)}<w:r><w:fldChar w:fldCharType="end"/></w:r>`;
const cell = (content: string, w: number, opts: { span?: number; bold?: boolean; fill?: string } = {}) =>
  `<w:tc><w:tcPr><w:tcW w:w="${w * 20}" w:type="dxa"/>${opts.span ? `<w:gridSpan w:val="${opts.span}"/>` : ''}${opts.fill ? `<w:shd w:val="clear" w:color="auto" w:fill="${opts.fill}"/>` : ''}</w:tcPr>${content.startsWith('<w:p') ? content : p(content ? run(content, opts.bold ? '<w:b/>' : '') : '')}</w:tc>`;
const row = (cells: string, header = false) => `<w:tr>${header ? '<w:trPr><w:tblHeader/></w:trPr>' : ''}${cells}</w:tr>`;
const table = (grid: number[], rows: string[]) => `<w:tbl><w:tblPr><w:tblW w:w="0" w:type="auto"/><w:tblBorders>${['top', 'left', 'bottom', 'right', 'insideH', 'insideV'].map((b) => `<w:${b} w:val="single" w:sz="4"/>`).join('')}</w:tblBorders></w:tblPr><w:tblGrid>${grid.map((g) => `<w:gridCol w:w="${g * 20}"/>`).join('')}</w:tblGrid>${rows.join('')}</w:tbl>`;

/** A nonconformance report: dropdown, date and text content controls, checkbox controls, a lot table with a calculated reject rate, a repeating defect table with a total, and sign-off. */
export function nonconformanceReportDocx(): Buffer {
  const body = [
    p(run('Nonconformance Report'), '<w:pStyle w:val="Title"/>'),
    p(run('Report owner: ') + text('owner', 'Report owner') + run('   Date found: ') + date('date_found', 'Date found')),
    p(run('Disposition: ') + drop('disposition', 'Disposition', ['Use as is', 'Rework', 'Scrap', 'Return to supplier'])),
    heading('Lot details'),
    table([150, 110, 130, 110], [
      row(cell('Lot', 500, { span: 4, bold: true, fill: 'D9E2F3' })),
      row(cell('Lot size', 150) + cell('', 110) + cell('Rejected', 130) + cell('', 110)),
      row(cell('Reject rate %', 150) + cell(p(field('=D2/B2*100', '0')), 110) + cell('Customer notified', 130) + cell(p(check('customer_notified', 'Customer notified')), 110)),
    ]),
    heading('Defects'),
    table([40, 220, 90, 90], [
      row(cell('No.', 40, { bold: true }) + cell('Defect', 220, { bold: true }) + cell('Quantity', 90, { bold: true }) + cell('Cost', 90, { bold: true }), true),
      row(cell('1', 40) + cell('', 220) + cell('', 90) + cell('', 90)),
      row(cell('2', 40) + cell('', 220) + cell('', 90) + cell('', 90)),
      row(cell('Total', 260, { span: 2, bold: true }) + cell(p(field('=SUM(ABOVE)', '0')), 90) + cell(p(field('=SUM(ABOVE)', '0')), 90)),
    ]),
    p(run('Containment: ☐ Sorted   ☐ Quarantined')),
    heading('Approval'),
    table([120, 180, 50, 120], [
      row(cell('Reported by', 120) + cell('', 180) + cell('Date', 50) + cell('', 120)),
      row(cell('Approved by', 120) + cell('', 180) + cell('Date', 50) + cell('', 120)),
    ]),
  ].join('');
  const sect = '<w:sectPr><w:headerReference w:type="default" r:id="rIdH"/><w:footerReference w:type="default" r:id="rIdF"/><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1080" w:bottom="1440" w:left="1080"/></w:sectPr>';
  const files: [string, Buffer][] = [
    ['[Content_Types].xml', enc('<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/><Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/><Override PartName="/word/footer1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/></Types>')],
    ['_rels/.rels', enc('<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>')],
    ['word/_rels/document.xml.rels', enc('<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdS" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/><Relationship Id="rIdH" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/><Relationship Id="rIdF" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer" Target="footer1.xml"/></Relationships>')],
    ['word/document.xml', enc(`<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document ${NS}><w:body>${body}${sect}</w:body></w:document>`)],
    ['word/styles.xml', enc(`<?xml version="1.0" encoding="UTF-8"?><w:styles ${NS}><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial"/><w:sz w:val="20"/></w:rPr></w:rPrDefault></w:docDefaults><w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style><w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:pPr><w:jc w:val="center"/></w:pPr><w:rPr><w:b/><w:color w:val="1257C4"/><w:sz w:val="36"/></w:rPr></w:style><w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:pPr><w:outlineLvl w:val="0"/></w:pPr><w:rPr><w:b/><w:sz w:val="26"/></w:rPr></w:style></w:styles>`)],
    ['word/header1.xml', enc(`<?xml version="1.0" encoding="UTF-8"?><w:hdr ${NS}>${p(run('QF-0810 Nonconformance Report') + '<w:r><w:tab/></w:r>' + run('Rev B'))}</w:hdr>`)],
    ['word/footer1.xml', enc(`<?xml version="1.0" encoding="UTF-8"?><w:ftr ${NS}>${p(run('Page ') + field('PAGE', '1') + run(' of ') + field('NUMPAGES', '1'), '<w:jc w:val="center"/>')}</w:ftr>`)],
  ];
  return zip(files);
}
