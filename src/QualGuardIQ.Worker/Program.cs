using Hangfire;
using Hangfire.PostgreSql;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Infrastructure;
using QualGuardIQ.Infrastructure.Jobs;

// Background job host: notification email delivery, scheduled releases and daily due-date reminders.
var builder = Host.CreateApplicationBuilder(args);
var cs = builder.Configuration.GetConnectionString("Default") ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, SystemCurrentUser>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddIdentityCore<QualGuardIQ.Infrastructure.Identity.ApplicationUser>()
    .AddEntityFrameworkStores<QualGuardIQ.Infrastructure.Persistence.AppDbContext>();
builder.Services.AddScoped<NotificationEmailJob>();
builder.Services.AddScoped<DocumentReleaseJob>();
builder.Services.AddHangfire(c => c
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(cs), new PostgreSqlStorageOptions { SchemaName = "jobs" }));
builder.Services.AddHangfireServer();
builder.Services.AddHostedService<JobRegistration>();
builder.Build().Run();

sealed class JobRegistration(IRecurringJobManager jobs) : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        jobs.AddOrUpdate<NotificationEmailJob>("notification-email", j => j.RunAsync(CancellationToken.None), Cron.Minutely());
        jobs.AddOrUpdate<DocumentReleaseJob>("document-release", j => j.RunAsync(CancellationToken.None), "5 * * * *");
        jobs.AddOrUpdate<ReminderJob>("due-reminders", j => j.RunAsync(CancellationToken.None), "15 6 * * *");
        jobs.AddOrUpdate<ErpSyncJob>("erp-sync", j => j.RunAsync(CancellationToken.None), Cron.Minutely());
        jobs.AddOrUpdate<AlertJob>("alerts", j => j.RunAsync(CancellationToken.None), "*/15 * * * *");
        jobs.AddOrUpdate<WorkforceJob>("workforce", j => j.RunAsync(CancellationToken.None), "30 5 * * *");
        jobs.AddOrUpdate<DataExportJob>("data-exports", j => j.RunAsync(CancellationToken.None), "*/10 * * * *");
        jobs.AddOrUpdate<PlatformEventJob>("platform-events", j => j.RunAsync(CancellationToken.None), Cron.Minutely());
        jobs.AddOrUpdate<ApprovalEscalationJob>("approval-escalations", j => j.RunAsync(CancellationToken.None), "*/15 * * * *");
        jobs.AddOrUpdate<ReportScheduleJob>("report-schedules", j => j.RunAsync(CancellationToken.None), "*/5 * * * *");
        jobs.AddOrUpdate<LabelPrintQueueJob>("label-print", j => j.RunAsync(CancellationToken.None), Cron.Minutely());
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
