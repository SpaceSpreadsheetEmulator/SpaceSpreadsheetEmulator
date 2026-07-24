using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Gateway.Backplane;
using SpaceSpreadsheetEmulator.Gateway.Compatibility;
using SpaceSpreadsheetEmulator.Protocol;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Connections;

internal sealed partial class GatewayClientConnection
{
    private async Task<ProtocolError?> ProcessRpcAsync(
        PyValue value,
        GatewayOutboundSequencer outbound,
        CancellationToken cancellationToken)
    {
        byte[] marshal = BlueMarshalCodec.Encode(value, profile, EncodingMode.PreserveWireForm);
        DecodeResult<MachoPacket> packet = MachoPacketCodec.Decode(new ReadOnlySequence<byte>(marshal), profile);
        if (!packet.IsSuccess)
        {
            return packet.Error;
        }

        if (packet.Value!.NumericType == 20)
        {
            await outbound.EnqueueAsync(
                [CreatePacketFrame(CreatePingResponse(packet.Value))],
                cancellationToken);
            return null;
        }

        if (packet.Value.NumericType == 12)
        {
            DecodeResult<MachoClientNotification> notification =
                MachoNotificationCodec.DecodeClientNotification(packet.Value, profile);
            if (!notification.IsSuccess)
            {
                return notification.Error;
            }

            LogClientNotification(logger, notification.Value!.Method);
            return null;
        }

        DecodeResult<MachoRpcRequest> request = MachoRpcCodec.DecodeRequest(packet.Value, profile);
        if (!request.IsSuccess)
        {
            return request.Error;
        }

        LogRpcRequest(logger, request.Value!.CallId, request.Value.Service, request.Value.Method, request.Value.BoundObject is not null);
        RpcDispatchResult dispatch = await DispatchAsync(request.Value!, cancellationToken);
        long clientId = checked(1_000_000L + loginSession!.AccountId);
        MachoPacket response = MachoRpcCodec.CreateResponse(
            request.Value!,
            clientId,
            loginSession.AccountId,
            dispatch.Result,
            profile);
        OutboundFrame[] batch =
        [
            .. dispatch.BeforeResponse.Select(packetToSend => CreatePacketFrame(packetToSend)),
            CreatePacketFrame(response, dispatch.CompressResponse),
            .. dispatch.AfterResponse.Select(packetToSend => CreatePacketFrame(packetToSend)),
        ];
        await outbound.EnqueueAsync(batch, cancellationToken);
        if (dispatch.AfterBatchQueued is not null)
        {
            await dispatch.AfterBatchQueued(cancellationToken);
        }

        return null;
    }

    private OutboundFrame CreatePacketFrame(
        MachoPacket packet,
        bool compress = false)
    {
        byte[] payload = MachoPacketCodec.Encode(packet, profile);
        if (compress)
        {
            payload = compression.Compress(payload);
        }

        return new OutboundFrame(payload, Encrypt: cipher is not null);
    }

    private MachoPacket CreatePingResponse(MachoPacket request)
    {
        long now = timeProvider.GetUtcNow().UtcDateTime.ToFileTimeUtc();
        var timings = new List<PyValue>();
        if (request.Payload is PyTuple { Items.Length: >= 1 } payload
            && Unwrap(payload.Items[0]) is PyList clientTimings)
        {
            timings.AddRange(clientTimings.Items);
        }

        (string Name, bool UseBuffer)[] phases =
        [
            ("proxy::handle_message", true),
            ("proxy::writing", true),
            ("server::handle_message", false),
            ("server::turnaround", false),
            ("proxy::handle_message", true),
            ("proxy::writing", true),
        ];
        timings.AddRange(phases.Select(phase => new PyTuple(
            new PyInteger(now - 20),
            new PyInteger(now),
            phase.UseBuffer
                ? new PyBuffer(Encoding.UTF8.GetBytes(phase.Name))
                : new PyText(phase.Name))));
        long clientId = checked(1_000_000L + loginSession!.AccountId);
        long? callId = (request.Source as MachoClientAddress)?.CallId;
        ImmutableArray<PyValue> extensions =
        [
            new PyDictionary(),
            .. request.Extensions.Skip(1),
        ];
        return new MachoPacket(
            "carbon.common.script.net.machoNetPacket.PingRsp",
            21,
            request.Destination,
            new MachoClientAddress(clientId, callId),
            loginSession.AccountId,
            new PyTuple(new PyList(timings.ToArray())),
            extensions);
    }

    private async Task<RpcDispatchResult> DispatchAsync(
        MachoRpcRequest request,
        CancellationToken cancellationToken)
    {
        string route = request.BoundObject is null
            ? $"{request.Service}.{request.Method}"
            : $"bound.{request.Method}";
        RpcDispatchResult? dynamicResponse = route switch
        {
            "machoNet.GetTime" => Result(new PyInteger(timeProvider.GetUtcNow().UtcDateTime.ToFileTimeUtc())),
            "objectCaching.GetCachableObject" => GetCachableObject(request),
            "charUnboundMgr.GetCharacterSelectionData" => Result(await CreateCharacterSelectionAsync(cancellationToken)),
            "config.GetMultiOwnersEx" => Result(Build3396210OwnerMapper.CreateOwners(
                characterSelection,
                ReadRequestedIds(request.Arguments))),
            "config.GetMultiCorpTickerNamesEx" => Result(Build3396210OwnerMapper.CreateCorporationTickers(
                characterSelection,
                ReadRequestedIds(request.Arguments))),
            "charUnboundMgr.SelectCharacterID" => SelectCharacter(request),
            "stationSvc.GetStationItemBits" => GetStationItemBits(request),
            "map.GetStationInfo" => await GetStationInfoAsync(request, cancellationToken),
            "home_station.get_home_station" => await GetHomeStationAsync(request, cancellationToken),
            "account.GetCashBalance" => GetCashBalance(request),
            "dogmaIM.MachoResolveObject" => ResolveDogmaLocation(request),
            "dogmaIM.MachoBindObject" => BindDogmaLocation(request),
            "invbroker.MachoResolveObject" => ResolveInventoryBroker(request),
            "invbroker.MachoBindObject" => BindInventoryBroker(request),
            "crimewatch.MachoResolveObject" => ResolveCrimewatchLocation(request),
            "crimewatch.MachoBindObject" => BindCrimewatchLocation(request),
            "corpRegistry.MachoResolveObject" => ResolveCorporationRegistry(request),
            "corpRegistry.MachoBindObject" => BindCorporationRegistry(request),
            "standingMgr.GetNPCNPCStandings" => GetNpcNpcStandings(request),
            "standingMgr.GetCharStandings" => GetCharacterStandings(request),
            "beyonce.GetFormations" => GetSolarSystemFormations(request),
            "beyonce.MachoResolveObject" => ResolveSolarSystem(request),
            "skillMgr2.GetMySkillHandler" => GetSkillHandler(request),
            "skillMgr2.MachoBindObject" => BindSkillHandler(request),
            "agentMgr.GetAgents" => await GetAgentsAsync(request, cancellationToken),
            "ship.MachoResolveObject" => ResolveShipAccess(request),
            "ship.MachoBindObject" => await BindShipAccessAsync(request, cancellationToken),
            "bound.GetInventoryFromId" => GetInventoryFromId(request),
            "bound.GetSelfInvItem" => GetSelfInventoryItem(request),
            "bound.List" => ListInventory(request),
            "bound.GetAvailableTurretSlots" => GetAvailableTurretSlots(request),
            "bound.GetTargets" => GetDogmaTargetCollection(request),
            "bound.GetTargeters" => GetDogmaTargetCollection(request),
            "bound.GetBoosters" => GetSkillBoosters(request),
            "bound.GetAggressionSettings" => GetCorporationAggressionSettings(request),
            "bound.GetEveOwners" => GetCorporationMembers(request),
            "beyonce.MachoBindObject" => await BindSolarSystemAsync(request, cancellationToken),
            "bound.CmdGotoDirection" => await SetDirectionalMovementIntentAsync(request, cancellationToken),
            "bound.CmdStop" => await StopMovementAsync(request, cancellationToken),
            "bound.CmdFollowBall" => await FollowAsync(request, cancellationToken),
            "bound.CmdOrbit" => await OrbitAsync(request, cancellationToken),
            "bound.CmdGotoPoint" => await GoToPointAsync(request, cancellationToken),
            "bound.CmdDock" => await DockAsync(request, cancellationToken),
            _ => null,
        };
        if (dynamicResponse is not null)
        {
            return dynamicResponse;
        }

        RpcDispatchResult? characterBootstrap = GetCharacterBootstrapResponse(route, request);
        if (characterBootstrap is not null)
        {
            return characterBootstrap;
        }

        StartupResponse? startupResponse = Build3396210StartupProfile.CreateResponse(
            startupReplay,
            route,
            request.Arguments);
        return startupResponse is null
            ? Unsupported(request, route)
            : Result(startupResponse.Value);
    }

    private static RpcDispatchResult GetNpcNpcStandings(MachoRpcRequest request)
        => request.Arguments.Items.Length == 0
            ? Result(Build3396210StandingMapper.CreateEmptyNpcStandings())
            : Result(PyNull.Instance);

    private static RpcDispatchResult GetCharacterStandings(MachoRpcRequest request)
        => request.Arguments.Items.Length == 0
            ? Result(Build3396210StandingMapper.CreateEmptyCharacterStandings())
            : Result(PyNull.Instance);

    private RpcDispatchResult Unsupported(MachoRpcRequest request, string route)
    {
        LogUnsupportedRpc(logger, request.CallId, route);
        return Result(PyNull.Instance);
    }

    [LoggerMessage(
        EventId = 113,
        Level = LogLevel.Information,
        Message = "Received client notification {Method}")]
    private static partial void LogClientNotification(ILogger logger, string method);

    private RpcDispatchResult SelectCharacter(MachoRpcRequest request)
    {
        if (request.Arguments.Items.Length == 0
            || !TryInteger(request.Arguments.Items[0], out long characterId)
            || characterSelection is null)
        {
            return Result(PyNull.Instance);
        }

        selectedCharacter = characterSelection.Characters.SingleOrDefault(
            character => character.CharacterId == characterId);
        inventoryBrokerBinding = null;
        inventoryBindings.Clear();
        characterManagerBinding = null;
        methodCache.Clear();
        crimewatchBinding = null;
        dogmaBinding = null;
        corporationRegistryBinding = null;
        skillHandlerBinding = null;
        shipAccessBinding = null;
        warRegistryBinding = null;
        return selectedCharacter is null
            ? Result(PyNull.Instance)
            : Result(
                PyNull.Instance,
                beforeResponse:
                [
                    Build3396210SessionMapper.CreateCharacterSelection(
                        gatewaySessionId,
                        ProxyNodeId,
                        checked(1_000_000L + loginSession!.AccountId),
                        loginSession.AccountId,
                        selectedCharacter,
                        request.Packet.Extensions),
                ]);
    }

    private RpcDispatchResult GetStationItemBits(MachoRpcRequest request)
    {
        if (request.Arguments.Items.Length != 0
            || selectedCharacter is not { HasStationId: true } character
            || character.StationOwnerId <= 0
            || character.StationOperationId <= 0
            || character.StationTypeId <= 0)
        {
            return Result(PyNull.Instance);
        }

        return Result(new PyTuple(
            new PyInteger(character.StationOwnerId),
            new PyInteger(character.StationId),
            new PyInteger(character.StationOperationId),
            new PyInteger(character.StationTypeId)));
    }

    private async Task<RpcDispatchResult> GetStationInfoAsync(
        MachoRpcRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Arguments.Items.Length != 0)
        {
            return Result(PyNull.Instance);
        }

        StationCatalogResponse? catalog = await loginBackend.GetStationCatalogAsync(
            gatewaySessionId,
            loginSession!.LoginTicket,
            cancellationToken);
        PyValue? stationInfo = catalog is null
            ? null
            : Build3396210StationMapper.CreateStationInfo(catalog.Stations);
        return stationInfo is null
            ? Result(PyNull.Instance)
            : CacheMethodResult("map", "GetStationInfo", stationInfo);
    }

    private async Task<RpcDispatchResult> GetHomeStationAsync(
        MachoRpcRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Arguments.Items.Length != 0
            || selectedCharacter is not { HeadquartersStationId: > 0 } character)
        {
            return Result(PyNull.Instance);
        }

        StationCatalogResponse? catalog = await loginBackend.GetStationCatalogAsync(
            gatewaySessionId,
            loginSession!.LoginTicket,
            cancellationToken);
        StationSummary? homeStation = catalog?.Stations.SingleOrDefault(
            station => station.StationId == character.HeadquartersStationId);
        return homeStation is null
            ? Result(PyNull.Instance)
            : Result(Build3396210StationBootstrapMapper.CreateHomeStation(homeStation));
    }

    private RpcDispatchResult GetCashBalance(MachoRpcRequest request)
        => request.Arguments.Items.Length == 0
            && selectedCharacter is { } character
                ? Result(Build3396210StationBootstrapMapper.CreateCashBalance(character))
                : Result(PyNull.Instance);

    private MachoPacket CreateInitialSessionNotification(long clientId)
    {
        BackendLoginSession session = loginSession
            ?? throw new InvalidOperationException("Login must complete before the initial session notification.");
        const long unauthenticatedRole = 4_611_686_018_427_387_904;
        return new MachoPacket(
            "carbon.common.script.net.machoNetPacket.SessionChangeNotification",
            16,
            new MachoNodeAddress(ProxyNodeId, null),
            new MachoClientAddress(clientId, null),
            session.AccountId,
            new PyTuple(
                new PyInteger(checked((long)gatewaySessionId)),
                new PyTuple(
                    new PyInteger(0),
                    Dictionary(
                        ("countryCode", Change(PyNull.Instance, new PyText(session.CountryCode))),
                        ("languageID", Change(PyNull.Instance, new PyText(session.LanguageId))),
                        ("userid", Change(PyNull.Instance, new PyInteger(session.AccountId))),
                        ("userType", Change(PyNull.Instance, new PyInteger(21))),
                        ("role", Change(new PyInteger(unauthenticatedRole), new PyInteger(session.Role))),
                        ("address", Change(
                            PyNull.Instance,
                            new PyText(client.Client.RemoteEndPoint?.ToString() ?? "loopback"))))),
                new PyList(new PyInteger(-1))),
            Enumerable.Repeat<PyValue>(PyNull.Instance, 9).ToImmutableArray());
    }

    private async Task<PyValue> CreateCharacterSelectionAsync(CancellationToken cancellationToken)
    {
        CharacterSelectionResponse? response = await loginBackend.GetCharacterSelectionAsync(
            gatewaySessionId,
            loginSession!.LoginTicket,
            cancellationToken);
        PyList result = Build3396210CharacterSelectionMapper.Create(
            loginSession,
            response,
            authenticatedAt ?? timeProvider.GetUtcNow(),
            out CharacterSelectionResponse? selectable);
        characterSelection = selectable;
        if (selectable is null && response is not null)
        {
            LogInvalidCharacterSelection(logger);
        }
        else
        {
            LogCharacterSelectionBuilt(logger, selectable?.Characters.Count ?? 0);
        }

        return result;
    }

    private static bool TryReadUndockArguments(
        PyTuple arguments,
        out long stationId,
        out long shipId)
    {
        stationId = 0;
        shipId = 0;
        return arguments.Items.Length >= 2
            && arguments.Items[0] is PyTuple { Items.Length: >= 1 } bind
            && TryInteger(bind.Items[0], out stationId)
            && arguments.Items[1] is PyTuple { Items.Length: >= 2 } nested
            && string.Equals(ReadText(nested.Items[0]), "Undock", StringComparison.Ordinal)
            && nested.Items[1] is PyTuple { Items.Length: >= 1 } action
            && TryInteger(action.Items[0], out shipId);
    }

    private static IReadOnlySet<long> ReadRequestedIds(PyTuple arguments)
    {
        if (arguments.Items.Length == 0 || Unwrap(arguments.Items[0]) is not PyList requested)
        {
            return new HashSet<long>();
        }

        return requested.Items
            .Select(Unwrap)
            .OfType<PyInteger>()
            .Select(value => value.Value)
            .ToHashSet();
    }

    private static bool TryReadDockArguments(
        PyTuple arguments,
        out long stationId,
        out long shipId)
    {
        stationId = 0;
        shipId = 0;
        return arguments.Items.Length >= 2
            && TryInteger(arguments.Items[0], out stationId)
            && TryInteger(arguments.Items[1], out shipId);
    }

    private static bool TryInteger(PyValue value, out long result)
    {
        value = Unwrap(value);
        if (value is PyInteger integer)
        {
            result = integer.Value;
            return true;
        }

        if (value is PyBigInteger bigInteger
            && bigInteger.Value >= long.MinValue
            && bigInteger.Value <= long.MaxValue)
        {
            result = (long)bigInteger.Value;
            return true;
        }

        result = 0;
        return false;
    }

    private static bool TryFloat(PyValue value, out double result)
    {
        value = Unwrap(value);
        if (value is PyFloat number && double.IsFinite(number.Value))
        {
            result = number.Value;
            return true;
        }

        result = 0;
        return false;
    }

    private static bool TryNumber(PyValue value, out double result)
    {
        if (TryFloat(value, out result))
        {
            return true;
        }

        if (TryInteger(value, out long integer))
        {
            result = integer;
            return true;
        }

        result = 0;
        return false;
    }

    private static PyValue Unwrap(PyValue value)
        => value is PySavedValueReference reference ? reference.Value : value;

    [LoggerMessage(
        EventId = 110,
        Level = LogLevel.Information,
        Message = "Received RPC call {CallId}: service {Service}, method {Method}, bound {IsBound}")]
    private static partial void LogRpcRequest(
        ILogger logger,
        long callId,
        string? service,
        string method,
        bool isBound);

    [LoggerMessage(
        EventId = 111,
        Level = LogLevel.Information,
        Message = "Returning null for unsupported RPC call {CallId}: {Route}")]
    private static partial void LogUnsupportedRpc(
        ILogger logger,
        long callId,
        string route);

    [LoggerMessage(
        EventId = 112,
        Level = LogLevel.Information,
        Message = "Built Worker-backed character selection with {CharacterCount} characters")]
    private static partial void LogCharacterSelectionBuilt(ILogger logger, int characterCount);

    [LoggerMessage(
        EventId = 113,
        Level = LogLevel.Warning,
        Message = "Worker returned invalid character selection data; returning no selectable characters")]
    private static partial void LogInvalidCharacterSelection(ILogger logger);

    private static string? ReadText(PyValue value)
        => Unwrap(value) switch
        {
            PyText text => text.Value,
            PyToken token => token.Value,
            PyStringTableReference tableReference => tableReference.Value,
            PyBuffer buffer => Encoding.UTF8.GetString(buffer.Value.AsSpan()),
            _ => null,
        };

    private static PyTuple Change(PyValue previous, PyValue current)
        => new(previous, current);

    private static RpcDispatchResult Result(
        PyValue result,
        ImmutableArray<MachoPacket> beforeResponse = default,
        ImmutableArray<MachoPacket> afterResponse = default,
        bool compressResponse = false,
        Func<CancellationToken, Task>? afterBatchQueued = null)
        => new(
            result,
            beforeResponse.IsDefault ? [] : beforeResponse,
            afterResponse.IsDefault ? [] : afterResponse,
            compressResponse,
            afterBatchQueued);

    private sealed record RpcDispatchResult(
        PyValue Result,
        ImmutableArray<MachoPacket> BeforeResponse,
        ImmutableArray<MachoPacket> AfterResponse,
        bool CompressResponse,
        Func<CancellationToken, Task>? AfterBatchQueued);
}
