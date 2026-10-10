using System.Globalization;
using AutoFixture;
using AutoFixture.AutoMoq;
using AutoFixture.Xunit2;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SkillSamples.Production;
using Xunit;
using Xunit.Sdk;

namespace SkillSamples.Tdd;

// AutoMoq on its own, without the fixed clock AutoMoqDataAttribute adds.
public sealed class PlainAutoMoqDataAttribute() : AutoDataAttribute(() => new Fixture().Customize(new AutoMoqCustomization()));

public sealed class TddStoryTests
{
    [Theory, PlainAutoMoqData]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_AutoMoqBuildsTheClock_TheTestRunsOnTheRealDate(TimeProvider clock)
    {
        // Given AutoMoq builds a TimeProvider, when the code asks for the time,
        // then it gets today's: a test with a fixed slot date passes now and fails once that date is past.
        clock.Should().NotBeOfType<FakeTimeProvider>();
        clock.GetUtcNow().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Theory, AutoMoqData]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_WithTheFixedClock_EveryTestSeesTheSameMoment(TimeProvider clock, FakeTimeProvider fake)
    {
        clock.Should().BeSameAs(fake);
        clock.GetUtcNow().Should().Be(new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_AutoFixtureBuildsTheFakeClockItself_TheClockMovesOnEveryRead()
    {
        // AutoFixture fills public settable properties, AutoAdvanceAmount among them.
        var fake = new Fixture().Create<FakeTimeProvider>();

        fake.AutoAdvanceAmount.Should().BePositive();
        var first = fake.GetUtcNow();
        fake.GetUtcNow().Should().BeAfter(first);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_AChangeMakesTheHandlerSaveTwice_VerifyingTheSideEffectCatchesIt()
    {
        // Given a booking that goes through the handler, and then a second save (the bug)
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.Zero));
        var slots = new Mock<ISlotRepository>();
        slots.Setup(s => s.GetAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(new Slot(42, time.GetUtcNow().AddDays(1)));
        var unitOfWork = new Mock<IUnitOfWork>();
        await new BookSlotHandler(slots.Object, Mock.Of<IBookingRepository>(), unitOfWork.Object, time)
            .Handle(new BookSlot(7, 42), CancellationToken.None);
        await unitOfWork.Object.SaveChangesAsync(CancellationToken.None);

        // When the test verifies the save / Then it fails, naming the count
        var verify = () => unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        verify.Should().Throw<MockException>().WithMessage("*once*2 times*");
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_APackageUpdateOffersFluentAssertions8_ThePinKeepsSeven()
    {
        // 8.0 and later need a paid licence for commercial use; the range in the project file stops the bump.
        typeof(AssertionExtensions).Assembly.GetName().Version!.Major.Should().Be(7);
        var project = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "SkillSamples.Tests.csproj"));
        project.Should().Contain("""Include="FluentAssertions" Version="[7.0.0,8.0.0)" """);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public void Production_NoIcu_ATestThatSetsACultureCrashes_SoTestsDontSetOne()
    {
        ProductionConditions.Require();

        // A test that switches to en-US or ar-SA to make a formatting assertion pass crashes in a CI image
        // without ICU. The domain formats nothing culture-dependent, so its tests need no culture at all.
        var setCulture = () => CultureInfo.CurrentCulture = new CultureInfo("en-US");
        setCulture.Should().Throw<CultureNotFoundException>();

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.Zero));
        var booking = Booking.Create(7, new Slot(42, time.GetUtcNow().AddHours(1)), time);
        var cancel = () => booking.Cancel("Patient asked", time);
        cancel.Should().Throw<DomainException>().WithMessage("A booking can't be cancelled less than two hours before it starts.");
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public void Production_NoIcu_AFailedAssertionStillSaysWhatWentWrong()
    {
        ProductionConditions.Require();

        var total = 45.5m;
        var assert = () => total.Should().Be(45m, "two items at 10 and one at 25");

        assert.Should().Throw<XunitException>().WithMessage("*45M*two items at 10 and one at 25*45.5M*");
    }
}
