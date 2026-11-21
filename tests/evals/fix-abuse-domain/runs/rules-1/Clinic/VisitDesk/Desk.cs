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

    // Booking and cancelling are check-then-act on the store (is the slot free? was the promo used?
    // is the visit still booked?). The store has no atomic "reserve", so the desk serialises those
    // writes. The lock is keyed by store, not by desk, so it still holds when the API builds a desk
    // per request over one shared store. It does not span processes: see report.md.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IVisitStore, object> Gates = new();
    private readonly object _gate = Gates.GetValue(store, _ => new object());

    /// <summary>Books a visit for the caller or for a dependent they are currently the guardian of.</summary>
    /// <remarks>
    /// The charge is worked out here from the slot's current price and the promo;
    /// <see cref="BookRequest.ShownPrice"/> comes from the app and is never trusted for money.
    /// </remarks>
    public BookResult Book(int callerId, BookRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = time.GetUtcNow();

        if (!MayActFor(callerId, request.PatientId, now))
            return Refuse(BookRefusal.NotYourPatient);

        var note = request.Note ?? "";
        if (note.Length > MaxNoteLength)
            return Refuse(BookRefusal.NoteTooLong);

        var code = string.IsNullOrWhiteSpace(request.PromoCode) ? null : request.PromoCode.Trim();

        lock (_gate)
        {
            var slot = store.FindSlot(request.SlotId);
            if (slot is null)
                return Refuse(BookRefusal.SlotNotFound);
            if (slot.StartsAt <= now)
                return Refuse(BookRefusal.SlotInPast);
            if (store.VisitsForSlot(slot.Id).Any(v => v.Status == VisitStatus.Booked))
                return Refuse(BookRefusal.SlotTaken);

            var charged = slot.Price;
            string? promoCode = null;
            if (code is not null)
            {
                var promo = store.FindPromo(code);
                if (promo is null
                    || (promo.ClinicId is { } clinic && clinic != slot.ClinicId)
                    || (promo.ValidUntil is { } until && now > until))
                    return Refuse(BookRefusal.UnknownPromo);

                // Once per patient: a booked visit that used it counts; a cancelled one was refunded in full,
                // so it gave the patient nothing and doesn't use the code up.
                if (store.VisitsOf(request.PatientId).Any(v =>
                        v.Status == VisitStatus.Booked && string.Equals(v.PromoCode, promo.Code, StringComparison.OrdinalIgnoreCase)))
                    return Refuse(BookRefusal.PromoUsed);

                var percent = Math.Clamp(promo.PercentOff, 0, 100);
                charged = Math.Round(slot.Price * (100 - percent) / 100m, 2, MidpointRounding.AwayFromZero);
                promoCode = promo.Code;
            }

            var visit = store.AddVisit(request.PatientId, slot.Id, callerId, charged, promoCode, note);
            return new BookResult(BookRefusal.None, visit);
        }
    }

    /// <summary>One visit, or null when it doesn't exist or the caller may not see it (the two look the same).</summary>
    public Visit? Get(int callerId, int visitId)
    {
        var visit = store.FindVisit(visitId);
        return visit is not null && MayActFor(callerId, visit.PatientId, time.GetUtcNow()) ? visit : null;
    }

    /// <summary>
    /// Cancels a booked visit that hasn't started, frees its slot and refunds what was charged;
    /// false when the visit isn't the caller's to cancel, is already cancelled, or has started.
    /// </summary>
    public bool Cancel(int callerId, int visitId)
    {
        lock (_gate)
        {
            var now = time.GetUtcNow();
            var visit = store.FindVisit(visitId);
            if (visit is null || visit.Status != VisitStatus.Booked || !MayActFor(callerId, visit.PatientId, now))
                return false;

            var slot = store.FindSlot(visit.SlotId);
            if (slot is null || slot.StartsAt <= now)
                return false;

            store.Save(visit with { Status = VisitStatus.Cancelled });
            store.Refund(visit.Id, visit.Charged);
            return true;
        }
    }

    /// <summary>A patient's visits, cancelled ones too; empty when the caller isn't the patient or their current guardian.</summary>
    public IReadOnlyList<Visit> ForPatient(int callerId, int patientId) =>
        MayActFor(callerId, patientId, time.GetUtcNow()) ? store.VisitsOf(patientId) : [];

    private static BookResult Refuse(BookRefusal why) => new(why, null);

    /// <summary>The caller is the patient, or holds a guardian link to them that hasn't ended.</summary>
    private bool MayActFor(int callerId, int patientId, DateTimeOffset now) =>
        callerId == patientId
        || store.LinksOf(callerId).Any(l => l.DependentId == patientId && (l.Until is null || l.Until > now));
}
