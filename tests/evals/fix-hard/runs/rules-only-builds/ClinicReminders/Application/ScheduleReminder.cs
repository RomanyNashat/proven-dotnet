using System.Text.RegularExpressions;

namespace ClinicReminders.Application;

public sealed partial record ScheduleReminder(int PatientId, string Mobile, DateTimeOffset AppointmentAt)
{
    /// <summary>How far ahead an appointment may be booked. Bounds typos such as year 2206.</summary>
    public static readonly TimeSpan MaxAhead = TimeSpan.FromDays(366);

    public Dictionary<string, string[]> Validate(DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        if (PatientId <= 0)
        {
            errors[nameof(PatientId)] = ["Must be a positive patient id."];
        }

        // JSON can leave a non-nullable string null.
        if (Mobile is null || !MobilePattern().IsMatch(Mobile.Trim()))
        {
            errors[nameof(Mobile)] = ["Must be a phone number of 8 to 15 digits, optionally starting with +."];
        }

        if (AppointmentAt <= now)
        {
            errors[nameof(AppointmentAt)] = ["Must be in the future."];
        }
        else if (AppointmentAt > now + MaxAhead)
        {
            errors[nameof(AppointmentAt)] = ["Must be within a year."];
        }

        return errors;
    }

    [GeneratedRegex(@"^\+?[0-9]{8,15}$", RegexOptions.CultureInvariant)]
    private static partial Regex MobilePattern();
}
