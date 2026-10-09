namespace ClinicBooking.Domain;

public sealed class Appointment
{
    public int Id { get; init; }
    public int PatientId { get; init; }
    public int ClinicId { get; init; }
    public DateOnly Day { get; init; }
    public InsuranceType Insurance { get; init; }
    public bool IsCancelled { get; set; }
    public bool IsCheckedIn { get; set; }
    public bool IsCompleted { get; set; }
}

public enum InsuranceType { SelfPay, Government, Private }
