using System.Text.RegularExpressions;
using LabResults.Domain;

namespace LabResults.Api;

public static partial class Validation
{
    public static Dictionary<string, string[]> Validate(RecordResultRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request is null)
        {
            errors["body"] = ["A request body is required."];
            return errors;
        }

        if (request.PatientId <= 0)
        {
            errors["patientId"] = ["A patient id is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Test) || request.Test.Length > LabResult.TestMaxLength)
        {
            errors["test"] = [$"The test is required and at most {LabResult.TestMaxLength} characters."];
        }

        if (string.IsNullOrWhiteSpace(request.Unit) || request.Unit.Length > LabResult.UnitMaxLength)
        {
            errors["unit"] = [$"The unit is required and at most {LabResult.UnitMaxLength} characters."];
        }

        if (Math.Abs(decimal.Round(request.Value, LabResult.ValueScale, MidpointRounding.AwayFromZero)) >= LabResult.ValueLimit)
        {
            errors["value"] = ["The value is out of range."];
        }

        if (request.Notes is { Length: > LabResult.NotesMaxLength })
        {
            errors["notes"] = [$"Notes are at most {LabResult.NotesMaxLength} characters."];
        }

        if (!IsPhone(request.Phone))
        {
            errors["phone"] = ["The phone number must be in international format, e.g. +966512345678."];
        }

        return errors;
    }

    /// <summary>E.164: a plus sign and up to 15 digits.</summary>
    public static bool IsPhone(string? value) => value is not null && Phone().IsMatch(value);

    /// <summary>
    /// The national ID goes into the lab network's URL path, so only plain identifiers are accepted
    /// (no '/', '.', '%' or anything else that could change which lab endpoint is called).
    /// </summary>
    public static bool IsNationalId(string? value) => value is not null && NationalIdPattern().IsMatch(value);

    [GeneratedRegex(@"^\+[1-9][0-9]{7,14}$", RegexOptions.CultureInvariant)]
    private static partial Regex Phone();

    [GeneratedRegex("^[0-9A-Za-z]{4,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex NationalIdPattern();
}
