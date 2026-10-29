using System.ComponentModel.DataAnnotations;

namespace LabResults.Infrastructure;

/// <summary>The Keycloak realm that issues the tokens. Validated at start-up.</summary>
public sealed class AuthOptions
{
    public const string Section = "Auth";
    public const string Audience = "lab-results";

    [Required]
    public required string Authority { get; init; }
}
