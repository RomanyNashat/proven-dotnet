using System.Data;
using System.Data.Common;
using System.Net;
using MeterSync.Application;
using MeterSync.Infrastructure;
using MeterSync.Tests.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace MeterSync.Tests.Application;

public sealed class MeterSyncServiceTests
{
    private const string GoodReadings = """
        [
          { "id": "r-1", "value": "HI", "unit": "mg/dL", "takenAt": "2026-11-02T07:58:00Z" },
          { "id": "r-2", "value": "5.5", "unit": "mmol/L", "takenAt": "2026-11-02T08:58:00Z" }
        ]
        """;

    private readonly FakeLogger<MeterSyncService> _logger = new();

    private static DataTable Meters(params (int Id, int PatientId, string Serial)[] meters)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("PatientId", typeof(int));
        table.Columns.Add("SerialNumber", typeof(string));
        foreach (var m in meters)
        {
            table.Rows.Add(m.Id, m.PatientId, m.Serial);
        }

        return table;
    }

    private static MeterVendorClient Vendor(RoutingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://meters.example.net/v2/") });

    private static List<CapturedCommand> Inserts(CapturingInterceptor capture) =>
        capture.Commands.Where(c => c.CommandText.Contains("INSERT INTO readings", StringComparison.Ordinal)).ToList();

    [Fact]
    public async Task SyncOnceAsync_OneMeterFails_StillSyncsTheOthers()
    {
        var capture = new CapturingInterceptor(_ => 1, _ => Meters((1, 7, "BAD"), (2, 8, "GOOD")));
        await using var db = TestDb.Create(capture);
        var vendor = new RoutingHandler(r => r.RequestUri!.AbsolutePath.Contains("/BAD/", StringComparison.Ordinal)
            ? (HttpStatusCode.InternalServerError, "")
            : (HttpStatusCode.OK, GoodReadings));

        var added = await new MeterSyncService(db, Vendor(vendor), _logger).SyncOnceAsync(CancellationToken.None);

        added.Should().Be(2);
        vendor.Requests.Should().HaveCount(2);
        _logger.Collector.GetSnapshot().Should().Contain(l => l.Level == LogLevel.Error);
    }

    [Fact]
    public async Task SyncOnceAsync_DuplicateReadings_CountsOnlyInsertedRows()
    {
        var capture = new CapturingInterceptor(_ => 0, _ => Meters((2, 8, "GOOD")));
        await using var db = TestDb.Create(capture);

        var added = await new MeterSyncService(db, Vendor(new RoutingHandler(_ => (HttpStatusCode.OK, GoodReadings))), _logger)
            .SyncOnceAsync(CancellationToken.None);

        added.Should().Be(0);
        Inserts(capture).Should().HaveCount(2);
    }

    [Fact]
    public async Task SyncOnceAsync_HiReading_IsStoredAsAHighAlert()
    {
        var capture = new CapturingInterceptor(_ => 1, _ => Meters((2, 8, "GOOD")));
        await using var db = TestDb.Create(capture);

        await new MeterSyncService(db, Vendor(new RoutingHandler(_ => (HttpStatusCode.OK, GoodReadings))), _logger)
            .SyncOnceAsync(CancellationToken.None);

        Inserts(capture)[0].ParameterValues.Should().Contain("High").And.Contain("HI");
    }

    [Theory]
    [InlineData("123.4", "mg/L")]
    [InlineData("1,5", "mg/dL")]
    public async Task SyncOnceAsync_BadReading_IsSkippedWithALogLineWithoutTheValue(string value, string unit)
    {
        var capture = new CapturingInterceptor(_ => 1, _ => Meters((2, 8, "GOOD")));
        await using var db = TestDb.Create(capture);
        var body = $$"""[{ "id": "r-9", "value": "{{value}}", "unit": "{{unit}}", "takenAt": "2026-11-02T07:58:00Z" }]""";

        var added = await new MeterSyncService(db, Vendor(new RoutingHandler(_ => (HttpStatusCode.OK, body))), _logger)
            .SyncOnceAsync(CancellationToken.None);

        added.Should().Be(0);
        Inserts(capture).Should().BeEmpty();
        var log = _logger.Collector.GetSnapshot().Should().ContainSingle(l => l.Level == LogLevel.Warning).Subject;
        log.Message.Should().Contain("r-9").And.NotContain(value);
    }
}
