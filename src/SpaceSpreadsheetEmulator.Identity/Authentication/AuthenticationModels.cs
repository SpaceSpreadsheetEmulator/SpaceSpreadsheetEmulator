using System.Collections.Immutable;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Identity.Authentication;

/// <summary>
/// Carries the normalized login data submitted by a client for authentication.
/// </summary>
public sealed record LoginAttempt(string UserName, ImmutableArray<byte> CredentialProof, string LanguageId, string CountryCode)
{
    public LoginAttempt(string userName, ReadOnlySpan<byte> credentialProof, string languageId, string countryCode)
        : this(userName, ImmutableArray.Create(credentialProof.ToArray()), languageId, countryCode)
    {
    }
}

/// <summary>
/// Represents the account identity and session attributes established after authentication.
/// </summary>
public sealed record AuthenticatedAccount(
    AccountId AccountId,
    string UserName,
    string LanguageId,
    string CountryCode,
    long Role);

/// <summary>
/// Classifies the reason an authentication attempt was rejected.
/// </summary>
public enum AuthenticationFailure
{
    None,
    InvalidRequest,
    AccountNotFound,
    CredentialMismatch,
    CapacityReached,
}

/// <summary>
/// Contains either an authenticated account or the failure that prevented authentication.
/// </summary>
public sealed record AuthenticationResult(AuthenticatedAccount? Account, AuthenticationFailure Failure)
{
    public bool IsSuccess => Account is not null;

    public static AuthenticationResult Success(AuthenticatedAccount account) => new(account, AuthenticationFailure.None);

    public static AuthenticationResult Rejected(AuthenticationFailure failure) => new(null, failure);
}

/// <summary>
/// Authenticates client login attempts without exposing protocol-specific wire values.
/// </summary>
public interface IAccountAuthenticator
{
    ValueTask<AuthenticationResult> AuthenticateAsync(
        LoginAttempt attempt,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Identifies an enrolled account without retaining its authentication proof.
/// </summary>
public sealed record AccountIdentity(AccountId AccountId, string UserName);

/// <summary>
/// Contains either the resolved account identity or a capacity rejection.
/// </summary>
public sealed record AccountEnrollmentResult(AccountIdentity? Account, bool CapacityReached)
{
    public static AccountEnrollmentResult Success(AccountIdentity account) => new(account, false);

    public static AccountEnrollmentResult CapacityRejected() => new(null, true);
}

/// <summary>
/// Resolves or enrolls durable account identities independently of credential storage.
/// </summary>
public interface IAccountIdentityStore
{
    ValueTask<AccountEnrollmentResult> GetOrEnrollAsync(
        string userName,
        int maximumAccounts,
        CancellationToken cancellationToken = default);
}
