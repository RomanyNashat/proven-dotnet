using System.Text.RegularExpressions;
using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Tests.Infrastructure;

public sealed partial class ReadingStoreTests
{
    private static readonly DateTimeOffset TakenAt = new(2026, 11, 2, 7, 58, 0, TimeSpan.Zero);

    private static Reading[] TwoReadings() =>
    [
        Reading.FromMeter(7, 3, "r-1", "HI", "mg/dL", TakenAt).Reading!,
        Reading.FromMeter(7, 3, "r-2", "5.5", "mmol/L", TakenAt).Reading!,
    ];

    [Fact]
    public async Task InsertIgnoringDuplicatesAsync_Readings_SendsAParameterisedInsertOnConflictDoNothingPerReading()
    {
        var capture = new CapturingInterceptor(rowsAffected: 1);
        await using var db = TestDb.Create(capture);

        await db.InsertIgnoringDuplicatesAsync(TwoReadings(), CancellationToken.None);

        capture.Commands.Should().HaveCount(2);
        string[] vendorIds = ["r-1", "r-2"];
        for (var i = 0; i < vendorIds.Length; i++)
        {
            var command = capture.Commands[i];
            command.CommandText.Should().Contain("ON CONFLICT (\"VendorId\") DO NOTHING");
            command.CommandText.Should().NotContain("r-1").And.NotContain("r-2").And.NotContain("'HI'");
            command.ParameterValues.Should().Contain(vendorIds[i], because: "values travel as parameters");
        }
    }

    [Fact]
    public async Task InsertIgnoringDuplicatesAsync_KnownVendorId_SkipsTheRowBeforeTheIdentityIsDrawn()
    {
        // Every sync re-sends all of a meter's readings. A bare ON CONFLICT still draws an identity value
        // per known row, which would run the int key out; the NOT EXISTS guard stops that.
        var capture = new CapturingInterceptor(rowsAffected: 0);
        await using var db = TestDb.Create(capture);

        await db.InsertIgnoringDuplicatesAsync(TwoReadings()[..1], CancellationToken.None);

        capture.Commands.Single().CommandText.Should().Contain("WHERE NOT EXISTS (SELECT 1 FROM readings WHERE \"VendorId\" = ");
    }

    [Fact]
    public async Task InsertIgnoringDuplicatesAsync_Sql_UsesOnlyNamesFromTheModelsReadingsTable()
    {
        var capture = new CapturingInterceptor(rowsAffected: 2);
        await using var db = TestDb.Create(capture);
        var script = db.Database.GenerateCreateScript();
        var readingsTable = script[script.IndexOf("CREATE TABLE readings (", StringComparison.Ordinal)..];
        readingsTable = readingsTable[..readingsTable.IndexOf(");", StringComparison.Ordinal)];

        await db.InsertIgnoringDuplicatesAsync(TwoReadings(), CancellationToken.None);

        var sql = capture.Commands[0].CommandText;
        sql.Should().Contain("INSERT INTO readings (");
        var columns = InsertColumnList().Match(sql).Groups[1].Value
            .Split(',', StringSplitOptions.TrimEntries);
        columns.Should().NotBeEmpty();
        foreach (var column in columns)
        {
            readingsTable.Should().Contain($"    {column} ", because: $"{column} must be a column of the readings table");
        }

        script.Should().Contain("CREATE UNIQUE INDEX \"IX_readings_VendorId\" ON readings (\"VendorId\");");
    }

    [Fact]
    public async Task InsertIgnoringDuplicatesAsync_OneDuplicate_ReturnsTheSumOfInsertedRows()
    {
        var calls = 0;
        var capture = new CapturingInterceptor(_ => calls++ == 0 ? 1 : 0);
        await using var db = TestDb.Create(capture);

        var inserted = await db.InsertIgnoringDuplicatesAsync(TwoReadings(), CancellationToken.None);

        inserted.Should().Be(1);
    }

    [Fact]
    public async Task InsertIgnoringDuplicatesAsync_NoReadings_SendsNothing()
    {
        var capture = new CapturingInterceptor(rowsAffected: 0);
        await using var db = TestDb.Create(capture);

        var inserted = await db.InsertIgnoringDuplicatesAsync([], CancellationToken.None);

        inserted.Should().Be(0);
        capture.Commands.Should().BeEmpty();
    }

    [GeneratedRegex(@"INSERT INTO readings \(([^)]*)\)")]
    private static partial Regex InsertColumnList();
}
