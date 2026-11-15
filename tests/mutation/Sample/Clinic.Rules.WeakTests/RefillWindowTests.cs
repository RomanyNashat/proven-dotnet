using Clinic.Rules;

namespace Clinic.Rules.WeakTests;

// Only the happy path: full coverage of the method, yet the edges are untested.
public sealed class RefillWindowTests
{
    [Fact]
    public void IsOpen_InTheMiddleOfTheWindow_IsOpen() =>
        Assert.True(RefillWindow.IsOpen(new DateOnly(2026, 11, 28), new DateOnly(2026, 11, 1), 30, refillsLeft: 2));

    [Fact]
    public void IsOpen_NoRefillsLeft_IsClosed() =>
        Assert.False(RefillWindow.IsOpen(new DateOnly(2026, 11, 28), new DateOnly(2026, 11, 1), 30, refillsLeft: -1));
}
