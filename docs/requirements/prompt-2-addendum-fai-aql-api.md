# FAI/AQL API endpoint list from Shawn (2026-10-16 9:58 PM ET). Every route is required.
## FAI (base /api/v1/fai)
- Management: GET /fai; GET/PUT/DELETE /fai/{faiId}; POST /fai; GET /fai/search, /fai/filter, /fai/status/{status}, /fai/open, /fai/overdue, /fai/approval-pending, /fai/closed; POST /fai/{faiId}/copy, /clone, /reopen, /close
- Types: GET /fai/types; POST /fai/full, /partial, /delta, /engineering-change, /process-change, /material-change, /tooling-change, /supplier-change, /requalification
- Workflow: POST /fai/{faiId}/submit, /review, /return, /approve, /reject, /start-inspection, /complete-inspection, /release; GET /fai/{faiId}/workflow, /history
- Balloon drawings: GET/POST /fai/{faiId}/balloon-drawings; PUT/DELETE /fai/{faiId}/balloon-drawings/{drawingId}; POST .../balloon-drawings/upload; GET .../balloon-drawings/download; GET /fai/{faiId}/ballooned-pdf
- Characteristics: GET/POST /fai/{faiId}/characteristics; GET/PUT/DELETE .../characteristics/{characteristicId}; GET .../characteristics/open, /failed, /missing
- Results: POST/GET /fai/{faiId}/characteristics/{characteristicId}/results; PUT/DELETE /fai/{faiId}/results/{resultId}; POST /fai/{faiId}/bulk-results, /import-results; GET /fai/{faiId}/export-results
- Approvals: GET /fai/{faiId}/approvals; POST .../approvals/request, /approve, /reject, /return; GET .../approvals/history
- Attachments: GET/POST /fai/{faiId}/attachments; GET/DELETE .../attachments/{attachmentId}
- Comments: GET/POST /fai/{faiId}/comments; PUT/DELETE .../comments/{commentId}
- NCR/CAPA: POST /fai/{faiId}/create-ncr, /create-capa; GET /fai/{faiId}/ncrs, /capas, /linked-records
- PDF/reports: GET /fai/{faiId}/pdf, /report, /report-summary, /report-detail
## AQL (base /api/v1/aql)
- Plans: GET/POST /aql/plans; GET/PUT/DELETE /aql/plans/{planId}; POST .../activate, /deactivate
- Table maintenance: GET/POST /aql/plans/{planId}/levels; PUT/DELETE .../levels/{levelId}; GET /aql/plans/{planId}/sampling-table
- Inspections: GET/POST /aql/inspections; GET/PUT/DELETE /aql/inspections/{inspectionId}; GET /aql/inspections/open, /completed, /rejected, /accepted
- Calculations: POST /aql/calculate-sample-size, /determine-plan, /calculate-result, /calculate-defect-rate, /calculate-ppm
- Defects: GET/POST /aql/inspections/{inspectionId}/defects; PUT/DELETE .../defects/{defectId}
- Pass/fail: POST /aql/inspections/{inspectionId}/evaluate, /accept, /reject, /submit, /approve
- Supplier: GET /aql/suppliers/{supplierId}/history, /metrics, /scorecard, /ppm, /acceptance-rate, /rejection-rate, /defect-trends
- NCR/CAPA: POST /aql/inspections/{inspectionId}/create-ncr, /create-capa; GET .../ncrs, /capas
## Dashboards
GET /api/v1/dashboard/fai/open, /overdue, /pass-rate, /fail-rate, /characteristics; GET /api/v1/dashboard/aql/acceptance-rate, /rejection-rate, /defect-rate, /ppm, /supplier-ranking
## Reports
GET /api/v1/reports/fai, /fai-summary, /fai-detail, /fai-characteristics, /aql, /aql-summary, /aql-supplier-performance, /aql-defect-analysis, /aql-ppm, /aql-trends
## Audit and history
GET /api/v1/fai/{faiId}/audit-history, /workflow-history; GET /api/v1/aql/{inspectionId}/audit-history, /workflow-history
## Bulk
POST /api/v1/fai/bulk-close, /fai/bulk-approve, /aql/bulk-approve, /aql/bulk-close, /aql/bulk-create-ncr, /aql/bulk-create-capa
