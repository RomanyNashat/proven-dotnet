using System.Text.RegularExpressions;

namespace MeterSync.Api;

/// <summary>Body of <c>POST /meters</c>.</summary>
public sealed partial record LinkMeterRequest(string NationalId, string SerialNumber)
{
    /// <summary>Checks the request before it reaches the database or the vendor URL.</summary>
    /// <returns>Errors by property name; empty when the request is valid.</returns>
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (NationalId is null || !NationalIdPattern().IsMatch(NationalId))
        {
            errors[nameof(NationalId)] = ["A national id of 10 digits is required."];
        }

        if (SerialNumber is null || !SerialNumberPattern().IsMatch(SerialNumber))
        {
            errors[nameof(SerialNumber)] = ["A serial number of 1 to 40 letters, digits, '-' or '_' is required."];
        }

        return errors;
    }

    // [0-9] not \d: \d also matches Arabic-Indic digits. \z not $: $ also matches before a final "\n".
    [GeneratedRegex(@"^[0-9]{10}\z", RegexOptions.CultureInvariant)]
    private static partial Regex NationalIdPattern();

    // The serial becomes a path segment of the vendor URL, so no '.', '/' or '%'.
    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,40}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SerialNumberPattern();
}
