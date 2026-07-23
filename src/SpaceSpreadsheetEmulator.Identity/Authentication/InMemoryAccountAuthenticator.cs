using System.Collections.Concurrent;
using System.Security.Cryptography;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Identity.Authentication;

/// <summary>
/// Authenticates a bounded set of local accounts and optionally enrolls them on first login.
/// </summary>
public sealed class InMemoryAccountAuthenticator : IAccountAuthenticator, IDisposable
{
    private const int MaximumUserNameLength = 100;
    private const int MaximumCredentialProofLength = 256;
    private readonly ConcurrentDictionary<string, AccountRecord> accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly IAccountIdentityStore identities;
    private readonly bool enrollmentEnabled;
    private readonly int maximumAccounts;

    public InMemoryAccountAuthenticator(bool enrollmentEnabled, int maximumAccounts = 64)
        : this(new InMemoryAccountIdentityStore(), enrollmentEnabled, maximumAccounts)
    {
    }

    public InMemoryAccountAuthenticator(
        IAccountIdentityStore identities,
        bool enrollmentEnabled,
        int maximumAccounts = 64)
    {
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumAccounts);
        this.identities = identities;
        this.enrollmentEnabled = enrollmentEnabled;
        this.maximumAccounts = maximumAccounts;
    }

    public async ValueTask<AuthenticationResult> AuthenticateAsync(
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
            return AuthenticationResult.Rejected(AuthenticationFailure.InvalidRequest);
        }

        if (!accounts.TryGetValue(attempt.UserName, out AccountRecord? record))
        {
            if (!enrollmentEnabled)
            {
                return AuthenticationResult.Rejected(AuthenticationFailure.AccountNotFound);
            }

            AccountEnrollmentResult enrollment = await identities.GetOrEnrollAsync(
                attempt.UserName,
                maximumAccounts,
                cancellationToken);
            if (enrollment.CapacityReached || enrollment.Account is null)
            {
                return AuthenticationResult.Rejected(AuthenticationFailure.CapacityReached);
            }

            var created = new AccountRecord(
                enrollment.Account.AccountId,
                enrollment.Account.UserName,
                attempt.CredentialProof.ToArray());
            record = accounts.GetOrAdd(attempt.UserName, created);
            if (!ReferenceEquals(record, created))
            {
                CryptographicOperations.ZeroMemory(created.CredentialProof);
            }
        }

        if (!CryptographicOperations.FixedTimeEquals(record.CredentialProof, attempt.CredentialProof.AsSpan()))
        {
            return AuthenticationResult.Rejected(AuthenticationFailure.CredentialMismatch);
        }

        string language = string.IsNullOrWhiteSpace(attempt.LanguageId) ? "EN" : attempt.LanguageId;
        string country = string.IsNullOrWhiteSpace(attempt.CountryCode) ? "BG" : attempt.CountryCode;
        return AuthenticationResult.Success(new AuthenticatedAccount(
            record.AccountId,
            record.UserName,
            language,
            country,
            1));
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

    private sealed class InMemoryAccountIdentityStore : IAccountIdentityStore
    {
        private readonly ConcurrentDictionary<string, AccountIdentity> identities = new(StringComparer.OrdinalIgnoreCase);
        private long nextAccountId;

        public ValueTask<AccountEnrollmentResult> GetOrEnrollAsync(
            string userName,
            int maximumAccounts,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (identities.TryGetValue(userName, out AccountIdentity? existing))
            {
                return ValueTask.FromResult(AccountEnrollmentResult.Success(existing));
            }

            if (identities.Count >= maximumAccounts)
            {
                return ValueTask.FromResult(AccountEnrollmentResult.CapacityRejected());
            }

            var created = new AccountIdentity(
                new AccountId(Interlocked.Increment(ref nextAccountId)),
                userName);
            AccountIdentity identity = identities.GetOrAdd(userName, created);
            return ValueTask.FromResult(AccountEnrollmentResult.Success(identity));
        }
    }
}
