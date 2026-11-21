using FluentAssertions;
using static RefillDesk.Tests.TestWorld;

namespace RefillDesk.Tests;

/// <summary>Delegates to an in-memory store but pauses on reads, widening any check-then-act race in the desk.</summary>
internal sealed class SlowStore(InMemoryRefillStore inner) : IRefillStore
{
    private static void Pause() => Thread.Sleep(15);
    public Prescription? FindPrescription(int id) { var p = inner.FindPrescription(id); Pause(); return p; }
    public RefillRequest? FindRequest(int id) { var r = inner.FindRequest(id); Pause(); return r; }
    public IReadOnlyList<RefillRequest> RequestsFor(int prescriptionId) { var r = inner.RequestsFor(prescriptionId); Pause(); return r; }
    public IReadOnlyList<Delegation> DelegationsOf(int caregiverId) => inner.DelegationsOf(caregiverId);
    public RefillRequest AddRequest(int prescriptionId, int requestedBy, RefillStatus status) => inner.AddRequest(prescriptionId, requestedBy, status);
    public void Save(Prescription prescription) => inner.Save(prescription);
}

public sealed class ConcurrencyTests
{
    private const int Threads = 8;

    private static T[] RunTogether<T>(Func<T> action)
    {
        using var start = new Barrier(Threads);
        var tasks = Enumerable.Range(0, Threads).Select(_ => Task.Factory.StartNew(() =>
        {
            start.SignalAndWait();
            return action();
        }, TaskCreationOptions.LongRunning)).ToArray();
        Task.WaitAll(tasks);
        return [.. tasks.Select(t => t.Result)];
    }

    [Fact]
    public void ConcurrentRequests_OnlyOneIsAccepted()
    {
        var inner = new InMemoryRefillStore();
        var w = new TestWorld(store: new SlowStore(inner));
        inner.Add(Rx());

        var results = RunTogether(() => w.Desk.Request(Patient, RxId));

        results.Count(r => r.Ok).Should().Be(1);
        results.Where(r => !r.Ok).Should().OnlyContain(r => r.Refusal == RefillRefusal.AlreadyOpen);
        inner.RequestsFor(RxId).Should().ContainSingle();
    }

    [Fact]
    public void ConcurrentDispenses_OfOneRequest_UseOneRefill()
    {
        var inner = new InMemoryRefillStore();
        var w = new TestWorld(store: new SlowStore(inner));
        inner.Add(Rx(refillsLeft: 3));
        var r = w.Desk.Request(Patient, RxId).Request!;
        w.Desk.Move(r.Id, RefillStatus.Approved).Should().BeTrue();

        var results = RunTogether(() => w.Desk.Move(r.Id, RefillStatus.Dispensed));

        results.Count(ok => ok).Should().Be(1);
        inner.FindPrescription(RxId)!.RefillsLeft.Should().Be(2);
    }

    [Fact]
    public void ConcurrentDispensesOfDifferentRequests_EachUseARefill()
    {
        var inner = new InMemoryRefillStore();
        var w = new TestWorld(store: new SlowStore(inner));
        inner.Add(Rx(refillsLeft: 20));
        // Approved requests don't block new ones, so several can be approved at once.
        var ids = new List<int>();
        for (var i = 0; i < Threads; i++)
        {
            var r = w.Desk.Request(Patient, RxId).Request!;
            w.Desk.Move(r.Id, RefillStatus.Approved).Should().BeTrue();
            ids.Add(r.Id);
        }
        var next = 0;

        var results = RunTogether(() => w.Desk.Move(ids[Interlocked.Increment(ref next) - 1], RefillStatus.Dispensed));

        results.Should().OnlyContain(ok => ok);
        inner.FindPrescription(RxId)!.RefillsLeft.Should().Be(20 - Threads);
    }

    [Fact]
    public void ConcurrentApproveAndCancel_ExactlyOneWins()
    {
        var inner = new InMemoryRefillStore();
        var w = new TestWorld(store: new SlowStore(inner));
        inner.Add(Rx());
        var r = w.Desk.Request(Patient, RxId).Request!;
        var n = 0;

        var results = RunTogether(() =>
            w.Desk.Move(r.Id, Interlocked.Increment(ref n) % 2 == 0 ? RefillStatus.Approved : RefillStatus.Cancelled));

        results.Count(ok => ok).Should().Be(1);
        r.Status.Should().BeOneOf(RefillStatus.Approved, RefillStatus.Cancelled);
    }
}
