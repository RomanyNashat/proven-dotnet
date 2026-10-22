namespace HomeReadings.Infrastructure;

/// <summary>What the care team's system receives. Values are in mmol/L; HI/LO readings have no value.</summary>
public sealed record CareTeamAlertMessage(string NationalId, decimal? Value, string Unit, string? OutOfRange, DateTime CollectedAt);
