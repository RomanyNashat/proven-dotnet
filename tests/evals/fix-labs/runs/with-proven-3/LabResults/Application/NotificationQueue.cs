using System.Threading.Channels;

namespace LabResults.Application;

/// <summary>
/// In-process only: anything still queued is lost if the pod is killed (see the outbox note in the review).
/// </summary>
public sealed class NotificationQueue
{
    private readonly Channel<Notification> _channel = Channel.CreateBounded<Notification>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.Wait });

    /// <summary>The worker's end of the queue.</summary>
    public ChannelReader<Notification> Reader => _channel.Reader;

    /// <summary>
    /// Queues without waiting. Called after the result is saved, so it must not block: a POST that hangs
    /// on a full queue gets retried by the client and stores the result twice.
    /// </summary>
    /// <param name="notification">The SMS.</param>
    /// <returns><c>false</c> when the queue is full or shutting down.</returns>
    public bool TryEnqueue(Notification notification) => _channel.Writer.TryWrite(notification);

    /// <summary>Refuses new items; the worker sends what is left, then stops.</summary>
    public void Complete() => _channel.Writer.TryComplete();
}
