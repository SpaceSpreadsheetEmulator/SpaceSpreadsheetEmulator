namespace SpaceSpreadsheetEmulator.Persistence.Entities;

internal sealed class AccountEntity
{
    public long AccountId { get; set; }

    public required string UserName { get; set; }

    public required string NormalizedUserName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long Version { get; set; }
}
