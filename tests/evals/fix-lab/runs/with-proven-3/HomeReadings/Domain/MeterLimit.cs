namespace HomeReadings.Domain;

/// <summary>The meter showed HI or LO: the reading was outside what it can measure, so there is no number.</summary>
public enum MeterLimit
{
    /// <summary>Above the meter's range ("HI"). Always urgent.</summary>
    High,

    /// <summary>Below the meter's range ("LO").</summary>
    Low,
}
