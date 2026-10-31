using System.Threading.Channels;

namespace Clinic.Api;

public sealed class TextWorker(Channel<string> queue, SmsClient sms) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var text in queue.Reader.ReadAllAsync(stoppingToken))
        {
            await sms.SendAsync(text, stoppingToken);
        }
    }

    // Drain before cancelling: base.StopAsync cancels stoppingToken at once.
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Writer.TryComplete();
        if (ExecuteTask is { } running)
        {
            await Task.WhenAny(running, Task.Delay(Timeout.Infinite, cancellationToken));
        }

        await base.StopAsync(cancellationToken);
    }
}
