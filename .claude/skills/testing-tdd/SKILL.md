---
name: testing-tdd
description: TDD for .NET — red-green-refactor, xUnit + Moq + FluentAssertions 7 + AutoFixture, AAA, naming, theories at the edges, verifying the side effects that matter, a fixed clock. Test behaviour, not implementation. Tested in CI, also under slim-image conditions.
version: 2.0.0
---

# TDD & Unit Testing Patterns

## The default setup

Where your team's own standard differs, the team's standard wins.

- **Stack:** xUnit + **Moq or NSubstitute** (whichever the codebase uses; never both in one test
  project) + FluentAssertions **7.x only, never 8+** + AutoFixture + Coverlet. The samples here use Moq.
- **FluentAssertions pin:** 7.x is the last Apache-2.0 version; 8.0 and later need a paid licence for
  commercial use. Pin the range in every test project so a package update can't move it:
  `<PackageReference Include="FluentAssertions" Version="[7.0.0,8.0.0)" />`. Tested as a story: the
  samples' project resolves 7.x with that range.
- **Red, green, refactor:** write the failing test first, make it pass with the least code, then clean up
  with the tests green.
- **AAA** (Arrange, Act, Assert) in every test; **naming** `MethodName_Scenario_ExpectedResult`.
- **Test projects:** one per service is simplest (`{Service}.Tests`), mirroring the source folders;
  split into unit, integration (`testing-integration`) and architecture (`testing-architecture`)
  projects when there's a reason. Coverage tools merge by source file, so splitting doesn't double-count.
- **Coverage:** one test per branch is the efficient path. Quality gates such as SonarQube count branch
  conditions as well as lines, often on new code only. Exclude migrations in the coverage tool's own
  settings (Coverlet `ExcludeByFile`, or `sonar.coverage.exclusions` when SonarQube computes the gate).
- **What not to test:** private methods (test them through the public ones), trivial properties, EF
  migrations, DI registration. Don't add tests to inflate coverage.
- **A test project on an older target than the CI runtime** (`net8.0` on a .NET 10 runner) needs
  `<RollForward>Major</RollForward>` until it moves.
- **Writing and maintaining tests with AI is fine**, as long as each test meets the same bar as a
  hand-written one.

## Domain tests: AAA, theories at the edges, events

The samples test a small booking aggregate: a slot can't be booked once it has started, and a booking
can't be cancelled less than two hours before.

<!-- sample: tests/SkillSamples.Tests/Tdd/BookingTests.cs -->
```csharp
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
```

- **Time comes from a `FakeTimeProvider`**, never the real clock: a test pinned to a date passes today
  and fails once that date is past. `SetUtcNow` only moves forward.
- **Theories at the edges:** exactly at a limit and just past it, not three values in the middle.
- **Events:** assert the event and its data (`ContainSingle().Which.Should().Be(...)` with a record).
- **TheoryData of simple types** (`int`, `bool`, `string`). xUnit can't list each row of other types as
  a separate test.

## Handler tests with Moq

<!-- sample: tests/SkillSamples.Tests/Tdd/BookSlotHandlerTests.cs -->
```csharp
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
```

- **Verify the side effects that matter** (a save, a charge, a message sent): `Times.Once` and
  `Times.Never`. Tested as a story: when a change makes the handler save twice, that `Verify` fails and
  says "once, but was 2 times". Don't verify every call: a test that checks how the work is done breaks
  on every refactor.
- An async method that wasn't set up returns a completed task with the default (`null`, `false`), so
  "not found" needs no setup.
- `Callback` captures what was passed; `SetupSequence` returns different values per call;
  `ThrowsAsync` simulates a failing dependency.

## AutoFixture with AutoMoq

<!-- sample: tests/SkillSamples.Tests/Tdd/AutoMoqDataAttribute.cs -->
```csharp
// AutoFixture builds the test's arguments and Moq mocks the interfaces. AutoMoq would also build
// TimeProvider as a mock that calls the real clock, so every test gets one fixed fake clock instead
// (asking for FakeTimeProvider gives the same instance, to move it).
public sealed class AutoMoqDataAttribute() : AutoDataAttribute(() =>
{
    var fixture = new Fixture().Customize(new AutoMoqCustomization());
    var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.Zero));
    fixture.Inject<TimeProvider>(clock);
    fixture.Inject(clock);
    return fixture;
});
```

<!-- sample: tests/SkillSamples.Tests/Tdd/BookSlotHandlerAutoMoqTests.cs -->
```csharp
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
```

**AutoMoq gives `TimeProvider` the real clock.** Tested as a story: AutoMoq builds a mock of the
abstract class that calls the base implementation, so `GetUtcNow()` is today's date. Every test that
builds a handler with a time-based rule then depends on when it runs. Inject one `FakeTimeProvider`, as
above. Don't let AutoFixture build a `FakeTimeProvider` itself either. Tested: it fills public settable
properties, `AutoAdvanceAmount` among them, and the clock then moves on every read.

## Validators

<!-- sample: tests/SkillSamples.Tests/Tdd/BookSlotValidatorTests.cs -->
```csharp
public sealed class BookSlotValidatorTests
{
    private readonly BookSlotValidator _validator = new();

    [Fact]
    public async Task Validate_NoPatient_FailsOnPatientId()
    {
        var result = await _validator.ValidateAsync(new BookSlot(PatientId: 0, SlotId: 42));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.PropertyName.Should().Be(nameof(BookSlot.PatientId));
    }

    [Fact]
    public async Task Validate_ValidCommand_IsValid()
    {
        var result = await _validator.ValidateAsync(new BookSlot(7, 42));

        result.IsValid.Should().BeTrue();
    }
}
```

## FluentAssertions you'll use most

```csharp
result.Should().BeEquivalentTo(expected, o => o.Excluding(x => x.CreatedAt));   // structural, ignoring a field
orders.Should().ContainSingle(o => o.Status == OrderStatus.Pending);
orders.Should().BeInDescendingOrder(o => o.CreatedAt);
orders.Should().AllSatisfy(o => o.CustomerId.Should().Be(customerId));

var act = async () => await handler.Handle(command, CancellationToken.None);
await act.Should().ThrowAsync<ValidationException>()
    .Where(ex => ex.Errors.Any(e => e.PropertyName == "Lines"));
```

Give a reason when the number alone doesn't explain itself: `total.Should().Be(45m, "two items at 10
and one at 25")`. Tested with no ICU: the failure message still reads "Expected total to be 45M because
two items at 10 and one at 25, but found 45.5M."

## Culture in tests

**Don't set a culture in a unit test** to make a formatting assertion pass. Tested on a slim image (no
ICU): `new CultureInfo("en-US")` throws `CultureNotFoundException`, so such a test passes on a laptop and
crashes in CI. Code that formats for machines uses `CultureInfo.InvariantCulture`, and its tests need no
culture. Arabic formatting belongs to localization tests that check ICU first (`localization`).

## Rules
- Red, green, refactor; AAA; `MethodName_Scenario_ExpectedResult`.
- FluentAssertions pinned `[7.0.0,8.0.0)`; Moq or NSubstitute, never both in one project.
- A `FakeTimeProvider` for time, injected into AutoFixture too.
- Theories at the edges; verify the side effects that matter, not every call.
- No culture set in unit tests.
