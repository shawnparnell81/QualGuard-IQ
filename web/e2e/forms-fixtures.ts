/** Shared form templates for the forms and import end-to-end tests. */

/** Sectioned inspection template (schema version 2) with tables, totals, running totals, repeated and conditional sections. */
export const inspectionSchema = {
  schemaVersion: 2,
  page: { size: 'Letter', orientation: 'portrait', marginMm: 15, printStyle: 'standardized', fontSize: 9 },
  sections: [
    { key: 'ident', title: 'Record identification', columns: 3 },
    { key: 'results', title: 'Dimensional results', columns: 2, orientation: 'landscape', instructions: 'Measure every ballooned characteristic and record the value as read.' },
    { key: 'failure', title: 'Failure details', columns: 1, visibleWhen: { field: 'outcome', operator: 'equals', value: 'Fail' } },
    { key: 'ops', title: 'Operations performed', columns: 2, repeatable: true, minRepeat: 1, maxRepeat: 5, repeatLabel: 'Operation' },
  ],
  fields: [
    { key: 'lot', label: 'Lot number', type: 'text', required: true, section: 'ident' },
    { key: 'qty', label: 'Quantity inspected', type: 'integer', min: 1, section: 'ident' },
    { key: 'unit_cost', label: 'Unit cost', type: 'currency', decimals: 2, section: 'ident' },
    { key: 'outcome', label: 'Outcome', type: 'radio', options: ['Pass', 'Fail'], section: 'ident' },
    { key: 'lot_value', label: 'Lot value', type: 'calc', formula: 'qty * unit_cost', decimals: 2, section: 'ident' },
    { key: 'reason', label: 'Describe the failure', type: 'longtext', required: true, section: 'failure' },
    { key: 'op_no', label: 'Operation number', type: 'integer', required: true, section: 'ops' },
    { key: 'op_minutes', label: 'Minutes', type: 'number', section: 'ops' },
    { key: 'fails', label: 'Characteristics out of tolerance', type: 'calc', formula: 'COUNTIF(dims.result, "FAIL")', section: 'results' },
  ],
  tables: [
    {
      key: 'dims', title: 'Characteristics', section: 'results', minRows: 1, maxRows: 50,
      headerGroups: [{ label: 'Characteristic', span: 1 }, { label: 'Specification', span: 2 }, { label: 'Result', span: 3 }],
      columns: [
        { key: 'balloon', label: 'Balloon', type: 'integer', required: true },
        { key: 'lsl', label: 'Lower limit', type: 'number' },
        { key: 'usl', label: 'Upper limit', type: 'number' },
        { key: 'measured', label: 'Measured', type: 'measurement', nominal: 0 },
        { key: 'dev', label: 'Over limit', type: 'calc', formula: 'MAX(0, measured - usl, lsl - measured)', decimals: 3 },
        { key: 'result', label: 'Result', type: 'calc', formula: 'WITHIN(measured, lsl, usl)', resultLabels: ['PASS', 'FAIL'] },
      ],
      totals: [{ column: 'dev', function: 'max' }, { column: 'balloon', function: 'count' }],
      runningTotals: ['dev'],
    },
    { key: 'visual', title: 'Visual checklist', section: 'results', fixedRows: ['Burrs', 'Scratches', 'Part marking'], minRows: 0, maxRows: 500, columns: [{ key: 'ok', label: 'Result', type: 'passfail', required: true }, { key: 'note', label: 'Note', type: 'text' }] },
  ],
  signatures: [{ key: 'inspector', label: 'Inspected by', role: 'performed', meaning: 'I performed this inspection as recorded', section: 'results', order: 1, required: true }],
};
