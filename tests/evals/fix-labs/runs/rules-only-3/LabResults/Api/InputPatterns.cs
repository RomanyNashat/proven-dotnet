using System.Text.RegularExpressions;

namespace LabResults.Api;

/// <summary>Formats shared by the request validators.</summary>
public static partial class InputPatterns
{
    /// <summary>
    /// Ten ASCII digits. <c>[0-9]</c> rather than <c>\d</c>, which also matches Arabic-Indic digits; and the
    /// strict shape keeps the value safe to put in the lab network's URL path (no "..", no "/").
    /// </summary>
    [GeneratedRegex("^[0-9]{10}$")]
    public static partial Regex NationalId();

    /// <summary>E.164, which is what the SMS gateway takes.</summary>
    [GeneratedRegex(@"^\+[1-9][0-9]{7,14}$")]
    public static partial Regex Phone();
}
