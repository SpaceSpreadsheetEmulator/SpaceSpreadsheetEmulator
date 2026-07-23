using SpaceSpreadsheetEmulator.Gateway.Compatibility;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Connections;

internal sealed partial class GatewayClientConnection
{
    private readonly Dictionary<string, Build3396210ObjectCacheEntry> methodCache =
        new(StringComparer.Ordinal);

    private RpcDispatchResult CacheMethodResult(string service, string method, PyValue value)
    {
        Build3396210ObjectCacheEntry entry = Build3396210ObjectCacheMapper.CreateEntry(
            service,
            method,
            value,
            ProxyNodeId,
            timeProvider.GetUtcNow(),
            profile);
        methodCache[Route(service, method)] = entry;
        return Result(Build3396210ObjectCacheMapper.CreateMethodResult(entry));
    }

    private RpcDispatchResult? GetCachableObject(MachoRpcRequest request)
    {
        if (!TryReadMethodCacheRoute(request.Arguments, out string? route)
            || !methodCache.TryGetValue(route, out Build3396210ObjectCacheEntry? entry))
        {
            return null;
        }

        return Result(
            Build3396210ObjectCacheMapper.CreateCachePayload(entry),
            compressResponse: true);
    }

    private static bool TryReadMethodCacheRoute(PyTuple arguments, out string route)
    {
        route = string.Empty;
        if (arguments.Items.Length < 2
            || Unwrap(arguments.Items[1]) is not PyTuple { Items.Length: >= 3 } key
            || Unwrap(key.Items[2]) is not PyTuple { Items.Length: >= 2 } method
            || ReadText(method.Items[0]) is not { Length: > 0 } service
            || ReadText(method.Items[1]) is not { Length: > 0 } methodName)
        {
            return false;
        }

        route = Route(service, methodName);
        return true;
    }

    private static string Route(string service, string method)
        => $"{service}.{method}";
}
