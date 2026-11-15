using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace RefillDesk.Tests;

// The reference: cases designed from story.md (decision table, boundaries, every pair of statuses, the
// pharmacy's date against UTC, two requests at once), each naming its criteria.
public sealed class RefillTests
{
    private const int Patient = 10, Caregiver = 20, Stranger = 30;

    // 09:00 at the pharmacy on 20 November 2026.
    private static readonly DateTimeOffset Morning = new(2026, 11, 20, 9, 0, 0, Desk.PharmacyOffset);
    private static readonly DateOnly Day = new(2026, 11, 20);

    private static (Desk Desk, InMemoryRefillStore Store, FakeTimeProvider Time) Setup(
        DateTimeOffset? now = null, bool controlled = false, int refillsLeft = 3, DateOnly? expiresOn = null,
        DateOnly? runsOut = null, Delegation? delegation = null)
    {
        var time = new FakeTimeProvider(now ?? Morning);
        var store = new InMemoryRefillStore();
        var outOn = runsOut ?? Day.AddDays(3);
        store.Add(new Prescription(1, Patient, "Metformin", controlled, refillsLeft, expiresOn ?? new DateOnly(2027, 6, 1),
            LastDispensedOn: outOn.AddDays(-30), DaysSupply: 30));
        if (delegation is not null)
            store.Add(delegation);
        return (new Desk(store, time), store, time);
    }

    [Theory]
    [InlineData(Patient, false, RefillRefusal.None)]
    [InlineData(Caregiver, true, RefillRefusal.None)]
    [InlineData(Caregiver, false, RefillRefusal.NotYourPrescription)]
    [InlineData(Stranger, false, RefillRefusal.NotYourPrescription)]
    [Trait("AC", "AC1")]
    public void Request_WhoAsks_FollowsTheDecisionTable(int requester, bool delegated, RefillRefusal expected)
    {
        var (desk, _, _) = Setup(delegation: delegated ? new Delegation(Caregiver, Patient, Day.AddDays(-10), Day.AddDays(10)) : null);

        desk.Request(requester, 1).Refusal.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 5, RefillRefusal.None)]       // first day of the delegation
    [InlineData(-5, 0, RefillRefusal.None)]      // last day of the delegation
    [InlineData(1, 5, RefillRefusal.NotYourPrescription)]
    [InlineData(-5, -1, RefillRefusal.NotYourPrescription)]
    [Trait("AC", "AC1")]
    public void Request_DelegationEdges_BothDaysIncluded(int fromOffset, int toOffset, RefillRefusal expected)
    {
        var (desk, _, _) = Setup(delegation: new Delegation(Caregiver, Patient, Day.AddDays(fromOffset), Day.AddDays(toOffset)));

        desk.Request(Caregiver, 1).Refusal.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, RefillRefusal.None)]          // expires today: still valid
    [InlineData(-1, RefillRefusal.Expired)]
    [Trait("AC", "AC2")]
    public void Request_ExpiryEdge_TheExpiryDayIsValid(int expiresOffset, RefillRefusal expected) =>
        Setup(expiresOn: Day.AddDays(expiresOffset)).Desk.Request(Patient, 1).Refusal.Should().Be(expected);

    [Theory]
    [InlineData(1, RefillRefusal.None)]
    [InlineData(0, RefillRefusal.NoRefillsLeft)]
    [Trait("AC", "AC2")]
    public void Request_RefillsLeftEdge_NoneLeftIsRefused(int left, RefillRefusal expected) =>
        Setup(refillsLeft: left).Desk.Request(Patient, 1).Refusal.Should().Be(expected);

    [Theory]
    [InlineData(7, RefillRefusal.None)]          // the window's first day
    [InlineData(8, RefillRefusal.TooEarly)]
    [InlineData(0, RefillRefusal.None)]
    [Trait("AC", "AC3")]
    public void Request_WindowEdge_SevenDaysBeforeRunningOutIsAllowed(int daysBeforeRunningOut, RefillRefusal expected) =>
        Setup(runsOut: Day.AddDays(daysBeforeRunningOut)).Desk.Request(Patient, 1).Refusal.Should().Be(expected);

    [Theory]
    [InlineData(RefillStatus.Requested)]
    [InlineData(RefillStatus.PendingApproval)]
    [Trait("AC", "AC4")]
    public void Request_WhileOneIsOpen_IsRefused(RefillStatus open)
    {
        var (desk, store, _) = Setup(controlled: open == RefillStatus.PendingApproval);
        desk.Request(Patient, 1).Request!.Status.Should().Be(open);

        desk.Request(Patient, 1).Refusal.Should().Be(RefillRefusal.AlreadyOpen);
        store.RequestsFor(1).Should().ContainSingle();
    }

    [Fact]
    [Trait("AC", "AC4")]
    public void Request_AfterTheOpenOneIsCancelled_IsAccepted()
    {
        var (desk, _, _) = Setup();
        var first = desk.Request(Patient, 1).Request!;
        desk.Move(first.Id, RefillStatus.Cancelled).Should().BeTrue();

        desk.Request(Patient, 1).Ok.Should().BeTrue();
    }

    [Fact]
    [Trait("AC", "AC4")]
    public async Task Request_TwoAtTheSameMoment_OnlyOneOpens()
    {
        for (var round = 0; round < 200; round++)
        {
            var (desk, store, _) = Setup();
            using var start = new Barrier(8);
            var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() => { start.SignalAndWait(); return desk.Request(Patient, 1); }));
            var results = await Task.WhenAll(tasks);

            results.Count(r => r.Ok).Should().Be(1, $"round {round}");
            store.RequestsFor(1).Should().ContainSingle();
        }
    }

    [Theory]
    [InlineData(Patient)]
    [InlineData(Caregiver)]
    [Trait("AC", "AC5")]
    public void Request_ControlledMedicine_WaitsForApprovalWhoeverAsks(int requester)
    {
        var (desk, _, _) = Setup(controlled: true, delegation: new Delegation(Caregiver, Patient, Day, Day));

        desk.Request(requester, 1).Request!.Status.Should().Be(RefillStatus.PendingApproval);
    }

    [Fact]
    [Trait("AC", "AC5")]
    public void Request_OrdinaryMedicine_StartsRequested() =>
        Setup().Desk.Request(Patient, 1).Request!.Status.Should().Be(RefillStatus.Requested);

    public static TheoryData<RefillStatus, RefillStatus, bool> EveryMove()
    {
        var allowed = new[]
        {
            (RefillStatus.Requested, RefillStatus.Approved), (RefillStatus.Requested, RefillStatus.Cancelled),
            (RefillStatus.PendingApproval, RefillStatus.Approved), (RefillStatus.PendingApproval, RefillStatus.Rejected),
            (RefillStatus.PendingApproval, RefillStatus.Cancelled), (RefillStatus.Approved, RefillStatus.Dispensed),
        };
        var data = new TheoryData<RefillStatus, RefillStatus, bool>();
        foreach (var from in Enum.GetValues<RefillStatus>())
            foreach (var to in Enum.GetValues<RefillStatus>())
                data.Add(from, to, allowed.Contains((from, to)));
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryMove))]
    [Trait("AC", "AC6")]
    public void Move_EveryPairOfStatuses_OnlyTheStoryMovesAreAllowed(RefillStatus from, RefillStatus to, bool expected)
    {
        var (desk, _, _) = Setup(controlled: from == RefillStatus.PendingApproval);
        var request = desk.Request(Patient, 1).Request!;
        // Walk the request to `from` along allowed moves.
        var path = from switch
        {
            RefillStatus.Approved => new[] { RefillStatus.Approved },
            RefillStatus.Dispensed => [RefillStatus.Approved, RefillStatus.Dispensed],
            RefillStatus.Cancelled => [RefillStatus.Cancelled],
            RefillStatus.Rejected => [RefillStatus.Rejected],
            _ => [],
        };
        if (from == RefillStatus.Rejected)
        {
            (desk, _, _) = Setup(controlled: true);
            request = desk.Request(Patient, 1).Request!;
        }
        foreach (var step in path)
            desk.Move(request.Id, step).Should().BeTrue();
        request.Status.Should().Be(from);

        desk.Move(request.Id, to).Should().Be(expected);
    }

    [Fact]
    [Trait("AC", "AC7")]
    public void Move_RefillsGoDownOnDispensingOnly()
    {
        var (desk, store, _) = Setup(refillsLeft: 3);
        var request = desk.Request(Patient, 1).Request!;
        store.FindPrescription(1)!.RefillsLeft.Should().Be(3);

        desk.Move(request.Id, RefillStatus.Approved).Should().BeTrue();
        store.FindPrescription(1)!.RefillsLeft.Should().Be(3);

        desk.Move(request.Id, RefillStatus.Dispensed).Should().BeTrue();
        store.FindPrescription(1)!.RefillsLeft.Should().Be(2);
    }

    [Fact]
    [Trait("AC", "AC8")]
    public void Request_JustAfterMidnightInRiyadh_CountsAsTheNewDay()
    {
        // 00:30 on 13 November in Riyadh is 21:30 on the 12th in UTC. Supply runs out on the 20th, so the
        // window opens on the 13th: at the pharmacy it is open, in UTC it would still be too early.
        var justAfterMidnight = new DateTimeOffset(2026, 11, 13, 0, 30, 0, Desk.PharmacyOffset);

        Setup(now: justAfterMidnight, runsOut: new DateOnly(2026, 11, 20)).Desk.Request(Patient, 1).Ok.Should().BeTrue();
    }
}
