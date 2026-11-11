using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeterSync.Tests.Infrastructure;

public sealed class PostgresErrorsTests
{
    [Fact]
    public void IsUniqueViolation_InnerUniqueViolation_ReturnsTrue()
    {
        var ex = new DbUpdateException("save failed", Postgres(PostgresErrorCodes.UniqueViolation));

        PostgresErrors.IsUniqueViolation(ex).Should().BeTrue();
    }

    [Fact]
    public void IsUniqueViolation_OtherPostgresError_ReturnsFalse()
    {
        var ex = new DbUpdateException("save failed", Postgres(PostgresErrorCodes.ForeignKeyViolation));

        PostgresErrors.IsUniqueViolation(ex).Should().BeFalse();
    }

    [Fact]
    public void IsUniqueViolation_NoInnerException_ReturnsFalse()
    {
        PostgresErrors.IsUniqueViolation(new DbUpdateException("save failed")).Should().BeFalse();
    }

    private static PostgresException Postgres(string sqlState) => new("boom", "ERROR", "ERROR", sqlState);
}
