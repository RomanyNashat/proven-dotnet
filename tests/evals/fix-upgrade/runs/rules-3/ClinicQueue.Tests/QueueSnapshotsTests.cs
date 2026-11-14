using ClinicQueue.Application;
using FluentAssertions;

namespace ClinicQueue.Tests;

public sealed class QueueSnapshotsTests
{
    [Fact]
    public void Deserialize_SerializedSnapshot_RoundTrips()
    {
        var snapshot = new QueueSnapshot(7, new DateOnly(2026, 11, 14), [1, 2, 3]);

        var restored = QueueSnapshots.Deserialize(QueueSnapshots.Serialize(snapshot));

        restored.Should().BeEquivalentTo(snapshot);
    }
}
