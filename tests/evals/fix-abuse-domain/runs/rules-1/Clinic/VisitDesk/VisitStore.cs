namespace VisitDesk;

public interface IVisitStore
{
    Slot? FindSlot(int id);
    IReadOnlyList<Slot> SlotsOf(int clinicId);
    Promo? FindPromo(string code);
    /// <summary>Every link the registry has for this guardian, the ended ones too.</summary>
    IReadOnlyList<GuardianLink> LinksOf(int guardianId);
    Visit? FindVisit(int id);
    IReadOnlyList<Visit> VisitsOf(int patientId);
    IReadOnlyList<Visit> VisitsForSlot(int slotId);
    Visit AddVisit(int patientId, int slotId, int bookedBy, decimal charged, string? promoCode, string note);
    void Save(Visit visit);
    /// <summary>Pays money back to whoever paid for the visit.</summary>
    void Refund(int visitId, decimal amount);
}

/// <summary>The store the desk runs on in tests and demos. Thread-safe.</summary>
public sealed class InMemoryVisitStore : IVisitStore
{
    private readonly object _gate = new();
    private readonly Dictionary<int, Slot> _slots = [];
    private readonly Dictionary<string, Promo> _promos = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GuardianLink> _links = [];
    private readonly List<Visit> _visits = [];
    private readonly List<(int VisitId, decimal Amount)> _refunds = [];

    public IReadOnlyList<(int VisitId, decimal Amount)> Refunds { get { lock (_gate) return [.. _refunds]; } }

    /// <summary>Adds a slot, or changes it (a new price) when its id is already there.</summary>
    public void Add(Slot slot) { lock (_gate) _slots[slot.Id] = slot; }

    public void Add(Promo promo) { lock (_gate) _promos[promo.Code] = promo; }

    public void Add(GuardianLink link) { lock (_gate) _links.Add(link); }

    public Slot? FindSlot(int id) { lock (_gate) return _slots.GetValueOrDefault(id); }

    public IReadOnlyList<Slot> SlotsOf(int clinicId) { lock (_gate) return [.. _slots.Values.Where(s => s.ClinicId == clinicId)]; }

    public Promo? FindPromo(string code) { lock (_gate) return _promos.GetValueOrDefault(code.Trim()); }

    public IReadOnlyList<GuardianLink> LinksOf(int guardianId) { lock (_gate) return [.. _links.Where(l => l.GuardianId == guardianId)]; }

    public Visit? FindVisit(int id) { lock (_gate) return _visits.Find(v => v.Id == id); }

    public IReadOnlyList<Visit> VisitsOf(int patientId) { lock (_gate) return [.. _visits.Where(v => v.PatientId == patientId)]; }

    public IReadOnlyList<Visit> VisitsForSlot(int slotId) { lock (_gate) return [.. _visits.Where(v => v.SlotId == slotId)]; }

    public Visit AddVisit(int patientId, int slotId, int bookedBy, decimal charged, string? promoCode, string note)
    {
        lock (_gate)
        {
            var visit = new Visit(_visits.Count + 1, patientId, slotId, bookedBy, charged, promoCode, note, VisitStatus.Booked);
            _visits.Add(visit);
            return visit;
        }
    }

    public void Save(Visit visit)
    {
        lock (_gate)
        {
            var at = _visits.FindIndex(v => v.Id == visit.Id);
            if (at >= 0)
                _visits[at] = visit;
        }
    }

    public void Refund(int visitId, decimal amount) { lock (_gate) _refunds.Add((visitId, amount)); }
}
