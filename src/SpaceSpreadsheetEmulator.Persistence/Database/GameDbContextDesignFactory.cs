using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SpaceSpreadsheetEmulator.Persistence.Database;

internal sealed class GameDbContextDesignFactory : IDesignTimeDbContextFactory<GameDbContext>
{
    private const string ConnectionEnvironmentVariable = "SSE_GAME_DATABASE_CONNECTION";

    public GameDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)
            ?? "Host=127.0.0.1;Port=5432;Database=space_spreadsheet_emulator;"
            + "Username=sse;Password=design-time-only";
        var options = new DbContextOptionsBuilder<GameDbContext>();
        GameDbContextConfiguration.Configure(options, connectionString);
        return new GameDbContext(options.Options);
    }
}
