namespace HomeReadings.Infrastructure;

/// <summary>The <c>CareTeam</c> settings.</summary>
public sealed class CareTeamOptions
{
    public string BaseAddress { get; set; } = "";

    /// <summary>The base address with a trailing slash, so "alerts" is added to its path rather than replacing the last segment.</summary>
    public Uri BaseUri => new(BaseAddress.EndsWith('/') ? BaseAddress : BaseAddress + "/");
}
