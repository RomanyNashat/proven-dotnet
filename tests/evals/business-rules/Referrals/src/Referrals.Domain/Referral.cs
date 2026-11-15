namespace Referrals.Domain;

public enum ReferralStatus
{
    Open,
    Accepted,
    Closed,
}

public sealed class Referral
{
    private Referral() { }

    public int Id { get; private set; }
    public int PatientId { get; private set; }
    public int DoctorId { get; private set; }
    public int ClinicId { get; private set; }
    public string Reason { get; private set; } = "";
    public string? Note { get; private set; }
    public ReferralStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Referral Make(int patientId, int doctorId, int clinicId, string reason, string? note, DateTimeOffset now) =>
        new() { PatientId = patientId, DoctorId = doctorId, ClinicId = clinicId, Reason = reason, Note = note, Status = ReferralStatus.Open, CreatedAt = now };

    public void Accept()
    {
        if (Status != ReferralStatus.Open)
            throw new InvalidOperationException($"A {Status} referral can't be accepted.");
        Status = ReferralStatus.Accepted;
    }

    public void Close() => Status = ReferralStatus.Closed;
}
