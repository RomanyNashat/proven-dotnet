using System.Data;
using System.Data.Common;
using System.Net;
using MeterSync.Application;
using MeterSync.Infrastructure;
using MeterSync.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace MeterSync.Tests.Application;

public sealed class AlertSweeperTests
{
    private const string Phone = "+966500000001";
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly FakeLogger<AlertSweeper> _logger = new();
    private readonly StubHandler _sms = new() { Body = "{}" };

    private static bool IsClaim(CapturedCommand c) => c.CommandText.Contains("\"AlertAttempts\" = ", StringComparison.Ordinal);

    private static bool IsMark(CapturedCommand c) => c.CommandText.Contains("SET \"AlertSentAt\" = ", StringComparison.Ordinal);

    // Only the batch query orders; the per-reading lookup (SingleAsync) also has a LIMIT.
    private static bool IsCandidates(CapturedCommand c) => c.CommandText.Contains("ORDER BY", StringComparison.Ordinal);

    private static IEnumerable<object?> Values(CapturedCommand c) => c.ParameterValues;

    private static CapturingInterceptor Db(int[] candidates, int claimRows, string? phone = Phone) =>
        new(c => IsClaim(c) ? claimRows : 1, c => IsCandidates(c)
            ? Table.Of(candidates)
            : Table.Row("High", "HI", "mg/dL", Now.AddHours(-1), phone));

    private AlertSweeper Sweeper(SyncDbContext db) =>
        new(db, new SmsClient(new HttpClient(_sms) { BaseAddress = new Uri("https://sms.example.net/") }), _time, _logger);

    [Fact]
    public async Task RunOnceAsync_ClaimWon_SendsOnceAndMarksSent()
    {
        var capture = Db([5], claimRows: 1);
        await using var db = TestDb.Create(capture);

        var sent = await Sweeper(db).RunOnceAsync(CancellationToken.None);

        sent.Should().Be(1);
        _sms.Requests.Should().ContainSingle();
        capture.Commands.Should().ContainSingle(c => IsMark(c));
    }

    [Fact]
    public async Task RunOnceAsync_ClaimLostToAnotherInstance_DoesNotSend()
    {
        var capture = Db([5], claimRows: 0);
        await using var db = TestDb.Create(capture);

        var sent = await Sweeper(db).RunOnceAsync(CancellationToken.None);

        sent.Should().Be(0);
        _sms.Requests.Should().BeEmpty();
        capture.Commands.Should().NotContain(c => IsMark(c));
    }

    [Fact]
    public async Task RunOnceAsync_SmsFails_LeavesTheLeaseAndCarriesOnWithoutLeakingThePhone()
    {
        _sms.Status = HttpStatusCode.InternalServerError;
        var capture = Db([5, 6], claimRows: 1);
        await using var db = TestDb.Create(capture);

        var sent = await Sweeper(db).RunOnceAsync(CancellationToken.None);

        sent.Should().Be(0);
        _sms.Requests.Should().HaveCount(2);
        capture.Commands.Should().NotContain(c => IsMark(c));
        _logger.Collector.GetSnapshot().Should().NotBeEmpty()
            .And.OnlyContain(l => !l.Message.Contains(Phone) && !l.Message.Contains("HI"));
    }

    [Fact]
    public async Task RunOnceAsync_PatientHasNoPhone_SkipsWithoutThrowing()
    {
        var capture = Db([5], claimRows: 1, phone: null);
        await using var db = TestDb.Create(capture);

        var sent = await Sweeper(db).RunOnceAsync(CancellationToken.None);

        sent.Should().Be(0);
        _sms.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_Candidates_AreDueUnclaimedUnparkedAndInsideTheWindow()
    {
        var capture = Db([], claimRows: 1);
        await using var db = TestDb.Create(capture);

        await Sweeper(db).RunOnceAsync(CancellationToken.None);

        var query = capture.Commands.Should().ContainSingle().Subject;
        query.CommandText.Should().Contain("\"AlertSentAt\" IS NULL").And.Contain("\"AlertClaimedUntil\"")
            .And.Contain("\"AlertAttempts\" < 5").And.Contain("ORDER BY");
        Values(query).Should().Contain(Now - TimeSpan.FromHours(12)).And.Contain(Now);
        (query.CommandText.Contains("LIMIT 100", StringComparison.Ordinal) || Values(query).Contains(100)).Should().BeTrue();
    }

    [Fact]
    public async Task RunOnceAsync_Claim_SetsAFiveMinuteLeaseUnderTheSameConditions()
    {
        var capture = Db([5], claimRows: 1);
        await using var db = TestDb.Create(capture);

        await Sweeper(db).RunOnceAsync(CancellationToken.None);

        var claim = capture.Commands.Single(c => IsClaim(c));
        claim.CommandText.Should().Contain("\"AlertAttempts\" < 5").And.Contain("\"AlertSentAt\" IS NULL");
        Values(claim).Should().Contain(Now.AddMinutes(5)).And.Contain(Now - TimeSpan.FromHours(12)).And.Contain(5);
    }
}
