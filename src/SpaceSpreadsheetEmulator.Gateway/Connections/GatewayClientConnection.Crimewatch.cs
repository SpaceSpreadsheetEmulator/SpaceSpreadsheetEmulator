using SpaceSpreadsheetEmulator.Gateway.Compatibility;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Connections;

internal sealed partial class GatewayClientConnection
{
    private string? crimewatchBinding;
    private string? corporationRegistryBinding;

    private RpcDispatchResult ResolveCrimewatchLocation(MachoRpcRequest request)
        => HasDockedInventoryObject(request.Arguments)
            ? Result(new PyInteger(ProxyNodeId))
            : Result(PyNull.Instance);

    private RpcDispatchResult BindCrimewatchLocation(MachoRpcRequest request)
    {
        if (request.Arguments.Items.Length != 2
            || !HasDockedInventoryObject(new PyTuple(request.Arguments.Items[0]))
            || Unwrap(request.Arguments.Items[1]) is not PyTuple { Items.Length: 3 } nested
            || Unwrap(nested.Items[1]) is not PyTuple { Items.Length: 0 }
            || Unwrap(nested.Items[2]) is not PyDictionary { Entries.Length: 0 })
        {
            return Result(PyNull.Instance);
        }

        crimewatchBinding ??= CreateCrimewatchBinding(request.CallId);
        return ReadText(nested.Items[0]) switch
        {
            "GetClientStates" => Result(new PyTuple(
                CreateLease(crimewatchBinding),
                Build3396210CrimewatchMapper.CreateClientStates())),
            "GetMySecurityStatus" => Result(new PyTuple(
                CreateLease(crimewatchBinding),
                new PyFloat(0))),
            _ => Result(PyNull.Instance),
        };
    }

    private RpcDispatchResult ResolveCorporationRegistry(MachoRpcRequest request)
        => HasSelectedCorporation(request.Arguments)
            ? Result(new PyInteger(ProxyNodeId))
            : Result(PyNull.Instance);

    private RpcDispatchResult BindCorporationRegistry(MachoRpcRequest request)
    {
        if (request.Arguments.Items.Length != 2
            || !HasSelectedCorporation(new PyTuple(request.Arguments.Items[0]))
            || Unwrap(request.Arguments.Items[1]) is not PyNull)
        {
            return Result(PyNull.Instance);
        }

        corporationRegistryBinding = CreateCorporationRegistryBinding(request.CallId);
        return Result(new PyTuple(
            CreateLease(corporationRegistryBinding),
            PyNull.Instance));
    }

    private RpcDispatchResult GetCorporationAggressionSettings(MachoRpcRequest request)
    {
        if (request.BoundObject != corporationRegistryBinding
            || request.Arguments.Items.Length != 0)
        {
            return Result(PyNull.Instance);
        }

        return Result(Build3396210CrimewatchMapper.CreateNpcCorporationAggressionSettings());
    }

    private RpcDispatchResult GetCorporationMembers(MachoRpcRequest request)
    {
        if (request.BoundObject != corporationRegistryBinding
            || request.Arguments.Items.Length != 0
            || selectedCharacter is null)
        {
            return Result(PyNull.Instance);
        }

        return Result(Build3396210OwnerMapper.CreateCorporationMembers(selectedCharacter));
    }

    private bool HasSelectedCorporation(PyTuple arguments)
        => selectedCharacter is { CorporationId: > 0 } character
            && arguments.Items.Length == 1
            && TryInteger(arguments.Items[0], out long corporationId)
            && corporationId == character.CorporationId;

    private static string CreateCrimewatchBinding(long callId)
        => $"N={ProxyNodeId}:{checked((callId * 2) + 30_000)}";

    private static string CreateCorporationRegistryBinding(long callId)
        => $"N={ProxyNodeId}:{checked((callId * 2) + 40_000)}";
}
