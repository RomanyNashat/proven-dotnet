using System.Text.RegularExpressions;

namespace MeterSync.Api;

public static partial class LinkMeterRequestValidator
{
    /// <summary>Checks a link request before anything reads the database or the vendor.</summary>
    /// <param name="request">The request body.</param>
    /// <returns>Errors by field; empty when the request is valid.</returns>
    public static IReadOnlyDictionary<string, string[]> Validate(LinkMeterRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.NationalId is null || !NationalId().IsMatch(request.NationalId))
        {
            errors[nameof(LinkMeterRequest.NationalId)] = ["A national id of up to 10 digits is required."];
        }

        // The serial number goes into the vendor's URL path, so '/', '..' and escapes are never allowed.
        if (request.SerialNumber is null || !SerialNumber().IsMatch(request.SerialNumber))
        {
            errors[nameof(LinkMeterRequest.SerialNumber)] =
                ["A serial number of up to 40 letters, digits, '.', '_' or '-' is required, starting with a letter or digit."];
        }

        return errors;
    }

    // \z, not $: in .NET '$' also matches before a trailing newline.
    [GeneratedRegex(@"^[0-9]{1,10}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex NationalId();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,39}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex SerialNumber();
}
