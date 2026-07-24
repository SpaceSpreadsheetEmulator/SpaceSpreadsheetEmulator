using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SpaceSpreadsheetEmulator.Gameplay.Characters;
using SpaceSpreadsheetEmulator.Identity.Authentication;
using SpaceSpreadsheetEmulator.Persistence;
using SpaceSpreadsheetEmulator.Persistence.Database;
using SpaceSpreadsheetEmulator.Persistence.IntegrationTests.Support;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;
using SpaceSpreadsheetEmulator.Simulation.Runtime;

namespace SpaceSpreadsheetEmulator.Persistence.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class PersistenceTests(PostgreSqlFixture database) : IAsyncLifetime
{
    public Task InitializeAsync() => database.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

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

    [Fact]
    public async Task LocationTransitionIsAtomicIdempotentAndRecoverable()
    {
        await using ServiceProvider services = database.CreateServices();
        IAccountIdentityStore accounts = services.GetRequiredService<IAccountIdentityStore>();
        AccountEnrollmentResult enrollment = await accounts.GetOrEnrollAsync("Durability-Pilot", 64);
        StoredStarterCharacter starter = await services.GetRequiredService<IStarterCharacterStore>()
            .GetOrCreateAsync(
                enrollment.Account!.AccountId,
                StarterDefinition(),
                DateTimeOffset.UtcNow);
        ICharacterStateReader reader = services.GetRequiredService<ICharacterStateReader>();
        ICharacterLocationWriter writer = services.GetRequiredService<ICharacterLocationWriter>();
        PlayableCharacterState initial = Assert.IsType<PlayableCharacterState>(
            await reader.FindAsync(enrollment.Account.AccountId, starter.CharacterId));
        var undock = new CharacterLocationTransition(
            "persistence-undock-1",
            CharacterLocationTransitionKind.Undock,
            initial.AccountId,
            initial.CharacterId,
            initial.ShipId,
            initial.SolarSystemId,
            initial.StationId,
            initial.CharacterVersion,
            initial.ShipVersion);

        CharacterLocationTransitionResult applied = await writer.TransitionAsync(undock);
        CharacterLocationTransitionResult repeated = await writer.TransitionAsync(undock);

        Assert.False(applied.AlreadyApplied);
        Assert.True(repeated.AlreadyApplied);
        Assert.Null(applied.State.StationId);
        Assert.Equal(applied.State, repeated.State);
        RecoverableInSpaceCharacter recovery = Assert.Single(
            await services.GetRequiredService<ICharacterRuntimeRecoveryReader>()
                .ListAsync(new SolarSystemId(initial.SolarSystemId)));
        Assert.Equal(initial.StationId, recovery.UndockStationId);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await writer.TransitionAsync(undock with
            {
                Kind = CharacterLocationTransitionKind.Dock,
            }));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () =>
            await writer.TransitionAsync(undock with
            {
                IdempotencyKey = "persistence-undock-stale",
            }));

        var dock = new CharacterLocationTransition(
            "persistence-dock-1",
            CharacterLocationTransitionKind.Dock,
            applied.State.AccountId,
            applied.State.CharacterId,
            applied.State.ShipId,
            applied.State.SolarSystemId,
            initial.StationId,
            applied.State.CharacterVersion,
            applied.State.ShipVersion);
        CharacterLocationTransitionResult docked = await writer.TransitionAsync(dock);
        Assert.Equal(initial.StationId, docked.State.StationId);
        Assert.Empty(await services.GetRequiredService<ICharacterRuntimeRecoveryReader>()
            .ListAsync(new SolarSystemId(initial.SolarSystemId)));
    }

    [Fact]
    public async Task ConcurrentDuplicateLocationTransitionCommitsOnlyOnce()
    {
        await using ServiceProvider services = database.CreateServices();
        AccountEnrollmentResult enrollment = await services
            .GetRequiredService<IAccountIdentityStore>()
            .GetOrEnrollAsync("Concurrent-Transition-Pilot", 64);
        StoredStarterCharacter starter = await services
            .GetRequiredService<IStarterCharacterStore>()
            .GetOrCreateAsync(
                enrollment.Account!.AccountId,
                StarterDefinition(),
                DateTimeOffset.UtcNow);
        PlayableCharacterState initial = Assert.IsType<PlayableCharacterState>(
            await services.GetRequiredService<ICharacterStateReader>()
                .FindAsync(enrollment.Account.AccountId, starter.CharacterId));
        var transition = new CharacterLocationTransition(
            "concurrent-undock-1",
            CharacterLocationTransitionKind.Undock,
            initial.AccountId,
            initial.CharacterId,
            initial.ShipId,
            initial.SolarSystemId,
            initial.StationId,
            initial.CharacterVersion,
            initial.ShipVersion);
        ICharacterLocationWriter writer = services.GetRequiredService<ICharacterLocationWriter>();

        CharacterLocationTransitionResult[] results = await Task.WhenAll(
            writer.TransitionAsync(transition).AsTask(),
            writer.TransitionAsync(transition).AsTask());

        Assert.Single(results, result => !result.AlreadyApplied);
        Assert.Single(results, result => result.AlreadyApplied);
        Assert.All(results, result => Assert.Null(result.State.StationId));
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        Assert.Equal(1L, await ScalarAsync(
            connection,
            """
            SELECT count(*)
            FROM operations.character_location_transitions
            WHERE idempotency_key = 'concurrent-undock-1'
            """));
    }

    [Fact]
    public async Task SnapshotStoreDetectsTamperingAndRejectsStaleReplacement()
    {
        await using ServiceProvider services = database.CreateServices();
        ISolarSystemSnapshotStore store = services.GetRequiredService<ISolarSystemSnapshotStore>();
        var systemId = new SolarSystemId(30_002_799);
        var epoch = new SimulationEpoch(7);
        var snapshot = new SolarSystemSnapshot(
            SolarSystemSnapshot.CurrentFormatVersion,
            systemId,
            epoch,
            4,
            8,
            [
                new SolarShipSnapshot(
                    new CharacterId(90_009_999),
                    190_009_999,
                    new SolarVector3(1, 2, 3),
                    new SolarVector3(4, 5, 6),
                    new SolarMovementSnapshot(
                        SolarMovementIntentKind.Follow,
                        SolarVector3.Zero,
                        0,
                        190_009_998,
                        2_500,
                        null)),
            ]);
        await store.SaveAsync(snapshot);
        SolarSystemSnapshot loaded = Assert.IsType<SolarSystemSnapshot>(
            await store.LoadLatestAsync(systemId));
        Assert.Equal(snapshot.SolarSystemId, loaded.SolarSystemId);
        Assert.Equal(snapshot.SourceEpoch, loaded.SourceEpoch);
        Assert.Equal(snapshot.Tick, loaded.Tick);
        Assert.Equal(snapshot.LastSequence, loaded.LastSequence);
        Assert.Equal(snapshot.Ships, loaded.Ships);
        await store.SaveAsync(snapshot);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () =>
            await store.SaveAsync(snapshot with { LastSequence = 7 }));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () =>
            await store.SaveAsync(snapshot with
            {
                Ships =
                [
                    snapshot.Ships[0] with { Position = SolarVector3.Zero },
                ],
            }));

        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var tamper = new NpgsqlCommand(
                """
                UPDATE simulation.solar_system_snapshots
                SET payload_sha256 = decode(repeat('00', 32), 'hex')
                WHERE solar_system_id = 30002799
                """,
                connection);
            Assert.Equal(1, await tamper.ExecuteNonQueryAsync());
        }

        await Assert.ThrowsAsync<InvalidDataException>(
            async () => await store.LoadLatestAsync(systemId));
    }

    [Fact]
    public void PostgreSqlRetryClassifierRecognizesOnlyTransactionRetryCodes()
    {
        var serialization = new PostgresException(
            "serialization",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.SerializationFailure);
        var unique = new PostgresException(
            "unique",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation);
        var deadlock = new PostgresException(
            "deadlock",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.DeadlockDetected);
        var check = new PostgresException(
            "check",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.CheckViolation);

        Assert.True(PostgreSqlRetryClassifier.IsRetryableTransaction(serialization));
        Assert.True(PostgreSqlRetryClassifier.IsRetryableTransaction(unique));
        Assert.True(PostgreSqlRetryClassifier.IsRetryableTransaction(deadlock));
        Assert.True(PostgreSqlRetryClassifier.IsRetryableTransaction(
            new DbUpdateException("wrapped", serialization)));
        Assert.True(PostgreSqlRetryClassifier.IsRetryableTransaction(
            new DbUpdateException("wrapped", unique)));
        Assert.True(PostgreSqlRetryClassifier.IsRetryableTransaction(
            new InvalidOperationException(
                "execution strategy",
                new DbUpdateException("wrapped", serialization))));
        Assert.False(PostgreSqlRetryClassifier.IsRetryableTransaction(check));
        Assert.False(PostgreSqlRetryClassifier.IsRetryableTransaction(
            new DbUpdateException("wrapped", new InvalidOperationException())));
        Assert.False(PostgreSqlRetryClassifier.IsRetryableTransaction(
            new DbUpdateException("unwrapped")));
        Assert.False(PostgreSqlRetryClassifier.IsRetryableTransaction(
            new InvalidOperationException("not PostgreSQL")));
    }

    [Fact]
    public void PersistenceRegistrationRejectsNullServiceCollection()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PersistenceServiceCollectionExtensions.AddGamePersistence(
                null!,
                database.ConnectionString));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-connection-string")]
    [InlineData("Database=game;Username=pilot")]
    [InlineData("Host=localhost;Username=pilot")]
    [InlineData("Host=localhost;Database=game")]
    public void PersistenceRegistrationRejectsIncompleteConnectionStrings(string connectionString)
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() =>
            services.AddGamePersistence(connectionString));
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
