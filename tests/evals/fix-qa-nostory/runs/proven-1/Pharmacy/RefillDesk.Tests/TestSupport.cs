using Microsoft.Extensions.Time.Testing;

namespace RefillDesk.Tests;

/// <summary>A desk on an in-memory store behind a recording decorator, at a fixed instant.</summary>
internal sealed class DeskFixture
{
    /// <summary>09:00 UTC on 20 Nov 2026, which is 12:00 on the 20th in Riyadh.</summary>
    public static readonly DateTimeOffset Morning = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The Riyadh date at <see cref="Morning"/>.</summary>
    public static readonly DateOnly Today = new(2026, 11, 20);

    /// <summary>The last tick of 20 Nov in Riyadh, in UTC.</summary>
    public static readonly DateTimeOffset LastTickOfTheDay = new DateTimeOffset(2026, 11, 20, 21, 0, 0, TimeSpan.Zero).AddTicks(-1);

    /// <summary>Midnight starting 21 Nov in Riyadh, in UTC.</summary>
    public static readonly DateTimeOffset RiyadhMidnight = new(2026, 11, 20, 21, 0, 0, TimeSpan.Zero);

    public const int PatientId = 10;
    public const int OtherPatientId = 11;
    public const int CaregiverId = 20;
    public const int OtherCaregiverId = 21;
    public const int StrangerId = 30;

    public DeskFixture(DateTimeOffset? utcNow = null)
    {
        Time = new FakeTimeProvider(utcNow ?? Morning);
        Store = new RecordingStore(Inner);
        Desk = new Desk(Store, Time);
    }

    public FakeTimeProvider Time { get; }
    public InMemoryRefillStore Inner { get; } = new();
    public RecordingStore Store { get; }
    public Desk Desk { get; }

    /// <summary>A prescription that a request from its patient on <see cref="Today"/> accepts:
    /// runsOut is 24 Nov, so the window opened on 17 Nov.</summary>
    public static Prescription Rx(int id = 1, int patientId = PatientId, bool controlled = false, int refillsLeft = 3,
        DateOnly? expiresOn = null, DateOnly? lastDispensedOn = null, int daysSupply = 30, string medicine = "Metformin")
        => new(id, patientId, medicine, controlled, refillsLeft, expiresOn ?? new DateOnly(2027, 6, 1),
            lastDispensedOn ?? new DateOnly(2026, 10, 25), daysSupply);

    public Prescription Add(Prescription prescription)
    {
        Inner.Add(prescription);
        return prescription;
    }

    public void Delegate(int caregiverId, int patientId, DateOnly from, DateOnly to)
        => Inner.Add(new Delegation(caregiverId, patientId, from, to));

    /// <summary>Puts a request straight into the store in any status, bypassing the desk's checks.</summary>
    public RefillRequest Seed(int prescriptionId, RefillStatus status, int requestedBy = PatientId)
        => Inner.AddRequest(prescriptionId, requestedBy, status);

    /// <summary>A request of the given status reached through the desk's own moves.</summary>
    public RefillRequest Reach(RefillStatus status, int prescriptionId = 1)
    {
        var controlled = Inner.FindPrescription(prescriptionId)!.Controlled;
        var request = Desk.Request(PatientId, prescriptionId).Request!;
        var path = status switch
        {
            RefillStatus.Requested or RefillStatus.PendingApproval => [],
            RefillStatus.Approved => new[] { RefillStatus.Approved },
            RefillStatus.Rejected => [RefillStatus.Rejected],
            RefillStatus.Dispensed => [RefillStatus.Approved, RefillStatus.Dispensed],
            RefillStatus.Cancelled => [RefillStatus.Cancelled],
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
        var needsControlled = status is RefillStatus.PendingApproval or RefillStatus.Rejected;
        if (status != RefillStatus.Approved && status != RefillStatus.Dispensed && status != RefillStatus.Cancelled
            && controlled != needsControlled)
            throw new InvalidOperationException($"prescription {prescriptionId} can't reach {status}");
        foreach (var step in path)
            if (!Desk.Move(request.Id, step))
                throw new InvalidOperationException($"could not move to {step}");
        return request;
    }
}

/// <summary>Wraps a store, counts the writes, and can slow reads down to widen race windows.</summary>
internal sealed class RecordingStore(IRefillStore inner) : IRefillStore
{
    private int _adds;
    private int _saves;

    public int Adds => Volatile.Read(ref _adds);
    public int Saves => Volatile.Read(ref _saves);
    public TimeSpan ReadDelay { get; set; } = TimeSpan.Zero;

    public Prescription? FindPrescription(int id)
    {
        var found = inner.FindPrescription(id);
        Pause();
        return found;
    }

    public RefillRequest? FindRequest(int id)
    {
        var found = inner.FindRequest(id);
        Pause();
        return found;
    }

    public IReadOnlyList<RefillRequest> RequestsFor(int prescriptionId)
    {
        var found = inner.RequestsFor(prescriptionId);
        Pause();
        return found;
    }

    public IReadOnlyList<Delegation> DelegationsOf(int caregiverId) => inner.DelegationsOf(caregiverId);

    public RefillRequest AddRequest(int prescriptionId, int requestedBy, RefillStatus status)
    {
        Interlocked.Increment(ref _adds);
        return inner.AddRequest(prescriptionId, requestedBy, status);
    }

    public void Save(Prescription prescription)
    {
        Interlocked.Increment(ref _saves);
        inner.Save(prescription);
    }

    private void Pause()
    {
        if (ReadDelay > TimeSpan.Zero)
            Thread.Sleep(ReadDelay);
    }
}
