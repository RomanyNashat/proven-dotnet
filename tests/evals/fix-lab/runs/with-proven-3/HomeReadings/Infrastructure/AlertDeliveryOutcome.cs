namespace HomeReadings.Infrastructure;

public enum AlertDeliveryOutcome
{
    /// <summary>The care team accepted it.</summary>
    Delivered,

    /// <summary>It certainly didn't arrive (no connection, open circuit, a 5xx, 408 or 429): safe to send again.</summary>
    Failed,

    /// <summary>The care team refused it (another 4xx): sending it again won't help.</summary>
    Rejected,

    /// <summary>It may have arrived (timeout, connection lost after sending): sending again could page a nurse twice.</summary>
    Unknown,
}
