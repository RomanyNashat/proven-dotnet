namespace HomeReadings.Application;

/// <summary>The app's request body. The value is text, as the meter shows it.</summary>
public sealed record NewReading(string? NationalId, string? Value, string? Unit, DateTime CollectedAt, string? Notes);
