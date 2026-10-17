# MASTER BUILD PROMPT 4 (Shawn, 2026-10-16 10:11 PM ET). Content verbatim; bullet lists joined with semicolons.
ADVANCED QUALITY MODULES, MANUFACTURING EXECUTION INTEGRATION, SUPPLIER DEVELOPMENT, COST OF QUALITY, AND ENTERPRISE QUALITY OPERATIONS
Execution order (set by Goose): Prompt 2 + FAI/AQL, then Prompt 4, then Prompt 3 hardening last so it covers everything.

IMPORTANT
Do not rebuild existing QMS functionality. Do not replace existing modules. Do not redesign architecture. Do not create duplicate entities already implemented in: Document Control; Drawing Control; Form Builder; NCR; CAPA; Audit Management; Training; Calibration; Supplier Quality; Customer Complaints; Risk Management; Change Management; Product Management; Inspection Management; Asset Management; Management Review; KPI Management; Reporting; Administration; FAI; AQL.
Extend the existing platform. Re-use: Authentication; Authorization; Workflow Engine; Approval Engine; Notification Engine; Audit Engine; Reporting Engine; Search Engine; Record Relationship Engine.

CONTROL PLAN MANAGEMENT
Build a complete Control Plan module. Support: Prototype Control Plans; Pre-Launch Control Plans; Production Control Plans; Supplier Control Plans.
Support: Product Family; Product; Part Number; Revision; Process Step; Characteristic; Method; Sample Size; Frequency; Reaction Plan; Process Owner.
Relationships: FMEA; Process Flow; Inspection Plans; Control Characteristics; FAI; AQL. Support revision control. Historical control plans must never change.

PFMEA MANAGEMENT
Build Process Failure Mode and Effects Analysis. Support: Process Functions; Failure Modes; Failure Causes; Failure Effects. Track: Severity; Occurrence; Detection. Calculate: RPN; Action Priority. Support: Recommended Actions; Responsibility; Due Date; Completion Date; Verification. Relationships: Control Plans; Process Flow; NCR; CAPA; Changes.

PROCESS FLOW MANAGEMENT
Support: Manufacturing Process Flow Diagrams; Process Steps; Inputs; Outputs; Process Ownership. Relationships: PFMEA; Control Plans; Work Instructions; Inspection Plans. Allow revision control.

LAYERED PROCESS AUDITS
Build LPA management. Support: LPA Templates; Audit Schedules; Audit Assignments; Findings; Escalations; Corrective Actions. Support: Daily LPAs; Weekly LPAs; Monthly LPAs. Automatically create NCR or CAPA when configured.

STATISTICAL PROCESS CONTROL
Build SPC management. Support: X-Bar R; X-Bar S; Individuals; Moving Range; P Charts; NP Charts; C Charts; U Charts. Capture: Measurement Data; Sample Data; Timestamp; Operator; Equipment. Calculate: Mean; Range; Sigma; Cp; Cpk; Pp; Ppk. Detect: Out Of Control Conditions; Trends; Rule Violations. Automatically notify quality personnel.

GAGE R&R MANAGEMENT
Support: Variable Gage R&R; Attribute Gage R&R. Track: Operators; Parts; Trials; Measurement Results. Calculate: Repeatability; Reproducibility; Total Variation; Percent Contribution; Percent Study Variation. Generate reports. Link results to: Equipment; Calibration; Inspection Methods.

MSA MANAGEMENT
Build Measurement System Analysis. Support: Bias Studies; Stability Studies; Linearity Studies; Gage R&R Studies. Track approval and review cycles.

SUPPLIER DEVELOPMENT
Build supplier development functionality. Support: Supplier Assessments; Supplier Development Plans; Supplier Action Items; Supplier Improvement Projects; Supplier Risk Reduction Programs. Track: Planned Completion; Actual Completion; Performance Improvement. Generate supplier scorecards.

SUPPLIER SCORECARD EXPANSION
Score suppliers on: Quality; Delivery; Responsiveness; Cost; Corrective Action Response; Audit Results; Certifications; Risk. Support weighted scoring. Automatically calculate rankings.

COST OF QUALITY
Build Cost of Quality module. Track: PREVENTION COSTS: Training; Audits; Supplier Development; Calibration; Validation. APPRAISAL COSTS: Inspection; Testing; FAI; AQL; Audits. INTERNAL FAILURE COSTS: Scrap; Rework; Downtime; Retesting; Material Loss. EXTERNAL FAILURE COSTS: Warranty; Returns; Customer Complaints; Field Failures; Recalls. Generate: COQ by Month; COQ by Product; COQ by Supplier; COQ by Customer.

SCRAP MANAGEMENT
Support: Scrap Events; Scrap Lots; Scrap Material; Scrap Cost; Scrap Reason. Relationships: NCR; Product; Supplier; Inspection. Generate scrap trend reporting.

REWORK MANAGEMENT
Support: Rework Instructions; Rework Approval; Rework Validation; Rework Costs. Link to: NCR; CAPA; Inspection.

CUSTOMER PORTAL
Build external customer portal. Allow authorized customers to: View Complaints; View Responses; Upload Evidence; Review Corrective Actions; View Product Quality Reports. Apply strict security controls.

SUPPLIER PORTAL
Build external supplier portal. Allow suppliers to: Respond To NCRs; Respond To SCARs; Upload Evidence; View Scorecards; View Audit Results; View Performance Metrics. Apply strict permission boundaries.

SCAR MANAGEMENT
Build Supplier Corrective Action Requests. Support: SCAR Number; Supplier; Issue; Root Cause; Corrective Action; Verification; Closure. Link: Supplier NCR; Supplier Audit; Supplier Complaint; AQL Failure.

PRODUCTION QUALITY DASHBOARDS
Create manufacturing dashboards for: First Pass Yield; Scrap Rate; Rework Rate; Defect Rate; Cost Of Quality; Supplier Performance; Audit Performance; SPC Performance; Calibration Compliance; Training Compliance. Use live data only.

ENTERPRISE SEARCH EXPANSION
Extend search across: FAI; AQL; PFMEA; Control Plans; Process Flow; SPC; MSA; LPAs; Supplier Development; COQ; Scrap; Rework; SCAR. Support cross-module search.

ADVANCED RELATIONSHIP ENGINE
Create visual record relationships. Allow navigation between: Products; Drawings; Specifications; FAI; AQL; NCR; CAPA; PFMEA; Control Plans; Process Flow; Audits; Complaints; Suppliers. Generate relationship diagrams.

QUALITY DATA WAREHOUSE
Create analytics data model. Support: Trend Analysis; Historical Analysis; Executive Reporting; KPI Aggregation. Generate: Monthly Metrics; Quarterly Metrics; Yearly Metrics.

ADVANCED REPORTING
Generate: Executive Quality Reports; Plant Quality Reports; Supplier Quality Reports; Customer Quality Reports; COQ Reports; SPC Reports; FMEA Reports; Control Plan Reports. Support: PDF; Excel; CSV.

EXECUTIVE DASHBOARDS
Create executive quality dashboards showing: Open NCRs; Open CAPAs; Supplier Risk; Customer Risk; Cost Of Quality; Audit Findings; Complaint Trends; Defect Trends. Provide executive drilldown.

PRODUCTION READINESS VALIDATION
Validate: PFMEA Workflows; Control Plan Workflows; SPC Calculations; MSA Calculations; Gage R&R Calculations; Cost Calculations; Supplier Portals; Customer Portals.

END TO END TESTING
Scenario 1: New Product -> PFMEA -> Control Plan -> FAI -> Inspection -> Production Release
Scenario 2: Supplier Failure -> AQL Reject -> NCR -> SCAR -> Supplier CAPA -> Verification
Scenario 3: Process Failure -> SPC Violation -> NCR -> CAPA -> PFMEA Update -> Control Plan Update
Scenario 4: Customer Complaint -> Investigation -> NCR -> CAPA -> Cost Recording -> Closure
Scenario 5: Audit Finding -> Action Item -> CAPA -> Verification -> Closure

FINAL INSTRUCTION
Implement all modules as integrated QMS functionality. Do not create stand-alone applications. Use existing architecture, workflows, approvals, permissions, notifications, reporting, audit trails, dashboards, relationships, and document-control services. All new data must be searchable, reportable, auditable, revision-controlled where required, and fully integrated with the existing Quality Management System.
