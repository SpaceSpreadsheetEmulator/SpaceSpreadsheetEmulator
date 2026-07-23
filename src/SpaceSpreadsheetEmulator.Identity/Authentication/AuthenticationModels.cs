using System.Collections.Immutable;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Identity.Authentication;

public sealed record LoginAttempt(string UserName, ImmutableArray<byte> CredentialProof, string LanguageId, string CountryCode)
{
    public LoginAttempt(string userName, ReadOnlySpan<byte> credentialProof, string languageId, string countryCode)
        : this(userName, ImmutableArray.Create(credentialProof.ToArray()), languageId, countryCode)
    {
    }
}

public sealed record AuthenticatedAccount(
    AccountId AccountId,
    string UserName,
    string LanguageId,
    string CountryCode,
    long Role);

public enum AuthenticationFailure
{
    None,
    InvalidRequest,
    AccountNotFound,
    CredentialMismatch,
    CapacityReached,
}

public sealed record AuthenticationResult(AuthenticatedAccount? Account, AuthenticationFailure Failure)
{
    public bool IsSuccess => Account is not null;

    public static AuthenticationResult Success(AuthenticatedAccount account) => new(account, AuthenticationFailure.None);

    public static AuthenticationResult Rejected(AuthenticationFailure failure) => new(null, failure);
}

public interface IAccountAuthenticator
{
    ValueTask<AuthenticationResult> AuthenticateAsync(
        LoginAttempt attempt,
        CancellationToken cancellationToken = default);
}
