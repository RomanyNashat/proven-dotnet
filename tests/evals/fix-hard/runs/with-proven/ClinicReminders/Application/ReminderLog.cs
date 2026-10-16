using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Logging;

namespace ClinicReminders.Application;

public static class ClinicData
{
    public static DataClassification PatientIdentifier => new("Clinic", nameof(PatientIdentifier));
}

public sealed class PatientIdentifierAttribute() : DataClassificationAttribute(ClinicData.PatientIdentifier);

public static partial class ReminderLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Reminder {ReminderId} sent to {Mobile}")]
    public static partial void Sent(ILogger logger, int reminderId, [PatientIdentifier] string mobile);

    // The reason is a status code or the kind of connection failure, never the exception message.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Reminder {ReminderId} not sent ({Reason}); released for the next run")]
    public static partial void NotSent(ILogger logger, int reminderId, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reminder {ReminderId}: SMS outcome unknown after {ExceptionType}; left claimed, not sent again")]
    public static partial void OutcomeUnknown(ILogger logger, int reminderId, string exceptionType);
}
