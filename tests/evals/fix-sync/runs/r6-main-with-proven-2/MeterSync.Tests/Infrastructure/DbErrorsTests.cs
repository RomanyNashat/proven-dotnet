using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeterSync.Tests.Infrastructure;

public sealed class DbErrorsTests
{
    [Fact]
    public void IsUniqueViolation_PostgresUniqueViolation_ReturnsTrue()
    {
        var ex = new DbUpdateException("save failed", new PostgresException("duplicate key", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation));

        DbErrors.IsUniqueViolation(ex).Should().BeTrue();
    }

    [Fact]
    public void IsUniqueViolation_OtherPostgresError_ReturnsFalse()
    {
        var ex = new DbUpdateException("save failed", new PostgresException("fk", "ERROR", "ERROR", PostgresErrorCodes.ForeignKeyViolation));

        DbErrors.IsUniqueViolation(ex).Should().BeFalse();
    }

    [Fact]
    public void IsUniqueViolation_NoInnerException_ReturnsFalse()
    {
        DbErrors.IsUniqueViolation(new DbUpdateException("save failed")).Should().BeFalse();
    }
}
