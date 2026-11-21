using FluentAssertions;
using static RefillDesk.Tests.TestWorld;

namespace RefillDesk.Tests;

public sealed class MoveTests
{
    private static readonly HashSet<(RefillStatus From, RefillStatus To)> Allowed =
    [
        (RefillStatus.Requested, RefillStatus.Approved),
        (RefillStatus.Requested, RefillStatus.Cancelled),
        (RefillStatus.PendingApproval, RefillStatus.Approved),
        (RefillStatus.PendingApproval, RefillStatus.Rejected),
        (RefillStatus.PendingApproval, RefillStatus.Cancelled),
        (RefillStatus.Approved, RefillStatus.Dispensed),
    ];

    public static TheoryData<RefillStatus, RefillStatus, bool> AllPairs()
    {
        var data = new TheoryData<RefillStatus, RefillStatus, bool>();
        foreach (var from in Enum.GetValues<RefillStatus>())
        foreach (var to in Enum.GetValues<RefillStatus>())
            data.Add(from, to, Allowed.Contains((from, to)));
        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void TransitionTable(RefillStatus from, RefillStatus to, bool allowed)
    {
        var w = new TestWorld();
        var request = w.RequestIn(from);
        var before = w.Rx1;

        var moved = w.Desk.Move(request.Id, to);

        moved.Should().Be(allowed);
        request.Status.Should().Be(allowed ? to : from);
        w.Store.FindRequest(request.Id)!.Status.Should().Be(allowed ? to : from);
        if (allowed && to == RefillStatus.Dispensed)
            w.Rx1.Should().Be(before with { RefillsLeft = before.RefillsLeft - 1, LastDispensedOn = DefaultToday });
        else
            w.Rx1.Should().Be(before);
    }

    [Fact]
    public void Move_UnknownRequest_IsFalse()
    {
        var w = new TestWorld();
        w.RequestIn(RefillStatus.Requested);

        foreach (var to in Enum.GetValues<RefillStatus>())
            w.Desk.Move(999, to).Should().BeFalse();
        w.Desk.Move(0, RefillStatus.Approved).Should().BeFalse();
    }

    [Fact]
    public void Move_UndefinedStatusValue_IsFalse()
    {
        var w = new TestWorld();
        var r = w.RequestIn(RefillStatus.Approved);

        w.Desk.Move(r.Id, (RefillStatus)42).Should().BeFalse();
        r.Status.Should().Be(RefillStatus.Approved);
    }

    [Fact]
    public void Approve_DoesNotTouchThePrescription()
    {
        var w = new TestWorld();
        var rx = w.AddRx(refillsLeft: 2);
        var r = w.RequestOk();

        w.MoveOk(r.Id, RefillStatus.Approved);

        w.Rx1.Should().Be(rx);
    }

    [Fact]
    public void Dispense_UsesOneRefill_AndSetsLastDispensedToTodayInRiyadh_KeepingEverythingElse()
    {
        var w = new TestWorld();
        var rx = w.AddRx(Rx(controlled: true, refillsLeft: 2, expiresOn: new DateOnly(2027, 1, 1),
            lastDispensedOn: new DateOnly(2026, 10, 25), daysSupply: 30, medicine: "Oxycodone"));
        var r = w.RequestOk();
        w.MoveOk(r.Id, RefillStatus.Approved);

        w.SetUtc(2026, 11, 22, 21, 30); // 00:30 on 2026-11-23 in Riyadh
        w.MoveOk(r.Id, RefillStatus.Dispensed);

        w.Rx1.Should().Be(rx with { RefillsLeft = 1, LastDispensedOn = new DateOnly(2026, 11, 23) });
        w.Rx1.Medicine.Should().Be("Oxycodone");
        w.Rx1.Controlled.Should().BeTrue();
    }

    [Fact]
    public void Dispense_LastDayInRiyadh_BeforeMidnight()
    {
        var w = new TestWorld();
        w.AddRx();
        var r = w.RequestOk();
        w.MoveOk(r.Id, RefillStatus.Approved);

        w.SetUtc(2026, 11, 22, 20, 59, 59); // 23:59:59 on 2026-11-22 in Riyadh
        w.MoveOk(r.Id, RefillStatus.Dispensed);

        w.Rx1.LastDispensedOn.Should().Be(new DateOnly(2026, 11, 22));
    }

    [Fact]
    public void Dispense_DoesNotTouchOtherPrescriptions()
    {
        var w = new TestWorld();
        var other = w.AddRx(Rx(id: 2, patientId: OtherPatient, refillsLeft: 5));
        var r = w.RequestIn(RefillStatus.Approved);

        w.MoveOk(r.Id, RefillStatus.Dispensed);

        w.Store.FindPrescription(2).Should().Be(other);
    }

    [Fact]
    public void Dispense_OfLastRefill_LeavesZero_AndFurtherRequestsAreRefused()
    {
        var w = new TestWorld();
        w.AddRx(refillsLeft: 1, daysSupply: 0);
        var r = w.RequestOk();
        w.MoveOk(r.Id, RefillStatus.Approved);
        w.MoveOk(r.Id, RefillStatus.Dispensed);

        w.Rx1.RefillsLeft.Should().Be(0);
        w.Desk.Request(Patient, RxId).Refusal.Should().Be(RefillRefusal.NoRefillsLeft);
    }

    [Fact]
    public void Dispense_IsNotRecheckedAgainstExpiryOrDelegation()
    {
        // Move only checks the transition; eligibility is judged when the request is made.
        var w = new TestWorld();
        w.AddRx(expiresOn: DefaultToday);
        w.Delegate(Caregiver, Patient, DefaultToday, DefaultToday);
        var r = w.RequestOk(requester: Caregiver);
        w.MoveOk(r.Id, RefillStatus.Approved);

        w.SetToday(DefaultToday.AddDays(5)); // prescription expired, delegation lapsed
        w.MoveOk(r.Id, RefillStatus.Dispensed);

        w.Rx1.RefillsLeft.Should().Be(2);
        w.Rx1.LastDispensedOn.Should().Be(DefaultToday.AddDays(5));
    }

    [Fact]
    public void Dispense_IsNotRecheckedAgainstRefillsLeft_CanGoNegative()
    {
        // Current behaviour: an Approved request doesn't block a new one, and dispensing
        // doesn't re-check RefillsLeft, so two dispenses can drive it below zero. See report.md.
        var w = new TestWorld();
        w.AddRx(refillsLeft: 1);
        var a = w.RequestOk();
        w.MoveOk(a.Id, RefillStatus.Approved);
        var b = w.RequestOk();
        w.MoveOk(b.Id, RefillStatus.Approved);

        w.MoveOk(a.Id, RefillStatus.Dispensed);
        w.MoveOk(b.Id, RefillStatus.Dispensed);

        w.Rx1.RefillsLeft.Should().Be(-1);
    }

    [Fact]
    public void Dispense_Twice_SecondIsRefused_AndRefillUsedOnce()
    {
        var w = new TestWorld();
        var r = w.RequestIn(RefillStatus.Approved);

        w.Desk.Move(r.Id, RefillStatus.Dispensed).Should().BeTrue();
        w.Desk.Move(r.Id, RefillStatus.Dispensed).Should().BeFalse();

        w.Rx1.RefillsLeft.Should().Be(2);
    }

    [Fact]
    public void Move_OnlyAffectsTheNamedRequest()
    {
        var w = new TestWorld();
        w.AddRx(id: 1);
        w.AddRx(id: 2);
        var a = w.RequestOk(rxId: 1);
        var b = w.RequestOk(rxId: 2);

        w.MoveOk(b.Id, RefillStatus.Cancelled);

        a.Status.Should().Be(RefillStatus.Requested);
        b.Status.Should().Be(RefillStatus.Cancelled);
    }

    [Fact]
    public void FullHappyPath_NonControlled()
    {
        var w = new TestWorld();
        w.AddRx(refillsLeft: 3);
        var r = w.RequestOk();
        r.Status.Should().Be(RefillStatus.Requested);
        w.MoveOk(r.Id, RefillStatus.Approved);
        w.MoveOk(r.Id, RefillStatus.Dispensed);

        r.Status.Should().Be(RefillStatus.Dispensed);
        w.Rx1.RefillsLeft.Should().Be(2);
        w.Rx1.LastDispensedOn.Should().Be(DefaultToday);
    }

    [Fact]
    public void FullHappyPath_Controlled()
    {
        var w = new TestWorld();
        w.AddRx(controlled: true, refillsLeft: 3);
        var r = w.RequestOk();
        r.Status.Should().Be(RefillStatus.PendingApproval);
        w.MoveOk(r.Id, RefillStatus.Approved);
        w.MoveOk(r.Id, RefillStatus.Dispensed);

        r.Status.Should().Be(RefillStatus.Dispensed);
        w.Rx1.RefillsLeft.Should().Be(2);
    }

    [Fact]
    public void Requested_CannotBeRejected_ButCanBeCancelled()
    {
        // Current behaviour for non-controlled requests. See report.md.
        var w = new TestWorld();
        var r = w.RequestIn(RefillStatus.Requested);

        w.Desk.Move(r.Id, RefillStatus.Rejected).Should().BeFalse();
        r.Status.Should().Be(RefillStatus.Requested);
        w.Desk.Move(r.Id, RefillStatus.Cancelled).Should().BeTrue();
    }

    [Fact]
    public void Approved_CannotBeCancelledOrRejected()
    {
        var w = new TestWorld();
        var r = w.RequestIn(RefillStatus.Approved);

        w.Desk.Move(r.Id, RefillStatus.Cancelled).Should().BeFalse();
        w.Desk.Move(r.Id, RefillStatus.Rejected).Should().BeFalse();
        r.Status.Should().Be(RefillStatus.Approved);
    }
}
