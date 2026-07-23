using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Gateway.Compatibility;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Connections;

internal sealed partial class GatewayClientConnection
{
    private async Task<RpcDispatchResult> GetAgentsAsync(
        MachoRpcRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Arguments.Items.Length != 0 || loginSession is null)
        {
            return Result(PyNull.Instance);
        }

        NpcAgentCatalogResponse? catalog =
            await loginBackend.GetNpcAgentCatalogAsync(
                gatewaySessionId,
                loginSession.LoginTicket,
                cancellationToken);
        return catalog is null
            ? Result(PyNull.Instance)
            : CacheMethodResult(
                "agentMgr",
                "GetAgents",
                Build3396210AgentMapper.CreateAgentCatalog(catalog));
    }
}
