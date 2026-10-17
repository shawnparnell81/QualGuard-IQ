using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Security;
using QualGuardIQ.Infrastructure.Services;
using QualGuardIQ.Infrastructure.Records;
using QualGuardIQ.Infrastructure.Storage;
using QualGuardIQ.Infrastructure.Modules.Documents;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Modules.MasterData;
using QualGuardIQ.Domain.MasterData;

namespace QualGuardIQ.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var cs = config.GetConnectionString("Default") ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<AuditInterceptor>();
        services.AddDbContext<AppDbContext>((sp, o) =>
            o.UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations", "qms"))
             .AddInterceptors(sp.GetRequiredService<AuditInterceptor>()));
        services.AddScoped<IAccessService, AccessService>();
        Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddScoped<ICallerRestriction, NoCallerRestriction>(services);
        services.AddScoped<ISecurityEventLogger, SecurityEventLogger>();
        services.AddScoped<INumberingService, NumberingService>();
        services.AddScoped<Seeder>();
        services.AddScoped<ISiteCalendar, SiteCalendar>();
        services.AddScoped<Jobs.ReminderJob>();

        // Shared engines
        services.AddScoped<IRecordRegistry, RecordRegistry>();
        services.AddSingleton(new RecordTypeLabels(config.GetSection("RecordTypeLabels").GetChildren().Where(c => c.Value is not null).ToDictionary(c => c.Key, c => c.Value!)));
        services.AddScoped<ISearchIndexer, SearchIndexer>();
        services.AddScoped<SearchService>();
        services.AddScoped<INotifier, Notifier>();
        if (string.Equals(config["Storage:Provider"], "AzureBlob", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IFileStorage, AzureBlobFileStorage>();
        else
            services.AddSingleton<IFileStorage, S3FileStorage>();
        if (string.Equals(config["MalwareScanning:Provider"], "ClamAV", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IMalwareScanner, ClamAvScanner>();
        else
            services.AddSingleton<IMalwareScanner, NoMalwareScanner>();
        services.AddScoped<AttachmentService>();
        services.AddScoped<WorkflowService>();
        services.AddScoped<RecordConditions>();
        services.AddScoped<ActionService>();
        services.AddScoped<CustomFieldService>();

        // Document control and drawings
        services.AddScoped<DocumentService>();
        services.AddScoped<DocumentTypeService>();
        foreach (var (kind, docType, revType) in new[]
        {
            (DocumentKind.Document, DocumentRecordTypes.Document, DocumentRecordTypes.DocumentRevision),
            (DocumentKind.Drawing, DocumentRecordTypes.Drawing, DocumentRecordTypes.DrawingRevision),
            (DocumentKind.FormTemplate, DocumentRecordTypes.FormTemplate, DocumentRecordTypes.FormTemplateRevision)
        })
        {
            services.AddScoped<IRecordTypeProvider>(sp => new DocumentRecordProvider(sp.GetRequiredService<AppDbContext>(), docType, kind));
            services.AddScoped<IRecordTypeProvider>(sp => new RevisionRecordProvider(sp.GetRequiredService<AppDbContext>(), revType, kind));
            services.AddScoped<IApprovalHandler>(sp => new RevisionApprovalHandler(revType, () => sp.GetRequiredService<DocumentService>()));
        }
        AddQuality(services);
        AddIntegration(services, config);
        AddEnterprise(services);
        AddEngineering(services);
        services.AddScoped<Modules.Platform.PlatformService>();
        services.AddScoped<Modules.Platform.DesignerService>();
        services.AddScoped<Modules.Platform.FormLibraryService>();
        services.AddScoped<Modules.Platform.RuleEngine>();
        services.AddScoped<Modules.Platform.DataGovernanceService>();
        services.AddScoped<Modules.Platform.RecoveryService>();
        services.AddScoped<Modules.Platform.MarketplaceService>();
        services.AddScoped<IRecordTypeProvider>(sp => new Modules.Quality.QualityRecordProvider<Domain.Platform.MasterDataChange>(sp.GetRequiredService<AppDbContext>(), Modules.Platform.DataGovernanceService.ChangeType, Permissions.MasterDataView, Permissions.MasterDataEdit,
            x => (x.Number, $"{x.DomainKey} {x.RecordLabel}: {string.Join(", ", x.Changes.Keys)}", x.Status.ToString(), $"/platform/governance?change={x.Id}", x.Status != Domain.Platform.MasterChangeStatus.Draft)));
        services.AddScoped<IApprovalHandler>(sp => new Modules.Quality.DelegateApprovalHandler(Modules.Platform.DataGovernanceService.ChangeType,
            (id, ct) => sp.GetRequiredService<Modules.Platform.DataGovernanceService>().OnApprovedAsync(id, ct), (id, _, ct) => sp.GetRequiredService<Modules.Platform.DataGovernanceService>().OnRejectedAsync(id, ct)));
        services.AddSingleton<Modules.Platform.ApiMetrics>();
        services.AddScoped<Jobs.PlatformEventJob>();
        services.AddScoped<Jobs.ReportScheduleJob>();
        services.AddScoped<Jobs.ApprovalEscalationJob>();
        return services;
    }

    private static void AddQuality(IServiceCollection s)
    {
        s.AddScoped<ModuleSupport>();
        s.AddScoped<FormService>();
        s.AddScoped<NcrService>();
        s.AddScoped<CapaService>();
        s.AddScoped<AuditService>();
        s.AddScoped<TrainingService>();
        s.AddScoped<IRevisionReleasedHandler>(sp => sp.GetRequiredService<TrainingService>());
        s.AddScoped<CalibrationService>();

        void P<T>(string type, string view, string edit, Func<T, (string, string, string, string, bool)> d) where T : QualGuardIQ.Domain.Common.Entity, QualGuardIQ.Domain.Common.ISiteScoped =>
            s.AddScoped<IRecordTypeProvider>(sp => new QualityRecordProvider<T>(sp.GetRequiredService<AppDbContext>(), type, view, edit, d));

        P<FormRecord>(FormService.RecordType, Permissions.FormsView, Permissions.FormsEdit, x => (x.Number, x.Title, x.Status.ToString(), $"/forms/records/{x.Id}", x.Status is FormRecordStatus.Submitted or FormRecordStatus.Approved));
        P<Ncr>(NcrService.RecordType, Permissions.NcrView, Permissions.NcrEdit, x => (x.Number, x.Title, x.Status.ToString(), $"/ncr/{x.Id}", x.Status is NcrStatus.Closed or NcrStatus.Cancelled));
        P<Capa>(CapaService.RecordType, Permissions.CapaView, Permissions.CapaEdit, x => (x.Number, x.Title, x.Status.ToString(), $"/capa/{x.Id}", x.Status is CapaStatus.Closed or CapaStatus.Cancelled));
        P<Audit>(AuditService.RecordType, Permissions.AuditsView, Permissions.AuditsEdit, x => (x.Number, x.Title, x.Status.ToString(), $"/audits/{x.Id}", x.Status is AuditStatus.Closed or AuditStatus.Cancelled));
        P<AuditFinding>(AuditService.FindingType, Permissions.AuditsView, Permissions.AuditsEdit, x => (x.Number, x.Statement.Length > 120 ? x.Statement[..120] : x.Statement, x.Status.ToString(), $"/audits/findings/{x.Id}", x.Status == FindingStatus.Closed));
        P<TrainingCourse>(TrainingService.RecordType, Permissions.TrainingView, Permissions.TrainingEdit, x => (x.Number, x.Title, x.IsArchived ? "Archived" : "Active", $"/training/courses/{x.Id}", false));
        P<Equipment>(CalibrationService.EquipmentType, Permissions.CalibrationView, Permissions.CalibrationEdit, x => (x.Number, x.Name, x.Status.ToString(), $"/calibration/equipment/{x.Id}", x.Status == EquipmentStatus.Retired));
        P<CalibrationEvent>(CalibrationService.EventType, Permissions.CalibrationView, Permissions.CalibrationEdit, x => (x.Number, $"Calibration {x.PerformedOn:yyyy-MM-dd}", x.Result.ToString(), $"/calibration/equipment/{x.EquipmentId}", true));
        P<ImpactAssessment>(CalibrationService.ImpactType, Permissions.CalibrationView, Permissions.CalibrationEdit, x => (x.Number, "Out-of-tolerance impact assessment", x.Status.ToString(), $"/calibration/impact/{x.Id}", x.Status == ImpactStatus.Completed));

        s.AddScoped<IApprovalHandler>(sp => new DelegateApprovalHandler(FormService.RecordType,
            (id, ct) => sp.GetRequiredService<FormService>().OnApprovedAsync(id, ct), (id, _, ct) => sp.GetRequiredService<FormService>().OnRejectedAsync(id, ct)));
        s.AddScoped<MasterDataService>();
        s.AddScoped<SupplierService>();
        s.AddScoped<SupplierQualityService>();
        s.AddScoped<Modules.Complaints.ComplaintService>();
        s.AddScoped<Modules.Risks.RiskService>();
        s.AddScoped<Modules.Reports.ReportService>();
        s.AddScoped<Modules.Fai.AqlService>();
        s.AddScoped<Modules.Fai.FaiService>();
        s.AddScoped<IApprovalHandler>(sp => new DelegateApprovalHandler(Modules.Fai.FaiService.RecordType,
            (id, ct) => sp.GetRequiredService<Modules.Fai.FaiService>().OnApprovedAsync(id, ct), (id, c, ct) => sp.GetRequiredService<Modules.Fai.FaiService>().OnRejectedAsync(id, c, ct)));
        P<FaiRecord>(Modules.Fai.FaiService.RecordType, Permissions.FaiView, Permissions.FaiEdit, x => (x.Number, x.Title, x.Status.ToString(), $"/fai/{x.Id}", x.Status is FaiStatus.PendingApproval or FaiStatus.Approved or FaiStatus.Closed or FaiStatus.Cancelled));
        P<AqlInspection>(Modules.Fai.AqlService.RecordType, Permissions.InspectionView, Permissions.InspectionEdit, x => (x.InspectionNumber, $"AQL sampling {x.InspectionNumber}", x.Status.ToString(), $"/fai/aql/{x.Id}", x.Status != AqlInspectionStatus.Sampling));
        s.AddScoped<MaintenanceService>();
        s.AddScoped<Modules.Objectives.KpiCalculator>();
        s.AddScoped<Modules.Objectives.ObjectiveService>();
        s.AddScoped<Modules.Objectives.ReviewService>();
        s.AddScoped<IApprovalHandler>(sp => new DelegateApprovalHandler(Modules.Objectives.ObjectiveService.RecordType,
            (id, ct) => sp.GetRequiredService<Modules.Objectives.ObjectiveService>().OnApprovedAsync(id, ct), (id, c, ct) => sp.GetRequiredService<Modules.Objectives.ObjectiveService>().OnRejectedAsync(id, c, ct)));
        s.AddScoped<IApprovalHandler>(sp => new DelegateApprovalHandler(Modules.Objectives.ReviewService.RecordType,
            (id, ct) => sp.GetRequiredService<Modules.Objectives.ReviewService>().OnApprovedAsync(id, ct), (id, c, ct) => sp.GetRequiredService<Modules.Objectives.ReviewService>().OnRejectedAsync(id, c, ct)));
        P<QualityObjective>(Modules.Objectives.ObjectiveService.RecordType, Permissions.ObjectivesView, Permissions.ObjectivesEdit, x => (x.Number, x.Title, x.Status.ToString(), $"/objectives/{x.Id}", x.Status is ObjectiveStatus.PendingApproval or ObjectiveStatus.Achieved or ObjectiveStatus.Retired));
        P<ManagementReview>(Modules.Objectives.ReviewService.RecordType, Permissions.ReviewsView, Permissions.ReviewsEdit, x => (x.Number, x.Title, x.Status.ToString(), $"/reviews/{x.Id}", x.Status is ReviewStatus.PendingApproval or ReviewStatus.Approved or ReviewStatus.Cancelled));
        P<MaintenanceEvent>(MaintenanceService.EventType, Permissions.CalibrationView, Permissions.MaintenanceEdit, x => (x.Number, $"{x.Kind} maintenance", x.Kind.ToString(), $"/calibration/equipment/{x.EquipmentId}", true));
        s.AddScoped<IApprovalHandler>(sp => new DelegateApprovalHandler(Modules.Risks.RiskService.RecordType,
            (id, ct) => sp.GetRequiredService<Modules.Risks.RiskService>().OnApprovedAsync(id, ct),
            (id, c, ct) => sp.GetRequiredService<Modules.Risks.RiskService>().OnRejectedAsync(id, c, ct)));
        P<RiskItem>(Modules.Risks.RiskService.RecordType, Permissions.RisksView, Permissions.RisksEdit, x => (x.Number, x.Title, x.Status.ToString(), $"/risks/{x.Id}", x.Status is RiskStatus.Closed or RiskStatus.Cancelled or RiskStatus.PendingAcceptance));
        s.AddScoped<Modules.Inspection.InspectionService>();
        s.AddScoped<Modules.Changes.ChangeService>();
        P<ChangeRequest>(Modules.Changes.ChangeService.RecordType, Permissions.ChangesView, Permissions.ChangesEdit, x => (x.Number, x.Title, x.Status.ToString(), $"/changes/{x.Id}", x.Status is ChangeStatus.Closed or ChangeStatus.Cancelled or ChangeStatus.InReview));
        s.AddScoped<IApprovalHandler>(sp => new DelegateApprovalHandler(Modules.Changes.ChangeService.RecordType,
            (id, ct) => sp.GetRequiredService<Modules.Changes.ChangeService>().OnApprovedAsync(id, ct),
            (id, c, ct) => sp.GetRequiredService<Modules.Changes.ChangeService>().OnRejectedAsync(id, c, ct)));
        P<InspectionPlan>(Modules.Inspection.InspectionService.PlanType, Permissions.InspectionView, Permissions.InspectionEdit, x => (x.Number, x.Title, x.IsObsolete ? "Obsolete" : "Active", $"/inspection/plans/{x.Id}", false));
        P<InspectionPlanRevision>(Modules.Inspection.InspectionService.RevisionType, Permissions.InspectionView, Permissions.InspectionEdit, x => (x.PlanNumber + " rev " + x.Revision, "Inspection plan revision " + x.Revision, x.Status.ToString(), $"/inspection/plans/{x.PlanId}", x.Status != PlanRevisionStatus.Draft));
        P<Inspection>(Modules.Inspection.InspectionService.RecordType, Permissions.InspectionView, Permissions.InspectionEdit, x => (x.Number, $"{x.InspectionType} inspection {x.PartNumber} {x.LotNumber}".Trim(), x.Status.ToString(), $"/inspection/{x.Id}", x.Status is InspectionStatus.Released or InspectionStatus.Rejected or InspectionStatus.Cancelled));
        s.AddScoped<IApprovalHandler>(sp => new DelegateApprovalHandler(Modules.Inspection.InspectionService.RevisionType,
            (id, ct) => sp.GetRequiredService<Modules.Inspection.InspectionService>().OnRevisionApprovedAsync(id, ct),
            (id, _, ct) => sp.GetRequiredService<Modules.Inspection.InspectionService>().OnRevisionRejectedAsync(id, ct)));
        P<Complaint>(Modules.Complaints.ComplaintService.RecordType, Permissions.ComplaintsView, Permissions.ComplaintsEdit, x => (x.Number, x.Title, x.Status.ToString(), $"/complaints/{x.Id}", x.Status is ComplaintStatus.Closed or ComplaintStatus.Cancelled));
        P<SupplierCorrectiveAction>(SupplierQualityService.ScarType, Permissions.SuppliersView, Permissions.SuppliersEdit, x => (x.Number, x.Title, x.Status.ToString(), $"/suppliers/scar/{x.Id}", x.Status is ScarStatus.Closed or ScarStatus.Cancelled));
        P<Product>(MasterDataService.ProductType, Permissions.MasterDataView, Permissions.MasterDataEdit, x => (x.Number, x.Name, x.Status.ToString(), $"/products/items/{x.Id}", false));
        P<Part>(MasterDataService.PartRecordType, Permissions.MasterDataView, Permissions.MasterDataEdit, x => (x.PartNumber, x.Name, x.Status.ToString(), $"/products/parts/{x.Id}", false));
        P<Customer>(MasterDataService.CustomerType, Permissions.MasterDataView, Permissions.MasterDataEdit, x => (x.Code, x.Name, x.Status.ToString(), $"/products/customers/{x.Id}", false));
        P<Supplier>(SupplierService.RecordType, Permissions.SuppliersView, Permissions.SuppliersEdit, x => (x.Code, x.LegalName, x.Status.ToString(), $"/suppliers/{x.Id}", false));
        s.AddScoped<IApprovalHandler>(sp => new DelegateApprovalHandler(SupplierService.RecordType,
            (id, ct) => sp.GetRequiredService<SupplierService>().OnApprovedAsync(id, ct), (id, c, ct) => sp.GetRequiredService<SupplierService>().OnRejectedAsync(id, c, ct)));
        s.AddScoped<IApprovalHandler>(sp => new DelegateApprovalHandler(NcrService.RecordType,
            (id, ct) => sp.GetRequiredService<NcrService>().OnApprovedAsync(id, ct), (id, _, ct) => sp.GetRequiredService<NcrService>().OnRejectedAsync(id, ct)));
        // Prompt 4: APQP, SPC/MSA, LPA, cost of quality, supplier development, analytics, portals.
        s.AddScoped<Modules.Apqp.ApqpService>();
        s.AddScoped<Modules.Apqp.SpcService>();
        s.AddScoped<Modules.Apqp.MsaService>();
        s.AddScoped<Modules.Apqp.LpaService>();
        s.AddScoped<Modules.Apqp.CoqService>();
        s.AddScoped<Modules.Apqp.SupplierDevelopmentService>();
        s.AddScoped<Modules.Apqp.InsightsService>();
        s.AddScoped<Modules.Apqp.PortalService>();
        foreach (var t in new[] { Modules.Apqp.ApqpService.FlowType, Modules.Apqp.ApqpService.PfmeaType, Modules.Apqp.ApqpService.PlanType })
            s.AddScoped<IApprovalHandler>(sp => new DelegateApprovalHandler(t,
                (id, ct) => sp.GetRequiredService<Modules.Apqp.ApqpService>().OnApprovedAsync(t, id, ct),
                (id, c, ct) => sp.GetRequiredService<Modules.Apqp.ApqpService>().OnRejectedAsync(t, id, c, ct)));
        s.AddScoped<IApprovalHandler>(sp => new DelegateApprovalHandler(Modules.Apqp.MsaService.RecordType,
            (id, ct) => sp.GetRequiredService<Modules.Apqp.MsaService>().OnApprovedAsync(id, ct), (id, c, ct) => sp.GetRequiredService<Modules.Apqp.MsaService>().OnRejectedAsync(id, c, ct)));
        s.AddScoped<IApprovalHandler>(sp => new DelegateApprovalHandler(Modules.Apqp.CoqService.ReworkType,
            (id, ct) => sp.GetRequiredService<Modules.Apqp.CoqService>().OnReworkDecidedAsync(id, true, null, ct), (id, c, ct) => sp.GetRequiredService<Modules.Apqp.CoqService>().OnReworkDecidedAsync(id, false, c, ct)));
        static string Doc(ApqpDocument d) => $"{d.Number} rev {d.Revision}";
        static bool Locked(ApqpDocument d) => d.Status != ApqpStatus.Draft;
        P<ProcessFlow>(Modules.Apqp.ApqpService.FlowType, Permissions.ApqpView, Permissions.ApqpEdit, x => (Doc(x), x.Title, x.Status.ToString(), $"/apqp/flows/{x.Id}", Locked(x)));
        P<Pfmea>(Modules.Apqp.ApqpService.PfmeaType, Permissions.ApqpView, Permissions.ApqpEdit, x => (Doc(x), x.Title, x.Status.ToString(), $"/apqp/pfmea/{x.Id}", Locked(x)));
        P<ControlPlan>(Modules.Apqp.ApqpService.PlanType, Permissions.ApqpView, Permissions.ApqpEdit, x => (Doc(x), x.Title, x.Status.ToString(), $"/apqp/plans/{x.Id}", Locked(x)));
        P<SpcChart>(Modules.Apqp.SpcService.RecordType, Permissions.SpcView, Permissions.SpcEdit, x => (x.Number, x.Title, x.IsArchived ? "Archived" : "Active", $"/spc/{x.Id}", x.IsArchived));
        P<MsaStudy>(Modules.Apqp.MsaService.RecordType, Permissions.SpcView, Permissions.SpcEdit, x => (x.Number, x.Title, x.Status.ToString(), $"/spc/msa/{x.Id}", x.Status is MsaStatus.PendingApproval or MsaStatus.Approved));
        P<LpaAudit>(Modules.Apqp.LpaService.RecordType, Permissions.LpaView, Permissions.LpaEdit, x => (x.Number, "Layered process audit due " + x.DueOn.ToString("yyyy-MM-dd"), x.Status.ToString(), $"/lpa/{x.Id}", x.Status == LpaStatus.Completed));
        P<SupplierProgram>(Modules.Apqp.SupplierDevelopmentService.RecordType, Permissions.SuppliersView, Permissions.SuppliersEdit, x => (x.Number, x.Title, x.Status.ToString(), $"/suppliers/programs/{x.Id}", x.Status is SupplierProgramStatus.Completed or SupplierProgramStatus.Cancelled));
        P<ScrapEvent>(Modules.Apqp.CoqService.ScrapType, Permissions.CoqView, Permissions.CoqEdit, x => (x.Number, "Scrap: " + x.Reason, "Recorded", $"/coq/scrap?id={x.Id}", true));
        P<ReworkOrder>(Modules.Apqp.CoqService.ReworkType, Permissions.CoqView, Permissions.CoqEdit, x => (x.Number, "Rework: " + (x.Instructions.Length > 80 ? x.Instructions[..80] : x.Instructions), x.Status.ToString(), $"/coq/rework/{x.Id}", x.Status is ReworkStatus.Validated or ReworkStatus.PendingApproval));
    }

    /// <summary>Prompt 6: multi-site governance, workforce qualification, compliance, validation, APQP projects/PPAP/capability, retention, continuity and public API.</summary>
    private static void AddEnterprise(IServiceCollection s)
    {
        s.AddHttpClient("notify", c => c.Timeout = TimeSpan.FromSeconds(10));
        s.AddScoped<Modules.Enterprise.QualificationService>();
        s.AddScoped<Modules.Enterprise.WorkforceService>();
        s.AddScoped<Modules.Quality.ITrainingCompletedHandler>(sp => sp.GetRequiredService<Modules.Enterprise.WorkforceService>());
        s.AddScoped<Modules.Enterprise.EnterpriseService>();
        s.AddScoped<Modules.Enterprise.ComplianceService>();
        s.AddScoped<Modules.Enterprise.AuditProgramService>();
        s.AddScoped<Modules.Enterprise.ValidationService>();
        s.AddScoped<Modules.Enterprise.ProjectService>();
        s.AddScoped<Modules.Enterprise.GovernanceService>();
        s.AddScoped<Modules.Enterprise.RetentionEngine>();
        s.AddScoped<Modules.Enterprise.ComplianceMappingService>();
        s.AddScoped<Modules.Integration.ErpValidationService>();
        s.AddScoped<Modules.Enterprise.ImpactService>();
        s.AddScoped<Jobs.WorkforceJob>();
        void P<T>(string type, string view, string edit, Func<T, (string, string, string, string, bool)> d) where T : QualGuardIQ.Domain.Common.Entity, QualGuardIQ.Domain.Common.ISiteScoped =>
            s.AddScoped<IRecordTypeProvider>(sp => new QualityRecordProvider<T>(sp.GetRequiredService<AppDbContext>(), type, view, edit, d));
        P<Domain.Enterprise.CorporateStandard>(Modules.Enterprise.EnterpriseService.StandardType, Permissions.EnterpriseView, Permissions.EnterpriseEdit, x => (x.Number, x.Title, x.Status.ToString(), $"/enterprise/standards/{x.Id}", x.Status == Domain.Enterprise.StandardStatus.Retired));
        P<Domain.Enterprise.Skill>(Modules.Enterprise.WorkforceService.SkillType, Permissions.TrainingView, Permissions.TrainingEdit, x => (x.Code, x.Name, x.Kind.ToString(), $"/training/skills/{x.Id}", x.IsArchived));
        P<Domain.Enterprise.TrainingCurriculum>(Modules.Enterprise.WorkforceService.CurriculumType, Permissions.TrainingView, Permissions.TrainingEdit, x => (x.Number, x.Title, x.Kind.ToString(), $"/training/curricula/{x.Id}", x.IsArchived));
        P<Domain.Enterprise.AuditProgram>(Modules.Enterprise.AuditProgramService.RecordType, Permissions.AuditsView, Permissions.AuditsEdit, x => (x.Number, x.Title, x.Status.ToString(), $"/audits/programs/{x.Id}", x.Status == Domain.Enterprise.AuditProgramStatus.Closed));
        P<Domain.Enterprise.ComplianceObligation>(Modules.Enterprise.ComplianceService.ObligationType, Permissions.ComplianceView, Permissions.ComplianceEdit, x => (x.Number, $"{x.Reference} {x.Title}", x.Applicable ? "Applicable" : "Not applicable", $"/compliance/obligations/{x.Id}", false));
        P<Domain.Enterprise.ValidationProject>(Modules.Enterprise.ValidationService.RecordType, Permissions.ValidationView, Permissions.ValidationEdit, x => (x.Number, x.Title, x.Status.ToString(), $"/validation/{x.Id}", x.Status is Domain.Enterprise.ValidationStatus.Released or Domain.Enterprise.ValidationStatus.Cancelled));
        P<Domain.Enterprise.QualityProject>(Modules.Enterprise.ProjectService.ProjectType, Permissions.ApqpView, Permissions.ApqpEdit, x => (x.Number, x.Title, x.Status.ToString(), $"/launch/projects/{x.Id}", x.Status is Domain.Enterprise.QualityProjectStatus.Completed or Domain.Enterprise.QualityProjectStatus.Cancelled));
        P<Domain.Enterprise.PpapSubmission>(Modules.Enterprise.ProjectService.PpapType, Permissions.ApqpView, Permissions.ApqpEdit, x => (x.Number, "PPAP level " + x.Level + " " + x.Reason, x.Status.ToString(), $"/launch/ppap/{x.Id}", x.Status is Domain.Enterprise.PpapStatus.Approved));
        P<Domain.Enterprise.CapabilityStudy>(Modules.Enterprise.ProjectService.CapabilityType, Permissions.ApqpView, Permissions.ApqpEdit, x => (x.Number, x.Title, x.Cpk is { } k && k >= x.RequiredCpk ? "Capable" : "Not capable", $"/launch/capability/{x.Id}", true));
        P<Domain.Enterprise.ContinuityExercise>(Modules.Enterprise.GovernanceService.ContinuityType, Permissions.GovernanceView, Permissions.GovernanceEdit, x => (x.Number, x.Title, x.Result.ToString(), $"/governance/continuity/{x.Id}", x.Result != Domain.Enterprise.ContinuityResult.Planned));
        s.AddScoped<IApprovalHandler>(sp => new DelegateApprovalHandler(Modules.Enterprise.ValidationService.RecordType,
            (id, ct) => sp.GetRequiredService<Modules.Enterprise.ValidationService>().OnApprovedAsync(id, ct), (id, c, ct) => sp.GetRequiredService<Modules.Enterprise.ValidationService>().OnRejectedAsync(id, c, ct)));
    }

    /// <summary>Prompt 7: engineering quality, knowledge, enterprise intelligence, advisor, security governance.</summary>
    private static void AddEngineering(IServiceCollection s)
    {
        s.AddScoped<Modules.Engineering.EngineeringService>();
        s.AddScoped<Modules.Engineering.IntelligenceService>();
        s.AddScoped<Modules.Engineering.KnowledgeService>();
        s.AddScoped<Modules.Engineering.SecurityGovernanceService>();
        s.AddScoped<Modules.Engineering.AdvisorService>();
        void P<T>(string type, string view, string edit, Func<T, (string, string, string, string, bool)> d) where T : QualGuardIQ.Domain.Common.Entity, QualGuardIQ.Domain.Common.ISiteScoped =>
            s.AddScoped<IRecordTypeProvider>(sp => new QualityRecordProvider<T>(sp.GetRequiredService<AppDbContext>(), type, view, edit, d));
        P<Domain.Enterprise.ProductRequirement>(Modules.Engineering.EngineeringService.RequirementType, Permissions.EngineeringView, Permissions.EngineeringEdit, x => (x.Number, x.Title, x.Status.ToString(), $"/engineering/requirements/{x.Id}", x.Status == Domain.Enterprise.RequirementStatus.Obsolete));
        P<Domain.Enterprise.DesignReview>(Modules.Engineering.EngineeringService.ReviewType, Permissions.EngineeringView, Permissions.EngineeringEdit, x => (x.Number, x.Title, x.Status.ToString(), $"/engineering/reviews/{x.Id}", x.Status is Domain.Enterprise.DesignReviewStatus.Approved or Domain.Enterprise.DesignReviewStatus.PendingApproval or Domain.Enterprise.DesignReviewStatus.Cancelled));
        P<Domain.Enterprise.KnowledgeArticle>(Modules.Engineering.KnowledgeService.KnowledgeType, Permissions.KnowledgeView, Permissions.KnowledgeEdit, x => (x.Number, x.Title, x.Published ? "Published" : "Draft", $"/knowledge/{x.Id}", false));
        P<Domain.Enterprise.BusinessProcess>(Modules.Engineering.KnowledgeService.ProcessType, Permissions.ProcessView, Permissions.ProcessEdit, x => (x.Code, x.Name, "Active", $"/processes/{x.Id}", false));
        P<Domain.Enterprise.MaturityAssessment>(Modules.Engineering.KnowledgeService.MaturityType, Permissions.ProcessView, Permissions.ProcessEdit, x => (x.Number, $"Maturity: {x.SubjectName}", x.Overall.ToString("0.00"), $"/processes/maturity?id={x.Id}", true));
        s.AddScoped<IApprovalHandler>(sp => new DelegateApprovalHandler(Modules.Engineering.EngineeringService.ReviewType,
            (id, ct) => sp.GetRequiredService<Modules.Engineering.EngineeringService>().OnReviewApprovedAsync(id, ct), (id, c, ct) => sp.GetRequiredService<Modules.Engineering.EngineeringService>().OnReviewRejectedAsync(id, c, ct)));
    }

    private static void AddIntegration(IServiceCollection s, IConfiguration config)
    {
        var dp = s.AddDataProtection().SetApplicationName("QualGuardIQ");
        if (config["DataProtection:KeyPath"] is { Length: > 0 } keyPath) dp.PersistKeysToFileSystem(new System.IO.DirectoryInfo(keyPath));
        s.AddSingleton<Modules.Integration.SecretProtector>();
        s.AddSingleton<Modules.Integration.ErpHttpHandlerProvider>();
        s.AddSingleton<Modules.Integration.Erp.ErpTokenCache>();
        foreach (var a in new Modules.Integration.Erp.IErpAdapter[] { new Modules.Integration.Erp.NetSuiteAdapter(), new Modules.Integration.Erp.SapAdapter(), new Modules.Integration.Erp.OracleAdapter(),
            new Modules.Integration.Erp.DynamicsAdapter(), new Modules.Integration.Erp.AcumaticaAdapter(), new Modules.Integration.Erp.InforAdapter(), new Modules.Integration.Erp.EpicorAdapter(),
            new Modules.Integration.Erp.PlexAdapter(), new Modules.Integration.Erp.DelmiaWorksAdapter(), new Modules.Integration.Erp.CustomRestAdapter(), new Modules.Integration.Erp.CsvExchangeAdapter() })
            s.AddSingleton(a);
        s.AddSingleton<Modules.Integration.ErpAdapterRegistry>();
        s.AddScoped<Modules.Integration.ErpSyncService>();
        s.AddScoped<Jobs.ErpSyncJob>();
        s.AddScoped<IRecordTypeProvider>(sp => new QualityRecordProvider<Domain.Integration.ErpChange>(sp.GetRequiredService<AppDbContext>(), Modules.Integration.ErpSyncService.ChangeType,
            Permissions.IntegrationsView, Permissions.IntegrationsEdit, x => (x.Number, $"{x.Action} {x.Entity} {x.ExternalId}", x.Status.ToString(), $"/integrations/changes/{x.Id}", x.Status != Domain.Integration.ChangeQueueStatus.Pending)));
        s.AddScoped<IApprovalHandler>(sp => new DelegateApprovalHandler(Modules.Integration.ErpSyncService.ChangeType,
            (id, ct) => sp.GetRequiredService<Modules.Integration.ErpSyncService>().OnApprovedAsync(id, ct), (id, c, ct) => sp.GetRequiredService<Modules.Integration.ErpSyncService>().OnRejectedAsync(id, c, ct)));

        s.AddScoped<Modules.Traceability.TraceabilityService>();
        s.AddScoped<Modules.Traceability.RecallService>();
        s.AddScoped<Modules.Traceability.ReturnService>();
        s.AddScoped(sp => new Modules.Traceability.BarcodeService(sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<ModuleSupport>())
        {
            LocationResolver = (raw, code, ct) => sp.GetRequiredService<Modules.Inventory.InventoryService>().ResolveLocationAsync(raw, code, ct)
        });
        s.AddScoped<Modules.Inventory.InventoryService>();
        s.AddScoped<Modules.Inventory.ReceivingService>();
        s.AddScoped<Modules.Inventory.ShippingService>();
        s.AddScoped<Modules.Inventory.CountService>();
        s.AddScoped<Modules.Inventory.WarehouseInsights>();
        s.AddScoped<Modules.Production.ProductionService>();
        s.AddScoped<Modules.Analytics.KpiEngine>();
        s.AddScoped<Modules.Analytics.CopilotService>();
        s.AddScoped<Modules.Analytics.AlertService>();
        s.AddScoped<Modules.Analytics.DatasetService>();
        s.AddScoped<Jobs.AlertJob>();
        s.AddScoped<Jobs.DataExportJob>();
        void P<T>(string type, string view, string edit, Func<T, (string, string, string, string, bool)> d) where T : QualGuardIQ.Domain.Common.Entity, QualGuardIQ.Domain.Common.ISiteScoped =>
            s.AddScoped<IRecordTypeProvider>(sp => new QualityRecordProvider<T>(sp.GetRequiredService<AppDbContext>(), type, view, edit, d));
        P<Lot>(Modules.Traceability.TraceabilityService.LotType, Permissions.TraceView, Permissions.TraceEdit, x => (x.Number, $"{x.Kind} lot", x.Status.ToString(), $"/trace/lots/{x.Id}", false));
        P<SerialUnit>(Modules.Traceability.TraceabilityService.SerialType, Permissions.TraceView, Permissions.TraceEdit, x => (x.SerialNumber, "Serial " + x.SerialNumber, x.Status.ToString(), $"/trace/serials/{x.Id}", false));
        P<Recall>(Modules.Traceability.RecallService.RecordType, Permissions.RecallsView, Permissions.RecallsEdit, x => (x.Number, x.Title, x.Status.ToString(), $"/field/recalls/{x.Id}", x.Status is RecallStatus.PendingClosure or RecallStatus.Closed or RecallStatus.Cancelled));
        P<CustomerReturn>(Modules.Traceability.ReturnService.ReturnType, Permissions.ReturnsView, Permissions.ReturnsEdit, x => (x.Number, x.ReturnReason.Length > 120 ? x.ReturnReason[..120] : x.ReturnReason, x.Status.ToString(), $"/field/returns/{x.Id}", x.Status is ReturnStatus.Closed or ReturnStatus.Cancelled));
        P<WarrantyClaim>(Modules.Traceability.ReturnService.WarrantyType, Permissions.ReturnsView, Permissions.ReturnsEdit, x => (x.Number, x.FailureMode, x.Status.ToString(), $"/field/warranty/{x.Id}", x.Status is not (WarrantyClaimStatus.Submitted or WarrantyClaimStatus.UnderReview)));
        P<WorkOrder>(Modules.Production.ProductionService.WorkOrderType, Permissions.ProductionView, Permissions.ProductionEdit, x => (x.Number, $"{x.ItemNumber} × {x.Quantity:0.##}", x.Status.ToString(), $"/production/work-orders/{x.Id}", x.Status is WorkOrderStatus.Closed or WorkOrderStatus.Cancelled));
        P<PurchaseOrder>(Modules.Production.ProductionService.PurchaseOrderType, Permissions.ProductionView, Permissions.ProductionEdit, x => (x.Number, "Purchase order " + x.Number, x.Status.ToString(), $"/production/purchase-orders/{x.Id}", false));
        P<WarehouseLocation>(Modules.Inventory.InventoryService.LocationType, Permissions.InventoryView, Permissions.InventoryEdit, x => (x.Code, x.Name, x.Active ? x.Purpose.ToString() : "Inactive", $"/warehouse/locations/{x.Id}", false));
        P<InventoryHold>(Modules.Inventory.InventoryService.HoldType, Permissions.InventoryView, Permissions.InventoryEdit, x => (x.Number, $"{x.Kind} hold: {(x.Reason.Length > 100 ? x.Reason[..100] : x.Reason)}", x.Status.ToString(), $"/warehouse/holds/{x.Id}", x.Status is HoldStatus.Released or HoldStatus.Cancelled));
        P<StockTransfer>(Modules.Inventory.InventoryService.TransferType, Permissions.InventoryView, Permissions.InventoryEdit, x => (x.Number, $"{x.Kind} transfer", x.Status.ToString(), $"/warehouse/transfers/{x.Id}", x.Status is TransferStatus.Received or TransferStatus.Cancelled));
        P<Receipt>(Modules.Inventory.ReceivingService.ReceiptType, Permissions.InventoryView, Permissions.InventoryEdit, x => (x.Number, $"{x.Kind} receipt {x.PurchaseOrderNumber ?? x.AsnNumber}".Trim(), x.Status.ToString(), $"/warehouse/receipts/{x.Id}", x.Status is ReceiptStatus.Completed or ReceiptStatus.Cancelled));
        P<MrbCase>(Modules.Inventory.ReceivingService.MrbType, Permissions.InventoryView, Permissions.InventoryEdit, x => (x.Number, $"Material review ({x.Disposition})", x.Status.ToString(), $"/warehouse/mrb/{x.Id}", x.Status is MrbStatus.PendingApproval or MrbStatus.Executed or MrbStatus.Cancelled));
        P<SupplierReturn>(Modules.Inventory.ReceivingService.RtvType, Permissions.InventoryView, Permissions.InventoryEdit, x => (x.Number, "Return to supplier", x.Status.ToString(), $"/warehouse/supplier-returns/{x.Id}", x.Status is SupplierReturnStatus.Closed or SupplierReturnStatus.Cancelled));
        P<CycleCount>(Modules.Inventory.CountService.CountType, Permissions.InventoryView, Permissions.InventoryEdit, x => (x.Number, $"{x.Frequency} count", x.Status.ToString(), $"/warehouse/counts/{x.Id}", x.Status is CountStatus.PendingApproval or CountStatus.Posted or CountStatus.Cancelled));
        P<Shipment>(Modules.Inventory.ShippingService.ShipmentType, Permissions.ShippingView, Permissions.ShippingEdit, x => (x.Number, $"{x.Kind} shipment {x.CustomerPo}".Trim(), x.Status.ToString(), $"/warehouse/shipments/{x.Id}", x.Status is ShipmentStatus.Shipped or ShipmentStatus.Delivered or ShipmentStatus.Cancelled));
        s.AddScoped<IApprovalHandler>(sp => new DelegateApprovalHandler(Modules.Inventory.InventoryService.HoldType,
            (id, ct) => sp.GetRequiredService<Modules.Inventory.InventoryService>().OnHoldApprovedAsync(id, ct), (id, c, ct) => sp.GetRequiredService<Modules.Inventory.InventoryService>().OnHoldRejectedAsync(id, c, ct)));
        s.AddScoped<IApprovalHandler>(sp => new DelegateApprovalHandler(Modules.Inventory.ReceivingService.MrbType,
            (id, ct) => sp.GetRequiredService<Modules.Inventory.ReceivingService>().OnMrbApprovedAsync(id, ct), (id, c, ct) => sp.GetRequiredService<Modules.Inventory.ReceivingService>().OnMrbRejectedAsync(id, c, ct)));
        s.AddScoped<IApprovalHandler>(sp => new DelegateApprovalHandler(Modules.Inventory.CountService.CountType,
            (id, ct) => sp.GetRequiredService<Modules.Inventory.CountService>().OnApprovedAsync(id, ct), (id, c, ct) => sp.GetRequiredService<Modules.Inventory.CountService>().OnRejectedAsync(id, c, ct)));
        s.AddScoped<IApprovalHandler>(sp => new DelegateApprovalHandler(Modules.Traceability.RecallService.RecordType,
            (id, ct) => sp.GetRequiredService<Modules.Traceability.RecallService>().OnApprovedAsync(id, ct), (id, c, ct) => sp.GetRequiredService<Modules.Traceability.RecallService>().OnRejectedAsync(id, c, ct)));
    }
}
