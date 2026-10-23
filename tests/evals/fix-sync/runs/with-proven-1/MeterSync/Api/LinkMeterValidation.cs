using System.Text.RegularExpressions;

namespace MeterSync.Api;

public static partial class LinkMeterValidation
{
    /// <summary>The errors for a ValidationProblem, or null when the request is well formed.</summary>
    public static Dictionary<string, string[]>? Validate(LinkMeterRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        // The serial goes into the vendor's URL path. Uri.EscapeDataString leaves "." and ".." alone, and
        // "meters/../readings" reaches another vendor path, so only letters, digits, '-' and '_'.
        if (request.SerialNumber is null || !Serial().IsMatch(request.SerialNumber))
        {
            errors[nameof(LinkMeterRequest.SerialNumber)] = ["A serial number of up to 40 letters, digits, '-' or '_' is required."];
        }

        if (request.NationalId is null || !NationalId().IsMatch(request.NationalId))
        {
            errors[nameof(LinkMeterRequest.NationalId)] = ["A national ID of 10 digits is required."];
        }

        return errors.Count == 0 ? null : errors;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,40}$")]
    private static partial Regex Serial();

    // [0-9], not \d: \d also matches Arabic-Indic digits, which the column and the lookup don't expect.
    [GeneratedRegex("^[0-9]{10}$")]
    private static partial Regex NationalId();
}
