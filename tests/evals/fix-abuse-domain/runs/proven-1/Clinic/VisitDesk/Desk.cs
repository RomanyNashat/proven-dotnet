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

    // The store has no atomic check-and-add, so the desk serialises every check-then-write (book, cancel) itself.
    private readonly object _writes = new();

    /// <summary>
    /// Books a visit for the caller or a dependent whose link holds, charged the slot's current price less any promo.
    /// Refusals, in order: NotYourPatient, NoteTooLong, SlotNotFound, SlotInPast, UnknownPromo, SlotTaken, PromoUsed.
    /// </summary>
    public BookResult Book(int callerId, BookRequest request)
    {
        if (!MayActFor(callerId, request.PatientId))
            return Refused(BookRefusal.NotYourPatient);

        var note = request.Note ?? "";
        if (note.Length > MaxNoteLength)
            return Refused(BookRefusal.NoteTooLong);

        var code = string.IsNullOrWhiteSpace(request.PromoCode) ? null : request.PromoCode.Trim();

        lock (_writes)
        {
            var now = time.GetUtcNow();
            var slot = store.FindSlot(request.SlotId);
            if (slot is null)
                return Refused(BookRefusal.SlotNotFound);
            if (slot.StartsAt <= now)
                return Refused(BookRefusal.SlotInPast);

            Promo? promo = null;
            if (code is not null)
            {
                promo = store.FindPromo(code);
                if (promo is null || !PromoApplies(promo, slot, now))
                    return Refused(BookRefusal.UnknownPromo);
            }

            if (store.VisitsForSlot(slot.Id).Any(v => v.Status == VisitStatus.Booked))
                return Refused(BookRefusal.SlotTaken);
            if (promo is not null && store.VisitsOf(request.PatientId).Any(v => SameCode(v.PromoCode, promo.Code)))
                return Refused(BookRefusal.PromoUsed);

            var charged = promo is null
                ? slot.Price
                : Math.Round(slot.Price * (100 - promo.PercentOff) / 100m, 2, MidpointRounding.AwayFromZero);
            var visit = store.AddVisit(request.PatientId, slot.Id, callerId, charged, promo?.Code, note);
            return new BookResult(BookRefusal.None, visit);
        }
    }

    /// <summary>One visit, to its patient or their guardian while the link holds; otherwise null.</summary>
    public Visit? Get(int callerId, int visitId)
    {
        var visit = store.FindVisit(visitId);
        return visit is not null && MayActFor(callerId, visit.PatientId) ? visit : null;
    }

    /// <summary>Cancels a booked visit before it starts and refunds what was charged; false when it can't be.</summary>
    public bool Cancel(int callerId, int visitId)
    {
        lock (_writes)
        {
            var visit = store.FindVisit(visitId);
            if (visit is null || visit.Status != VisitStatus.Booked || !MayActFor(callerId, visit.PatientId))
                return false;

            var slot = store.FindSlot(visit.SlotId);
            if (slot is null || slot.StartsAt <= time.GetUtcNow())
                return false;

            store.Save(visit with { Status = VisitStatus.Cancelled });
            store.Refund(visit.Id, visit.Charged);
            return true;
        }
    }

    /// <summary>A patient's visits, to the patient or their guardian while the link holds; otherwise empty.</summary>
    public IReadOnlyList<Visit> ForPatient(int callerId, int patientId) =>
        MayActFor(callerId, patientId) ? store.VisitsOf(patientId) : [];

    private bool MayActFor(int callerId, int patientId)
    {
        if (callerId == patientId)
            return true;
        var now = time.GetUtcNow();
        return store.LinksOf(callerId).Any(l => l.DependentId == patientId && (l.Until is null || l.Until > now));
    }

    private static bool PromoApplies(Promo promo, Slot slot, DateTimeOffset now) =>
        promo.PercentOff is >= 0 and <= 100
        && (promo.ClinicId is null || promo.ClinicId == slot.ClinicId)
        && (promo.ValidUntil is null || promo.ValidUntil > now);

    private static bool SameCode(string? used, string code) =>
        used is not null && string.Equals(used.Trim(), code.Trim(), StringComparison.OrdinalIgnoreCase);

    private static BookResult Refused(BookRefusal refusal) => new(refusal, null);
}
