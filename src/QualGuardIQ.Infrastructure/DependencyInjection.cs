using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Security;
using QualGuardIQ.Infrastructure.Services;

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
        return services;
    }
}
