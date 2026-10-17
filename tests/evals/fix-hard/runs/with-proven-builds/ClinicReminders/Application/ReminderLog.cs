using Microsoft.Extensions.Logging;

namespace ClinicReminders.Application;

public static partial class ReminderLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Reminder {ReminderId} sent to {Mobile}")]
    public static partial void Sent(ILogger logger, int reminderId, [PatientIdentifier] string mobile);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reminder {ReminderId} not sent (status {StatusCode}, {Failure}); released for the next run")]
    public static partial void NotSent(ILogger logger, int reminderId, int? statusCode, string? failure);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reminder {ReminderId}: no answer from the SMS provider ({Failure}); left claimed and not resent")]
    public static partial void OutcomeUnknown(ILogger logger, int reminderId, string? failure);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reminder {ReminderId} failed with {ExceptionType}")]
    public static partial void Failed(ILogger logger, int reminderId, string exceptionType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reminder run: {Sent} sent, {NotSent} not sent, {Unknown} unknown, {Failed} failed, {Skipped} skipped")]
    public static partial void RunFinished(ILogger logger, int sent, int notSent, int unknown, int failed, int skipped);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reminder run failed with {ExceptionType}")]
    public static partial void RunFailed(ILogger logger, string exceptionType);
}
