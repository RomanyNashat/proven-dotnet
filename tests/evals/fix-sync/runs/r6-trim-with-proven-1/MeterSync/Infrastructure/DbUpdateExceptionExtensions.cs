using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeterSync.Infrastructure;

public static class DbUpdateExceptionExtensions
{
    /// <summary>True when PostgreSQL rejected the save because a unique index already holds the value.</summary>
    /// <param name="exception">The exception from SaveChanges.</param>
    /// <returns>Whether the cause is a unique violation (SQLSTATE 23505).</returns>
    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
