using LabResults.Application;

namespace LabResults.Api;

/// <summary>Checks a <see cref="LookupRequest"/> before it is forwarded to the lab network.</summary>
public static class LookupRequestValidator
{
    /// <summary>Returns the errors keyed by property name; empty when the request is valid.</summary>
    /// <param name="request">The request body.</param>
    /// <returns>The validation errors.</returns>
    public static Dictionary<string, string[]> Validate(LookupRequest request) =>
        Identifiers.IsNationalId(request.NationalId)
            ? []
            : new() { [nameof(request.NationalId)] = ["National id must be 10 digits."] };
}
