using System.Text.Json;

namespace ClinicQueue.Application;

/// <summary>
/// The cache format for <see cref="QueueSnapshot"/>. JSON replaces BinaryFormatter, which .NET 9 removed
/// (it threw on every call) and which deserialized arbitrary types from the cache.
/// </summary>
public static class QueueSnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Writes a snapshot as UTF-8 JSON.</summary>
    /// <param name="snapshot">The snapshot to cache.</param>
    /// <returns>The bytes to store.</returns>
    public static byte[] Serialize(QueueSnapshot snapshot) => JsonSerializer.SerializeToUtf8Bytes(snapshot, Options);

    /// <summary>Reads a snapshot written by <see cref="Serialize"/>.</summary>
    /// <param name="bytes">The cached bytes.</param>
    /// <returns>The snapshot.</returns>
    /// <exception cref="JsonException">The bytes are not a snapshot.</exception>
    public static QueueSnapshot Deserialize(byte[] bytes) =>
        JsonSerializer.Deserialize<QueueSnapshot>(bytes, Options) ?? throw new JsonException("The cached queue snapshot is empty.");
}
