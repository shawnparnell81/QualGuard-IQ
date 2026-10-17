namespace QualGuardIQ.Domain.Security;

/// <summary>
/// Permission catalog. One edit permission covers a whole module; there are no per-form permissions.
/// Roles reference these codes as data.
/// </summary>
public static class Permissions
{
    public const string AdminUsers = "admin.users";
    public const string AdminRoles = "admin.roles";
    public const string AdminOrganization = "admin.organization";
    public const string AuditTrailView = "audittrail.view";
    public const string SecurityLogView = "securitylog.view";

    public const string DocumentsView = "documents.view";
    public const string DocumentsEdit = "documents.edit";
    public const string DocumentsApprove = "documents.approve";
    public const string DrawingsView = "drawings.view";
    public const string DrawingsEdit = "drawings.edit";
    public const string DrawingsApprove = "drawings.approve";
    public const string FormsView = "forms.view";
    public const string FormsEdit = "forms.edit";
    public const string FormsApprove = "forms.approve";
    public const string NcrView = "ncr.view";
    public const string NcrEdit = "ncr.edit";
    public const string NcrApprove = "ncr.approve";
    public const string CapaView = "capa.view";
    public const string CapaEdit = "capa.edit";
    public const string CapaApprove = "capa.approve";
    public const string AuditsView = "audits.view";
    public const string AuditsEdit = "audits.edit";
    public const string AuditsApprove = "audits.approve";
    public const string TrainingView = "training.view";
    public const string TrainingEdit = "training.edit";
    public const string CalibrationView = "calibration.view";
    public const string CalibrationEdit = "calibration.edit";
    public const string CalibrationApprove = "calibration.approve";
    public const string MasterDataView = "masterdata.view";
    public const string MasterDataEdit = "masterdata.edit";
    public const string SuppliersView = "suppliers.view";
    public const string SuppliersEdit = "suppliers.edit";
    public const string SuppliersApprove = "suppliers.approve";
    /// <summary>Use a restricted (suspended, disqualified, unapproved, expired) supplier with a recorded reason.</summary>
    public const string SuppliersOverride = "suppliers.override";
    public const string ComplaintsView = "complaints.view";
    public const string ComplaintsEdit = "complaints.edit";
    /// <summary>Verify complaint corrective action and reopen closed complaints.</summary>
    public const string ComplaintsApprove = "complaints.approve";
    public const string InspectionView = "inspection.view";
    public const string InspectionEdit = "inspection.edit";
    /// <summary>Approve inspection plan revisions and make release/rejection decisions.</summary>
    public const string InspectionApprove = "inspection.approve";
    public const string ChangesView = "changes.view";
    public const string ChangesEdit = "changes.edit";
    /// <summary>Approve changes, waive affected items, verify implementation and reopen.</summary>
    public const string ChangesApprove = "changes.approve";
    /// <summary>Record maintenance, repairs, breakdowns and qualification on equipment (viewing equipment needs calibration.view).</summary>
    public const string MaintenanceEdit = "maintenance.edit";
    public const string ObjectivesView = "objectives.view";
    public const string ObjectivesEdit = "objectives.edit";
    /// <summary>Approve quality objectives and their targets.</summary>
    public const string ObjectivesApprove = "objectives.approve";
    public const string ReviewsView = "reviews.view";
    public const string ReviewsEdit = "reviews.edit";
    /// <summary>Approve management review records.</summary>
    public const string ReviewsApprove = "reviews.approve";
    public const string RisksView = "risks.view";
    public const string RisksEdit = "risks.edit";
    /// <summary>Accept residual risk, reopen risks.</summary>
    public const string RisksApprove = "risks.approve";

    /// <summary>Run reports and dashboards (each report also needs view on its source module).</summary>
    public const string ReportsView = "reports.view";
    /// <summary>Export report results to CSV/PDF; every export is written to the audit trail.</summary>
    public const string ReportsExport = "reports.export";
    /// <summary>See overdue actions across all modules in scope.</summary>
    public const string ActionsView = "actions.view";

    public const string FaiView = "fai.view";
    public const string FaiEdit = "fai.edit";
    /// <summary>Sign FAI approval steps (Quality, Engineering, Manufacturing steps are configured on the FAI route).</summary>
    public const string FaiApprove = "fai.approve";

    /// <summary>Process flows, PFMEAs and control plans.</summary>
    public const string ApqpView = "apqp.view";
    public const string ApqpEdit = "apqp.edit";
    /// <summary>Release process flow, PFMEA and control plan revisions.</summary>
    public const string ApqpApprove = "apqp.approve";
    /// <summary>SPC charts and samples, MSA and gage R&amp;R studies.</summary>
    public const string SpcView = "spc.view";
    public const string SpcEdit = "spc.edit";
    /// <summary>Approve MSA studies.</summary>
    public const string MsaApprove = "msa.approve";
    public const string LpaView = "lpa.view";
    public const string LpaEdit = "lpa.edit";
    /// <summary>Cost of quality, scrap and rework.</summary>
    public const string CoqView = "coq.view";
    public const string CoqEdit = "coq.edit";
    /// <summary>Approve rework orders.</summary>
    public const string ReworkApprove = "rework.approve";
    /// <summary>Production and executive dashboards, data warehouse snapshots.</summary>
    public const string AnalyticsView = "analytics.view";
    /// <summary>Create and manage customer and supplier portal accounts.</summary>
    public const string PortalAdmin = "portal.admin";

    /// <summary>ERP/MES connections, mappings, sync runs and the inbound change queue.</summary>
    public const string IntegrationsView = "integrations.view";
    /// <summary>Configure connections (secrets are write-only), run syncs and submit queued changes.</summary>
    public const string IntegrationsEdit = "integrations.edit";
    /// <summary>Approve queued ERP/MES changes before they are applied.</summary>
    public const string IntegrationsApprove = "integrations.approve";
    /// <summary>Work orders, purchase orders, shop-floor data collection and live monitoring.</summary>
    public const string ProductionView = "production.view";
    public const string ProductionEdit = "production.edit";
    /// <summary>Lots, serial numbers, genealogy and traceability search.</summary>
    public const string TraceView = "trace.view";
    public const string TraceEdit = "trace.edit";
    public const string RecallsView = "recalls.view";
    public const string RecallsEdit = "recalls.edit";
    /// <summary>Close recalls (e-signed).</summary>
    public const string RecallsApprove = "recalls.approve";
    /// <summary>Customer returns (RMA) and warranty claims.</summary>
    public const string ReturnsView = "returns.view";
    public const string ReturnsEdit = "returns.edit";
    /// <summary>Decide warranty claims.</summary>
    public const string ReturnsApprove = "returns.approve";
    /// <summary>Use the quality assistant (answers only include records the user can already view).</summary>
    public const string CopilotUse = "copilot.use";
    /// <summary>Configure alert rules, outbound integration points and data exports.</summary>
    public const string AlertsAdmin = "alerts.admin";

    /// <summary>Warehouse locations, stock balances, ledger, receiving, holds, counts, issues and transfers.</summary>
    public const string InventoryView = "inventory.view";
    public const string InventoryEdit = "inventory.edit";
    /// <summary>Release holds and approve count variances.</summary>
    public const string InventoryApprove = "inventory.approve";
    /// <summary>Receive material without a purchase order, supplier delivery or ASN (blind receipt). Kept separate from inventory.edit.</summary>
    public const string InventoryReceiveBlind = "inventory.receive_blind";
    /// <summary>Sign Material Review Board dispositions.</summary>
    public const string MrbApprove = "mrb.approve";
    /// <summary>Shipments, pick lists, packing, verification, carriers and bills of lading.</summary>
    public const string ShippingView = "shipping.view";
    public const string ShippingEdit = "shipping.edit";
    /// <summary>Approve shipping document templates (bill of lading, packing list, manifest).</summary>
    public const string ShippingApprove = "shipping.approve";

    /// <summary>Organization tree, corporate standards and plant rollout, war room and governance dashboards.</summary>
    public const string EnterpriseView = "enterprise.view";
    public const string EnterpriseEdit = "enterprise.edit";
    /// <summary>Compliance obligations, packs and requirement mapping.</summary>
    public const string ComplianceView = "compliance.view";
    public const string ComplianceEdit = "compliance.edit";
    /// <summary>Verify or reject compliance mapping content against an authoritative source.</summary>
    public const string ComplianceVerify = "compliance.verify";
    /// <summary>Process, equipment, software and test method validation.</summary>
    public const string ValidationView = "validation.view";
    public const string ValidationEdit = "validation.edit";
    /// <summary>Approve validation protocols and release validated items.</summary>
    public const string ValidationApprove = "validation.approve";
    /// <summary>Retention policies and decisions, business continuity exercises.</summary>
    public const string GovernanceView = "governance.view";
    public const string GovernanceEdit = "governance.edit";
    /// <summary>Requirements, traceability, design reviews, design FMEA links, product configuration and BOM quality.</summary>
    public const string EngineeringView = "engineering.view";
    public const string EngineeringEdit = "engineering.edit";
    /// <summary>Approve design reviews and approve requirement revisions.</summary>
    public const string EngineeringApprove = "engineering.approve";
    /// <summary>Quality knowledge base, root-cause library and playbooks.</summary>
    public const string KnowledgeView = "knowledge.view";
    public const string KnowledgeEdit = "knowledge.edit";
    /// <summary>Business processes, maturity assessments and benchmarking.</summary>
    public const string ProcessView = "process.view";
    public const string ProcessEdit = "process.edit";
    /// <summary>Security reviews, privileged access reviews, access certification and deployment history (organization-wide).</summary>
    public const string SecurityReview = "security.review";
    /// <summary>Public API clients (keys and OAuth client credentials) and notification channels.</summary>
    public const string ApiAdmin = "api.admin";
    /// <summary>Platform operations: webhooks, event bus, health and observability, external events (organization-wide).</summary>
    public const string PlatformAdmin = "platform.admin";
    /// <summary>Share saved reports and dashboards with everyone.</summary>
    public const string ReportsShare = "reports.share";

    public static readonly IReadOnlyList<string> All = typeof(Permissions)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToArray();

    /// <summary>Permissions that only make sense organization-wide (never site-scoped).</summary>
    public static readonly IReadOnlySet<string> GlobalOnly = new HashSet<string>
    {
        AdminUsers, AdminRoles, AdminOrganization, SecurityLogView, ApiAdmin, SecurityReview, PlatformAdmin
    };
}
