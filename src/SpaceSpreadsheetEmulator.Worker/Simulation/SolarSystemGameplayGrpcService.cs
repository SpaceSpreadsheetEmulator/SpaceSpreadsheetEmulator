using Grpc.Core;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Gameplay.Characters;
using SpaceSpreadsheetEmulator.Identity.Authentication;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;
using SpaceSpreadsheetEmulator.Simulation.Runtime;
using SpaceSpreadsheetEmulator.Worker.Login;
using GameplayCharacterSummary = SpaceSpreadsheetEmulator.Gameplay.Characters.CharacterSummary;

namespace SpaceSpreadsheetEmulator.Worker.Simulation;

public sealed class SolarSystemGameplayGrpcService(
    LoginTicketRegistry tickets,
    ICharacterSelectionQuery characterSelection,
    ISolarSystemRuntime runtime) : SolarSystemGameplay.SolarSystemGameplayBase
{
    public override Task<SolarSystemMutationResponse> Undock(
        SolarSystemMutationRequest request,
        ServerCallContext context)
        => MutateAsync(request, dock: false, context.CancellationToken);

    public override Task<SolarSystemMutationResponse> Dock(
        SolarSystemMutationRequest request,
        ServerCallContext context)
        => MutateAsync(request, dock: true, context.CancellationToken);

    private async Task<SolarSystemMutationResponse> MutateAsync(
        SolarSystemMutationRequest request,
        bool dock,
        CancellationToken cancellationToken)
    {
        if (!BackplaneRequestValidator.TryValidateContext(
                request.Context,
                out NodeId gatewayId,
                out GatewaySessionId sessionId)
            || !tickets.TryResolve(request.LoginTicket.Span, gatewayId, sessionId, out AuthenticatedAccount? account))
        {
            return Failure("identity.invalid_session", "The login session is invalid or expired.");
        }

        SolarSystemRuntimeContext owner = runtime.Context;
        if (!string.Equals(request.OwnerNodeId, owner.OwnerNodeId.Value, StringComparison.Ordinal)
            || request.ExpectedEpoch != owner.Epoch.Value
            || request.SolarSystemId != owner.SolarSystemId.Value)
        {
            return Failure("simulation.stale_route", "The solar-system ownership route is stale.");
        }

        CharacterSelection selection = await characterSelection.ExecuteAsync(account!, cancellationToken);
        GameplayCharacterSummary? selected = selection.Characters.SingleOrDefault(character =>
            character.CharacterId.Value == request.CharacterId
            && character.ShipId == request.ShipId
            && character.StationId == request.StationId
            && character.SolarSystemId == request.SolarSystemId);
        if (selected is null)
        {
            return Failure("gameplay.character_mismatch", "The character does not belong to this login session.");
        }

        try
        {
            var character = new SolarCharacter(
                selected.CharacterId,
                selected.ShipId,
                new SolarSystemId(selected.SolarSystemId));
            var expectedEpoch = new SimulationEpoch(request.ExpectedEpoch);
            SolarCharacterLocation location = dock
                ? await runtime.DockAsync(character, selected.StationId, expectedEpoch, cancellationToken)
                : await runtime.UndockAsync(character, expectedEpoch, cancellationToken);
            return Success(location, owner.OwnerNodeId);
        }
        catch (InvalidOperationException error)
        {
            return Failure("simulation.mutation_rejected", error.Message);
        }
    }

    private static SolarSystemMutationResponse Success(
        SolarCharacterLocation location,
        NodeId ownerNodeId)
    {
        var response = new SolarSystemMutationResponse
        {
            OwnerNodeId = ownerNodeId.Value,
            Epoch = location.Epoch.Value,
            SolarSystemId = location.SolarSystemId.Value,
            CharacterId = location.CharacterId.Value,
            ShipId = location.ShipId,
        };
        if (location.StationId is int stationId)
        {
            response.StationId = stationId;
        }

        return response;
    }

    private static SolarSystemMutationResponse Failure(string code, string message)
        => new() { Error = new ServiceError { Code = code, Message = message } };
}
