using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace SkillSamples.Tdd;

public sealed class BookSlotHandlerTests
{
    private readonly Mock<ISlotRepository> _slots = new();
    private readonly Mock<IBookingRepository> _bookings = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.Zero));

    private BookSlotHandler Handler() => new(_slots.Object, _bookings.Object, _unitOfWork.Object, _time);

    private Slot FreeSlotTomorrow(int id)
    {
        var slot = new Slot(id, _time.GetUtcNow().AddDays(1));
        _slots.Setup(s => s.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(slot);
        return slot;
    }

    [Fact]
    public async Task Handle_FreeSlot_AddsTheBookingAndSavesOnce()
    {
        // Arrange
        FreeSlotTomorrow(42);
        Booking? added = null;
        _bookings.Setup(b => b.Add(It.IsAny<Booking>())).Callback<Booking>(b => added = b);

        // Act
        var outcome = await Handler().Handle(new BookSlot(PatientId: 7, SlotId: 42), CancellationToken.None);

        // Assert
        outcome.Should().Be(BookSlotOutcome.Booked);
        added.Should().NotBeNull();
        added!.PatientId.Should().Be(7);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SlotTaken_SavesNothing()
    {
        FreeSlotTomorrow(42);
        _bookings.Setup(b => b.IsTakenAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var outcome = await Handler().Handle(new BookSlot(7, 42), CancellationToken.None);

        outcome.Should().Be(BookSlotOutcome.SlotTaken);
        _bookings.Verify(b => b.Add(It.IsAny<Booking>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownSlot_ReturnsNotFoundWithoutTouchingBookings()
    {
        // A strict mock fails the test on any call that wasn't set up.
        var bookings = new Mock<IBookingRepository>(MockBehavior.Strict);
        var handler = new BookSlotHandler(_slots.Object, bookings.Object, _unitOfWork.Object, _time);

        var outcome = await handler.Handle(new BookSlot(7, SlotId: 404), CancellationToken.None);

        outcome.Should().Be(BookSlotOutcome.SlotNotFound);
    }
}
