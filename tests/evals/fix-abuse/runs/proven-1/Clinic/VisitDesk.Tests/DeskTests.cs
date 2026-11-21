using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace VisitDesk.Tests;

public sealed class DeskTests
{
    [Fact]
    public void FreeSlots_OneSlotTomorrow_IsListed()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
        var store = new InMemoryVisitStore();
        store.Add(new Slot(1, ClinicId: 7, StartsAt: time.GetUtcNow().AddDays(1), Price: 300m));

        var slots = new Desk(store, time).FreeSlots(clinicId: 7);

        slots.Should().ContainSingle(s => s.Id == 1);
    }
}
