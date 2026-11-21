using FluentAssertions;
using static RefillDesk.Tests.DeskFixture;

namespace RefillDesk.Tests;

[Trait("AC", "AC13")]
public sealed class ContractTests
{
    [Fact]
    [Trait("EC", "EC51")]
    public void Constants_PharmacyOffsetAndWindowDays_AreThreeHoursAndSevenDays()
    {
        Desk.PharmacyOffset.Should().Be(TimeSpan.FromHours(3));
        Desk.PharmacyOffset.Ticks.Should().Be(3 * TimeSpan.TicksPerHour);
        Desk.WindowDays.Should().Be(7);
    }

    [Fact]
    [Trait("EC", "EC52")]
    public void RefillStatus_Members_KeepTheirNamesAndValues()
    {
        Enum.GetValues<RefillStatus>().Select(s => (s.ToString(), (int)s)).Should().Equal(
            ("Requested", 0), ("PendingApproval", 1), ("Approved", 2), ("Rejected", 3), ("Dispensed", 4), ("Cancelled", 5));
    }

    [Fact]
    [Trait("EC", "EC52")]
    public void RefillRefusal_Members_KeepTheirNamesAndValues()
    {
        Enum.GetValues<RefillRefusal>().Select(s => (s.ToString(), (int)s)).Should().Equal(
            ("None", 0), ("NotYourPrescription", 1), ("Expired", 2), ("NoRefillsLeft", 3), ("TooEarly", 4),
            ("AlreadyOpen", 5), ("NotFound", 6));
    }

    public static TheoryData<RefillRefusal> EveryRefusal() => [.. Enum.GetValues<RefillRefusal>()];

    [Theory]
    [MemberData(nameof(EveryRefusal))]
    [Trait("EC", "EC53")]
    public void RefillResult_Ok_IsTrueOnlyForNone(RefillRefusal refusal)
    {
        new RefillResult(refusal, null).Ok.Should().Be(refusal == RefillRefusal.None);
    }

    [Fact]
    [Trait("EC", "EC53")]
    public void RefillResult_Equality_IsByValue()
    {
        var request = new RefillRequest(1, 2, 3, RefillStatus.Requested);

        new RefillResult(RefillRefusal.None, request).Should().Be(new RefillResult(RefillRefusal.None, request));
        new RefillResult(RefillRefusal.Expired, null).Should().NotBe(new RefillResult(RefillRefusal.TooEarly, null));
        new RefillResult(RefillRefusal.None, request).Ok.Should().BeTrue();
    }

    [Fact]
    [Trait("EC", "EC53")]
    public void RefillRequest_Constructor_ExposesItsValues()
    {
        var request = new RefillRequest(id: 4, prescriptionId: 5, requestedBy: 6, RefillStatus.PendingApproval);

        (request.Id, request.PrescriptionId, request.RequestedBy, request.Status)
            .Should().Be((4, 5, 6, RefillStatus.PendingApproval));
    }
}

[Trait("AC", "AC14")]
public sealed class InMemoryRefillStoreTests
{
    [Fact]
    [Trait("EC", "EC54")]
    public void AddRequest_AcrossPrescriptions_NumbersFromOneAndKeepsTheValues()
    {
        var store = new InMemoryRefillStore();

        var a = store.AddRequest(1, 10, RefillStatus.Requested);
        var b = store.AddRequest(2, 20, RefillStatus.Dispensed);
        var c = store.AddRequest(1, 30, RefillStatus.Cancelled);

        new[] { a, b, c }.Select(r => (r.Id, r.PrescriptionId, r.RequestedBy, r.Status)).Should().Equal(
            (1, 1, 10, RefillStatus.Requested), (2, 2, 20, RefillStatus.Dispensed), (3, 1, 30, RefillStatus.Cancelled));
        store.FindRequest(2).Should().BeSameAs(b);
    }

    [Fact]
    [Trait("EC", "EC55")]
    public void Find_UnknownIds_ReturnNull()
    {
        var store = new InMemoryRefillStore();
        store.Add(Rx(id: 1));
        store.AddRequest(1, 10, RefillStatus.Requested);

        store.FindPrescription(2).Should().BeNull();
        store.FindRequest(0).Should().BeNull();
        store.FindRequest(2).Should().BeNull();
        store.RequestsFor(2).Should().BeEmpty();
        store.DelegationsOf(99).Should().BeEmpty();
    }

    [Fact]
    [Trait("EC", "EC56")]
    public void RequestsForAndDelegationsOf_ReturnOnlyTheirOwnRowsInOrder()
    {
        var store = new InMemoryRefillStore();
        var r1 = store.AddRequest(1, 10, RefillStatus.Requested);
        store.AddRequest(2, 10, RefillStatus.Requested);
        var r3 = store.AddRequest(1, 10, RefillStatus.Approved);
        var d1 = new Delegation(20, 10, Today, Today);
        var d2 = new Delegation(21, 10, Today, Today);
        var d3 = new Delegation(20, 11, Today, Today.AddDays(1));
        store.Add(d1);
        store.Add(d2);
        store.Add(d3);

        store.RequestsFor(1).Should().Equal(r1, r3);
        store.DelegationsOf(20).Should().Equal(d1, d3);
        store.DelegationsOf(21).Should().Equal(d2);
    }

    [Fact]
    [Trait("EC", "EC57")]
    public void AddAndSave_SameId_ReplaceThePrescription()
    {
        var store = new InMemoryRefillStore();
        store.Add(Rx(id: 1, refillsLeft: 3));
        store.Add(Rx(id: 2, refillsLeft: 9));

        store.Add(Rx(id: 1, refillsLeft: 2));
        store.FindPrescription(1)!.RefillsLeft.Should().Be(2);

        store.Save(Rx(id: 1, refillsLeft: 1));
        store.FindPrescription(1)!.RefillsLeft.Should().Be(1);
        store.FindPrescription(2)!.RefillsLeft.Should().Be(9);

        store.Save(Rx(id: 3, refillsLeft: 4));
        store.FindPrescription(3)!.RefillsLeft.Should().Be(4);
    }
}
