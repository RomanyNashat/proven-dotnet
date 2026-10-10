using AutoFixture;
using AutoFixture.AutoMoq;
using AutoFixture.Xunit2;
using Microsoft.Extensions.Time.Testing;

namespace SkillSamples.Tdd;

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
