using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace SkillSamples.Tdd;

public sealed class BookingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(Now);

    private static Slot SlotStartingIn(TimeSpan fromNow) => new(42, Now + fromNow);

    [Fact]
    public void Create_FutureSlot_IsBookedAndRaisesBookingCreated()
    {
        // Arrange
        var slot = SlotStartingIn(TimeSpan.FromDays(1));

        // Act
        var booking = Booking.Create(patientId: 7, slot, _time);

        // Assert
        booking.Status.Should().Be(BookingStatus.Booked);
        booking.Events.Should().ContainSingle()
            .Which.Should().Be(new BookingCreated(PatientId: 7, SlotId: 42));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void Create_SlotAlreadyStarted_Throws(int minutesFromNow)
    {
        var act = () => Booking.Create(7, SlotStartingIn(TimeSpan.FromMinutes(minutesFromNow)), _time);

        act.Should().Throw<DomainException>().WithMessage("*already started*");
    }

    [Theory]
    [MemberData(nameof(CancellationWindows))]
    public void Cancel_AroundTheTwoHourCutoff_AllowedOnlyBeforeIt(int minutesBeforeStart, bool allowed)
    {
        var booking = Booking.Create(7, SlotStartingIn(TimeSpan.FromDays(1)), _time);
        _time.SetUtcNow(booking.Slot.StartsAt - TimeSpan.FromMinutes(minutesBeforeStart));

        var act = () => booking.Cancel("Patient asked", _time);

        if (allowed)
        {
            act.Should().NotThrow();
            booking.Events.OfType<BookingCancelled>().Should().ContainSingle()
                .Which.Reason.Should().Be("Patient asked");
        }
        else
        {
            act.Should().Throw<DomainException>().WithMessage("*two hours*");
            booking.Status.Should().Be(BookingStatus.Booked);
        }
    }

    // The edges are the cases that matter: exactly at the cutoff, and one minute inside it.
    public static TheoryData<int, bool> CancellationWindows => new()
    {
        { 180, true },
        { 120, true },
        { 119, false }
    };
}
