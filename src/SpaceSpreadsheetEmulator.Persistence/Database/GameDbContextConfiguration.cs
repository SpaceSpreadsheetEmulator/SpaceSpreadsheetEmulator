using Microsoft.EntityFrameworkCore;

namespace SpaceSpreadsheetEmulator.Persistence.Database;

internal static class GameDbContextConfiguration
{
    public static void Configure(DbContextOptionsBuilder options, string connectionString)
        => options.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsAssembly(typeof(GameDbContext).Assembly.FullName);
            npgsql.MigrationsHistoryTable("__ef_migrations_history", "operations");
            npgsql.CommandTimeout(30);
        });
}
