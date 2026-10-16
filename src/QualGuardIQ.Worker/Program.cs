using Hangfire;
using Hangfire.PostgreSql;

// Background job host. Jobs (notification delivery, due-date reminders, retention) are registered
// by the modules that own them; this process only runs the Hangfire server.
var builder = Host.CreateApplicationBuilder(args);
var cs = builder.Configuration.GetConnectionString("Default") ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
builder.Services.AddHangfire(c => c
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(cs), new PostgreSqlStorageOptions { SchemaName = "jobs" }));
builder.Services.AddHangfireServer();
builder.Build().Run();
