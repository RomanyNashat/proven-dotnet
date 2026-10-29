using System.Text.RegularExpressions;

namespace LabResults.Application;

/// <summary>Formats of the patient identifiers the service accepts.</summary>
public static partial class Identifiers
{
    /// <summary>A national id is exactly 10 ASCII digits; it becomes a path segment on the lab API, so nothing else may pass.</summary>
    /// <param name="value">The candidate value.</param>
    /// <returns><c>true</c> when the value is a national id.</returns>
    public static bool IsNationalId(string? value) => value is not null && NationalIdPattern().IsMatch(value);

    /// <summary>The SMS gateway takes E.164 numbers ("+9665...").</summary>
    /// <param name="value">The candidate value.</param>
    /// <returns><c>true</c> when the value is an E.164 phone number.</returns>
    public static bool IsE164Phone(string? value) => value is not null && E164Pattern().IsMatch(value);

    [GeneratedRegex(@"\A[0-9]{10}\z", RegexOptions.CultureInvariant)]
    private static partial Regex NationalIdPattern();

    [GeneratedRegex(@"\A\+[1-9][0-9]{7,14}\z", RegexOptions.CultureInvariant)]
    private static partial Regex E164Pattern();
}
