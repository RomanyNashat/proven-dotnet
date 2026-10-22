namespace HomeReadings.Api;

/// <summary>The answer to POST /readings: <c>{ id, message }</c>.</summary>
public sealed record SavedReading(int Id, string Message);
