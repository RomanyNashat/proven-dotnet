using System.Globalization;
using MeterSync.Application;
using MeterSync.Domain;

namespace MeterSync.Tests.Application;

public sealed class AlertTextTests
{
    private static readonly DateTimeOffset TakenAt = new(2026, 11, 2, 7, 58, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(54, AlertKind.Low)]
    [InlineData(300, AlertKind.High)]
    public void For_OutOfRangeReading_FormatsRiyadhTimeGregorianWithLatinDigits(int mgDl, AlertKind expected)
    {
        var reading = Reading.FromMeter(1, 2, "r-1", mgDl.ToString(CultureInfo.InvariantCulture), "mg/dL", mgDl, TakenAt);
        reading.Alert.Should().Be(expected);

        AlertText.For(reading).Should().Contain("02/11/2026 10:58");
    }

    [Fact]
    public void For_LowReading_KeepsArabicMessage()
    {
        var reading = Reading.FromMeter(1, 2, "r-1", "54", "mg/dL", 54m, TakenAt);

        AlertText.For(reading).Should().Be("قراءة السكر 54 mg/dL منخفضة (02/11/2026 10:58). إذا كنت تشعر بتعب، تواصل مع طبيبك.");
    }

    [Fact]
    public void For_CurrentCultureArabic_StillUsesGregorianLatinDigits()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            var reading = Reading.FromMeter(1, 2, "r-1", "HI", "mg/dL", 600m, TakenAt);

            AlertText.For(reading).Should().Contain("02/11/2026 10:58").And.Contain("مرتفعة");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
