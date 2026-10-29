using System.Threading.Channels;

namespace LabResults.Application;

public sealed class NotificationQueue
{
    private readonly Channel<Notification> _channel = Channel.CreateBounded<Notification>(1000);

    public ChannelReader<Notification> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(Notification notification, CancellationToken ct) => _channel.Writer.WriteAsync(notification, ct);
}
