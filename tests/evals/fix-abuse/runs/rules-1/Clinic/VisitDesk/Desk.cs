namespace VisitDesk;

/// <summary>The clinic's visits: free slots, booking, and who may see or cancel what.</summary>
/// <remarks>The API calls one desk for many users at once; <c>callerId</c> is the signed-in user, from their token.</remarks>
public sealed class Desk(IVisitStore store, TimeProvider time)
{
    public const int MaxNoteLength = 500;

    // Booking and cancelling are check-then-write against the store (is the slot free? was the promo used?).
    // The desk is shared by all callers, so those sequences run one at a time to stop two requests
    // taking the same slot or spending the same promo twice.
    private readonly object _writeGate = new();

    /// <summary>The caller's own visits.</summary>
    public IReadOnlyList<Visit> Mine(int callerId) => store.VisitsOf(callerId);

    /// <summary>The clinic's slots that haven't started and have no booked visit.</summary>
    public IReadOnlyList<Slot> FreeSlots(int clinicId)
    {
        var now = time.GetUtcNow();
        return [.. store.SlotsOf(clinicId)
            .Where(s => s.StartsAt > now && !store.VisitsForSlot(s.Id).Any(v => v.Status == VisitStatus.Booked))
            .OrderBy(s => s.StartsAt)];
    }

    /// <summary>
    /// Books a visit for the caller or one of their dependents. The charge is computed from the slot's price
    /// and the promo; <see cref="BookRequest.ShownPrice"/> is never trusted.
    /// </summary>
    public BookResult Book(int callerId, BookRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!MayActFor(callerId, request.PatientId))
            return Refused(BookRefusal.NotYourPatient);

        var note = request.Note ?? "";
        if (note.Length > MaxNoteLength)
            return Refused(BookRefusal.NoteTooLong);

        var code = string.IsNullOrWhiteSpace(request.PromoCode) ? null : request.PromoCode.Trim();
        Promo? promo = null;
        if (code is not null)
        {
            promo = store.FindPromo(code);
            if (promo is null)
                return Refused(BookRefusal.UnknownPromo);
        }

        lock (_writeGate)
        {
            var slot = store.FindSlot(request.SlotId);
            if (slot is null)
                return Refused(BookRefusal.SlotNotFound);
            if (slot.StartsAt <= time.GetUtcNow())
                return Refused(BookRefusal.SlotInPast);
            if (store.VisitsForSlot(slot.Id).Any(v => v.Status == VisitStatus.Booked))
                return Refused(BookRefusal.SlotTaken);

            if (promo is not null && store.VisitsOf(request.PatientId).Any(v =>
                    v.Status == VisitStatus.Booked &&
                    string.Equals(v.PromoCode, promo.Code, StringComparison.OrdinalIgnoreCase)))
                return Refused(BookRefusal.PromoUsed);

            var charged = promo is null ? slot.Price : Discounted(slot.Price, promo.PercentOff);
            var visit = store.AddVisit(request.PatientId, slot.Id, callerId, charged, promo?.Code, note);
            return new BookResult(BookRefusal.None, visit);
        }
    }

    /// <summary>One visit, or null when it doesn't exist or isn't the caller's or a dependent's.</summary>
    public Visit? Get(int callerId, int visitId)
    {
        var visit = store.FindVisit(visitId);
        return visit is not null && MayActFor(callerId, visit.PatientId) ? visit : null;
    }

    /// <summary>
    /// Cancels a visit; false when it can't be: not found, not the caller's or a dependent's,
    /// already cancelled, or its slot has started.
    /// </summary>
    public bool Cancel(int callerId, int visitId)
    {
        lock (_writeGate)
        {
            var visit = store.FindVisit(visitId);
            if (visit is null || !MayActFor(callerId, visit.PatientId) || visit.Status != VisitStatus.Booked)
                return false;

            var slot = store.FindSlot(visit.SlotId);
            if (slot is null || slot.StartsAt <= time.GetUtcNow())
                return false;

            store.Save(visit with { Status = VisitStatus.Cancelled });
            return true;
        }
    }

    /// <summary>A patient's visits; empty unless the patient is the caller or one of their dependents.</summary>
    public IReadOnlyList<Visit> ForPatient(int callerId, int patientId) =>
        MayActFor(callerId, patientId) ? store.VisitsOf(patientId) : [];

    private bool MayActFor(int callerId, int patientId) =>
        callerId == patientId || store.DependentsOf(callerId).Contains(patientId);

    private static decimal Discounted(decimal price, int percentOff)
    {
        var percent = Math.Clamp(percentOff, 0, 100);
        return Math.Round(price * (100 - percent) / 100m, 2, MidpointRounding.AwayFromZero);
    }

    private static BookResult Refused(BookRefusal why) => new(why, null);
}
