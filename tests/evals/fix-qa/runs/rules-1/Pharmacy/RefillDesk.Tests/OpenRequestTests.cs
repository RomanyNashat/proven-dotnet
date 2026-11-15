using FluentAssertions;
using static RefillDesk.Tests.TestPharmacy;

namespace RefillDesk.Tests;

/// <summary>AC4: at most one open (Requested or PendingApproval) request per prescription, even under a race.</summary>
public sealed class OpenRequestTests
{
    [Theory]
    [InlineData(RefillStatus.Requested)]
    [InlineData(RefillStatus.PendingApproval)]
    public void Request_WhileAnotherIsOpen_IsRefusedAsAlreadyOpen(RefillStatus open)
    {
        var pharmacy = new TestPharmacy();
        pharmacy.RequestIn(open);

        var result = pharmacy.Request();

        result.Should().BeEquivalentTo(new { Ok = false, Refusal = RefillRefusal.AlreadyOpen, Request = (RefillRequest?)null });
    }

    [Theory]
    [InlineData(RefillStatus.Requested)]
    [InlineData(RefillStatus.PendingApproval)]
    public void Request_CaregiverWhilePatientRequestIsOpen_IsRefusedAsAlreadyOpen(RefillStatus open)
    {
        var pharmacy = new TestPharmacy();
        pharmacy.RequestIn(open);
        pharmacy.Delegate(CaregiverId, PatientId, DefaultDay.AddDays(-30), DefaultDay.AddDays(30));

        var result = pharmacy.Request(CaregiverId);

        result.Refusal.Should().Be(RefillRefusal.AlreadyOpen);
    }

    [Theory]
    [InlineData(RefillStatus.Approved)]
    [InlineData(RefillStatus.Rejected)]
    [InlineData(RefillStatus.Cancelled)]
    [InlineData(RefillStatus.Dispensed)]
    public void Request_WhenEarlierRequestIsClosed_IsAccepted(RefillStatus closed)
    {
        var pharmacy = new TestPharmacy();
        pharmacy.RequestIn(closed);

        var result = pharmacy.Request();

        result.Refusal.Should().Be(RefillRefusal.None);
    }

    [Fact]
    public void Request_OpenRequestOnAnotherPrescription_DoesNotBlock()
    {
        var pharmacy = new TestPharmacy();
        pharmacy.Add(Rx(id: 1));
        pharmacy.Add(Rx(id: 2));
        pharmacy.Request(prescriptionId: 1).Ok.Should().BeTrue();

        var result = pharmacy.Request(prescriptionId: 2);

        result.Refusal.Should().Be(RefillRefusal.None);
    }

    [Fact]
    public void Request_AfterCancellingTheOpenOne_IsAccepted()
    {
        var pharmacy = new TestPharmacy();
        var first = pharmacy.RequestIn(RefillStatus.Requested);
        pharmacy.Desk.Move(first.Id, RefillStatus.Cancelled);

        var second = pharmacy.Request();

        second.Request!.Id.Should().NotBe(first.Id);
    }

    [Fact]
    public void Request_ManyArrivingAtTheSameMoment_ExactlyOneIsAccepted()
    {
        // The store pauses inside the open-request check so that, without mutual exclusion around
        // check-then-add, every caller would see "no open request" and all would be accepted.
        var slow = new SlowCheckStore();
        var pharmacy = new TestPharmacy(slow);
        slow.Inner = pharmacy.Memory;
        pharmacy.Add(Rx());
        pharmacy.Delegate(CaregiverId, PatientId, DefaultDay.AddDays(-30), DefaultDay.AddDays(30));

        const int callers = 4;
        var results = new RefillResult[callers];
        using var start = new Barrier(callers);
        var threads = Enumerable.Range(0, callers).Select(i => new Thread(() =>
        {
            start.SignalAndWait();
            results[i] = pharmacy.Request(i % 2 == 0 ? PatientId : CaregiverId);
        })).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        results.Count(r => r.Ok).Should().Be(1);
        results.Where(r => !r.Ok).Should().AllSatisfy(r => r.Refusal.Should().Be(RefillRefusal.AlreadyOpen));
        pharmacy.Memory.RequestsFor(1).Should().ContainSingle();
    }

    private sealed class SlowCheckStore : IRefillStore
    {
        private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(150);
        private readonly ManualResetEventSlim _never = new(false);

        public InMemoryRefillStore Inner { get; set; } = null!;

        public Prescription? FindPrescription(int id) => Inner.FindPrescription(id);

        public RefillRequest? FindRequest(int id) => Inner.FindRequest(id);

        public IReadOnlyList<RefillRequest> RequestsFor(int prescriptionId)
        {
            var snapshot = Inner.RequestsFor(prescriptionId);
            _never.Wait(Pause);
            return snapshot;
        }

        public IReadOnlyList<Delegation> DelegationsOf(int caregiverId) => Inner.DelegationsOf(caregiverId);

        public RefillRequest AddRequest(int prescriptionId, int requestedBy, RefillStatus status) =>
            Inner.AddRequest(prescriptionId, requestedBy, status);

        public void Save(Prescription prescription) => Inner.Save(prescription);
    }
}
