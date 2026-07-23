using SpaceSpreadsheetEmulator.Gateway.Compatibility;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Connections;

internal sealed partial class GatewayClientConnection
{
    private RpcDispatchResult ResolveDogmaLocation(MachoRpcRequest request)
        => HasDockedInventoryObject(request.Arguments)
            ? Result(new PyInteger(ProxyNodeId))
            : Result(PyNull.Instance);

    private RpcDispatchResult BindDogmaLocation(MachoRpcRequest request)
    {
        if (selectedCharacter is not { HasStationId: true } character
            || request.Arguments.Items.Length != 2
            || !HasDockedInventoryObject(new PyTuple(request.Arguments.Items[0]))
            || Unwrap(request.Arguments.Items[1]) is not PyTuple { Items.Length: 3 } nested
            || !string.Equals(ReadText(nested.Items[0]), "GetAllInfo", StringComparison.Ordinal)
            || Unwrap(nested.Items[1]) is not PyTuple { Items.Length: 3 } arguments
            || Unwrap(arguments.Items[0]) is not PyBoolean { Value: true }
            || Unwrap(arguments.Items[1]) is not PyBoolean { Value: true }
            || Unwrap(arguments.Items[2]) is not PyNull)
        {
            return Result(PyNull.Instance);
        }

        string binding = CreateDogmaBinding(request.CallId);
        return Result(new PyTuple(
            CreateLease(binding),
            Build3396210DogmaMapper.CreateAllInfo(character, timeProvider.GetUtcNow())));
    }

    private static string CreateDogmaBinding(long callId)
        => $"N={ProxyNodeId}:{checked((callId * 2) + 20_000)}";
}
