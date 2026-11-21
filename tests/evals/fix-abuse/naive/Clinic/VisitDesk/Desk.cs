namespace VisitDesk;

/// <summary>The clinic's visits, built from task.md's words alone: every stated rule, no thought of misuse.</summary>
public sealed class Desk(IVisitStore store, TimeProvider time)
{
    public const int MaxNoteLength = 500;

    public IReadOnlyList<Visit> Mine(int callerId) => store.VisitsOf(callerId);

    public IReadOnlyList<Slot> FreeSlots(int clinicId)
    {
        var now = time.GetUtcNow();
        return [.. store.SlotsOf(clinicId)
            .Where(s => s.StartsAt > now && !store.VisitsForSlot(s.Id).Any(v => v.Status == VisitStatus.Booked))
            .OrderBy(s => s.StartsAt)];
    }

    public BookResult Book(int callerId, BookRequest request)
    {
        if (request.PatientId != callerId && !store.DependentsOf(callerId).Contains(request.PatientId))
            return new(BookRefusal.NotYourPatient, null);
        var note = request.Note ?? "";
        if (note.Length > MaxNoteLength)
            return new(BookRefusal.NoteTooLong, null);
        var slot = store.FindSlot(request.SlotId);
        if (slot is null)
            return new(BookRefusal.SlotNotFound, null);
        if (store.VisitsForSlot(slot.Id).Any(v => v.Status == VisitStatus.Booked))
            return new(BookRefusal.SlotTaken, null);
        var price = slot.Price;
        string? code = null;
        if (!string.IsNullOrWhiteSpace(request.PromoCode))
        {
            var promo = store.FindPromo(request.PromoCode);
            if (promo is null)
                return new(BookRefusal.UnknownPromo, null);
            if (store.VisitsOf(request.PatientId).Any(v => v.PromoCode == promo.Code))
                return new(BookRefusal.PromoUsed, null);
            price -= price * promo.PercentOff / 100m;
            code = promo.Code;
        }
        return new(BookRefusal.None, store.AddVisit(request.PatientId, slot.Id, callerId, price, code, note));
    }

    public Visit? Get(int callerId, int visitId) => store.FindVisit(visitId);

    public bool Cancel(int callerId, int visitId)
    {
        var visit = store.FindVisit(visitId);
        if (visit is null || visit.Status != VisitStatus.Booked || store.FindSlot(visit.SlotId)!.StartsAt <= time.GetUtcNow())
            return false;
        store.Save(visit with { Status = VisitStatus.Cancelled });
        return true;
    }

    public IReadOnlyList<Visit> ForPatient(int callerId, int patientId) => store.VisitsOf(patientId);
}
