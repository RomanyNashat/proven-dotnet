using FluentAssertions;
using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MeterSync.Tests;

// Translation only: nothing here opens a connection.
public sealed class SyncDbContextModelTests
{
    private static SyncDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SyncDbContext>().UseNpgsql("Host=unused;Database=unused").Options);

    [Fact]
    public void GenerateCreateScript_Model_HasBoundedColumnsForeignKeysAndIdentityKeys()
    {
        using var db = NewContext();

        var script = db.Database.GenerateCreateScript();

        script.Should().NotContain(" text").And.NotContain("bytea");
        script.Should().Contain("GENERATED ALWAYS AS IDENTITY");
        script.Should().Contain("FOREIGN KEY (\"PatientId\") REFERENCES patients");
        script.Should().Contain("FOREIGN KEY (\"MeterId\") REFERENCES meters");
        script.Should().Contain("\"AlertClaimedUntil\" timestamptz");
    }

    [Fact]
    public void DueAlertQuery_Translates_ToSql()
    {
        using var db = NewContext();
        var now = DateTimeOffset.UnixEpoch;

        var sql = (from r in db.Readings
                   join p in db.Patients on r.PatientId equals p.Id
                   where r.Alert != AlertKind.None && r.AlertSentAt == null
                         && (r.AlertClaimedUntil == null || r.AlertClaimedUntil < now)
                   orderby r.Id
                   select new { Reading = r, p.Phone })
            .AsNoTracking().Take(100).ToQueryString();

        sql.Should().Contain("JOIN patients").And.Contain("LIMIT");
    }
}
