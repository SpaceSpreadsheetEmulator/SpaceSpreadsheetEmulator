using Google.Protobuf;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V2;
using SpaceSpreadsheetEmulator.Gameplay.Characters;
using SpaceSpreadsheetEmulator.Identity.Authentication;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;
using SpaceSpreadsheetEmulator.Simulation.Runtime;
using SpaceSpreadsheetEmulator.Worker.Login;

namespace SpaceSpreadsheetEmulator.Worker.Simulation;

/// <summary>
/// Resolves a backplane request to its authenticated character and correctly fenced local runtime.
/// </summary>
internal sealed class SolarSystemRequestResolver(
    LoginTicketRegistry tickets,
    ICharacterStateReader characters,
    ISolarSystemRuntimeRegistry runtimes)
{
    public async Task<SolarSystemRequestResolution> ResolveAsync(
        GameplayRequestContext? context,
        ByteString loginTicket,
        string ownerNodeId,
        ulong expectedEpoch,
        int solarSystemId,
        long characterId,
        long shipId,
        CancellationToken cancellationToken)
    {
        if (!BackplaneRequestValidator.TryValidateContext(
                context,
                out NodeId gatewayId,
                out GatewaySessionId sessionId)
            || !tickets.TryResolve(loginTicket.Span, gatewayId, sessionId, out AuthenticatedAccount? account))
        {
            return SolarSystemRequestResolution.Failed(
                "identity.invalid_session",
                "The login session is invalid or expired.");
        }

        if (solarSystemId <= 0
            || !runtimes.TryGet(new SolarSystemId(solarSystemId), out ISolarSystemRuntime? runtime)
            || runtime is null)
        {
            return SolarSystemRequestResolution.Failed(
                "simulation.system_not_owned",
                "This Worker does not own the requested solar system.");
        }

        if (!string.Equals(ownerNodeId, runtime.Context.OwnerNodeId.Value, StringComparison.Ordinal)
            || expectedEpoch != runtime.Context.Epoch.Value)
        {
            return SolarSystemRequestResolution.Failed(
                "simulation.stale_route",
                "The solar-system ownership route is stale.");
        }

        PlayableCharacterState? selected = await characters.FindAsync(
            account!.AccountId,
            new CharacterId(characterId),
            cancellationToken);
        if (selected is not null
            && (selected.ShipId != shipId || selected.SolarSystemId != solarSystemId))
        {
            selected = null;
        }
        if (selected is null)
        {
            return SolarSystemRequestResolution.Failed(
                "gameplay.character_mismatch",
                "The character does not belong to this login session.");
        }

        return SolarSystemRequestResolution.Succeeded(
            runtime,
            selected,
            new SolarCharacter(
                selected.CharacterId,
                selected.ShipId,
                runtime.Context.SolarSystemId));
    }
}

/// <summary>
/// Contains either the validated runtime and character context or a service-level rejection.
/// </summary>
internal sealed record SolarSystemRequestResolution(
    ISolarSystemRuntime? Runtime,
    PlayableCharacterState? Character,
    SolarCharacter? SolarCharacter,
    GameplayError? Error)
{
    public static SolarSystemRequestResolution Failed(string code, string message)
        => new(null, null, null, new GameplayError { Code = code, Message = message });

    public static SolarSystemRequestResolution Succeeded(
        ISolarSystemRuntime runtime,
        PlayableCharacterState character,
        SolarCharacter solarCharacter)
        => new(runtime, character, solarCharacter, null);
}
