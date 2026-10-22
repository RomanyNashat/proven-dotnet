using HomeReadings.Domain;

namespace HomeReadings.Application;

/// <summary>Checks a <see cref="NewReading"/> before anything reaches the database. Limits match the columns.</summary>
public static class NewReadingValidator
{
    public const int NationalIdLength = 10;
    public const int NotesMaxLength = 500;

    /// <summary>Validates the request.</summary>
    /// <param name="request">The request body.</param>
    /// <param name="reading">The validated reading, or null when there are errors.</param>
    /// <returns>Errors by field (camelCase, as the app sent them); empty when valid.</returns>
    public static IReadOnlyDictionary<string, string[]> Validate(NewReading request, out ValidReading? reading)
    {
        var errors = new Dictionary<string, string[]>();

        var nationalId = AsciiDigits(request.NationalId?.Trim());
        if (nationalId.Length != NationalIdLength || !nationalId.All(char.IsAsciiDigit))
        {
            errors["nationalId"] = [$"Must be {NationalIdLength} digits."];
        }

        if (!MeterValue.TryParse(request.Value, out var meter))
        {
            errors["value"] = ["Must be the number the meter shows, or HI or LO."];
        }

        if (!GlucoseUnit.TryNormalize(request.Unit, out var unit))
        {
            errors["unit"] = [$"Must be {GlucoseUnit.MmolPerLitre} or {GlucoseUnit.MgPerDecilitre}."];
        }

        if (request.CollectedAt == default)
        {
            errors["collectedAt"] = ["Is required."];
        }

        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (notes?.Length > NotesMaxLength)
        {
            errors["notes"] = [$"Must be at most {NotesMaxLength} characters."];
        }

        reading = errors.Count == 0
            ? new ValidReading(nationalId, meter, unit, RiyadhTime.ToUtc(request.CollectedAt), notes)
            : null;
        return errors;
    }

    private static string AsciiDigits(string? value) =>
        value is null ? "" : new string([.. value.Select(c => c switch
        {
            >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
            >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
            _ => c,
        })]);
}
