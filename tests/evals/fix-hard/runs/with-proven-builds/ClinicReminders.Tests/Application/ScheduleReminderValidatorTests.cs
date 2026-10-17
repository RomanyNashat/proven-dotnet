using ClinicReminders.Application;
using Microsoft.Extensions.Time.Testing;

namespace ClinicReminders.Tests.Application;

public sealed class ScheduleReminderValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 6, 0, 0, TimeSpan.Zero);
    private readonly ScheduleReminderValidator _validator = new(new FakeTimeProvider(Now));

    [Theory]
    [InlineData("0501234567")]
    [InlineData("+966501234567")]
    public void Validate_ValidRequest_Passes(string mobile)
    {
        _validator.Validate(new ScheduleReminder(null, mobile, Now.AddDays(1))).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345678901234567890123")]   // longer than the 20-character column
    [InlineData("٠٥٠١٢٣٤٥٦٧")]                 // Arabic-Indic digits: the provider wants ASCII
    [InlineData("050-123-4567")]
    [InlineData("1234")]
    public void Validate_BadMobile_FailsOnMobile(string mobile)
    {
        var result = _validator.Validate(new ScheduleReminder(null, mobile, Now.AddDays(1)));

        result.Errors.Should().Contain(e => e.PropertyName == nameof(ScheduleReminder.Mobile));
    }

    [Fact]
    public void Validate_NullMobile_FailsOnMobile()
    {
        var result = _validator.Validate(new ScheduleReminder(null, null!, Now.AddDays(1)));

        result.Errors.Should().Contain(e => e.PropertyName == nameof(ScheduleReminder.Mobile));
    }

    [Fact]
    public void Validate_AppointmentNotInTheFuture_FailsOnAppointmentAt()
    {
        var result = _validator.Validate(new ScheduleReminder(null, "0501234567", Now));

        result.Errors.Should().Contain(e => e.PropertyName == nameof(ScheduleReminder.AppointmentAt));
    }
}
