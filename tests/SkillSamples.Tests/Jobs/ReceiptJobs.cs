using Hangfire;

namespace SkillSamples.Jobs;

// Hangfire stores the job as "call SendAsync on IReceiptSender". Filters such as [AutomaticRetry] and
// [Queue] are read from that type, so they go on the interface method, not on the class.
public interface IReceiptSender
{
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 120, 600])]
    Task SendAsync(int orderId, CancellationToken ct);
}

public sealed class OrderJobs(IBackgroundJobClient jobs)
{
    // CancellationToken.None is a placeholder: Hangfire passes its own token, cancelled on shutdown.
    public string QueueReceipt(int orderId) =>
        jobs.Enqueue<IReceiptSender>("default", sender => sender.SendAsync(orderId, CancellationToken.None));
}
