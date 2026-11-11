using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeterSync.Infrastructure;

public static class DbErrors
{
    /// <summary>A unique index rejected the insert: another request or pod wrote the same row first.</summary>
    /// <param name="ex">The exception from SaveChanges.</param>
    /// <returns><c>true</c> for a unique violation.</returns>
    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
