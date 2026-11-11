using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeterSync.Tests.Infrastructure;

public sealed class DbUpdateExceptionExtensionsTests
{
    [Fact]
    public void IsUniqueViolation_PostgresUniqueViolation_IsTrue()
    {
        Wrap(PostgresErrorCodes.UniqueViolation).IsUniqueViolation().Should().BeTrue();
    }

    [Fact]
    public void IsUniqueViolation_OtherPostgresError_IsFalse()
    {
        Wrap(PostgresErrorCodes.ForeignKeyViolation).IsUniqueViolation().Should().BeFalse();
    }

    [Fact]
    public void IsUniqueViolation_NoPostgresInnerException_IsFalse()
    {
        new DbUpdateException("failed", new InvalidOperationException()).IsUniqueViolation().Should().BeFalse();
    }

    private static DbUpdateException Wrap(string sqlState) =>
        new("failed", new PostgresException("duplicate", "ERROR", "ERROR", sqlState));
}
