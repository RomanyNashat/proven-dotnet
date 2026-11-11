using FluentAssertions;
using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MeterSync.Tests;

// MeterSyncService inserts readings with hand-written SQL; these names must match the model.
public sealed class SyncDbContextModelTests
{
    [Fact]
    public void Readings_RawInsertColumns_MatchTheModel()
    {
        using var db = new SyncDbContext(new DbContextOptionsBuilder<SyncDbContext>()
            .UseNpgsql("Host=unused").Options);
        var entity = db.Model.FindEntityType(typeof(Reading))!;
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());

        string Column(string property) => entity.FindProperty(property)!.GetColumnName(table)!;

        entity.GetTableName().Should().Be("readings");
        new[] { "PatientId", "MeterId", "VendorId", "Raw", "Unit", "ValueMgDl", "TakenAt", "Alert", "AlertSentAt", "AlertClaimedUntil" }
            .Select(Column)
            .Should().Equal("PatientId", "MeterId", "VendorId", "Raw", "Unit", "ValueMgDl", "TakenAt", "Alert", "AlertSentAt", "AlertClaimedUntil");
    }

    [Fact]
    public void Model_EveryStringProperty_IsBounded()
    {
        using var db = new SyncDbContext(new DbContextOptionsBuilder<SyncDbContext>()
            .UseNpgsql("Host=unused").Options);

        db.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(string) || p.GetProviderClrType() == typeof(string))
            .Should().OnlyContain(p => p.GetMaxLength() != null);
    }
}
