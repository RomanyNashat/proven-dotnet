using System.Text.Json;

namespace ClinicQueue.Application;

/// <summary>
/// The cache format of a <see cref="QueueSnapshot"/>. JSON replaces BinaryFormatter, which always throws
/// from .NET 9 on and was a remote-code-execution risk before that.
/// </summary>
public static class QueueSnapshotCodec
{
    /// <summary>Writes a snapshot as UTF-8 JSON.</summary>
    /// <param name="snapshot">The snapshot to write.</param>
    /// <returns>The bytes to cache.</returns>
    public static byte[] Serialize(QueueSnapshot snapshot) => JsonSerializer.SerializeToUtf8Bytes(snapshot);

    /// <summary>Reads a snapshot written by <see cref="Serialize"/>.</summary>
    /// <param name="bytes">The cached bytes.</param>
    /// <returns>The snapshot.</returns>
    /// <exception cref="JsonException">The bytes are not a snapshot.</exception>
    public static QueueSnapshot Deserialize(byte[] bytes) =>
        JsonSerializer.Deserialize<QueueSnapshot>(bytes) ?? throw new JsonException("The cached queue snapshot is empty.");
}
