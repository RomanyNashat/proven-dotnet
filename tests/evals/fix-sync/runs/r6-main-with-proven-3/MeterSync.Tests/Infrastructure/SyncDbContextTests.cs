using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Tests.Infrastructure;

/// <summary>
/// The schema the model creates, read from the generated DDL (no database needed). The raw SQL in
/// MeterSyncService and AlertSweeper names these tables and columns, so they must not drift.
/// </summary>
public sealed class SyncDbContextTests
{
    private static readonly string Ddl = CreateScript();

    [Theory]
    [InlineData("\"PatientId\" integer NOT NULL")]
    [InlineData("\"MeterId\" integer NOT NULL")]
    [InlineData("\"VendorId\" character varying(64) NOT NULL")]
    [InlineData("\"Raw\" character varying(16) NOT NULL")]
    [InlineData("\"Unit\" character varying(8) NOT NULL")]
    [InlineData("\"ValueMgDl\" numeric(6,1) NOT NULL")]
    [InlineData("\"TakenAt\" timestamptz NOT NULL")]
    [InlineData("\"Alert\" character varying(8) NOT NULL")]
    [InlineData("\"AlertSentAt\" timestamptz")]
    [InlineData("\"AlertClaimedAt\" timestamptz")]
    public void CreateScript_Readings_HasColumnsTheRawSqlUses(string column)
    {
        Ddl.Should().Contain("CREATE TABLE readings").And.Contain(column);
    }

    [Fact]
    public void CreateScript_Readings_HasUniqueVendorIdForOnConflict()
    {
        Ddl.Should().Contain("CREATE UNIQUE INDEX \"IX_readings_VendorId\" ON readings (\"VendorId\")");
    }

    [Fact]
    public void CreateScript_Readings_HasPartialIndexForDueAlerts()
    {
        Ddl.Should().Contain("CREATE INDEX ix_readings_alert_due ON readings (\"Id\") WHERE \"Alert\" <> 'None' AND \"AlertSentAt\" IS NULL");
    }

    [Theory]
    [InlineData("REFERENCES meters (\"Id\") ON DELETE RESTRICT")]
    [InlineData("REFERENCES patients (\"Id\") ON DELETE RESTRICT")]
    [InlineData("CREATE INDEX \"IX_readings_MeterId\"")]
    [InlineData("CREATE INDEX \"IX_readings_PatientId\"")]
    public void CreateScript_Readings_HasForeignKeysAndTheirIndexes(string fragment)
    {
        Ddl.Should().Contain(fragment);
    }

    [Fact]
    public void CreateScript_AnyTable_HasNoUnboundedText()
    {
        Ddl.Should().NotContain(" text").And.NotContain("character varying NOT NULL").And.NotContain("bytea");
    }

    private static string CreateScript()
    {
        var options = new DbContextOptionsBuilder<SyncDbContext>().UseNpgsql("Host=unused").Options;
        using var db = new SyncDbContext(options);
        return db.Database.GenerateCreateScript();
    }
}
