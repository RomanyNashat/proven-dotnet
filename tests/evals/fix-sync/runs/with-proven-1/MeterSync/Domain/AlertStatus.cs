namespace MeterSync.Domain;

/// <summary>Where a reading is in the alert flow. Only <see cref="Pending"/> readings are texted.</summary>
public enum AlertStatus
{
    /// <summary>In range: nothing to send.</summary>
    NotNeeded,

    /// <summary>Out of range and not sent yet.</summary>
    Pending,

    Sent,

    /// <summary>Out of range, but taken before the meter was linked to this patient (history, or a previous owner).</summary>
    BeforeLink,

    /// <summary>Every attempt failed; needs a person to look at it.</summary>
    GaveUp,
}
