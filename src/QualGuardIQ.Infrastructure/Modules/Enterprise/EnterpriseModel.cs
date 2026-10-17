using QualGuardIQ.Application.Records;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Enterprise;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Organization;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<OrgUnit> OrgUnits => Set<OrgUnit>();
    public DbSet<CorporateStandard> CorporateStandards => Set<CorporateStandard>();
    public DbSet<StandardRollout> StandardRollouts => Set<StandardRollout>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<SkillRequirement> SkillRequirements => Set<SkillRequirement>();
    public DbSet<EmployeeCertification> EmployeeCertifications => Set<EmployeeCertification>();
    public DbSet<TrainingCurriculum> TrainingCurricula => Set<TrainingCurriculum>();
    public DbSet<AuditProgram> AuditPrograms => Set<AuditProgram>();
    public DbSet<AuditPlanItem> AuditPlanItems => Set<AuditPlanItem>();
    public DbSet<ComplianceObligation> ComplianceObligations => Set<ComplianceObligation>();
    public DbSet<CompliancePackActivation> CompliancePackActivations => Set<CompliancePackActivation>();
    public DbSet<ValidationProject> ValidationProjects => Set<ValidationProject>();
    public DbSet<ValidationTest> ValidationTests => Set<ValidationTest>();
    public DbSet<ValidationDeviation> ValidationDeviations => Set<ValidationDeviation>();
    public DbSet<QualityProject> QualityProjects => Set<QualityProject>();
    public DbSet<ProjectDeliverable> ProjectDeliverables => Set<ProjectDeliverable>();
    public DbSet<PhaseGate> PhaseGates => Set<PhaseGate>();
    public DbSet<PpapSubmission> PpapSubmissions => Set<PpapSubmission>();
    public DbSet<PpapElement> PpapElements => Set<PpapElement>();
    public DbSet<CapabilityStudy> CapabilityStudies => Set<CapabilityStudy>();
    public DbSet<RetentionPolicy> RetentionPolicies => Set<RetentionPolicy>();
    public DbSet<RetentionDecision> RetentionDecisions => Set<RetentionDecision>();
    public DbSet<ContinuityExercise> ContinuityExercises => Set<ContinuityExercise>();
    public DbSet<NotificationChannel> NotificationChannels => Set<NotificationChannel>();
    public DbSet<ApiClient> ApiClients => Set<ApiClient>();

    private static void ConfigureEnterprise(ModelBuilder b)
    {
        const DeleteBehavior R = DeleteBehavior.Restrict, C = DeleteBehavior.Cascade;
        b.Entity<OrgUnit>(e =>
        {
            e.ToTable("org_units"); e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(30); e.Property(x => x.Name).HasMaxLength(200);
            e.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(R);
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(R);
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(R);
            e.HasIndex(x => x.SiteId).IsUnique().HasFilter("\"SiteId\" IS NOT NULL AND \"Level\" IN (4, 5, 6) AND NOT \"IsArchived\"");
        });
        b.Entity<CorporateStandard>(e =>
        {
            e.ToTable("corporate_standards"); e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.Title).HasMaxLength(300);
            e.HasOne<Domain.Documents.ControlledDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(R);
            UserReq(e, x => x.OwnerId); SiteRefs(e);
        });
        b.Entity<StandardRollout>(e =>
        {
            e.ToTable("standard_rollouts"); e.HasIndex(x => new { x.StandardId, x.SiteId }).IsUnique();
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasOne<CorporateStandard>().WithMany().HasForeignKey(x => x.StandardId).OnDelete(C);
            e.HasOne<Domain.Documents.ControlledDocument>().WithMany().HasForeignKey(x => x.LocalDocumentId).OnDelete(R);
            e.HasOne<Domain.Quality.TrainingCourse>().WithMany().HasForeignKey(x => x.TrainingCourseId).OnDelete(R);
            User(e, x => x.VerifiedById); SiteRefs(e);
        });
        b.Entity<Skill>(e =>
        {
            e.ToTable("skills"); e.HasIndex(x => new { x.SiteId, x.Code }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(40); e.Property(x => x.Name).HasMaxLength(200);
            SiteRefs(e);
        });
        b.Entity<SkillRequirement>(e =>
        {
            e.ToTable("skill_requirements"); e.HasIndex(x => new { x.SkillId, x.SiteId, x.DepartmentId, x.WorkCenterId }).IsUnique().AreNullsDistinct(false);
            e.HasOne<Skill>().WithMany().HasForeignKey(x => x.SkillId).OnDelete(C);
            e.HasOne<Domain.Quality.WorkCenter>().WithMany().HasForeignKey(x => x.WorkCenterId).OnDelete(R);
            SiteRefs(e);
        });
        b.Entity<EmployeeCertification>(e =>
        {
            e.ToTable("employee_certifications"); e.HasIndex(x => new { x.UserId, x.SkillId });
            e.Property(x => x.Reference).HasMaxLength(100); e.Property(x => x.IssuedBy).HasMaxLength(200); e.Property(x => x.RevokedReason).HasMaxLength(1000);
            e.HasOne<Skill>().WithMany().HasForeignKey(x => x.SkillId).OnDelete(R);
            UserReq(e, x => x.UserId); SiteRefs(e);
        });
        b.Entity<TrainingCurriculum>(e =>
        {
            e.ToTable("training_curricula"); e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.Title).HasMaxLength(300);
            e.HasOne<Skill>().WithMany().HasForeignKey(x => x.GrantsSkillId).OnDelete(R);
            SiteRefs(e);
        });
        b.Entity<AuditProgram>(e =>
        {
            e.ToTable("audit_programs"); e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.Title).HasMaxLength(300); e.Property(x => x.Objectives).HasMaxLength(4000);
            UserReq(e, x => x.OwnerId); SiteRefs(e);
        });
        b.Entity<AuditPlanItem>(e =>
        {
            e.ToTable("audit_plan_items", t => t.HasCheckConstraint("ck_plan_month", "\"PlannedMonth\" BETWEEN 1 AND 12"));
            e.HasIndex(x => x.ProgramId);
            e.Property(x => x.AuditType).HasMaxLength(30); e.Property(x => x.Subject).HasMaxLength(300); e.Property(x => x.Criteria).HasMaxLength(1000);
            e.HasOne<AuditProgram>().WithMany().HasForeignKey(x => x.ProgramId).OnDelete(C);
            e.HasOne<Domain.Quality.Audit>().WithMany().HasForeignKey(x => x.AuditId).OnDelete(R);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(R);
            User(e, x => x.LeadAuditorId); SiteRefs(e);
        });
        b.Entity<ComplianceObligation>(e =>
        {
            e.ToTable("compliance_obligations"); e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.PackCode, x.SiteId, x.Reference }).IsUnique().HasFilter("\"PackCode\" IS NOT NULL");
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.PackCode).HasMaxLength(30); e.Property(x => x.Reference).HasMaxLength(60);
            e.Property(x => x.Title).HasMaxLength(300); e.Property(x => x.Party).HasMaxLength(200); e.Property(x => x.Notes).HasMaxLength(2000);
            User(e, x => x.OwnerId); SiteRefs(e);
        });
        b.Entity<CompliancePackActivation>(e =>
        {
            e.ToTable("compliance_pack_activations"); e.HasIndex(x => new { x.PackCode, x.SiteId }).IsUnique();
            e.Property(x => x.PackCode).HasMaxLength(30);
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(R);
        });
        b.Entity<ValidationProject>(e =>
        {
            e.ToTable("validation_projects"); e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.Title).HasMaxLength(300); e.Property(x => x.Scope).HasMaxLength(4000); e.Property(x => x.Conclusion).HasMaxLength(4000);
            e.HasOne<Domain.Quality.Equipment>().WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(R);
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(R);
            UserReq(e, x => x.OwnerId); SiteRefs(e);
        });
        b.Entity<ValidationTest>(e =>
        {
            e.ToTable("validation_tests"); e.HasIndex(x => new { x.ProjectId, x.Stage, x.Sequence }).IsUnique();
            e.Property(x => x.Stage).HasMaxLength(20); e.Property(x => x.Description).HasMaxLength(2000); e.Property(x => x.AcceptanceCriteria).HasMaxLength(2000); e.Property(x => x.ActualResult).HasMaxLength(4000);
            e.HasOne<ValidationProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(C);
            User(e, x => x.ExecutedById);
        });
        b.Entity<ValidationDeviation>(e =>
        {
            e.ToTable("validation_deviations"); e.HasIndex(x => x.ProjectId);
            e.Property(x => x.Description).HasMaxLength(4000); e.Property(x => x.Impact).HasMaxLength(4000); e.Property(x => x.Resolution).HasMaxLength(4000);
            e.HasOne<ValidationProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(C);
            e.HasOne<ValidationTest>().WithMany().HasForeignKey(x => x.TestId).OnDelete(R);
        });
        b.Entity<QualityProject>(e =>
        {
            e.ToTable("quality_projects", t => t.HasCheckConstraint("ck_project_phase", "\"CurrentPhase\" BETWEEN 1 AND 6")); e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.Title).HasMaxLength(300);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(R);
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(R);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(R);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(R);
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.TargetSiteId).OnDelete(R);
            UserReq(e, x => x.OwnerId); SiteRefs(e);
        });
        b.Entity<ProjectDeliverable>(e =>
        {
            e.ToTable("project_deliverables"); e.HasIndex(x => new { x.ProjectId, x.Phase });
            e.Property(x => x.Name).HasMaxLength(300); e.Property(x => x.LinkedType).HasMaxLength(50);
            e.HasOne<QualityProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(C);
            User(e, x => x.OwnerId);
        });
        b.Entity<PhaseGate>(e =>
        {
            e.ToTable("phase_gates"); e.HasIndex(x => new { x.ProjectId, x.Phase });
            e.Property(x => x.DecidedByName).HasMaxLength(200); e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasOne<QualityProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(C);
            UserReq(e, x => x.DecidedById);
        });
        b.Entity<PpapSubmission>(e =>
        {
            e.ToTable("ppap_submissions", t => t.HasCheckConstraint("ck_ppap_level", "\"Level\" BETWEEN 1 AND 5")); e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.PartRevision).HasMaxLength(25); e.Property(x => x.Reason).HasMaxLength(300);
            e.Property(x => x.CustomerDecisionBy).HasMaxLength(200); e.Property(x => x.CustomerComments).HasMaxLength(4000);
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(R);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(R);
            e.HasOne<QualityProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(R);
            UserReq(e, x => x.OwnerId); SiteRefs(e);
        });
        b.Entity<PpapElement>(e =>
        {
            e.ToTable("ppap_elements"); e.HasIndex(x => new { x.SubmissionId, x.Code }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.LinkedType).HasMaxLength(50); e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasOne<PpapSubmission>().WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(C);
        });
        b.Entity<CapabilityStudy>(e =>
        {
            e.ToTable("capability_studies"); e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.Title).HasMaxLength(300); e.Property(x => x.Characteristic).HasMaxLength(300); e.Property(x => x.Process).HasMaxLength(200);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(R);
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(R);
            e.HasOne<Domain.Quality.ControlPlan>().WithMany().HasForeignKey(x => x.ControlPlanId).OnDelete(R);
            SiteRefs(e);
        });
        b.Entity<RetentionPolicy>(e =>
        {
            e.ToTable("retention_policies"); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.RecordType).HasMaxLength(50);
            e.Property(x => x.ProductFamily).HasMaxLength(100); e.Property(x => x.RegulatoryCategory).HasMaxLength(100);
        });
        b.Entity<RetentionDecision>(e =>
        {
            e.ToTable("retention_decisions"); e.HasIndex(x => new { x.RecordType, x.RecordId });
            e.Property(x => x.RecordType).HasMaxLength(50); e.Property(x => x.RecordNumber).HasMaxLength(60); e.Property(x => x.Reason).HasMaxLength(2000);
            e.HasOne<RetentionPolicy>().WithMany().HasForeignKey(x => x.PolicyId).OnDelete(R);
            UserReq(e, x => x.DecidedById);
        });
        b.Entity<ContinuityExercise>(e =>
        {
            e.ToTable("continuity_exercises"); e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.Title).HasMaxLength(300); e.Property(x => x.Findings).HasMaxLength(4000);
            e.Property(x => x.SystemChecks).HasColumnType("jsonb");
            User(e, x => x.PerformedById); SiteRefs(e);
        });
        b.Entity<NotificationChannel>(e =>
        {
            e.ToTable("notification_channels"); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Target).HasMaxLength(1000);
            e.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(R);
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(R);
        });
        b.Entity<ApiClient>(e =>
        {
            e.ToTable("api_clients"); e.HasIndex(x => x.ClientId).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.ClientId).HasMaxLength(60); e.Property(x => x.SecretHash).HasMaxLength(200); e.Property(x => x.SecretHint).HasMaxLength(10);
            UserReq(e, x => x.ServiceUserId);
        });
    }
}
