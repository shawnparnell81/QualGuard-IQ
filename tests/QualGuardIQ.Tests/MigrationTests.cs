using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Tests;

/// <summary>The committed migrations describe the whole model and apply cleanly in order on an empty database.</summary>
[Collection("api")]
public sealed class MigrationTests(TestApp app)
{
    [Fact]
    public void Model_has_no_changes_missing_from_the_migrations()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(db.Database.HasPendingModelChanges(), "The model differs from the last migration snapshot. Add a migration.");
    }

    [Fact]
    public async Task Every_migration_is_applied_to_the_rebuilt_test_database_in_order()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var all = db.Database.GetMigrations().ToList();
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.NotEmpty(all);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(all, applied);
        Assert.Equal(all.OrderBy(m => m, StringComparer.Ordinal), all);
    }

    [Fact]
    public async Task Migrations_apply_on_an_empty_database_and_the_last_one_rolls_back_and_forward()
    {
        var name = "qgiq_test_mig_" + Guid.NewGuid().ToString("N")[..8];
        var cs = new Npgsql.NpgsqlConnectionStringBuilder(TestApp.ConnectionString) { Database = name }.ConnectionString;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs, o => o.MigrationsHistoryTable("__ef_migrations", "qms"));
        await using var db = new AppDbContext(options.Options);
        try
        {
            await db.Database.MigrateAsync();
            var all = db.Database.GetMigrations().ToList();
            Assert.Equal(all, (await db.Database.GetAppliedMigrationsAsync()).ToList());
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(all[^2]);
            Assert.Equal(all[..^1], (await db.Database.GetAppliedMigrationsAsync()).ToList());
            await migrator.MigrateAsync();
            Assert.Equal(all, (await db.Database.GetAppliedMigrationsAsync()).ToList());
        }
        finally { await TestDatabases.DropAsync(db); }
    }
}
