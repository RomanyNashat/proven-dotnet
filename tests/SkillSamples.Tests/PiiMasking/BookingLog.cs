using Microsoft.Extensions.Logging;

namespace SkillSamples.PiiMasking;

public static partial class BookingLog
{
    // The classification travels with the parameter: wherever this line is written, the national ID goes
    // through the redactor chosen for PatientIdentifier, before any provider sees it.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Booking {BookingId} refused for {NationalId}: {Reason}")]
    public static partial void BookingRefused(ILogger logger, int bookingId, [PatientIdentifier] string nationalId, string reason);

    // Every property of the request becomes a field. Only the classified ones are redacted, so a property
    // someone adds later without the attribute (Mobile here) goes out in clear.
    [LoggerMessage(Level = LogLevel.Information, Message = "Booking requested")]
    public static partial void BookingRequested(ILogger logger, [LogProperties] BookingRequest request);
}
