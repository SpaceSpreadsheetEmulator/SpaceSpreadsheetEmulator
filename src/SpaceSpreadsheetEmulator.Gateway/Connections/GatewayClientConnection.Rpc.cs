using System.Buffers;
using System.Collections.Immutable;
using System.Numerics;
using System.Text;
using System.Threading.Channels;
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
        ChannelWriter<OutboundFrame> outbound,
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
            await WritePacketAsync(CreatePingResponse(packet.Value), outbound, cancellationToken);
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
        foreach (MachoPacket beforeResponse in dispatch.BeforeResponse)
        {
            await WritePacketAsync(beforeResponse, outbound, cancellationToken);
        }

        await WritePacketAsync(
            response,
            outbound,
            cancellationToken,
            compress: dispatch.CompressResponse);
        foreach (MachoPacket afterResponse in dispatch.AfterResponse)
        {
            await WritePacketAsync(afterResponse, outbound, cancellationToken);
        }

        return null;
    }

    private async Task WritePacketAsync(
        MachoPacket packet,
        ChannelWriter<OutboundFrame> outbound,
        CancellationToken cancellationToken,
        bool compress = false)
    {
        byte[] payload = MachoPacketCodec.Encode(packet, profile);
        if (compress)
        {
            payload = compression.Compress(payload);
        }

        await outbound.WriteAsync(
            new OutboundFrame(payload, Encrypt: cipher is not null),
            cancellationToken);
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
            "skillMgr2.GetMySkillHandler" => GetSkillHandler(request),
            "skillMgr2.MachoBindObject" => BindSkillHandler(request),
            "agentMgr.GetAgents" => await GetAgentsAsync(request, cancellationToken),
            "ship.MachoResolveObject" => ResolveShipAccess(request),
            "ship.MachoBindObject" => await BindShipAccessAsync(request, cancellationToken),
            "bound.GetInventoryFromId" => GetInventoryFromId(request),
            "bound.GetSelfInvItem" => GetSelfInventoryItem(request),
            "bound.List" => ListInventory(request),
            "bound.GetAvailableTurretSlots" => GetAvailableTurretSlots(request),
            "bound.GetBoosters" => GetSkillBoosters(request),
            "bound.GetAggressionSettings" => GetCorporationAggressionSettings(request),
            "bound.GetEveOwners" => GetCorporationMembers(request),
            "beyonce.MachoBindObject" => await BindSolarSystemAsync(request, cancellationToken),
            "bound.CmdDock" => await DockAsync(request, cancellationToken),
            _ => null,
        };
        if (dynamicResponse is not null)
        {
            return dynamicResponse;
        }

        RpcDispatchResult? stationBootstrap = GetStationBootstrapResponse(route, request);
        if (stationBootstrap is not null)
        {
            return stationBootstrap;
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

        SolarSystemTransition? transition = await solarSystemBackend.UndockAsync(
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

        int? previousStationId = selectedCharacter.StationId;
        ApplyTransition(transition);
        solarSystemBinding = $"N=solarsystem:{transition.SolarSystemId}:{transition.Epoch}";
        return Result(
            new PyText($"N=ship:{transition.ShipId}:{transition.Epoch}"),
            afterResponse:
            [
                CreateSessionNotification(previousStationId, stationId: null),
            ]);
    }

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

        if (solarSystemBinding is null && !selectedCharacter.HasStationId)
        {
            SolarSystemRoute? route = await solarSystemBackend.ResolveAsync(
                selectedCharacter.SolarSystemId,
                cancellationToken);
            if (route is null)
            {
                return Result(PyNull.Instance);
            }

            solarSystemBinding = $"N=solarsystem:{route.SolarSystemId}:{route.Epoch}";
        }

        return solarSystemBinding is null
            ? Result(PyNull.Instance)
            : Result(new PyText(solarSystemBinding));
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

        SolarSystemTransition? transition = await solarSystemBackend.DockAsync(
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

        ApplyTransition(transition);
        solarSystemBinding = null;
        return Result(
            PyNull.Instance,
            afterResponse:
            [
                CreateSessionNotification(previousStationId: null, transition.StationId),
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

    private MachoPacket CreateSessionNotification(int? previousStationId, int? stationId)
    {
        CharacterSummary character = selectedCharacter
            ?? throw new InvalidOperationException("A character must be selected before emitting a session notification.");
        long clientId = checked(1_000_000L + loginSession!.AccountId);
        return new MachoPacket(
            "carbon.common.script.net.machoNetPacket.Notification",
            12,
            new MachoNodeAddress(ProxyNodeId, null),
            new MachoClientAddress(clientId, null),
            loginSession.AccountId,
            Dictionary(
                ("charid", Change(null, character.CharacterId)),
                ("stationid", Change(previousStationId, stationId)),
                ("shipid", Change(null, character.ShipId)),
                ("solarsystemid", Change(null, character.SolarSystemId))),
            DefaultPacketExtensions());
    }

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
        => value switch
        {
            PyText text => text.Value,
            PyToken token => token.Value,
            PyBuffer buffer => Encoding.UTF8.GetString(buffer.Value.AsSpan()),
            _ => null,
        };

    private static PyTuple Change(long? previous, long? current)
        => new(NumberOrNull(previous), NumberOrNull(current));

    private static PyTuple Change(PyValue previous, PyValue current)
        => new(previous, current);

    private static PyValue NumberOrNull(long? value)
        => value is long number ? new PyInteger(number) : PyNull.Instance;

    private static ImmutableArray<PyValue> DefaultPacketExtensions()
        =>
        [
            new PyDictionary(),
            PyNull.Instance,
            PyNull.Instance,
            PyNull.Instance,
            PyNull.Instance,
            new PyBoolean(false),
            new PyInteger(0),
            new PyInteger(1000),
            PyNull.Instance,
        ];

    private static RpcDispatchResult Result(
        PyValue result,
        ImmutableArray<MachoPacket> beforeResponse = default,
        ImmutableArray<MachoPacket> afterResponse = default,
        bool compressResponse = false)
        => new(
            result,
            beforeResponse.IsDefault ? [] : beforeResponse,
            afterResponse.IsDefault ? [] : afterResponse,
            compressResponse);

    private sealed record RpcDispatchResult(
        PyValue Result,
        ImmutableArray<MachoPacket> BeforeResponse,
        ImmutableArray<MachoPacket> AfterResponse,
        bool CompressResponse);
}
