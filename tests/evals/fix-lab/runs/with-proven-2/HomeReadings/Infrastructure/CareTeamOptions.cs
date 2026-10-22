using System.ComponentModel.DataAnnotations;

namespace HomeReadings.Infrastructure;

public sealed class CareTeamOptions
{
    public const string Section = "CareTeam";

    [Required]
    public Uri? BaseAddress { get; set; }
}
