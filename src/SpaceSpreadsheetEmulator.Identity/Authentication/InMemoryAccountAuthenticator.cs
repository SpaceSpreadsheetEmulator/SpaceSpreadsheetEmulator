using System.Collections.Concurrent;
using System.Security.Cryptography;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Identity.Authentication;

public sealed class InMemoryAccountAuthenticator(bool enrollmentEnabled, int maximumAccounts = 64) : IAccountAuthenticator, IDisposable
{
    private const int MaximumUserNameLength = 100;
    private const int MaximumCredentialProofLength = 256;
    private readonly ConcurrentDictionary<string, AccountRecord> accounts = new(StringComparer.OrdinalIgnoreCase);
    private long nextAccountId;

    public ValueTask<AuthenticationResult> AuthenticateAsync(
        LoginAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(attempt);
        if (string.IsNullOrWhiteSpace(attempt.UserName)
            || attempt.UserName.Length > MaximumUserNameLength
            || attempt.CredentialProof.IsDefaultOrEmpty
            || attempt.CredentialProof.Length > MaximumCredentialProofLength)
        {
            return ValueTask.FromResult(AuthenticationResult.Rejected(AuthenticationFailure.InvalidRequest));
        }

        if (!accounts.TryGetValue(attempt.UserName, out AccountRecord? record))
        {
            if (!enrollmentEnabled)
            {
                return ValueTask.FromResult(AuthenticationResult.Rejected(AuthenticationFailure.AccountNotFound));
            }

            if (accounts.Count >= maximumAccounts)
            {
                return ValueTask.FromResult(AuthenticationResult.Rejected(AuthenticationFailure.CapacityReached));
            }

            var created = new AccountRecord(
                new AccountId(Interlocked.Increment(ref nextAccountId)),
                attempt.UserName,
                attempt.CredentialProof.ToArray());
            record = accounts.GetOrAdd(attempt.UserName, created);
            if (!ReferenceEquals(record, created))
            {
                CryptographicOperations.ZeroMemory(created.CredentialProof);
            }
        }

        if (!CryptographicOperations.FixedTimeEquals(record.CredentialProof, attempt.CredentialProof.AsSpan()))
        {
            return ValueTask.FromResult(AuthenticationResult.Rejected(AuthenticationFailure.CredentialMismatch));
        }

        string language = string.IsNullOrWhiteSpace(attempt.LanguageId) ? "EN" : attempt.LanguageId;
        string country = string.IsNullOrWhiteSpace(attempt.CountryCode) ? "BG" : attempt.CountryCode;
        return ValueTask.FromResult(AuthenticationResult.Success(new AuthenticatedAccount(
            record.AccountId,
            record.UserName,
            language,
            country,
            1)));
    }

    public void Dispose()
    {
        foreach (AccountRecord account in accounts.Values)
        {
            CryptographicOperations.ZeroMemory(account.CredentialProof);
        }

        accounts.Clear();
    }

    private sealed record AccountRecord(AccountId AccountId, string UserName, byte[] CredentialProof);
}
