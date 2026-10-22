using System.ComponentModel.DataAnnotations;

namespace HomeReadings.Infrastructure;

public sealed class CareTeamOptions
{
    public const string Section = "CareTeam";

    [Required]
    public Uri? BaseAddress { get; set; }
}

public sealed class StorageOptions
{
    public const string Section = "Storage";

    [Required]
    public string PhotoRoot { get; set; } = "";
}

public sealed class AuthOptions
{
    public const string Section = "Auth";

    [Required]
    public string Authority { get; set; } = "";
}
