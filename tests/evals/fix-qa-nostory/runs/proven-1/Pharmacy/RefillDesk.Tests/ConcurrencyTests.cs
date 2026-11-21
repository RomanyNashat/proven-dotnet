using FluentAssertions;
using static RefillDesk.Tests.DeskFixture;

namespace RefillDesk.Tests;

[Trait("AC", "AC12")]
public sealed class ConcurrencyTests
{
    private const int Threads = 32;

    /// <summary>Runs <paramref name="work"/> on <paramref name="count"/> dedicated threads released together.</summary>
    private static T[] AllAtOnce<T>(int count, Func<int, T> work)
    {
        using var start = new Barrier(count);
        var results = new T[count];
        var threads = Enumerable.Range(0, count).Select(i => new Thread(() =>
        {
            start.SignalAndWait();
            results[i] = work(i);
        })).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());
        return results;
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 2)]
    [Trait("EC", "EC48")]
    public void Request_ManyAtOnceOnOnePrescription_ExactlyOneIsAccepted(bool controlled, int readDelayMs)
    {
        var f = new DeskFixture();
        f.Store.ReadDelay = TimeSpan.FromMilliseconds(readDelayMs);
        f.Add(Rx(controlled: controlled));
        f.Delegate(CaregiverId, PatientId, Today, Today);

        var results = AllAtOnce(Threads, i => f.Desk.Request(i % 2 == 0 ? PatientId : CaregiverId, 1));

        results.Count(r => r.Ok).Should().Be(1);
        results.Where(r => !r.Ok).Should().OnlyContain(r => r.Refusal == RefillRefusal.AlreadyOpen && r.Request == null);
        f.Inner.RequestsFor(1).Should().ContainSingle()
            .Which.Should().BeSameAs(results.Single(r => r.Ok).Request);
        f.Store.Adds.Should().Be(1);
    }

    [Fact]
    [Trait("EC", "EC49")]
    public void Move_ManyDispensesOfOneRequestAtOnce_OneSucceedsAndOneRefillIsTaken()
    {
        var f = new DeskFixture();
        f.Store.ReadDelay = TimeSpan.FromMilliseconds(1);
        f.Add(Rx(refillsLeft: 10));
        var request = f.Seed(1, RefillStatus.Approved);

        var results = AllAtOnce(Threads, _ => f.Desk.Move(request.Id, RefillStatus.Dispensed));

        results.Count(moved => moved).Should().Be(1);
        f.Inner.FindPrescription(1)!.RefillsLeft.Should().Be(9);
        f.Store.Saves.Should().Be(1);
    }

    [Fact]
    [Trait("EC", "EC50")]
    public void Move_DispensesOfDifferentRequestsAtOnce_LoseNoDecrement()
    {
        var f = new DeskFixture();
        f.Store.ReadDelay = TimeSpan.FromMilliseconds(1);
        f.Add(Rx(refillsLeft: 100));
        var requests = Enumerable.Range(0, Threads).Select(_ => f.Seed(1, RefillStatus.Approved)).ToArray();

        var results = AllAtOnce(Threads, i => f.Desk.Move(requests[i].Id, RefillStatus.Dispensed));

        results.Should().OnlyContain(moved => moved);
        f.Inner.FindPrescription(1)!.RefillsLeft.Should().Be(100 - Threads);
    }
}
