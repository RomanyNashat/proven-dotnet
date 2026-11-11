using FluentAssertions;
using MeterSync.Application;
using MeterSync.Domain;
using MeterSync.Infrastructure;
using Xunit;

namespace MeterSync.Tests;

public sealed class ReadingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FromMeter_FreshLowReading_IsPending()
    {
        var reading = Create("60", "mg/dL", Now.AddMinutes(-2));

        reading.AlertState.Should().Be(AlertState.Pending);
    }

    [Fact]
    public void FromMeter_OldLowReading_IsStale()
    {
        var reading = Create("60", "mg/dL", Now.AddDays(-30));

        reading.AlertState.Should().Be(AlertState.Stale);
    }

    [Fact]
    public void FromMeter_InRange_NeedsNoAlert()
    {
        var reading = Create("5.5", "mmol/L", Now);

        reading.AlertState.Should().Be(AlertState.NotNeeded);
    }

    [Fact]
    public void AlertText_Reading_UsesGregorianDateInRiyadhTime()
    {
        var reading = Create("60", "mg/dL", new DateTimeOffset(2026, 11, 2, 22, 30, 0, TimeSpan.Zero));

        var text = AlertText.For(reading);

        text.Should().Contain("03/11/2026م 01:30").And.Contain("60 mg/dL").And.Contain("منخفضة");
    }

    [Fact]
    public void AlertText_HiReading_SpellsOutTheRange()
    {
        var reading = Create("HI", "mg/dL", Now);

        AlertText.For(reading).Should().Contain("أعلى من 600").And.Contain("مرتفعة").And.NotContain("HI");
    }

    [Theory]
    [InlineData("https://meters.example.net/v2", "https://meters.example.net/v2/meters/x/readings")]
    [InlineData("https://sms.example.net/", "https://sms.example.net/meters/x/readings")]
    public void ServiceAddress_RelativePath_KeepsBasePath(string configured, string expected)
    {
        new Uri(ServiceAddress.From(configured), "meters/x/readings").ToString().Should().Be(expected);
    }

    private static Reading Create(string raw, string unit, DateTimeOffset takenAt)
    {
        Glucose.TryParse(raw, unit, out var glucose).Should().BeTrue();
        return Reading.FromMeter(1, 1, "r-1", raw, glucose, takenAt, Now);
    }
}
