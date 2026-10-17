using ClinicReminders.Infrastructure;
using Microsoft.Extensions.Logging;

namespace ClinicReminders.Application;

/// <remarks>
/// The reminder id identifies the record; the patient's mobile number is never logged. (It used to be, behind a
/// redaction attribute, but <c>EnableRedaction()</c> replaced Serilog's logger factory: the number was erased only
/// because Serilog, and its configured sinks, received no logs at all.)
/// </remarks>
public static partial class ReminderLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Reminder {ReminderId} sent")]
    public static partial void Sent(ILogger logger, int reminderId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reminder {ReminderId} not sent: the provider could not be reached; the next run tries again")]
    public static partial void NotSent(ILogger logger, int reminderId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reminder {ReminderId} rejected by the SMS provider; it will not be retried")]
    public static partial void Rejected(ILogger logger, int reminderId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reminder {ReminderId}: the SMS may or may not have been sent; not retried, to avoid a duplicate. Check with the provider")]
    public static partial void Unknown(ILogger logger, int reminderId);

    public static void Outcome(ILogger logger, int reminderId, SmsOutcome outcome)
    {
        switch (outcome)
        {
            case SmsOutcome.Sent: Sent(logger, reminderId); break;
            case SmsOutcome.NotSent: NotSent(logger, reminderId); break;
            case SmsOutcome.Rejected: Rejected(logger, reminderId); break;
            default: Unknown(logger, reminderId); break;
        }
    }
}
