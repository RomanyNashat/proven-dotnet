using MeterSync.Domain;

namespace MeterSync.Api;

public static class LinkMeterValidation
{
    public const int NationalIdLength = 10;

    /// <summary>Checks a link request before it reaches the database.</summary>
    /// <param name="request">The request body.</param>
    /// <returns>The errors by field, or null when the request is valid.</returns>
    public static Dictionary<string, string[]>? Errors(LinkMeterRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        // Ten ASCII digits, as stored. (The body binds null for a missing field despite the annotation.)
        if (request.NationalId is not { Length: NationalIdLength } nationalId || !nationalId.All(char.IsAsciiDigit))
        {
            errors[nameof(request.NationalId)] = [$"A national ID of {NationalIdLength} digits is required."];
        }

        if (string.IsNullOrWhiteSpace(request.SerialNumber) || request.SerialNumber.Length > Meter.SerialNumberMaxLength)
        {
            errors[nameof(request.SerialNumber)] = [$"A serial number of up to {Meter.SerialNumberMaxLength} characters is required."];
        }

        return errors.Count == 0 ? null : errors;
    }
}
