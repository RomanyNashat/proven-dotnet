namespace ClinicBooking.Infrastructure;

// Retry settings for the Kafka producer, read in Program.cs.
public static class RetryStrategy
{
    public const int MaxAttempts = 3;
    public static readonly TimeSpan Backoff = TimeSpan.FromMilliseconds(200);
}
