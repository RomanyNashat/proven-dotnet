namespace MeterSync.Domain;

public enum AlertState
{
    /// <summary>The reading is in range.</summary>
    NotNeeded,

    /// <summary>Out of range and not texted yet.</summary>
    Pending,

    Sent,

    /// <summary>Out of range, but too old to be worth a text by the time it was seen or could be sent.</summary>
    Expired,
}
