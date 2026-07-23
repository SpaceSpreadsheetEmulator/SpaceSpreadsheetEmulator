using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace SpaceSpreadsheetEmulator.Persistence.Database;

internal static class PostgreSqlRetryClassifier
{
    public static bool IsRetryableTransaction(Exception error)
    {
        PostgresException? postgres = error as PostgresException
            ?? (error as DbUpdateException)?.InnerException as PostgresException;
        return postgres?.SqlState is PostgresErrorCodes.SerializationFailure
            or PostgresErrorCodes.UniqueViolation;
    }
}
