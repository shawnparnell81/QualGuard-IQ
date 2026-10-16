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

        // Shared engines
        services.AddScoped<IRecordRegistry, RecordRegistry>();
        services.AddScoped<ISearchIndexer, SearchIndexer>();
        services.AddScoped<SearchService>();
        services.AddScoped<INotifier, Notifier>();
        services.AddSingleton<IFileStorage, S3FileStorage>();
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
            (DocumentKind.Drawing, DocumentRecordTypes.Drawing, DocumentRecordTypes.DrawingRevision)
        })
        {
            services.AddScoped<IRecordTypeProvider>(sp => new DocumentRecordProvider(sp.GetRequiredService<AppDbContext>(), docType, kind));
            services.AddScoped<IRecordTypeProvider>(sp => new RevisionRecordProvider(sp.GetRequiredService<AppDbContext>(), revType, kind));
            services.AddScoped<IApprovalHandler>(sp => new RevisionApprovalHandler(revType, () => sp.GetRequiredService<DocumentService>()));
        }
        return services;
    }
}
