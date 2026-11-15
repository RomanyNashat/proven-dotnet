using FluentAssertions;
using static RefillDesk.Tests.TestPharmacy;

namespace RefillDesk.Tests;

/// <summary>
/// AC8: every day is a Riyadh day (UTC+3). 20 Nov 20:59:59 UTC is still 20 Nov in Riyadh;
/// 20 Nov 21:00:00 UTC is already 21 Nov.
/// </summary>
public sealed class PharmacyDayTests
{
    private static readonly DateTimeOffset LastSecondOf20Nov = Utc(2026, 11, 20, 20, 59, 59);
    private static readonly DateTimeOffset FirstSecondOf21Nov = Utc(2026, 11, 20, 21, 0, 0);
    private static readonly DateOnly Nov20 = new(2026, 11, 20);
    private static readonly DateOnly Nov21 = new(2026, 11, 21);

    public static TheoryData<DateTimeOffset, DateOnly> Instants() => new()
    {
        { Utc(2026, 11, 20, 0, 0), Nov20 },
        { Utc(2026, 11, 20, 12, 0), Nov20 },
        { Utc(2026, 11, 20, 20, 0), Nov20 },
        { LastSecondOf20Nov, Nov20 },
        { FirstSecondOf21Nov, Nov21 },
        { Utc(2026, 11, 20, 23, 30), Nov21 },
        { Utc(2026, 12, 31, 21, 0), new DateOnly(2027, 1, 1) },
        { new DateTimeOffset(2026, 11, 21, 1, 0, 0, TimeSpan.FromHours(5)), Nov20 },
    };

    [Theory]
    [MemberData(nameof(Instants))]
    public void Today_IsTheRiyadhDate(DateTimeOffset now, DateOnly expected)
    {
        var pharmacy = At(now);

        pharmacy.Desk.Today.Should().Be(expected);
    }

    [Fact]
    public void Request_ExpiresOn20NovAtLastRiyadhSecondOfThatDay_IsAccepted()
    {
        var pharmacy = At(LastSecondOf20Nov);
        pharmacy.Add(Rx(expiresOn: Nov20));

        pharmacy.Request().Refusal.Should().Be(RefillRefusal.None);
    }

    [Fact]
    public void Request_ExpiresOn20NovOnceRiyadhIsOn21NovButUtcIsNot_IsRefusedAsExpired()
    {
        var pharmacy = At(FirstSecondOf21Nov);
        pharmacy.Add(Rx(expiresOn: Nov20));

        pharmacy.Request().Refusal.Should().Be(RefillRefusal.Expired);
    }

    [Fact]
    public void Request_WindowOpens21NovAtLastRiyadhSecondOf20Nov_IsRefusedAsTooEarly()
    {
        var pharmacy = At(LastSecondOf20Nov);
        pharmacy.Add(Rx(lastDispensedOn: new DateOnly(2026, 10, 29), daysSupply: 30)); // runs out 28 Nov

        pharmacy.Request().Refusal.Should().Be(RefillRefusal.TooEarly);
    }

    [Fact]
    public void Request_WindowOpens21NovOnceRiyadhIsOn21NovButUtcIsNot_IsAccepted()
    {
        var pharmacy = At(FirstSecondOf21Nov);
        pharmacy.Add(Rx(lastDispensedOn: new DateOnly(2026, 10, 29), daysSupply: 30));

        pharmacy.Request().Refusal.Should().Be(RefillRefusal.None);
    }

    [Theory]
    [InlineData(false, RefillRefusal.NotYourPrescription)]
    [InlineData(true, RefillRefusal.None)]
    public void Request_DelegationStarting21Nov_FollowsTheRiyadhDay(bool riyadhIs21Nov, RefillRefusal expected)
    {
        var pharmacy = At(riyadhIs21Nov ? FirstSecondOf21Nov : LastSecondOf20Nov);
        pharmacy.Add(Rx());
        pharmacy.Delegate(CaregiverId, PatientId, Nov21, Nov21.AddDays(30));

        pharmacy.Request(CaregiverId).Refusal.Should().Be(expected);
    }

    [Theory]
    [InlineData(false, RefillRefusal.None)]
    [InlineData(true, RefillRefusal.NotYourPrescription)]
    public void Request_DelegationEnding20Nov_FollowsTheRiyadhDay(bool riyadhIs21Nov, RefillRefusal expected)
    {
        var pharmacy = At(riyadhIs21Nov ? FirstSecondOf21Nov : LastSecondOf20Nov);
        pharmacy.Add(Rx());
        pharmacy.Delegate(CaregiverId, PatientId, Nov20.AddDays(-30), Nov20);

        pharmacy.Request(CaregiverId).Refusal.Should().Be(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Move_ToDispensed_RecordsTheRiyadhDayAsLastDispensingDate(bool riyadhIs21Nov)
    {
        var pharmacy = new TestPharmacy();
        pharmacy.Add(Rx());
        var request = pharmacy.Request().Request!;
        pharmacy.Desk.Move(request.Id, RefillStatus.Approved);
        pharmacy.Time.SetUtcNow(riyadhIs21Nov ? FirstSecondOf21Nov : LastSecondOf20Nov);

        pharmacy.Desk.Move(request.Id, RefillStatus.Dispensed);

        pharmacy.Memory.FindPrescription(1)!.LastDispensedOn.Should().Be(riyadhIs21Nov ? Nov21 : Nov20);
    }
}
