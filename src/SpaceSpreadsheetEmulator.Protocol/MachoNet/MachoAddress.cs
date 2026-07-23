using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.MachoNet;

/// <summary>
/// Provides the typed base for source and destination addresses carried by MachoNet packets.
/// </summary>
public abstract record MachoAddress
{
    public PyObject? OriginalValue { get; init; }
}

/// <summary>
/// Addresses a cluster node and, optionally, a service hosted by that node.
/// </summary>
public sealed record MachoNodeAddress(long? NodeId, string? Service) : MachoAddress;

/// <summary>
/// Addresses a connected client and, optionally, one outstanding call.
/// </summary>
public sealed record MachoClientAddress(long ClientId, long? CallId) : MachoAddress;

/// <summary>
/// Addresses recipients selected by a MachoNet broadcast scope and narrowcast value.
/// </summary>
public sealed record MachoBroadcastAddress(string Scope, PyValue Narrowcast, string? Service) : MachoAddress;

/// <summary>
/// Represents the wildcard MachoNet address with no named service.
/// </summary>
public sealed record MachoAnyAddress : MachoAddress
{
    public static MachoAnyAddress Instance { get; } = new();

    private MachoAnyAddress()
    {
    }
}

/// <summary>
/// Addresses a named service without selecting a specific cluster node.
/// </summary>
public sealed record MachoServiceAddress(string Service) : MachoAddress;
