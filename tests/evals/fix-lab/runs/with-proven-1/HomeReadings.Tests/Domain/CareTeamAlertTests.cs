using HomeReadings.Domain;

namespace HomeReadings.Tests.Domain;

public sealed class CareTeamAlertTests
{
    private static readonly DateTime At = new(2026, 10, 22, 5, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RecordFailure_TwoFailures_CountsBothAndKeepsTheLast()
    {
        var alert = new CareTeamAlert(1, At);

        alert.RecordFailure("HttpRequestException 503", At);
        alert.RecordFailure("TimeoutRejectedException", At.AddMinutes(1));

        alert.Attempts.Should().Be(2);
        alert.LastError.Should().Be("TimeoutRejectedException");
        alert.LastAttemptAt.Should().Be(At.AddMinutes(1));
        alert.SentAt.Should().BeNull();
    }

    [Fact]
    public void RecordFailure_LongerThanColumn_IsCut()
    {
        var alert = new CareTeamAlert(1, At);

        alert.RecordFailure(new string('x', 300), At);

        alert.LastError.Should().HaveLength(CareTeamAlert.LastErrorMaxLength);
    }
}
