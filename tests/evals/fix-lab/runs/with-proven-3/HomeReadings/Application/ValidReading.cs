using HomeReadings.Domain;

namespace HomeReadings.Application;

/// <summary>A request that passed validation, in the shapes the domain stores.</summary>
public sealed record ValidReading(string NationalId, MeterValue Meter, string Unit, DateTime CollectedAtUtc, string? Notes);
