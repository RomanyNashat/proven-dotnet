using ClinicReminders.Application;

namespace ClinicReminders.Tests;

public sealed class ReminderTextTests
{
    [Fact]
    public void For_UtcInstant_NamesTheRiyadhDayAndTimeInGregorian()
    {
        // 06:30 UTC on Monday 2 November 2026 is 09:30 in Riyadh.
        var text = ReminderText.For(new DateTimeOffset(2026, 11, 2, 6, 30, 0, TimeSpan.Zero));

        text.Should().Be("موعدك يوم الاثنين 2 نوفمبر 2026م الساعة 09:30");
    }

    [Fact]
    public void For_LateUtcEvening_RollsOverToTheNextRiyadhDay()
    {
        // 22:15 UTC on Saturday 31 October is 01:15 on Sunday 1 November in Riyadh.
        var text = ReminderText.For(new DateTimeOffset(2026, 10, 31, 22, 15, 0, TimeSpan.Zero));

        text.Should().Be("موعدك يوم الأحد 1 نوفمبر 2026م الساعة 01:15");
    }

    [Fact]
    public void For_AnyMachineCulture_GivesTheSameText()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("ar-SA");
            var underArabic = ReminderText.For(new DateTimeOffset(2026, 11, 2, 6, 30, 0, TimeSpan.Zero));

            underArabic.Should().Contain("2026م").And.NotContain("1448");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    public void Riyadh_IsUtcPlusThreeAllYear()
    {
        ClinicTime.Riyadh.GetUtcOffset(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)).Should().Be(TimeSpan.FromHours(3));
        ClinicTime.Riyadh.GetUtcOffset(new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc)).Should().Be(TimeSpan.FromHours(3));
    }
}
