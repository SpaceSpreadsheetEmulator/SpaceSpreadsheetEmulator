using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
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
        await WritePacketAsync(response, outbound, cancellationToken);
        if (dispatch.Notification is not null)
        {
            await WritePacketAsync(dispatch.Notification, outbound, cancellationToken);
        }

        return null;
    }

    private async Task WritePacketAsync(
        MachoPacket packet,
        ChannelWriter<OutboundFrame> outbound,
        CancellationToken cancellationToken)
        => await outbound.WriteAsync(
            new OutboundFrame(MachoPacketCodec.Encode(packet, profile), Encrypt: cipher is not null),
            cancellationToken);

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
        StartupResponse? startupResponse = Build3396210StartupProfile.CreateResponse(
            startupReplay,
            route,
            request.Arguments);
        if (startupResponse is not null)
        {
            return Result(startupResponse.Value);
        }

        return route switch
        {
            "machoNet.GetTime" => Result(new PyInteger(timeProvider.GetUtcNow().UtcDateTime.ToFileTimeUtc())),
            "charUnboundMgr.GetCharacterSelectionData" => Result(await CreateCharacterSelectionAsync(cancellationToken)),
            "charUnboundMgr.SelectCharacterID" => SelectCharacter(request),
            "ship.MachoBindObject" => await UndockAsync(request, cancellationToken),
            "beyonce.MachoBindObject" => BindSolarSystem(request),
            "bound.CmdDock" => await DockAsync(request, cancellationToken),
            _ => Unsupported(request, route),
        };
    }

    private RpcDispatchResult Unsupported(MachoRpcRequest request, string route)
    {
        LogUnsupportedRpc(logger, request.CallId, route);
        return Result(PyNull.Instance);
    }

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
        return selectedCharacter is null
            ? Result(PyNull.Instance)
            : Result(
                PyNull.Instance,
                CreateSessionNotification(previousStationId: null, selectedCharacter.StationId));
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
            cancellationToken);
        if (transition is null)
        {
            return Result(PyNull.Instance);
        }

        solarSystemBinding = $"N=solarsystem:{transition.SolarSystemId}:{transition.Epoch}";
        return Result(
            new PyText($"N=ship:{transition.ShipId}:{transition.Epoch}"),
            CreateSessionNotification(selectedCharacter.StationId, stationId: null));
    }

    private RpcDispatchResult BindSolarSystem(MachoRpcRequest request)
    {
        if (selectedCharacter is null
            || solarSystemBinding is null
            || request.Arguments.Items.Length == 0
            || !TryInteger(request.Arguments.Items[0], out long solarSystemId)
            || solarSystemId != selectedCharacter.SolarSystemId)
        {
            return Result(PyNull.Instance);
        }

        return Result(new PyText(solarSystemBinding));
    }

    private async Task<RpcDispatchResult> DockAsync(
        MachoRpcRequest request,
        CancellationToken cancellationToken)
    {
        if (selectedCharacter is null
            || solarSystemBinding is null
            || !string.Equals(request.BoundObject, solarSystemBinding, StringComparison.Ordinal)
            || !TryReadDockArguments(request.Arguments, out long stationId, out long shipId)
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

        SolarSystemTransition? transition = await solarSystemBackend.DockAsync(
            route,
            gatewaySessionId,
            loginSession!.LoginTicket,
            selectedCharacter,
            cancellationToken);
        return transition is null
            ? Result(PyNull.Instance)
            : Result(
                PyNull.Instance,
                CreateSessionNotification(previousStationId: null, transition.StationId));
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
        characterSelection = response;
        if (response is null)
        {
            return new PyTuple(new PyDictionary(), new PyDictionary(), new PyList(), new PyList());
        }

        var characters = new PyList(response.Characters.Select(character => Dictionary(
            ("characterID", new PyInteger(character.CharacterId)),
            ("characterName", new PyText(character.Name)),
            ("gender", new PyInteger(0)),
            ("typeID", new PyInteger(character.CharacterTypeId)),
            ("raceID", new PyInteger(character.RaceId)),
            ("bloodlineID", new PyInteger(character.BloodlineId)),
            ("ancestryID", new PyInteger(character.AncestryId)),
            ("corporationID", new PyInteger(character.CorporationId)),
            ("corporationName", new PyText(character.CorporationName)),
            ("stationID", new PyInteger(character.StationId)),
            ("stationName", new PyText(character.StationName)),
            ("solarSystemID", new PyInteger(character.SolarSystemId)),
            ("solarSystemName", new PyText(character.SolarSystemName)),
            ("constellationID", new PyInteger(character.ConstellationId)),
            ("regionID", new PyInteger(character.RegionId)),
            ("shipID", new PyInteger(character.ShipId)),
            ("shipTypeID", new PyInteger(character.ShipTypeId)),
            ("shipName", new PyText(character.ShipName)),
            ("balance", new PyFloat(double.Parse(character.Balance, CultureInfo.InvariantCulture))),
            ("skillPoints", new PyInteger(character.SkillPoints)),
            ("lastLogin", new PyInteger(character.LastLoginUnixMilliseconds))))
            .Cast<PyValue>()
            .ToArray());
        return new PyTuple(
            Dictionary(("userID", new PyInteger(response.AccountId))),
            new PyDictionary(),
            characters,
            new PyList());
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

    private static RpcDispatchResult Result(PyValue result, MachoPacket? notification = null)
        => new(result, notification);

    private sealed record RpcDispatchResult(PyValue Result, MachoPacket? Notification);
}
