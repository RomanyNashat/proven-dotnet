using Microsoft.Extensions.Logging;

namespace SkillSamples.Tdd;

public sealed record CancelBooking(int BookingId, string PatientNationalId, string Reason);

public sealed class CancelBookingHandler(ILogger<CancelBookingHandler> logger)
{
    public void Handle(CancelBooking command)
    {
        // What support needs to find the booking: its id as a field. Never who the patient is: the
        // national ID and the free-text reason stay out of the log.
        logger.LogInformation("Booking {BookingId} cancelled", command.BookingId);
    }
}
