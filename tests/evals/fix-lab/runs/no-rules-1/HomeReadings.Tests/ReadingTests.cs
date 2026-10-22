using System.Globalization;
using HomeReadings.Application;
using HomeReadings.Domain;
using Xunit;

public class ReadingTests
{
    [Theory]
    [InlineData("7.2", 7.2)]
    [InlineData("7,2", 7.2)]
    [InlineData("٧٫٢", 7.2)]
    [InlineData(" 126 ", 126)]
    public void Parses_meter_text_under_any_culture(string text, double expected)
    {
        var old = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
        try
        {
            Assert.True(ReadingService.TryParseMeterValue(text, out var v));
            Assert.Equal((decimal)expected, v);
        }
        finally
        {
            CultureInfo.CurrentCulture = old;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1.2.3")]
    [InlineData("1e3")]
    public void Rejects_garbage(string text) => Assert.False(ReadingService.TryParseMeterValue(text, out _));

    [Theory]
    [InlineData(11.0, "mmol/L", false)]
    [InlineData(11.1, "mmol/L", true)]
    [InlineData(150, "mg/dL", false)]
    [InlineData(200, "mg/dL", true)]
    public void Alert_level_is_unit_aware(double value, string unit, bool alert) =>
        Assert.Equal(alert, new Reading(1, "1", (decimal)value, unit, DateTime.UtcNow, null, DateTime.UtcNow).NeedsAlert);
}
