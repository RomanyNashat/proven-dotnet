using Microsoft.Extensions.Hosting;

namespace SkillSamples.Kubernetes;

/// <summary>A worker that needs <paramref name="drainTime"/> after SIGTERM to finish its in-flight batch.</summary>
public sealed class DrainingWorker(TimeSpan drainTime) : BackgroundService
{
    private readonly TaskCompletionSource _running = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Running => _running.Task;

    public bool Drained { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _running.TrySetResult();
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // SIGTERM: stop taking new work, finish the batch in hand
        }

        await Task.Delay(drainTime, CancellationToken.None);
        Drained = true;
    }
}
