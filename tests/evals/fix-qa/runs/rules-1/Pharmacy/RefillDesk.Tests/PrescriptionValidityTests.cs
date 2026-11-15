using FluentAssertions;
using static RefillDesk.Tests.TestPharmacy;

namespace RefillDesk.Tests;

/// <summary>AC2: on or before the expiry date, with refills left.</summary>
public sealed class PrescriptionValidityTests
{
    [Theory]
    [InlineData(0)] // expires today
    [InlineData(1)]
    [InlineData(200)]
    public void Request_OnOrBeforeExpiryDate_IsAccepted(int daysUntilExpiry)
    {
        var pharmacy = new TestPharmacy();
        pharmacy.Add(Rx(expiresOn: DefaultDay.AddDays(daysUntilExpiry)));

        var result = pharmacy.Request();

        result.Refusal.Should().Be(RefillRefusal.None);
    }

    [Theory]
    [InlineData(1)] // expired yesterday
    [InlineData(100)]
    public void Request_AfterExpiryDate_IsRefusedAsExpired(int daysSinceExpiry)
    {
        var pharmacy = new TestPharmacy();
        pharmacy.Add(Rx(expiresOn: DefaultDay.AddDays(-daysSinceExpiry)));

        var result = pharmacy.Request();

        result.Should().BeEquivalentTo(new { Ok = false, Refusal = RefillRefusal.Expired, Request = (RefillRequest?)null });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void Request_WithRefillsLeft_IsAccepted(int refillsLeft)
    {
        var pharmacy = new TestPharmacy();
        pharmacy.Add(Rx(refillsLeft: refillsLeft));

        var result = pharmacy.Request();

        result.Refusal.Should().Be(RefillRefusal.None);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Request_NoRefillsLeft_IsRefusedAsNoRefillsLeft(int refillsLeft)
    {
        var pharmacy = new TestPharmacy();
        pharmacy.Add(Rx(refillsLeft: refillsLeft));

        var result = pharmacy.Request();

        result.Should().BeEquivalentTo(new { Ok = false, Refusal = RefillRefusal.NoRefillsLeft, Request = (RefillRequest?)null });
    }

    [Fact]
    public void Request_CaregiverOnExpiredPrescription_IsRefusedAsExpired()
    {
        var pharmacy = new TestPharmacy();
        pharmacy.Add(Rx(expiresOn: DefaultDay.AddDays(-1)));
        pharmacy.Delegate(CaregiverId, PatientId, DefaultDay.AddDays(-30), DefaultDay.AddDays(30));

        var result = pharmacy.Request(CaregiverId);

        result.Refusal.Should().Be(RefillRefusal.Expired);
    }
}
