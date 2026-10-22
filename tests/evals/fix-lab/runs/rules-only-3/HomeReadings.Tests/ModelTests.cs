using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HomeReadings.Tests;

public sealed class ModelTests
{
    [Fact]
    public void CreateScript_Model_HasBoundedNotesAndUniqueReading()
    {
        using var db = new ReadingsDbContext(new DbContextOptionsBuilder<ReadingsDbContext>().UseNpgsql("Host=unused").Options);

        var script = db.Database.GenerateCreateScript();

        script.Should().Contain("\"Notes\" character varying(500)")
            .And.Contain("CREATE UNIQUE INDEX \"IX_readings_PatientId_CollectedAt_Value\"")
            .And.Contain("WHERE \"SentAt\" IS NULL")
            .And.NotContain(" text");
    }
}
