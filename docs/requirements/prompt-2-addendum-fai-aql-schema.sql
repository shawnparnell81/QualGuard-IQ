-- Reference schema from Shawn (2026-10-16 9:56 PM ET) for the FAI/AQL module. Design intent; implement via EF Core migrations
-- using the project's naming, audit columns, shared link/attachment/approval engines, and real FKs to existing tables.
CREATE TABLE fai_master (fai_id UUID PRIMARY KEY, fai_number VARCHAR(50) UNIQUE NOT NULL,
 fai_type VARCHAR(50) NOT NULL, /* Full, Partial, Delta */ fai_reason VARCHAR(100) NOT NULL, /* New Product, ECN, Supplier Change, etc. */
 product_id UUID NOT NULL, part_id UUID NOT NULL, drawing_id UUID, drawing_revision VARCHAR(25), specification_id UUID,
 supplier_id UUID, customer_id UUID, site_id UUID, department_id UUID, lot_number VARCHAR(100), batch_number VARCHAR(100), serial_number VARCHAR(100),
 initiated_by UUID NOT NULL, inspector_id UUID, reviewer_id UUID, approver_id UUID, status VARCHAR(50) NOT NULL,
 inspection_start_date TIMESTAMP, inspection_complete_date TIMESTAMP, approval_date TIMESTAMP, comments TEXT,
 created_by UUID NOT NULL, created_date TIMESTAMP NOT NULL, modified_by UUID, modified_date TIMESTAMP);
CREATE TABLE fai_balloon_drawing (balloon_drawing_id UUID PRIMARY KEY, fai_id UUID NOT NULL, drawing_id UUID NOT NULL, drawing_revision VARCHAR(25),
 file_attachment_id UUID, upload_date TIMESTAMP, uploaded_by UUID, FOREIGN KEY (fai_id) REFERENCES fai_master(fai_id));
CREATE TABLE fai_characteristics (characteristic_id UUID PRIMARY KEY, fai_id UUID NOT NULL, balloon_number INTEGER NOT NULL, drawing_reference VARCHAR(250),
 characteristic_type VARCHAR(50), /* Critical, Significant, Major, Minor, Key Characteristic */ description TEXT NOT NULL,
 nominal_value DECIMAL(20,8), lower_tolerance DECIMAL(20,8), upper_tolerance DECIMAL(20,8), unit_of_measure VARCHAR(25),
 inspection_method VARCHAR(255), requirement_source VARCHAR(255), acceptance_criteria TEXT, FOREIGN KEY (fai_id) REFERENCES fai_master(fai_id));
CREATE TABLE fai_characteristic_results (result_id UUID PRIMARY KEY, characteristic_id UUID NOT NULL, inspection_sequence INTEGER, measured_value DECIMAL(20,8),
 pass_fail VARCHAR(10), inspection_date TIMESTAMP, inspection_equipment_id UUID, inspector_id UUID, notes TEXT,
 FOREIGN KEY (characteristic_id) REFERENCES fai_characteristics(characteristic_id));
CREATE TABLE fai_approvals (approval_id UUID PRIMARY KEY, fai_id UUID NOT NULL, approval_role VARCHAR(50), approver_id UUID, approval_status VARCHAR(25),
 approval_date TIMESTAMP, comments TEXT, FOREIGN KEY (fai_id) REFERENCES fai_master(fai_id));
CREATE TABLE fai_ncr_links (id UUID PRIMARY KEY, fai_id UUID NOT NULL, ncr_id UUID NOT NULL, FOREIGN KEY (fai_id) REFERENCES fai_master(fai_id));
CREATE TABLE fai_capa_links (id UUID PRIMARY KEY, fai_id UUID NOT NULL, capa_id UUID NOT NULL, FOREIGN KEY (fai_id) REFERENCES fai_master(fai_id));
CREATE TABLE aql_sampling_plans (plan_id UUID PRIMARY KEY, plan_name VARCHAR(255) NOT NULL, inspection_type VARCHAR(50), /* Single, Double, Multiple, Continuous */
 inspection_severity VARCHAR(25), /* Normal, Reduced, Tightened */ revision VARCHAR(25), active BOOLEAN DEFAULT TRUE, created_date TIMESTAMP);
CREATE TABLE aql_plan_details (detail_id UUID PRIMARY KEY, plan_id UUID NOT NULL, lot_size_min INTEGER, lot_size_max INTEGER, inspection_level VARCHAR(25),
 aql_level DECIMAL(5,2), sample_size INTEGER, accept_quantity INTEGER, reject_quantity INTEGER, FOREIGN KEY (plan_id) REFERENCES aql_sampling_plans(plan_id));
CREATE TABLE aql_inspections (inspection_id UUID PRIMARY KEY, inspection_number VARCHAR(50) UNIQUE, plan_id UUID NOT NULL, supplier_id UUID, customer_id UUID,
 product_id UUID, part_id UUID, lot_number VARCHAR(100), lot_size INTEGER, sample_size INTEGER, inspection_level VARCHAR(25), aql_level DECIMAL(5,2),
 inspection_date TIMESTAMP, inspector_id UUID, status VARCHAR(50), overall_result VARCHAR(25), acceptance_quantity INTEGER, rejection_quantity INTEGER, notes TEXT,
 FOREIGN KEY (plan_id) REFERENCES aql_sampling_plans(plan_id));
CREATE TABLE aql_defect_categories (defect_category_id UUID PRIMARY KEY, defect_code VARCHAR(50) UNIQUE, defect_type VARCHAR(25), /* Critical, Major, Minor */ description TEXT);
CREATE TABLE aql_inspection_defects (defect_id UUID PRIMARY KEY, inspection_id UUID NOT NULL, defect_category_id UUID NOT NULL, quantity_found INTEGER, description TEXT,
 FOREIGN KEY (inspection_id) REFERENCES aql_inspections(inspection_id), FOREIGN KEY (defect_category_id) REFERENCES aql_defect_categories(defect_category_id));
CREATE TABLE supplier_aql_metrics (metric_id UUID PRIMARY KEY, supplier_id UUID NOT NULL, reporting_period_start DATE, reporting_period_end DATE,
 inspections_completed INTEGER, lots_accepted INTEGER, lots_rejected INTEGER, acceptance_percentage DECIMAL(10,2),
 critical_defects INTEGER, major_defects INTEGER, minor_defects INTEGER, defect_ppm DECIMAL(20,4), created_date TIMESTAMP);
-- Recommended dashboard views: vw_open_fai, vw_overdue_fai, vw_failed_fai, vw_closed_fai, vw_supplier_aql_performance, vw_supplier_rejection_rate,
-- vw_supplier_defect_trends, vw_characteristic_pass_fail, vw_defect_trends, vw_fai_to_ncr, vw_fai_to_capa
-- Shared relationships: FAI -> Product, Part, Drawing, Specification, NCR, CAPA, Supplier, Customer;
-- AQL -> Supplier, Product, Part, NCR, CAPA, Supplier Corrective Action
-- Additional tables requested by Shawn (2026-10-16 9:56 PM ET):
-- fai_workflow_history, fai_audit_trail, fai_attachments, fai_comments, fai_revision_history
-- aql_workflow_history, aql_audit_trail, aql_attachments, aql_comments
-- supplier_aql_scorecards, supplier_receiving_inspections, inspection_levels, sampling_codes, sampling_revisions
