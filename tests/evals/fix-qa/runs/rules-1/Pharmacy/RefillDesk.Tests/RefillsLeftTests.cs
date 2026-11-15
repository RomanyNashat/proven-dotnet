using FluentAssertions;
using static RefillDesk.Tests.TestPharmacy;

namespace RefillDesk.Tests;

/// <summary>AC7: refills left go down by one on Dispensed, and at no other time.</summary>
public sealed class RefillsLeftTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(1)]
    public void Move_ToDispensed_LowersRefillsLeftByOne(int refillsLeft)
    {
        var pharmacy = new TestPharmacy();
        pharmacy.Add(Rx(refillsLeft: refillsLeft));
        var request = pharmacy.Request().Request!;
        pharmacy.Desk.Move(request.Id, RefillStatus.Approved);

        pharmacy.Desk.Move(request.Id, RefillStatus.Dispensed);

        pharmacy.RefillsLeft().Should().Be(refillsLeft - 1);
    }

    [Fact]
    public void Move_ToDispensed_RecordsTodayAsLastDispensingDateAndChangesNothingElse()
    {
        var pharmacy = new TestPharmacy();
        var original = pharmacy.Add(Rx(refillsLeft: 3));
        var request = pharmacy.Request().Request!;
        pharmacy.Desk.Move(request.Id, RefillStatus.Approved);

        pharmacy.Desk.Move(request.Id, RefillStatus.Dispensed);

        pharmacy.Memory.FindPrescription(1).Should().Be(original with { RefillsLeft = 2, LastDispensedOn = DefaultDay });
    }

    [Fact]
    public void Move_ToDispensed_StartsANewSupplyForTheWindowRule()
    {
        var pharmacy = new TestPharmacy();
        pharmacy.Add(Rx(refillsLeft: 3, daysSupply: 30));
        var request = pharmacy.Request().Request!;
        pharmacy.Desk.Move(request.Id, RefillStatus.Approved);
        pharmacy.Desk.Move(request.Id, RefillStatus.Dispensed);

        var refusals = new[]
        {
            pharmacy.Request().Refusal,
            Advance(pharmacy, DefaultDay.AddDays(22)).Request().Refusal,
            Advance(pharmacy, DefaultDay.AddDays(23)).Request().Refusal,
        };

        refusals.Should().Equal(RefillRefusal.TooEarly, RefillRefusal.TooEarly, RefillRefusal.None);
    }

    [Fact]
    public void Move_DispenseLastRefill_LaterRequestIsRefusedAsNoRefillsLeft()
    {
        var pharmacy = new TestPharmacy();
        pharmacy.RequestIn(RefillStatus.Approved);
        pharmacy.Memory.Save(pharmacy.Memory.FindPrescription(1)! with { RefillsLeft = 1 });
        pharmacy.Desk.Move(1, RefillStatus.Dispensed);

        var result = pharmacy.Request();

        result.Refusal.Should().Be(RefillRefusal.NoRefillsLeft);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Request_Accepted_DoesNotChangeRefillsLeft(bool controlled)
    {
        var pharmacy = new TestPharmacy();
        pharmacy.Add(Rx(refillsLeft: 3, controlled: controlled));

        pharmacy.Request();

        pharmacy.RefillsLeft().Should().Be(3);
    }

    [Fact]
    public void Request_Refused_DoesNotChangeRefillsLeft()
    {
        var pharmacy = new TestPharmacy();
        pharmacy.Add(Rx(refillsLeft: 3));
        pharmacy.Request();

        pharmacy.Request().Refusal.Should().Be(RefillRefusal.AlreadyOpen);

        pharmacy.RefillsLeft().Should().Be(3);
    }

    [Theory]
    [InlineData(RefillStatus.Requested, RefillStatus.Approved)]
    [InlineData(RefillStatus.Requested, RefillStatus.Cancelled)]
    [InlineData(RefillStatus.PendingApproval, RefillStatus.Approved)]
    [InlineData(RefillStatus.PendingApproval, RefillStatus.Rejected)]
    [InlineData(RefillStatus.PendingApproval, RefillStatus.Cancelled)]
    public void Move_AllowedMoveOtherThanDispense_DoesNotChangeRefillsLeft(RefillStatus from, RefillStatus to)
    {
        var pharmacy = new TestPharmacy();
        pharmacy.RequestIn(from);
        var before = pharmacy.RefillsLeft();

        pharmacy.Desk.Move(1, to);

        pharmacy.RefillsLeft().Should().Be(before);
    }

    [Theory]
    [InlineData(RefillStatus.Requested)] // skipping approval
    [InlineData(RefillStatus.PendingApproval)] // skipping approval
    [InlineData(RefillStatus.Rejected)]
    [InlineData(RefillStatus.Cancelled)]
    [InlineData(RefillStatus.Dispensed)] // dispensing twice
    public void Move_RefusedDispense_DoesNotChangeRefillsLeft(RefillStatus from)
    {
        var pharmacy = new TestPharmacy();
        pharmacy.RequestIn(from);
        var before = pharmacy.RefillsLeft();

        pharmacy.Desk.Move(1, RefillStatus.Dispensed).Should().BeFalse();

        pharmacy.RefillsLeft().Should().Be(before);
    }

    [Fact]
    public void Move_DispenseOnOnePrescription_DoesNotChangeAnother()
    {
        var pharmacy = new TestPharmacy();
        pharmacy.Add(Rx(id: 1, refillsLeft: 3));
        pharmacy.Add(Rx(id: 2, refillsLeft: 3));
        var request = pharmacy.Request(prescriptionId: 1).Request!;
        pharmacy.Desk.Move(request.Id, RefillStatus.Approved);

        pharmacy.Desk.Move(request.Id, RefillStatus.Dispensed);

        pharmacy.RefillsLeft(2).Should().Be(3);
    }

    private static TestPharmacy Advance(TestPharmacy pharmacy, DateOnly day)
    {
        pharmacy.SetRiyadhDay(day);
        return pharmacy;
    }
}
