using System.Data;
using Microsoft.EntityFrameworkCore;
using SpaceSpreadsheetEmulator.Identity.Authentication;
using SpaceSpreadsheetEmulator.Persistence.Database;
using SpaceSpreadsheetEmulator.Persistence.Entities;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Persistence.Accounts;

internal sealed class PostgreSqlAccountIdentityStore(
    IDbContextFactory<GameDbContext> contextFactory,
    TimeProvider timeProvider) : IAccountIdentityStore
{
    private const int MaximumAttempts = 3;

    public async ValueTask<AccountEnrollmentResult> GetOrEnrollAsync(
        string userName,
        int maximumAccounts,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumAccounts);
        string normalizedUserName = userName.ToUpperInvariant();
        if (normalizedUserName.Length > 100)
        {
            throw new ArgumentException("The normalized user name cannot exceed 100 characters.", nameof(userName));
        }

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await using GameDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
                await using var transaction = await context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
                AccountEntity? existing = await context.Accounts.SingleOrDefaultAsync(
                    account => account.NormalizedUserName == normalizedUserName,
                    cancellationToken);
                if (existing is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return AccountEnrollmentResult.Success(Map(existing));
                }

                if (await context.Accounts.CountAsync(cancellationToken) >= maximumAccounts)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return AccountEnrollmentResult.CapacityRejected();
                }

                DateTimeOffset now = timeProvider.GetUtcNow();
                var created = new AccountEntity
                {
                    UserName = userName,
                    NormalizedUserName = normalizedUserName,
                    CreatedAt = now,
                    UpdatedAt = now,
                    Version = 1,
                };
                await context.Accounts.AddAsync(created, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return AccountEnrollmentResult.Success(Map(created));
            }
            catch (Exception error) when (
                attempt < MaximumAttempts
                && PostgreSqlRetryClassifier.IsRetryableTransaction(error))
            {
            }
        }
    }

    private static AccountIdentity Map(AccountEntity account)
        => new(new AccountId(account.AccountId), account.UserName);
}
