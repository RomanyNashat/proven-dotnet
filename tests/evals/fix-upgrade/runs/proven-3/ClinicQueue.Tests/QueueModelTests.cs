using ClinicQueue.Domain;
using ClinicQueue.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Tests;

/// <summary>The model against the tables as they are; builds the SQL without a database.</summary>
public sealed class QueueModelTests : IDisposable
{
    private readonly QueueDbContext _db = new(new DbContextOptionsBuilder<QueueDbContext>()
        .UseNpgsql("Host=unused;Database=unused").Options);

    [Theory]
    [InlineData(nameof(Visit.Id), "id", "integer")]
    [InlineData(nameof(Visit.ClinicId), "clinic_id", "integer")]
    [InlineData(nameof(Visit.PatientId), "patient_id", "integer")]
    [InlineData(nameof(Visit.Day), "day", "date")]
    [InlineData(nameof(Visit.Position), "position", "integer")]
    public void VisitModel_Property_MapsToExistingColumn(string property, string column, string type)
    {
        var mapped = _db.Model.FindEntityType(typeof(Visit))!.FindProperty(property)!;

        (mapped.GetColumnName(), mapped.GetColumnType()).Should().Be((column, type));
    }

    [Fact]
    public void VisitModel_Table_IsVisit() =>
        _db.Model.FindEntityType(typeof(Visit))!.GetTableName().Should().Be("visit");

    [Fact]
    public void ClinicModel_Columns_AreIdAndName()
    {
        var clinic = _db.Model.FindEntityType(typeof(Clinic))!;

        (clinic.GetTableName(), clinic.FindProperty(nameof(Clinic.Name))!.GetColumnName()).Should().Be(("clinic", "name"));
    }

    [Fact]
    public void QueueQuery_DayFilter_ComparesDateColumnInSql()
    {
        var day = new DateOnly(2026, 11, 14);

        var sql = _db.Visits.Where(v => v.ClinicId == 1 && v.Day == day).OrderBy(v => v.Position).Select(v => v.Position).ToQueryString();

        sql.Should().Contain("v.day = @").And.Contain("ORDER BY v.position");
    }

    public void Dispose() => _db.Dispose();
}
