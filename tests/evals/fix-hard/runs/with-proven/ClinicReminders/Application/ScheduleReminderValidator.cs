using System.Text.RegularExpressions;
using ClinicReminders.Infrastructure;

namespace ClinicReminders.Application;

/// <summary>
/// Runs before the handler: a bad value is a 400 naming the field, never a database error. A mobile longer
/// than the column used to reach SQL Server, whose truncation error (2628) quotes the value into the
/// exception and the logs. Messages never echo the value.
/// </summary>
public static partial class ScheduleReminderValidator
{
    public static IReadOnlyDictionary<string, string[]> Validate(ScheduleReminder request, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.PatientId <= 0)
        {
            errors["patientId"] = ["patientId must be a positive number."];
        }

        if (string.IsNullOrWhiteSpace(request.Mobile))
        {
            errors["mobile"] = ["mobile is required."];
        }
        else if (request.Mobile.Length > ReminderConfiguration.MobileMaxLength || !MobilePattern().IsMatch(request.Mobile))
        {
            errors["mobile"] = ["mobile must be 8 to 15 digits, optionally starting with +."];
        }

        if (request.AppointmentAt <= now)
        {
            errors["appointmentAt"] = ["appointmentAt must be in the future."];
        }

        return errors;
    }

    // ASCII digits only: \d also matches Arabic-Indic digits, which the SMS provider isn't sent.
    [GeneratedRegex(@"^\+?[0-9]{8,15}$", RegexOptions.CultureInvariant)]
    private static partial Regex MobilePattern();
}
