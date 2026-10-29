namespace LabResults.Application;

/// <summary>What may appear in a log line about a patient.</summary>
public static class Masking
{
    private const int Visible = 4;

    /// <summary>"1012345678" becomes "******5678". Values of eight characters or fewer are masked completely.</summary>
    /// <remarks>Masks by length rather than by pattern, so a mistyped or spaced ID is never logged in full.</remarks>
    public static string NationalId(string? value) =>
        value is null || value.Length <= 2 * Visible
            ? "****"
            : string.Concat(new string('*', value.Length - Visible), value.AsSpan(value.Length - Visible));
}
