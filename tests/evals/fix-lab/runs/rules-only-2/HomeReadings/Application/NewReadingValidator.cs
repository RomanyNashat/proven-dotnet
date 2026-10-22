using HomeReadings.Domain;
using HomeReadings.Infrastructure;

namespace HomeReadings.Application;

public sealed record NewReading(string NationalId, string Value, string Unit, DateTime CollectedAt, string? Notes);

/// <summary>A <see cref="NewReading"/> that passed validation, with its text parsed and its time in UTC.</summary>
public sealed record ValidReading(string NationalId, decimal Value, string Unit, DateTime CollectedAtUtc, string? Notes);

public static class NewReadingValidator
{
    /// <summary>How far ahead of the server clock a meter's clock may run before a reading is refused.</summary>
    private static readonly TimeSpan AllowedClockDrift = TimeSpan.FromMinutes(10);

    public static ValidReading? Validate(NewReading? request, DateTime nowUtc, out Dictionary<string, string[]> errors)
    {
        errors = [];
        if (request is null)
        {
            errors["body"] = ["A reading is required."];
            return null;
        }

        var nationalId = MeterText.ToAsciiDigits(request.NationalId?.Trim() ?? "");
        if (!IsNationalId(nationalId))
        {
            errors["nationalId"] = ["Must be a 10-digit national ID or iqama number starting with 1 or 2."];
        }

        var hasUnit = GlucoseUnits.TryNormalize(request.Unit, out var unit);
        if (!hasUnit)
        {
            errors["unit"] = [$"Must be {GlucoseUnits.MmolPerL} or {GlucoseUnits.MgPerDl}."];
        }

        if (!MeterText.TryParseValue(request.Value, out var value) || value <= 0
            || (hasUnit && value > GlucoseUnits.MaxValue(unit)))
        {
            errors["value"] = ["Must be the number the meter shows, such as 7.5, with at most two decimals."];
        }

        DateTime collectedAt = default;
        if (request.CollectedAt == default)
        {
            errors["collectedAt"] = ["Is required."];
        }
        else
        {
            collectedAt = PatientTime.ToUtc(request.CollectedAt);
            if (collectedAt > nowUtc + AllowedClockDrift)
            {
                errors["collectedAt"] = ["Cannot be in the future."];
            }
        }

        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (notes is { Length: > ReadingsDbContext.NotesMaxLength })
        {
            errors["notes"] = [$"Must be at most {ReadingsDbContext.NotesMaxLength} characters."];
        }

        return errors.Count == 0 ? new ValidReading(nationalId, value, unit, collectedAt, notes) : null;
    }

    private static bool IsNationalId(string value) =>
        value.Length == 10 && value[0] is '1' or '2' && value.All(char.IsAsciiDigit);
}
