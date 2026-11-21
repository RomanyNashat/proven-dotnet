using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace RefillDesk.Tests;

/// <summary>A desk on an in-memory store with a fake clock, plus shorthands for building scenarios.</summary>
internal sealed class TestWorld
{
    public const int Patient = 10;
    public const int OtherPatient = 11;
    public const int Caregiver = 20;
    public const int Stranger = 99;
    public const int RxId = 1;

    /// <summary>Default "today" (Riyadh date) for scenarios.</summary>
    public static readonly DateOnly DefaultToday = new(2026, 11, 20);

    public FakeTimeProvider Time { get; }
    public InMemoryRefillStore Store { get; }
    public Desk Desk { get; }

    public TestWorld(DateOnly? today = null, IRefillStore? store = null)
    {
        Time = new FakeTimeProvider(RiyadhNoon(today ?? DefaultToday));
        Store = new InMemoryRefillStore();
        Desk = new Desk(store ?? Store, Time);
    }

    public static DateTimeOffset RiyadhNoon(DateOnly date) =>
        new(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.FromHours(3));

    public void SetToday(DateOnly date) => Time.SetUtcNow(RiyadhNoon(date));

    public void SetUtc(int y, int m, int d, int h, int min, int s = 0) =>
        Time.SetUtcNow(new DateTimeOffset(y, m, d, h, min, s, TimeSpan.Zero));

    /// <summary>
    /// A prescription that is eligible today (DefaultToday): runs out on DefaultToday + 2,
    /// so the window opened DefaultToday - 5.
    /// </summary>
    public static Prescription Rx(int id = RxId, int patientId = Patient, bool controlled = false, int refillsLeft = 3,
        DateOnly? expiresOn = null, DateOnly? lastDispensedOn = null, int daysSupply = 30, string medicine = "Metformin") =>
        new(id, patientId, medicine, controlled, refillsLeft,
            expiresOn ?? new DateOnly(2027, 6, 1),
            lastDispensedOn ?? DefaultToday.AddDays(2 - 30),
            daysSupply);

    public Prescription AddRx(Prescription rx)
    {
        Store.Add(rx);
        return rx;
    }

    public Prescription AddRx(int id = RxId, int patientId = Patient, bool controlled = false, int refillsLeft = 3,
        DateOnly? expiresOn = null, DateOnly? lastDispensedOn = null, int daysSupply = 30) =>
        AddRx(Rx(id, patientId, controlled, refillsLeft, expiresOn, lastDispensedOn, daysSupply));

    public void Delegate(int caregiver, int patient, DateOnly from, DateOnly to) =>
        Store.Add(new Delegation(caregiver, patient, from, to));

    public RefillRequest RequestOk(int requester = Patient, int rxId = RxId)
    {
        var result = Desk.Request(requester, rxId);
        result.Refusal.Should().Be(RefillRefusal.None);
        result.Ok.Should().BeTrue();
        result.Request.Should().NotBeNull();
        return result.Request!;
    }

    public void MoveOk(int requestId, RefillStatus to) => Desk.Move(requestId, to).Should().BeTrue();

    /// <summary>Creates a prescription (id <paramref name="rxId"/>) and a request on it that is now in <paramref name="status"/>.</summary>
    public RefillRequest RequestIn(RefillStatus status, int rxId = RxId)
    {
        var controlled = status is RefillStatus.PendingApproval or RefillStatus.Rejected;
        AddRx(id: rxId, controlled: controlled);
        var request = RequestOk(rxId: rxId);
        switch (status)
        {
            case RefillStatus.Requested:
            case RefillStatus.PendingApproval:
                break;
            case RefillStatus.Approved:
                MoveOk(request.Id, RefillStatus.Approved);
                break;
            case RefillStatus.Rejected:
                MoveOk(request.Id, RefillStatus.Rejected);
                break;
            case RefillStatus.Cancelled:
                MoveOk(request.Id, RefillStatus.Cancelled);
                break;
            case RefillStatus.Dispensed:
                MoveOk(request.Id, RefillStatus.Approved);
                MoveOk(request.Id, RefillStatus.Dispensed);
                break;
        }
        request.Status.Should().Be(status);
        return request;
    }

    public Prescription Rx1 => Store.FindPrescription(RxId)!;
}
