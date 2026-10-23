using System.ComponentModel.DataAnnotations;

namespace MeterSync.Api;

/// <summary>The JWT settings the service can't start without.</summary>
public sealed class AuthSettings
{
    public const string Audience = "meter-sync";

    [Required]
    public string? Authority { get; init; }
}
