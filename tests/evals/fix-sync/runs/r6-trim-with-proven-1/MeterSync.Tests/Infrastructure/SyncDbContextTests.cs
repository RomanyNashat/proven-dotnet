using System.Text.RegularExpressions;
using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MeterSync.Tests.Infrastructure;

/// <summary>
/// Reads the model and the DDL EF would generate; nothing connects to a database. Whether PostgreSQL accepts
/// the DDL is only proven by the integration tests that create the schema from the model.
/// </summary>
public sealed partial class SyncDbContextTests : IDisposable
{
    private readonly SyncDbContext _db = new(new DbContextOptionsBuilder<SyncDbContext>()
        .UseNpgsql("Host=unused;Database=unused").Options);

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData(typeof(Reading), nameof(Reading.MeterId), typeof(Meter))]
    [InlineData(typeof(Reading), nameof(Reading.PatientId), typeof(Patient))]
    [InlineData(typeof(Meter), nameof(Meter.PatientId), typeof(Patient))]
    public void Model_ForeignKey_ExistsWithRestrictDelete(Type dependent, string property, Type principal)
    {
        var fk = Entity(dependent).GetForeignKeys().SingleOrDefault(f => f.Properties.Single().Name == property);

        fk.Should().NotBeNull();
        fk!.PrincipalEntityType.ClrType.Should().Be(principal);
        fk.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);
    }

    [Fact]
    public void Model_Readings_HaveMeterIdTakenAtIndex()
    {
        Entity(typeof(Reading)).GetIndexes()
            .Should().Contain(i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(Reading.MeterId), nameof(Reading.TakenAt) }));
    }

    [Fact]
    public void Model_AlertIndex_IsPartialAndNamesRealColumns()
    {
        var readings = Entity(typeof(Reading));
        var table = StoreObjectIdentifier.Table("readings");
        var columns = readings.GetProperties().Select(p => p.GetColumnName(table)).ToHashSet();

        var filter = readings.GetIndexes().Select(i => i.GetFilter()).SingleOrDefault(f => f is not null);

        filter.Should().NotBeNull();
        var quoted = QuotedIdentifier().Matches(filter!).Select(m => m.Groups[1].Value).ToList();
        quoted.Should().Contain(nameof(Reading.AlertSentAt)).And.OnlyContain(c => columns.Contains(c));
    }

    [Fact]
    public void Model_AlertAndSentIndex_IsReplaced()
    {
        Entity(typeof(Reading)).GetIndexes()
            .Should().NotContain(i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(Reading.Alert), nameof(Reading.AlertSentAt) }));
    }

    [Fact]
    public void Model_EveryString_IsBoundedAtMost4000()
    {
        var strings = _db.Model.GetEntityTypes().SelectMany(e => e.GetProperties()).Where(p => p.ClrType == typeof(string)).ToList();

        strings.Should().OnlyContain(p => p.GetMaxLength() != null && p.GetMaxLength() <= 4000);
    }

    [Fact]
    public void Model_EveryTimestamp_IsTimestamptz()
    {
        var timestamps = _db.Model.GetEntityTypes().SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)).ToList();

        timestamps.Should().Contain(p => p.Name == nameof(Reading.AlertClaimedUntil));
        timestamps.Should().OnlyContain(p => p.GetColumnType() == "timestamptz");
    }

    [Fact]
    public void CreateScript_ContainsPartialIndexAndForeignKeys()
    {
        var script = _db.Database.GenerateCreateScript();

        script.Should().Contain("WHERE \"AlertSentAt\" IS NULL")
            .And.Contain("REFERENCES meters (\"Id\") ON DELETE RESTRICT")
            .And.Contain("REFERENCES patients (\"Id\") ON DELETE RESTRICT")
            .And.Contain("\"AlertAttempts\" integer NOT NULL");
    }

    [GeneratedRegex("\"([^\"]+)\"")]
    private static partial Regex QuotedIdentifier();

    private IEntityType Entity(Type type) => _db.Model.FindEntityType(type)!;
}
