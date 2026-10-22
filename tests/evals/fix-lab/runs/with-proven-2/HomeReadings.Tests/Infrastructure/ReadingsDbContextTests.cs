using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HomeReadings.Tests.Infrastructure;

public sealed class ReadingsDbContextTests
{
    private static ReadingsDbContext Context() =>
        new(new DbContextOptionsBuilder<ReadingsDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=x").Options);

    [Fact]
    public void Model_EveryStringColumn_HasAMaxLength()
    {
        using var db = Context();

        var unbounded = db.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(string) && p.GetMaxLength() is null)
            .Select(p => $"{p.DeclaringType.ClrType.Name}.{p.Name}");

        unbounded.Should().BeEmpty("an unconfigured string is an unbounded text column (efcore-rules)");
    }

    [Fact]
    public void Model_EveryTimestamp_IsTimestamptz()
    {
        using var db = Context();

        var notTz = db.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => (Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType) == typeof(DateTime) && p.GetColumnType() != "timestamptz")
            .Select(p => $"{p.DeclaringType.ClrType.Name}.{p.Name}");

        notTz.Should().BeEmpty();
    }
}
