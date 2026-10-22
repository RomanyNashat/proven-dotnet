using HomeReadings.Domain;

namespace HomeReadings.Application;

/// <summary>The saved reading, or <c>null</c> when the national ID differs from the one on the patient's earlier readings.</summary>
public sealed record AddReadingResult(Reading? Reading)
{
    public static AddReadingResult NationalIdMismatch { get; } = new((Reading?)null);
}
