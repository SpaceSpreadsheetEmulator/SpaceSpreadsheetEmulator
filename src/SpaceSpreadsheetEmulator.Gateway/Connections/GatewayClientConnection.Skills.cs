using SpaceSpreadsheetEmulator.Gateway.Compatibility;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Connections;

internal sealed partial class GatewayClientConnection
{
    private string? skillHandlerBinding;

    private RpcDispatchResult GetSkillHandler(MachoRpcRequest request)
        => request.Arguments.Items.Length == 0 && selectedCharacter is not null
            ? Result(Build3396210MonikerMapper.CreateSkillHandler(
                ProxyNodeId,
                selectedCharacter.CharacterId))
            : Result(PyNull.Instance);

    private RpcDispatchResult BindSkillHandler(MachoRpcRequest request)
    {
        if (selectedCharacter is null
            || request.Arguments.Items.Length != 2
            || !TryInteger(request.Arguments.Items[0], out long characterId)
            || characterId != selectedCharacter.CharacterId
            || Unwrap(request.Arguments.Items[1]) is not PyTuple { Items.Length: 3 } nested
            || ReadText(nested.Items[0]) is not ("GetSkills" or "GetBoosters")
            || Unwrap(nested.Items[1]) is not PyTuple { Items.Length: 0 }
            || Unwrap(nested.Items[2]) is not PyDictionary { Entries.Length: 0 })
        {
            return Result(PyNull.Instance);
        }

        skillHandlerBinding ??= CreateSkillHandlerBinding(request.CallId);
        return Result(new PyTuple(
            CreateLease(skillHandlerBinding),
            new PyDictionary()));
    }

    private RpcDispatchResult GetSkillBoosters(MachoRpcRequest request)
        => request.BoundObject == skillHandlerBinding
            && request.Arguments.Items.Length == 0
                ? Result(new PyDictionary())
                : Result(PyNull.Instance);

    private static string CreateSkillHandlerBinding(long callId)
        => $"N={ProxyNodeId}:{checked((callId * 2) + 50_000)}";
}
