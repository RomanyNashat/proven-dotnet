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

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Reminder {ReminderId} did not reach the SMS provider (status {StatusCode}); the next run tries again")]
    public static partial void NotSent(ILogger logger, int reminderId, int? statusCode, Exception? error);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "The SMS provider refused reminder {ReminderId} with status {StatusCode}; it is not retried")]
    public static partial void Rejected(ILogger logger, int reminderId, int? statusCode);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Reminder {ReminderId} may or may not have been delivered (status {StatusCode}); it is not retried, because the SMS provider has no idempotency key. Check with the provider.")]
    public static partial void OutcomeUnknown(ILogger logger, int reminderId, int? statusCode, Exception? error);
}
