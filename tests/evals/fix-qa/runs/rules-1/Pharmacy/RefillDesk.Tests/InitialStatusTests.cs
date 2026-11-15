using FluentAssertions;
using static RefillDesk.Tests.TestPharmacy;

namespace RefillDesk.Tests;

/// <summary>AC5: controlled medicine starts PendingApproval, whoever asks; anything else starts Requested.</summary>
public sealed class InitialStatusTests
{
    [Theory]
    [InlineData(true, PatientId, RefillStatus.PendingApproval)]
    [InlineData(true, CaregiverId, RefillStatus.PendingApproval)]
    [InlineData(false, PatientId, RefillStatus.Requested)]
    [InlineData(false, CaregiverId, RefillStatus.Requested)]
    public void Request_Accepted_StartsInStatusSetByControlledFlag(bool controlled, int requesterId, RefillStatus expected)
    {
        var pharmacy = new TestPharmacy();
        pharmacy.Add(Rx(controlled: controlled));
        pharmacy.Delegate(CaregiverId, PatientId, DefaultDay.AddDays(-30), DefaultDay.AddDays(30));

        var result = pharmacy.Request(requesterId);

        result.Request!.Status.Should().Be(expected);
        pharmacy.StatusOf(result.Request.Id).Should().Be(expected);
    }
}
