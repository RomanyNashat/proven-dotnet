using ClinicQueue.Application;

namespace ClinicQueue.Tests;

public sealed class QueueSnapshotCodecTests
{
    [Fact]
    public void Deserialize_SerializedSnapshot_ReturnsSameValues()
    {
        var snapshot = new QueueSnapshot(7, new DateOnly(2026, 11, 14), [1, 2, 3]);

        var read = QueueSnapshotCodec.Deserialize(QueueSnapshotCodec.Serialize(snapshot));

        read.Should().BeEquivalentTo(snapshot);
    }
}
