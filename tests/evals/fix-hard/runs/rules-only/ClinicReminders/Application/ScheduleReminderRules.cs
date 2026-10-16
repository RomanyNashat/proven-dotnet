namespace ClinicReminders.Application;

/// <summary>Input rules for <see cref="ScheduleReminder"/>.</summary>
public static class ScheduleReminderRules
{
    /// <summary>The number without spaces or dashes, as it is stored and sent.</summary>
    /// <param name="mobile">The number as the caller sent it.</param>
    /// <returns>The normalized number.</returns>
    public static string NormalizeMobile(string? mobile) =>
        (mobile ?? "").Trim().Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);

    /// <summary>Checks a request. An empty result means it is valid.</summary>
    /// <param name="request">The request.</param>
    /// <param name="now">The current time.</param>
    /// <returns>Errors by field, in the shape ValidationProblem expects.</returns>
    public static Dictionary<string, string[]> Validate(ScheduleReminder request, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.PatientId <= 0)
        {
            errors["patientId"] = ["patientId is required."];
        }

        if (!IsMobile(NormalizeMobile(request.Mobile)))
        {
            errors["mobile"] = ["mobile must be 8 to 15 digits, optionally starting with +."];
        }

        if (request.AppointmentAt <= now)
        {
            errors["appointmentAt"] = ["appointmentAt must be in the future."];
        }

        return errors;
    }

    private static bool IsMobile(string mobile)
    {
        var digits = mobile.StartsWith('+') ? mobile.AsSpan(1) : mobile.AsSpan();
        return digits.Length is >= 8 and <= 15 && !digits.ContainsAnyExceptInRange('0', '9');
    }
}
