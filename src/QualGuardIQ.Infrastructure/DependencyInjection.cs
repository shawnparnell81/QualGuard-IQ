using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
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
        services.AddScoped<ISecurityEventLogger, SecurityEventLogger>();
        services.AddScoped<INumberingService, NumberingService>();
        services.AddScoped<Seeder>();
        services.AddScoped<ISiteCalendar, SiteCalendar>();
        services.AddScoped<Jobs.ReminderJob>();

        // Shared engines
        services.AddScoped<IRecordRegistry, RecordRegistry>();
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
        services.AddScoped<ActionService>();
        services.AddScoped<CustomFieldService>();

        // Document control and drawings
        services.AddScoped<DocumentService>();
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
}

