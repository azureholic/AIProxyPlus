using Microsoft.EntityFrameworkCore;

namespace AIProxyPlus.Infrastructure.Persistence;

/// <summary>Serializes startup schema migrations across replicas.</summary>
public static class DatabaseMigrationExtensions
{
    /// <summary>Applies migrations while holding a PostgreSQL session advisory lock.</summary>
    /// <param name="context">The context using the startup migration credentials.</param>
    public static async Task MigrateWithLockAsync(this AIProxyPlusDbContext context)
    {
        const long migrationLockId = 0x414950524F585950;
        var commandTimeout = context.Database.GetCommandTimeout();
        await context.Database.OpenConnectionAsync();
        var lockAcquired = false;
        try
        {
            try
            {
                context.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
                await context.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_lock({migrationLockId})");
                lockAcquired = true;
            }
            finally
            {
                context.Database.SetCommandTimeout(commandTimeout);
            }

            await context.Database.MigrateAsync();
        }
        finally
        {
            if (lockAcquired)
            {
                await context.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_unlock({migrationLockId})");
            }
            await context.Database.CloseConnectionAsync();
        }
    }
}
