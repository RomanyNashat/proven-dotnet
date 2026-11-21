using FluentAssertions;
using static RefillDesk.Tests.DeskFixture;

namespace RefillDesk.Tests;

public sealed class MoveTests
{
    private static readonly (RefillStatus From, RefillStatus To)[] Allowed =
    [
        (RefillStatus.Requested, RefillStatus.Approved),
        (RefillStatus.Requested, RefillStatus.Cancelled),
        (RefillStatus.PendingApproval, RefillStatus.Approved),
        (RefillStatus.PendingApproval, RefillStatus.Rejected),
        (RefillStatus.PendingApproval, RefillStatus.Cancelled),
        (RefillStatus.Approved, RefillStatus.Dispensed),
    ];

    public static TheoryData<RefillStatus, RefillStatus, bool> EveryMove()
    {
        var data = new TheoryData<RefillStatus, RefillStatus, bool>();
        foreach (var from in Enum.GetValues<RefillStatus>())
            foreach (var to in Enum.GetValues<RefillStatus>())
                data.Add(from, to, Allowed.Contains((from, to)));
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryMove))]
    [Trait("AC", "AC10"), Trait("EC", "EC39")]
    public void Move_EveryPairOfStatuses_FollowsTheTable(RefillStatus from, RefillStatus to, bool allowed)
    {
        var f = new DeskFixture();
        var rx = f.Add(Rx());
        var request = f.Seed(1, from);

        var moved = f.Desk.Move(request.Id, to);

        moved.Should().Be(allowed);
        request.Status.Should().Be(allowed ? to : from);
        f.Inner.FindRequest(request.Id)!.Status.Should().Be(allowed ? to : from);
        var dispensed = allowed && to == RefillStatus.Dispensed;
        f.Store.Saves.Should().Be(dispensed ? 1 : 0);
        if (!dispensed)
            f.Inner.FindPrescription(1).Should().BeSameAs(rx);
    }

    [Fact]
    [Trait("AC", "AC10")]
    public void Move_TheAllowedMovesTable_HasExactlySixMoves()
    {
        EveryMove().Count(row => (bool)row[2]).Should().Be(6);
        EveryMove().Count().Should().Be(36);
    }

    [Theory]
    [InlineData(RefillStatus.Requested)]
    [InlineData(RefillStatus.PendingApproval)]
    [InlineData(RefillStatus.Approved)]
    [InlineData(RefillStatus.Rejected)]
    [InlineData(RefillStatus.Dispensed)]
    [InlineData(RefillStatus.Cancelled)]
    [Trait("AC", "AC10"), Trait("EC", "EC41")]
    public void Move_ToTheSameStatus_ReturnsFalse(RefillStatus status)
    {
        var f = new DeskFixture();
        f.Add(Rx());
        var request = f.Seed(1, status);

        f.Desk.Move(request.Id, status).Should().BeFalse();
        request.Status.Should().Be(status);
    }

    [Theory]
    [InlineData(0, RefillStatus.Approved)]
    [InlineData(2, RefillStatus.Approved)]
    [InlineData(-1, RefillStatus.Cancelled)]
    [InlineData(99, RefillStatus.Dispensed)]
    [Trait("AC", "AC10"), Trait("EC", "EC40")]
    public void Move_UnknownRequestId_ReturnsFalseAndChangesNothing(int requestId, RefillStatus to)
    {
        var f = new DeskFixture();
        var rx = f.Add(Rx());
        var existing = f.Seed(1, RefillStatus.Approved);

        f.Desk.Move(requestId, to).Should().BeFalse();

        existing.Status.Should().Be(RefillStatus.Approved);
        f.Store.Saves.Should().Be(0);
        f.Inner.FindPrescription(1).Should().BeSameAs(rx);
    }

    [Fact]
    [Trait("AC", "AC10"), Trait("EC", "EC42")]
    public void Move_AfterRequest_ChangesTheReturnedRequestObject()
    {
        var f = new DeskFixture();
        f.Add(Rx(controlled: true));
        var request = f.Desk.Request(PatientId, 1).Request!;

        f.Desk.Move(request.Id, RefillStatus.Rejected).Should().BeTrue();

        request.Status.Should().Be(RefillStatus.Rejected);
        f.Desk.Move(request.Id, RefillStatus.Approved).Should().BeFalse();
        request.Status.Should().Be(RefillStatus.Rejected);
    }

    [Fact]
    [Trait("AC", "AC10")]
    public void Move_RequestedNonControlledToRejected_ReturnsFalse()
    {
        // QUESTION: pinned as it is. A non-controlled refill can't be rejected, only cancelled.
        var f = new DeskFixture();
        f.Add(Rx(controlled: false));
        var request = f.Desk.Request(PatientId, 1).Request!;

        f.Desk.Move(request.Id, RefillStatus.Rejected).Should().BeFalse();
        request.Status.Should().Be(RefillStatus.Requested);
    }

    // ---------- AC11: dispensing ----------

    [Fact]
    [Trait("AC", "AC11"), Trait("EC", "EC43")]
    public void Move_Dispensed_TakesOneRefillAndSetsLastDispensedOnToday_OnlyOnThisPrescription()
    {
        var f = new DeskFixture();
        var rx = f.Add(Rx(id: 1, controlled: true, refillsLeft: 5, medicine: "Tramadol",
            expiresOn: new DateOnly(2027, 2, 3), lastDispensedOn: new DateOnly(2026, 10, 28), daysSupply: 28));
        var other = f.Add(Rx(id: 2, refillsLeft: 5, medicine: "Metformin"));
        var request = f.Reach(RefillStatus.Approved);
        f.Store.Saves.Should().Be(0);

        f.Desk.Move(request.Id, RefillStatus.Dispensed).Should().BeTrue();

        request.Status.Should().Be(RefillStatus.Dispensed);
        f.Store.Saves.Should().Be(1);
        f.Inner.FindPrescription(1).Should().Be(rx with { RefillsLeft = 4, LastDispensedOn = Today });
        f.Inner.FindPrescription(1).Should().Be(new Prescription(1, PatientId, "Tramadol", true, 4,
            new DateOnly(2027, 2, 3), Today, 28));
        f.Inner.FindPrescription(2).Should().BeSameAs(other);
    }

    [Theory]
    [InlineData(RefillStatus.Requested, RefillStatus.Approved)]
    [InlineData(RefillStatus.Requested, RefillStatus.Cancelled)]
    [InlineData(RefillStatus.PendingApproval, RefillStatus.Approved)]
    [InlineData(RefillStatus.PendingApproval, RefillStatus.Rejected)]
    [InlineData(RefillStatus.PendingApproval, RefillStatus.Cancelled)]
    [Trait("AC", "AC11"), Trait("EC", "EC44")]
    public void Move_NonDispenseMove_DoesNotTouchThePrescription(RefillStatus from, RefillStatus to)
    {
        var f = new DeskFixture();
        var rx = f.Add(Rx());
        var request = f.Seed(1, from);

        f.Desk.Move(request.Id, to).Should().BeTrue();

        f.Store.Saves.Should().Be(0);
        f.Inner.FindPrescription(1).Should().BeSameAs(rx);
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(-3, -4)]
    [InlineData(1, 0)]
    [Trait("AC", "AC11"), Trait("EC", "EC45")]
    public void Move_DispensedWithNoRefillsLeft_StillDispensesAndGoesNegative(int refillsLeft, int expected)
    {
        // QUESTION: pinned as it is. Dispensing doesn't re-check refills.
        var f = new DeskFixture();
        f.Add(Rx(refillsLeft: refillsLeft));
        var request = f.Seed(1, RefillStatus.Approved);

        f.Desk.Move(request.Id, RefillStatus.Dispensed).Should().BeTrue();

        f.Inner.FindPrescription(1)!.RefillsLeft.Should().Be(expected);
    }

    [Fact]
    [Trait("AC", "AC11"), Trait("EC", "EC45")]
    public void Move_DispensedAfterThePrescriptionExpired_StillDispenses()
    {
        // QUESTION: pinned as it is. Approved on the expiry day, dispensed the next Riyadh day.
        var f = new DeskFixture();
        f.Add(Rx(expiresOn: Today));
        var request = f.Reach(RefillStatus.Approved);
        f.Time.SetUtcNow(RiyadhMidnight);

        f.Desk.Move(request.Id, RefillStatus.Dispensed).Should().BeTrue();

        f.Inner.FindPrescription(1)!.Should().Be(Rx(expiresOn: Today, refillsLeft: 2, lastDispensedOn: Today.AddDays(1)));
    }

    [Fact]
    [Trait("AC", "AC11"), Trait("EC", "EC46")]
    public void Move_DispensedWhenThePrescriptionIsMissing_ThrowsAfterTheStatusChanged()
    {
        // QUESTION: pinned as it is. The status is set before the prescription is read.
        var f = new DeskFixture();
        var request = f.Seed(prescriptionId: 42, RefillStatus.Approved);

        var act = () => f.Desk.Move(request.Id, RefillStatus.Dispensed);

        act.Should().Throw<NullReferenceException>();
        request.Status.Should().Be(RefillStatus.Dispensed);
        f.Store.Saves.Should().Be(0);
        f.Desk.Move(request.Id, RefillStatus.Dispensed).Should().BeFalse();
    }

    [Fact]
    [Trait("AC", "AC11"), Trait("EC", "EC47")]
    public void Lifecycle_StoryTest_RequestApproveDispense_ThenTheNextRequestIsTooEarly()
    {
        // Given a 30-day prescription with 2 refills left, last dispensed on 25 Oct (runsOut 24 Nov)
        var f = new DeskFixture();
        f.Add(Rx(refillsLeft: 2));
        f.Delegate(CaregiverId, PatientId, Today, Today);

        // When the caregiver asks, the pharmacist approves and dispenses it
        var asked = f.Desk.Request(CaregiverId, 1);
        f.Desk.Move(asked.Request!.Id, RefillStatus.Approved).Should().BeTrue();
        f.Desk.Move(asked.Request.Id, RefillStatus.Dispensed).Should().BeTrue();

        // Then the prescription has one refill left, was dispensed today, and the next window is 23 days away
        f.Inner.FindPrescription(1).Should().Be(Rx(refillsLeft: 1, lastDispensedOn: Today));
        asked.Request.Status.Should().Be(RefillStatus.Dispensed);
        f.Desk.Request(PatientId, 1).Refusal.Should().Be(RefillRefusal.TooEarly);
        f.Desk.Move(asked.Request.Id, RefillStatus.Dispensed).Should().BeFalse();
        f.Inner.FindPrescription(1)!.RefillsLeft.Should().Be(1);
    }

    [Fact]
    [Trait("AC", "AC11"), Trait("EC", "EC45")]
    public void Lifecycle_TwoApprovedRequests_BothDispenseAndTakeTwoRefills()
    {
        // QUESTION: pinned as it is. Approved doesn't block a new request, so two can be dispensed.
        var f = new DeskFixture();
        f.Add(Rx(refillsLeft: 3));
        var first = f.Reach(RefillStatus.Approved);
        var second = f.Reach(RefillStatus.Approved);

        f.Desk.Move(first.Id, RefillStatus.Dispensed).Should().BeTrue();
        f.Desk.Move(second.Id, RefillStatus.Dispensed).Should().BeTrue();

        f.Inner.FindPrescription(1)!.RefillsLeft.Should().Be(1);
    }
}
