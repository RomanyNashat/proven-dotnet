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
    // is the visit still booked?), and the API calls this desk from many requests at once. One gate
    // makes each of them atomic so two callers can't book the same slot, use a promo twice, or get
    // a visit refunded twice. It only covers this process; see report.md.
    private readonly object _gate = new();

    /// <summary>Books a visit for the caller or for a dependent they are currently the guardian of.</summary>
    /// <remarks>
    /// The charge is the slot's current price, less the promo; <see cref="BookRequest.ShownPrice"/> comes
    /// from the app and is never used to decide what the patient pays.
    /// </remarks>
    public BookResult Book(int callerId, BookRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var note = request.Note ?? "";
        var code = string.IsNullOrWhiteSpace(request.PromoCode) ? null : request.PromoCode.Trim();

        lock (_gate)
        {
            var now = time.GetUtcNow();

            if (!MayActFor(callerId, request.PatientId, now))
                return Refused(BookRefusal.NotYourPatient);

            if (note.Length > MaxNoteLength)
                return Refused(BookRefusal.NoteTooLong);

            var slot = store.FindSlot(request.SlotId);
            if (slot is null)
                return Refused(BookRefusal.SlotNotFound);
            if (slot.StartsAt <= now)
                return Refused(BookRefusal.SlotInPast);
            if (store.VisitsForSlot(slot.Id).Any(v => v.Status == VisitStatus.Booked))
                return Refused(BookRefusal.SlotTaken);

            var charged = slot.Price;
            string? usedCode = null;
            if (code is not null)
            {
                var promo = store.FindPromo(code);
                // A code for another clinic, or one that has ended, is not a code this clinic has today.
                if (promo is null
                    || (promo.ClinicId is { } clinic && clinic != slot.ClinicId)
                    || (promo.ValidUntil is { } until && until <= now))
                    return Refused(BookRefusal.UnknownPromo);

                if (store.VisitsOf(request.PatientId).Any(v => v.Status == VisitStatus.Booked
                        && string.Equals(v.PromoCode, promo.Code, StringComparison.OrdinalIgnoreCase)))
                    return Refused(BookRefusal.PromoUsed);

                var percent = Math.Clamp(promo.PercentOff, 0, 100);
                charged = Math.Round(slot.Price * (100 - percent) / 100m, 2, MidpointRounding.AwayFromZero);
                usedCode = promo.Code;
            }

            var visit = store.AddVisit(request.PatientId, slot.Id, callerId, charged, usedCode, note);
            return new BookResult(BookRefusal.None, visit);
        }
    }

    /// <summary>One visit, or null when there is none or the caller may not see it.</summary>
    public Visit? Get(int callerId, int visitId)
    {
        var visit = store.FindVisit(visitId);
        return visit is not null && MayActFor(callerId, visit.PatientId, time.GetUtcNow()) ? visit : null;
    }

    /// <summary>Cancels a visit and refunds what was charged; false when it can't be.</summary>
    /// <remarks>
    /// The patient or their current guardian may cancel, until the visit's slot starts. A cancelled
    /// visit can't be cancelled (or refunded) again.
    /// </remarks>
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

    /// <summary>A patient's visits: the caller's own, or a current dependent's; empty for anyone else.</summary>
    public IReadOnlyList<Visit> ForPatient(int callerId, int patientId) =>
        MayActFor(callerId, patientId, time.GetUtcNow()) ? store.VisitsOf(patientId) : [];

    /// <summary>The caller is the patient, or holds a guardian link to them that hasn't ended.</summary>
    private bool MayActFor(int callerId, int patientId, DateTimeOffset now) =>
        callerId == patientId
        || store.LinksOf(callerId).Any(l => l.DependentId == patientId && (l.Until is null || l.Until > now));

    private static BookResult Refused(BookRefusal refusal) => new(refusal, null);
}
