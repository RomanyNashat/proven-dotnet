using System.Text.RegularExpressions;

namespace LabResults.Application;

/// <summary>A Saudi national ID (starts with 1) or iqama (starts with 2): ten ASCII digits.</summary>
public static partial class NationalIdFormat
{
    /// <summary>Whether <paramref name="value"/> is a well-formed national ID or iqama number.</summary>
    /// <param name="value">The value from the request.</param>
    /// <returns><c>true</c> only for exactly ten ASCII digits starting with 1 or 2.</returns>
    public static bool IsValid(string? value) => value is not null && Pattern().IsMatch(value);

    // [0-9], not \d: \d also matches Arabic-Indic digits, which the lab's URL path must never receive.
    [GeneratedRegex("^[12][0-9]{9}$")]
    private static partial Regex Pattern();
}
