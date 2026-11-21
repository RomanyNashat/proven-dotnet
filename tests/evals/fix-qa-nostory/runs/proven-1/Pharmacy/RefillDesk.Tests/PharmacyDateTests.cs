using FluentAssertions;
using static RefillDesk.Tests.DeskFixture;

namespace RefillDesk.Tests;

[Trait("AC", "AC9")]
public sealed class PharmacyDateTests
{
    private static readonly DateOnly Nov20 = new(2026, 11, 20);
    private static readonly DateOnly Nov21 = new(2026, 11, 21);

    public static TheoryData<DateTimeOffset, DateOnly> Instants() => new()
    {
        { new DateTimeOffset(2026, 11, 19, 21, 0, 0, TimeSpan.Zero), Nov20 },               // Riyadh midnight
        { new DateTimeOffset(2026, 11, 19, 21, 0, 0, TimeSpan.Zero).AddTicks(-1), new DateOnly(2026, 11, 19) },
        { new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero), Nov20 },                 // UTC midnight: no flip
        { new DateTimeOffset(2026, 11, 20, 20, 59, 0, TimeSpan.Zero), Nov20 },
        { LastTickOfTheDay, Nov20 },
        { RiyadhMidnight, Nov21 },
        { new DateTimeOffset(2026, 11, 20, 21, 1, 0, TimeSpan.Zero), Nov21 },
        { new DateTimeOffset(2026, 11, 20, 23, 59, 59, TimeSpan.Zero), Nov21 },
        { new DateTimeOffset(2026, 12, 31, 21, 0, 0, TimeSpan.Zero), new DateOnly(2027, 1, 1) },
        { new DateTimeOffset(2026, 11, 21, 2, 0, 0, TimeSpan.FromHours(5)), Nov21 },          // 21:00 UTC, given in +05:00
    };

    [Theory]
    [MemberData(nameof(Instants))]
    [Trait("EC", "EC33")]
    public void Today_AroundRiyadhMidnight_IsTheRiyadhDate(DateTimeOffset now, DateOnly expected)
    {
        var f = new DeskFixture(now);

        f.Desk.Today.Should().Be(expected);
    }

    [Fact]
    [Trait("EC", "EC33")]
    public void Today_ClockAdvancesPastRiyadhMidnight_IsReadLive()
    {
        var f = new DeskFixture(LastTickOfTheDay);
        f.Desk.Today.Should().Be(Nov20);

        f.Time.Advance(TimeSpan.FromTicks(1));

        f.Desk.Today.Should().Be(Nov21);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(14)]
    [Trait("EC", "EC34")]
    public void Today_TimeProviderInAnotherLocalZone_IsStillTheRiyadhDate(int localOffsetHours)
    {
        var f = new DeskFixture(RiyadhMidnight.AddHours(-1)); // 20:00 UTC, 23:00 Riyadh
        f.Time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone(
            $"test{localOffsetHours}", TimeSpan.FromHours(localOffsetHours), "test", "test"));

        f.Desk.Today.Should().Be(Nov20);
    }

    [Theory]
    [InlineData(false, RefillRefusal.None)]
    [InlineData(true, RefillRefusal.Expired)]
    [Trait("EC", "EC35")]
    public void Request_ExpiresOnTheRiyadhDay_FlipsToExpiredAt2100Utc(bool afterMidnight, RefillRefusal expected)
    {
        var f = new DeskFixture(afterMidnight ? RiyadhMidnight : LastTickOfTheDay);
        f.Add(Rx(expiresOn: Nov20));

        f.Desk.Request(PatientId, 1).Refusal.Should().Be(expected);
    }

    [Theory]
    [InlineData(false, RefillRefusal.None)]
    [InlineData(true, RefillRefusal.NotYourPrescription)]
    [Trait("EC", "EC36")]
    public void Request_DelegationEndingOnTheRiyadhDay_EndsAt2100Utc(bool afterMidnight, RefillRefusal expected)
    {
        var f = new DeskFixture(afterMidnight ? RiyadhMidnight : LastTickOfTheDay);
        f.Add(Rx());
        f.Delegate(CaregiverId, PatientId, Nov20.AddDays(-3), Nov20);

        f.Desk.Request(CaregiverId, 1).Refusal.Should().Be(expected);
    }

    [Theory]
    [InlineData(false, RefillRefusal.NotYourPrescription)]
    [InlineData(true, RefillRefusal.None)]
    [Trait("EC", "EC36")]
    public void Request_DelegationStartingNextRiyadhDay_StartsAt2100Utc(bool afterMidnight, RefillRefusal expected)
    {
        var f = new DeskFixture(afterMidnight ? RiyadhMidnight : LastTickOfTheDay);
        f.Add(Rx());
        f.Delegate(CaregiverId, PatientId, Nov21, Nov21.AddDays(3));

        f.Desk.Request(CaregiverId, 1).Refusal.Should().Be(expected);
    }

    [Theory]
    [InlineData(false, RefillRefusal.TooEarly)]
    [InlineData(true, RefillRefusal.None)]
    [Trait("EC", "EC37")]
    public void Request_WindowOpeningNextRiyadhDay_OpensAt2100Utc(bool afterMidnight, RefillRefusal expected)
    {
        // runsOut = 22 Oct + 37 = 28 Nov; the window opens on 21 Nov.
        var f = new DeskFixture(afterMidnight ? RiyadhMidnight : LastTickOfTheDay);
        f.Add(Rx(lastDispensedOn: new DateOnly(2026, 10, 22), daysSupply: 37));

        f.Desk.Request(PatientId, 1).Refusal.Should().Be(expected);
    }

    [Fact]
    [Trait("EC", "EC35")]
    public void Request_StoryTest_PatientAsksJustAfterRiyadhMidnightOnTheDayAfterExpiry_IsExpired()
    {
        // Given a prescription that expires on 20 Nov, and a patient asking at 21:30 UTC on 20 Nov,
        // which is 00:30 on 21 Nov in Riyadh
        var now = new DateTimeOffset(2026, 11, 20, 21, 30, 0, TimeSpan.Zero);
        now.ToOffset(Desk.PharmacyOffset).Day.Should().Be(21);
        var f = new DeskFixture(now);
        f.Add(Rx(expiresOn: Nov20));

        // When they request a refill
        var result = f.Desk.Request(PatientId, 1);

        // Then it is the 21st at the pharmacy, so the prescription has expired.
        // Counting the day in UTC would still say the 20th and accept it.
        result.Should().Be(new RefillResult(RefillRefusal.Expired, null));
    }

    [Theory]
    [InlineData(false, 2026, 11, 20)]
    [InlineData(true, 2026, 11, 21)]
    [Trait("AC", "AC11"), Trait("EC", "EC38")]
    public void Move_DispensedAroundRiyadhMidnight_WritesTheRiyadhDate(bool afterMidnight, int y, int m, int d)
    {
        var f = new DeskFixture();
        f.Add(Rx());
        var request = f.Reach(RefillStatus.Approved);
        f.Time.SetUtcNow(afterMidnight ? RiyadhMidnight : LastTickOfTheDay);

        f.Desk.Move(request.Id, RefillStatus.Dispensed).Should().BeTrue();

        f.Inner.FindPrescription(1)!.LastDispensedOn.Should().Be(new DateOnly(y, m, d));
    }
}
