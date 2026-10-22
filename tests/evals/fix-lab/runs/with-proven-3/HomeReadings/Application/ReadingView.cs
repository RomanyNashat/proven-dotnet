namespace HomeReadings.Application;

/// <summary>A reading as the app shows it.</summary>
/// <param name="Value">The number the meter showed; null when it showed HI or LO.</param>
/// <param name="OutOfRange">"HI" or "LO" when the meter couldn't measure; otherwise null.</param>
public sealed record ReadingView(int Id, decimal? Value, string Unit, DateTime CollectedAt, string? Notes, string? OutOfRange);
