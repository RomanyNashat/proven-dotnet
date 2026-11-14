using ClinicQueue.Application;
using FluentAssertions;

namespace ClinicQueue.Tests;

public sealed class QueueSnapshotSerializerTests
{
    [Fact]
    public void Deserialize_SerializedSnapshot_ReturnsSameSnapshot()
    {
        var snapshot = new QueueSnapshot(7, new DateOnly(2026, 11, 14), [1, 2, 3]);

        var roundTripped = QueueSnapshotSerializer.Deserialize(QueueSnapshotSerializer.Serialize(snapshot));

        roundTripped.Should().BeEquivalentTo(snapshot);
    }
}
