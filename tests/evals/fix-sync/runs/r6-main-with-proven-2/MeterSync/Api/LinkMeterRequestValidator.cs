using System.Text.RegularExpressions;

namespace MeterSync.Api;

public static partial class LinkMeterRequestValidator
{
    public const int NationalIdMaxLength = 10;

    /// <returns>Errors by property name; empty when the request is valid.</returns>
    public static Dictionary<string, string[]> Validate(LinkMeterRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.NationalId) || request.NationalId.Length > NationalIdMaxLength)
        {
            errors[nameof(LinkMeterRequest.NationalId)] = [$"A national id of up to {NationalIdMaxLength} characters is required."];
        }

        // The serial goes into the vendor's URL path, so only a safe set of characters is accepted.
        if (request.SerialNumber is null || !SerialNumber().IsMatch(request.SerialNumber))
        {
            errors[nameof(LinkMeterRequest.SerialNumber)] =
                ["A serial number of 1 to 40 letters, digits, '.', '_' or '-' is required, starting with a letter or digit."];
        }

        return errors;
    }

    // \z, not $: in .NET "$" also matches before a final newline, which would let "ABC\n" through.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,39}\z")]
    private static partial Regex SerialNumber();
}
