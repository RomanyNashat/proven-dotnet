using ClinicBooking.Domain;

namespace ClinicBooking.Application;

public sealed class EligibilityChecker
{
    public bool CanBook(Appointment appointment, int bookingsThisMonth) => appointment.Insurance switch
    {
        InsuranceType.SelfPay => true,
        InsuranceType.Government => bookingsThisMonth < 4,
        InsuranceType.Private => bookingsThisMonth < 10,
        _ => false,
    };
}
