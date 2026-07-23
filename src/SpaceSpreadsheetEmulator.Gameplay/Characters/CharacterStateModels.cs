using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Gameplay.Characters;

/// <summary>
/// Represents the durable playable state of one account-owned character and active ship.
/// </summary>
public sealed record PlayableCharacterState(
    AccountId AccountId,
    CharacterId CharacterId,
    string CharacterName,
    long ShipId,
    int ShipTypeId,
    string ShipName,
    int SolarSystemId,
    int? StationId,
    long CharacterVersion,
    long ShipVersion);

/// <summary>
/// Reads authoritative character and active-ship state without exposing persistence entities.
/// </summary>
public interface ICharacterStateReader
{
    ValueTask<PlayableCharacterState?> FindAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<PlayableCharacterState>> ListInSpaceAsync(
        SolarSystemId solarSystemId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Supplies the durable state needed to rebuild one in-space runtime after a restart.
/// </summary>
public sealed record RecoverableInSpaceCharacter(
    PlayableCharacterState State,
    int UndockStationId);

/// <summary>
/// Reads authoritative in-space characters together with their last committed entry station.
/// </summary>
public interface ICharacterRuntimeRecoveryReader
{
    ValueTask<IReadOnlyList<RecoverableInSpaceCharacter>> ListAsync(
        SolarSystemId solarSystemId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Identifies the durable location transition requested for a character's active ship.
/// </summary>
public enum CharacterLocationTransitionKind
{
    Undock = 1,
    Dock = 2,
}

/// <summary>
/// Carries an idempotent, optimistic character-location transition.
/// </summary>
public sealed record CharacterLocationTransition(
    string IdempotencyKey,
    CharacterLocationTransitionKind Kind,
    AccountId AccountId,
    CharacterId CharacterId,
    long ShipId,
    int SolarSystemId,
    int? StationId,
    long ExpectedCharacterVersion,
    long ExpectedShipVersion);

/// <summary>
/// Reports the authoritative state after a location transition.
/// </summary>
public sealed record CharacterLocationTransitionResult(
    PlayableCharacterState State,
    bool AlreadyApplied);

/// <summary>
/// Atomically moves a character and active ship between station and solar-system locations.
/// </summary>
public interface ICharacterLocationWriter
{
    ValueTask<CharacterLocationTransitionResult> TransitionAsync(
        CharacterLocationTransition transition,
        CancellationToken cancellationToken = default);
}
