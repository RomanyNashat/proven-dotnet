using System.Globalization;
using ClinicReminders.Application;

namespace ClinicReminders.Tests.Application;

public sealed class ReminderMessageTests
{
    [Fact]
    public void Arabic_AppointmentInUtc_ShowsRiyadhTimeOnTheGregorianCalendar()
    {
        // 21:30 UTC on 3 October is 00:30 on Sunday 4 October in Riyadh.
        var text = ReminderMessage.Arabic(new DateTimeOffset(2026, 10, 3, 21, 30, 0, TimeSpan.Zero));

        text.Should().Be("موعدك يوم الأحد 4 أكتوبر 2026 الساعة 00:30");
    }

    [Fact]
    public void Arabic_UnderAHijriCurrentCulture_StaysGregorian()
    {
        var original = CultureInfo.CurrentCulture;
        var hijri = (CultureInfo)CultureInfo.GetCultureInfo("ar-SA").Clone();
        hijri.DateTimeFormat.Calendar = new UmAlQuraCalendar();
        try
        {
            CultureInfo.CurrentCulture = hijri;

            var text = ReminderMessage.Arabic(new DateTimeOffset(2026, 10, 4, 6, 0, 0, TimeSpan.Zero));

            text.Should().Contain("2026").And.Contain("أكتوبر").And.Contain("09:00");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Arabic_AnyAppointment_FitsOneArabicSmsPart()
    {
        var text = ReminderMessage.Arabic(new DateTimeOffset(2026, 9, 30, 6, 0, 0, TimeSpan.Zero));

        text.Length.Should().BeLessThanOrEqualTo(70);
    }
}
