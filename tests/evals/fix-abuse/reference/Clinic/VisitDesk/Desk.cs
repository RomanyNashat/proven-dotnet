namespace VisitDesk;

/// <summary>The clinic's visits: free slots, booking, and who may see or cancel what.</summary>
/// <remarks>The API calls one desk for many users at once; <c>callerId</c> is the signed-in user, from their token.</remarks>
public sealed class Desk(IVisitStore store, TimeProvider time)
{
    public const int MaxNoteLength = 500;

    // Booking reads, decides, then writes: two at once must not both see the slot free or the code unused.
    private readonly Lock _gate = new();

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

    /// <summary>Books a visit for the caller or one of their dependents, at the slot's price.</summary>
    public BookResult Book(int callerId, BookRequest request)
    {
        if (!MayActFor(callerId, request.PatientId))
            return new(BookRefusal.NotYourPatient, null);
        var note = request.Note ?? "";
        if (note.Length > MaxNoteLength)
            return new(BookRefusal.NoteTooLong, null);

        lock (_gate)
        {
            var slot = store.FindSlot(request.SlotId);
            if (slot is null)
                return new(BookRefusal.SlotNotFound, null);
            if (slot.StartsAt <= time.GetUtcNow())
                return new(BookRefusal.SlotInPast, null);
            if (store.VisitsForSlot(slot.Id).Any(v => v.Status == VisitStatus.Booked))
                return new(BookRefusal.SlotTaken, null);

            // The price is the clinic's; what the app showed is only for its receipt screen.
            var price = slot.Price;
            Promo? promo = null;
            if (!string.IsNullOrWhiteSpace(request.PromoCode))
            {
                promo = store.FindPromo(request.PromoCode);
                if (promo is null)
                    return new(BookRefusal.UnknownPromo, null);
                if (store.VisitsOf(request.PatientId).Any(v =>
                        string.Equals(v.PromoCode, promo.Code, StringComparison.OrdinalIgnoreCase)))
                    return new(BookRefusal.PromoUsed, null);
                price -= price * promo.PercentOff / 100m;
            }
            return new(BookRefusal.None, store.AddVisit(request.PatientId, slot.Id, callerId, price, promo?.Code, note));
        }
    }

    /// <summary>One visit, or null when there is none the caller may see (so another family's id says nothing).</summary>
    public Visit? Get(int callerId, int visitId)
    {
        var visit = store.FindVisit(visitId);
        return visit is not null && MayActFor(callerId, visit.PatientId) ? visit : null;
    }

    /// <summary>Cancels a visit the caller may act on, until it starts; false otherwise.</summary>
    public bool Cancel(int callerId, int visitId)
    {
        lock (_gate)
        {
            var visit = Get(callerId, visitId);
            if (visit is null || visit.Status != VisitStatus.Booked)
                return false;
            var slot = store.FindSlot(visit.SlotId);
            if (slot is null || slot.StartsAt <= time.GetUtcNow())
                return false;
            store.Save(visit with { Status = VisitStatus.Cancelled });
            return true;
        }
    }

    /// <summary>A patient's visits, for the patient or their guardian; empty for anyone else.</summary>
    public IReadOnlyList<Visit> ForPatient(int callerId, int patientId) =>
        MayActFor(callerId, patientId) ? store.VisitsOf(patientId) : [];

    private bool MayActFor(int callerId, int patientId) =>
        callerId == patientId || store.DependentsOf(callerId).Contains(patientId);
}
