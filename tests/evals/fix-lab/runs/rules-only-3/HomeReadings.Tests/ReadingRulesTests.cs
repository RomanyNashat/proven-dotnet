using System.Globalization;
using System.Security.Claims;
using HomeReadings.Api;
using HomeReadings.Application;
using HomeReadings.Domain;

namespace HomeReadings.Tests;

public sealed class ReadingRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("7.5", "7.5")]
    [InlineData(" 12.4 ", "12.4")]
    [InlineData("7,5", "7.5")]
    [InlineData("٧٫٥", "7.5")]
    [InlineData("۱۲.۴", "12.4")]
    [InlineData("180", "180")]
    public void TryParseValue_MeterText_ReadsTheNumber(string text, string expected)
    {
        MeterText.TryParseValue(text, out var value).Should().BeTrue();
        value.Should().Be(decimal.Parse(expected, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("")]
    [InlineData("HI")]
    [InlineData("1,234.5")]
    [InlineData("-3")]
    [InlineData("7.5 mmol/L")]
    public void TryParseValue_NotANumber_Fails(string text) =>
        MeterText.TryParseValue(text, out _).Should().BeFalse();

    [Fact]
    public void TryParseValue_ThreadCultureIsArabic_StillReadsDecimalPoint()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            MeterText.TryParseValue("7.5", out var value).Should().BeTrue();
            value.Should().Be(7.5m);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void DayOf_LateEveningUtc_IsTheNextRiyadhDay()
    {
        var (start, end) = RiyadhTime.DayOf(new DateTimeOffset(2026, 10, 22, 22, 30, 0, TimeSpan.Zero));

        start.Should().Be(new DateTime(2026, 10, 22, 21, 0, 0, DateTimeKind.Utc));
        end.Should().Be(new DateTime(2026, 10, 23, 21, 0, 0, DateTimeKind.Utc));
        start.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void ToUtc_NoOffset_IsRiyadhTime()
    {
        var utc = RiyadhTime.ToUtc(new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Unspecified));

        utc.Should().Be(new DateTime(2026, 10, 22, 5, 0, 0, DateTimeKind.Utc));
        utc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void ToUtc_Utc_IsUnchanged() =>
        RiyadhTime.ToUtc(Now).Should().Be(Now);

    [Theory]
    [InlineData("11.1", "mmol/L", true)]
    [InlineData("11.0", "mmol/L", false)]
    [InlineData("95", "mg/dL", false)]
    [InlineData("200", "mg/dL", true)]
    public void NeedsAlert_ByUnit_UsesThatUnitsThreshold(string value, string unit, bool expected)
    {
        var reading = new Reading(1, "1012345678", decimal.Parse(value, CultureInfo.InvariantCulture), unit, Now, null, Now);
        reading.NeedsAlert.Should().Be(expected);
    }

    [Fact]
    public void ValueInMmolPerLitre_MgPerDl_IsConverted()
    {
        var reading = new Reading(1, "1012345678", 200m, Reading.MgPerDecilitre, Now, null, Now);
        reading.ValueInMmolPerLitre.Should().Be(11.1m);
    }

    [Fact]
    public void Validate_GoodReading_ReturnsStoredForm()
    {
        var request = new NewReading("١٠١٢٣٤٥٦٧٨", "٧٫٥", "MMOL/L", new DateTime(2026, 10, 22, 11, 0, 0), "  after lunch ");

        var valid = NewReadingValidator.Validate(request, Now, out var errors);

        errors.Should().BeEmpty();
        valid.Should().Be(new ValidReading("1012345678", 7.5m, Reading.MmolPerLitre,
            new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Utc), "after lunch"));
    }

    [Theory]
    [InlineData("12345", "7.5", "mmol/L", 0, "nationalId")]
    [InlineData("1012345678", "abc", "mmol/L", 0, "value")]
    [InlineData("1012345678", "75", "mmol/L", 0, "value")]
    [InlineData("1012345678", "7.5", "mg", 0, "unit")]
    [InlineData("1012345678", "7.5", "mmol/L", 2, "collectedAt")]
    public void Validate_BadField_NamesTheField(string nationalId, string value, string unit, int hoursAhead, string field)
    {
        var request = new NewReading(nationalId, value, unit, Now.AddHours(hoursAhead), null);

        NewReadingValidator.Validate(request, Now, out var errors).Should().BeNull();
        errors.Keys.Should().Equal(field);
    }

    [Fact]
    public void Validate_LongNotes_Rejected()
    {
        var request = new NewReading("1012345678", "7.5", "mmol/L", Now, new string('x', 501));

        NewReadingValidator.Validate(request, Now, out var errors).Should().BeNull();
        errors.Should().ContainKey("notes");
    }

    [Theory]
    [InlineData("42", true, 42)]
    [InlineData("abc", false, 0)]
    [InlineData("-1", false, 0)]
    public void TryGetId_Sub_ParsesPatientId(string sub, bool ok, int expected)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", sub)], "test"));

        HttpCurrentPatient.TryGetId(user, out var id).Should().Be(ok);
        id.Should().Be(expected);
    }

    [Theory]
    [InlineData("ar", "تم حفظ القراءة")]
    [InlineData("AR-sa", "تم حفظ القراءة")]
    [InlineData("en", "Reading saved")]
    [InlineData(null, "Reading saved")]
    public void Saved_Language_PicksText(string? language, string expected) =>
        Messages.Saved(language).Should().Be(expected);
}
