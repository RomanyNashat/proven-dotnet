using Microsoft.Extensions.Time.Testing;

namespace RefillDesk.Tests;

/// <summary>
/// One desk on an in-memory store with a fake clock. Riyadh is UTC+3, so a Riyadh day runs from 21:00 UTC
/// the day before to 20:59:59.999 UTC on the day.
/// </summary>
internal sealed class TestPharmacy
{
    public const int PatientId = 10;
    public const int OtherPatientId = 20;
    public const int CaregiverId = 50;
    public const int OtherCaregiverId = 60;
    public const int StrangerId = 99;

    /// <summary>The default "today": 20 Nov 2026, 12:00 in Riyadh.</summary>
    public static readonly DateOnly DefaultDay = new(2026, 11, 20);

    public TestPharmacy(IRefillStore? store = null, DateTimeOffset? utcNow = null)
    {
        // FakeTimeProvider can only move forward, so each test picks its starting instant here.
        Time = new FakeTimeProvider(utcNow ?? RiyadhNoon(DefaultDay));
        Memory = new InMemoryRefillStore();
        Desk = new Desk(store ?? Memory, Time);
    }

    public FakeTimeProvider Time { get; }

    public InMemoryRefillStore Memory { get; }

    public Desk Desk { get; }

    public static DateTimeOffset RiyadhNoon(DateOnly day) =>
        new(day.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);

    public static DateTimeOffset Utc(int year, int month, int day, int hour, int minute, int second = 0) =>
        new(year, month, day, hour, minute, second, TimeSpan.Zero);

    /// <summary>
    /// A prescription for <see cref="PatientId"/> that, on <see cref="DefaultDay"/>, is valid, has refills
    /// and is inside its refill window (runs out 24 Nov, window opened 17 Nov).
    /// </summary>
    public static TestPharmacy At(DateTimeOffset utcNow) => new(utcNow: utcNow);

    public static TestPharmacy OnRiyadhDay(DateOnly day) => new(utcNow: RiyadhNoon(day));

    public static Prescription Rx(int id = 1, int patientId = PatientId, bool controlled = false,
        int refillsLeft = 3, DateOnly? expiresOn = null, DateOnly? lastDispensedOn = null, int daysSupply = 30) =>
        new(id, patientId, "Metformin", controlled, refillsLeft,
            expiresOn ?? new DateOnly(2027, 6, 1), lastDispensedOn ?? new DateOnly(2026, 10, 25), daysSupply);

    public Prescription Add(Prescription prescription)
    {
        Memory.Add(prescription);
        return prescription;
    }

    public void Delegate(int caregiverId, int patientId, DateOnly from, DateOnly to) =>
        Memory.Add(new Delegation(caregiverId, patientId, from, to));

    public void SetRiyadhDay(DateOnly day) => Time.SetUtcNow(RiyadhNoon(day));

    public RefillResult Request(int requesterId = PatientId, int prescriptionId = 1) =>
        Desk.Request(requesterId, prescriptionId);

    public int RefillsLeft(int prescriptionId = 1) => Memory.FindPrescription(prescriptionId)!.RefillsLeft;

    public RefillStatus StatusOf(int requestId) => Memory.FindRequest(requestId)!.Status;

    /// <summary>
    /// Adds prescription 1 and a request on it, then walks the request to <paramref name="status"/> through
    /// allowed moves only. Uses a 7-day supply so the window stays open after a dispense on the same day.
    /// </summary>
    public RefillRequest RequestIn(RefillStatus status)
    {
        var controlled = status is RefillStatus.PendingApproval or RefillStatus.Rejected;
        Add(Rx(controlled: controlled, daysSupply: 7));
        var request = Request().Request!;

        RefillStatus[] path = status switch
        {
            RefillStatus.Requested or RefillStatus.PendingApproval => [],
            RefillStatus.Approved => [RefillStatus.Approved],
            RefillStatus.Rejected => [RefillStatus.Rejected],
            RefillStatus.Cancelled => [RefillStatus.Cancelled],
            RefillStatus.Dispensed => [RefillStatus.Approved, RefillStatus.Dispensed],
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
        foreach (var step in path)
        {
            if (!Desk.Move(request.Id, step))
                throw new InvalidOperationException($"Setup could not move request to {step}.");
        }

        if (StatusOf(request.Id) != status)
            throw new InvalidOperationException($"Setup expected {status}, got {StatusOf(request.Id)}.");
        return request;
    }
}
