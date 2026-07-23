using Google.Protobuf;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Gameplay.Characters;
using SpaceSpreadsheetEmulator.Identity.Authentication;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;
using SpaceSpreadsheetEmulator.Simulation.Runtime;
using SpaceSpreadsheetEmulator.Worker.Login;
using GameplayCharacterSummary = SpaceSpreadsheetEmulator.Gameplay.Characters.CharacterSummary;

namespace SpaceSpreadsheetEmulator.Worker.Simulation;

internal sealed class SolarSystemRequestResolver(
    LoginTicketRegistry tickets,
    ICharacterSelectionQuery characterSelection,
    ISolarSystemRuntimeRegistry runtimes)
{
    public async Task<SolarSystemRequestResolution> ResolveAsync(
        RequestContext? context,
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

        CharacterSelection selection = await characterSelection.ExecuteAsync(account!, cancellationToken);
        GameplayCharacterSummary? selected = selection.Characters.SingleOrDefault(character =>
            character.CharacterId.Value == characterId
            && character.ShipId == shipId
            && character.SolarSystemId == solarSystemId);
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

internal sealed record SolarSystemRequestResolution(
    ISolarSystemRuntime? Runtime,
    GameplayCharacterSummary? Character,
    SolarCharacter? SolarCharacter,
    ServiceError? Error)
{
    public static SolarSystemRequestResolution Failed(string code, string message)
        => new(null, null, null, new ServiceError { Code = code, Message = message });

    public static SolarSystemRequestResolution Succeeded(
        ISolarSystemRuntime runtime,
        GameplayCharacterSummary character,
        SolarCharacter solarCharacter)
        => new(runtime, character, solarCharacter, null);
}
