using ClinicQueue.Domain;
using ClinicQueue.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Tests;

public sealed class QueueDbContextTests
{
    [Fact]
    public void Model_Visit_MapsToExistingVisitTableAndColumns()
    {
        using var db = NewContext();
        var visit = db.Model.FindEntityType(typeof(Visit))!;

        var columns = visit.GetProperties().ToDictionary(p => p.GetColumnName(), p => p.GetColumnType());

        visit.GetTableName().Should().Be("visit");
        columns.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["id"] = "integer", ["clinic_id"] = "integer", ["patient_id"] = "integer", ["day"] = "date", ["position"] = "integer",
        });
    }

    [Fact]
    public void QueueQuery_DateOnlyDay_ComparesDateColumnInDatabase()
    {
        using var db = NewContext();
        var day = new DateOnly(2026, 11, 14);

        var sql = db.Visits.Where(v => v.ClinicId == 1 && v.Day == day).OrderBy(v => v.Position).Select(v => v.Position).ToQueryString();

        sql.Should().Contain("v.day = @day").And.Contain("ORDER BY v.position");
    }

    private static QueueDbContext NewContext() =>
        new(new DbContextOptionsBuilder<QueueDbContext>().UseNpgsql("Host=invalid.invalid").Options);
}
