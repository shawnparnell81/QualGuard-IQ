# MASTER BUILD PROMPT 5 (Shawn, 2026-10-16 10:13 PM ET). Content verbatim; bullet lists joined with semicolons.
ERP INTEGRATION, MES INTEGRATION, INVENTORY TRACEABILITY, SHOP FLOOR QUALITY, ADVANCED ANALYTICS, AI COPILOT, AND ENTERPRISE MANUFACTURING CONNECTIVITY
SHAWN'S DIRECTIVE: Research and build the ERP connectors exactly as each vendor requires. The first NetSuite connector was wrong (webhooks; it was replaced with OAuth 2.0 M2M + REST). ALL ERP CONNECTORS NEED TO BE CORRECT. Each connector must follow that vendor's official docs (auth flow, API, objects, field names, paging, rate limits, errors), with every claim cited to a doc in docs/integrations/<erp>.md, and be tested against recorded vendor-shaped fixtures or a sandbox. No guessed endpoints.
Run order: after Prompt 4, before Prompt 3 hardening.

IMPORTANT
Do not rebuild existing modules. Do not replace existing architecture. Do not redesign authentication. Do not redesign authorization. Do not create duplicate workflow engines. Extend the existing QMS. Use existing: Security; Audit Trail; Notifications; Workflows; Approvals; Reporting; Dashboards; Search; Attachments; Relationships.

ERP INTEGRATION FRAMEWORK
Build a configurable ERP integration framework. Support: SAP; Oracle; Epicor; Infor; Microsoft Dynamics; NetSuite; Acumatica; Plex; IQMS; Custom ERP Systems. Support integration methods: REST; SOAP; OData; GraphQL; Database Integration; CSV Exchange; Scheduled Imports; Scheduled Exports; Event Driven Integration.

ERP MASTER DATA SYNCHRONIZATION
Synchronize: Products; Parts; Revisions; BOMs; Customers; Suppliers; Work Centers; Employees; Production Orders; Purchase Orders. Support: One Way Sync; Two Way Sync; Approval Required Sync; Validation Rules.

MES INTEGRATION
Integrate with Manufacturing Execution Systems. Exchange: Work Orders; Production Jobs; Process Steps; Inspection Results; Nonconformance Results; Production Quantities; Scrap Quantities; Rework Quantities. Support: Real Time Updates; Scheduled Synchronization.

PURCHASE ORDER INTEGRATION
Import: PO Number; Supplier; Item; Revision; Quantity; Due Date. Automatically relate: AQL Inspections; Supplier NCRs; Supplier Scorecards.

WORK ORDER INTEGRATION
Support: Production Orders; Manufacturing Orders; Jobs; Routings. Relate: Inspections; FAI; SPC Results; NCRs; Rework; Scrap.

LOT TRACEABILITY
Build complete lot traceability. Support: Raw Material Lots; Component Lots; Finished Product Lots; Supplier Lots; Customer Lots. Track: Receipt; Consumption; Production; Inspection; Shipment. Support forward and backward traceability.

SERIALIZATION
Support serialized products. Track: Serial Number; Product; Revision; Manufacturing Date; Inspection Results; NCR History; Customer Shipment; Service History.

GENEALOGY MANAGEMENT
Build product genealogy. Track: Parent Assemblies; Child Components; Raw Materials; Suppliers; Manufacturing Operations. Allow: Full Product History; Recall Investigations; Containment Activities.

RECALL MANAGEMENT
Build controlled recall management. Support: Recall Number; Recall Reason; Risk Rating; Affected Products; Affected Lots; Affected Serial Numbers. Track: Customers Impacted; Supplier Impacted; Corrective Actions; Containment.

CUSTOMER RETURN MANAGEMENT
Support: RMA Number; Product; Serial Number; Return Reason; Root Cause; Corrective Action. Integrate with: NCR; CAPA; Complaints; Warranty.

WARRANTY MANAGEMENT
Build warranty quality management. Track: Warranty Claims; Warranty Costs; Product Failures; Component Failures; Suppliers. Calculate: Warranty PPM; Warranty Trends; Warranty Cost.

ADVANCED TRACEABILITY SEARCH
Allow searching by: Product; Part; Lot; Batch; Serial Number; Customer; Supplier. Return: FAI; AQL; Inspection; NCR; CAPA; Complaints; Shipments; Returns.

SHOP FLOOR DATA COLLECTION
Build manufacturing data collection. Capture: Production Counts; Reject Counts; Scrap Counts; Downtime; Inspection Values; Process Parameters. Support: Desktop; Tablet; Mobile.

REAL TIME QUALITY MONITORING
Create live monitoring. Display: Open NCRs; SPC Violations; Scrap Events; Production Quality; Supplier Quality; Customer Quality. Support: Auto Refresh; Real Time Metrics.

AI COPILOT FOR QUALITY
Build AI quality assistant. Support: Natural Language Search; Record Discovery; Trend Explanation; Audit Preparation; NCR Summaries; CAPA Summaries; Complaint Summaries. Allow users to ask: "Show me all supplier NCRs for Supplier A." "Show overdue CAPAs." "Show complaint trends by product." "Explain the increase in scrap."

AI INVESTIGATION ASSISTANT
Analyze: NCR History; CAPA History; Inspection Data; SPC Data; Complaint Data. Suggest: Potential Root Causes; Similar Historical Events; Recommended Actions. Do not automatically close records. Do not automatically approve records.

AI TREND ANALYSIS
Identify: Recurring Defects; Recurring Suppliers; Recurring Customers; Recurring Process Failures. Generate: Risk Trends; Quality Trends; Performance Trends.

PREDICTIVE QUALITY
Create predictive quality services. Estimate: Supplier Risk; Product Risk; Process Risk; Complaint Probability; NCR Probability. Provide confidence scoring.

EXECUTIVE AI INSIGHTS
Generate: Monthly Executive Summaries; Quality Risk Summaries; Supplier Risk Reviews; Cost of Quality Summaries. Support dashboard delivery.

ADVANCED KPI ENGINE
Add: PPM; DPPM; FPY; RTY; COPQ; Scrap %; Rework %; Warranty %; Supplier Performance; Audit Completion. Support: Daily; Weekly; Monthly; Quarterly; Annual.

ENTERPRISE ALERTING
Support alerts for: Overdue NCRs; Overdue CAPAs; Failed Audits; Failed AQL; Failed FAI; SPC Violations; Supplier Risk Thresholds; Complaint Thresholds. Deliver through: Email; In-App Notifications; SMS Integration Points; Teams Integration Points.

MICROSOFT POWER PLATFORM INTEGRATION
Support: Power BI; Power Automate; Power Apps. Expose: Secure APIs; Reporting Datasets; Analytics Views.

MICROSOFT COPILOT INTEGRATION
Support: QMS Search; QMS Reporting; KPI Explanations; Audit Preparation Assistance; Supplier Quality Assistance; Management Review Assistance. Implement secure permission-aware responses.

DATA LAKE EXPORT FRAMEWORK
Support exports to: Data Warehouse; Data Lake; Analytics Platforms. Provide: Scheduled Exports; Incremental Exports; Secure APIs.

MOBILE QUALITY APPLICATION
Build mobile capabilities. Support: Audits; NCRs; Inspections; FAI; AQL; CAPA Verification. Support: Offline Data Collection; Synchronization; Photo Capture; Barcode Scanning; QR Code Scanning.

BARCODE AND QR SUPPORT
Support: Product Labels; Lot Labels; Serial Labels; Work Order Labels. Allow: Scan To Search; Scan To Inspect; Scan To Trace.

ADVANCED END TO END TESTING
Scenario 1: ERP Product Sync -> Revision Change -> FAI -> Inspection -> Release
Scenario 2: Supplier Receipt -> AQL -> NCR -> SCAR -> Supplier Improvement
Scenario 3: Shop Floor Defect -> SPC Violation -> NCR -> CAPA -> Verification
Scenario 4: Customer Return -> Complaint -> NCR -> CAPA -> Warranty Claim
Scenario 5: Recall -> Lot Traceability -> Customer Identification -> Containment -> Closure
Scenario 6: AI Investigation -> Similar NCR Retrieval -> Trend Analysis -> Suggested Root Cause

FINAL INSTRUCTION
Implement all functionality as integrated enterprise manufacturing quality modules. All new records must: Support Permissions; Support Audit Trails; Support Notifications; Support Reporting; Support Search; Support Relationships; Support Dashboards; Support API Access. Do not create demonstration-only functionality. Implement production-ready ERP, MES, Traceability, AI, Analytics, and Enterprise Connectivity features directly within the existing Manufacturing Quality Management System.
