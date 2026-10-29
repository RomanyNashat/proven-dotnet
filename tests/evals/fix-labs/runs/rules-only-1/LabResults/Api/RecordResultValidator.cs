using LabResults.Application;

namespace LabResults.Api;

/// <summary>Checks a <see cref="RecordResultRequest"/> before anything is stored or sent.</summary>
public static class RecordResultValidator
{
    public const int MaxTestLength = 40;
    public const int MaxUnitLength = 16;
    public const int MaxNotesLength = 1000;

    // The column is numeric(10,3): 7 digits before the point, 3 after.
    private const decimal MaxAbsoluteValue = 9_999_999.999m;

    /// <summary>Returns the errors keyed by property name; empty when the request is valid.</summary>
    /// <param name="request">The request body.</param>
    /// <returns>The validation errors.</returns>
    public static Dictionary<string, string[]> Validate(RecordResultRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.PatientId <= 0)
        {
            errors[nameof(request.PatientId)] = ["Patient id must be positive."];
        }

        if (!Identifiers.IsNationalId(request.NationalId))
        {
            errors[nameof(request.NationalId)] = ["National id must be 10 digits."];
        }

        if (!Identifiers.IsE164Phone(request.Phone))
        {
            errors[nameof(request.Phone)] = ["Phone must be in international format, e.g. +966551234567."];
        }

        if (string.IsNullOrWhiteSpace(request.Test) || request.Test.Length > MaxTestLength)
        {
            errors[nameof(request.Test)] = [$"Test is required, at most {MaxTestLength} characters."];
        }

        if (string.IsNullOrWhiteSpace(request.Unit) || request.Unit.Length > MaxUnitLength)
        {
            errors[nameof(request.Unit)] = [$"Unit is required, at most {MaxUnitLength} characters."];
        }

        if (request.Notes?.Length > MaxNotesLength)
        {
            errors[nameof(request.Notes)] = [$"Notes are at most {MaxNotesLength} characters."];
        }

        if (Math.Abs(request.Value) > MaxAbsoluteValue || decimal.Round(request.Value, 3) != request.Value)
        {
            errors[nameof(request.Value)] = ["Value must fit 7 digits before the point and 3 after."];
        }

        return errors;
    }
}
