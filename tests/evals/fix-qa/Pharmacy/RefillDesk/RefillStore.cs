namespace RefillDesk;

public interface IRefillStore
{
    Prescription? FindPrescription(int id);
    RefillRequest? FindRequest(int id);
    IReadOnlyList<RefillRequest> RequestsFor(int prescriptionId);
    IReadOnlyList<Delegation> DelegationsOf(int caregiverId);
    RefillRequest AddRequest(int prescriptionId, int requestedBy, RefillStatus status);
    void Save(Prescription prescription);
}

/// <summary>The store the desk runs on in tests and demos. Thread-safe.</summary>
public sealed class InMemoryRefillStore : IRefillStore
{
    private readonly object _gate = new();
    private readonly Dictionary<int, Prescription> _prescriptions = [];
    private readonly List<RefillRequest> _requests = [];
    private readonly List<Delegation> _delegations = [];

    public void Add(Prescription prescription) { lock (_gate) _prescriptions[prescription.Id] = prescription; }

    public void Add(Delegation delegation) { lock (_gate) _delegations.Add(delegation); }

    public Prescription? FindPrescription(int id) { lock (_gate) return _prescriptions.GetValueOrDefault(id); }

    public RefillRequest? FindRequest(int id) { lock (_gate) return _requests.Find(r => r.Id == id); }

    public IReadOnlyList<RefillRequest> RequestsFor(int prescriptionId)
    {
        lock (_gate) return [.. _requests.Where(r => r.PrescriptionId == prescriptionId)];
    }

    public IReadOnlyList<Delegation> DelegationsOf(int caregiverId)
    {
        lock (_gate) return [.. _delegations.Where(d => d.CaregiverId == caregiverId)];
    }

    public RefillRequest AddRequest(int prescriptionId, int requestedBy, RefillStatus status)
    {
        lock (_gate)
        {
            var request = new RefillRequest(_requests.Count + 1, prescriptionId, requestedBy, status);
            _requests.Add(request);
            return request;
        }
    }

    public void Save(Prescription prescription) { lock (_gate) _prescriptions[prescription.Id] = prescription; }
}
