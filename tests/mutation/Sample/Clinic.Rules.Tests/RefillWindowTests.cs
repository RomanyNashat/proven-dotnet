using Clinic.Rules;

namespace Clinic.Rules.Tests;

// Boundaries on both edges of the window and of the refills left: the cases test-design asks for.
public sealed class RefillWindowTests
{
    private static readonly DateOnly Dispensed = new(2026, 11, 1); // runs out on 1 December, window from 24 November

    [Theory]
    [InlineData(23, false)]
    [InlineData(24, true)]
    [InlineData(30, true)]
    public void IsOpen_AroundTheWindowsFirstDay_OpensSevenDaysBefore(int novemberDay, bool expected) =>
        Assert.Equal(expected, RefillWindow.IsOpen(new DateOnly(2026, 11, novemberDay), Dispensed, 30, refillsLeft: 2));

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void IsOpen_RefillsLeft_NoneLeftIsClosed(int left, bool expected) =>
        Assert.Equal(expected, RefillWindow.IsOpen(new DateOnly(2026, 11, 28), Dispensed, 30, left));
}
