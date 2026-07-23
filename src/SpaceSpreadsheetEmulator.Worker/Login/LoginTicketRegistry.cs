using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using SpaceSpreadsheetEmulator.Identity.Authentication;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Worker.Login;

/// <summary>
/// Issues bounded, expiring login tickets tied to one Gateway and client session.
/// </summary>
public sealed class LoginTicketRegistry(
    IOptions<WorkerLoginOptions> options,
    TimeProvider timeProvider)
{
    private const int TicketLength = 32;
    private readonly ConcurrentDictionary<string, TicketRecord> tickets = new(StringComparer.Ordinal);

    public byte[] Issue(NodeId gatewayId, GatewaySessionId sessionId, AuthenticatedAccount account)
    {
        PruneExpired();
        if (tickets.Count >= options.Value.MaximumSessions)
        {
            throw new InvalidOperationException("The Worker login-session capacity is full.");
        }

        byte[] ticket = RandomNumberGenerator.GetBytes(TicketLength);
        string key = Convert.ToHexString(ticket);
        var record = new TicketRecord(
            gatewayId,
            sessionId,
            account,
            timeProvider.GetUtcNow().AddMinutes(options.Value.SessionLifetimeMinutes));
        if (!tickets.TryAdd(key, record))
        {
            CryptographicOperations.ZeroMemory(ticket);
            throw new InvalidOperationException("A duplicate login ticket was generated.");
        }

        return ticket;
    }

    public bool TryResolve(
        ReadOnlySpan<byte> ticket,
        NodeId gatewayId,
        GatewaySessionId sessionId,
        out AuthenticatedAccount? account)
    {
        account = null;
        if (ticket.Length != TicketLength
            || !tickets.TryGetValue(Convert.ToHexString(ticket), out TicketRecord? record))
        {
            return false;
        }

        if (record.ExpiresAt <= timeProvider.GetUtcNow()
            || record.GatewayId != gatewayId
            || record.SessionId != sessionId)
        {
            tickets.TryRemove(Convert.ToHexString(ticket), out _);
            return false;
        }

        account = record.Account;
        return true;
    }

    public bool Close(ReadOnlySpan<byte> ticket, NodeId gatewayId, GatewaySessionId sessionId)
    {
        string key = Convert.ToHexString(ticket);
        return tickets.TryGetValue(key, out TicketRecord? record)
            && record.GatewayId == gatewayId
            && record.SessionId == sessionId
            && tickets.TryRemove(key, out _);
    }

    private void PruneExpired()
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        foreach ((string key, TicketRecord record) in tickets)
        {
            if (record.ExpiresAt <= now)
            {
                tickets.TryRemove(key, out _);
            }
        }
    }

    private sealed record TicketRecord(
        NodeId GatewayId,
        GatewaySessionId SessionId,
        AuthenticatedAccount Account,
        DateTimeOffset ExpiresAt);
}
