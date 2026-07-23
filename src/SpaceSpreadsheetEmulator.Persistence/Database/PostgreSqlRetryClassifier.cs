using Npgsql;

namespace SpaceSpreadsheetEmulator.Persistence.Database;

internal static class PostgreSqlRetryClassifier
{
    public static bool IsRetryableTransaction(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres
                && postgres.SqlState is PostgresErrorCodes.SerializationFailure
                    or PostgresErrorCodes.DeadlockDetected
                    or PostgresErrorCodes.UniqueViolation)
            {
                return true;
            }
        }

        return false;
    }
}
