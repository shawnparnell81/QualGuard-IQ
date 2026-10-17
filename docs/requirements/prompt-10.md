# MASTER BUILD PROMPT 10 (Shawn, 2026-10-16 10:25 PM ET). Content verbatim; bullet lists joined with semicolons.
INVENTORY MANAGEMENT, WAREHOUSE MANAGEMENT, SHIPPING, RECEIVING, LOGISTICS, MATERIAL FLOW, BARCODE SCANNING, QR TRACKING, AND END-TO-END MATERIAL TRACEABILITY
Shawn: include with the other module builds. Run order: Prompt 2 + FAI/AQL, 4, 5, 10 (builds on 5's lots/serials/barcode/traceability), 6, 7, 8, then 3, then 9.

IMPORTANT
Do not create a separate ERP. Do not replace ERP integrations. Do not replace MES integrations. Do not create duplicate Product, Part, Supplier, Customer, Lot, Serial Number, Traceability, FAI, AQL, NCR, CAPA, Recall, or Genealogy modules. Integrate all functionality into the existing Manufacturing Quality Operating System. Reuse: Authentication; Authorization; Workflow Engine; Approval Engine; Audit Engine; Search Engine; Reporting Engine; Dashboard Engine; Notification Engine; Traceability Engine; Supplier Quality; Customer Quality; ERP Integration; MES Integration.

INVENTORY MANAGEMENT
Build complete inventory management. Support: Raw Material Inventory; Component Inventory; WIP Inventory; Finished Goods Inventory; Spare Parts Inventory; Quarantine Inventory; MRB Inventory; Consigned Inventory. Track: Quantity On Hand; Available Quantity; Reserved Quantity; Allocated Quantity; Damaged Quantity; Quarantined Quantity. Support inventory history.

WAREHOUSE MANAGEMENT
Build warehouse management. Support: Warehouses; Buildings; Zones; Aisles; Rows; Bins; Racks; Floor Locations. Allow: Unlimited Locations; Multi-Warehouse Operations; Multi-Site Warehouses. Generate warehouse utilization reporting.

LOCATION MANAGEMENT
Support: Receiving Locations; Inspection Locations; Quarantine Locations; Production Locations; WIP Locations; Shipping Locations; Returns Locations; Scrap Locations. Track inventory movement between locations.

INVENTORY TRANSACTIONS
Build inventory transaction engine. Support: Receipt; Issue; Consumption; Adjustment; Transfer; Return; Scrap; Rework Return; Shipment; Cycle Count Adjustment. Maintain immutable transaction history.

RECEIVING MANAGEMENT
Build receiving module. Support: Incoming Receipts; Purchase Order Receipts; Supplier Deliveries; ASN Processing; Blind Receiving. Capture: Supplier; PO Number; Receiver; Date; Lot Numbers; Serial Numbers; Quantity Received. Generate receiving records.

INCOMING INSPECTION INTEGRATION
Automatically connect receiving to: AQL; Receiving Inspection; NCR; Supplier Quality. Support workflows: Receipt -> Inspection; Inspection -> Accept; Inspection -> Reject; Inspection -> Quarantine.

RECEIVING HOLD MANAGEMENT
Support: Quality Hold; Supplier Hold; Engineering Hold; Customer Hold. Prevent release until approval.

QUARANTINE INVENTORY
Create quarantine inventory controls. Track: Held Material; Rejected Material; Awaiting Inspection Material; Awaiting Disposition Material. Link to: NCR; Supplier NCR; MRB.

MRB MANAGEMENT
Build Material Review Board module. Support dispositions: Accept; Rework; Repair; Scrap; Return To Supplier; Use As Is. Capture approvals. Maintain audit history.

CYCLE COUNT MANAGEMENT
Support: Daily Counts; Weekly Counts; Monthly Counts; Annual Counts. Generate: Variance Reports; Adjustment Reports. Require approval for variances.

PHYSICAL INVENTORY
Build physical inventory module. Support: Inventory Freeze; Count Sheets; Variance Analysis; Reconciliation. Generate adjustments.

MATERIAL ISSUE MANAGEMENT
Support issuing material to: Production Orders; Work Orders; Jobs; Operations. Track: Quantity Issued; Lot Used; Serial Used. Maintain genealogy.

MATERIAL CONSUMPTION TRACKING
Track: Component Usage; Raw Material Usage; Production Consumption. Link to: Work Orders; Finished Goods; Product Genealogy.

WIP TRACKING
Track: Work In Process Inventory; Routed Inventory; Operation Inventory. Provide: Quantity; Location; Status.

TRANSFER MANAGEMENT
Support: Warehouse Transfers; Site Transfers; Department Transfers. Generate transfer documentation. Track chain of custody.

SHIPPING MANAGEMENT
Build shipment management. Support: Customer Shipments; Supplier Returns; Intercompany Shipments; Sample Shipments. Track: Quantities; Lots; Serials; Carriers; Shipment Dates.

PICKING MANAGEMENT
Support: Pick Lists; Pick Verification; Pick Completion. Validate inventory before shipment.

PACKING MANAGEMENT
Support: Packing Lists; Containers; Cartons; Pallets. Track packaging hierarchy.

SHIPPING VERIFICATION
Verify: Correct Product; Correct Revision; Correct Quantity; Correct Lot; Correct Serial. Prevent shipment errors.

BILL OF LADING MANAGEMENT
Support: BOL Generation; Shipping Documents; Carrier Information. Store shipping history.

CARRIER MANAGEMENT
Support: UPS; FedEx; DHL; LTL Carriers; Customer Carriers. Track: Tracking Numbers; Shipment Status.

CUSTOMER RETURNS
Expand return processing. Support: RMAs; Returned Material; Return Inspection; Return Disposition. Integrate: Complaints; NCR; CAPA.

SUPPLIER RETURNS
Support: RTV Processing; Supplier Return Tracking; Supplier Credits. Link to NCRs.

LOT MANAGEMENT
Expand lot controls. Track: Lot Creation; Lot Splits; Lot Merges; Lot Transfers. Maintain complete lot genealogy.

SERIAL NUMBER MANAGEMENT
Expand serial controls. Track: Creation; Assignment; Movement; Shipment; Return. Maintain complete serial history.

BARCODE GENERATION
Generate barcodes for: Products; Parts; Lots; Serials; Containers; Pallets; Work Orders; Locations. Support: Code 128; Code 39; Data Matrix.

QR CODE GENERATION
Generate QR codes for: Products; Lots; Serials; Documents; Work Orders; Inventory Locations.

BARCODE SCANNING FRAMEWORK
Build universal barcode scanning. Support: USB Barcode Scanners; Wireless Barcode Scanners; Mobile Cameras; Tablet Cameras; Android Devices; iOS Devices. Support keyboard wedge scanners. Support direct camera decoding.

SCAN TO RECEIVE
Allow users to: Scan PO -> Receive Material; Scan Product -> Validate Receipt; Scan Lot -> Validate Quantity.

SCAN TO INSPECT
Scan: Receiving Lots; Serial Numbers; Products. Automatically open: AQL; Inspection Plan; Inspection Records.

SCAN TO MOVE
Support: Scan Item -> Move Inventory; Scan Source Location -> Scan Destination. Generate inventory transaction automatically.

SCAN TO ISSUE
Support: Scan Material -> Issue To Work Order. Validate: Quantity; Lot; Revision.

SCAN TO SHIP
Support: Scan Product -> Verify Shipment; Scan Carton -> Verify Contents; Scan Pallet -> Verify Shipment.

SCAN TO TRACE
Allow scanning: Product; Lot; Serial. Display: Supplier; Receipt; Inspection; NCR History; CAPA History; Shipment History.

MOBILE WAREHOUSE APPLICATION
Build mobile warehouse functionality. Support: Receiving; Inspection; Transfers; Shipping; Cycle Counts; Inventory Lookup. Support offline mode.

LOGISTICS MANAGEMENT
Build logistics operations. Track: Inbound Material; Outbound Material; Transfers; Carrier Performance. Support logistics reporting.

ADVANCED TRACEABILITY
Provide complete navigation: Supplier -> Receipt; Receipt -> Inspection; Inspection -> Inventory; Inventory -> Production; Production -> Shipment; Shipment -> Customer; Customer -> Complaint; Complaint -> NCR; NCR -> CAPA; CAPA -> Closure.

WAREHOUSE DASHBOARDS
Create dashboards for: Inventory Accuracy; Inventory Turns; Receiving Performance; Shipping Performance; Warehouse Utilization; Quarantine Inventory; Scrap Inventory; MRB Inventory.

END TO END TESTING
Scenario 1: PO Receipt -> Receiving -> Inspection -> Inventory
Scenario 2: Inspection Failure -> Quarantine -> NCR -> Supplier Return
Scenario 3: Inventory Transfer -> Warehouse A -> Warehouse B
Scenario 4: Production Issue -> Work Order -> Consumption -> Genealogy
Scenario 5: Shipment -> Pick -> Pack -> Verify -> Ship
Scenario 6: Customer Complaint -> Trace Serial -> Trace Lot -> Trace Supplier

FINAL INSTRUCTION
Implement Inventory Management, Warehouse Management, Shipping, Receiving, Material Flow, Logistics, Barcode Scanning, QR Tracking, and End-to-End Traceability as fully integrated enterprise manufacturing modules. All transactions must be: Auditable; Searchable; Traceable; Permission Controlled; Reportable; Mobile Enabled. All inventory movements must support complete genealogy and quality traceability from supplier receipt through customer shipment.
