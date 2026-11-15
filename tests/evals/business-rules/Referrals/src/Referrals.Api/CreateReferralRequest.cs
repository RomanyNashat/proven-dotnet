using System.ComponentModel.DataAnnotations;

namespace Referrals.Api;

public sealed record CreateReferralRequest
{
    [Required]
    public required int PatientId { get; init; }

    [Required]
    public required int ClinicId { get; init; }

    [Required, MaxLength(500)]
    public required string Reason { get; init; }

    public string? Note { get; init; }
}
