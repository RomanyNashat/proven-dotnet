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
}
