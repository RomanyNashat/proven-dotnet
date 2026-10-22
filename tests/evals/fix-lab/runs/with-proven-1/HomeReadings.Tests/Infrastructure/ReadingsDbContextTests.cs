using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HomeReadings.Tests.Infrastructure;

/// <summary>The DDL the model produces, read without a database.</summary>
public sealed class ReadingsDbContextTests
{
    private static string CreateScript()
    {
        using var db = new ReadingsDbContext(new DbContextOptionsBuilder<ReadingsDbContext>().UseNpgsql("Host=unused").Options);
        return db.Database.GenerateCreateScript();
    }

    [Fact]
    public void Model_EveryString_IsBounded()
    {
        var script = CreateScript();

        script.Should().NotContain(" text").And.Contain("\"Notes\" character varying(500)");
    }

    [Fact]
    public void Model_Alert_BelongsToOneReading()
    {
        var script = CreateScript();

        script.Should().Contain("REFERENCES readings (\"Id\")")
            .And.Contain("CREATE UNIQUE INDEX \"IX_care_team_alerts_ReadingId\"")
            .And.Contain("WHERE \"SentAt\" IS NULL");
    }
}
