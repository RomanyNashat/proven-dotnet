using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeterSync.Infrastructure;

public static class PostgresErrors
{
    /// <summary>Whether a save failed on a unique index (another request inserted the same key first).</summary>
    /// <param name="exception">The failed save.</param>
    /// <returns>True for a PostgreSQL unique violation.</returns>
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
