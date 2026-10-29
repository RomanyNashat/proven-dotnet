using System.ComponentModel.DataAnnotations;

namespace LabResults.Infrastructure;

/// <summary><c>Auth:Authority</c>: the Keycloak realm URL.</summary>
public sealed class AuthOptions
{
    public const string Section = "Auth";
    public const string Audience = "lab-results";

    [Required]
    public string? Authority { get; init; }
}
