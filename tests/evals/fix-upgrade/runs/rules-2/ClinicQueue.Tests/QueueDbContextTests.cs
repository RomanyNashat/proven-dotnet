using ClinicQueue.Domain;
using ClinicQueue.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Tests;

public sealed class QueueDbContextTests
{
    [Fact]
    public void Model_VisitDay_MapsToDateColumnNamedDay()
    {
        using var db = NewContext();

        var day = db.Model.FindEntityType(typeof(Visit))!.FindProperty(nameof(Visit.Day))!;

        (day.GetColumnName(), day.GetColumnType(), day.ClrType).Should().Be(("day", "date", typeof(DateOnly)));
    }

    [Fact]
    public void QueueQuery_ByClinicAndDay_TranslatesToSqlOnVisitTable()
    {
        using var db = NewContext();
        var day = new DateOnly(2026, 11, 14);

        var sql = db.Visits.Where(v => v.ClinicId == 3 && v.Day == day).OrderBy(v => v.Position).Select(v => v.Position).ToQueryString();

        sql.Should().Contain("FROM visit").And.Contain("v.day = @day").And.Contain("(DbType = Date)");
    }

    private static QueueDbContext NewContext() =>
        new(new DbContextOptionsBuilder<QueueDbContext>().UseNpgsql("Host=unused;Database=unused").Options);
}
