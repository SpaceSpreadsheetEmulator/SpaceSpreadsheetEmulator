using System.Numerics;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Connections;

internal sealed partial class GatewayClientConnection
{
    private string? shipAccessBinding;

    private RpcDispatchResult ResolveShipAccess(MachoRpcRequest request)
        => HasSelectedLocationObject(request.Arguments)
            ? Result(new PyInteger(ProxyNodeId))
            : Result(PyNull.Instance);

    private async Task<RpcDispatchResult> BindShipAccessAsync(
        MachoRpcRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Arguments.Items.Length != 2
            || !HasSelectedLocationObject(new PyTuple(request.Arguments.Items[0]))
            || Unwrap(request.Arguments.Items[1]) is not PyTuple { Items.Length: 3 } nested)
        {
            return Result(PyNull.Instance);
        }

        string? method = ReadText(nested.Items[0]);
        if (string.Equals(method, "Undock", StringComparison.Ordinal))
        {
            return await UndockAsync(request, cancellationToken);
        }

        if (!string.Equals(method, "GetDirtTimestamp", StringComparison.Ordinal)
            || selectedCharacter is null
            || Unwrap(nested.Items[1]) is not PyTuple { Items.Length: 1 } arguments
            || !TryInteger(arguments.Items[0], out long shipId)
            || shipId != selectedCharacter.ShipId
            || Unwrap(nested.Items[2]) is not PyDictionary { Entries.Length: 0 })
        {
            return Result(PyNull.Instance);
        }

        shipAccessBinding ??= CreateShipAccessBinding(request.CallId);
        long dirtTimestamp = timeProvider.GetUtcNow().UtcDateTime.ToFileTimeUtc();
        return Result(new PyTuple(
            CreateLease(shipAccessBinding),
            new PyBigInteger(new BigInteger(dirtTimestamp))));
    }

    private static string CreateShipAccessBinding(long callId)
        => $"N={ProxyNodeId}:{checked((callId * 2) + 60_000)}";
}
