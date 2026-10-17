using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Organization;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<ProcessFlow> ProcessFlows => Set<ProcessFlow>();
    public DbSet<ProcessFlowStep> ProcessFlowSteps => Set<ProcessFlowStep>();
    public DbSet<Pfmea> Pfmeas => Set<Pfmea>();
    public DbSet<PfmeaLine> PfmeaLines => Set<PfmeaLine>();
    public DbSet<ControlPlan> ControlPlans => Set<ControlPlan>();
    public DbSet<ControlPlanLine> ControlPlanLines => Set<ControlPlanLine>();
    public DbSet<SpcChart> SpcCharts => Set<SpcChart>();
    public DbSet<SpcSample> SpcSamples => Set<SpcSample>();
    public DbSet<MsaStudy> MsaStudies => Set<MsaStudy>();
    public DbSet<MsaMeasurement> MsaMeasurements => Set<MsaMeasurement>();
    public DbSet<MsaResultVersion> MsaResultVersions => Set<MsaResultVersion>();
    public DbSet<LpaTemplate> LpaTemplates => Set<LpaTemplate>();
    public DbSet<LpaSchedule> LpaSchedules => Set<LpaSchedule>();
    public DbSet<LpaAudit> LpaAudits => Set<LpaAudit>();
    public DbSet<SupplierProgram> SupplierPrograms => Set<SupplierProgram>();
    public DbSet<SupplierScoreWeights> SupplierScoreWeights => Set<SupplierScoreWeights>();
    public DbSet<CoqEntry> CoqEntries => Set<CoqEntry>();
    public DbSet<ScrapEvent> ScrapEvents => Set<ScrapEvent>();
    public DbSet<ReworkOrder> ReworkOrders => Set<ReworkOrder>();
    public DbSet<QualityMetricFact> QualityMetricFacts => Set<QualityMetricFact>();
    public DbSet<PortalResponse> PortalResponses => Set<PortalResponse>();

    private static void SiteRefs<T>(EntityTypeBuilder<T> e) where T : class, Domain.Common.ISiteScoped
    {
        e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ApqpDoc<T>(EntityTypeBuilder<T> e, string table) where T : ApqpDocument
    {
        const DeleteBehavior R = DeleteBehavior.Restrict;
        e.ToTable(table);
        e.HasIndex(x => new { x.Number, x.Revision }).IsUnique();
        // One released and one open (draft / pending) revision per document number.
        e.HasIndex(x => x.Number, $"ux_{table}_one_released").IsUnique().HasFilter("\"Status\" = 2");
        e.HasIndex(x => x.Number, $"ux_{table}_one_open").IsUnique().HasFilter("\"Status\" IN (0, 1)");
        e.Property(x => x.Number).HasMaxLength(50);
        e.Property(x => x.Title).HasMaxLength(300);
        e.Property(x => x.PartRevision).HasMaxLength(25);
        e.Property(x => x.ChangeReason).HasMaxLength(2000);
        e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(R);
        e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(R);
        UserReq(e, x => x.OwnerId);
        SiteRefs(e);
    }

    private static void ConfigureApqp(ModelBuilder b)
    {
        const DeleteBehavior R = DeleteBehavior.Restrict, C = DeleteBehavior.Cascade;
        b.Ignore<ApqpDocument>();
        b.Entity<ProcessFlow>(e => ApqpDoc(e, "process_flows"));
        b.Entity<ProcessFlowStep>(e =>
        {
            e.ToTable("process_flow_steps");
            e.HasIndex(x => new { x.FlowId, x.Sequence }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(300); e.Property(x => x.StepType).HasMaxLength(30);
            e.Property(x => x.Inputs).HasMaxLength(2000); e.Property(x => x.Outputs).HasMaxLength(2000);
            e.HasOne<ProcessFlow>().WithMany().HasForeignKey(x => x.FlowId).OnDelete(C);
            e.HasOne<InspectionPlan>().WithMany().HasForeignKey(x => x.InspectionPlanId).OnDelete(R);
            e.HasOne<Domain.Documents.ControlledDocument>().WithMany().HasForeignKey(x => x.WorkInstructionId).OnDelete(R);
            User(e, x => x.OwnerId);
        });
        b.Entity<Pfmea>(e => { ApqpDoc(e, "pfmeas"); e.HasOne<ProcessFlow>().WithMany().HasForeignKey(x => x.ProcessFlowId).OnDelete(R); });
        b.Entity<PfmeaLine>(e =>
        {
            e.ToTable("pfmea_lines", t => t.HasCheckConstraint("ck_pfmea_sod", "\"Severity\" BETWEEN 1 AND 10 AND \"Occurrence\" BETWEEN 1 AND 10 AND \"Detection\" BETWEEN 1 AND 10"));
            e.HasIndex(x => x.PfmeaId);
            foreach (var p in new[] { nameof(PfmeaLine.ProcessFunction), nameof(PfmeaLine.FailureMode), nameof(PfmeaLine.FailureEffect), nameof(PfmeaLine.FailureCause) }) e.Property(p).HasMaxLength(1000);
            foreach (var p in new[] { nameof(PfmeaLine.PreventionControls), nameof(PfmeaLine.DetectionControls), nameof(PfmeaLine.RecommendedAction), nameof(PfmeaLine.ActionTaken) }) e.Property(p).HasMaxLength(2000);
            e.Property(x => x.SpecialCharacteristic).HasMaxLength(20);
            e.HasOne<Pfmea>().WithMany().HasForeignKey(x => x.PfmeaId).OnDelete(C);
            User(e, x => x.ResponsibleId); User(e, x => x.VerifiedById);
        });
        b.Entity<ControlPlan>(e =>
        {
            ApqpDoc(e, "control_plans");
            e.Property(x => x.ProductFamily).HasMaxLength(200);
            e.HasOne<Pfmea>().WithMany().HasForeignKey(x => x.PfmeaId).OnDelete(R);
            e.HasOne<ProcessFlow>().WithMany().HasForeignKey(x => x.ProcessFlowId).OnDelete(R);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(R);
            User(e, x => x.ProcessOwnerId);
        });
        b.Entity<ControlPlanLine>(e =>
        {
            e.ToTable("control_plan_lines");
            e.HasIndex(x => x.PlanId);
            foreach (var p in new[] { nameof(ControlPlanLine.ProcessStep), nameof(ControlPlanLine.Characteristic), nameof(ControlPlanLine.MachineOrTool), nameof(ControlPlanLine.Specification), nameof(ControlPlanLine.Method), nameof(ControlPlanLine.ControlMethod) }) e.Property(p).HasMaxLength(500);
            e.Property(x => x.ReactionPlan).HasMaxLength(2000);
            e.Property(x => x.CharacteristicType).HasMaxLength(20); e.Property(x => x.SpecialCharacteristic).HasMaxLength(20);
            e.Property(x => x.SampleSize).HasMaxLength(50); e.Property(x => x.Frequency).HasMaxLength(100);
            e.HasOne<ControlPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(C);
            e.HasOne<InspectionPlan>().WithMany().HasForeignKey(x => x.InspectionPlanId).OnDelete(R);
            e.HasOne<SpcChart>().WithMany().HasForeignKey(x => x.SpcChartId).OnDelete(R);
        });
        b.Entity<SpcChart>(e =>
        {
            e.ToTable("spc_charts");
            e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Characteristic).HasMaxLength(300); e.Property(x => x.Unit).HasMaxLength(25);
            foreach (var p in new[] { nameof(SpcChart.Lsl), nameof(SpcChart.Usl), nameof(SpcChart.Target) }) e.Property(p).HasPrecision(20, 8);
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(R);
            e.HasOne<Equipment>().WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(R);
            UserReq(e, x => x.OwnerId); SiteRefs(e);
        });
        b.Entity<SpcSample>(e =>
        {
            e.ToTable("spc_samples");
            e.HasIndex(x => new { x.ChartId, x.TakenAt });
            e.Property(x => x.Values).HasColumnType("jsonb"); e.Property(x => x.Violations).HasMaxLength(1000); e.Property(x => x.Notes).HasMaxLength(1000);
            e.HasOne<SpcChart>().WithMany().HasForeignKey(x => x.ChartId).OnDelete(C);
            e.HasOne<Equipment>().WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(R);
            e.HasOne<Ncr>().WithMany().HasForeignKey(x => x.NcrId).OnDelete(R);
            UserReq(e, x => x.OperatorId);
        });
        b.Entity<MsaStudy>(e =>
        {
            e.ToTable("msa_studies");
            e.HasIndex(x => x.Number).IsUnique(); e.HasIndex(x => x.EquipmentId);
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.InspectionMethod).HasMaxLength(300); e.Property(x => x.Characteristic).HasMaxLength(300);
            e.Property(x => x.Tolerance).HasPrecision(20, 8); e.Property(x => x.ReferenceValue).HasPrecision(20, 8);
            e.Property(x => x.Result).HasColumnType("jsonb"); e.Property(x => x.Verdict).HasMaxLength(30);
            e.Property(x => x.ConfidenceLevel).HasPrecision(5, 4).HasDefaultValue(0.95m); e.Property(x => x.CalculationVersion).HasMaxLength(80);
            e.ToTable(t => t.HasCheckConstraint("ck_msa_confidence", "\"ConfidenceLevel\" BETWEEN 0.80 AND 0.999"));
            e.HasOne<Equipment>().WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(R);
            UserReq(e, x => x.OwnerId); SiteRefs(e);
        });
        b.Entity<MsaResultVersion>(e =>
        {
            e.ToTable("msa_result_versions");
            e.HasIndex(x => new { x.StudyId, x.CreatedAt });
            e.Property(x => x.CalculationVersion).HasMaxLength(80); e.Property(x => x.Result).HasColumnType("jsonb"); e.Property(x => x.Verdict).HasMaxLength(30); e.Property(x => x.Reason).HasMaxLength(200);
            e.HasOne<MsaStudy>().WithMany().HasForeignKey(x => x.StudyId).OnDelete(C);
        });
        b.Entity<MsaMeasurement>(e =>
        {
            e.ToTable("msa_measurements");
            e.HasIndex(x => x.StudyId);
            e.Property(x => x.Operator).HasMaxLength(100); e.Property(x => x.Attribute).HasMaxLength(50); e.Property(x => x.AttributeReference).HasMaxLength(50);
            e.Property(x => x.Value).HasPrecision(20, 8); e.Property(x => x.Reference).HasPrecision(20, 8);
            e.HasOne<MsaStudy>().WithMany().HasForeignKey(x => x.StudyId).OnDelete(C);
        });
        b.Entity<LpaTemplate>(e =>
        {
            e.ToTable("lpa_templates");
            e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Area).HasMaxLength(200); e.Property(x => x.Questions).HasColumnType("jsonb");
            User(e, x => x.EscalateToId); SiteRefs(e);
        });
        b.Entity<LpaSchedule>(e =>
        {
            e.ToTable("lpa_schedules");
            e.HasOne<LpaTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(R);
            UserReq(e, x => x.AssigneeId); SiteRefs(e);
        });
        b.Entity<LpaAudit>(e =>
        {
            e.ToTable("lpa_audits");
            e.HasIndex(x => x.Number).IsUnique(); e.HasIndex(x => new { x.Status, x.DueOn });
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.Answers).HasColumnType("jsonb");
            e.HasOne<LpaTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(R);
            e.HasOne<LpaSchedule>().WithMany().HasForeignKey(x => x.ScheduleId).OnDelete(R);
            e.HasOne<Ncr>().WithMany().HasForeignKey(x => x.NcrId).OnDelete(R);
            e.HasOne<Capa>().WithMany().HasForeignKey(x => x.CapaId).OnDelete(R);
            UserReq(e, x => x.AssigneeId); SiteRefs(e);
        });
        b.Entity<SupplierProgram>(e =>
        {
            e.ToTable("supplier_programs");
            e.HasIndex(x => x.Number).IsUnique(); e.HasIndex(x => x.SupplierId);
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Objective).HasMaxLength(2000); e.Property(x => x.Outcome).HasMaxLength(2000);
            foreach (var p in new[] { nameof(SupplierProgram.BaselineScore), nameof(SupplierProgram.TargetScore), nameof(SupplierProgram.CurrentScore) }) e.Property(p).HasPrecision(7, 2);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(R);
            UserReq(e, x => x.OwnerId); SiteRefs(e);
        });
        b.Entity<SupplierScoreWeights>(e =>
        {
            e.ToTable("supplier_score_weights");
            foreach (var p in e.Metadata.GetProperties().Where(p => p.ClrType == typeof(decimal)).Select(p => p.Name).ToList()) e.Property(p).HasPrecision(7, 2);
        });
        b.Entity<CoqEntry>(e =>
        {
            e.ToTable("coq_entries", t => t.HasCheckConstraint("ck_coq_amount", "\"Amount\" >= 0"));
            e.HasIndex(x => new { x.SiteId, x.IncurredOn }); e.HasIndex(x => new { x.SourceType, x.SourceId });
            e.Property(x => x.Element).HasMaxLength(60); e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Description).HasMaxLength(1000); e.Property(x => x.SourceType).HasMaxLength(50);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(R);
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(R);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(R);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(R);
            SiteRefs(e);
        });
        b.Entity<ScrapEvent>(e =>
        {
            e.ToTable("scrap_events", t => t.HasCheckConstraint("ck_scrap_qty", "\"Quantity\" > 0 AND \"UnitCost\" >= 0"));
            e.HasIndex(x => x.Number).IsUnique(); e.HasIndex(x => new { x.SiteId, x.ScrappedOn });
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.LotNumber).HasMaxLength(100); e.Property(x => x.Material).HasMaxLength(200); e.Property(x => x.Reason).HasMaxLength(1000);
            e.Property(x => x.Quantity).HasPrecision(18, 4); e.Property(x => x.UnitCost).HasPrecision(18, 4); e.Property(x => x.TotalCost).HasPrecision(18, 2);
            e.HasOne<Ncr>().WithMany().HasForeignKey(x => x.NcrId).OnDelete(R);
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(R);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(R);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(R);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(R);
            SiteRefs(e);
        });
        b.Entity<ReworkOrder>(e =>
        {
            e.ToTable("rework_orders");
            e.HasIndex(x => x.Number).IsUnique(); e.HasIndex(x => x.NcrId);
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.Instructions).HasMaxLength(4000); e.Property(x => x.ValidationNotes).HasMaxLength(2000);
            foreach (var p in new[] { nameof(ReworkOrder.Quantity), nameof(ReworkOrder.LaborHours), nameof(ReworkOrder.LaborRate) }) e.Property(p).HasPrecision(18, 4);
            e.Property(x => x.MaterialCost).HasPrecision(18, 2); e.Property(x => x.TotalCost).HasPrecision(18, 2);
            e.HasOne<Ncr>().WithMany().HasForeignKey(x => x.NcrId).OnDelete(R);
            e.HasOne<Capa>().WithMany().HasForeignKey(x => x.CapaId).OnDelete(R);
            UserReq(e, x => x.OwnerId); User(e, x => x.ValidatedById); SiteRefs(e);
        });
        b.Entity<PortalResponse>(e =>
        {
            e.ToTable("portal_responses");
            e.HasIndex(x => new { x.RecordType, x.RecordId });
            e.Property(x => x.RecordType).HasMaxLength(50); e.Property(x => x.PartyKind).HasMaxLength(20); e.Property(x => x.Body).HasMaxLength(10000);
            UserReq(e, x => x.AuthorId);
        });
        b.Entity<QualityMetricFact>(e =>
        {
            e.ToTable("quality_metric_facts");
            e.HasIndex(x => new { x.Metric, x.PeriodType, x.PeriodStart, x.SiteId }).IsUnique().AreNullsDistinct(false);
            e.Property(x => x.Metric).HasMaxLength(60);
            e.Property(x => x.Value).HasPrecision(20, 6); e.Property(x => x.Numerator).HasPrecision(20, 6); e.Property(x => x.Denominator).HasPrecision(20, 6);
        });
    }
}
