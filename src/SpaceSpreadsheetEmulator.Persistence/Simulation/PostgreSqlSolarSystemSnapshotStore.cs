using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SpaceSpreadsheetEmulator.Persistence.Database;
using SpaceSpreadsheetEmulator.Persistence.Entities;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;
using SpaceSpreadsheetEmulator.Simulation.Runtime;

namespace SpaceSpreadsheetEmulator.Persistence.Simulation;

internal sealed class PostgreSqlSolarSystemSnapshotStore(
    IDbContextFactory<GameDbContext> contextFactory,
    TimeProvider timeProvider) : ISolarSystemSnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async ValueTask<SolarSystemSnapshot?> LoadLatestAsync(
        SolarSystemId solarSystemId,
        CancellationToken cancellationToken = default)
    {
        await using GameDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        SolarSystemSnapshotEntity? entity = await context.SolarSystemSnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(
                snapshot => snapshot.SolarSystemId == solarSystemId.Value,
                cancellationToken);
        if (entity is null)
        {
            return null;
        }

        byte[] hash = SHA256.HashData(entity.Payload);
        if (!CryptographicOperations.FixedTimeEquals(hash, entity.PayloadSha256))
        {
            throw new InvalidDataException(
                $"Solar-system snapshot {solarSystemId} failed its payload hash check.");
        }

        SnapshotPayload payload = JsonSerializer.Deserialize<SnapshotPayload>(
            entity.Payload,
            JsonOptions) ?? throw new InvalidDataException("The solar-system snapshot payload is empty.");
        SolarSystemSnapshot snapshot = Map(payload);
        if (snapshot.SolarSystemId != solarSystemId
            || snapshot.FormatVersion != entity.FormatVersion
            || checked((long)snapshot.SourceEpoch.Value) != entity.SourceEpoch
            || checked((long)snapshot.Tick) != entity.Tick
            || checked((long)snapshot.LastSequence) != entity.LastSequence)
        {
            throw new InvalidDataException(
                $"Solar-system snapshot {solarSystemId} metadata does not match its payload.");
        }

        return snapshot;
    }

    public async ValueTask SaveAsync(
        SolarSystemSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        Validate(snapshot);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(Map(snapshot), JsonOptions);
        byte[] hash = SHA256.HashData(payload);
        await using GameDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        SolarSystemSnapshotEntity? entity = await context.SolarSystemSnapshots
            .SingleOrDefaultAsync(
                candidate => candidate.SolarSystemId == snapshot.SolarSystemId.Value,
                cancellationToken);
        if (entity is null)
        {
            entity = new SolarSystemSnapshotEntity
            {
                SolarSystemId = snapshot.SolarSystemId.Value,
                SourceEpoch = checked((long)snapshot.SourceEpoch.Value),
                FormatVersion = snapshot.FormatVersion,
                Tick = checked((long)snapshot.Tick),
                LastSequence = checked((long)snapshot.LastSequence),
                Payload = payload,
                PayloadSha256 = hash,
                CreatedAt = timeProvider.GetUtcNow(),
                Version = 1,
            };
            await context.SolarSystemSnapshots.AddAsync(entity, cancellationToken);
        }
        else
        {
            long sourceEpoch = checked((long)snapshot.SourceEpoch.Value);
            long sequence = checked((long)snapshot.LastSequence);
            if (entity.SourceEpoch > sourceEpoch
                || (entity.SourceEpoch == sourceEpoch && entity.LastSequence > sequence))
            {
                throw new DbUpdateConcurrencyException(
                    "A stale ownership epoch or sequence cannot replace a newer solar-system snapshot.");
            }

            if (entity.SourceEpoch == sourceEpoch && entity.LastSequence == sequence)
            {
                if (!CryptographicOperations.FixedTimeEquals(entity.PayloadSha256, hash))
                {
                    throw new DbUpdateConcurrencyException(
                        "The same solar-system snapshot sequence contains different state.");
                }

                return;
            }

            entity.SourceEpoch = sourceEpoch;
            entity.FormatVersion = snapshot.FormatVersion;
            entity.Tick = checked((long)snapshot.Tick);
            entity.LastSequence = checked((long)snapshot.LastSequence);
            entity.Payload = payload;
            entity.PayloadSha256 = hash;
            entity.CreatedAt = timeProvider.GetUtcNow();
            entity.Version = checked(entity.Version + 1);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static void Validate(SolarSystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.FormatVersion != SolarSystemSnapshot.CurrentFormatVersion
            || snapshot.SourceEpoch.Value == 0
            || snapshot.Ships.Any(ship => ship.ShipId <= 0)
            || snapshot.Ships.Select(ship => ship.CharacterId).Distinct().Count() != snapshot.Ships.Count
            || snapshot.Ships.Select(ship => ship.ShipId).Distinct().Count() != snapshot.Ships.Count)
        {
            throw new InvalidDataException("The solar-system snapshot is invalid.");
        }
    }

    private static SnapshotPayload Map(SolarSystemSnapshot snapshot)
        => new(
            snapshot.FormatVersion,
            snapshot.SolarSystemId.Value,
            snapshot.SourceEpoch.Value,
            snapshot.Tick,
            snapshot.LastSequence,
            snapshot.Ships
                .OrderBy(ship => ship.ShipId)
                .Select(ship => new ShipPayload(
                    ship.CharacterId.Value,
                    ship.ShipId,
                    VectorPayload.From(ship.Position),
                    VectorPayload.From(ship.Velocity),
                    ship.Movement is null ? null : MovementPayload.From(ship.Movement)))
                .ToArray());

    private static SolarSystemSnapshot Map(SnapshotPayload payload)
        => new(
            payload.FormatVersion,
            new SolarSystemId(payload.SolarSystemId),
            new SimulationEpoch(payload.SourceEpoch),
            payload.Tick,
            payload.LastSequence,
            payload.Ships.Select(ship => new SolarShipSnapshot(
                    new CharacterId(ship.CharacterId),
                    ship.ShipId,
                    ship.Position.ToVector(),
                    ship.Velocity.ToVector(),
                    ship.Movement?.ToSnapshot()))
                .ToArray());

    private sealed record SnapshotPayload(
        int FormatVersion,
        int SolarSystemId,
        ulong SourceEpoch,
        ulong Tick,
        ulong LastSequence,
        IReadOnlyList<ShipPayload> Ships);

    private sealed record ShipPayload(
        long CharacterId,
        long ShipId,
        VectorPayload Position,
        VectorPayload Velocity,
        MovementPayload? Movement = null);

    private sealed record MovementPayload(
        SolarMovementIntentKind Kind,
        VectorPayload Direction,
        double RequestedSpeed,
        long? TargetEntityId,
        double DesiredRange,
        VectorPayload? TargetPosition)
    {
        public static MovementPayload From(SolarMovementSnapshot movement)
            => new(
                movement.Kind,
                VectorPayload.From(movement.Direction),
                movement.RequestedSpeed,
                movement.TargetEntityId,
                movement.DesiredRange,
                movement.TargetPosition is { } position ? VectorPayload.From(position) : null);

        public SolarMovementSnapshot ToSnapshot()
            => new(
                Kind,
                Direction.ToVector(),
                RequestedSpeed,
                TargetEntityId,
                DesiredRange,
                TargetPosition?.ToVector());
    }

    private sealed record VectorPayload(double X, double Y, double Z)
    {
        public static VectorPayload From(SolarVector3 vector)
            => new(vector.X, vector.Y, vector.Z);

        public SolarVector3 ToVector() => new(X, Y, Z);
    }
}
