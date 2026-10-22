using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HomeReadings.Tests.Infrastructure;

/// <summary>The schema the model creates, read from the DDL EF generates (no database needed).</summary>
public sealed class ReadingsDbContextTests
{
    private static string CreateScript()
    {
        using var db = new ReadingsDbContext(new DbContextOptionsBuilder<ReadingsDbContext>().UseNpgsql("Host=unused").Options);
        return db.Database.GenerateCreateScript();
    }

    [Fact]
    public void CreateScript_EveryColumn_IsBoundedAndNotBinary()
    {
        var script = CreateScript();

        script.Should().NotContain(" text").And.NotContain("bytea").And.NotContain("character varying,").And.NotContain("character varying\n");
    }

    [Fact]
    public void CreateScript_Alert_ReferencesItsReading()
    {
        CreateScript().Should().Contain("FOREIGN KEY (\"ReadingId\") REFERENCES readings (\"Id\")");
    }

    [Fact]
    public void CreateScript_WaitingAlerts_HaveAPartialIndex()
    {
        CreateScript().Should().Contain("ix_care_team_alerts_waiting").And.Contain("WHERE \"SentAt\" IS NULL AND \"NeedsFollowUpAt\" IS NULL");
    }

    [Fact]
    public void CreateScript_Notes_AreLimitedLikeTheRequest()
    {
        CreateScript().Should().Contain("\"Notes\" character varying(500)");
    }
}
