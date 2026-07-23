using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Worker;

/// <summary>
/// Validates Gateway identity, session, correlation, and client-build fields on Worker requests.
/// </summary>
internal static class BackplaneRequestValidator
{
    public static bool TryValidateContext(
        RequestContext? context,
        out NodeId gatewayId,
        out GatewaySessionId sessionId)
    {
        gatewayId = default;
        sessionId = default;
        if (context is null
            || context.ClientBuild != 3_396_210
            || context.GatewaySessionId == 0
            || string.IsNullOrWhiteSpace(context.GatewayId)
            || string.IsNullOrWhiteSpace(context.CorrelationId))
        {
            return false;
        }

        try
        {
            gatewayId = new NodeId(context.GatewayId);
            sessionId = new GatewaySessionId(context.GatewaySessionId);
            _ = new CorrelationId(context.CorrelationId);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
