using System.Runtime.CompilerServices;

namespace VisitDesk;

/// <summary>The clinic's visits: free slots, booking, and who may see or cancel what.</summary>
/// <remarks>The API calls one desk for many users at once; <c>callerId</c> is the signed-in user, from their token.</remarks>
public sealed class Desk(IVisitStore store, TimeProvider time)
{
    public const int MaxNoteLength = 500;

    // One gate per store, shared by every desk on that store, so desks created per request still serialize
    // the read-then-write steps of booking and cancelling.
    private static readonly ConditionalWeakTable<IVisitStore, object> Gates = new();

    private object Gate => Gates.GetValue(store, _ => new object());

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

    /// <summary>Books a free slot for the caller or one of their dependents.</summary>
    /// <remarks>
    /// The charge is always computed from the slot's price in the store; <see cref="BookRequest.ShownPrice"/>
    /// comes from the app and is never used for money.
    /// </remarks>
    public BookResult Book(int callerId, BookRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!MayActFor(callerId, request.PatientId))
            return Refused(BookRefusal.NotYourPatient);

        var note = request.Note ?? "";
        if (note.Length > MaxNoteLength)
            return Refused(BookRefusal.NoteTooLong);

        var slot = store.FindSlot(request.SlotId);
        if (slot is null)
            return Refused(BookRefusal.SlotNotFound);

        Promo? promo = null;
        if (!string.IsNullOrWhiteSpace(request.PromoCode))
        {
            promo = store.FindPromo(request.PromoCode.Trim());
            if (promo is null)
                return Refused(BookRefusal.UnknownPromo);
        }

        // These checks read the store and then write to it; they must not interleave with another booking
        // or cancellation, or two callers could both take one slot or a patient could use one promo twice.
        lock (Gate)
        {
            if (slot.StartsAt <= time.GetUtcNow())
                return Refused(BookRefusal.SlotInPast);

            if (store.VisitsForSlot(slot.Id).Any(v => v.Status == VisitStatus.Booked))
                return Refused(BookRefusal.SlotTaken);

            if (promo is not null && store.VisitsOf(request.PatientId).Any(v =>
                    v.Status == VisitStatus.Booked &&
                    string.Equals(v.PromoCode, promo.Code, StringComparison.OrdinalIgnoreCase)))
                return Refused(BookRefusal.PromoUsed);

            var charged = promo is null ? slot.Price : Discount(slot.Price, promo.PercentOff);
            var visit = store.AddVisit(request.PatientId, slot.Id, callerId, charged, promo?.Code, note);
            return new BookResult(BookRefusal.None, visit);
        }
    }

    /// <summary>One visit, or null when it doesn't exist or the caller may not see it.</summary>
    public Visit? Get(int callerId, int visitId)
    {
        var visit = store.FindVisit(visitId);
        return visit is not null && MayActFor(callerId, visit.PatientId) ? visit : null;
    }

    /// <summary>
    /// Cancels a booked visit that hasn't started; its slot becomes free again. False when the visit doesn't
    /// exist, the caller may not act for its patient, it is already cancelled, or it has started.
    /// </summary>
    public bool Cancel(int callerId, int visitId)
    {
        lock (Gate)
        {
            var visit = store.FindVisit(visitId);
            if (visit is null || visit.Status != VisitStatus.Booked || !MayActFor(callerId, visit.PatientId))
                return false;

            var slot = store.FindSlot(visit.SlotId);
            if (slot is null || slot.StartsAt <= time.GetUtcNow())
                return false;

            store.Save(visit with { Status = VisitStatus.Cancelled });
            return true;
        }
    }

    /// <summary>
    /// A patient's visits: the caller's own, or a dependent's when the caller is their guardian.
    /// Empty for anyone else, so the API can't be used to read other patients' visits.
    /// </summary>
    public IReadOnlyList<Visit> ForPatient(int callerId, int patientId) =>
        MayActFor(callerId, patientId) ? store.VisitsOf(patientId) : [];

    private bool MayActFor(int callerId, int patientId) =>
        callerId == patientId || store.DependentsOf(callerId).Contains(patientId);

    private static decimal Discount(decimal price, int percentOff)
    {
        var percent = Math.Clamp(percentOff, 0, 100);
        return Math.Round(price * (100 - percent) / 100m, 2, MidpointRounding.AwayFromZero);
    }

    private static BookResult Refused(BookRefusal refusal) => new(refusal, null);
}
