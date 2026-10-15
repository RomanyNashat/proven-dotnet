using Moq;
using Xunit;

namespace SkillSamples.Tdd;

public sealed class TestSmellTests
{
    [Fact]
    public async Task DeadSetup_ACopiedSetupTheCodeNeverReaches_IsShownByVerifyAll()
    {
        // A setup copied from the happy-path test: "the slot is free". This path never asks, because the
        // slot doesn't exist, so the setup only tells the reader something untrue about the test.
        var slots = new Mock<ISlotRepository>();
        slots.Setup(s => s.GetAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync((Slot?)null);
        var bookings = new Mock<IBookingRepository>();
        bookings.Setup(b => b.IsTakenAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var handler = new BookSlotHandler(slots.Object, bookings.Object, Mock.Of<IUnitOfWork>(), TimeProvider.System);

        var outcome = await handler.Handle(new BookSlot(PatientId: 7, SlotId: 99), CancellationToken.None);

        Assert.Equal(BookSlotOutcome.SlotNotFound, outcome);
        slots.VerifyAll();                                     // used: passes
        Assert.Throws<MockException>(() => bookings.VerifyAll());   // never reached: delete that setup
    }
}
