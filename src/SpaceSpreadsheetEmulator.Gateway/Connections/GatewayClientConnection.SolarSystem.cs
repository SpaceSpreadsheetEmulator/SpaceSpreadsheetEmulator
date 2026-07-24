using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Gateway.Backplane;
using SpaceSpreadsheetEmulator.Gateway.Compatibility;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Connections;

internal sealed partial class GatewayClientConnection
{
    private async Task<RpcDispatchResult> UndockAsync(
        MachoRpcRequest request,
        CancellationToken cancellationToken)
    {
        if (selectedCharacter is null
            || !TryReadUndockArguments(request.Arguments, out long stationId, out long shipId)
            || stationId != selectedCharacter.StationId
            || shipId != selectedCharacter.ShipId)
        {
            return Result(PyNull.Instance);
        }

        SolarSystemRoute? route = await solarSystemBackend.ResolveAsync(
            selectedCharacter.SolarSystemId,
            cancellationToken);
        if (route is null)
        {
            return Result(PyNull.Instance);
        }

        SolarSystemTransition? transition = await solarSystemBackend.RequestUndockAsync(
            route,
            gatewaySessionId,
            loginSession!.LoginTicket,
            selectedCharacter,
            checked((int)stationId),
            request.CallId,
            cancellationToken);
        if (transition is null)
        {
            return Result(PyNull.Instance);
        }

        int previousStationId = selectedCharacter.StationId;
        ApplyTransition(transition);
        solarSystemBinding = $"N=solarsystem:{transition.SolarSystemId}:{transition.Epoch}";
        string shipBinding = $"N=ship:{transition.ShipId}:{transition.Epoch}";
        long clientId = checked(1_000_000L + loginSession.AccountId);
        return Result(
            new PyTuple(CreateLease(shipBinding), PyNull.Instance),
            beforeResponse:
            [
                Build3396210SessionMapper.CreateUndock(
                    gatewaySessionId,
                    ProxyNodeId,
                    clientId,
                    loginSession.AccountId,
                    previousStationId,
                    selectedCharacter,
                    request.Packet.Extensions),
            ]);
    }

    private RpcDispatchResult GetSolarSystemFormations(MachoRpcRequest request)
        => request.Arguments.Items.Length == 0
            ? CacheMethodResult("beyonce", "GetFormations", new PyList())
            : Result(PyNull.Instance);

    private RpcDispatchResult ResolveSolarSystem(MachoRpcRequest request)
        => selectedCharacter is not null
            && request.Arguments.Items.Length == 1
            && TryInteger(request.Arguments.Items[0], out long solarSystemId)
            && solarSystemId == selectedCharacter.SolarSystemId
                ? Result(new PyInteger(ProxyNodeId))
                : Result(PyNull.Instance);

    private async Task<RpcDispatchResult> BindSolarSystemAsync(
        MachoRpcRequest request,
        CancellationToken cancellationToken)
    {
        if (selectedCharacter is null
            || request.Arguments.Items.Length == 0
            || !TryInteger(request.Arguments.Items[0], out long solarSystemId)
            || solarSystemId != selectedCharacter.SolarSystemId)
        {
            return Result(PyNull.Instance);
        }

        SolarSystemRoute? route = null;
        if (solarSystemBinding is null && !selectedCharacter.HasStationId)
        {
            route = await solarSystemBackend.ResolveAsync(
                selectedCharacter.SolarSystemId,
                cancellationToken);
            if (route is null)
            {
                return Result(PyNull.Instance);
            }

            solarSystemBinding = $"N=solarsystem:{route.SolarSystemId}:{route.Epoch}";
        }

        if (solarSystemBinding is null)
        {
            return Result(PyNull.Instance);
        }

        route ??= await solarSystemBackend.ResolveAsync(
            selectedCharacter.SolarSystemId,
            cancellationToken);
        if (route is null
            || !solarSystemBinding.EndsWith($":{route.Epoch}", StringComparison.Ordinal))
        {
            return Result(PyNull.Instance);
        }

        SolarSystemRoute subscribedRoute = route;
        Func<CancellationToken, Task>? startSubscription =
            solarSubscriptionTask is null
                ? token => StartSolarSystemSubscriptionAsync(subscribedRoute, token)
                : null;
        return Result(
            new PyTuple(CreateLease(solarSystemBinding), PyNull.Instance),
            afterBatchQueued: startSubscription);
    }

    private async Task<RpcDispatchResult> SetMovementIntentAsync(
        MachoRpcRequest request,
        CancellationToken cancellationToken)
    {
        if (selectedCharacter is null
            || solarSystemBinding is null
            || !string.Equals(request.BoundObject, solarSystemBinding, StringComparison.Ordinal)
            || request.Arguments.Items.Length != 3
            || !TryFloat(request.Arguments.Items[0], out double directionX)
            || !TryFloat(request.Arguments.Items[1], out double directionY)
            || !TryFloat(request.Arguments.Items[2], out double directionZ))
        {
            return Result(PyNull.Instance);
        }

        SolarSystemRoute? route = await solarSystemBackend.ResolveAsync(
            selectedCharacter.SolarSystemId,
            cancellationToken);
        if (route is null)
        {
            return Result(PyNull.Instance);
        }

        _ = await solarSystemBackend.SetMovementIntentAsync(
            route,
            gatewaySessionId,
            loginSession!.LoginTicket,
            selectedCharacter,
            new SolarSystemMovementIntent(
                directionX,
                directionY,
                directionZ,
                RequestedSpeed: Math.Sqrt(
                    checked((directionX * directionX)
                        + (directionY * directionY)
                        + (directionZ * directionZ)))),
            cancellationToken);
        return Result(PyNull.Instance);
    }

    private async Task<RpcDispatchResult> DockAsync(
        MachoRpcRequest request,
        CancellationToken cancellationToken)
    {
        if (selectedCharacter is null
            || solarSystemBinding is null
            || !string.Equals(request.BoundObject, solarSystemBinding, StringComparison.Ordinal)
            || !TryReadDockArguments(request.Arguments, out long stationId, out long shipId)
            || selectedCharacter.HasStationId
            || shipId != selectedCharacter.ShipId)
        {
            return Result(PyNull.Instance);
        }

        SolarSystemRoute? route = await solarSystemBackend.ResolveAsync(
            selectedCharacter.SolarSystemId,
            cancellationToken);
        if (route is null)
        {
            return Result(PyNull.Instance);
        }

        await SetSolarOutputPausedAsync(paused: true, cancellationToken);
        SolarSystemTransition? transition = await solarSystemBackend.RequestDockAsync(
            route,
            gatewaySessionId,
            loginSession!.LoginTicket,
            selectedCharacter,
            checked((int)stationId),
            request.CallId,
            cancellationToken);
        if (transition?.StationId is not int dockedStationId)
        {
            await SetSolarOutputPausedAsync(paused: false, cancellationToken);
            return Result(PyNull.Instance);
        }

        string binding = solarSystemBinding;
        long clientId = checked(1_000_000L + loginSession.AccountId);
        ApplyTransition(transition);
        solarSystemBinding = null;
        await StopSolarSystemSubscriptionAsync(cancel: true);
        return Result(
            PyNull.Instance,
            afterResponse:
            [
                Build3396210SpaceSessionMapper.CreateDockingAccepted(
                    ProxyNodeId,
                    loginSession.AccountId,
                    dockedStationId),
                Build3396210SpaceSessionMapper.CreateStop(
                    ProxyNodeId,
                    loginSession.AccountId,
                    solarLastTick,
                    transition.ShipId),
                Build3396210SpaceSessionMapper.CreateObjectDisconnect(
                    ProxyNodeId,
                    loginSession.AccountId,
                    binding,
                    clientId),
                Build3396210SessionMapper.CreateDock(
                    gatewaySessionId,
                    ProxyNodeId,
                    clientId,
                    loginSession.AccountId,
                    dockedStationId,
                    selectedCharacter,
                    request.Packet.Extensions),
                Build3396210SpaceSessionMapper.CreateDockingFinished(
                    ProxyNodeId,
                    loginSession.AccountId,
                    dockedStationId),
            ]);
    }

    private void ApplyTransition(SolarSystemTransition transition)
    {
        CharacterSummary updated = selectedCharacter!.Clone();
        if (transition.StationId is int stationId)
        {
            updated.StationId = stationId;
        }
        else
        {
            updated.ClearStationId();
        }

        selectedCharacter = updated;
    }
}
