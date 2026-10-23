using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeterSync.Infrastructure;

public static class DbErrors
{
    /// <summary>True when the save failed on a unique index, for example another pod inserted the row first.</summary>
    /// <param name="ex">The exception from <c>SaveChangesAsync</c>.</param>
    /// <returns>Whether it is a unique violation.</returns>
    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
