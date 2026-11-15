using FluentAssertions;
using static RefillDesk.Tests.TestPharmacy;

namespace RefillDesk.Tests;

/// <summary>AC3: supply runs out on last dispensing date + days' supply; requests open 7 days before that.</summary>
public sealed class RefillWindowTests
{
    private static readonly DateOnly LastDispensed = new(2026, 10, 1);

    [Theory]
    [InlineData(30, -7)] // first day of the window
    [InlineData(30, -6)]
    [InlineData(30, 0)] // the day it runs out
    [InlineData(30, 10)] // after it ran out
    [InlineData(90, -7)]
    [InlineData(28, -7)]
    [InlineData(7, -7)] // window opens on the dispensing day itself
    public void Request_FromSevenDaysBeforeRunOutOnwards_IsAccepted(int daysSupply, int daysFromRunOut)
    {
        var pharmacy = OnRiyadhDay(LastDispensed.AddDays(daysSupply + daysFromRunOut));
        pharmacy.Add(Rx(lastDispensedOn: LastDispensed, daysSupply: daysSupply, expiresOn: new DateOnly(2028, 1, 1)));

        var result = pharmacy.Request();

        result.Refusal.Should().Be(RefillRefusal.None);
    }

    [Theory]
    [InlineData(30, -8)] // the day before the window opens
    [InlineData(30, -20)]
    [InlineData(90, -8)]
    [InlineData(90, -30)]
    [InlineData(28, -8)]
    [InlineData(10, -8)]
    public void Request_BeforeSevenDaysBeforeRunOut_IsRefusedAsTooEarly(int daysSupply, int daysFromRunOut)
    {
        var pharmacy = OnRiyadhDay(LastDispensed.AddDays(daysSupply + daysFromRunOut));
        pharmacy.Add(Rx(lastDispensedOn: LastDispensed, daysSupply: daysSupply, expiresOn: new DateOnly(2028, 1, 1)));

        var result = pharmacy.Request();

        result.Should().BeEquivalentTo(new { Ok = false, Refusal = RefillRefusal.TooEarly, Request = (RefillRequest?)null });
    }

    [Fact]
    public void Request_SameDayDifferentLastDispensingDate_DecidesByLastDispensingDate()
    {
        var pharmacy = new TestPharmacy();
        // Today 20 Nov. Rx 1 runs out 27 Nov (window 20 Nov); Rx 2 runs out 28 Nov (window 21 Nov).
        pharmacy.Add(Rx(id: 1, lastDispensedOn: new DateOnly(2026, 10, 28), daysSupply: 30));
        pharmacy.Add(Rx(id: 2, lastDispensedOn: new DateOnly(2026, 10, 29), daysSupply: 30));

        var refusals = new[] { pharmacy.Request(prescriptionId: 1).Refusal, pharmacy.Request(prescriptionId: 2).Refusal };

        refusals.Should().Equal(RefillRefusal.None, RefillRefusal.TooEarly);
    }

    [Fact]
    public void Request_CaregiverTooEarly_IsRefusedAsTooEarly()
    {
        var pharmacy = new TestPharmacy();
        pharmacy.Add(Rx(lastDispensedOn: DefaultDay.AddDays(-1), daysSupply: 30));
        pharmacy.Delegate(CaregiverId, PatientId, DefaultDay.AddDays(-30), DefaultDay.AddDays(30));

        var result = pharmacy.Request(CaregiverId);

        result.Refusal.Should().Be(RefillRefusal.TooEarly);
    }
}
