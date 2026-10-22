namespace HomeReadings.Infrastructure;

public sealed class CareTeamOptions
{
    public const string Section = "CareTeam";

    /// <summary>Ends with "/", so "alerts" resolves under it.</summary>
    public string BaseAddress { get; set; } = "";
}
