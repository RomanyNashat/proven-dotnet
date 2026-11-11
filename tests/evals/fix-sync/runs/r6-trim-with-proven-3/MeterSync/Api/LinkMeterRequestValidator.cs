namespace MeterSync.Api;

/// <summary>Checks a link request before it reaches the database.</summary>
public static class LinkMeterRequestValidator
{
    public const int NationalIdMaxLength = 10;
    public const int SerialNumberMaxLength = 40;

    /// <summary>Validates <paramref name="request"/>; the serial number is judged after trimming.</summary>
    /// <param name="request">The request body.</param>
    /// <returns>Errors by field; empty when the request is valid.</returns>
    public static IReadOnlyDictionary<string, string[]> Validate(LinkMeterRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.NationalId) || request.NationalId.Length > NationalIdMaxLength)
        {
            errors[nameof(request.NationalId)] = [$"A national id of up to {NationalIdMaxLength} characters is required."];
        }

        var serial = request.SerialNumber?.Trim();
        if (string.IsNullOrEmpty(serial) || serial.Length > SerialNumberMaxLength)
        {
            errors[nameof(request.SerialNumber)] = [$"A serial number of up to {SerialNumberMaxLength} characters is required."];
        }

        return errors;
    }
}
