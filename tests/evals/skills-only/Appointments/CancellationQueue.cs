using RabbitMQ.Client;

namespace Appointments;

/// <summary>Cancellations from the call centre arrive on RabbitMQ.</summary>
public static class CancellationQueue
{
    public const string Name = "appointments.cancellations";

    public static async Task DeclareAsync(IChannel channel, CancellationToken ct) =>
        await channel.QueueDeclareAsync(Name, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" }, cancellationToken: ct);
}
