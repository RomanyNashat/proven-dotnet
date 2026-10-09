using ClinicBooking.Domain;

namespace ClinicBooking.Application;

public sealed class ClaimExporter
{
    public string? ClaimCodeFor(Appointment appointment) => appointment.Insurance switch
    {
        InsuranceType.SelfPay => null,
        InsuranceType.Government => $"GOV-{appointment.Id}",
        InsuranceType.Private => $"PRV-{appointment.Id}",
        _ => null,
    };
}
