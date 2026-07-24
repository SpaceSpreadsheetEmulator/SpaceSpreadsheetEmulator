using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V2;
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
        => TryValidateContext(
            context?.GatewayId,
            context?.GatewaySessionId ?? 0,
            context?.CorrelationId,
            context?.ClientBuild ?? 0,
            out gatewayId,
            out sessionId);

    public static bool TryValidateContext(
        GameplayRequestContext? context,
        out NodeId gatewayId,
        out GatewaySessionId sessionId)
        => TryValidateContext(
            context?.GatewayId,
            context?.GatewaySessionId ?? 0,
            context?.CorrelationId,
            context?.ClientBuild ?? 0,
            out gatewayId,
            out sessionId);

    private static bool TryValidateContext(
        string? gatewayIdValue,
        ulong gatewaySessionId,
        string? correlationId,
        uint clientBuild,
        out NodeId gatewayId,
        out GatewaySessionId sessionId)
    {
        gatewayId = default;
        sessionId = default;
        if (clientBuild != 3_396_210
            || gatewaySessionId == 0
            || string.IsNullOrWhiteSpace(gatewayIdValue)
            || string.IsNullOrWhiteSpace(correlationId))
        {
            return false;
        }

        try
        {
            gatewayId = new NodeId(gatewayIdValue);
            sessionId = new GatewaySessionId(gatewaySessionId);
            _ = new CorrelationId(correlationId);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
