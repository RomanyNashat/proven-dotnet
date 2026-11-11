using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Tests.Infrastructure;

// Generates the DDL from the model offline (no connection is opened): the raw INSERT in MeterSyncService
// and the partial index filter depend on these exact names.
public sealed class SyncDbContextTests
{
    private static readonly string Ddl = CreateScript();

    [Theory]
    [InlineData("\"PatientId\" integer NOT NULL")]
    [InlineData("\"MeterId\" integer NOT NULL")]
    [InlineData("\"VendorId\" character varying(64) NOT NULL")]
    [InlineData("\"Raw\" character varying(16) NOT NULL")]
    [InlineData("\"Unit\" character varying(8) NOT NULL")]
    [InlineData("\"ValueMgDl\" numeric(6,1),")]
    [InlineData("\"TakenAt\" timestamptz NOT NULL")]
    [InlineData("\"Alert\" character varying(8) NOT NULL")]
    [InlineData("\"AlertSentAt\" timestamptz,")]
    [InlineData("\"AlertClaimedAt\" timestamptz,")]
    [InlineData("\"AlertAttempts\" integer NOT NULL")]
    public void Model_Readings_HasTheColumnsTheRawInsertUses(string column)
    {
        Ddl.Should().Contain(column);
    }

    [Fact]
    public void Model_Readings_HasPartialIndexForDueAlerts()
    {
        Ddl.Should().Contain("CREATE INDEX ix_readings_alert_due ON readings (\"Id\") WHERE \"Alert\" <> 'None' AND \"AlertSentAt\" IS NULL;");
    }

    [Fact]
    public void Model_VendorId_IsUniqueForOnConflict()
    {
        Ddl.Should().Contain("CREATE UNIQUE INDEX \"IX_readings_VendorId\" ON readings (\"VendorId\");");
    }

    [Theory]
    [InlineData("FOREIGN KEY (\"PatientId\") REFERENCES patients (\"Id\") ON DELETE RESTRICT")]
    [InlineData("FOREIGN KEY (\"MeterId\") REFERENCES meters (\"Id\") ON DELETE RESTRICT")]
    public void Model_Readings_HasRestrictForeignKeys(string foreignKey)
    {
        Ddl.Should().Contain(foreignKey);
    }

    [Fact]
    public void Model_EveryStringColumn_IsBounded()
    {
        Ddl.Should().NotContain(" text").And.NotContain("character varying NOT NULL").And.NotContain("character varying,");
    }

    private static string CreateScript()
    {
        var options = new DbContextOptionsBuilder<SyncDbContext>().UseNpgsql("Host=unused").Options;
        using var db = new SyncDbContext(options);
        return db.Database.GenerateCreateScript();
    }
}
