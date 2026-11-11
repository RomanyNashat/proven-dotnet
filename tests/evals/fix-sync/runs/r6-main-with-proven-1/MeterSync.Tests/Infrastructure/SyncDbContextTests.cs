using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MeterSync.Tests.Infrastructure;

/// <summary>Inspects the EF model only; nothing connects to a database.</summary>
public sealed class SyncDbContextTests
{
    private static IModel DesignModel()
    {
        var options = new DbContextOptionsBuilder<SyncDbContext>().UseNpgsql("Host=x").Options;
        using var db = new SyncDbContext(options);
        return db.GetService<IDesignTimeModel>().Model;
    }

    [Theory]
    [InlineData(typeof(Meter), nameof(Meter.PatientId), typeof(Patient))]
    [InlineData(typeof(Reading), nameof(Reading.MeterId), typeof(Meter))]
    [InlineData(typeof(Reading), nameof(Reading.PatientId), typeof(Patient))]
    public void Model_ForeignKey_ExistsAndRestrictsDelete(Type dependent, string property, Type principal)
    {
        var fk = DesignModel().FindEntityType(dependent)!.GetForeignKeys()
            .SingleOrDefault(k => k.Properties.Single().Name == property);

        fk.Should().NotBeNull();
        fk!.PrincipalEntityType.ClrType.Should().Be(principal);
        fk.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);
    }

    [Fact]
    public void Model_DueAlertIndex_IsPartialOnId()
    {
        var readings = DesignModel().FindEntityType(typeof(Reading))!;

        var index = readings.GetIndexes().SingleOrDefault(i => i.GetFilter() is not null);

        index.Should().NotBeNull();
        index!.Properties.Select(p => p.Name).Should().Equal(nameof(Reading.Id));
        index.GetFilter().Should().Be("\"Alert\" <> 'None' AND \"AlertSentAt\" IS NULL");
        readings.GetIndexes().Should().NotContain(i => i.Properties.Any(p => p.Name == nameof(Reading.AlertSentAt)));
    }

    [Fact]
    public void Model_AlertFailures_IsMappedWithDefaultZero()
    {
        var property = DesignModel().FindEntityType(typeof(Reading))!.FindProperty(nameof(Reading.AlertFailures));

        property.Should().NotBeNull();
        var mapped = property!;
        mapped.GetColumnName().Should().Be("AlertFailures");
        var defaultValue = mapped.FindAnnotation(RelationalAnnotationNames.DefaultValue);
        defaultValue.Should().NotBeNull("existing rows and raw inserts get 0");
        defaultValue!.Value.Should().Be(0);
    }

    [Theory]
    [InlineData(typeof(Patient))]
    [InlineData(typeof(Meter))]
    [InlineData(typeof(Reading))]
    public void Model_Key_IsIdentityAlways(Type entity)
    {
        var id = DesignModel().FindEntityType(entity)!.FindPrimaryKey()!.Properties.Single();

        id.GetValueGenerationStrategy().Should().Be(Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityAlwaysColumn);
    }

    [Fact]
    public void Model_EveryStringColumn_IsBounded()
    {
        var unbounded = DesignModel().GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(string) && p.GetMaxLength() is null)
            .Select(p => $"{p.DeclaringType.ClrType.Name}.{p.Name}");

        unbounded.Should().BeEmpty();
    }

    [Fact]
    public void Model_ReadingsColumnsUsedByRawSql_HaveExpectedNames()
    {
        var readings = DesignModel().FindEntityType(typeof(Reading))!;

        readings.GetTableName().Should().Be("readings");
        readings.FindProperty(nameof(Reading.Alert))!.GetProviderClrType().Should().Be(typeof(string));
        readings.FindProperty(nameof(Reading.AlertSentAt))!.GetColumnName().Should().Be("AlertSentAt");
        readings.FindProperty(nameof(Reading.Id))!.GetColumnName().Should().Be("Id");
    }
}
