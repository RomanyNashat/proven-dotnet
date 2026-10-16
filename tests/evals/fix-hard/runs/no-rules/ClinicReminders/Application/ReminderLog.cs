using Microsoft.Extensions.Logging;

namespace ClinicReminders.Application;

public static partial class ReminderLog
{
    // Deliberately no phone number or patient details: logs must not carry patient identifiers.
    [LoggerMessage(Level = LogLevel.Information, Message = "Reminder {ReminderId} sent")]
    public static partial void Sent(ILogger logger, int reminderId);
}
