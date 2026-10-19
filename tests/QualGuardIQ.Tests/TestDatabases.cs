using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace QualGuardIQ.Tests;

/// <summary>Clean-up for the throwaway databases some tests create.</summary>
public static class TestDatabases
{
    /// <summary>
    /// Drops the database behind <paramref name="db"/>. Pooled connections to it are closed first. An owner without superuser rights
    /// can't end other sessions (42501), and a session that is still closing keeps the database busy (55006), so it waits and tries again.
    /// </summary>
    public static async Task DropAsync(DbContext db)
    {
        var cs = db.Database.GetConnectionString();
        for (var attempt = 1; ; attempt++)
        {
            if (cs is not null) NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
            try { await db.Database.EnsureDeletedAsync(); return; }
            catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.InsufficientPrivilege or PostgresErrorCodes.ObjectInUse && attempt < 20)
            {
                await Task.Delay(250 * Math.Min(attempt, 4));
            }
        }
    }
}
