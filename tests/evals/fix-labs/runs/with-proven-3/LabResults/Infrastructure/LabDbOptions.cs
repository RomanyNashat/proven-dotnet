using System.ComponentModel.DataAnnotations;

namespace LabResults.Infrastructure;

public sealed class LabDbOptions
{
    /// <summary><c>ConnectionStrings:Lab</c>.</summary>
    [Required(ErrorMessage = "ConnectionStrings:Lab is required.")]
    public string? ConnectionString { get; set; }
}
