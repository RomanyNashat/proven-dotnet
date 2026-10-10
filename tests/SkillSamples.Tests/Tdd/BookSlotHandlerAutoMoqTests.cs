using AutoFixture.Xunit2;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace SkillSamples.Tdd;

public sealed class BookSlotHandlerAutoMoqTests
{
    // [Frozen] Mock<T>, not T: the handler gets mock.Object, and the test can set it up and verify it.
    // Frozen parameters come before the one that uses them.
    [Theory, AutoMoqData]
    public async Task Handle_FreeSlot_SavesOnce(
        [Frozen] Mock<ISlotRepository> slots,
        [Frozen] Mock<IUnitOfWork> unitOfWork,
        FakeTimeProvider time,
        BookSlotHandler sut,
        BookSlot command)
    {
        slots.Setup(s => s.GetAsync(command.SlotId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Slot(command.SlotId, time.GetUtcNow().AddDays(1)));

        var outcome = await sut.Handle(command, CancellationToken.None);

        outcome.Should().Be(BookSlotOutcome.Booked);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
