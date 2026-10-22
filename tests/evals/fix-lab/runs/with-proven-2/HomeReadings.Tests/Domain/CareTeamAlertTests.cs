using HomeReadings.Domain;

namespace HomeReadings.Tests.Domain;

public sealed class CareTeamAlertTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 6, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RecordFailure_TwoFailures_CountsBothAndKeepsTheLastError()
    {
        var alert = new CareTeamAlert(7, Now);

        alert.RecordFailure("HttpRequestException");
        alert.RecordFailure("TimeoutRejectedException");

        alert.Attempts.Should().Be(2);
        alert.LastError.Should().Be("TimeoutRejectedException");
        alert.SentAt.Should().BeNull();
        alert.ClaimedUntil.Should().BeNull();
    }

    [Fact]
    public void RecordFailure_ErrorLongerThanTheColumn_IsCutToFit()
    {
        var alert = new CareTeamAlert(7, Now);

        alert.RecordFailure(new string('x', 300));

        alert.LastError.Should().HaveLength(CareTeamAlert.LastErrorMaxLength);
    }

    [Fact]
    public void MarkSent_AfterAFailure_IsSentAndReleased()
    {
        var alert = new CareTeamAlert(7, Now);
        alert.RecordFailure("HttpRequestException");

        alert.MarkSent(Now.AddMinutes(1));

        alert.SentAt.Should().Be(Now.AddMinutes(1));
        alert.ClaimedUntil.Should().BeNull();
    }
}
