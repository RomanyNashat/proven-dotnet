namespace MeterSync.Domain;

/// <summary>Whether the patient still has to be texted about a reading.</summary>
public enum AlertDelivery
{
    /// <summary>In range, or out of range but taken long before the meter was linked (its history).</summary>
    NotNeeded,

    /// <summary>Waiting for the sweeper.</summary>
    Pending,

    Sent,

    /// <summary>Moved aside after repeated failures, so it no longer blocks the alerts behind it.</summary>
    Failed,
}
