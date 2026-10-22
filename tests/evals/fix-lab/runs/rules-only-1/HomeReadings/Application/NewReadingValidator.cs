using HomeReadings.Domain;
using HomeReadings.Infrastructure;

namespace HomeReadings.Application;

public sealed record NewReading(string NationalId, string Value, string Unit, DateTime CollectedAt, string? Notes);

/// <summary>A request that passed validation, in the shape the service stores.</summary>
public sealed record ValidReading(string NationalId, decimal Value, string Unit, DateTime CollectedAtUtc, string? Notes);

public static class NewReadingValidator
{
    /// <summary>Returns the reading to store, or the errors to send back as a 400.</summary>
    public static (ValidReading? Reading, Dictionary<string, string[]> Errors) Validate(NewReading? request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request is null)
        {
            errors["body"] = ["A reading is required."];
            return (null, errors);
        }

        var nationalId = request.NationalId?.Trim();
        if (nationalId is null || nationalId.Length != 10 || !nationalId.All(char.IsAsciiDigit))
        {
            errors["nationalId"] = ["Must be a 10-digit national ID or iqama number."];
        }

        var unit = GlucoseUnit.Normalize(request.Unit);
        if (unit is null)
        {
            errors["unit"] = [$"Must be {GlucoseUnit.MmolPerLitre} or {GlucoseUnit.MgPerDecilitre}."];
        }

        decimal value = 0;
        if (!MeterValue.TryParse(request.Value, out value))
        {
            errors["value"] = ["Must be the number the meter shows."];
        }
        else if (unit is not null && !InRange(value, unit))
        {
            errors["value"] = ["Is outside the range a glucose meter can show."];
        }

        if (request.CollectedAt == default)
        {
            errors["collectedAt"] = ["Is required."];
        }

        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (notes is { Length: > ReadingsDbContext.NotesMaxLength })
        {
            errors["notes"] = [$"Must be {ReadingsDbContext.NotesMaxLength} characters or fewer."];
        }

        return errors.Count > 0
            ? (null, errors)
            : (new ValidReading(nationalId!, Math.Round(value, 2, MidpointRounding.AwayFromZero), unit!, Riyadh.ToUtc(request.CollectedAt), notes), errors);
    }

    // Wider than any meter's display range (about 1.1-33.3 mmol/L, 20-600 mg/dL), narrow enough to catch
    // a value typed in the wrong unit or a slip of the finger.
    private static bool InRange(decimal value, string unit) => unit == GlucoseUnit.MgPerDecilitre
        ? value is > 0 and <= 900
        : value is > 0 and <= 50;
}
