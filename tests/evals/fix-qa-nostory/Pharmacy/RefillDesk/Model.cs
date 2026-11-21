namespace RefillDesk;

public enum RefillStatus { Requested, PendingApproval, Approved, Rejected, Dispensed, Cancelled }

public enum RefillRefusal { None, NotYourPrescription, Expired, NoRefillsLeft, TooEarly, AlreadyOpen, NotFound }

public sealed record Prescription(int Id, int PatientId, string Medicine, bool Controlled, int RefillsLeft,
    DateOnly ExpiresOn, DateOnly LastDispensedOn, int DaysSupply);

/// <summary>A caregiver may act for a patient from <see cref="From"/> to <see cref="To"/>, both days included.</summary>
public sealed record Delegation(int CaregiverId, int PatientId, DateOnly From, DateOnly To);

public sealed class RefillRequest(int id, int prescriptionId, int requestedBy, RefillStatus status)
{
    public int Id { get; } = id;
    public int PrescriptionId { get; } = prescriptionId;
    public int RequestedBy { get; } = requestedBy;
    public RefillStatus Status { get; internal set; } = status;
}

public sealed record RefillResult(RefillRefusal Refusal, RefillRequest? Request)
{
    public bool Ok => Refusal == RefillRefusal.None;
}
