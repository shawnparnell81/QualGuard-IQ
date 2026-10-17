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

        // Shared engines
        services.AddScoped<IRecordRegistry, RecordRegistry>();
        services.AddScoped<ISearchIndexer, SearchIndexer>();
        services.AddScoped<SearchService>();
        services.AddScoped<INotifier, Notifier>();
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
        s.AddScoped<IApprovalHandler>(sp => new DelegateApprovalHandler(NcrService.RecordType,
            (id, ct) => sp.GetRequiredService<NcrService>().OnApprovedAsync(id, ct), (id, _, ct) => sp.GetRequiredService<NcrService>().OnRejectedAsync(id, ct)));
    }
}
