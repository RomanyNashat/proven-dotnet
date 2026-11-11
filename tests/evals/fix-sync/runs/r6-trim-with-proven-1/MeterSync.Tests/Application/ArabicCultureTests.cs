using System.Globalization;
using MeterSync.Application;

namespace MeterSync.Tests.Application;

public sealed class ArabicCultureTests
{
    [Fact]
    public void Gregorian_WhenIcuHasArSa_FormatsWithGregorianCalendar()
    {
        var culture = ArabicCulture.Gregorian;

        culture.Should().NotBeNull();
        culture!.DateTimeFormat.Calendar.Should().BeOfType<GregorianCalendar>();
        new DateTime(2026, 11, 2).ToString("yyyy", culture).Should().Be("2026");
    }

    [Fact]
    public void Gregorian_IsReadOnly()
    {
        ArabicCulture.Gregorian!.IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public void CreateGregorian_CultureNotAvailable_ReturnsNull()
    {
        ArabicCulture.CreateGregorian("not a culture!").Should().BeNull();
    }
}
