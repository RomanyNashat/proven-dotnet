using System.Globalization;
using MeterSync.Application;
using MeterSync.Domain;
using MeterSync.Tests.Support;

namespace MeterSync.Tests.Application;

public sealed class AlertTextTests
{
    // 07:58 UTC is 10:58 in Riyadh (UTC+3).
    private static readonly DateTimeOffset TakenAt = new(2026, 11, 2, 7, 58, 0, TimeSpan.Zero);

    [Fact]
    public void For_UnderArSaCulture_WritesGregorianDateInRiyadhTime()
    {
        using var _ = new CultureScope("ar-SA");

        var text = AlertText.For(Make("300", "mg/dL"));

        // ar-SA's date separator carries a right-to-left mark, so the date keeps its order inside Arabic text.
        var sep = ArabicCulture.Gregorian!.DateTimeFormat.DateSeparator;
        text.Should().Contain($"02{sep}11{sep}2026 10:58").And.NotContain("1448");
    }

    [Fact]
    public void For_UnderHijriCurrentCulture_StillWritesGregorianDate()
    {
        var hijri = (CultureInfo)CultureInfo.GetCultureInfo("ar-SA").Clone();
        hijri.DateTimeFormat.Calendar = new UmAlQuraCalendar();
        using var _ = new CultureScope(hijri);

        var text = AlertText.For(Make("300", "mg/dL"));

        text.Should().Contain("2026").And.NotContain("1448");
    }

    [Fact]
    public void For_WithoutArabicCulture_FallsBackToIsoDate()
    {
        var text = AlertText.For(Make("300", "mg/dL"), arabic: null);

        text.Should().Contain("2026-11-02 10:58");
    }

    [Fact]
    public void For_HiReading_ShowsAboveRangePhraseInsteadOfCode()
    {
        var text = AlertText.For(Make("HI", "mg/dL"));

        text.Should().Contain("أعلى من 600").And.NotContain("HI");
    }

    [Fact]
    public void For_LoReading_ShowsBelowRangePhraseInsteadOfCode()
    {
        var text = AlertText.For(Make("LO", "mmol/L"));

        text.Should().Contain("أقل من 20").And.NotContain("LO");
    }

    [Fact]
    public void For_NumericReading_ShowsValueAsMeterShowedIt()
    {
        var text = AlertText.For(Make("15", "mmol/L"));

        text.Should().Contain("15 mmol/L").And.Contain("مرتفعة");
    }

    private static Reading Make(string raw, string unit)
    {
        Reading.TryFromMeter(1, 2, "r-1", raw, unit, TakenAt, out var reading).Should().BeTrue();
        return reading!;
    }
}
