using FluentAssertions;

namespace RefillDesk.Tests;

/// <summary>AC6: the allowed moves, and that every other move is refused and changes nothing.</summary>
public sealed class StatusMoveTests
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

    public static TheoryData<RefillStatus, RefillStatus> AllowedMoves()
    {
        var data = new TheoryData<RefillStatus, RefillStatus>();
        foreach (var (from, to) in Allowed)
            data.Add(from, to);
        return data;
    }

    public static TheoryData<RefillStatus, RefillStatus> ForbiddenMoves()
    {
        var data = new TheoryData<RefillStatus, RefillStatus>();
        foreach (var from in Enum.GetValues<RefillStatus>())
        foreach (var to in Enum.GetValues<RefillStatus>())
        {
            if (!Allowed.Contains((from, to)))
                data.Add(from, to);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllowedMoves))]
    public void Move_AllowedMove_ReturnsTrue(RefillStatus from, RefillStatus to)
    {
        var pharmacy = new TestPharmacy();
        var request = pharmacy.RequestIn(from);

        var moved = pharmacy.Desk.Move(request.Id, to);

        moved.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(AllowedMoves))]
    public void Move_AllowedMove_SetsNewStatus(RefillStatus from, RefillStatus to)
    {
        var pharmacy = new TestPharmacy();
        var request = pharmacy.RequestIn(from);

        pharmacy.Desk.Move(request.Id, to);

        pharmacy.StatusOf(request.Id).Should().Be(to);
    }

    [Theory]
    [MemberData(nameof(ForbiddenMoves))]
    public void Move_ForbiddenMove_ReturnsFalse(RefillStatus from, RefillStatus to)
    {
        var pharmacy = new TestPharmacy();
        var request = pharmacy.RequestIn(from);

        var moved = pharmacy.Desk.Move(request.Id, to);

        moved.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(ForbiddenMoves))]
    public void Move_ForbiddenMove_LeavesStatusUnchanged(RefillStatus from, RefillStatus to)
    {
        var pharmacy = new TestPharmacy();
        var request = pharmacy.RequestIn(from);

        pharmacy.Desk.Move(request.Id, to);

        pharmacy.StatusOf(request.Id).Should().Be(from);
    }

    [Fact]
    public void Move_UnknownRequest_ReturnsFalse()
    {
        var pharmacy = new TestPharmacy();

        var moved = pharmacy.Desk.Move(404, RefillStatus.Approved);

        moved.Should().BeFalse();
    }

    [Fact]
    public void Move_OneRequest_DoesNotChangeAnotherRequest()
    {
        var pharmacy = new TestPharmacy();
        pharmacy.Add(TestPharmacy.Rx(id: 1));
        pharmacy.Add(TestPharmacy.Rx(id: 2));
        var first = pharmacy.Request(prescriptionId: 1).Request!;
        var second = pharmacy.Request(prescriptionId: 2).Request!;

        pharmacy.Desk.Move(first.Id, RefillStatus.Approved);

        pharmacy.StatusOf(second.Id).Should().Be(RefillStatus.Requested);
    }
}
