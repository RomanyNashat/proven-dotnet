using Microsoft.Extensions.Logging;

namespace SkillSamples.PiiMasking;

public static partial class BookingLog
{
    // The classification travels with the parameter: wherever this line is written, the national ID goes
    // through the redactor chosen for PatientIdentifier, before any provider sees it.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Booking {BookingId} refused for {NationalId}: {Reason}")]
    public static partial void BookingRefused(ILogger logger, int bookingId, [PatientIdentifier] string nationalId, string reason);
}
