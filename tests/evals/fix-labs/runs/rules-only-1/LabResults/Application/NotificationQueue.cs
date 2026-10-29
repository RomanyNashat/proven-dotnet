using System.Threading.Channels;

namespace LabResults.Application;

/// <summary>In-memory hand-off from request handlers to <see cref="NotificationWorker"/>.</summary>
public sealed class NotificationQueue(int capacity = 1000)
{
    private readonly Channel<Notification> _channel = Channel.CreateBounded<Notification>(
        new BoundedChannelOptions(capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    public ChannelReader<Notification> Reader => _channel.Reader;

    /// <summary>
    /// Queues without waiting. The result is already saved when this runs, so a backed-up SMS gateway
    /// must not hold the clinician's request open.
    /// </summary>
    /// <param name="notification">The SMS to send.</param>
    /// <returns><c>false</c> when the queue is full and the SMS was not queued.</returns>
    public bool TryEnqueue(Notification notification) => _channel.Writer.TryWrite(notification);
}
