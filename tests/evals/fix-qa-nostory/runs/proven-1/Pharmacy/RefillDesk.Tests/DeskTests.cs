using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace RefillDesk.Tests;

/// <summary>The original smoke test, kept as it was.</summary>
public sealed class DeskTests
{
    [Fact]
    [Trait("AC", "AC8")]
    public void Request_OwnPrescriptionInTheWindow_IsAccepted()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
        var store = new InMemoryRefillStore();
        store.Add(new Prescription(1, PatientId: 10, "Metformin", Controlled: false, RefillsLeft: 3,
            ExpiresOn: new DateOnly(2027, 6, 1), LastDispensedOn: new DateOnly(2026, 10, 25), DaysSupply: 30));

        var result = new Desk(store, time).Request(requesterId: 10, prescriptionId: 1);

        result.Ok.Should().BeTrue();
    }
}
