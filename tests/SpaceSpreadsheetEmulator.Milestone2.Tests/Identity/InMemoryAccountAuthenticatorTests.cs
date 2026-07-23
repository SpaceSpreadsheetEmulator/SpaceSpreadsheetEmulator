using System.Collections.Immutable;
using SpaceSpreadsheetEmulator.Identity.Authentication;

namespace SpaceSpreadsheetEmulator.Milestone2.Tests.Identity;

public class InMemoryAccountAuthenticatorTests
{
    [Fact]
    public async Task DevelopmentEnrollmentCreatesStableAccountAndChecksProof()
    {
        using var authenticator = new InMemoryAccountAuthenticator(enrollmentEnabled: true);
        var firstAttempt = new LoginAttempt("Pilot", ImmutableArray.Create<byte>(1, 2, 3), "EN", "BG");

        AuthenticationResult first = await authenticator.AuthenticateAsync(firstAttempt);
        AuthenticationResult second = await authenticator.AuthenticateAsync(
            firstAttempt with { UserName = "pilot" });
        AuthenticationResult rejected = await authenticator.AuthenticateAsync(
            firstAttempt with { CredentialProof = ImmutableArray.Create<byte>(9, 9, 9) });

        Assert.True(first.IsSuccess);
        Assert.Equal(first.Account!.AccountId, second.Account!.AccountId);
        Assert.Equal(AuthenticationFailure.CredentialMismatch, rejected.Failure);
    }

    [Fact]
    public async Task ProductionModeDoesNotEnrollUnknownAccount()
    {
        using var authenticator = new InMemoryAccountAuthenticator(enrollmentEnabled: false);

        AuthenticationResult result = await authenticator.AuthenticateAsync(
            new LoginAttempt("unknown", ImmutableArray.Create<byte>(1), "EN", "BG"));

        Assert.Equal(AuthenticationFailure.AccountNotFound, result.Failure);
    }
}
