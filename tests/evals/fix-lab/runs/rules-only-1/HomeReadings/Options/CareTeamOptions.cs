using System.ComponentModel.DataAnnotations;

namespace HomeReadings.Options;

public sealed class CareTeamOptions
{
    public const string Section = "CareTeam";

    [Required]
    public Uri? BaseAddress { get; set; }
}
