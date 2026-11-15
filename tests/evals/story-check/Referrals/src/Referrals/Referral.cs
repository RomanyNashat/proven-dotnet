namespace Referrals;

public enum ReferralStatus
{
    Open,
    Accepted,
    Closed,
}

public sealed class Referral
{
    public int Id { get; init; }
    public required int PatientId { get; init; }
    public required int DoctorId { get; init; }
    public required int ClinicId { get; init; }
    public required string Reason { get; init; }
    public ReferralStatus Status { get; private set; } = ReferralStatus.Open;
    public bool ClinicNotified { get; private set; }
    public DateTimeOffset CreatedAt { get; init; }

    public void MarkClinicNotified() => ClinicNotified = true;

    public void Accept() => Status = Status == ReferralStatus.Open
        ? ReferralStatus.Accepted
        : throw new InvalidOperationException($"A {Status} referral can't be accepted.");

    public void Close() => Status = ReferralStatus.Closed;
}
