using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace LabResults.Application;

/// <summary>A Saudi national ID or iqama: 10 digits starting with 1 or 2.</summary>
public static partial class NationalId
{
    /// <summary>Accepts Western or Arabic-Indic digits and returns the ID in Western digits.</summary>
    /// <param name="value">What the caller sent.</param>
    /// <param name="normalized">The ID in Western digits, when valid.</param>
    /// <returns>Whether the value is a national ID.</returns>
    public static bool TryNormalize(string? value, [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var western = Digits.ToWestern(value.Trim());
        if (!Shape().IsMatch(western))
        {
            return false;
        }

        normalized = western;
        return true;
    }

    /// <summary>The normalized ID of a value already validated.</summary>
    /// <param name="value">A value that passed <see cref="TryNormalize"/>.</param>
    /// <returns>The ID in Western digits.</returns>
    /// <exception cref="ArgumentException">The value is not a national ID.</exception>
    public static string Normalize(string value) =>
        TryNormalize(value, out var normalized) ? normalized : throw new ArgumentException("Not a national ID.", nameof(value));

    // [0-9], not \d: \d also matches other scripts' digits, which the lab's API would not understand.
    [GeneratedRegex("^[12][0-9]{9}$")]
    private static partial Regex Shape();
}
