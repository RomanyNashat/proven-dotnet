using FluentAssertions;
using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MeterSync.Tests;

// Builds the model and the DDL against the Npgsql provider without opening a connection.
public sealed class ModelTests
{
    [Fact]
    public void Model_CreateScript_HasNoUnboundedOrUntypedColumns()
    {
        using var db = new SyncDbContext(new DbContextOptionsBuilder<SyncDbContext>().UseNpgsql("Host=unused").Options);

        var script = db.Database.GenerateCreateScript();

        script.Should().NotContain(" text").And.NotContain("timestamp without time zone")
            .And.Contain("GENERATED ALWAYS AS IDENTITY").And.Contain("FOREIGN KEY");
    }

    [Fact]
    public void PendingAlertsQuery_Translates()
    {
        using var db = new SyncDbContext(new DbContextOptionsBuilder<SyncDbContext>().UseNpgsql("Host=unused").Options);
        var now = DateTimeOffset.UnixEpoch;

        var sql = db.Readings.Where(r => r.AlertState == AlertState.Pending && (r.AlertClaimedUntil == null || r.AlertClaimedUntil < now))
            .Select(r => r.Id).ToQueryString();

        sql.Should().Contain("AlertState");
    }
}
