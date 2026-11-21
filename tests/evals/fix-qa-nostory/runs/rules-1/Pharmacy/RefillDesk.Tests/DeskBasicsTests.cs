using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace RefillDesk.Tests;

public sealed class DeskBasicsTests
{
    [Fact]
    public void PharmacyOffset_IsPlusThreeHours() => Desk.PharmacyOffset.Should().Be(TimeSpan.FromHours(3));

    [Theory]
    [InlineData("2026-11-20T00:00:00Z", "2026-11-20")]
    [InlineData("2026-11-20T20:59:59Z", "2026-11-20")]
    [InlineData("2026-11-20T21:00:00Z", "2026-11-21")]
    [InlineData("2026-11-20T23:59:59Z", "2026-11-21")]
    [InlineData("2026-12-31T21:00:00Z", "2027-01-01")]
    [InlineData("2026-06-15T21:00:00Z", "2026-06-16")] // no daylight saving: same offset in summer
    [InlineData("2026-11-20T23:30:00+05:00", "2026-11-20")] // 18:30Z -> 21:30 Riyadh
    public void Today_IsTheRiyadhDate(string now, string expected)
    {
        var desk = new Desk(new InMemoryRefillStore(), new FakeTimeProvider(DateTimeOffset.Parse(now)));

        desk.Today.Should().Be(DateOnly.Parse(expected));
    }

    [Fact]
    public void Today_FollowsTheClock()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 20, 20, 0, 0, TimeSpan.Zero));
        var desk = new Desk(new InMemoryRefillStore(), time);
        desk.Today.Should().Be(new DateOnly(2026, 11, 20));

        time.Advance(TimeSpan.FromHours(1));
        desk.Today.Should().Be(new DateOnly(2026, 11, 21));
    }

    [Fact]
    public void RefillResult_Ok_OnlyForNone()
    {
        foreach (var refusal in Enum.GetValues<RefillRefusal>())
            new RefillResult(refusal, null).Ok.Should().Be(refusal == RefillRefusal.None);
    }

    [Fact]
    public void Enums_HaveTheExpectedMembers()
    {
        Enum.GetValues<RefillStatus>().Should().Equal(
            RefillStatus.Requested, RefillStatus.PendingApproval, RefillStatus.Approved,
            RefillStatus.Rejected, RefillStatus.Dispensed, RefillStatus.Cancelled);
        Enum.GetValues<RefillRefusal>().Should().Equal(
            RefillRefusal.None, RefillRefusal.NotYourPrescription, RefillRefusal.Expired,
            RefillRefusal.NoRefillsLeft, RefillRefusal.TooEarly, RefillRefusal.AlreadyOpen, RefillRefusal.NotFound);
        ((int)RefillRefusal.None).Should().Be(0);
        ((int)RefillStatus.Requested).Should().Be(0);
    }
}

public sealed class InMemoryRefillStoreTests
{
    private static Prescription Rx(int id, int patient = 10, int refills = 3) =>
        new(id, patient, "M", false, refills, new DateOnly(2027, 1, 1), new DateOnly(2026, 11, 1), 30);

    [Fact]
    public void Prescriptions_AddFindSave()
    {
        var store = new InMemoryRefillStore();
        store.FindPrescription(1).Should().BeNull();

        store.Add(Rx(1));
        store.Add(Rx(2, refills: 9));
        store.FindPrescription(1).Should().Be(Rx(1));

        store.Save(Rx(1, refills: 0));
        store.FindPrescription(1).Should().Be(Rx(1, refills: 0));
        store.FindPrescription(2).Should().Be(Rx(2, refills: 9));

        store.Add(Rx(1, refills: 5)); // Add replaces an existing id
        store.FindPrescription(1)!.RefillsLeft.Should().Be(5);
    }

    [Fact]
    public void Requests_GetSequentialIdsFromOne_AndAreFilteredByPrescription()
    {
        var store = new InMemoryRefillStore();
        store.FindRequest(1).Should().BeNull();

        var a = store.AddRequest(1, 10, RefillStatus.Requested);
        var b = store.AddRequest(2, 20, RefillStatus.PendingApproval);
        var c = store.AddRequest(1, 20, RefillStatus.Requested);

        new[] { a.Id, b.Id, c.Id }.Should().Equal(1, 2, 3);
        b.PrescriptionId.Should().Be(2);
        b.RequestedBy.Should().Be(20);
        b.Status.Should().Be(RefillStatus.PendingApproval);
        store.FindRequest(2).Should().BeSameAs(b);
        store.RequestsFor(1).Should().Equal(a, c);
        store.RequestsFor(2).Should().Equal(b);
        store.RequestsFor(3).Should().BeEmpty();
    }

    [Fact]
    public void RequestsFor_ReturnsASnapshot()
    {
        var store = new InMemoryRefillStore();
        store.AddRequest(1, 10, RefillStatus.Requested);
        var snapshot = store.RequestsFor(1);

        store.AddRequest(1, 10, RefillStatus.Requested);

        snapshot.Should().HaveCount(1);
        store.RequestsFor(1).Should().HaveCount(2);
    }

    [Fact]
    public void Delegations_AreFilteredByCaregiver()
    {
        var store = new InMemoryRefillStore();
        var d1 = new Delegation(20, 10, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var d2 = new Delegation(21, 10, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var d3 = new Delegation(20, 11, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        store.Add(d1); store.Add(d2); store.Add(d3);

        store.DelegationsOf(20).Should().Equal(d1, d3);
        store.DelegationsOf(21).Should().Equal(d2);
        store.DelegationsOf(10).Should().BeEmpty();
    }

    [Fact]
    public void Desk_AssignsRequestIdsAcrossPrescriptions()
    {
        var w = new TestWorld();
        w.AddRx(id: 1);
        w.AddRx(id: 2);

        w.RequestOk(rxId: 1).Id.Should().Be(1);
        w.RequestOk(rxId: 2).Id.Should().Be(2);
    }
}
