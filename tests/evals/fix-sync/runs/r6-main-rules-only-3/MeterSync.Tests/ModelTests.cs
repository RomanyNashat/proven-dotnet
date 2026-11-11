using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Tests;

// GenerateCreateScript needs the provider only, not a server.
public sealed class ModelTests
{
    private static string CreateScript()
    {
        using var db = new SyncDbContext(new DbContextOptionsBuilder<SyncDbContext>().UseNpgsql("Host=unused").Options);
        return db.Database.GenerateCreateScript();
    }

    [Fact]
    public void CreateScript_Always_HasNoUnboundedOrBinaryColumns() =>
        CreateScript().Should().NotContainAny(" text", "bytea", "character varying,", "character varying\n");

    [Fact]
    public void CreateScript_Always_UsesIdentityAlwaysAndForeignKeys() =>
        CreateScript().Should().Contain("GENERATED ALWAYS AS IDENTITY").And.Contain("FOREIGN KEY (\"PatientId\")").And.Contain("FOREIGN KEY (\"MeterId\")");

    [Fact]
    public void CreateScript_ConcurrencyToken_IsTheSystemXminColumn() =>
        CreateScript().Should().NotContain("\"Version\"").And.NotContain("xmin");
}
