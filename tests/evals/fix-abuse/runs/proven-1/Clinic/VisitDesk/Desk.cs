namespace VisitDesk;

/// <summary>The clinic's visits: free slots, booking, and who may see or cancel what.</summary>
/// <remarks>The API calls one desk for many users at once; <c>callerId</c> is the signed-in user, from their token.</remarks>
public sealed class Desk(IVisitStore store, TimeProvider time)
{
    public const int MaxNoteLength = 500;

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

    /// <summary>Books a visit for the caller or one of their dependents, charged from the slot's price.</summary>
    public BookResult Book(int callerId, BookRequest request)
    {
        // Ownership first, so a refusal says nothing about someone else's patient.
        if (!MayActFor(callerId, request.PatientId))
            return Refused(BookRefusal.NotYourPatient);

        var note = request.Note ?? "";
        if (note.Length > MaxNoteLength)
            return Refused(BookRefusal.NoteTooLong);

        var slot = store.FindSlot(request.SlotId);
        if (slot is null)
            return Refused(BookRefusal.SlotNotFound);

        Promo? promo = null;
        if (request.PromoCode is not null)
        {
            promo = store.FindPromo(request.PromoCode);
            if (promo is null)
                return Refused(BookRefusal.UnknownPromo);
        }

        // The store has no atomic check-and-add: hold the desk's lock from the checks to AddVisit.
        lock (_gate)
        {
            if (slot.StartsAt <= time.GetUtcNow())
                return Refused(BookRefusal.SlotInPast);

            if (store.VisitsForSlot(slot.Id).Any(v => v.Status == VisitStatus.Booked))
                return Refused(BookRefusal.SlotTaken);

            if (promo is not null && store.VisitsOf(request.PatientId)
                    .Any(v => string.Equals(v.PromoCode, promo.Code, StringComparison.OrdinalIgnoreCase)))
                return Refused(BookRefusal.PromoUsed);

            var charged = promo is null ? slot.Price : slot.Price * (100 - promo.PercentOff) / 100;
            var visit = store.AddVisit(request.PatientId, slot.Id, callerId, charged, promo?.Code, note);
            return new BookResult(BookRefusal.None, visit);
        }
    }

    /// <summary>One visit, to its patient or the patient's guardian; otherwise null.</summary>
    public Visit? Get(int callerId, int visitId)
    {
        var visit = store.FindVisit(visitId);
        return visit is not null && MayActFor(callerId, visit.PatientId) ? visit : null;
    }

    /// <summary>Cancels a booked visit before it starts, by its patient or their guardian; false when it can't be.</summary>
    public bool Cancel(int callerId, int visitId)
    {
        lock (_gate)
        {
            var visit = Get(callerId, visitId);
            if (visit is not { Status: VisitStatus.Booked })
                return false;

            var slot = store.FindSlot(visit.SlotId);
            if (slot is null || time.GetUtcNow() >= slot.StartsAt)
                return false;

            store.Save(visit with { Status = VisitStatus.Cancelled });
            return true;
        }
    }

    /// <summary>A patient's visits, cancelled ones included, to the patient or their guardian; otherwise empty.</summary>
    public IReadOnlyList<Visit> ForPatient(int callerId, int patientId) =>
        MayActFor(callerId, patientId) ? store.VisitsOf(patientId) : [];

    /// <summary>Makes Book's and Cancel's check-then-write atomic within this process.</summary>
    private readonly object _gate = new();

    private bool MayActFor(int callerId, int patientId) =>
        patientId == callerId || store.DependentsOf(callerId).Contains(patientId);

    private static BookResult Refused(BookRefusal refusal) => new(refusal, null);
}
