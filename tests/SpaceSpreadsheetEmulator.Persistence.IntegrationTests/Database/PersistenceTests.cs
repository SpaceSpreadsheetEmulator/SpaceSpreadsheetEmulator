using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SpaceSpreadsheetEmulator.Gameplay.Characters;
using SpaceSpreadsheetEmulator.Identity.Authentication;
using SpaceSpreadsheetEmulator.Persistence.Database;
using SpaceSpreadsheetEmulator.Persistence.IntegrationTests.Support;

namespace SpaceSpreadsheetEmulator.Persistence.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class PersistenceTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task MigrationCreatesModuleSchemasAndEnforcesSingletonQuantity()
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();

        await using (var schemaCommand = new NpgsqlCommand(
                         """
                         SELECT count(*)
                         FROM information_schema.schemata
                         WHERE schema_name IN ('identity', 'characters', 'inventory', 'operations')
                         """,
                         connection))
        {
            Assert.Equal(4L, await schemaCommand.ExecuteScalarAsync());
        }

        await using var invalidItem = new NpgsqlCommand(
            """
            INSERT INTO inventory.items (
                item_id, type_id, owner_id, location_id, location_kind, flag,
                quantity, singleton, custom_name, created_at, updated_at, version)
            VALUES (
                999999001, 601, 90000001, 90000001, 1, 1,
                2, TRUE, 'invalid', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 1)
            """,
            connection);
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(
            async () => await invalidItem.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task AccountCharacterAndShipSurviveFreshServiceProvider()
    {
        long accountId;
        StoredStarterCharacter firstCharacter;
        await using (ServiceProvider firstServices = database.CreateServices())
        {
            IAccountIdentityStore accounts = firstServices.GetRequiredService<IAccountIdentityStore>();
            AccountEnrollmentResult enrollment = await accounts.GetOrEnrollAsync("Pilot-B", 64);
            accountId = enrollment.Account!.AccountId.Value;
            firstCharacter = await firstServices.GetRequiredService<IStarterCharacterStore>()
                .GetOrCreateAsync(
                    enrollment.Account.AccountId,
                    StarterDefinition(),
                    DateTimeOffset.UtcNow);
        }

        await using (ServiceProvider secondServices = database.CreateServices())
        {
            IAccountIdentityStore accounts = secondServices.GetRequiredService<IAccountIdentityStore>();
            AccountEnrollmentResult enrollment = await accounts.GetOrEnrollAsync("pilot-b", 64);
            StoredStarterCharacter reloaded = await secondServices.GetRequiredService<IStarterCharacterStore>()
                .GetOrCreateAsync(
                    enrollment.Account!.AccountId,
                    StarterDefinition(),
                    DateTimeOffset.UtcNow.AddMinutes(1));

            Assert.Equal(accountId, enrollment.Account.AccountId.Value);
            Assert.Equal(firstCharacter.CharacterId, reloaded.CharacterId);
            Assert.Equal(firstCharacter.ShipId, reloaded.ShipId);
            Assert.Equal("Cell Reference", reloaded.ShipName);
        }

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        Assert.Equal(1L, await ScalarAsync(
            connection,
            "SELECT count(*) FROM identity.accounts WHERE normalized_user_name = 'PILOT-B'"));
        Assert.Equal(1L, await ScalarAsync(
            connection,
            """
            SELECT count(*)
            FROM characters.characters c
            JOIN identity.accounts a USING (account_id)
            WHERE a.normalized_user_name = 'PILOT-B'
            """));
        Assert.Equal(1L, await ScalarAsync(
            connection,
            """
            SELECT count(*)
            FROM inventory.items i
            JOIN characters.characters c
              ON c.active_ship_item_id = i.item_id
            JOIN identity.accounts a USING (account_id)
            WHERE a.normalized_user_name = 'PILOT-B'
            """));
    }

    [Fact]
    public async Task ConcurrentEnrollmentCreatesOneCaseInsensitiveIdentity()
    {
        await using ServiceProvider services = database.CreateServices();
        IAccountIdentityStore accounts = services.GetRequiredService<IAccountIdentityStore>();

        AccountEnrollmentResult[] results = await Task.WhenAll(
            accounts.GetOrEnrollAsync("Concurrent-Pilot", 64).AsTask(),
            accounts.GetOrEnrollAsync("concurrent-pilot", 64).AsTask());

        Assert.All(results, result => Assert.NotNull(result.Account));
        Assert.Equal(results[0].Account!.AccountId, results[1].Account!.AccountId);
    }

    [Fact]
    public async Task StaleCharacterVersionIsRejected()
    {
        await using ServiceProvider services = database.CreateServices();
        IAccountIdentityStore accounts = services.GetRequiredService<IAccountIdentityStore>();
        AccountEnrollmentResult enrollment = await accounts.GetOrEnrollAsync("Versioned-Pilot", 64);
        await services.GetRequiredService<IStarterCharacterStore>().GetOrCreateAsync(
            enrollment.Account!.AccountId,
            StarterDefinition(),
            DateTimeOffset.UtcNow);

        IDbContextFactory<GameDbContext> factory =
            services.GetRequiredService<IDbContextFactory<GameDbContext>>();
        await using GameDbContext firstContext = await factory.CreateDbContextAsync();
        await using GameDbContext staleContext = await factory.CreateDbContextAsync();
        long accountId = enrollment.Account.AccountId.Value;
        var firstCharacter = await firstContext.Characters.SingleAsync(
            character => character.AccountId == accountId);
        var staleCharacter = await staleContext.Characters.SingleAsync(
            character => character.AccountId == accountId);

        firstCharacter.Version++;
        firstCharacter.UpdatedAt = DateTimeOffset.UtcNow;
        await firstContext.SaveChangesAsync();

        staleCharacter.Version++;
        staleCharacter.UpdatedAt = DateTimeOffset.UtcNow.AddSeconds(1);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            async () => await staleContext.SaveChangesAsync());
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static StarterCharacterDefinition StarterDefinition()
        => new(
            "Spreadsheet Pilot",
            1,
            1,
            10,
            1373,
            1000002,
            60000004,
            30002780,
            20000407,
            10000033,
            601,
            "Cell Reference");
}
