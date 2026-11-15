namespace RefillDesk;

/// <summary>Refill requests for repeat prescriptions: who may ask, when, and what happens next. See story.md.</summary>
public sealed class Desk(IRefillStore store, TimeProvider time)
{
    /// <summary>The pharmacy's dates are Riyadh dates; Saudi Arabia keeps UTC+3 all year.</summary>
    public static readonly TimeSpan PharmacyOffset = TimeSpan.FromHours(3);
    public const int WindowDays = 7;

    private static readonly HashSet<(RefillStatus, RefillStatus)> Moves =
    [
        (RefillStatus.Requested, RefillStatus.Approved),
        (RefillStatus.Requested, RefillStatus.Cancelled),
        (RefillStatus.PendingApproval, RefillStatus.Approved),
        (RefillStatus.PendingApproval, RefillStatus.Rejected),
        (RefillStatus.PendingApproval, RefillStatus.Cancelled),
        (RefillStatus.Approved, RefillStatus.Dispensed),
    ];

    private readonly object _gate = new();

    public DateOnly Today => DateOnly.FromDateTime(time.GetUtcNow().ToOffset(PharmacyOffset).DateTime);

    public RefillResult Request(int requesterId, int prescriptionId)
    {
        lock (_gate)
        {
            var prescription = store.FindPrescription(prescriptionId);
            if (prescription is null)
                return new(RefillRefusal.NotFound, null);
            var today = Today;

            if (requesterId != prescription.PatientId && !store.DelegationsOf(requesterId).Any(d =>
                    d.PatientId == prescription.PatientId && d.From <= today && today <= d.To))
                return new(RefillRefusal.NotYourPrescription, null);
            if (today > prescription.ExpiresOn)
                return new(RefillRefusal.Expired, null);
            if (prescription.RefillsLeft <= 0)
                return new(RefillRefusal.NoRefillsLeft, null);

            var runsOut = prescription.LastDispensedOn.AddDays(prescription.DaysSupply);
            if (today < runsOut.AddDays(-WindowDays))
                return new(RefillRefusal.TooEarly, null);

            if (store.RequestsFor(prescriptionId).Any(r => r.Status is RefillStatus.Requested or RefillStatus.PendingApproval))
                return new(RefillRefusal.AlreadyOpen, null);

            var status = prescription.Controlled && requesterId == prescription.PatientId ? RefillStatus.PendingApproval : RefillStatus.Requested;
            return new(RefillRefusal.None, store.AddRequest(prescriptionId, requesterId, status));
        }
    }

    /// <summary>Moves a request to <paramref name="to"/>; false when that move isn't allowed.</summary>
    public bool Move(int requestId, RefillStatus to)
    {
        lock (_gate)
        {
            var request = store.FindRequest(requestId);
            if (request is null || !Moves.Contains((request.Status, to)))
                return false;

            request.Status = to;
            if (to == RefillStatus.Dispensed)
            {
                var prescription = store.FindPrescription(request.PrescriptionId)!;
                store.Save(prescription with { RefillsLeft = prescription.RefillsLeft - 1, LastDispensedOn = Today });
            }
            return true;
        }
    }
}
