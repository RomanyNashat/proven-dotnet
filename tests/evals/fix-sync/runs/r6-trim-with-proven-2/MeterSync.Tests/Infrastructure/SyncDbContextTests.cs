using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Tests.Infrastructure;

// Generates the DDL from the model with the real Npgsql provider; no connection is opened.
public sealed class SyncDbContextTests
{
    private static string CreateScript()
    {
        using var db = new SyncDbContext(new DbContextOptionsBuilder<SyncDbContext>().UseNpgsql("Host=unused").Options);
        return db.Database.GenerateCreateScript();
    }

    [Fact]
    public void GenerateCreateScript_Readings_HasPartialDueIndexOnId() =>
        CreateScript().Should().Contain(
            "CREATE INDEX ix_readings_alert_due ON readings (\"Id\") WHERE \"AlertSentAt\" IS NULL AND \"Alert\" <> 'None';");

    [Fact]
    public void GenerateCreateScript_Readings_HasClaimColumns()
    {
        var script = CreateScript();

        script.Should().Contain("\"AlertClaimedUntil\" timestamptz")
            .And.Contain("\"AlertAttempts\" integer NOT NULL")
            .And.NotContain("\"Alert\", \"AlertSentAt\"");
    }
}
