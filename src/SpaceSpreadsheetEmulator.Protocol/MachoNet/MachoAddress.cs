using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.MachoNet;

public abstract record MachoAddress
{
    public PyObject? OriginalValue { get; init; }
}

public sealed record MachoNodeAddress(long? NodeId, string? Service) : MachoAddress;

public sealed record MachoClientAddress(long ClientId, long? CallId) : MachoAddress;

public sealed record MachoBroadcastAddress(string Scope, PyValue Narrowcast, string? Service) : MachoAddress;

public sealed record MachoAnyAddress : MachoAddress
{
    public static MachoAnyAddress Instance { get; } = new();

    private MachoAnyAddress()
    {
    }
}

public sealed record MachoServiceAddress(string Service) : MachoAddress;
