using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HomeReadings.Tests;

/// <summary>Checks the schema the model creates, without a database.</summary>
public sealed class ModelTests
{
    private static string CreateScript()
    {
        var options = new DbContextOptionsBuilder<ReadingsDbContext>().UseNpgsql("Host=unused").Options;
        using var db = new ReadingsDbContext(options);
        return db.Database.GenerateCreateScript();
    }

    [Fact]
    public void CreateScript_HasNoUnboundedTextColumns()
    {
        var script = CreateScript();

        script.Should().NotContain(" text").And.NotContain("bytea");
        script.Should().Contain("\"Notes\" character varying(500)");
    }

    [Fact]
    public void CreateScript_AlertHasLeaseAndForeignKey()
    {
        var script = CreateScript();

        script.Should().Contain("\"ClaimedUntil\" timestamptz");
        script.Should().Contain("REFERENCES readings (\"Id\")");
    }
}
