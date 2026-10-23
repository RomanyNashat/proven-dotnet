namespace MeterSync.Domain;

/// <summary>Where a reading's SMS is.</summary>
public enum AlertDelivery
{
    /// <summary>In range, or taken before the meter was linked: no SMS.</summary>
    NotNeeded,

    /// <summary>Waiting for the sweeper, or for its next attempt.</summary>
    Pending,

    Sent,

    /// <summary>Gave up after <see cref="Reading.MaxAlertAttempts"/> attempts. Needs a person to look.</summary>
    Failed,
}
