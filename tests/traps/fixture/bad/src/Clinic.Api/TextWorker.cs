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
}
