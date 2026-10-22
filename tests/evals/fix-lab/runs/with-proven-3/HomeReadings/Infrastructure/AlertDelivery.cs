namespace HomeReadings.Infrastructure;

/// <summary>How a POST to the care team ended.</summary>
/// <param name="Reason">An HTTP status or an exception type, never a message.</param>
public sealed record AlertDelivery(AlertDeliveryOutcome Outcome, string? Reason);
